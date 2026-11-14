using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 簡易 DSL の条件の総当たりを、EF Core 版 Repository だけを実 DB で流す方言（PostgreSQL・MySQL・Oracle）で流す共有の手順
/// （<see cref="NamedQueryDslConditionFuzzPostgreSqlRuntimeTests"/>・<see cref="NamedQueryDslConditionFuzzMySqlRuntimeTests"/>・
/// <see cref="NamedQueryDslConditionFuzzOracleRuntimeTests"/>）
/// </summary>
/// <remarks>
/// <para>
/// 本体（生成器・オラクル・診断の引き当て・照合）は QuickER.Tests の <see cref="DslFuzzRunner"/> を共有し、手順は SQL Server の派生
/// （<see cref="DslConditionFuzzSqlServerRuntimeTests"/>）と同じ：網羅の単位を種の前に流し、種の単位を流し、既知の失敗の一覧と厳密に
/// 照合し、網羅しない組を照合する。
/// </para>
/// <para>
/// QuickER 版 Repository はこの 3 方言を生成しない（<c>CodeGenerationOptions.SupportedRepositoryDialects</c> は SQL Server と SQLite）。
/// インメモリ Repository は方言に依らず式木を評価するので、SQLite・MySQL の構成（QuickER.Tests の DslConditionFuzzTests）で流す。
/// 単位ごとにスキーマを作り直す（コンテナのスキーマを空にして、その方言の DDL を当てる）。
/// </para>
/// </remarks>
internal static class DslConditionFuzzEfCore
{
    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeedCount = 16;

    /// <summary>
    /// 種の決め方（<c>QUICKER_FUZZ_SEEDS</c> を名前の総当たりと共有する。普段の既定は先頭の種 1 つ＝1 種でも実コンテナで時間が掛かるため）
    /// </summary>
    public static SeedSettings Seeds { get; } =
        new(NameFuzz.SeedCountVariable, 1, NightlySeedCount);

    /// <summary>QuickER 版 Repository を流さない理由</summary>
    private const string NoAdoReason =
        "QuickER 版 Repository は SQL Server と SQLite の方言しか生成しない（CodeGenerationOptions.SupportedRepositoryDialects）ので、この方言の構成は GenerateRepositories を立てない";

    /// <summary>インメモリ Repository を流さない理由</summary>
    private const string NoInMemoryReason =
        "インメモリ Repository は方言に依らず式木を評価するので、SQLite・MySQL の構成（QuickER.Tests の DslConditionFuzzTests）で流す";

    /// <summary>
    /// 方言の構成の実装先の軸（EF Core 版だけ）。データベースには実装先を開くときにだけ触れるので、軸を作るだけなら Docker は要らない
    /// </summary>
    /// <param name="dbms">図の対象 DBMS</param>
    /// <param name="rule">文字列の大文字小文字・空白の扱い（docs の表の方言の行）</param>
    /// <param name="nullOrder">並び替えで NULL を置く位置（docs のインメモリの節「実 DB との既知の乖離」）</param>
    /// <param name="resetAndApplyDdl">スキーマを空にして DDL を当てる</param>
    /// <param name="configure">EF Core のプロバイダの指定</param>
    public static MatrixAxis<DslFuzzBackend> BackendAxis(
        string dbms,
        DslCaseRule rule,
        DslNullOrder nullOrder,
        Func<string, CancellationToken, Task> resetAndApplyDdl,
        Action<DbContextOptionsBuilder> configure
    ) =>
        DslFuzzBackends.Axis(
            dbms,
            [
                new DslFuzzBackend(
                    DslFuzzBackends.EfCore,
                    rule,
                    nullOrder,
                    async (probe, diagram, ct) =>
                    {
                        await resetAndApplyDdl(DdlOf(dbms, diagram), ct);

                        return new DslFuzzSession(
                            DslFuzzProbe.EfCore(
                                probe,
                                options =>
                                {
                                    configure(options);
                                    options.IgnoreServiceProviderCacheLimit();
                                }
                            )
                        );
                    },
                    // 単位ごとにコンテナの同じスキーマを空にして使うので、開いてから閉じるまでをほかの単位と重ねない
                    Exclusive: new SemaphoreSlim(1, 1)
                ),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DslFuzzBackends.Ado] = NoAdoReason,
                [DslFuzzBackends.InMemory] = NoInMemoryReason,
            }
        );

    /// <summary>方言の構成（EF Core 版 Repository 単独の生成）</summary>
    /// <param name="suite">失敗のキーの入口の接頭辞</param>
    /// <param name="dbms">図の対象 DBMS</param>
    /// <param name="provider">型を解決する方言プロバイダ</param>
    /// <param name="axis">実装先の軸</param>
    public static DslFuzzTarget Target(
        string suite,
        string dbms,
        IDatabaseProvider provider,
        MatrixAxis<DslFuzzBackend> axis
    ) =>
        new(
            suite,
            dbms,
            provider,
            unit => new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCoreRepositories = true,
                GenerateValueObjects = unit.ValueObjects,
                UseGuidKeyForStringPrimaryKey = unit.Key.Clr == DslClr.GuidKey,
                GenerateEditModels = false,
                GenerateMappers = false,
            },
            AdoRegistration: null,
            EfCore: true,
            InMemory: false,
            axis.Adapters
        );

    /// <summary>網羅の単位と種の単位を流して照合する</summary>
    /// <param name="target">方言の構成</param>
    /// <param name="scopes">網羅を数える範囲</param>
    /// <param name="units">種の単位</param>
    /// <param name="coverageUnits">網羅の単位（種を 1 つだけ流すときは流さない）</param>
    /// <param name="ct">取り消し</param>
    public static async Task RunAsync(
        DslFuzzTarget target,
        IReadOnlyList<string> scopes,
        Func<int, IEnumerable<DslFuzzUnit>> units,
        IReadOnlyList<DslFuzzUnit> coverageUnits,
        CancellationToken ct
    )
    {
        var plan = SeedSource.Resolve(Seeds);
        plan.WriteWarnings();
        var coverage = new DslFuzzCoverage(scopes);
        IReadOnlyList<DslFuzzUnit> coverageRun = plan.IsSingle ? [] : coverageUnits;

        // 実装先はコンテナの 1 つのスキーマを単位ごとに作り直すので、実行は排他（BackendAxis の Exclusive）で 1 本にし、
        // 生成・コンパイルだけを並べる
        await DslFuzzRunner.RunSuiteAsync(
            coverage,
            plan,
            [.. coverageRun.Select(unit => new DslFuzzRun(unit, target))],
            [
                .. plan.Seeds.SelectMany(seed =>
                    units(seed).Select(unit => new DslFuzzRun(unit, target))
                ),
            ],
            [$"{target.Suite}-"],
            ct
        );
    }

    private static string DdlOf(string dbms, ErDiagram diagram) =>
        dbms switch
        {
            QuickER.Dialects.DialectNames.PostgreSql =>
                new QuickER.Provider.PostgreSql.PostgreSqlDdlGenerator().Build(diagram),
            QuickER.Dialects.DialectNames.MySql =>
                new QuickER.Provider.MySql.MySqlDdlGenerator().Build(diagram),
            QuickER.Dialects.DialectNames.Oracle =>
                new QuickER.Provider.Oracle.OracleDdlGenerator().Build(diagram),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dbms),
                dbms,
                "EF Core 版だけを流す方言ではない"
            ),
        };
}
