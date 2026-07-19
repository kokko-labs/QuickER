using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前付きクエリ（ErDiagram.Queries）から Repository メソッドが生成されることを、
/// 実経路（<see cref="DiagramCodeGenerator"/>＝型トークン解決込み）で検証するテストクラス
/// </summary>
public class QueryGenerationTests
{
    private readonly Entity _order;

    /// <summary>Order エンティティ（OrderId PK / CustomerId / Amount / Memo / CreatedAt）を用意する</summary>
    public QueryGenerationTests()
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
        _order.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsNullable = false,
            }
        );
        _order.Columns.Add(
            new Column
            {
                Name = "Amount",
                DataType = "decimal(12,2)",
                IsNullable = false,
            }
        );
        _order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });
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

    /// <summary>QuickER 版 Repository＋EF Core 併存の標準オプション</summary>
    private static CodeGenerationOptions CreateOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEfCore = true,
            IncludeDataAnnotations = true,
        };

    /// <summary>全ファイルの内容を連結して返す</summary>
    private static string AllContent(CodeGenerationResult result) =>
        string.Join("\n", result.Files.Select(file => file.Content));

    /// <summary>ミニ DSL の一覧クエリ（条件・並び順・ページング）が契約・QuickER 版 Repository・EF Core 版 Repository に同一本体で出ることを検証する</summary>
    [Fact(DisplayName = "DSL 一覧クエリ: 契約＋QuickER＋EF Core に同一の Query() 本体が出る")]
    public void Generate_DslListQuery_EmitsSharedBody()
    {
        var amountColumn = _order.Columns.First(c => c.Name == "Amount");
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByCustomer",
                Description = "顧客IDで注文を検索する",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
                OrderBy =
                {
                    new QueryOrdering { ColumnId = amountColumn.Id, Descending = true },
                },
                HasPaging = true,
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 契約（インターフェイスが一行 { } でなくブロック展開され、メソッド宣言を含む）
        content
            .Should()
            .Contain("public partial interface IOrderRepository : IRepository<OrderEntity, int>")
            .And.NotContain(
                "public partial interface IOrderRepository : IRepository<OrderEntity, int> { }"
            );
        content
            .Should()
            .Contain(
                "Task<IReadOnlyList<OrderEntity>> GetByCustomerAsync(int customerId, int take, int skip = 0, CancellationToken cancellationToken = default);"
            );

        // 共有本体（QuickER 版 Repository と EF Core の双方に同一テキスト）
        var body =
            "Query().Where(e => e.CustomerId == customerId).OrderByDescending(e => e.Amount).Skip(skip).Take(take).ToListAsync(cancellationToken)";
        content
            .Split(body)
            .Length.Should()
            .Be(3, "QuickER 版 Repository と EF Core 版 Repository の 2 箇所に同一本体が出る");

        // XML doc に説明が載る
        content.Should().Contain("/// <summary>顧客IDで注文を検索する</summary>");
    }

    /// <summary>単一・件数・文字列一致・IN の各形が生成されることを検証する</summary>
    [Fact(DisplayName = "DSL 単一/件数/LIKE/IN の各形が生成される")]
    public void Generate_DslShapes_EmitBodies()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "FindTop",
                Returns = QueryReturnShape.Single,
                OrderBy =
                {
                    new QueryOrdering
                    {
                        ColumnId = _order.Columns.First(c => c.Name == "Amount").Id,
                        Descending = true,
                    },
                },
            },
            new QueryDefinition
            {
                Name = "CountLarge",
                Returns = QueryReturnShape.Count,
                Parameters =
                {
                    new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
                },
                Condition = "Amount >= @minAmount",
            },
            new QueryDefinition
            {
                Name = "SearchMemo",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "keyword", Type = "string(50)" },
                },
                Condition = "Memo LIKE @keyword",
            },
            new QueryDefinition
            {
                Name = "GetByIds",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "ids",
                        Type = "int32",
                        IsList = true,
                    },
                },
                Condition = "OrderId IN @ids",
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain(
                "public Task<OrderEntity?> FindTopAsync(CancellationToken cancellationToken = default) =>"
            );
        content
            .Should()
            .Contain(
                "Query().OrderByDescending(e => e.Amount).FirstOrDefaultAsync(cancellationToken)"
            );
        content
            .Should()
            .Contain("Query().Where(e => e.Amount >= minAmount).CountAsync(cancellationToken)");
        content
            .Should()
            .Contain(
                "Query().Where(e => e.Memo!.Contains(keyword)).ToListAsync(cancellationToken)"
            );
        content
            .Should()
            .Contain("IReadOnlyList<int> ids")
            .And.Contain(
                "Query().Where(e => ids.Contains(e.OrderId)).ToListAsync(cancellationToken)"
            );
    }

    /// <summary>射影クエリ: DTO クラスと ToProjectionListAsync 本体が生成されることを検証する</summary>
    [Fact(DisplayName = "DSL 射影クエリ: DTO＋選択式が生成される")]
    public void Generate_DslProjection_EmitsDtoAndSelector()
    {
        var customerId = _order.Columns.First(c => c.Name == "CustomerId");
        var amount = _order.Columns.First(c => c.Name == "Amount");
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetSummaries",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderSummaryRow",
                Parameters =
                {
                    new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
                },
                Condition = "Amount >= @minAmount",
                Fields =
                {
                    new ProjectionField
                    {
                        Name = "CustomerId",
                        Type = "int32",
                        SourceColumnId = customerId.Id,
                    },
                    new ProjectionField
                    {
                        Name = "Amount",
                        Type = "decimal(12,2)",
                        SourceColumnId = amount.Id,
                    },
                },
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // DTO（settable＝寛容マッパー互換。NULL 許容は列から引き当てる＝両列とも非 NULL）
        content.Should().Contain("public sealed partial class OrderSummaryRow");
        content.Should().Contain("public int CustomerId { get; set; }");
        content.Should().Contain("public decimal Amount { get; set; }");

        // 本体（選択式つき射影終端）
        content
            .Should()
            .Contain(
                ".ToProjectionListAsync(e => new OrderSummaryRow { CustomerId = e.CustomerId, Amount = e.Amount }, cancellationToken)"
            );
    }

    /// <summary>
    /// 射影 DTO の NULL 許容が「列参照＝列の NULL 許容・自由フィールド＝既定 NULL 許容・明示指定＝優先」で
    /// 引き当てられ、非 NULL の参照型には null! 初期化子が付くことを検証する（C-2）。
    /// </summary>
    [Fact(DisplayName = "射影 DTO の NULL 許容は列参照・自由・明示指定で正しく引き当てる")]
    public void Generate_ProjectionDto_DerivesNullabilityFromColumns()
    {
        var customerId = _order.Columns.First(c => c.Name == "CustomerId");
        var memo = _order.Columns.First(c => c.Name == "Memo");
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetRows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT ..." },
                Fields =
                {
                    // 列参照×非 NULL 列 → 非 NULL の値型
                    new ProjectionField { Name = "CustomerId", SourceColumnId = customerId.Id },
                    // 列参照×NULL 許容列 → NULL 許容の参照型
                    new ProjectionField { Name = "Memo", SourceColumnId = memo.Id },
                    // 列参照×明示 IsNullable=true → 列が非 NULL でも NULL 許容へ上書き
                    new ProjectionField
                    {
                        Name = "OptionalId",
                        SourceColumnId = customerId.Id,
                        IsNullable = true,
                    },
                    // 自由フィールド → 既定で NULL 許容（寛容マッパーの列欠落・集計 NULL を安全に受ける）
                    new ProjectionField { Name = "Total", Type = "decimal(12,2)" },
                    // 自由フィールド×明示 IsNullable=false → 非 NULL（参照型は null! 初期化）
                    new ProjectionField
                    {
                        Name = "Label",
                        Type = "string(50)",
                        IsNullable = false,
                    },
                },
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content.Should().Contain("public int CustomerId { get; set; }");
        content.Should().Contain("public string? Memo { get; set; }");
        content.Should().Contain("public int? OptionalId { get; set; }");
        content.Should().Contain("public decimal? Total { get; set; }");
        content.Should().Contain("public string Label { get; set; } = null!;");
    }

    /// <summary>
    /// ダングリング Guid 参照（存在しないエンティティ・列参照）のクエリ定義は、生成前の整合性検証で
    /// ローカライズ済みの警告としてスキップされ、他のクエリと生成全体は継続することを検証する（C-5）。
    /// </summary>
    [Fact(DisplayName = "ダングリング Guid 参照のクエリは警告でスキップし生成は継続する")]
    public void Generate_DanglingGuidReferences_WarnAndSkip()
    {
        var missingColumnId = Guid.NewGuid();
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "ValidQuery",
                Returns = QueryReturnShape.Count,
                Condition = "Amount > 0",
            },
            new QueryDefinition
            {
                Name = "DanglingParameter",
                Parameters =
                {
                    new QueryParameter { Name = "typedId", SourceColumnId = missingColumnId },
                },
            },
            new QueryDefinition
            {
                Name = "DanglingField",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "DanglingRow",
                Fields =
                {
                    new ProjectionField { Name = "Ghost", SourceColumnId = missingColumnId },
                },
            },
            new QueryDefinition
            {
                Name = "DanglingOrderBy",
                OrderBy = { new QueryOrdering { ColumnId = missingColumnId } },
            }
        );

        // 存在しないエンティティを参照するクエリ（削除済みエンティティの残骸）
        diagram.Queries.Add(new QueryDefinition { EntityId = Guid.NewGuid(), Name = "Orphan" });

        var result = Generate(diagram, CreateOptions());

        // すべて警告（エラーなし）でファイルは生成され、有効なクエリだけが出力される
        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result.Files.Should().NotBeEmpty();
        result
            .Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Warning)
            .Should()
            .HaveCount(4);
        result.Diagnostics.Select(d => d.Message).Should().Contain(m => m.Contains("typedId"));
        result.Diagnostics.Select(d => d.Message).Should().Contain(m => m.Contains("Ghost"));
        result
            .Diagnostics.Select(d => d.Message)
            .Should()
            .Contain(m => m.Contains("DanglingOrderBy"));
        result.Diagnostics.Select(d => d.Message).Should().Contain(m => m.Contains("Orphan"));

        var content = AllContent(result);
        content.Should().Contain("ValidQueryAsync(");
        content
            .Should()
            .NotContain("DanglingParameterAsync(")
            .And.NotContain("DanglingFieldAsync(")
            .And.NotContain("DanglingOrderByAsync(")
            .And.NotContain("OrphanAsync(");
    }

    /// <summary>自由 SQL: QuickER 版 Repository のみ実装され、EF Core 版 Repository は契約宣言のみ（manual 扱い）になることを検証する</summary>
    [Fact(DisplayName = "自由 SQL: QuickER のみ実装・EF Core 版 Repository は manual 扱い")]
    public void Generate_SqlQuery_AdoOnlyAndEfManual()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetRecent",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlserver"] = "SELECT * FROM [Order] WHERE [Amount] > 0",
                    ["sqlite"] = "SELECT * FROM \"Order\" WHERE \"Amount\" > 0",
                },
            },
            new QueryDefinition
            {
                Name = "SumAmount",
                Returns = QueryReturnShape.Scalar,
                ScalarType = "decimal(12,2)",
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT SUM([Amount]) FROM [Order]" },
            },
            new QueryDefinition
            {
                Name = "SpecialLookup",
                Returns = QueryReturnShape.Single,
                Implementation = QueryImplementationKind.Manual,
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 契約には 3 メソッドとも宣言される（manual 注記つき）
        content.Should().Contain("Task<IReadOnlyList<OrderEntity>> GetRecentAsync(");
        content.Should().Contain("Task<decimal?> SumAmountAsync(");
        content.Should().Contain("Task<OrderEntity?> SpecialLookupAsync(");
        content.Should().Contain("Implementation targets that do not get a generated body");

        // QuickER 版 Repository（sqlserver 方言）には SQL 入り本体が出る
        content.Should().Contain("QueryBySqlAsync(");
        content.Should().Contain("@\"SELECT * FROM [Order] WHERE [Amount] > 0\"");
        content.Should().Contain("ExecuteScalarSqlAsync<decimal?>(");

        // EF Core 版 Repository クラスには自由 SQL・manual の実装が出ない（クラス本体に SQL 文字列が含まれない）
        var efClass = ExtractClassBody(content, "EfCoreOrderRepository");
        efClass.Should().NotContain("QueryBySqlAsync").And.NotContain("SpecialLookupAsync");
    }

    /// <summary>
    /// 自由 SQL の静的検証: 未宣言パラメータのクエリは警告＋該当クエリのみスキップされ、
    /// 有効なクエリは生成される（生成全体は成功する）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "生 SQL 未宣言パラメータ: 警告＋該当クエリのみスキップ")]
    public void Generate_RawSqlUndeclaredParameter_WarnsAndSkipsQuery()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetGood",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Sql = { ["sqlserver"] = "SELECT * FROM [Order] WHERE [CustomerId] = @customerId" },
            },
            new QueryDefinition
            {
                Name = "GetBad",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                // @ghost は宣言に無い → 実行時に必ず失敗するため該当クエリをスキップ
                Sql = { ["sqlserver"] = "SELECT * FROM [Order] WHERE [X] = @ghost" },
            }
        );

        var result = Generate(diagram, CreateOptions());

        // 警告のみ（エラーなし）で生成は成功する
        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning
                && d.Message.Contains("GetBad")
                && d.Message.Contains("ghost")
            );

        var content = AllContent(result);
        // 有効なクエリは生成され、未宣言パラメータのクエリだけがスキップされる
        content.Should().Contain("GetGoodAsync(");
        content.Should().NotContain("GetBadAsync(");
    }

    /// <summary>自由 SQL の静的検証: 未使用パラメータは警告のみで生成が継続することを検証する</summary>
    [Fact(DisplayName = "生 SQL 未使用パラメータ: 警告のみで生成継続")]
    public void Generate_RawSqlUnusedParameter_WarnsButGenerates()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "ListEverything",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Parameters =
                {
                    new QueryParameter { Name = "unused", Type = "int32" },
                },
                Sql = { ["sqlserver"] = "SELECT * FROM [Order]" },
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning && d.Message.Contains("unused")
            );

        // 警告のみ＝クエリは生成される
        AllContent(result).Should().Contain("ListEverythingAsync(");
    }

    /// <summary>自由 SQL の静的検証: 複文は警告のみで生成が継続することを検証する</summary>
    [Fact(DisplayName = "生 SQL 複文: 警告のみで生成継続")]
    public void Generate_RawSqlMultipleStatements_WarnsButGenerates()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetTwo",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT * FROM [Order]; SELECT * FROM [Order]" },
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning && d.Message.Contains("GetTwo")
            );

        AllContent(result).Should().Contain("GetTwoAsync(");
    }

    /// <summary>マルチターゲット: 共有 DSL 本体は両方言に、方言 SQL は該当方言のみに出ることを検証する</summary>
    [Fact(DisplayName = "マルチターゲット: DSL は両方言・SQL は該当方言のみ")]
    public void Generate_MultiTarget_DispatchesByDialect()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
            },
            new QueryDefinition
            {
                Name = "ServerOnly",
                Returns = QueryReturnShape.Count,
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT COUNT(*) FROM [Order]" },
            }
        );

        var options = new CodeGenerationOptions
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            IncludeDataAnnotations = true,
        };
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            new Dictionary<string, IColumnTypeMapper> { ["sqlserver"] = provider.TypeMapper },
            diagram,
            options
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 共有 DSL 本体は sqlserver / sqlite の両実装に出る（契約 1＋方言実装 2 で計 2 回）
        var sharedBody =
            "Query().Where(e => e.CustomerId == customerId).ToListAsync(cancellationToken)";
        content.Split(sharedBody).Length.Should().Be(3);

        // sqlserver 限定の SQL 本体は 1 回だけ
        content.Split("SELECT COUNT(*) FROM [Order]").Length.Should().Be(2);
    }

    /// <summary>VO 生成時: 比較は VO.Create で包まれ、IN は前置文で持ち上げられることを検証する</summary>
    [Fact(DisplayName = "VO 図: 比較は VO.Create・IN は前置文つき本体になる")]
    public void Generate_WithValueObjects_WrapsComparisons()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
            },
            new QueryDefinition
            {
                Name = "GetByIds",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "ids",
                        Type = "int32",
                        IsList = true,
                    },
                },
                Condition = "OrderId IN @ids",
            }
        );

        var options = new CodeGenerationOptions
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEfCore = true,
            IncludeDataAnnotations = true,
            GenerateValueObjects = true,
        };
        var result = Generate(diagram, options);

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content.Should().Contain("e.CustomerId == CustomerIdValue.Create(customerId)");
        content
            .Should()
            .Contain("var idsValues = ids.Select(OrderIdValue.Create).ToList();")
            .And.Contain("idsValues.Contains(e.OrderId)");
    }

    /// <summary>列参照で型付けしたパラメータが、VO 図では VO 型引数＋直接比較・VO なし図ではプリミティブ引数になることを検証する</summary>
    [Fact(DisplayName = "列参照パラメータ: VO 図は VO 型引数・VO なし図はプリミティブ引数")]
    public void Generate_ColumnTypedParameter_FollowsColumnType()
    {
        var customerColumn = _order.Columns.First(c => c.Name == "CustomerId");

        QueryDefinition CreateQuery() =>
            new()
            {
                Name = "GetByCustomerTyped",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", SourceColumnId = customerColumn.Id },
                },
                Condition = "CustomerId = @customerId",
            };

        // VO 有効: 引数が VO 型になり、条件は Create なしの直接比較
        var voOptions = new CodeGenerationOptions
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEfCore = true,
            IncludeDataAnnotations = true,
            GenerateValueObjects = true,
        };
        var voResult = Generate(CreateDiagram(CreateQuery()), voOptions);
        voResult.HasErrors.Should().BeFalse(FormatDiagnostics(voResult));
        var voContent = AllContent(voResult);
        voContent
            .Should()
            .Contain("GetByCustomerTypedAsync(CustomerIdValue customerId,")
            .And.Contain("Query().Where(e => e.CustomerId == customerId)");

        // VO 無効: 引数は列のプリミティブ型
        var plainResult = Generate(CreateDiagram(CreateQuery()), CreateOptions());
        plainResult.HasErrors.Should().BeFalse(FormatDiagnostics(plainResult));
        AllContent(plainResult).Should().Contain("GetByCustomerTypedAsync(int customerId,");
    }

    /// <summary>検証エラー（未知の列・スカラー×DSL・重複メソッド名）が診断エラーになりファイルが出ないことを検証する</summary>
    [Fact(DisplayName = "検証エラーは診断になりファイルを出さない")]
    public void Generate_InvalidQueries_ProduceDiagnostics()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "BadColumn",
                Returns = QueryReturnShape.List,
                Condition = "Nope = 1",
            },
            new QueryDefinition
            {
                Name = "BadScalar",
                Returns = QueryReturnShape.Scalar,
                ScalarType = "int32",
                Implementation = QueryImplementationKind.Dsl,
            },
            new QueryDefinition { Name = "Dup", Returns = QueryReturnShape.List },
            new QueryDefinition { Name = "Dup", Returns = QueryReturnShape.Count }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result.Diagnostics.Select(d => d.Message).Should().Contain(m => m.Contains("Nope"));
        result.Diagnostics.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    /// <summary>クエリなしの図では契約が従来どおり一行 { } のままであることを検証する（バイト不変の要）</summary>
    [Fact(DisplayName = "クエリなしの図は契約が一行 { } のまま")]
    public void Generate_NoQueries_KeepsOneLinerInterface()
    {
        var result = Generate(CreateDiagram(), CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        AllContent(result)
            .Should()
            .Contain(
                "public partial interface IOrderRepository : IRepository<OrderEntity, int> { }"
            );
    }

    /// <summary>クラス本体（宣言行から対応する閉じブレースまで）を素朴に取り出す</summary>
    private static string ExtractClassBody(string content, string className)
    {
        var start = content.IndexOf($"class {className}", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"クラス {className} が生成されている前提");
        var end = content.IndexOf("\npublic ", start, StringComparison.Ordinal);
        return end > start ? content[start..end] : content[start..];
    }

    /// <summary>診断メッセージを失敗理由として整形する</summary>
    private static string FormatDiagnostics(CodeGenerationResult result) =>
        string.Join(" / ", result.Diagnostics.Select(d => d.Message));
}
