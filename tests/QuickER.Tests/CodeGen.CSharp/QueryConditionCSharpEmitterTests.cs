using AwesomeAssertions;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>ミニ DSL 構文木 → C# ラムダ式エミッタの出力（ラムダの全文と前置文）を表駆動で固定するテストクラス</summary>
/// <remarks>
/// 残す理由（P3-6）：エミッタの出力の契約（綴り）。<see cref="QueryConditionCSharpEmitter"/> が返す式と前置文の文字列の完全一致で、
/// 括弧の入れ方・NOT を原子まで押し下げた形・NULL 前提の内側の否定・NULL 非許容の列に <c>!</c> と NULL 前提を付けない形・
/// 値オブジェクトの <c>Create</c> の包み方・持ち上げ変数の綴りを見る（どれも動作に出ない形を含み、実行の結果では言えない）。
/// 意味（NULL の行・NOT・文字列一致・IN・値オブジェクト）は簡易 DSL の総当たり（<c>DslConditionFuzzTests</c>・NOT の入れ子は
/// 次元表の因子 NotNesting と <c>DslFuzzUnits.Coverage</c> の決まった形）が、生成→コンパイル→3 実装先の実行でオラクルと照合する。
/// </remarks>
public class QueryConditionCSharpEmitterTests
{
    private readonly Entity _entity;
    private readonly List<QueryParameter> _parameters;

    /// <summary>列の束縛の組み合わせ</summary>
    public enum Binding
    {
        /// <summary>素の C#（Memo だけ NULL 許容）</summary>
        Plain,

        /// <summary>素の C#・Memo を NULL 非許容にしたもの</summary>
        PlainMemoNotNull,

        /// <summary>素の C#・Amount を NULL 許容にしたもの</summary>
        PlainAmountNullable,

        /// <summary>全列が値オブジェクト（Memo だけ NULL 許容）</summary>
        ValueObjects,

        /// <summary>全列が値オブジェクト・Amount を NULL 許容にしたもの</summary>
        ValueObjectsAmountNullable,
    }

    /// <summary>表の行（条件・列の束縛・期待のラムダの全文・期待の前置文）</summary>
    private sealed record EmitRow(
        string Condition,
        Binding Binding,
        string Lambda,
        string[] Prelude
    );

