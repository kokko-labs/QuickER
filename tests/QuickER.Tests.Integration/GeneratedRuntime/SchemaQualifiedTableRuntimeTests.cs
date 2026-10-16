using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// スキーマで修飾された図のテーブル名（<c>sales.orders</c>）を、EF Core 版と QuickER 版 Repository が同じテーブル
/// （<c>[sales].[orders]</c>）として読み書きすることを、実 SQL Server で固定する
/// </summary>
/// <remarks>
/// EF Core の <c>ToTable(name)</c> は名前を <c>.</c> で分けないので、生成した Fluent 構成が分けずに渡すと、EF Core だけが
/// 既定のスキーマの、名前に <c>.</c> を含むテーブル（<c>[sales.orders]</c>）を指していた（QuickER 版 Repository は最初の <c>.</c> で分ける）。
/// 図から生成コードを作ってその場でコンパイルするので、フィクスチャのドリフトは無い。
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SchemaQualifiedTableRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Probe = """
        using System;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        namespace Sample.Domain;

        public static class SchemaProbe
        {
            public static async Task<string[]> RunAsync(string connectionString, Action<DbContextOptionsBuilder> configure)
            {
                await using var ado = new ServiceCollection()
                    .AddGeneratedSqlServerRepositories(connectionString)
                    .BuildServiceProvider();
                await using var efCore = new ServiceCollection()
                    .AddGeneratedEfCoreRepositories(configure)
                    .BuildServiceProvider();

                var adoOrders = ado.GetRequiredService<ISalesOrderRepository>();
                var efCoreOrders = efCore.GetRequiredService<ISalesOrderRepository>();

                await efCoreOrders.InsertAsync(new SalesOrderEntity { Id = 1, Name = "ef-core" });
                await adoOrders.InsertAsync(new SalesOrderEntity { Id = 2, Name = "ado" });

                static string Rows(System.Collections.Generic.IEnumerable<SalesOrderEntity> rows) =>
                    string.Join(",", rows.OrderBy(row => row.Id).Select(row => $"{row.Id}:{row.Name}"));

                return ["ado=" + Rows(await adoOrders.GetAllAsync()), "ef-core=" + Rows(await efCoreOrders.GetAllAsync())];
            }
        }
        """;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "IF SCHEMA_ID(N'sales') IS NULL EXEC (N'CREATE SCHEMA [sales]');",
            Ct
        );
        await fixture.ApplyDdlAsync(BuildDiagram(), Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (!fixture.IsAvailable)
        {
            return;
        }

        await fixture.ResetSchemaAsync(CancellationToken.None);
        await fixture.ExecuteAsync(
            "IF SCHEMA_ID(N'sales') IS NOT NULL EXEC (N'DROP SCHEMA [sales]');",
            CancellationToken.None
        );
    }

    [Fact(
        DisplayName = "スキーマで修飾されたテーブルを、EF Core 版と QuickER 版 Repository が同じテーブル [sales].[orders] として読み書きする"
    )]
    public async Task SchemaQualifiedTable_IsTheSameTableForEfCoreAndQuickErRepository()
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            BuildDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver"],
                GenerateEfCoreRepositories = true,
                GenerateEditModels = false,
                GenerateMappers = false,
            }
        );
        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var probe = GeneratedCodeCompiler
            .CompileAndLoad(result, $"QuickER.SchemaQualified.Tests.{Guid.NewGuid():N}", Probe)
            .GetType("Sample.Domain.SchemaProbe")!;
        Action<DbContextOptionsBuilder> configure = options =>
            options.UseSqlServer(fixture.ConnectionString).IgnoreServiceProviderCacheLimit();

        var rows = await (Task<string[]>)
            probe.GetMethod("RunAsync")!.Invoke(null, [fixture.ConnectionString, configure])!;

        rows.Should().Equal("ado=1:ef-core,2:ado", "ef-core=1:ef-core,2:ado");

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT (SELECT COUNT(*) FROM [sales].[orders]), CASE WHEN OBJECT_ID(N'[dbo].[sales.orders]') IS NULL THEN 0 ELSE 1 END;";
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue();
        reader.GetInt32(0).Should().Be(2, "どちらの実装先も [sales].[orders] へ書いた");
        reader.GetInt32(1).Should().Be(0, "既定のスキーマの、名前に . を含むテーブルは無い");
    }

    private static ErDiagram BuildDiagram() =>
        new()
        {
            TargetDbms = "sqlserver",
            Entities =
            [
                new Entity
                {
                    TableName = "sales.orders",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                        new Column
                        {
                            Name = "name",
                            DataType = "nvarchar(20)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };
}
