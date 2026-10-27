using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Provider.SqlServer;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

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
            DialectNames.SqlServer,
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
            BackendAxis(fixture).Adapters
        );
        var ledger = new FuzzFailureLog(DslFuzzKnownFailures.Set);
        var seedsSpecified =
            Environment.GetEnvironmentVariable("QUICKER_FUZZ_SEEDS") is { Length: > 0 };
        var seeds = seedsSpecified ? NameFuzz.Seeds : [NameFuzz.Seeds[0]];
        var coverage = new DslFuzzCoverage(DslFuzzDimensions.SqlServerScopes);

        foreach (var seed in seeds)
        {
            foreach (var unit in DslFuzzUnits.SqlServer(seed))
            {
                await DslFuzzRunner.RunAsync(ledger, unit, target, Ct, coverage);
            }
        }

        // 網羅の宣言の誤りは照合の結果より先に赤にする
        var coverageReports = coverage.Reports();
        DslFuzzCoverage.WriteSummaries(coverageReports);

        ledger.AssertMatchesKnown($"{Suite}-");

        // 網羅しない組は、照合の結果を表明してから照合する。このテストの既定（QUICKER_FUZZ_SEEDS を指定しない＝先頭の種 1 つ）
        // のときだけ照合する（種の数で欠けが変わるため）
        if (!seedsSpecified)
        {
            DslFuzzCoverage.AssertGaps(coverageReports, plan: null);
        }
    }

    /// <summary>
    /// SQL Server の構成の実装先の軸（QuickER 版 Repository と EF Core）。コンテナには実装先を開くときにだけ触れるので、
    /// 軸を作るだけなら Docker は要らない（<see cref="MatrixAxisGuardTests"/> が Docker なしで作れることを確かめる）
    /// </summary>
    /// <param name="fixture">SQL Server のコンテナ（実装先を開くときにだけ使う）</param>
    internal static MatrixAxis<DslFuzzBackend> BackendAxis(SqlServerContainerFixture fixture) =>
        DslFuzzBackends.Axis(
            DialectNames.SqlServer,
            [
                Database(fixture, DslFuzzBackends.Ado, (probe, cs) => DslFuzzProbe.Ado(probe, cs)),
                Database(
                    fixture,
                    DslFuzzBackends.EfCore,
                    (probe, cs) =>
                        DslFuzzProbe.EfCore(
                            probe,
                            options => options.UseSqlServer(cs).IgnoreServiceProviderCacheLimit()
                        )
                ),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DslFuzzBackends.InMemory] =
                    "インメモリ Repository は方言に依らず式木を評価するので、SQLite の構成（QuickER.Tests の DslConditionFuzzTests）で流す",
            }
        );

    /// <summary>単位ごとに新しいデータベースを作り、DDL を当てて開く実装先（閉じるときにデータベースを捨てる）</summary>
    private static DslFuzzBackend Database(
        SqlServerContainerFixture fixture,
        string name,
        Func<Type, string, ServiceProvider> build
    ) =>
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
