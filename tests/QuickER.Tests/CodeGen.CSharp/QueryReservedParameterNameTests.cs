using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前付きクエリのパラメータ名に使えない名前（<see cref="QueryDefinitionRules.IsReservedParameterName"/>）が、
/// 生成されるメソッドの形の中で実際にコンパイルできない名前と一致することを検証するテストクラス。
/// </summary>
/// <remarks>
/// C# の文脈キーワードは識別子として合法なので <see cref="QueryDefinitionRules.IsValidIdentifier"/> を通るが、
/// 生成されるメソッドの形によっては識別子として使えない（async メソッドの中の <c>await</c>）。予約名の表を
/// 文脈キーワードの全数と実コンパイルで突き合わせ、表から漏れた名前と、表に要らない名前の両方を検出する。
/// </remarks>
public class QueryReservedParameterNameTests
{
    /// <summary>Roslyn が知っている文脈キーワードの全数（予約されたものも含む）</summary>
    private static IReadOnlyList<string> ContextualKeywords() =>
        SyntaxFacts
            .GetContextualKeywordKinds()
            .Select(SyntaxFacts.GetText)
            .Where(QueryDefinitionRules.IsValidIdentifier)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

    /// <summary>1 つのパラメータ名を、生成されるメソッドの形すべてで使うクエリ群を作る</summary>
    private static IEnumerable<QueryDefinition> QueriesUsing(string parameterName, int index)
    {
        var prefix = $"P{index}";

        QueryParameter Scalar() => new() { Name = parameterName, Type = "int32" };

        // 生 SQL × 全戻り形（単一・件数・スカラーは async メソッド、一覧・射影は式本体）
        foreach (var returns in Enum.GetValues<QueryReturnShape>())
        {
            var query = new QueryDefinition
            {
                Name = $"{prefix}Sql{returns}",
                Returns = returns,
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = $"SELECT * FROM [Order] WHERE [Qty] = @{parameterName}" },
                Parameters = { Scalar() },
            };

            if (returns == QueryReturnShape.Scalar)
            {
                query.ScalarType = "int32";
            }

            if (returns == QueryReturnShape.Projection)
            {
                query.ResultTypeName = $"{prefix}SqlRow";
                query.Fields.Add(new ProjectionField { Name = "Total", Type = "int32" });
            }

            yield return query;
        }

