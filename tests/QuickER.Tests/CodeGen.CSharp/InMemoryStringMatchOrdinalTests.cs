using System.IO;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// インメモリ実装の前方一致・後方一致（簡易 DSL の LIKE と手書きの LINQ の <c>StartsWith</c> / <c>EndsWith</c>）が、
/// 素の string 列でも序数で比べ、現在のカルチャに依らず SQLite の QuickER 版 Repository と同じ行を返すことを固定する
/// </summary>
/// <remarks>
/// C# の <c>string.StartsWith(string)</c> は現在のカルチャで比べるので、合成済みの「が」と分解形の「か＋濁点」を同じとみなし、
/// ソフトハイフン（U+00AD）を無視する。インメモリの実行器が述語を序数の多重定義へ書き換えるので、
/// docs の「インメモリは序数」と、同じ実装先の <c>Contains</c>・値オブジェクトの列と同じ答えになる。
/// </remarks>
public sealed class InMemoryStringMatchOrdinalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>1: 合成済みの「が」で始まる / 2: 分解形で始まる / 3: 合成済みで終わる / 4: 分解形で終わる / 5: abc</summary>
    private const string Rows =
        "(1, 'が-x'), (2, 'が-x'), (3, 'x-が'), (4, 'x-が'), (5, 'abc')";

    private const string Probe = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        namespace Sample.Domain;

        public static class MatchProbe
        {
            public static async Task<string[]> RunAllAsync(string connectionString)
            {
                await using var ado = new ServiceCollection()
                    .AddGeneratedSqliteRepositories(connectionString)
                    .BuildServiceProvider();
                await using var inMemory = new ServiceCollection()
                    .AddGeneratedInMemoryRepositories(seedSampleData: false)
                    .BuildServiceProvider();

                var adoItems = ado.GetRequiredService<IItemRepository>();
                var memoryItems = inMemory.GetRequiredService<IItemRepository>();

                foreach (var item in await adoItems.GetAllAsync())
                {
                    await memoryItems.InsertAsync(item);
                }

                return ["ado=" + await RunAsync(adoItems), "inmemory=" + await RunAsync(memoryItems)];
            }

            private static string Ids(IEnumerable<ItemEntity> items) =>
                string.Join(",", items.Select(item => long.Parse(item.Id.ToString())).OrderBy(id => id));

            private static async Task<string> RunAsync(IItemRepository items) =>
                string.Join(
                    ";",
                    "StartsWith:" + Ids(await items.StartsComposedAsync()),
                    "EndsWith:" + Ids(await items.EndsComposedAsync()),
                    "Contains:" + Ids(await items.ContainsComposedAsync()),
                    "SoftHyphen:" + Ids(await items.StartsSoftHyphenAsync()),
                    "LinqStartsWith:" + Ids(await items.Query().Where(item => item.Name.StartsWith("が")).ToListAsync()),
                    "LinqEndsWith:" + Ids(await items.Query().Where(item => item.Name.EndsWith("が")).ToListAsync()),
                    "LinqSoftHyphen:" + Ids(await items.Query().Where(item => item.Name.StartsWith("­a")).ToListAsync())
                );
        }
        """;

    /// <summary>序数で比べたときの答え（docs の宣言）</summary>
    private const string OrdinalExpected =
        "StartsWith:1;EndsWith:3;Contains:1,3;SoftHyphen:;LinqStartsWith:1;LinqEndsWith:3;LinqSoftHyphen:";

    [Theory(
        DisplayName = "インメモリの前方一致・後方一致は、簡易 DSL でも手書きの LINQ でも序数で比べ、カルチャに依らない"
    )]
    [InlineData(false, "")]
    [InlineData(false, "ja-JP")]
    [InlineData(false, "tr-TR")]
    [InlineData(true, "")]
    [InlineData(true, "ja-JP")]
    public async Task StringMatch_IsOrdinal(bool valueObjects, string culture)
    {
        using var scope = new CultureScope(culture);
        var diagram = BuildDiagram();
        var probe = LoadProbe(diagram, valueObjects);
        var path = Path.Combine(Path.GetTempPath(), $"quicker-match-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString();

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(Ct);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    new SqliteDdlGenerator().Build(diagram)
                    + $"\nINSERT INTO items (id, name) VALUES {Rows};";
                await command.ExecuteNonQueryAsync(Ct);
            }

            var results = await (Task<string[]>)
                probe.GetMethod("RunAllAsync")!.Invoke(null, [connectionString])!;

            results.Should().Equal($"ado={OrdinalExpected}", $"inmemory={OrdinalExpected}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Type LoadProbe(ErDiagram diagram, bool valueObjects)
    {
        var provider = new SqliteProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
                GenerateInMemoryRepositories = true,
                GenerateValueObjects = valueObjects,
                GenerateEditModels = false,
                GenerateMappers = false,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.StringMatch.Tests.{Guid.NewGuid():N}",
            Probe
        );
        return assembly.GetType("Sample.Domain.MatchProbe")!;
    }

    /// <summary>素の string 列 1 本の表と、前方・後方・部分一致の簡易 DSL のクエリ</summary>
    internal static ErDiagram BuildDiagram()
    {
        var items = new Entity
        {
            TableName = "items",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "INTEGER",
                    IsPrimaryKey = true,
                },
                new Column
                {
                    Name = "name",
                    DataType = "TEXT",
                    IsNullable = false,
                },
            ],
        };

        QueryDefinition Query(string name, string condition) =>
            new()
            {
                EntityId = items.Id,
                Name = name,
                Returns = QueryReturnShape.List,
                Condition = condition,
            };

        return new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities = [items],
            Queries =
            [
                Query("StartsComposed", "name LIKE 'が%'"),
                Query("EndsComposed", "name LIKE '%が'"),
                Query("ContainsComposed", "name LIKE '%が%'"),
                Query("StartsSoftHyphen", "name LIKE '­a%'"),
            ],
        };
    }
}
