using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Provider.SqlServer;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 簡易 DSL の条件の総当たり（区切り 23a）の SQL Server の派生: 実コンテナの QuickER 版 Repository（SQL Server 方言）と
/// EF Core（SQL Server）で、生成→実コンパイル→実行し、読み戻した行へ docs の意味のオラクルを当てた結果と照合するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 本体（生成器・オラクル・診断の引き当て・照合）は QuickER.Tests の <see cref="DslFuzzRunner"/> を共有する。
/// このクラスが受け持つ失敗のキーの入口は <c>dsl-sqlserver-</c> で、既知の失敗は <see cref="DslFuzzKnownFailures"/> に載せる。
/// </para>
/// <para>
/// 文字列の大文字小文字・末尾の空白は <see cref="DslCaseRule.Collation"/>（docs の表の SQL Server の行）で扱う。
/// 照合順序で結果が変わる行だけを判定せず、NULL の行と照合順序に依らず決まる行は判定する。
/// </para>
/// <para>
/// 単位ごと・実装先ごとに新しいデータベースを作って捨てる。シードは既定で 1 つ（<c>QUICKER_FUZZ_SEEDS</c> を指定したときだけ
/// その数）。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class DslConditionFuzzSqlServerRuntimeTests(SqlServerContainerFixture fixture)
{
    /// <summary>このクラスが受け持つ失敗のキーの接頭辞</summary>
    private const string Suite = "dsl-sqlserver";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "簡易 DSL の条件の総当たり（SQL Server）: 生成→実コンパイル→QuickER 版 Repository と EF Core の結果が docs の意味と一致する（既知の失敗の一覧と厳密に一致）"
    )]
    public async Task DslConditions_MatchDocumentedSemanticsOnSqlServer()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var target = new DslFuzzTarget(
            Suite,
            "sqlserver",
            new SqlServerProvider(),
            unit => new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver"],
                GenerateEfCoreRepositories = true,
                GenerateValueObjects = unit.ValueObjects,
                UseGuidKeyForStringPrimaryKey = unit.Key.Clr == DslClr.GuidKey,
                GenerateEditModels = false,
                GenerateMappers = false,
            },
            "AddGeneratedSqlServerRepositories",
            EfCore: true,
            InMemory: false,
            [
                Database("ado", (probe, cs) => DslFuzzProbe.Ado(probe, cs)),
                Database(
                    "efcore",
                    (probe, cs) => DslFuzzProbe.EfCore(probe, options => options.UseSqlServer(cs))
                ),
            ]
        );
        var ledger = new FuzzFailureLedger(DslFuzzKnownFailures.All);
        var seeds = Environment.GetEnvironmentVariable("QUICKER_FUZZ_SEEDS") is { Length: > 0 }
            ? NameFuzz.Seeds
            : [NameFuzz.Seeds[0]];

        foreach (var seed in seeds)
        {
            foreach (var unit in DslFuzzUnits.SqlServer(seed))
            {
                await DslFuzzRunner.RunAsync(ledger, unit, target, Ct);
            }
        }

        ledger.AssertMatchesKnown($"{Suite}-");
    }

    /// <summary>単位ごとに新しいデータベースを作り、DDL を当てて開く実装先（閉じるときにデータベースを捨てる）</summary>
    private DslFuzzBackend Database(string name, Func<Type, string, ServiceProvider> build) =>
        new(
            name,
            DslCaseRule.Collation,
            async (probe, diagram, ct) =>
            {
                var database = string.Create(
                    CultureInfo.InvariantCulture,
                    $"dslfuzz_{name}_{Guid.NewGuid():N}"
                );
                await fixture.ExecuteAsync($"CREATE DATABASE [{database}];", ct);

                var cs = new SqlConnectionStringBuilder(fixture.ConnectionString)
                {
                    InitialCatalog = database,
                    Pooling = false,
                }.ConnectionString;

                await using (var connection = new SqlConnection(cs))
                {
                    await connection.OpenAsync(ct);
                    await using var command = new SqlCommand(
                        new SqlServerDdlGenerator().Build(diagram),
                        connection
                    );
                    await command.ExecuteNonQueryAsync(ct);
                }

                return new DslFuzzSession(
                    build(probe, cs),
                    async () =>
                        await fixture.ExecuteAsync(
                            $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];",
                            CancellationToken.None
                        )
                );
            }
        );
}
