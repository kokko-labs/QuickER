using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 生成で必ずエラーになるクエリ定義を <c>set_query</c> が保存前に拒否すること、その判定と文言が生成器と食い違わないことを、
/// 同じ定義の表を両方へ流して検証する。
/// </summary>
/// <remarks>
/// 各ケースは意味モデル（<see cref="QueryDefinition"/>）で書き、生成器へはそのまま図へ入れて、<c>set_query</c> へは
/// 同じ定義を引数 JSON に直して渡す（<see cref="ToSetQueryArguments"/>）。突き合わせは 3 点＝
/// (1) <see cref="QueryDefinitionRules.Validate"/> が期待した規則の違反を返す
/// (2) 生成器がその定義にエラー診断を出し、文言が違反の文言と一致する
/// (3) <c>set_query</c> が保存せずに失敗し、応答に違反の英語の文言が出る（ツール自身の診断が先に報告する 3 規則は
/// 失敗することだけを見る）。
/// </remarks>
public sealed class QueryToolGenerationRuleTests : IDisposable
{
    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-query-rule-" + Guid.NewGuid().ToString("N")
    );

    public QueryToolGenerationRuleTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // クリーンアップ失敗はテスト結果に影響させない
        }
    }

    /// <summary>テスト用の Order テーブル（OrderId 主キー・CustomerId・Amount）を作る</summary>
    private static Entity CreateOrder()
    {
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        order.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsNullable = false,
            }
        );
        order.Columns.Add(
            new Column
            {
                Name = "Amount",
                DataType = "decimal(12,2)",
                IsNullable = false,
            }
        );

        return order;
    }

    /// <summary>
    /// ケース 1 件。<see cref="Existing"/> は先に図へ入っている有効な定義、<see cref="Candidate"/> は調べる定義
    /// （どちらも列は名前で書き、図へ入れるときに Id へ直す）
    /// </summary>
    public sealed record RuleCase(
        string Label,
        QueryRuleKind Expected,
        Func<Entity, QueryDefinition> Candidate,
        Func<Entity, QueryDefinition>? Existing = null
    )
    {
        public override string ToString() => Label;
    }

    /// <summary>列名から列 Id を引く</summary>
    private static Guid ColumnId(Entity entity, string name) =>
        entity.Columns.Single(column => column.Name == name).Id;

    /// <summary>Amount 列を参照する射影フィールド</summary>
    private static ProjectionField AmountField(Entity entity, string name = "Amount") =>
        new() { Name = name, SourceColumnId = ColumnId(entity, "Amount") };

    /// <summary>違反のケース表（テストの引数はラベルだけにして、ここから引く）</summary>
    private static readonly RuleCase[] ViolationCases =
    [
        new RuleCase(
            "reserved method name",
            QueryRuleKind.ReservedMethodName,
            _ => new QueryDefinition { Name = "GetById", Returns = QueryReturnShape.List }
        ),
        new RuleCase(
            "method name duplicates another query (case and separator differ)",
            QueryRuleKind.DuplicateMethodName,
            _ => new QueryDefinition { Name = "find_by_customer", Returns = QueryReturnShape.List },
            _ => new QueryDefinition { Name = "FindByCustomer", Returns = QueryReturnShape.List }
        ),
        new RuleCase(
            "method name duplicates another query (Async suffix)",
            QueryRuleKind.DuplicateMethodName,
            _ => new QueryDefinition { Name = "LookupAsync", Returns = QueryReturnShape.List },
            _ => new QueryDefinition { Name = "Lookup", Returns = QueryReturnShape.List }
        ),
        new RuleCase(
            "scalar with the simple DSL",
            QueryRuleKind.ScalarDslUnsupported,
            _ => new QueryDefinition
            {
                Name = "TotalAmount",
                Returns = QueryReturnShape.Scalar,
                ScalarType = "decimal(12,2)",
                Implementation = QueryImplementationKind.Dsl,
            }
        ),
        new RuleCase(
            "paging on a single row",
            QueryRuleKind.PagingRequiresList,
            _ => new QueryDefinition
            {
                Name = "FirstOrder",
                Returns = QueryReturnShape.Single,
                HasPaging = true,
            }
        ),
        new RuleCase(
            "parameter name is not an identifier",
            QueryRuleKind.InvalidParameterName,
            _ => new QueryDefinition
            {
                Name = "ByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customer-id", Type = "int32" },
                },
            }
        ),
        new RuleCase(
            "parameter named like a member of the remote request record",
            QueryRuleKind.ReservedParameterName,
            _ => new QueryDefinition
            {
                Name = "ByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "toString", Type = "int32" },
                },
            }
        ),
        new RuleCase(
            "parameter declared twice",
            QueryRuleKind.DuplicateParameterName,
            _ => new QueryDefinition
            {
                Name = "ByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "id", Type = "int32" },
                    new QueryParameter { Name = "ID", Type = "int32" },
                },
            }
        ),
        new RuleCase(
            "parameter named like the implicit cancellationToken",
            QueryRuleKind.DuplicateParameterName,
            _ => new QueryDefinition
            {
                Name = "ByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "cancellationToken", Type = "int32" },
                },
            }
        ),
        new RuleCase(
            "parameter named take with paging",
            QueryRuleKind.DuplicateParameterName,
            _ => new QueryDefinition
            {
                Name = "ByCustomer",
                Returns = QueryReturnShape.List,
                HasPaging = true,
                Parameters =
                {
                    new QueryParameter { Name = "take", Type = "int32" },
                },
            }
        ),
        new RuleCase(
            "result type name is not an identifier",
            QueryRuleKind.InvalidResultTypeName,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "Order-Row",
                Fields = { AmountField(entity) },
            }
        ),
        new RuleCase(
            "result type name already used by another query",
            QueryRuleKind.DuplicateResultTypeName,
            entity => new QueryDefinition
            {
                Name = "MoreRows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
                Fields = { AmountField(entity) },
            },
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
                Fields = { AmountField(entity) },
            }
        ),
        new RuleCase(
            "result type name is a BCL type the generated code uses by its simple name",
            QueryRuleKind.ReservedResultTypeName,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "Task",
                Fields = { AmountField(entity) },
            }
        ),
        new RuleCase(
            "result type name is a runtime type",
            QueryRuleKind.ReservedResultTypeName,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "SqlQuery",
                Fields = { AmountField(entity) },
            }
        ),
        new RuleCase(
            "result type name is the generated entity class",
            QueryRuleKind.ReservedResultTypeName,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderEntity",
                Fields = { AmountField(entity) },
            }
        ),
        new RuleCase(
            "result type name is the generated repository interface",
            QueryRuleKind.ReservedResultTypeName,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "IOrderRepository",
                Fields = { AmountField(entity) },
            }
        ),
        new RuleCase(
            "result type name is a generated value object",
            QueryRuleKind.ReservedResultTypeName,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "AmountValue",
                Fields = { AmountField(entity) },
            }
        ),
        new RuleCase(
            "field name is not an identifier",
            QueryRuleKind.InvalidFieldName,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
                Fields = { AmountField(entity, "1st") },
            }
        ),
        new RuleCase(
            "field declared twice",
            QueryRuleKind.InvalidFieldName,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
                Fields = { AmountField(entity), AmountField(entity) },
            }
        ),
        new RuleCase(
            "field named after the result type",
            QueryRuleKind.FieldNameEqualsResultType,
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "Total",
                Fields = { AmountField(entity, "Total") },
            }
        ),
        new RuleCase(
            "simple DSL projection with a field that references no column",
            QueryRuleKind.ProjectionDslRequiresColumns,
            _ => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
                Fields =
                {
                    new ProjectionField { Name = "Total", Type = "decimal(12,2)" },
                },
            }
        ),
        // ツール自身の診断が同じ判定で先に報告する 3 規則（構造規則の側からは積まない）
        new RuleCase(
            "scalar without a type token",
            QueryRuleKind.ScalarRequiresType,
            _ => new QueryDefinition
            {
                Name = "TotalAmount",
                Returns = QueryReturnShape.Scalar,
                Implementation = QueryImplementationKind.Manual,
            }
        ),
        new RuleCase(
            "projection without fields",
            QueryRuleKind.ProjectionRequiresFields,
            _ => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
                Implementation = QueryImplementationKind.Manual,
            }
        ),
        new RuleCase(
            "order by on a count",
            QueryRuleKind.OrderByRequiresList,
            entity => new QueryDefinition
            {
                Name = "CountAll",
                Returns = QueryReturnShape.Count,
                OrderBy = { new QueryOrdering { ColumnId = ColumnId(entity, "Amount") } },
            }
        ),
    ];

    /// <summary>違反のケースのラベル</summary>
    public static TheoryData<string> Violations() => new(ViolationCases.Select(c => c.Label));

    /// <summary>ツール自身の診断が先に報告する規則（応答の文言は構造規則のものでなくツールのもの）</summary>
    private static readonly HashSet<QueryRuleKind> ReportedByTool =
    [
        QueryRuleKind.ScalarRequiresType,
        QueryRuleKind.ProjectionRequiresFields,
        QueryRuleKind.OrderByRequiresList,
    ];

    [Theory(
        DisplayName = "生成で必ずエラーになる定義は、生成器と set_query の両方が同じ規則で拒否する"
    )]
    [MemberData(nameof(Violations))]
    public void Violation_IsRejectedByGeneratorAndSetQuery(string label)
    {
        var ruleCase = ViolationCases.Single(c => c.Label == label);
        var order = CreateOrder();
        var existing = ruleCase.Existing?.Invoke(order);
        var candidate = ruleCase.Candidate(order);

        // (1) 構造規則が期待した違反を返す
        var violation = QueryDefinitionRules
            .Validate(
                order,
                Attach(candidate, order),
                existing is null ? [] : [Attach(existing, order)],
                [order]
            )
            .Should()
            .ContainSingle(v => v.Kind == ruleCase.Expected)
            .Which;

        // (2) 生成器が同じ文言のエラー診断を出す（後に来た定義へ報告されるよう、既存を先に並べる）
        var diagram = new ErDiagram { TargetDbms = "sqlserver", Entities = { order } };

        if (existing is not null)
        {
            diagram.Queries.Add(existing);
        }

        diagram.Queries.Add(candidate);
        var errors = Generate(diagram)
            .Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToList();
        errors.Should().Contain(violation.Text.Format(null));

        // (3) set_query は保存せずに失敗し、応答に英語の文言が出る
        var file = CreateDiagramFile(
            existing is null ? null : ToSetQueryArguments(order, existing)
        );
        var before = File.ReadAllBytes(file);

        var (result, success) = DocumentErDiagramToolHost.Execute(
            QueryToolCore.SetQueryToolName,
            file,
            JsonSerializer.SerializeToElement(ToSetQueryArguments(order, candidate))
        );

        success.Should().BeFalse(result);
        File.ReadAllBytes(file).Should().Equal(before, "a rejected definition must not be saved");

        if (!ReportedByTool.Contains(ruleCase.Expected))
        {
            result.Should().Contain(violation.Text.FormatEnglish());
        }
    }

    /// <summary>通すべき境界の定義の表（テストの引数はラベルだけにして、ここから引く）</summary>
    private static readonly (string Label, Func<Entity, QueryDefinition> Build)[] ValidCases =
    [
        (
            "a lower-case parameter named query",
            _ => new QueryDefinition
            {
                Name = "ByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "query", Type = "int32" },
                },
            }
        ),
        (
            "a parameter named Query (the generated body calls the member through this.)",
            _ => new QueryDefinition
            {
                Name = "ByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "Query", Type = "int32" },
                },
            }
        ),
        (
            "a name that already ends in Async",
            _ => new QueryDefinition { Name = "LookupAsync", Returns = QueryReturnShape.List }
        ),
        (
            "a parameter named take without paging",
            _ => new QueryDefinition
            {
                Name = "Latest",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "take", Type = "int32" },
                },
            }
        ),
        (
            "a simple DSL projection whose fields all reference columns",
            entity => new QueryDefinition
            {
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
                Fields = { AmountField(entity) },
                HasPaging = true,
            }
        ),
        (
            "a scalar with raw SQL",
            _ => new QueryDefinition
            {
                Name = "TotalAmount",
                Returns = QueryReturnShape.Scalar,
                ScalarType = "decimal(12,2)",
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT SUM([Amount]) FROM [Order]" },
            }
        ),
    ];

    /// <summary>通すべき境界の定義のラベル</summary>
    public static TheoryData<string> ValidDefinitions() => new(ValidCases.Select(c => c.Label));

    [Theory(DisplayName = "規則に違反しない境界の定義は、生成器も set_query も通す")]
    [MemberData(nameof(ValidDefinitions))]
    public void ValidDefinition_IsAcceptedByGeneratorAndSetQuery(string label)
    {
        var build = ValidCases.Single(c => c.Label == label).Build;
        var order = CreateOrder();
        var candidate = build(order);

        QueryDefinitionRules
            .Validate(order, Attach(candidate, order), [], [order])
            .Should()
            .BeEmpty(label);

        var diagram = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = { order },
            Queries = { candidate },
        };
        var result = Generate(diagram);
        result
            .HasErrors.Should()
            .BeFalse(string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));

        var file = CreateDiagramFile(null);
        var (message, success) = DocumentErDiagramToolHost.Execute(
            QueryToolCore.SetQueryToolName,
            file,
            JsonSerializer.SerializeToElement(ToSetQueryArguments(order, candidate))
        );

        success.Should().BeTrue(message);
    }

    /// <summary>定義をテーブルへ結び付けて返す</summary>
    private static QueryDefinition Attach(QueryDefinition query, Entity entity)
    {
        query.EntityId = entity.Id;

        return query;
    }

    /// <summary>実経路（SQL Server プロバイダ・QuickER 版 Repository）で生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram)
    {
        foreach (var query in diagram.Queries)
        {
            query.EntityId = diagram.Entities.Single().Id;
        }

        var provider = new SqlServerProvider();

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );
    }

    /// <summary>Order テーブルを持つ図ファイルを作り、必要なら先に有効なクエリを 1 件 set_query で入れる</summary>
    private string CreateDiagramFile(object? existingArguments)
    {
        var file = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        var document = new DiagramDocument
        {
            Schema = new ErDiagram { TargetDbms = "sqlserver", Entities = { CreateOrder() } },
        };
        JsonStorageService.SaveAtomic(file, document);

        if (existingArguments is not null)
        {
            var (message, success) = DocumentErDiagramToolHost.Execute(
                QueryToolCore.SetQueryToolName,
                file,
                JsonSerializer.SerializeToElement(existingArguments)
            );
            success.Should().BeTrue(message);
        }

        return file;
    }

    /// <summary>意味モデルのクエリ定義を set_query の引数（列は名前）へ直す</summary>
    private static Dictionary<string, object?> ToSetQueryArguments(
        Entity entity,
        QueryDefinition query
    )
    {
        string ColumnName(Guid id) => entity.Columns.Single(column => column.Id == id).Name;

        var arguments = new Dictionary<string, object?>
        {
            ["table_name"] = entity.TableName,
            ["query_name"] = query.Name,
            ["returns"] = query.Returns.ToString().ToLowerInvariant(),
            ["implementation"] = query.Implementation.ToString().ToLowerInvariant(),
            ["paging"] = query.HasPaging,
            ["parameters"] = query
                .Parameters.Select(parameter =>
                    parameter.SourceColumnId is { } columnId
                        ? new Dictionary<string, object?>
                        {
                            ["name"] = parameter.Name,
                            ["source_column"] = ColumnName(columnId),
                            ["is_list"] = parameter.IsList,
                        }
                        : new Dictionary<string, object?>
                        {
                            ["name"] = parameter.Name,
                            ["type"] = parameter.Type,
                            ["is_list"] = parameter.IsList,
                        }
                )
                .ToList(),
            ["order_by"] = query
                .OrderBy.Select(ordering => new Dictionary<string, object?>
                {
                    ["column"] = ColumnName(ordering.ColumnId),
                    ["descending"] = ordering.Descending,
                })
                .ToList(),
            ["fields"] = query
                .Fields.Select(field =>
                    field.SourceColumnId is { } columnId
                        ? new Dictionary<string, object?>
                        {
                            ["name"] = field.Name,
                            ["source_column"] = ColumnName(columnId),
                        }
                        : new Dictionary<string, object?>
                        {
                            ["name"] = field.Name,
                            ["type"] = field.Type,
                        }
                )
                .ToList(),
        };

        if (query.ScalarType is not null)
        {
            arguments["scalar_type"] = query.ScalarType;
        }

        if (query.Condition is not null)
        {
            arguments["condition"] = query.Condition;
        }

        if (query.ResultTypeName is not null)
        {
            arguments["result_type_name"] = query.ResultTypeName;
        }

        if (query.Sql.Count > 0)
        {
            arguments["sql"] = query.Sql;
        }

        return arguments;
    }
}
