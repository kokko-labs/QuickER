namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>
/// 名前付きクエリ条件（ミニ DSL）の型整合検査。列の生成型 × 演算子 × オペランドの組み合わせが
/// コンパイル可能な C# 式になるかを、エミッタが出力する形（<see cref="QueryConditionCSharpEmitter"/>）の
/// 鏡として静的に判定する。
/// </summary>
/// <remarks>
/// <para>
/// <b>判定の第一条件は「現在コンパイルも翻訳も通っている形を新たに拒否しない」</b>。違反として報告するのは
/// 「エミットされた C# が必ずコンパイルエラーになる組み合わせ」（と、Q2 の持ち上げが実行時
/// <c>InvalidCastException</c> になる NULL 許容値型列 × 型違いリストの 1 種）だけで、判定できない型
/// （未知の CLR 型名）は違反にしない（寛容側＝方言マッパーの将来の型追加で正当なクエリを誤って弾かない）。
/// </para>
/// <para>
/// 具体的には次を鏡写しにする: 順序比較演算子の有無（数値・日時系のみ。VO は基底の派生規則
/// <see cref="CSharpGenerationModelBuilder"/> の <c>ValueObjectHasOrderingOperators</c> が正）・
/// 数値リテラルの型サフィックス（<c>long</c> 列の小数リテラルは <c>1.5L</c> となり字句エラー）・
/// VO の <c>Create(値)</c> の暗黙変換（整数内包型への小数リテラルは不可）・C# の暗黙数値変換表
/// （<c>decimal</c> と <c>double</c>/<c>float</c> は混在不可・<c>ulong</c> と符号付き整数は比較不可）・
/// 文字列一致メソッドの引数型（string か同一 VO）・IN の <c>Contains</c> 型推論。
/// </para>
/// </remarks>
internal static class QueryConditionTypeChecker
{
    /// <summary>パラメータ 1 件の型情報（生成メソッドの引数型と同じ解決結果）</summary>
    /// <param name="TypeName">生成コード上の型名（VO 列参照なら VO クラス名・それ以外は CLR 型名）</param>
    /// <param name="ValueObjectClassName">VO 列参照で型付けされたときの VO クラス名（それ以外は null）</param>
    /// <param name="IsList">リストパラメータか（IN 用。宣言型は IReadOnlyList&lt;TypeName&gt;）</param>
    internal sealed record ParameterTypeInfo(
        string TypeName,
        string? ValueObjectClassName,
        bool IsList
    );

    /// <summary>違反の種別（メッセージの resx キーの選択に使う）</summary>
    internal enum ViolationKind
    {
        /// <summary>順序比較演算子（&lt; &lt;= &gt; &gt;=）を順序を持たない型の列に適用した</summary>
        OrderedOperatorUnsupported,

        /// <summary>文字列一致（LIKE / CONTAINS / STARTSWITH / ENDSWITH）を文字列でない列に適用した</summary>
        StringMatchUnsupported,

        /// <summary>列とオペランド（リテラル・パラメータ）の型が両立しない</summary>
        OperandTypeMismatch,
    }

    /// <summary>違反 1 件（列名・列の生成型表記・詳細＝演算子またはオペランドの表記）</summary>
    internal sealed record Violation(
        ViolationKind Kind,
        string ColumnName,
        string ColumnTypeText,
        string Detail
    );

    /// <summary>型ファミリ（互換判定の粒度。値は CLR 型名から分類する）</summary>
    private enum TypeFamily
    {
        Integer,
        Decimal,
        Floating,
        Text,
        Boolean,
        DateTime,
        DateTimeOffset,
        DateOnly,
        TimeOnly,
        TimeSpan,
        Guid,
        Binary,
        Unknown,
    }

    /// <summary>条件式全体を検査し、違反の一覧を返す（違反なしは空リスト）</summary>
    /// <param name="root">検証済み（列・パラメータ解決済み）の構文木</param>
    /// <param name="columns">列 ID → 列バインディング（エミッタと同じもの）</param>
    /// <param name="parameters">パラメータ名 → 型情報（生成メソッドの引数型の解決結果）</param>
    internal static List<Violation> Check(
        ConditionNode root,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columns,
        IReadOnlyDictionary<string, ParameterTypeInfo> parameters
    )
    {
        var violations = new List<Violation>();
        Visit(root, columns, parameters, violations);
        return violations;
    }

