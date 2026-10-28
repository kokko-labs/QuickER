using System.Diagnostics;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前付きクエリのパラメータ名が、生成されるどの形（生 SQL・簡易 DSL の全戻り形・リモートのクライアントと
/// サーバーのリクエスト・同期のジャーナル記録デコレータ）でもコンパイルできるか、そうでなければ生成前に名指しで
/// 断られることを、名前ごとに固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 生成本体は継承メンバー（<c>QueryBySqlAsync</c> など）・フィールド（<c>_inner</c>）を単純名で呼び、
/// リモートのリクエストはパラメータ名を PascalCase にした positional record のメンバーにする。そのため、
/// 同じ名前のパラメータはメンバーを隠すか、record が合成するメンバーとぶつかる。
/// 1 つの図に全部の名前を載せて 1 回だけコンパイルし、エラー・警告の行に現れる名前で失敗を名前へ割り当てる。
/// </para>
/// <para>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownCaseLedger"/>。</para>
/// </remarks>
public class QueryParameterNameSafetyTests(ITestOutputHelper output)
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownCaseLedger Known = new("QueryParameterNameSafetyTests", []);

    /// <summary>
    /// 試す名前。生成本体が単純名で呼ぶメンバーと、record が持つ・合成するメンバーの名前（先頭の大小の両方）
    /// </summary>
    private static IReadOnlyList<string> CandidateNames()
    {
        var memberNames = new List<string>
        {
            "_inner",
            "InvokeAsync",
            "InvokeNullableAsync",
            "QueryBySqlAsync",
            "ExecuteScalarSqlAsync",
            "QueryProjectionBySqlAsync",
            "ExecuteSqlAsync",
            "Query",
            // record が合成するメンバー
            "EqualityContract",
            "PrintMembers",
            "Deconstruct",
        };

        // object のメンバー（record のメンバーと同じ名前のプロパティは隠蔽か衝突になる）
        memberNames.AddRange(
            typeof(object)
                .GetMethods(
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.DeclaredOnly
                )
                .Where(method => !method.IsPrivate && !method.IsAssembly)
                .Select(method => method.Name)
        );

        return memberNames
            .SelectMany(name => new[] { name, char.ToLowerInvariant(name[0]) + name[1..] })
            .Distinct(StringComparer.Ordinal)
            .Where(QueryDefinitionRules.IsValidIdentifier)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>1 つのパラメータ名を、生成されるメソッドの形すべてで使うクエリ群を作る</summary>
    private static IEnumerable<QueryDefinition> QueriesUsing(string parameterName, int index)
    {
        var prefix = $"P{index}";

        QueryParameter Scalar() => new() { Name = parameterName, Type = "int32" };

        foreach (var returns in Enum.GetValues<QueryReturnShape>())
        {
            var sql = $"SELECT * FROM Orders WHERE Qty = @{parameterName}";
            var query = new QueryDefinition
            {
                Name = $"{prefix}Sql{returns}",
                Returns = returns,
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = sql, ["sqlite"] = sql },
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
                Condition = $"Qty = @{parameterName}",
                Parameters = { Scalar() },
                HasPaging = returns is QueryReturnShape.List,
            };

            if (returns == QueryReturnShape.Projection)
            {
                query.ResultTypeName = $"{prefix}DslRow";
            }

            yield return query;
        }
    }

    /// <summary>Orders（OrderId・Qty）の表と、名前ごとのクエリを載せた図を作る</summary>
    private static (
        ErDiagram Diagram,
        Entity Order,
        Dictionary<string, List<QueryDefinition>> Queries
    ) CreateDiagram(IEnumerable<(string Name, int Index)> names)
    {
        var order = new Entity { TableName = "Orders" };
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
        var diagram = new ErDiagram { Entities = { order } };
        var queriesByName = new Dictionary<string, List<QueryDefinition>>(StringComparer.Ordinal);

        foreach (var (name, index) in names)
        {
            var queries = QueriesUsing(name, index).ToList();

            foreach (var query in queries)
            {
                query.EntityId = order.Id;

                if (
                    query is
                    {
                        Implementation: QueryImplementationKind.Dsl,
                        Returns: QueryReturnShape.Projection
                    }
                )
                {
                    query.Fields.Add(new ProjectionField { Name = "Qty", SourceColumnId = qty.Id });
                }

                diagram.Queries.Add(query);
            }

            queriesByName[name] = queries;
        }

        return (diagram, order, queriesByName);
    }

    /// <summary>sqlserver＋sqlite のマルチターゲットに、リモートサービスと同期支援を足して生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram)
    {
        var sqlServer = new SqlServerProvider();
        var sqlite = new SqliteProvider();

        return DiagramCodeGenerator.Generate(
            sqlServer.TypeMapper,
            sqlServer.TypeCatalog,
            new Dictionary<string, IColumnTypeMapper>(StringComparer.OrdinalIgnoreCase)
            {
                ["sqlserver"] = sqlServer.TypeMapper,
                ["sqlite"] = sqlite.TypeMapper,
            },
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
                GenerateRemoteServices = true,
                GenerateSyncSupport = true,
            }
        );
    }

    [Fact(
        DisplayName = "パラメータ名は、生成されるどの形でもコンパイルできるか、生成前に名指しで断られる（リモート・同期を含む）"
    )]
    public void EveryCandidateName_CompilesOrIsRejected()
    {
        var watch = Stopwatch.StartNew();
        var names = CandidateNames();

        // 1 段目: 名前ごとに生成して、生成器が断る名前を決める（断られた名前が 1 つでもあると生成物が出ないため）。
        // 保存前の規則（Validate）が同じ名前を断ることもここで見る
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var ruleMismatch = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < names.Count; i++)
        {
            var (diagram, order, queries) = CreateDiagram([(names[i], i)]);
            var generatorRejects = Generate(diagram).HasErrors;
            var validateRejects = queries[names[i]]
                .Any(query =>
                    QueryDefinitionRules
                        .Validate(order, query, [])
                        .Any(violation => violation.Kind == QueryRuleKind.ReservedParameterName)
                );

            if (generatorRejects)
            {
                rejected.Add(names[i]);
            }

            if (generatorRejects != validateRejects)
            {
                ruleMismatch[names[i]] =
                    $"生成器 {(generatorRejects ? "断る" : "通す")}・保存前の規則 {(validateRejects ? "断る" : "通す")}";
            }
        }

        // 2 段目: 断られなかった名前を全部 1 つの図に載せ、1 回だけ生成してコンパイルする
        var accepted = names
            .Select((name, index) => (name, index))
            .Where(pair => !rejected.Contains(pair.name))
            .ToList();
        var (acceptedDiagram, _, _) = CreateDiagram(accepted);
        var result = Generate(acceptedDiagram);
        result
            .HasErrors.Should()
            .BeFalse(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        result
            .Files.Should()
            .Contain(
                file => file.Content.Contains("_inner.", StringComparison.Ordinal),
                "同期のデコレータが生成されている"
            )
            .And.Contain(
                file => file.Content.Contains("Request(", StringComparison.Ordinal),
                "リモートのリクエストが生成されている"
            );

        var compilation = GeneratedCodeCompiler.Compile(result, "QueryParameterNameSafety");
        var problemLines = compilation
            .Errors.Concat(compilation.Warnings)
            .Select(diagnostic =>
                (
                    Diagnostic: diagnostic,
                    Line: diagnostic.Location.SourceTree is { } tree
                        ? tree.GetText()
                            .Lines[diagnostic.Location.GetLineSpan().StartLinePosition.Line]
                            .ToString()
                        : string.Empty
                )
            )
            .ToList();
        output.WriteLine(
            $"生成とコンパイル: {watch.ElapsedMilliseconds} ms・名前 {names.Count} 個（断られた名前 {rejected.Count} 個）"
        );

        foreach (var name in names)
        {
            Known.Run(
                name,
                () =>
                {
                    ruleMismatch
                        .GetValueOrDefault(name)
                        .Should()
                        .BeNull($"'{name}' を断るかどうかが、生成器と保存前の規則で一致する");

                    if (rejected.Contains(name))
                    {
                        return;
                    }

                    // リモートのリクエストの record はパラメータ名を PascalCase にしたメンバーを持つので、その綴りでも探す
                    var pascal = char.ToUpperInvariant(name[0]) + name[1..];
                    var pattern = new Regex(
                        $@"(?<![A-Za-z0-9_])({Regex.Escape(name)}|{Regex.Escape(pascal)})(?![A-Za-z0-9_])"
                    );
                    var hits = problemLines
                        .Where(line => pattern.IsMatch(line.Line))
                        .Select(line => $"{line.Diagnostic.Id}: {line.Line.Trim()}")
                        .ToList();
                    hits.Should()
                        .BeEmpty(
                            $"パラメータ名 '{name}' の生成物がコンパイルできない（断られてもいない）"
                        );
                },
                problems
            );
        }

        // 名前に割り当てられなかったエラーも見逃さない（生成物そのものが壊れている）
        var unassigned = problemLines
            .Where(line =>
                !names.Any(name =>
                    Regex.IsMatch(
                        line.Line,
                        $@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])"
                    )
                )
            )
            .Where(line => line.Diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(line => $"{line.Diagnostic.Id}: {line.Line.Trim()}")
            .ToList();
        unassigned.Should().BeEmpty("どの名前にも割り当てられないコンパイルエラーがある");

        KnownCaseLedger.ThrowIfAny(problems);
    }
}
