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

    /// <summary>生成する（VO の有無・リモートサービスの有無を切り替える）</summary>
    private static CodeGenerationResult Generate(
        ErDiagram diagram,
        bool valueObjects,
        bool remoteServices = false
    )
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
                GenerateRemoteServices = remoteServices,
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
    [InlineData("ToString")]
    [InlineData("toString")]
    [InlineData("Equals")]
    [InlineData("GetHashCode")]
    [InlineData("GetType")]
    [InlineData("MemberwiseClone")]
    [InlineData("ReferenceEquals")]
    [InlineData("EqualityContract")]
    [InlineData("PrintMembers")]
    [InlineData("deconstruct")]
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

        // record のメンバーとぶつかる名前はリモートのリクエストでだけ壊れるので、リモートサービスつきで生成する
        var result = Generate(diagram, valueObjects: false, remoteServices: true);
        result.HasErrors.Should().BeFalse();

        // リモートのリクエストの record はパラメータ名を PascalCase にしたメンバーを持つので、その綴りも差し替える
        var pattern = new System.Text.RegularExpressions.Regex(
            $@"(?<![A-Za-z0-9_@]){placeholder}(?![A-Za-z0-9_])"
        );
        var pascalPlaceholder = char.ToUpperInvariant(placeholder[0]) + placeholder[1..];
        var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        var pascalPattern = new System.Text.RegularExpressions.Regex(
            $@"(?<![A-Za-z0-9_@]){pascalPlaceholder}(?![A-Za-z0-9_])"
        );
        var replaced = result
            .Files.Select(file => new GeneratedFile
            {
                FileName = file.FileName,
                RelativeDirectory = file.RelativeDirectory,
                Content = pascalPattern.Replace(pattern.Replace(file.Content, name), pascalName),
            })
            .ToList();

        GeneratedCodeCompiler
            .CompileProject(replaced, $"QueryReservedNameProbe_{name}", [], out _)
            .Success.Should()
            .BeFalse(
                $"'{name}' を予約から外すと生成物がコンパイルできないはず（要らない予約は外す）"
            );
    }

    [Theory(
        DisplayName = "生成本体が this. で呼ぶメンバーと同じ名前のパラメータは予約せず、生成されるどの形でもコンパイルできる"
    )]
    [InlineData("Query")]
    [InlineData("QueryBySqlAsync")]
    [InlineData("ExecuteScalarSqlAsync")]
    [InlineData("QueryProjectionBySqlAsync")]
    [InlineData("InvokeAsync")]
    [InlineData("InvokeNullableAsync")]
    public void MemberNamesCalledWithThis_AreNotReservedAndCompile(string name)
    {
        QueryDefinitionRules.IsReservedParameterName(name).Should().BeFalse();

        var (diagram, order) = CreateDiagram();

        foreach (var query in QueriesUsing(name, 0))
        {
            query.EntityId = order.Id;
            diagram.Queries.Add(query);
        }

        BindProjectionFields(diagram, order);

        var result = Generate(diagram, valueObjects: false, remoteServices: true);
        result
            .HasErrors.Should()
            .BeFalse(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));

        var compilation = GeneratedCodeCompiler.Compile(result, $"QueryMemberNameParam_{name}");

        compilation.Success.Should().BeTrue(compilation.DescribeErrors());
    }

    [Fact(
        DisplayName = "簡易 DSL の射影の選択式のフィールドと同じ名前のパラメータは、生成器と保存前の規則の両方で断られる"
    )]
    public void SelectorFieldNamedParameter_IsRejected()
    {
        var (diagram, order) = CreateDiagram();
        var qty = order.Columns.Single(column => column.Name == "Qty");
        var query = new QueryDefinition
        {
            EntityId = order.Id,
            Name = "Rows",
            Returns = QueryReturnShape.Projection,
            Implementation = QueryImplementationKind.Dsl,
            Condition = "Qty = @_rowsAsyncSelector",
            ResultTypeName = "RowsRow",
            Parameters =
            {
                new QueryParameter { Name = "_rowsAsyncSelector", Type = "int32" },
            },
            Fields =
            {
                new ProjectionField { Name = "Qty", SourceColumnId = qty.Id },
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
                && diagnostic.Message.Contains("'_rowsAsyncSelector'")
            );
    }

    [Fact(
        DisplayName = "選択式のフィールドと同じ名前は、それだけを予約から外すと生成物がコンパイルできない（表に要らない名前を載せない）"
    )]
    public void SelectorFieldName_IsActuallyUncompilable()
    {
        // 仮の名前で生成してから、その名前を選択式のフィールドの名前へ差し替えて実コンパイルする
        const string placeholder = "selectorNameProbe";
        var (diagram, order) = CreateDiagram();
        var qty = order.Columns.Single(column => column.Name == "Qty");
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "Rows",
                Returns = QueryReturnShape.Projection,
                Implementation = QueryImplementationKind.Dsl,
                Condition = $"Qty = @{placeholder}",
                ResultTypeName = "RowsRow",
                Parameters =
                {
                    new QueryParameter { Name = placeholder, Type = "int32" },
                },
                Fields =
                {
                    new ProjectionField { Name = "Qty", SourceColumnId = qty.Id },
                },
            }
        );

        var result = Generate(diagram, valueObjects: false);
        result.HasErrors.Should().BeFalse();
        result
            .Files.Should()
            .Contain(
                file => file.Content.Contains("_rowsAsyncSelector", StringComparison.Ordinal),
                "選択式のフィールドが生成されている"
            );

        var pattern = new System.Text.RegularExpressions.Regex(
            $@"(?<![A-Za-z0-9_@]){placeholder}(?![A-Za-z0-9_])"
        );
        var replaced = result
            .Files.Select(file => new GeneratedFile
            {
                FileName = file.FileName,
                RelativeDirectory = file.RelativeDirectory,
                Content = pattern.Replace(file.Content, "_rowsAsyncSelector"),
            })
            .ToList();

        GeneratedCodeCompiler
            .CompileProject(replaced, "QuerySelectorFieldNameProbe", [], out _)
            .Success.Should()
            .BeFalse(
                "選択式のフィールドと同じ名前のパラメータは、フィールドを隠してコンパイルできないはず"
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
