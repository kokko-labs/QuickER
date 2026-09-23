using System.Globalization;
using System.Text;

namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>条件式の列が生成コード上でどう見えるか（プロパティ名・型・値オブジェクト情報）</summary>
/// <param name="PropertyName">生成エンティティ上のプロパティ名</param>
/// <param name="UnderlyingTypeName">素の C# 型名（値オブジェクトなら内包値の型。null 許容 <c>?</c> は含まない）</param>
/// <param name="ValueObjectClassName">値オブジェクト列の VO クラス名（VO でなければ null）</param>
/// <param name="IsNullable">NULL 許容列かどうか</param>
/// <param name="IsUnderlyingReferenceType">素の C# 型が参照型か（射影 DTO の非 NULL プロパティ初期化子の要否判定と、NULL 許容値型列の IN リスト持ち上げ要否の判定に使う）</param>
public sealed record QueryColumnBinding(
    string PropertyName,
    string UnderlyingTypeName,
    string? ValueObjectClassName,
    bool IsNullable,
    bool IsUnderlyingReferenceType = false
);

/// <summary>
/// 名前付きクエリ条件（ミニ DSL）の構文木から C# のラムダ式（述語）テキストを生成するエミッタ
/// </summary>
/// <remarks>
/// <para>
/// 生成するのは <c>Query().Where(...)</c> に渡す単一の式で、QuickER 版 Repository（ADO）の
/// SqlExpressionTranslator と EF Core の双方が翻訳できる形に限定する：
/// 比較・論理結合・否定・null 判定・文字列 Contains / StartsWith / EndsWith・コレクション Contains（IN）。
/// 方言 SQL への翻訳は実行時にランタイム側が行うため、このエミッタに方言分岐はない。
/// </para>
/// <para>
/// 値オブジェクト（VO）列は「VO 同士の比較」（<c>e.Prop == VoClass.Create(値)</c>）で出力する。
/// ADO 側はパラメータ化時に素値へ開き、EF Core 側はコンバータで列型へ写すため両系統で翻訳できる。
/// IN のリストパラメータのみ、行ごとの生成を避けるためメソッド冒頭で VO リストへ持ち上げる
/// （<see cref="EmitResult.PreludeLines"/>）。
/// </para>
/// </remarks>
public static class QueryConditionCSharpEmitter
{
    /// <summary>エミット結果（ラムダ本体と、ラムダより前に置く前置文）</summary>
    /// <param name="Lambda">ラムダ式のテキスト（例: <c>e =&gt; e.CustomerId == customerId</c>）</param>
    /// <param name="PreludeLines">メソッド本体の先頭へ置く文（VO リストの持ち上げ等。不要なら空）</param>
    public sealed record EmitResult(string Lambda, IReadOnlyList<string> PreludeLines);

    /// <summary>検証済みの構文木からラムダ式テキストを生成する</summary>
    /// <param name="root">検証済み（列・パラメータ解決済み）の構文木</param>
    /// <param name="columns">列 ID → 生成コード上の列情報</param>
    /// <param name="parameterNames">ラムダ変数名の衝突回避に使う、メソッド引数名の一覧</param>
    /// <param name="parameterValueObjects">
    /// VO 型で型付けされたパラメータの「名前 → VO クラス名」対応（列参照型付け）。
    /// 条件列と同じ VO 型のパラメータは <c>Create</c> で包まず直接比較する。null は VO 型パラメータなし
    /// </param>
    public static EmitResult Emit(
        ConditionNode root,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columns,
        IReadOnlyCollection<string> parameterNames,
        IReadOnlyDictionary<string, string>? parameterValueObjects = null
    )
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(parameterNames);

