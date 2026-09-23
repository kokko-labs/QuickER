using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

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
            GenerateEfCoreRepositories = true,
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
                "Query().Where(e => (e.Memo != null && e.Memo!.Contains(keyword))).ToListAsync(cancellationToken)"
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

        // 選択式は static readonly フィールドへ巻き上げる（メソッド内のラムダは呼び出しのたびに新しい式ツリー
        // インスタンスになり、実行器の参照同一性キーの compile-once キャッシュが一度もヒットしないため）
        content
            .Should()
            .Contain(
                "private static readonly Expression<Func<OrderEntity, OrderSummaryRow>> _getSummariesAsyncSelector = e => new OrderSummaryRow { CustomerId = e.CustomerId, Amount = e.Amount };"
            );

        // 本体（射影終端は巻き上げたフィールドを渡す）
        content
            .Should()
            .Contain(".ToProjectionListAsync(_getSummariesAsyncSelector, cancellationToken)");

        // 巻き上げた以上、選択式がメソッド本体へ直書きされて残っていてはならない
        content
            .Should()
            .NotContain(
                ".ToProjectionListAsync(e => new OrderSummaryRow",
                "選択式の直書きが残ると呼び出しごとに新しい式ツリーになり、compile-once キャッシュが効かない"
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
            GenerateEfCoreRepositories = true,
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
            GenerateEfCoreRepositories = true,
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

    /// <summary>
    /// クエリなしの図でも契約にクエリメソッドが現れないことを検証する
    /// （契約本体は重複事前チェック <c>CheckUniquenessAsync</c> のみ＝常時出力される固定メンバー）。
    /// </summary>
    [Fact(DisplayName = "クエリなしの図の契約は重複事前チェックのみを持つ")]
    public void Generate_NoQueries_KeepsContractFreeOfQueryMembers()
    {
        var result = Generate(CreateDiagram(), CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));

        var content = AllContent(result);
        content
            .Should()
            .Contain("public partial interface IOrderRepository : IRepository<OrderEntity, int>");
        content.Should().Contain("Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(");
    }

    /// <summary>クラス本体（宣言行から対応する閉じブレースまで）を素朴に取り出す</summary>
    private static string ExtractClassBody(string content, string className)
    {
        var start = content.IndexOf($"class {className}", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"クラス {className} が生成されている前提");
        var end = content.IndexOf("\npublic ", start, StringComparison.Ordinal);
        return end > start ? content[start..end] : content[start..];
    }

    /// <summary>
    /// NULL 許容の値型列への IN が、メソッド冒頭で NULL 許容型のリストへ持ち上げてから比較されることを
    /// 検証する。素のままだと <c>IReadOnlyList&lt;int&gt;.Contains(int?)</c> の型推論が一致せず
    /// CS1929 でコンパイルできない（VO リストの持ち上げと同じ経路。列が NULL の行は
    /// <c>Contains(null)</c>＝false で SQL / EF Core と揃う）
    /// </summary>
    [Fact(DisplayName = "NULL 許容の値型列への IN は NULL 許容型リストへ持ち上げる")]
    public void Generate_InOnNullableValueTypeColumn_LiftsToNullableList()
    {
        _order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByQuantities",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "quantities",
                        Type = "int32",
                        IsList = true,
                    },
                },
                Condition = "Quantity IN @quantities",
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain("var quantitiesValues = quantities.Cast<int?>().ToList();")
            .And.Contain("quantitiesValues.Contains(e.Quantity)");

        var compilation = GeneratedCodeCompiler.Compile(result, "QueryNullableIn");

        compilation
            .Success.Should()
            .BeTrue(
                "NULL 許容値型列への IN はコンパイルできるべき: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }

    /// <summary>
    /// 否定 LIKE の等値形（ワイルドカードなしの <c>NOT LIKE 'abc'</c>）と前置 <c>NOT</c> の文字列一致が、
    /// どちらも NULL 前提の内側で否定されることを検証する（docs の「NOT LIKE も NULL 前提の内側＝NULL 行は
    /// どちらの向きでも一致しない」に一致させる）。従来は等値の <c>!=</c> 経路・<c>!(...)</c> 包みへ落ちて
    /// NULL 行が一致に含まれ、<c>col NOT LIKE '%x%'</c> と結果が割れていた。肯定形の <c>LIKE 'abc'</c> は
    /// 従来どおり素の等値比較（NULL 行は一致しない＝結論は同じ）
    /// </summary>
    [Fact(DisplayName = "否定 LIKE の等値形と前置 NOT も NULL 前提の内側で否定される")]
    public void Generate_NegatedLike_StaysInsideNullPremise()
    {
        static QueryDefinition ListQuery(string name, string condition) =>
            new()
            {
                Name = name,
                Returns = QueryReturnShape.List,
                Condition = condition,
            };

        _order.Columns.Add(
            new Column
            {
                Name = "Code",
                DataType = "nvarchar(20)",
                IsNullable = false,
            }
        );
        var diagram = CreateDiagram(
            ListQuery("ExactNegated", "Memo NOT LIKE 'abc'"),
            ListQuery("ExactNegatedNotNull", "Code NOT LIKE 'abc'"),
            ListQuery("PrefixNot", "NOT Memo LIKE '%x%'"),
            ListQuery("DoubleNot", "NOT Memo NOT LIKE '%x%'"),
            ListQuery("ExactPositive", "Memo LIKE 'abc'")
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 等値形の否定: NULL 前提の内側（NULL 行はどちらの向きでも一致しない）
        content.Should().Contain("(e.Memo != null && !(e.Memo == \"abc\"))");
        // 非 NULL 列は前提が不要（従来どおり素の否定）
        content.Should().Contain("!(e.Code == \"abc\")");
        // 前置 NOT は Negated へ畳み込まれ、col NOT LIKE と同じ形になる
        content.Should().Contain("(e.Memo != null && !(e.Memo!.Contains(\"x\")))");
        // 二重否定は肯定形へ畳まれる（前提は保たれる）
        content.Should().Contain("(e.Memo != null && e.Memo!.Contains(\"x\"))");
        // 肯定形のワイルドカードなし LIKE は従来どおり素の等値比較
        content
            .Should()
            .Contain("e.Memo == \"abc\"")
            .And.NotContain("e.Memo != null && e.Memo == \"abc\"");
    }

    /// <summary>
    /// VO 有効 × NULL 許容列への順序比較が「列が NULL でないこと」を AND した形へエミットされることを
    /// 検証する（文字列一致の NULL 前提と同じ流儀）。VO の比較演算子は null を最小として順序付けるため、
    /// 素のままだとインメモリ実行器だけが &lt; / &lt;= で NULL 行を返し、SQL・EF Core（UNKNOWN で脱落）と
    /// 観測結果が割れていた。等値比較は従来どおりガードなし（null == 値 は C# でも false＝3 実装先で一致）
    /// </summary>
    [Fact(DisplayName = "VO 有効: NULL 許容列への順序比較は null ガードの内側にエミットされる")]
    public void Generate_NullableVoOrderedComparison_EmitsNullGuard()
    {
        _order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetBelow",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "limit", Type = "int32" },
                },
                Condition = "Quantity < @limit",
            },
            new QueryDefinition
            {
                Name = "GetExact",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "exact", Type = "int32" },
                },
                Condition = "Quantity = @exact",
            }
        );

        var result = Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                GenerateValueObjects = true,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain("(e.Quantity != null && e.Quantity < QuantityValue.Create(limit))");
        content
            .Should()
            .Contain("e.Quantity == QuantityValue.Create(exact)")
            .And.NotContain("e.Quantity != null && e.Quantity ==");
    }

    /// <summary>
    /// リモートエンドポイントの操作名と衝突するクエリ名が予約名として拒否されることを検証する。
    /// ルート（<c>POST {prefix}/{エンティティ}/{操作}</c>）が重なると同じパスへハンドラが 2 本張られ
    /// 実行時に AmbiguousMatch の 500 になるため、C# メンバーと衝突しない名前も予約する
    /// </summary>
    [Theory(DisplayName = "リモート操作名と衝突するクエリ名は予約名として拒否される")]
    [InlineData("SaveMany")]
    [InlineData("Ping")]
    [InlineData("SyncCeiling")]
    [InlineData("SyncChanges")]
    [InlineData("SyncKeys")]
    [InlineData("SyncPage")]
    [InlineData("savemany")] // ルートは大小を区別しないため、大小違いの綴りも同じパスに化ける
    public void Generate_QueryNamedAfterRemoteOperation_IsRejected(string queryName)
    {
        var diagram = CreateDiagram(
            new QueryDefinition { Name = queryName, Returns = QueryReturnShape.List }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error && d.Message.Contains(queryName)
            );
    }

    /// <summary>
    /// 大小だけ違うクエリ名の 2 本目が重複として拒否されることを検証する。C# メンバーとしては共存できるが、
    /// リモートのルートは大小を区別しないため同じパスへの 2 本目のハンドラになる
    /// </summary>
    [Fact(DisplayName = "大小だけ違うクエリ名は重複として拒否される")]
    public void Generate_QueriesDifferingOnlyInCase_SecondIsRejected()
    {
        var diagram = CreateDiagram(
            new QueryDefinition { Name = "FindStuff", Returns = QueryReturnShape.List },
            new QueryDefinition { Name = "findstuff", Returns = QueryReturnShape.List }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error && d.Message.Contains("findstuff")
            );
    }

    /// <summary>
    /// C# の予約語のパラメータ名と、契約メンバー Query() を隠すパラメータ名「Query」が診断エラーで
    /// 拒否されることを検証する。生成器は識別子を @ エスケープせずそのまま出力するため、予約語は
    /// 引数宣言がコンパイル不能になり、Query（大文字小文字まで一致）は生成メソッド本体の Query() 呼び出しが
    /// メソッドグループを隠されてコンパイル不能になる
    /// </summary>
    [Theory(DisplayName = "予約語・Query のパラメータ名は診断エラーで拒否される")]
    [InlineData("class")]
    [InlineData("int")]
    [InlineData("Query")]
    public void Generate_ReservedParameterName_IsRejected(string parameterName)
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "FindReserved",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = parameterName, Type = "int32" },
                },
                Condition = $"CustomerId = @{parameterName}",
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message.Contains(parameterName)
            );
    }

    /// <summary>
    /// 小文字の query は Query() メソッドグループを隠さないため従来どおり使えることを検証する
    /// （拒否は大文字小文字まで一致する「Query」だけ＝文脈キーワードも予約語ではないので合法）
    /// </summary>
    [Fact(DisplayName = "小文字の query パラメータ名は従来どおり使える")]
    public void Generate_LowercaseQueryParameterName_IsAccepted()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "FindByQueryWord",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "query", Type = "string(50)" },
                },
                Condition = "Memo LIKE @query",
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));

        var compilation = GeneratedCodeCompiler.Compile(result, "QueryLowercaseParam");

        compilation
            .Success.Should()
            .BeTrue(
                "小文字の query は Query() を隠さずコンパイルできるべき: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }

    /// <summary>
    /// 射影 DTO の結果型名と同名のフィールドが診断エラーで拒否されることを検証する
    /// （C# はメンバー名を外側の型名と同じにできない＝素通しすると CS0542 で生成物全体が壊れる）
    /// </summary>
    [Fact(DisplayName = "結果型名と同名の射影フィールドは診断エラーで拒否される")]
    public void Generate_ProjectionFieldNamedAfterResultType_IsRejected()
    {
        var amount = _order.Columns.First(c => c.Name == "Amount");
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetClashRows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "ClashRow",
                Fields =
                {
                    new ProjectionField
                    {
                        Name = "ClashRow",
                        Type = "decimal(12,2)",
                        SourceColumnId = amount.Id,
                    },
                },
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error && d.Message.Contains("ClashRow")
            );
    }

    /// <summary>
    /// IN の持ち上げリスト変数名（{パラメータ名}Values）が別のパラメータ名と衝突するとき、_ を後置して
    /// 回避することを検証する（衝突したままだとローカル変数が引数を隠して CS0136 になる）
    /// </summary>
    [Fact(DisplayName = "IN の持ち上げ変数はパラメータ名との衝突を _ 後置で回避する")]
    public void Generate_InLiftVariableCollidingWithParameter_IsRenamed()
    {
        _order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByQuantitySet",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "quantities",
                        Type = "int32",
                        IsList = true,
                    },
                    new QueryParameter { Name = "quantitiesValues", Type = "int32" },
                },
                Condition = "Quantity IN @quantities AND CustomerId = @quantitiesValues",
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain("var quantitiesValues_ = quantities.Cast<int?>().ToList();")
            .And.Contain("quantitiesValues_.Contains(e.Quantity)");

        var compilation = GeneratedCodeCompiler.Compile(result, "QueryLiftCollision");

        compilation
            .Success.Should()
            .BeTrue(
                "持ち上げ変数がリネームされてコンパイルできるべき: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }

    /// <summary>
    /// 生 SQL × 単一戻り形の本体ローカル（items）が、同名の引数と衝突せずリネームされることを検証する
    /// （衝突したままだとローカルが引数を隠して CS0136）
    /// </summary>
    [Fact(DisplayName = "生 SQL 単一戻り形のローカルは引数名 items との衝突を回避する")]
    public void Generate_RawSqlSingleWithItemsParameter_RenamesLocal()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "FindByItems",
                Returns = QueryReturnShape.Single,
                Implementation = QueryImplementationKind.Sql,
                Parameters =
                {
                    new QueryParameter { Name = "items", Type = "int32" },
                },
                Sql = { ["sqlserver"] = "SELECT * FROM [Order] WHERE [CustomerId] = @items" },
            }
        );

        // EF Core を外す（EF Core × 生 SQL は手動実装＝契約宣言のみで、素のコンパイル検証が CS0535 になるため）
        var result = Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain("var items_ = await QueryBySqlAsync(")
            .And.Contain("return items_.Count > 0 ? items_[0] : null;");

        var compilation = GeneratedCodeCompiler.Compile(result, "QueryItemsParam");

        compilation
            .Success.Should()
            .BeTrue(
                "引数名 items の生 SQL 単一戻り形はコンパイルできるべき: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }

    /// <summary>
    /// 生 SQL 実装のクエリに残った条件・並び順が、黙って無視されず警告診断で告げられることを検証する
    /// （WHERE / ORDER BY は SQL 側の責務＝生成は続行する）
    /// </summary>
    [Fact(DisplayName = "生 SQL 実装に残った条件・並び順は警告で告げられる")]
    public void Generate_RawSqlWithConditionAndOrder_WarnsIgnored()
    {
        var amount = _order.Columns.First(c => c.Name == "Amount");
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "ListBigOrders",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Parameters =
                {
                    new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
                },
                Condition = "Amount >= @minAmount",
                OrderBy =
                {
                    new QueryOrdering { ColumnId = amount.Id, Descending = true },
                },
                Sql =
                {
                    ["sqlserver"] =
                        "SELECT * FROM [Order] WHERE [Amount] >= @minAmount ORDER BY [Amount] DESC",
                },
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result
            .Diagnostics.Where(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning
                && d.Message.Contains("ListBigOrders")
            )
            .Should()
            .HaveCount(2, "条件と並び順の 2 件が別々に告げられる");

        // 生成自体は続行し、SQL 実装が出る
        AllContent(result).Should().Contain("ListBigOrdersAsync");
    }

    /// <summary>
    /// DSL 射影で NULL 許容の値型列を非 NULL 指定のフィールドへ写す定義が、CS0266（int? → int 変換不能）で
    /// 生成アセンブリ全体を壊さず、警告つきでそのクエリだけスキップされることを検証する
    /// （参照型の列は null 許容性の警告どまりでコンパイルは通るため対象外）
    /// </summary>
    [Fact(DisplayName = "DSL 射影の非 NULL 上書き × NULL 許容値型列は警告＋クエリスキップ")]
    public void Generate_ProjectionNotNullOverrideOnNullableValueColumn_SkipsWithWarning()
    {
        _order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );
        var quantity = _order.Columns.First(c => c.Name == "Quantity");
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetForcedRows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "ForcedRow",
                Fields =
                {
                    new ProjectionField
                    {
                        Name = "Quantity",
                        SourceColumnId = quantity.Id,
                        IsNullable = false,
                    },
                },
            },
            new QueryDefinition { Name = "ListAll", Returns = QueryReturnShape.List }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning
                && d.Message.Contains("GetForcedRows")
                && d.Message.Contains("Quantity")
            );

        var content = AllContent(result);
        content.Should().NotContain("GetForcedRowsAsync", "スキップされたクエリのメソッドは出ない");
        content.Should().Contain("ListAllAsync", "他のクエリは従来どおり生成される");

        var compilation = GeneratedCodeCompiler.Compile(result, "QueryNotNullOverride");

        compilation
            .Success.Should()
            .BeTrue(
                "スキップ後の残存生成物はコンパイルできるべき: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }

    /// <summary>
    /// 生 SQL の逐語リテラル内の連続空行が、レンダラーの空行畳み込みに書き換えられないことを検証する
    /// （SQL の文字列リテラルに連続空行を含むユーザーの SQL が黙って変わっていた）。
    /// リテラルの外の連続空行は従来どおり 1 空行へ畳まれる
    /// </summary>
    [Fact(DisplayName = "生 SQL の逐語リテラル内の連続空行は畳まれない")]
    public void Generate_RawSqlWithBlankLines_PreservesThemInsideVerbatimLiteral()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "FindByBlankMemo",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlserver"] = "SELECT * FROM [Order] WHERE [Memo] = 'first\n\n\nsecond'",
                },
            }
        );

        var result = Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);
        var newline = Environment.NewLine;

        // リテラルの中身は改行 3 連（空行 2 つ）のまま保たれる（EOL は環境改行へ正規化される）
        content.Should().Contain($"'first{newline}{newline}{newline}second'");

        // リテラルの外は従来どおり畳まれている＝3 連以上の改行はこのリテラル内の 1 箇所だけ
        System
            .Text.RegularExpressions.Regex.Matches(
                content,
                $"(?:{System.Text.RegularExpressions.Regex.Escape(newline)}){{3,}}"
            )
            .Count.Should()
            .Be(1, "逐語リテラルの外の連続空行は 1 空行へ畳まれるべき");
    }

    /// <summary>診断メッセージを失敗理由として整形する</summary>
    private static string FormatDiagnostics(CodeGenerationResult result) =>
        string.Join(" / ", result.Diagnostics.Select(d => d.Message));
}
