using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core 版 Repository の素の Guid の単一列の主キーは EF Core の規約のままで、キーが <c>Guid.Empty</c> の行を挿入すると
/// EF Core が値を作って挿入することを、生成→実行（SQLite）で検証するテストクラス。
/// </summary>
/// <remarks>
/// <c>ValueGeneratedNever()</c> は、EF Core が意味のある値を作れない主キー（整数など）だけに出す。Guid の主キーに出すと
/// <c>Guid.Empty</c> がそのまま書かれ、EF Core の採番に頼るコードは 2 行目から主キーの重複になる。QuickER 版 Repository は
/// <c>Guid.Empty</c> をそのまま書く（実装先の違い・docs の EF Core モードの節）。
/// </remarks>
public class EfCoreGuidKeyExecutionTests
{
    /// <summary>生成物と一緒にコンパイルする検証コード（キーが Guid.Empty の行を 2 つ挿入し、保存されたキーを返す）</summary>
    private const string Probe = """
        using System;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        namespace Sample.Domain;

        public static class GuidKeyProbe
        {
            public static async Task<Guid[]> InsertTwoEmptyKeysAsync(Action<DbContextOptionsBuilder> configureEfCore)
            {
                await using var efCore = new ServiceCollection()
                    .AddGeneratedEfCoreRepositories(configureEfCore)
                    .BuildServiceProvider();
                var tokens = efCore.GetRequiredService<ITokenRepository>();

                await tokens.InsertAsync(new TokenEntity { TokenId = Guid.Empty, Label = "a" });
                await tokens.InsertAsync(new TokenEntity { TokenId = Guid.Empty, Label = "b" });

                return (await tokens.GetAllAsync()).Select(token => token.TokenId).ToArray();
            }
        }
        """;

    [Fact(
        DisplayName = "EF Core: 素の Guid の主キーが Guid.Empty の行を 2 つ挿入すると、EF Core がそれぞれ値を作って 2 行入る"
    )]
    public async Task EmptyGuidKeys_AreGeneratedByEfCore()
    {
        var diagram = BuildDiagram();
        var probe = LoadProbe(diagram);
        var path = Path.Combine(Path.GetTempPath(), $"quicker-guid-key-{Guid.NewGuid():N}.db");
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
                command.CommandText = new SqliteDdlGenerator().Build(diagram);
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            Action<DbContextOptionsBuilder> configureEfCore = options =>
                options.UseSqlite(connectionString).IgnoreServiceProviderCacheLimit();
            var keys = await (Task<Guid[]>)
                probe.GetMethod("InsertTwoEmptyKeysAsync")!.Invoke(null, [configureEfCore])!;

            keys.Should().HaveCount(2);
            keys.Should().NotContain(Guid.Empty).And.OnlyHaveUniqueItems();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>図を生成し、検証コードと一緒にコンパイルして検証コードの型を返す</summary>
    private static Type LoadProbe(ErDiagram diagram)
    {
        var provider = new SqliteProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCoreRepositories = true,
                GenerateEditModels = false,
                GenerateMappers = false,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.GuidKey.Tests.{Guid.NewGuid():N}",
            Probe
        );
        return assembly.GetType("Sample.Domain.GuidKeyProbe")!;
    }

    /// <summary>素の Guid（uniqueidentifier）の主キーを持つ tokens の図（SQLite）</summary>
    private static ErDiagram BuildDiagram() =>
        new()
        {
            TargetDbms = "sqlite",
            Entities =
            {
                new Entity
                {
                    TableName = "tokens",
                    Columns =
                    {
                        new Column
                        {
                            Name = "token_id",
                            DataType = "uniqueidentifier",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "label",
                            DataType = "TEXT",
                            IsNullable = false,
                        },
                    },
                },
            },
        };
}
