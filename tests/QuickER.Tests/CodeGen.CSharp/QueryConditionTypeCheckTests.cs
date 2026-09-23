using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前付きクエリ条件（ミニ DSL）の型整合検査を、実経路（<see cref="DiagramCodeGenerator"/>＝型トークン
/// 解決込み）で検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 固定する規則は 2 つ。(1) <b>エミットされた C# が必ずコンパイルエラーになる組み合わせは Warning 診断＋
/// 該当クエリだけスキップ</b>（Guid 参照切れ・生 SQL の未宣言パラメータと同じ流儀。従来は診断ゼロで生成され、
/// 1 クエリの誤りで生成アセンブリ全体が CS0019 等で落ちていた）。(2) <b>現在コンパイルも翻訳も通っている形は
/// 新たに拒否しない</b>（整数列 × 小数リテラルの暗黙拡大・列参照で型付けした @パラメータ・decimal 内包 VO ×
/// 小数リテラルなど）。
/// </para>
/// <para>
/// 受理側は Roslyn 実コンパイルまで通し、「受理したものは本当にコンパイルできる」ことも同時に固定する。
/// </para>
/// </remarks>
public class QueryConditionTypeCheckTests
{
    private readonly Entity _order;
    private readonly Column _name;
    private readonly Column _qty;
    private readonly Column _big;
    private readonly Column _amount;
    private readonly Column _seenAt;
    private readonly Column _flag;
    private readonly Column _quantity;

    /// <summary>型ファミリを網羅する Order エンティティを用意する</summary>
    public QueryConditionTypeCheckTests()
    {
        _order = new Entity { TableName = "Order" };
        _order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        _name = new Column
        {
            Name = "Name",
            DataType = "nvarchar(50)",
            IsNullable = false,
        };
        _qty = new Column
        {
            Name = "Qty",
            DataType = "int",
            IsNullable = false,
        };
        _big = new Column
        {
            Name = "Big",
            DataType = "bigint",
            IsNullable = false,
        };
        _amount = new Column
        {
            Name = "Amount",
            DataType = "decimal(12,2)",
            IsNullable = false,
        };
        _seenAt = new Column
        {
            Name = "SeenAt",
            DataType = "datetime2",
            IsNullable = false,
        };
        _flag = new Column
        {
            Name = "Flag",
            DataType = "bit",
            IsNullable = false,
        };
        _quantity = new Column
        {
            Name = "Quantity",
            DataType = "int",
            IsNullable = true,
        };
        _order.Columns.Add(_name);
        _order.Columns.Add(_qty);
        _order.Columns.Add(_big);
        _order.Columns.Add(_amount);
        _order.Columns.Add(_seenAt);
        _order.Columns.Add(_flag);
        _order.Columns.Add(_quantity);
    }

    /// <summary>クエリ定義付きの図を作る</summary>
    private ErDiagram CreateDiagram(params QueryDefinition[] queries)
    {
        var diagram = new ErDiagram { Entities = { _order } };

        foreach (var query in queries)
        {
            query.EntityId = _order.Id;
            diagram.Queries.Add(query);
        }

        return diagram;
    }