    /// <summary>
    /// 行（Id → 行）。元のメソッドごとの表明をすべて写した（前置文は元が見ていなかった行でも、前置文が無いことまで見る）
    /// </summary>
    private static IReadOnlyDictionary<string, EmitRow> Rows { get; } =
        new Dictionary<string, EmitRow>(StringComparer.Ordinal)
        {
            // 比較・AND/OR・IS NULL（元 Emit_ComparisonAndLogical）
            ["comparison-and-logical"] = new(
                "CustomerId = @customerId AND (Amount >= @minAmount OR Memo IS NULL)",
                Binding.Plain,
                "e => (e.CustomerId == customerId && (e.Amount >= minAmount || e.Memo == null))",
                []
            ),
            // 文字列一致は Contains / StartsWith / EndsWith の呼び出しで、NULL 許容の列は NULL 前提の内側（元 Emit_StringMatch）
            ["string-match-like-param"] = new(
                "Memo LIKE @keyword",
                Binding.Plain,
                "e => (e.Memo != null && e.Memo!.Contains(keyword))",
                []
            ),
            ["string-match-like-prefix"] = new(
                "Memo LIKE 'abc%'",
                Binding.Plain,
                "e => (e.Memo != null && e.Memo!.StartsWith(literal1))",
                ["var literal1 = \"abc\";"]
            ),
            ["string-match-not-like-suffix"] = new(
                "Memo NOT LIKE '%abc'",
                Binding.Plain,
                "e => (e.Memo != null && !(e.Memo!.EndsWith(literal1)))",
                ["var literal1 = \"abc\";"]
            ),
            ["string-match-startswith-param"] = new(
                "Memo STARTSWITH @keyword",
                Binding.Plain,
                "e => (e.Memo != null && e.Memo!.StartsWith(keyword))",
                []
            ),
            // ワイルドカードなしの LIKE：肯定は素の等値・否定と外側の NOT は NULL 前提の内側（元 Emit_ExactLike_NegatesInsideNullPremise）
            ["exact-like"] = new(
                "Memo LIKE 'abc'",
                Binding.Plain,
                "e => e.Memo == literal1",
                ["var literal1 = \"abc\";"]
            ),
            ["exact-like-negated"] = new(
                "Memo NOT LIKE 'abc'",
                Binding.Plain,
                "e => (e.Memo != null && !(e.Memo == literal1))",
                ["var literal1 = \"abc\";"]
            ),
            ["exact-like-under-not"] = new(
                "NOT Memo LIKE 'abc'",
                Binding.Plain,
                "e => (e.Memo != null && !(e.Memo == literal1))",
                ["var literal1 = \"abc\";"]
            ),
            ["exact-like-under-double-not"] = new(
                "NOT NOT Memo LIKE 'abc'",
                Binding.Plain,
                "e => e.Memo == literal1",
                ["var literal1 = \"abc\";"]
            ),
            ["exact-like-in-negated-or"] = new(
                "NOT (Memo LIKE 'abc' OR CustomerId = 1)",
                Binding.Plain,
                "e => ((e.Memo != null && !(e.Memo == literal1)) && e.CustomerId != 1)",
                ["var literal1 = \"abc\";"]
            ),
            // 前置 NOT の連鎖は偶奇で畳む（元 Emit_ChainedNot_FoldsByParity）
            ["chained-not-double"] = new(
                "NOT NOT Memo LIKE '%x%'",
                Binding.Plain,
                "e => (e.Memo != null && e.Memo!.Contains(literal1))",
                ["var literal1 = \"x\";"]
            ),
            ["chained-not-triple"] = new(
                "NOT NOT NOT Memo LIKE '%x%'",
                Binding.Plain,
                "e => (e.Memo != null && !(e.Memo!.Contains(literal1)))",
                ["var literal1 = \"x\";"]
            ),
            // 文字列のリテラルは前置文のローカル変数へ持ち上げる（EF Core がパラメータにする）。同じ値は 1 つの変数を共有し、
            // 名前はメソッド引数（literal2）とぶつかると _ を後置する。比較の右辺の文字列のリテラルも同じ
            ["string-literal-same-value-shares-one-local"] = new(
                "Memo = 'abc' OR Memo LIKE '%abc'",
                Binding.Plain,
                "e => (e.Memo == literal1 || (e.Memo != null && e.Memo!.EndsWith(literal1)))",
                ["var literal1 = \"abc\";"]
            ),
            ["string-literal-avoids-parameter-name"] = new(
                "Memo = 'a' OR Memo = 'b'",
                Binding.Plain,
                "e => (e.Memo == literal1 || e.Memo == literal2_)",
                ["var literal1 = \"a\";", "var literal2_ = \"b\";"]
            ),
            ["value-object-string-literal"] = new(
                "Memo = 'abc'",
                Binding.ValueObjects,
                "e => e.Memo == MemoValue.Create(literal1)",
                ["var literal1 = \"abc\";"]
            ),
            // float の列の数値のリテラルは、文字列のリテラルと同じく前置文のローカル変数へ持ち上げる（EF Core がパラメータ〔単精度〕に
            // する）。同じ綴りでも文字列のリテラルとは別の変数になり、名前は文字列と同じ規則で決まる
            ["float-literal-lifted"] = new(
                "Ratio = 0.1 OR Ratio > 16777217 OR Memo = '0.1f'",
                Binding.Plain,
                "e => ((e.Ratio == literal1 || e.Ratio > literal2_) || e.Memo == literal3)",
                ["var literal1 = 0.1f;", "var literal2_ = 16777217f;", "var literal3 = \"0.1f\";"]
            ),
            ["value-object-float-literal"] = new(
                "Ratio = 0.5",
                Binding.ValueObjects,
                "e => e.Ratio == RatioValue.Create(literal1)",
                ["var literal1 = 0.5f;"]
            ),
            // double の列の数値のリテラルは定数のまま（EF Core のリテラルが倍精度の値のまま書かれ、C# と同じ答えになる）
            ["double-literal-inline"] = new(
                "Score = 0.1",
                Binding.Plain,
                "e => e.Score == 0.1d",
                []
            ),
            // NULL 非許容の文字列の列には ! も NULL 前提も付けない（元 Emit_StringMatch_NonNullableColumn）
            ["string-match-non-nullable-column"] = new(
                "Memo CONTAINS @keyword",
                Binding.PlainMemoNotNull,
                "e => e.Memo.Contains(keyword)",
                []
            ),
            // IN / NOT IN はコレクションの Contains（元 Emit_In）
            ["in"] = new(
                "CustomerId IN @ids",
                Binding.Plain,
                "e => ids.Contains(e.CustomerId)",
                []
            ),
            ["not-in"] = new(
                "CustomerId NOT IN @ids",
                Binding.Plain,
                "e => !(ids.Contains(e.CustomerId))",
                []
            ),
            // NOT は原子まで押し下げ、NULL 許容の列の順序比較の否定は列が NULL の行も一致させる（元 Emit_Not_IsPushedDownToAtoms）
            ["not-nullable-ordered"] = new(
                "NOT Amount < @minAmount",
                Binding.PlainAmountNullable,
                "e => (e.Amount == null || !(e.Amount < minAmount))",
                []
            ),
            ["not-non-nullable-ordered"] = new(
                "NOT CustomerId < 3",
                Binding.PlainAmountNullable,
                "e => !(e.CustomerId < 3)",
                []
            ),
            ["not-or-group"] = new(
                "NOT (Amount < @minAmount OR CustomerId = @customerId)",
                Binding.PlainAmountNullable,
                "e => ((e.Amount == null || !(e.Amount < minAmount)) && e.CustomerId != customerId)",
                []
            ),
            ["not-and-group"] = new(
                "NOT (CustomerId <> 1 AND Memo IS NULL)",
                Binding.PlainAmountNullable,
                "e => (e.CustomerId == 1 || e.Memo != null)",
                []
            ),
            ["double-not-nullable-ordered"] = new(
                "NOT NOT Amount < @minAmount",
                Binding.PlainAmountNullable,
                "e => e.Amount < minAmount",
                []
            ),
            ["not-in-group"] = new(
                "NOT (CustomerId IN @ids)",
                Binding.PlainAmountNullable,
                "e => !(ids.Contains(e.CustomerId))",
                []
            ),
            ["not-not-in-group"] = new(
                "NOT (CustomerId NOT IN @ids)",
                Binding.PlainAmountNullable,
                "e => ids.Contains(e.CustomerId)",
                []
            ),
            // 値オブジェクトの NULL 許容の列の順序比較は NULL 前提の内側・否定は列が NULL の行も一致（元 Emit_Not_ValueObjectOrderedComparison）
            ["value-object-nullable-ordered"] = new(
                "Amount < 1",
                Binding.ValueObjectsAmountNullable,
                "e => (e.Amount != null && e.Amount < AmountValue.Create(1m))",
                []
            ),
            ["value-object-not-nullable-ordered"] = new(
                "NOT Amount < 1",
                Binding.ValueObjectsAmountNullable,
                "e => (e.Amount == null || !(e.Amount < AmountValue.Create(1m)))",
                []
            ),
            // 値オブジェクトの列の比較は Create で包む（元 Emit_ValueObjectComparison）
            ["value-object-compare-param"] = new(
                "CustomerId = @customerId",
                Binding.ValueObjects,
                "e => e.CustomerId == CustomerIdValue.Create(customerId)",
                []
            ),
            ["value-object-compare-literal"] = new(
                "Amount > -1.5",
                Binding.ValueObjects,
                "e => e.Amount > AmountValue.Create(-1.5m)",
                []
            ),
            // 値オブジェクトの列の IN は前置文で値オブジェクトのリストへ持ち上げる（元 Emit_ValueObjectIn）
            ["value-object-in"] = new(
                "CustomerId IN @ids",
                Binding.ValueObjects,
                "e => idsValues.Contains(e.CustomerId)",
                ["var idsValues = ids.Select(CustomerIdValue.Create).ToList();"]
            ),
        };