    /// <summary>ノードを再帰的に検査する</summary>
    private static void Visit(
        ConditionNode node,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columns,
        IReadOnlyDictionary<string, ParameterTypeInfo> parameters,
        List<Violation> violations
    )
    {
        switch (node)
        {
            case LogicalNode logical:
                Visit(logical.Left, columns, parameters, violations);
                Visit(logical.Right, columns, parameters, violations);
                break;

            case NotNode not:
                Visit(not.Operand, columns, parameters, violations);
                break;

            case ComparisonNode comparison:
                CheckComparison(comparison, columns, parameters, violations);
                break;

            case StringMatchNode match:
                CheckStringMatch(match, columns, parameters, violations);
                break;

            case InNode inNode:
                CheckIn(inNode, columns, parameters, violations);
                break;

            // NullCheckNode は型に依存しない（NULL 許容性はパーサ側の意味検証が担当）
        }
    }

    /// <summary>比較述語（列 演算子 オペランド）の型整合を検査する</summary>
    private static void CheckComparison(
        ComparisonNode node,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columns,
        IReadOnlyDictionary<string, ParameterTypeInfo> parameters,
        List<Violation> violations
    )
    {
        if (!TryBind(node.Column, columns, out var column))
        {
            return;
        }

        // 順序比較は列の生成型に比較演算子が要る（string == は可・string < は CS0019）
        var isOrdered =
            node.Operator
            is ComparisonOperator.Less
                or ComparisonOperator.LessOrEqual
                or ComparisonOperator.Greater
                or ComparisonOperator.GreaterOrEqual;

        if (isOrdered && !SupportsOrdering(column))
        {
            violations.Add(
                new Violation(
                    ViolationKind.OrderedOperatorUnsupported,
                    node.Column.ResolvedName ?? node.Column.Text,
                    ColumnTypeText(column),
                    OperatorText(node.Operator)
                )
            );
            return;
        }

        if (!OperandCompatible(column, node.Operand, parameters, out var operandText))
        {
            violations.Add(
                new Violation(
                    ViolationKind.OperandTypeMismatch,
                    node.Column.ResolvedName ?? node.Column.Text,
                    ColumnTypeText(column),
                    operandText
                )
            );
        }
    }

    /// <summary>文字列一致述語（LIKE / CONTAINS 系）の型整合を検査する</summary>
    private static void CheckStringMatch(
        StringMatchNode node,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columns,
        IReadOnlyDictionary<string, ParameterTypeInfo> parameters,
        List<Violation> violations
    )
    {
        if (!TryBind(node.Column, columns, out var column))
        {
            return;
        }

        // 列が文字列（string または内包値が string の VO）でなければ Contains 等のメソッドが存在しない
        var underlyingFamily = Classify(column.UnderlyingTypeName);

        if (underlyingFamily is not TypeFamily.Text and not TypeFamily.Unknown)
        {
            violations.Add(
                new Violation(
                    ViolationKind.StringMatchUnsupported,
                    node.Column.ResolvedName ?? node.Column.Text,
                    ColumnTypeText(column),
                    // Exact はワイルドカードなしの NOT LIKE 由来（診断には DSL の綴りで載せる）
                    node.Kind == StringMatchKind.Exact
                        ? "NOT LIKE"
                        : node.Kind.ToString().ToUpperInvariant()
                )
            );
            return;
        }

        // オペランドは文字列リテラル・string 型パラメータ・（VO 列なら）同一 VO 型パラメータのみ
        if (node.Operand is ParameterOperand parameterOperand)
        {
            if (!TryResolveParameter(parameterOperand, parameters, out var parameter))
            {
                return;
            }

            var compatible = parameter.ValueObjectClassName is { } parameterVo
                ? parameterVo == column.ValueObjectClassName
                : parameter.TypeName == "string"
                    || Classify(parameter.TypeName) == TypeFamily.Unknown;

            if (!compatible)
            {
                violations.Add(
                    new Violation(
                        ViolationKind.OperandTypeMismatch,
                        node.Column.ResolvedName ?? node.Column.Text,
                        ColumnTypeText(column),
                        ParameterText(parameterOperand, parameter)
                    )
                );
            }
        }
    }