    /// <summary>実経路（SqlServer プロバイダ）で生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram, CodeGenerationOptions options)
    {
        var provider = new SqlServerProvider();
        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            options
        );
    }

    /// <summary>QuickER 版 Repository のみの標準オプション</summary>
    private static CodeGenerationOptions CreateOptions(bool valueObjects = false) =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            IncludeDataAnnotations = true,
            GenerateValueObjects = valueObjects,
        };

    /// <summary>一覧クエリ定義の糖衣</summary>
    private static QueryDefinition ListQuery(
        string name,
        string condition,
        params QueryParameter[] parameters
    )
    {
        var query = new QueryDefinition
        {
            Name = name,
            Returns = QueryReturnShape.List,
            Condition = condition,
        };

        foreach (var parameter in parameters)
        {
            query.Parameters.Add(parameter);
        }

        return query;
    }

    // ===== 拒否側（コンパイル不能な組み合わせ＝Error＋該当クエリだけスキップ） =====

    public static TheoryData<string, string, QueryParameter[]> RejectedConditions =>
        new()
        {
            // string 列への順序比較（e.Name > "M" は CS0019）
            { "BadOrder", "Name > 'M'", [] },
            // 数値列への文字列一致（int に Contains は無い＝CS1061）
            { "BadContains", "Qty CONTAINS '1'", [] },
            // 日時列への文字列リテラル比較（DateTime == string は CS0019）
            { "BadDate", "SeenAt > '2026-01-01'", [] },
            // bool 列への数値リテラル比較（bool == int は CS0019）
            { "BadFlag", "Flag = 1", [] },
            // long 列への小数リテラル（サフィックス L が付いて "1.5L"＝字句エラー）
            { "BadLong", "Big > 1.5", [] },
            // string 列の CONTAINS へ int トークン引数（string.Contains(int) は CS1503）
            {
                "BadMatchParam",
                "Name CONTAINS @num",
                [new QueryParameter { Name = "num", Type = "int32" }]
            },
            // string 列の IN へ int リスト（Contains の型推論が一致しない）
            {
                "BadIn",
                "Name IN @ids",
                [
                    new QueryParameter
                    {
                        Name = "ids",
                        Type = "int32",
                        IsList = true,
                    },
                ]
            },
            // NULL 許容 int 列の IN へ long リスト（Cast<int?> の持ち上げが実行時 InvalidCastException）
            {
                "BadNullableIn",
                "Quantity IN @bigs",
                [
                    new QueryParameter
                    {
                        Name = "bigs",
                        Type = "int64",
                        IsList = true,
                    },
                ]
            },
            // 日時列 × int トークン引数（DateTime == int は CS0019）
            {
                "BadDateParam",
                "SeenAt = @num",
                [new QueryParameter { Name = "num", Type = "int32" }]
            },
        };

    [Theory(DisplayName = "コンパイル不能な型不整合は Warning になり該当クエリだけスキップされる")]
    [MemberData(nameof(RejectedConditions))]
    public void Generate_IncompatibleCondition_IsRejectedAndOnlyThatQuerySkipped(
        string queryName,
        string condition,
        QueryParameter[] parameters
    )
    {
        var diagram = CreateDiagram(
            ListQuery(queryName, condition, parameters),
            // 対照: 正当なクエリは巻き添えにしない
            ListQuery("GetByQty", "Qty = @qty", new QueryParameter { Name = "qty", Type = "int32" })
        );

        var result = Generate(diagram, CreateOptions());

        // Warning ＋該当クエリのみスキップ（Error は生成全体を止めるため使わない＝Guid 参照切れと同じ流儀）
        result.HasErrors.Should().BeFalse("型不整合はクエリ単位のスキップで生成全体は完走する");
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning && d.Message.Contains(queryName)
            );

        var content = string.Join("\n", result.Files.Select(f => f.Content));

        content.Should().NotContain($"{queryName}Async", "違反したクエリだけがスキップされる");
        content.Should().Contain("GetByQtyAsync", "正当なクエリは生成が続く");

        GeneratedCodeCompiler
            .Compile(result, $"QueryTypeCheckSkip{queryName}")
            .Success.Should()
            .BeTrue("違反クエリをスキップした残りの生成物はコンパイルできるべき");
    }

    /// <summary>VO 有効の図でだけ成立する拒否ケース（整数内包 VO × 小数リテラル・別 VO 引数）</summary>
    [Theory(DisplayName = "VO 有効: 整数内包 VO への小数リテラルと別 VO 引数は拒否される")]
    [InlineData("BadVoFraction", "Qty = 1.5")]
    [InlineData("BadVoParam", "Qty = @amount")]
    public void Generate_ValueObjectIncompatibleCondition_IsRejected(
        string queryName,
        string condition
    )
    {
        var query = ListQuery(queryName, condition);

        if (condition.Contains("@amount"))
        {
            // 列参照型付け＝AmountValue（decimal 内包）で型付けした引数を int 内包の QtyValue 列と比較する
            query.Parameters.Add(
                new QueryParameter { Name = "amount", SourceColumnId = _amount.Id }
            );
        }

        var result = Generate(CreateDiagram(query), CreateOptions(valueObjects: true));

        result.HasErrors.Should().BeFalse();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning && d.Message.Contains(queryName)
            );
        string.Join("\n", result.Files.Select(f => f.Content))
            .Should()
            .NotContain($"{queryName}Async", "違反したクエリはスキップされる");
    }

    // ===== 受理側（現在コンパイルも翻訳も通っている形は新たに拒否しない） =====

    [Fact(DisplayName = "現在コンパイルできる形は拒否されず、生成物は実コンパイルできる")]
    public void Generate_CompatibleConditions_AreAcceptedAndCompile()
    {
        var diagram = CreateDiagram(
            // (a) 整数列 × 小数リテラル＝double への暗黙拡大で通る（long 以外）
            ListQuery("FracOnInt", "Qty > 1.5"),
            // (b) 列参照で型付けした引数は列と同型
            ListQuery(
                "ColRefParam",
                "Qty = @qty",
                new QueryParameter { Name = "qty", SourceColumnId = _qty.Id }
            ),
            // decimal 列 × 小数・整数リテラル（サフィックス m）
            ListQuery("DecimalLiterals", "Amount >= 1.5 AND Amount < 100"),
            // 日時列 × datetime トークン引数
            ListQuery(
                "DateParam",
                "SeenAt >= @since",
                new QueryParameter { Name = "since", Type = "datetime" }
            ),
            // bool 列 × boolean トークン引数
            ListQuery(
                "FlagParam",
                "Flag = @flag",
                new QueryParameter { Name = "flag", Type = "boolean" }
            ),
            // long 列 × 整数リテラル（サフィックス L は整数なら合法）
            ListQuery("LongIntegral", "Big = 2"),
            // int 列（非 NULL 許容）の IN × long リスト（int → long の暗黙変換で Contains が成立）
            ListQuery(
                "WideIn",
                "Qty IN @bigs",
                new QueryParameter
                {
                    Name = "bigs",
                    Type = "int64",
                    IsList = true,
                }
            ),
            // 文字列一致（リテラル・string トークン引数・LIKE）
            ListQuery(
                "TextMatch",
                "Name CONTAINS 'x' OR Name LIKE @keyword",
                new QueryParameter { Name = "keyword", Type = "string(50)" }
            )
        );

        var result = Generate(diagram, CreateOptions());

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var compilation = GeneratedCodeCompiler.Compile(result, "QueryTypeCheckAccepted");

        compilation
            .Success.Should()
            .BeTrue(
                "受理した条件はすべてコンパイルできるべき: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }

    /// <summary>VO 有効の図で受理される形（decimal 内包 VO × 小数リテラル・同一 VO 引数・VO 文字列一致）</summary>
    [Fact(DisplayName = "VO 有効: decimal 内包 VO × 小数リテラルと同一 VO 引数は受理される")]
    public void Generate_ValueObjectCompatibleConditions_AreAcceptedAndCompile()
    {
        var diagram = CreateDiagram(
            // (c) VO 列 × 小数リテラルは内包型が decimal なら許容（Create(1.5m)）
            ListQuery("VoFraction", "Amount > 1.5"),
            // 同一 VO で型付けした引数は直接比較
            ListQuery(
                "VoParam",
                "Amount = @amount",
                new QueryParameter { Name = "amount", SourceColumnId = _amount.Id }
            ),
            // 整数内包 VO × 整数リテラル（Create(1) は合法）
            ListQuery("VoIntegral", "Qty = 1"),
            // 文字列内包 VO の文字列一致と順序なし比較
            ListQuery("VoText", "Name CONTAINS 'x' AND Name = 'exact'")
        );

        var result = Generate(diagram, CreateOptions(valueObjects: true));

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var compilation = GeneratedCodeCompiler.Compile(result, "QueryTypeCheckVoAccepted");

        compilation
            .Success.Should()
            .BeTrue(
                "受理した条件はすべてコンパイルできるべき: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }
}
