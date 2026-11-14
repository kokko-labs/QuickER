using System.Globalization;
using AwesomeAssertions;
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
/// 単位ごと・実装先ごとに新しいデータベースを作って捨てる。シードは普段の既定で 1 つ・夜間は <see cref="NightlySeedCount"/>
/// （<c>QUICKER_FUZZ_SEEDS</c> を指定したときはその数）。
/// </para>
/// </remarks>
[Collection(SqlServerDslFuzzContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class DslConditionFuzzSqlServerRuntimeTests(SqlServerContainerFixture fixture)
{
    /// <summary>このクラスが受け持つ失敗のキーの接頭辞</summary>
    private const string Suite = "dsl-sqlserver";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeedCount = 16;

    /// <summary>
    /// 種の決め方（<c>QUICKER_FUZZ_SEEDS</c> を名前の総当たりと共有する。普段の既定は先頭の種 1 つ＝1 種でも実コンテナで時間が掛かるため）
    /// </summary>
    public static SeedSettings Seeds { get; } =
        new(NameFuzz.SeedCountVariable, 1, NightlySeedCount);

    [Fact(
        DisplayName = "簡易 DSL の条件の総当たり（SQL Server）: 生成→実コンパイル→QuickER 版 Repository と EF Core の結果が docs の意味と一致する（既知の失敗の一覧と厳密に一致）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
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
        var plan = SeedSource.Resolve(Seeds);
        plan.WriteWarnings();
        var coverage = new DslFuzzCoverage(DslFuzzDimensions.SqlServerScopes);

        // 網羅の単位（DslFuzzUnits.Coverage）を種の前に流して数える（種を 1 つだけ流すときは流さない）。単位のクエリは
        // どの実装先でも照合したクエリにならなければ名指しで赤にする
        // time の列と範囲の外の値の単位は QuickER 版だけで流す（EF Core 版は範囲の外の値を送れず例外にする＝docs の TimeSpan の
        // 既知の割り切り）
        var adoTarget = target with
        {
            Backends = [.. target.Backends.Where(backend => backend.Name == DslFuzzBackends.Ado)],
        };
        (DslFuzzUnit Unit, DslFuzzTarget Target)[] coverageUnits = plan.IsSingle
            ? []
            :
            [
                (DslFuzzUnits.Coverage(DialectNames.SqlServer), target),
                (DslFuzzUnits.PlainCoverage(DialectNames.SqlServer), target),
                .. DslFuzzUnits
                    .FamiliesCoverage(DialectNames.SqlServer)
                    .Select(unit => (unit, target)),
                (DslFuzzUnits.SqlServerAnsiCoverage(), target),
                (DslFuzzUnits.SqlServerMoneyCoverage(), target),
                .. DslFuzzUnits
                    .TimeSpanCoverage(DialectNames.SqlServer)
                    .Select(unit => (unit, target)),
                .. DslFuzzUnits
                    .DateTimeOffsetCoverage(DialectNames.SqlServer)
                    .Select(unit => (unit, target)),
                (DslFuzzUnits.SqlServerOutOfDayCoverage(), adoTarget),
            ];

        // 単位・実装先ごとに別のデータベースを作るので、単位を並べて流せる
        await DslFuzzRunner.RunSuiteAsync(
            coverage,
            plan,
            [.. coverageUnits.Select(pair => new DslFuzzRun(pair.Unit, pair.Target))],
            [
                .. plan.Seeds.SelectMany(seed =>
                    DslFuzzUnits.SqlServer(seed).Select(unit => new DslFuzzRun(unit, target))
                ),
            ],
            [$"{Suite}-"],
            Ct
        );
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
                Database(
                    fixture,
                    DslFuzzBackends.Ado,
                    (probe, cs) => DslFuzzProbe.Ado(probe, cs)
                ) with
                {
                    // QuickER 版は固定長の列とは比べる値を保存したときの値に直してから長さまで比べる（docs のバイナリの段落）
                    BinaryRule = DslBinaryRule.FixedLengthAsStored,
                },
                Database(
                    fixture,
                    DslFuzzBackends.EfCore,
                    (probe, cs) =>
                        DslFuzzProbe.EfCore(
                            probe,
                            options => options.UseSqlServer(cs).IgnoreServiceProviderCacheLimit()
                        )
                ) with
                {
                    // EF Core 版は SQL Server の比べ方のまま（末尾の 0x00 だけが違う値を等しいとみなす＝docs のバイナリの段落の既知の割り切り）
                    BinaryRule = DslBinaryRule.TrailingZerosIgnored,
                },
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DslFuzzBackends.InMemory] =
                    "インメモリ Repository は方言に依らず式木を評価するので、SQLite の構成（QuickER.Tests の DslConditionFuzzTests）で流す",
            }
        );

    /// <summary>単位ごとに新しいデータベースを作り、DDL を当てて開く実装先（閉じるときにデータベースを捨てる）</summary>
    /// <remarks>
    /// 接続はプールする。プールは接続文字列ごとで、データベースの名前が単位・実装先ごとに違うので、単位どうしで接続を共有しない。
    /// プールしないとクエリごとに TCP の接続を張り直し、単位を並べて流すと TIME_WAIT がエフェメラルポートを使い切って
    /// 接続が断られる（Windows の Docker Desktop で実測）。データベースを捨てる前にそのプールを空にする。
    /// </remarks>
    private static DslFuzzBackend Database(
        SqlServerContainerFixture fixture,
        string name,
        Func<Type, string, ServiceProvider> build
    ) =>
        new(
            name,
            DslCaseRule.Collation,
            DslNullOrder.FirstWhenAscending,
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
                    {
                        using (var connection = new SqlConnection(cs))
                        {
                            SqlConnection.ClearPool(connection);
                        }

                        await fixture.ExecuteAsync(
                            $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];",
                            CancellationToken.None
                        );
                    }
                );
            }
        );
}