    /// <summary>IN 述語（列 IN @リスト）の型整合を検査する</summary>
    private static void CheckIn(
        InNode node,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columns,
        IReadOnlyDictionary<string, ParameterTypeInfo> parameters,
        List<Violation> violations
    )
    {
        if (
            !TryBind(node.Column, columns, out var column)
            || !TryResolveParameter(node.Parameter, parameters, out var element)
        )
        {
            return;
        }

        bool compatible;

        if (column.ValueObjectClassName is { } columnVo)
        {
            // VO 列: 同一 VO のリストは直接比較・素のリストは Create(要素) の暗黙変換が要る
            compatible = element.ValueObjectClassName is { } elementVo
                ? elementVo == columnVo
                : ImplicitlyConvertible(element.TypeName, column.UnderlyingTypeName);
        }
        else if (element.ValueObjectClassName is not null)
        {
            // 素の列に VO リストは比較できない（VO 有効の図では列も VO のためここへは来ない）
            compatible = false;
        }
        else if (
            Classify(column.UnderlyingTypeName) == TypeFamily.Unknown
            || Classify(element.TypeName) == TypeFamily.Unknown
        )
        {
            compatible = true;
        }
        else if (column.IsNullable && !column.IsUnderlyingReferenceType)
        {
            // NULL 許容の値型列は Cast<T?>() で持ち上げるため、要素型は列型と完全一致が必要
            // （Cast は参照/ボックス変換しか通さず、long リストを int? へ Cast すると実行時 InvalidCastException）
            compatible = element.TypeName == column.UnderlyingTypeName;
        }
        else
        {
            // Contains<TSource>(リスト, 列) の型推論: 列型 → 要素型の暗黙変換が要る
            compatible = ImplicitlyConvertible(column.UnderlyingTypeName, element.TypeName);
        }

        if (!compatible)
        {
            violations.Add(
                new Violation(
                    ViolationKind.OperandTypeMismatch,
                    node.Column.ResolvedName ?? node.Column.Text,
                    ColumnTypeText(column),
                    ParameterText(node.Parameter, element)
                )
            );
        }
    }

    /// <summary>比較オペランド（リテラル・パラメータ）と列の型整合を判定する（不一致なら表記を返す）</summary>
    private static bool OperandCompatible(
        QueryColumnBinding column,
        ConditionOperand operand,
        IReadOnlyDictionary<string, ParameterTypeInfo> parameters,
        out string operandText
    )
    {
        var underlyingFamily = Classify(column.UnderlyingTypeName);

        switch (operand)
        {
            case NumberOperand number:
                operandText = number.Literal;
                return NumberLiteralCompatible(column, underlyingFamily, number.Literal);

            case StringOperand text:
                operandText = $"'{Truncate(text.Value)}'";
                // 文字列リテラルは string（VO なら内包値 string）の列のみ（日時・bool 等は @パラメータで渡す）
                return underlyingFamily is TypeFamily.Text or TypeFamily.Unknown;

            case ParameterOperand parameterOperand:
                if (!TryResolveParameter(parameterOperand, parameters, out var parameter))
                {
                    operandText = string.Empty;
                    return true;
                }

                operandText = ParameterText(parameterOperand, parameter);

                if (column.ValueObjectClassName is { } columnVo)
                {
                    // VO 列: 同一 VO は直接比較・素のパラメータは Create(値) の暗黙変換が要る
                    return parameter.ValueObjectClassName is { } parameterVo
                        ? parameterVo == columnVo
                        : ImplicitlyConvertible(parameter.TypeName, column.UnderlyingTypeName);
                }

                if (parameter.ValueObjectClassName is not null)
                {
                    // 素の列と VO パラメータは比較できない
                    return false;
                }

                return Comparable(column.UnderlyingTypeName, parameter.TypeName);

            default:
                operandText = string.Empty;
                return true;
        }
    }

