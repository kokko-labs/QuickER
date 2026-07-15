using System.Globalization;
using System.Text;

namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>条件式の列が生成コード上でどう見えるか（プロパティ名・型・値オブジェクト情報）</summary>
/// <param name="PropertyName">生成エンティティ上のプロパティ名</param>
/// <param name="UnderlyingTypeName">素の C# 型名（値オブジェクトなら内包値の型。null 許容 <c>?</c> は含まない）</param>
/// <param name="ValueObjectClassName">値オブジェクト列の VO クラス名（VO でなければ null）</param>
/// <param name="IsNullable">NULL 許容列かどうか</param>
public sealed record QueryColumnBinding(
    string PropertyName,
    string UnderlyingTypeName,
    string? ValueObjectClassName,
    bool IsNullable
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
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>エミット中の共有状態（ラムダ変数名・列情報・前置文の収集先・VO 型パラメータ）</summary>
    private sealed record Context(
        string LambdaVar,
        IReadOnlyDictionary<Guid, QueryColumnBinding> Columns,
        List<string> PreludeLines,
        IReadOnlyDictionary<string, string> ParameterValueObjects
    )
    {
        /// <summary>パラメータが指定 VO クラスで型付けされているか（列参照型付けの直接比較判定）</summary>
        public bool IsParameterOfValueObject(string parameterName, string valueObjectClassName) =>
            ParameterValueObjects.TryGetValue(parameterName, out var vo)
            && vo == valueObjectClassName;
    }

    /// <summary>ノードを C# 式テキストへ変換する</summary>
    private static string Visit(ConditionNode node, Context context) =>
        node switch
        {
            LogicalNode logical => VisitLogical(logical, context),
            NotNode not => $"!({Visit(not.Operand, context)})",
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
        return $"{context.LambdaVar}.{column.PropertyName} {op} {operand}";
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

        // NULL 許容列は null 抑止（!）を付ける（式は翻訳されるだけで実行はされないため安全）
        var suppression = column.IsNullable ? "!" : string.Empty;
        var call = $"{context.LambdaVar}.{column.PropertyName}{suppression}.{method}({operand})";
        return node.Negated ? $"!({call})" : call;
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
            var listVar = parameter + "Values";
            var prelude = $"var {listVar} = {parameter}.Select({voClass}.Create).ToList();";

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