        // 簡易 DSL × スカラー以外の戻り形（比較と IN の持ち上げの両方）
        foreach (var returns in Enum.GetValues<QueryReturnShape>())
        {
            if (returns == QueryReturnShape.Scalar)
            {
                continue;
            }

            var query = new QueryDefinition
            {
                Name = $"{prefix}Dsl{returns}",
                Returns = returns,
                Implementation = QueryImplementationKind.Dsl,
                Condition = $"Qty = @{parameterName} OR Quantity IN @{parameterName}List",
                Parameters =
                {
                    Scalar(),
                    new QueryParameter
                    {
                        Name = parameterName + "List",
                        Type = "int32",
                        IsList = true,
                    },
                },
                HasPaging = returns is QueryReturnShape.List,
            };

            if (returns == QueryReturnShape.Projection)
            {
                query.ResultTypeName = $"{prefix}DslRow";
            }

            yield return query;
        }
    }

    /// <summary>Qty（int）と NULL 許容の Quantity（int）を持つ Order の図を作る</summary>
    private static (ErDiagram Diagram, Entity Order) CreateDiagram()
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
        var qty = new Column
        {
            Name = "Qty",
            DataType = "int",
            IsNullable = false,
        };
        order.Columns.Add(qty);
        order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );

        return (new ErDiagram { Entities = { order } }, order);
    }

    /// <summary>DSL の射影へ列参照のフィールドを足す（列 ID は図ごとに違うため後から埋める）</summary>
    private static void BindProjectionFields(ErDiagram diagram, Entity order)
    {
        var qty = order.Columns.Single(column => column.Name == "Qty");

        foreach (
            var query in diagram.Queries.Where(query =>
                query.Implementation == QueryImplementationKind.Dsl
                && query.Returns == QueryReturnShape.Projection
            )
        )
        {
            query.Fields.Add(new ProjectionField { Name = "Qty", SourceColumnId = qty.Id });
        }
    }

    /// <summary>生成する（VO の有無を切り替える）</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram, bool valueObjects)
    {
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
                GenerateValueObjects = valueObjects,
            }
        );
    }

    [Theory(
        DisplayName = "予約されていない文脈キーワードは、生成されるどのメソッドの形でもパラメータ名としてコンパイルできる"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreservedContextualKeywords_CompileInEveryMethodShape(bool valueObjects)
    {
        var keywords = ContextualKeywords()
            .Where(keyword => !QueryDefinitionRules.IsReservedParameterName(keyword))
            .ToList();
        keywords.Should().NotBeEmpty();

        var (diagram, order) = CreateDiagram();
        var index = 0;

        foreach (var keyword in keywords)
        {
            foreach (var query in QueriesUsing(keyword, index++))
            {
                query.EntityId = order.Id;
                diagram.Queries.Add(query);
            }
        }

        BindProjectionFields(diagram, order);

        var result = Generate(diagram, valueObjects);

        result
            .Diagnostics.Where(diagnostic =>
                diagnostic.Severity != GenerationDiagnosticSeverity.Info
            )
            .Should()
            .BeEmpty("すべてのクエリが診断なしで生成されるべき");

        var compilation = GeneratedCodeCompiler.Compile(
            result,
            $"QueryContextualKeywordParams{valueObjects}"
        );

        compilation.Success.Should().BeTrue(compilation.DescribeErrors());
    }

    [Theory(
        DisplayName = "予約された名前は、それだけを予約から外すと生成物がコンパイルできない（表に要らない名前を載せない）"
    )]
    [InlineData("await")]
    public void ReservedName_IsActuallyUncompilable(string name)
    {
        QueryDefinitionRules.IsReservedParameterName(name).Should().BeTrue();

        // 生成器は予約名を通さないので、仮の名前で全形を生成してから、その名前だけを差し替えて実コンパイルする
        // （生成物の形そのもので確かめる。仮の名前は他の識別子の一部に現れないもの）
        const string placeholder = "reservedNameProbe";
        var (diagram, order) = CreateDiagram();

        foreach (var query in QueriesUsing(placeholder, 0))
        {
            query.EntityId = order.Id;
            diagram.Queries.Add(query);
        }

        BindProjectionFields(diagram, order);

        var result = Generate(diagram, valueObjects: false);
        result.HasErrors.Should().BeFalse();

        var pattern = new System.Text.RegularExpressions.Regex(
            $@"(?<![A-Za-z0-9_@]){placeholder}(?![A-Za-z0-9_])"
        );
        var replaced = result
            .Files.Select(file => new GeneratedFile
            {
                FileName = file.FileName,
                RelativeDirectory = file.RelativeDirectory,
                Content = pattern.Replace(file.Content, name),
            })
            .ToList();

        GeneratedCodeCompiler
            .CompileProject(replaced, $"QueryReservedNameProbe_{name}", [], out _)
            .Success.Should()
            .BeFalse(
                $"'{name}' を予約から外すと生成物がコンパイルできないはず（要らない予約は外す）"
            );
    }

    [Fact(
        DisplayName = "await という名前のパラメータは生成時エラーになり、保存前の規則でも拒否される"
    )]
    public void AwaitParameter_IsRejected()
    {
        var (diagram, order) = CreateDiagram();
        var query = new QueryDefinition
        {
            EntityId = order.Id,
            Name = "FindOne",
            Returns = QueryReturnShape.Single,
            Implementation = QueryImplementationKind.Sql,
            Sql = { ["sqlserver"] = "SELECT * FROM [Order] WHERE [Qty] = @await" },
            Parameters =
            {
                new QueryParameter { Name = "await", Type = "int32" },
            },
        };
        diagram.Queries.Add(query);

        QueryDefinitionRules
            .Validate(order, query, [])
            .Should()
            .ContainSingle(violation => violation.Kind == QueryRuleKind.ReservedParameterName);

        var result = Generate(diagram, valueObjects: false);

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("'await'")
            );
    }
}