    /// <summary>表の行の Id</summary>
    public static TheoryData<string> Ids() => [.. Rows.Keys];

    /// <summary>Order エンティティ（CustomerId / Amount / Memo）と標準パラメータを用意する</summary>
    public QueryConditionCSharpEmitterTests()
    {
        _entity = new Entity { TableName = "Order" };
        _entity.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        _entity.Columns.Add(
            new Column
            {
                Name = "Amount",
                DataType = "decimal(12,2)",
                IsNullable = false,
            }
        );
        _entity.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });
        _entity.Columns.Add(
            new Column
            {
                Name = "Ratio",
                DataType = "real",
                IsNullable = false,
            }
        );
        _entity.Columns.Add(
            new Column
            {
                Name = "Score",
                DataType = "float",
                IsNullable = false,
            }
        );

        _parameters =
        [
            new QueryParameter { Name = "customerId", Type = "int32" },
            new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
            new QueryParameter { Name = "keyword", Type = "string(50)" },
            new QueryParameter
            {
                Name = "ids",
                Type = "int32",
                IsList = true,
            },
            // 持ち上げた文字列のリテラルの名前とぶつかる引数（行 string-literal-avoids-parameter-name）
            new QueryParameter { Name = "literal2", Type = "string(50)" },
        ];
    }

    [Theory(DisplayName = "エミッタの出力（ラムダの全文と前置文）は表の行の綴りどおり")]
    [MemberData(nameof(Ids))]
    public void Emit_WritesExpectedLambdaAndPrelude(string id)
    {
        var row = Rows[id];
        var emitted = Emit(row.Condition, CreateBindings(row.Binding));

        emitted.Lambda.Should().Be(row.Lambda);
        emitted.PreludeLines.Should().Equal(row.Prelude);
    }

    /// <summary>列の束縛を作る（NULL 許容を変える組み合わせは、パーサの検証が見る列の NULL 許容もそろえる）</summary>
    private Dictionary<Guid, QueryColumnBinding> CreateBindings(Binding binding)
    {
        var valueObjects = binding is Binding.ValueObjects or Binding.ValueObjectsAmountNullable;
        var amountNullable =
            binding is Binding.PlainAmountNullable or Binding.ValueObjectsAmountNullable;
        var memoNullable = binding != Binding.PlainMemoNotNull;

        if (!memoNullable)
        {
            _entity.Columns[2].IsNullable = false;
        }

        return new()
        {
            [_entity.Columns[0].Id] = new QueryColumnBinding(
                "CustomerId",
                "int",
                valueObjects ? "CustomerIdValue" : null,
                false,
                IsUnderlyingReferenceType: false,
                IsGuidKeyValueObject: false
            ),
            [_entity.Columns[1].Id] = new QueryColumnBinding(
                "Amount",
                "decimal",
                valueObjects ? "AmountValue" : null,
                amountNullable,
                IsUnderlyingReferenceType: false,
                IsGuidKeyValueObject: false
            ),
            [_entity.Columns[2].Id] = new QueryColumnBinding(
                "Memo",
                "string",
                valueObjects ? "MemoValue" : null,
                memoNullable,
                IsUnderlyingReferenceType: true,
                IsGuidKeyValueObject: false
            ),
            [_entity.Columns[3].Id] = new QueryColumnBinding(
                "Ratio",
                "float",
                valueObjects ? "RatioValue" : null,
                false,
                IsUnderlyingReferenceType: false,
                IsGuidKeyValueObject: false
            ),
            [_entity.Columns[4].Id] = new QueryColumnBinding(
                "Score",
                "double",
                valueObjects ? "ScoreValue" : null,
                false,
                IsUnderlyingReferenceType: false,
                IsGuidKeyValueObject: false
            ),
        };
    }

    /// <summary>条件をパース・検証してエミットする</summary>
    private QueryConditionCSharpEmitter.EmitResult Emit(
        string condition,
        Dictionary<Guid, QueryColumnBinding> bindings
    )
    {
        var result = QueryConditionParser.ParseAndValidate(condition, _entity, _parameters);
        result
            .Success.Should()
            .BeTrue(
                $"条件 '{condition}' は検証を通る前提: {string.Join(" / ", result.Diagnostics.Select(d => d.Message))}"
            );

        return QueryConditionCSharpEmitter.Emit(
            result.Root!,
            bindings,
            _parameters.Select(p => p.Name).ToList()
        );
    }
}
