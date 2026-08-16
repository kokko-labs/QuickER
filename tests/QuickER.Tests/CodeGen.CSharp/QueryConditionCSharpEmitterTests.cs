using AwesomeAssertions;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>ミニ DSL 構文木 → C# ラムダ式エミッタのテストクラス</summary>
public class QueryConditionCSharpEmitterTests
{
    private readonly Entity _entity;
    private readonly List<QueryParameter> _parameters;

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
        ];
    }

    /// <summary>素の C#（VO なし）の列バインディングを作る</summary>
    private Dictionary<Guid, QueryColumnBinding> CreatePlainBindings() =>
        new()
        {
            [_entity.Columns[0].Id] = new QueryColumnBinding("CustomerId", "int", null, false),
            [_entity.Columns[1].Id] = new QueryColumnBinding("Amount", "decimal", null, false),
            [_entity.Columns[2].Id] = new QueryColumnBinding("Memo", "string", null, true),
        };

    /// <summary>VO（全列値オブジェクト）の列バインディングを作る</summary>
    private Dictionary<Guid, QueryColumnBinding> CreateVoBindings() =>
        new()
        {
            [_entity.Columns[0].Id] = new QueryColumnBinding(
                "CustomerId",
                "int",
                "CustomerIdValue",
                false
            ),
            [_entity.Columns[1].Id] = new QueryColumnBinding(
                "Amount",
                "decimal",
                "AmountValue",
                false
            ),
            [_entity.Columns[2].Id] = new QueryColumnBinding("Memo", "string", "MemoValue", true),
        };

    /// <summary>条件をパース・検証してエミットする</summary>
    private QueryConditionCSharpEmitter.EmitResult Emit(
        string condition,
        Dictionary<Guid, QueryColumnBinding> bindings,
        params string[] extraParameterNames
    )
    {
        var result = QueryConditionParser.ParseAndValidate(condition, _entity, _parameters);
        result
            .Success.Should()
            .BeTrue(
                $"条件 '{condition}' は検証を通る前提: {string.Join(" / ", result.Diagnostics.Select(d => d.Message))}"
            );

        var names = _parameters.Select(p => p.Name).Concat(extraParameterNames).ToList();
        return QueryConditionCSharpEmitter.Emit(result.Root!, bindings, names);
    }

    /// <summary>比較・論理結合・null 判定が期待どおりのラムダになることを検証する</summary>
    [Fact(DisplayName = "比較・AND/OR・IS NULL のラムダ生成")]
    public void Emit_ComparisonAndLogical()
    {
        var emitted = Emit(
            "CustomerId = @customerId AND (Amount >= @minAmount OR Memo IS NULL)",
            CreatePlainBindings()
        );

        emitted
            .Lambda.Should()
            .Be("e => (e.CustomerId == customerId && (e.Amount >= minAmount || e.Memo == null))");
        emitted.PreludeLines.Should().BeEmpty();
    }

    /// <summary>数値リテラルに列型のサフィックスが付くことを検証する</summary>
    [Fact(DisplayName = "数値リテラルは列型に応じたサフィックス付き")]
    public void Emit_NumberLiteralSuffix()
    {
        Emit("Amount > -1.5", CreatePlainBindings()).Lambda.Should().Be("e => e.Amount > -1.5m");
        Emit("CustomerId <> 0", CreatePlainBindings()).Lambda.Should().Be("e => e.CustomerId != 0");
    }

    /// <summary>文字列リテラルが C# エスケープされることを検証する</summary>
    [Fact(DisplayName = "文字列リテラルは C# エスケープされる")]
    public void Emit_StringLiteral()
    {
        Emit("Memo = 'it''s \"quoted\"'", CreatePlainBindings())
            .Lambda.Should()
            .Be("e => e.Memo == \"it's \\\"quoted\\\"\"");
    }

    /// <summary>LIKE / CONTAINS 系が文字列メソッド呼び出しになり、NULL 許容列では NULL 前提が AND されることを検証する</summary>
    /// <remarks>
    /// SQL の LIKE は NULL 行を UNKNOWN で落とすため <c>IS NOT NULL AND LIKE</c> は SQL 側で意味が変わらないが、
    /// インメモリ実行器は式木を<b>実際に評価する</b>ので、前提がないと NULL 行で NullReferenceException になる。
    /// 否定（NOT LIKE）も同じ前提の内側に入る＝NULL 行はどちらの向きでも一致しない（SQL と同じ観測結果）。
    /// </remarks>
    [Fact(
        DisplayName = "文字列一致は Contains/StartsWith/EndsWith 呼び出し（NULL 許容列は NULL 前提つき）"
    )]
    public void Emit_StringMatch()
    {
        Emit("Memo LIKE @keyword", CreatePlainBindings())
            .Lambda.Should()
            .Be("e => (e.Memo != null && e.Memo!.Contains(keyword))");
        Emit("Memo LIKE 'abc%'", CreatePlainBindings())
            .Lambda.Should()
            .Be("e => (e.Memo != null && e.Memo!.StartsWith(\"abc\"))");
        Emit("Memo NOT LIKE '%abc'", CreatePlainBindings())
            .Lambda.Should()
            .Be("e => (e.Memo != null && !(e.Memo!.EndsWith(\"abc\")))");
        Emit("Memo STARTSWITH @keyword", CreatePlainBindings())
            .Lambda.Should()
            .Be("e => (e.Memo != null && e.Memo!.StartsWith(keyword))");
    }

    /// <summary>NULL 非許容の文字列列には null 抑止（!）も NULL 前提も付かないことを検証する</summary>
    [Fact(DisplayName = "NULL 非許容列の文字列一致に ! と NULL 前提は付かない")]
    public void Emit_StringMatch_NonNullableColumn()
    {
        var bindings = CreatePlainBindings();
        bindings[_entity.Columns[2].Id] = new QueryColumnBinding("Memo", "string", null, false);
        // Memo を NULL 非許容へ変更（パーサ検証は IsNullable を見るため列側も揃える）
        _entity.Columns[2].IsNullable = false;

        Emit("Memo CONTAINS @keyword", bindings)
            .Lambda.Should()
            .Be("e => e.Memo.Contains(keyword)");
    }

    /// <summary>IN / NOT IN がコレクション Contains になることを検証する</summary>
    [Fact(DisplayName = "IN はコレクション Contains になる")]
    public void Emit_In()
    {
        Emit("CustomerId IN @ids", CreatePlainBindings())
            .Lambda.Should()
            .Be("e => ids.Contains(e.CustomerId)");
        Emit("CustomerId NOT IN @ids", CreatePlainBindings())
            .Lambda.Should()
            .Be("e => !(ids.Contains(e.CustomerId))");
    }

    /// <summary>VO 列の比較が VO.Create で包まれることを検証する</summary>
    [Fact(DisplayName = "VO 列の比較は VO.Create で包む")]
    public void Emit_ValueObjectComparison()
    {
        Emit("CustomerId = @customerId", CreateVoBindings())
            .Lambda.Should()
            .Be("e => e.CustomerId == CustomerIdValue.Create(customerId)");
        Emit("Amount > -1.5", CreateVoBindings())
            .Lambda.Should()
            .Be("e => e.Amount > AmountValue.Create(-1.5m)");
    }

    /// <summary>VO 列の IN がメソッド冒頭の VO リスト持ち上げ＋Contains になることを検証する</summary>
    [Fact(DisplayName = "VO 列の IN は前置文で VO リストへ持ち上げる")]
    public void Emit_ValueObjectIn()
    {
        var emitted = Emit("CustomerId IN @ids", CreateVoBindings());

        emitted
            .PreludeLines.Should()
            .ContainSingle()
            .Which.Should()
            .Be("var idsValues = ids.Select(CustomerIdValue.Create).ToList();");
        emitted.Lambda.Should().Be("e => idsValues.Contains(e.CustomerId)");
    }

    /// <summary>VO 型で型付けされたパラメータ（列参照）は Create で包まず直接比較されることを検証する</summary>
    [Fact(DisplayName = "VO 型パラメータは直接比較・IN も持ち上げなし")]
    public void Emit_ValueObjectTypedParameter_ComparesDirectly()
    {
        var names = _parameters.Select(p => p.Name).ToList();
        var parameterVos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customerId"] = "CustomerIdValue",
            ["ids"] = "CustomerIdValue",
        };

        var equal = QueryConditionParser.ParseAndValidate(
            "CustomerId = @customerId",
            _entity,
            _parameters
        );
        QueryConditionCSharpEmitter
            .Emit(equal.Root!, CreateVoBindings(), names, parameterVos)
            .Lambda.Should()
            .Be("e => e.CustomerId == customerId");

        var inCondition = QueryConditionParser.ParseAndValidate(
            "CustomerId IN @ids",
            _entity,
            _parameters
        );
        var emitted = QueryConditionCSharpEmitter.Emit(
            inCondition.Root!,
            CreateVoBindings(),
            names,
            parameterVos
        );
        emitted.Lambda.Should().Be("e => ids.Contains(e.CustomerId)");
        emitted.PreludeLines.Should().BeEmpty("VO 型リストはそのまま比較できるため持ち上げ不要");
    }

    /// <summary>VO 列の文字列一致は string オーバーロードのまま（Create で包まない）ことを検証する</summary>
    [Fact(DisplayName = "VO 列の文字列一致は string 引数のまま")]
    public void Emit_ValueObjectStringMatch()
    {
        Emit("Memo LIKE @keyword", CreateVoBindings())
            .Lambda.Should()
            .Be("e => (e.Memo != null && e.Memo!.Contains(keyword))");
    }

    /// <summary>ラムダ変数がパラメータ名と衝突しないことを検証する</summary>
    [Fact(DisplayName = "ラムダ変数は引数名と衝突しない")]
    public void Emit_LambdaVariableAvoidsCollision()
    {
        Emit("CustomerId = @customerId", CreatePlainBindings(), "e")
            .Lambda.Should()
            .StartWith("e1 => ");
    }

    /// <summary>NULL 非許容列への IS NULL が診断エラーになることを検証する（パーサ側ガード）</summary>
    [Fact(DisplayName = "NULL 非許容列への IS NULL は診断エラー")]
    public void Validate_NullCheckOnNonNullableColumn_Fails()
    {
        var result = QueryConditionParser.ParseAndValidate(
            "CustomerId IS NULL",
            _entity,
            _parameters
        );

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("CustomerId");
    }
}