        var lambdaVar = PickLambdaVariable(parameterNames);
        var prelude = new List<string>();
        var body = Visit(
            root,
            new Context(
                lambdaVar,
                columns,
                prelude,
                parameterValueObjects
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                parameterNames
            )
        );
        return new EmitResult($"{lambdaVar} => {body}", prelude);
    }

    /// <summary>引数名と衝突しないラムダ変数名（e, e1, e2, ...）を選ぶ（並び順・射影の選択式でも同じ変数名を使うため公開）</summary>
    public static string PickLambdaVariable(IReadOnlyCollection<string> parameterNames)
    {
        if (!parameterNames.Contains("e"))
        {
            return "e";
        }

        for (var i = 1; ; i++)
        {
            var candidate = "e" + i.ToString(CultureInfo.InvariantCulture);

            if (!parameterNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>エミット中の共有状態（ラムダ変数名・列情報・前置文の収集先・VO 型パラメータ・使用済み名）</summary>
    private sealed record Context(
        string LambdaVar,
        IReadOnlyDictionary<Guid, QueryColumnBinding> Columns,
        List<string> PreludeLines,
        IReadOnlyDictionary<string, string> ParameterValueObjects,
        IReadOnlyCollection<string> TakenNames
    )
    {
        /// <summary>パラメータが指定 VO クラスで型付けされているか（列参照型付けの直接比較判定）</summary>
        public bool IsParameterOfValueObject(string parameterName, string valueObjectClassName) =>
            ParameterValueObjects.TryGetValue(parameterName, out var vo)
            && vo == valueObjectClassName;

        /// <summary>
        /// IN の持ち上げリスト変数名（<c>{パラメータ名}Values</c>）を選ぶ。メソッド引数名と衝突するとき
        /// （例: パラメータ <c>ids</c> と <c>idsValues</c> が同居する定義）は <c>_</c> を後置して回避する
        /// （衝突したままだとローカル変数が引数を隠して CS0136 になる）。決定的なので同じパラメータの
        /// 複数回出現は同じ名前へ解決され、前置文の重複排除がそのまま効く
        /// </summary>
        public string PickListVariable(string parameterName)
        {
            var candidate = parameterName + "Values";

            while (TakenNames.Contains(candidate))
            {
                candidate += "_";
            }

            return candidate;
        }
    }

    /// <summary>ノードを C# 式テキストへ変換する</summary>
    private static string Visit(ConditionNode node, Context context) =>
        node switch
        {
            LogicalNode logical => VisitLogical(logical, context),
            NotNode not => VisitNot(not, context),
            ComparisonNode comparison => VisitComparison(comparison, context),
            NullCheckNode nullCheck => VisitNullCheck(nullCheck, context),
            StringMatchNode match => VisitStringMatch(match, context),
            InNode inNode => VisitIn(inNode, context),
            _ => throw new InvalidOperationException(
                $"未知の条件ノードです: {node.GetType().Name}"
            ),
        };

    /// <summary>AND / OR（優先順位を明示するため常に括弧で囲む）</summary>
    private static string VisitLogical(LogicalNode node, Context context)
    {
        var op = node.Operator == LogicalOperator.And ? "&&" : "||";
        return $"({Visit(node.Left, context)} {op} {Visit(node.Right, context)})";
    }

    /// <summary>前置 NOT（文字列一致には Negated として畳み込み、それ以外は <c>!(...)</c> で包む）</summary>
    /// <remarks>
    /// <c>NOT col LIKE '%x%'</c> を <c>!(...)</c> で包むと NULL 前提の外側に出て、<c>col NOT LIKE '%x%'</c>
    /// （前提の内側で否定＝NULL 行はどちらの向きでも一致しない）と結果が割れる。多重 NOT は偶奇で畳む。
    /// 文字列一致以外（比較・IN・括弧つき複合条件）は従来どおり素直に包む（等値の反転・IN の畳み込みは
    /// 実行時の SQL 翻訳器が担う既存の線を変えない）。
    /// </remarks>
    private static string VisitNot(NotNode node, Context context)
    {
        var negate = true;
        ConditionNode operand = node.Operand;

        while (operand is NotNode inner)
        {
            negate = !negate;
            operand = inner.Operand;
        }

        if (operand is StringMatchNode match)
        {
            return VisitStringMatch(
                new StringMatchNode
                {
                    Column = match.Column,
                    Negated = match.Negated ^ negate,
                    Kind = match.Kind,
                    Operand = match.Operand,
                },
                context
            );
        }

        return $"!({Visit(node.Operand, context)})";
    }

    /// <summary>比較（VO 列は VO 同士の比較に揃える）</summary>
    private static string VisitComparison(ComparisonNode node, Context context)
    {
        var column = Bind(node.Column, context);
        var op = node.Operator switch
        {
            ComparisonOperator.Equal => "==",
            ComparisonOperator.NotEqual => "!=",
            ComparisonOperator.Less => "<",
            ComparisonOperator.LessOrEqual => "<=",
            ComparisonOperator.Greater => ">",
            ComparisonOperator.GreaterOrEqual => ">=",
            _ => throw new InvalidOperationException($"未知の比較演算子です: {node.Operator}"),
        };
        var operand = RenderOperand(node.Operand, column, context);
        var access = $"{context.LambdaVar}.{column.PropertyName}";
        var comparison = $"{access} {op} {operand}";

        // NULL 許容の VO 列への順序比較は「列が NULL でないこと」を AND した形へエミットする（文字列一致の
        // NULL 前提と同じ流儀）。VO の比較演算子は null を最小として順序付けるため、素のままだとインメモリ
        // 実行器（式木をコンパイルして評価）だけが < / <= で NULL 行を返し、SQL・EF Core（UNKNOWN で脱落）と
        // 観測結果が割れる。SQL 側は IS NOT NULL の連言が加わるだけで意味は変わらない。
        // 素の NULL 許容値型列は C# の持ち上げ演算子（null は常に false）が SQL と一致するため対象外
        var isOrdered =
            node.Operator
            is ComparisonOperator.Less
                or ComparisonOperator.LessOrEqual
                or ComparisonOperator.Greater
                or ComparisonOperator.GreaterOrEqual;

        if (isOrdered && column.ValueObjectClassName is not null && column.IsNullable)
        {
            return $"({access} != null && {comparison})";
        }

        return comparison;
    }

    /// <summary>null 判定（NULL 許容列であることはパーサ側の検証で保証済み）</summary>
    private static string VisitNullCheck(NullCheckNode node, Context context)
    {
        var column = Bind(node.Column, context);
        var op = node.IsNotNull ? "!=" : "==";
        return $"{context.LambdaVar}.{column.PropertyName} {op} null";
    }

    /// <summary>文字列一致（string / VO どちらの列でもインスタンスメソッド呼び出しに揃える）</summary>
    private static string VisitStringMatch(StringMatchNode node, Context context)
    {
        var column = Bind(node.Column, context);

        // 完全一致（ワイルドカードなしの NOT LIKE）は等値比較を NULL 前提の内側で否定する。
        // 等値の <> へ畳むと列側の IS NULL 補償で NULL 行が一致に含まれ、LIKE の意味論
        // （NULL 行はどちらの向きでも一致しない）から外れる
        if (node.Kind == StringMatchKind.Exact)
        {
            return VisitExactMatch(node, column, context);
        }

        var method = node.Kind switch
        {
            StringMatchKind.Contains => "Contains",
            StringMatchKind.StartsWith => "StartsWith",
            StringMatchKind.EndsWith => "EndsWith",
            _ => throw new InvalidOperationException($"未知の一致種別です: {node.Kind}"),
        };

        // 文字列一致は VO でも string 引数のオーバーロードを使う（VO 包装は不要）
        var operand = node.Operand switch
        {
            ParameterOperand parameter => ParameterName(parameter),
            StringOperand literal => RenderStringLiteral(literal.Value),
            _ => throw new InvalidOperationException(
                "文字列一致の右辺はパラメータか文字列リテラルのみです。"
            ),
        };

        // NULL 許容列は「列が NULL でないこと」を明示的な前提として AND する。SQL の LIKE は NULL 行を
        // UNKNOWN で落とすので、この前提は SQL 側では追加の選言にならず（IS NOT NULL AND LIKE ＝ LIKE 単独）
        // 意味が変わらない。一方インメモリ実行器は式木をコンパイルして実際に評価するため、
        // 前提を書かないと NULL 行で NullReferenceException になり、3 実装先の観測結果が割れる。
        // 否定（NOT CONTAINS）も同じ前提の内側へ入れる＝NULL 行はどちらの向きでも一致しない（SQL と同じ）。
        var access = $"{context.LambdaVar}.{column.PropertyName}";

        // VO 列の != は利用者定義演算子のためコンパイラの NULL 状態解析が伝播しない。抑止（!）は残す
        var suppression = column.IsNullable ? "!" : string.Empty;
        var call = $"{access}{suppression}.{method}({operand})";
        var matched = node.Negated ? $"!({call})" : call;

        return column.IsNullable ? $"({access} != null && {matched})" : matched;
    }

    /// <summary>完全一致（ワイルドカードなしの [NOT] LIKE リテラル）を NULL 前提の内側で組み立てる</summary>
    private static string VisitExactMatch(
        StringMatchNode node,
        QueryColumnBinding column,
        Context context
    )
    {
        // Exact のオペランドは常に文字列リテラル（パーサが LIKE のリテラル分解からのみ作る）。
        // VO 列は等値比較のため VO へ包む（文字列一致メソッドの string オーバーロードとは違う）
        var literal = RenderStringLiteral(((StringOperand)node.Operand).Value);
        var operand = column.ValueObjectClassName is { } voClass
            ? $"{voClass}.Create({literal})"
            : literal;

        var access = $"{context.LambdaVar}.{column.PropertyName}";
        var comparison = $"{access} == {operand}";
        var matched = node.Negated ? $"!({comparison})" : comparison;

        // NULL 許容列は「列が NULL でないこと」を前提に AND する（他の一致種別と同じ＝NULL 行は
        // どちらの向きでも一致しない）。二重否定などで肯定形へ畳まれた場合も同じ前提の内側に置く
        return column.IsNullable ? $"({access} != null && {matched})" : matched;
    }

    /// <summary>IN（コレクション Contains。VO 列はリストを VO へ持ち上げてから比較する）</summary>
    private static string VisitIn(InNode node, Context context)
    {
        var column = Bind(node.Column, context);
        var parameter = ParameterName(node.Parameter);
        string call;

        if (
            column.ValueObjectClassName is { } voClass
            && !context.IsParameterOfValueObject(parameter, voClass)
        )
        {
            // 行ごとの VO 生成を避けるため、メソッド冒頭で一度だけ VO リストへ変換する
            var listVar = context.PickListVariable(parameter);
            var prelude = $"var {listVar} = {parameter}.Select({voClass}.Create).ToList();";

            if (!context.PreludeLines.Contains(prelude))
            {
                context.PreludeLines.Add(prelude);
            }

            call = $"{listVar}.Contains({context.LambdaVar}.{column.PropertyName})";
        }
        else if (
            column.ValueObjectClassName is null
            && column.IsNullable
            && !column.IsUnderlyingReferenceType
        )
        {
            // NULL 許容の値型列は、リスト（IReadOnlyList<T>）と列（T?）で Contains の型推論が
            // 一致せずコンパイルできない（CS1929）。メソッド冒頭で一度だけ T? のリストへ
            // 持ち上げる（VO リストの持ち上げと同じ経路）。列が NULL の行は Contains(null) が
            // false になり、SQL / EF Core の「NULL は非 null 値のリストに含まれない」と揃う
            var listVar = context.PickListVariable(parameter);
            var prelude =
                $"var {listVar} = {parameter}.Cast<{column.UnderlyingTypeName}?>().ToList();";

            if (!context.PreludeLines.Contains(prelude))
            {
                context.PreludeLines.Add(prelude);
            }

            call = $"{listVar}.Contains({context.LambdaVar}.{column.PropertyName})";
        }
        else
        {
            // 素の列、または VO 型で型付けされたパラメータ（列参照）は変換なしでそのまま比較できる
            call = $"{parameter}.Contains({context.LambdaVar}.{column.PropertyName})";
        }

        return node.Negated ? $"!({call})" : call;
    }

    /// <summary>比較の右辺（パラメータ・数値・文字列。VO 列は Create で包む。VO 型パラメータは直接比較）</summary>
    private static string RenderOperand(
        ConditionOperand operand,
        QueryColumnBinding column,
        Context context
    )
    {
        // VO 型で型付けされたパラメータ（列参照）は、同じ VO 型の列と Create なしで直接比較できる
        if (
            operand is ParameterOperand typedParameter
            && column.ValueObjectClassName is { } columnVo
            && context.IsParameterOfValueObject(ParameterName(typedParameter), columnVo)
        )
        {
            return ParameterName(typedParameter);
        }

        var raw = operand switch
        {
            ParameterOperand parameter => ParameterName(parameter),
            NumberOperand number => RenderNumberLiteral(number.Literal, column.UnderlyingTypeName),
            StringOperand literal => RenderStringLiteral(literal.Value),
            _ => throw new InvalidOperationException(
                $"未知のオペランドです: {operand.GetType().Name}"
            ),
        };

        return column.ValueObjectClassName is { } voClass ? $"{voClass}.Create({raw})" : raw;
    }

    /// <summary>数値リテラルへ列の C# 型に応じたサフィックスを付ける</summary>
    private static string RenderNumberLiteral(string literal, string underlyingTypeName) =>
        underlyingTypeName switch
        {
            "decimal" => literal + "m",
            "double" => literal + "d",
            "float" => literal + "f",
            "long" => literal + "L",
            _ => literal,
        };

    /// <summary>C# の文字列リテラルとしてエスケープする</summary>
    /// <remarks>
    /// CR / LF に加えて、描画後のレンダラーの <c>ReplaceLineEndings</c> が実改行へ変える文字
    /// （FORM FEED U+000C・NEL U+0085・LINE SEPARATOR U+2028・PARAGRAPH SEPARATOR U+2029）も
    /// エスケープシーケンスで書く（生のまま出すとリテラルが行をまたいで壊れる）。
    /// </remarks>
    private static string RenderStringLiteral(string value)
    {
        var builder = new StringBuilder("\"");

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\u0085':
                    builder.Append("\\u0085");
                    break;
                case '\u2028':
                    builder.Append("\\u2028");
                    break;
                case '\u2029':
                    builder.Append("\\u2029");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>解決済みの列参照から列情報を引く</summary>
    private static QueryColumnBinding Bind(ColumnReference column, Context context)
    {
        if (
            column.ResolvedColumnId is not { } id
            || !context.Columns.TryGetValue(id, out var binding)
        )
        {
            throw new InvalidOperationException(
                $"列 '{column.Text}' が未解決です。ParseAndValidate を通した構文木を渡してください。"
            );
        }

        return binding;
    }

    /// <summary>解決済みのパラメータ名（正準名）を返す</summary>
    private static string ParameterName(ParameterOperand parameter) =>
        parameter.ResolvedName
        ?? throw new InvalidOperationException(
            $"パラメータ '@{parameter.Text}' が未解決です。ParseAndValidate を通した構文木を渡してください。"
        );
}