    /// <summary>数値リテラルと列の型整合（エミッタのサフィックス規則と VO Create の暗黙変換の鏡）</summary>
    private static bool NumberLiteralCompatible(
        QueryColumnBinding column,
        TypeFamily underlyingFamily,
        string literal
    )
    {
        // 小数点を含むか（エミッタはリテラルを原文のまま出し、列型に応じたサフィックスを付ける）
        var isFractional = literal.Contains('.');

        switch (underlyingFamily)
        {
            case TypeFamily.Integer when !isFractional:
                return true;

            case TypeFamily.Integer:
                // 小数リテラル × 整数列: VO は Create(1.5)（double → 整数の暗黙変換なし）で CS1503。
                // 素の long 列はサフィックス L が付いて "1.5L"＝字句エラー。それ以外の素の整数列は
                // サフィックスなし＝double 比較へ暗黙拡大されてコンパイル・翻訳とも通るため許容する
                return column.ValueObjectClassName is null && column.UnderlyingTypeName != "long";

            case TypeFamily.Decimal:
            case TypeFamily.Floating:
            case TypeFamily.Unknown:
                return true;

            default:
                // 日時・bool・文字列・Guid・バイナリの列に数値リテラルは比較できない
                return false;
        }
    }

    /// <summary>列の生成型に順序比較演算子があるか</summary>
    private static bool SupportsOrdering(QueryColumnBinding column)
    {
        if (column.ValueObjectClassName is not null)
        {
            // VO は基底クラスの派生規則が演算子の有無を決める（string/bool/byte[]/Guid 等は等価のみ）
            return CSharpGenerationModelBuilder.ValueObjectHasOrderingOperators(
                column.UnderlyingTypeName
            );
        }

        return Classify(column.UnderlyingTypeName)
            is TypeFamily.Integer
                or TypeFamily.Decimal
                or TypeFamily.Floating
                or TypeFamily.DateTime
                or TypeFamily.DateTimeOffset
                or TypeFamily.DateOnly
                or TypeFamily.TimeOnly
                or TypeFamily.TimeSpan
                or TypeFamily.Unknown;
    }

    /// <summary>2 つの CLR 型が比較演算の両辺として両立するか（C# の暗黙数値変換規則の鏡）</summary>
    private static bool Comparable(string left, string right)
    {
        if (left == right)
        {
            return true;
        }

        var leftFamily = Classify(left);
        var rightFamily = Classify(right);

        if (leftFamily == TypeFamily.Unknown || rightFamily == TypeFamily.Unknown)
        {
            return true;
        }

        // 数値どうし: decimal と float/double は混在不可・ulong と符号付き整数は比較不可（いずれも CS0019）
        if (IsNumeric(leftFamily) && IsNumeric(rightFamily))
        {
            if (
                (leftFamily == TypeFamily.Decimal && rightFamily == TypeFamily.Floating)
                || (leftFamily == TypeFamily.Floating && rightFamily == TypeFamily.Decimal)
            )
            {
                return false;
            }

            return !IsUnsignedLongSignedMix(left, right);
        }

        // DateTime と DateTimeOffset は暗黙変換（DateTime → DateTimeOffset）で比較できる
        if (
            (leftFamily == TypeFamily.DateTime && rightFamily == TypeFamily.DateTimeOffset)
            || (leftFamily == TypeFamily.DateTimeOffset && rightFamily == TypeFamily.DateTime)
        )
        {
            return true;
        }

        return false;
    }

    /// <summary>ulong と符号付き整数の組か（C# の二項数値昇格が拒否する組）</summary>
    private static bool IsUnsignedLongSignedMix(string left, string right) =>
        (left == "ulong" && right is "sbyte" or "short" or "int" or "long")
        || (right == "ulong" && left is "sbyte" or "short" or "int" or "long");

