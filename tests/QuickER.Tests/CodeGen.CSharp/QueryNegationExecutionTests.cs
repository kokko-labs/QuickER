using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前付きクエリの簡易 DSL の NOT が、QuickER 版 Repository（SQLite）・EF Core（SQLite）・インメモリの 3 実装先と
/// 値オブジェクトの有無に依らず同じ行を返すことを、生成→実行で検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 規則は「NOT と <c>&lt;&gt;</c> は C# の否定と同じ（NULL の行も一致する）」。エミッタは NOT をド・モルガンの法則で
/// 原子まで押し下げ、NULL 許容列の順序比較の否定には「列が NULL」を OR する。<c>!(...)</c> で包んだままだと、
/// SQL では中の NULL 由来の UNKNOWN が否定しても UNKNOWN のまま行を落とし、C# の否定（インメモリ）と割れる。
/// </para>
/// <para>
/// 期待値は C# の意味で手計算した行（行の内容はテスト中の <see cref="Rows"/>）。3 実装先は同じ DB 内容を見る
/// （SQLite の行を ADO で読み、そのままインメモリへ入れる）。
/// </para>
/// </remarks>
public class QueryNegationExecutionTests
{
    /// <summary>行（id, qty, name）。qty・name は NULL 許容</summary>
    private const string Rows =
        "(1, NULL, NULL), (2, 5, 'x'), (3, 20, 'y'), (4, 20, NULL), (5, NULL, 'x'), (6, 5, NULL)";

    /// <summary>C# の意味で求めた各クエリの結果（id の昇順）</summary>
    private const string Expected =
        "NotBelow:1,3,4,5;"
        + "NotEitherOr:1,3,4;"
        + "NotBoth:1,2,3,5,6;"
        + "NotNotBelow:2,6;"
        + "NotEqual:1,3,4,5;"
        + "NotIn:1,3,4,5;"
        + "NotNullCheck:2,3,5;"
        // 文字列一致は NULL の行がどちら向きでも一致しない（ワイルドカードなしの LIKE も、外側の NOT を押し下げた形も）
        + "NotLikeExact:3;"
        + "NotNotLikeExact:2,5;"
        + "NotLikeExactOrBelow:3;"
        + "LikeExactOrNot:2,3,5";

    /// <summary>生成物と一緒にコンパイルする検証コード（同じ手順を 3 実装先へ流す）</summary>
    private const string Probe = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        namespace Sample.Domain;

        public static class NegationProbe
        {
            public static async Task<string[]> RunAllAsync(
                string connectionString,
                Action<DbContextOptionsBuilder> configureEfCore
            )
            {
                await using var ado = new ServiceCollection()
                    .AddGeneratedSqliteRepositories(connectionString)
                    .BuildServiceProvider();
                await using var efCore = new ServiceCollection()
                    .AddGeneratedEfCoreRepositories(configureEfCore)
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

                return
                [
                    "ado=" + await RunAsync(adoItems),
                    "efcore=" + await RunAsync(efCore.GetRequiredService<IItemRepository>()),
                    "inmemory=" + await RunAsync(memoryItems),
                ];
            }

            private static string Ids(IEnumerable<ItemEntity> items) =>
                string.Join(",", items.Select(item => long.Parse(item.Id.ToString())).OrderBy(id => id));

            private static async Task<string> RunAsync(IItemRepository items) =>
                string.Join(
                    ";",
                    "NotBelow:" + Ids(await items.NotBelowAsync(10)),
                    "NotEitherOr:" + Ids(await items.NotEitherOrAsync(10)),
                    "NotBoth:" + Ids(await items.NotBothAsync(10)),
                    "NotNotBelow:" + Ids(await items.NotNotBelowAsync(10)),
                    "NotEqual:" + Ids(await items.NotEqualAsync(5)),
                    "NotIn:" + Ids(await items.NotInAsync(new List<long> { 5 })),
                    "NotNullCheck:" + Ids(await items.NotNullCheckAsync()),
                    "NotLikeExact:" + Ids(await items.NotLikeExactAsync()),
                    "NotNotLikeExact:" + Ids(await items.NotNotLikeExactAsync()),
                    "NotLikeExactOrBelow:" + Ids(await items.NotLikeExactOrBelowAsync(10)),
                    "LikeExactOrNot:" + Ids(await items.LikeExactOrNotAsync())
                );
        }
        """;

    [Theory(
        DisplayName = "簡易 DSL の NOT は 3 実装先と VO の有無に依らず C# の否定と同じ行を返す（NULL の行も一致する）"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Negation_MatchesCSharpSemanticsOnEveryBackend(bool valueObjects)
    {
        var diagram = BuildDiagram();
        var probe = LoadProbe(diagram, valueObjects);
        var path = Path.Combine(Path.GetTempPath(), $"quicker-negation-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString();

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    new SqliteDdlGenerator().Build(diagram)
                    + $"\nINSERT INTO items (id, qty, name) VALUES {Rows};";
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            Action<DbContextOptionsBuilder> configureEfCore = options =>
                options.UseSqlite(connectionString);
            var results = await (Task<string[]>)
                probe.GetMethod("RunAllAsync")!.Invoke(null, [connectionString, configureEfCore])!;

            results.Should().Equal($"ado={Expected}", $"efcore={Expected}", $"inmemory={Expected}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>図を生成し、検証コードと一緒にコンパイルして検証コードの型を返す</summary>
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
                GenerateEfCoreRepositories = true,
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
            $"QuickER.Negation.Tests.{Guid.NewGuid():N}",
            Probe
        );
        return assembly.GetType("Sample.Domain.NegationProbe")!;
    }

    /// <summary>NULL 許容の qty（INTEGER）と name（TEXT）を持つ items と、NOT を使うクエリの図（SQLite）</summary>
    private static ErDiagram BuildDiagram()
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
                new Column { Name = "qty", DataType = "INTEGER" },
                new Column { Name = "name", DataType = "TEXT" },
            ],
        };

        QueryDefinition Query(string name, string condition, params QueryParameter[] parameters)
        {
            var query = new QueryDefinition
            {
                EntityId = items.Id,
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

        QueryParameter Limit() => new() { Name = "limit", Type = "int64" };

        return new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities = [items],
            Queries =
            [
                Query("NotBelow", "NOT qty < @limit", Limit()),
                Query("NotEitherOr", "NOT (qty < @limit OR name = 'x')", Limit()),
                Query("NotBoth", "NOT (qty >= @limit AND name IS NULL)", Limit()),
                Query("NotNotBelow", "NOT NOT qty < @limit", Limit()),
                Query(
                    "NotEqual",
                    "NOT qty = @value",
                    new QueryParameter { Name = "value", Type = "int64" }
                ),
                Query(
                    "NotIn",
                    "NOT qty IN @values",
                    new QueryParameter
                    {
                        Name = "values",
                        Type = "int64",
                        IsList = true,
                    }
                ),
                Query("NotNullCheck", "NOT (name IS NULL)"),
                Query("NotLikeExact", "NOT name LIKE 'x'"),
                Query("NotNotLikeExact", "NOT NOT name LIKE 'x'"),
                Query("NotLikeExactOrBelow", "NOT (name LIKE 'x' OR qty < @limit)", Limit()),
                Query("LikeExactOrNot", "name LIKE 'x' OR NOT (name LIKE 'x' OR name IS NULL)"),
            ],
        };
    }
}