    /// <summary>from 型の値を to 型へ暗黙変換できるか（同一型を含む。C# の暗黙数値変換表の鏡）</summary>
    private static bool ImplicitlyConvertible(string from, string to)
    {
        if (from == to)
        {
            return true;
        }

        if (Classify(from) == TypeFamily.Unknown || Classify(to) == TypeFamily.Unknown)
        {
            return true;
        }

        var targets = from switch
        {
            "sbyte" => (string[])["short", "int", "long", "float", "double", "decimal"],
            "byte" =>
            [
                "short",
                "ushort",
                "int",
                "uint",
                "long",
                "ulong",
                "float",
                "double",
                "decimal",
            ],
            "short" => ["int", "long", "float", "double", "decimal"],
            "ushort" => ["int", "uint", "long", "ulong", "float", "double", "decimal"],
            "int" => ["long", "float", "double", "decimal"],
            "uint" => ["long", "ulong", "float", "double", "decimal"],
            "long" => ["float", "double", "decimal"],
            "ulong" => ["float", "double", "decimal"],
            "float" => ["double"],
            "DateTime" => ["DateTimeOffset"],
            _ => [],
        };

        return targets.Contains(to);
    }

    /// <summary>CLR 型名を互換判定の型ファミリへ分類する（未知の型名は Unknown＝判定しない）</summary>
    private static TypeFamily Classify(string clrTypeName) =>
        clrTypeName switch
        {
            "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" =>
                TypeFamily.Integer,
            "decimal" => TypeFamily.Decimal,
            "float" or "double" => TypeFamily.Floating,
            "string" => TypeFamily.Text,
            "bool" => TypeFamily.Boolean,
            "DateTime" => TypeFamily.DateTime,
            "DateTimeOffset" => TypeFamily.DateTimeOffset,
            "DateOnly" => TypeFamily.DateOnly,
            "TimeOnly" => TypeFamily.TimeOnly,
            "TimeSpan" => TypeFamily.TimeSpan,
            "Guid" => TypeFamily.Guid,
            "byte[]" => TypeFamily.Binary,
            _ => TypeFamily.Unknown,
        };

    /// <summary>数値ファミリか</summary>
    private static bool IsNumeric(TypeFamily family) =>
        family is TypeFamily.Integer or TypeFamily.Decimal or TypeFamily.Floating;

    /// <summary>列参照からバインディングを引く（未解決＝既に別の診断が出ているためスキップ）</summary>
    private static bool TryBind(
        ColumnReference column,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columns,
        out QueryColumnBinding binding
    )
    {
        if (column.ResolvedColumnId is { } id && columns.TryGetValue(id, out var resolved))
        {
            binding = resolved;
            return true;
        }

        binding = null!;
        return false;
    }

    /// <summary>パラメータ参照から型情報を引く（未解決＝既に別の診断が出ているためスキップ）</summary>
    private static bool TryResolveParameter(
        ParameterOperand operand,
        IReadOnlyDictionary<string, ParameterTypeInfo> parameters,
        out ParameterTypeInfo parameter
    )
    {
        if (parameters.TryGetValue(operand.ResolvedName ?? operand.Text, out var resolved))
        {
            parameter = resolved;
            return true;
        }

        parameter = null!;
        return false;
    }

    /// <summary>診断へ載せる比較演算子の表記（DSL の綴り）</summary>
    private static string OperatorText(ComparisonOperator op) =>
        op switch
        {
            ComparisonOperator.Less => "<",
            ComparisonOperator.LessOrEqual => "<=",
            ComparisonOperator.Greater => ">",
            ComparisonOperator.GreaterOrEqual => ">=",
            ComparisonOperator.NotEqual => "<>",
            _ => "=",
        };

    /// <summary>診断へ載せる列の生成型表記（VO なら「VO クラス名 (内包型)」）</summary>
    private static string ColumnTypeText(QueryColumnBinding column) =>
        column.ValueObjectClassName is { } vo
            ? $"{vo} ({column.UnderlyingTypeName})"
            : column.UnderlyingTypeName;

    /// <summary>診断へ載せるパラメータ表記（例: <c>@ids (IReadOnlyList&lt;int&gt;)</c>）</summary>
    private static string ParameterText(ParameterOperand operand, ParameterTypeInfo parameter)
    {
        var typeText = parameter.IsList
            ? $"IReadOnlyList<{parameter.TypeName}>"
            : parameter.TypeName;
        return $"@{operand.ResolvedName ?? operand.Text} ({typeText})";
    }

    /// <summary>診断へ載せる文字列リテラルの表記（長すぎる値は先頭だけ）</summary>
    private static string Truncate(string value) => value.Length <= 20 ? value : value[..20] + "…";
}
