using System.Globalization;
using System.IO;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.MySql;
using QuickER.Provider.Sqlite;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 簡易 DSL の条件の総当たり（区切り 23a）: 列ごとに書ける全ての形の原子（値は乱数）と、乱数で組んだ合成の条件を
/// 生成→実コンパイル（エラー 0・警告 0）し、NULL を含むデータで QuickER 版 Repository（SQLite）・EF Core（SQLite）・
/// インメモリへ流して、同じ実装先から読み戻した全行へ docs の意味のオラクル（<see cref="DslFuzz.Evaluate"/>）を当てた結果と
/// 照合するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 本体は <see cref="DslFuzzRunner"/>。結果は既知の失敗の一覧（<see cref="DslFuzzKnownFailures"/>）と厳密に照合する
/// （一覧に無い失敗も、載っているのに起きない失敗も赤）。このクラスが受け持つ入口は <c>dsl-generate:</c>・<c>dsl-compile:</c>・
/// <c>dsl-exec:</c>（SQL Server の派生は Integration の <c>dsl-sqlserver-</c>）。
/// </para>
/// <para>
/// MySQL の単位（符号なし整数・sbyte）は MySQL の QuickER 版 Repository が無いため、インメモリでだけ実行する。
/// シードの数は <c>QUICKER_FUZZ_SEEDS</c>（既定 2）。
/// </para>
/// <para>
/// 網羅の単位（値オブジェクトありの <see cref="DslFuzzUnits.Coverage"/> と、値オブジェクトなしの
/// <see cref="DslFuzzUnits.PlainCoverage"/>）は書式のカルチャをインバリアントにして流し、SQLite の 2 単位の序数の比較のクエリ
/// （<see cref="DslFuzzGenerator.OrdinalQueryPrefix"/>）だけをインメモリの範囲で ja-JP・tr-TR でも流し直す（インメモリの前方・
/// 後方一致は序数で、カルチャに依らない）。
/// </para>
/// </remarks>
public class DslConditionFuzzTests
{
    /// <summary>このクラスが受け持つ失敗のキーの接頭辞</summary>
    private const string Suite = "dsl";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>SQLite の 3 実装先</summary>
    private static readonly DslFuzzTarget SqliteTarget = new(
        Suite,
        DialectNames.Sqlite,
        new SqliteProvider(),
        unit => Options(unit, ado: true),
        "AddGeneratedSqliteRepositories",
        EfCore: true,
        InMemory: true,
        SqliteBackendAxis().Adapters
    );

    /// <summary>MySQL の図（インメモリだけ）</summary>
    private static readonly DslFuzzTarget MySqlTarget = new(
        Suite,
        DialectNames.MySql,
        new MySqlProvider(),
        unit => Options(unit, ado: false),
        AdoRegistration: null,
        EfCore: false,
        InMemory: true,
        MySqlBackendAxis().Adapters
    );

    /// <summary>SQLite の構成の実装先の軸（3 実装先とも流す）</summary>
    internal static MatrixAxis<DslFuzzBackend> SqliteBackendAxis() =>
        DslFuzzBackends.Axis(
            DialectNames.Sqlite,
            [
                SqliteFile(
                    DslFuzzBackends.Ado,
                    DslCaseRule.AsciiFold,
                    (probe, cs) => DslFuzzProbe.Ado(probe, cs),
                    excludes: null
                ),
                SqliteFile(
                    DslFuzzBackends.EfCore,
                    DslCaseRule.Indeterminate,
                    (probe, cs) =>
                        DslFuzzProbe.EfCore(
                            probe,
                            options => options.UseSqlite(cs).IgnoreServiceProviderCacheLimit()
                        ),
                    ExcludedOnEfCoreSqlite
                ),
                DslFuzzBackend.InMemory,
            ]
        );

    /// <summary>MySQL の構成の実装先の軸（インメモリだけ）</summary>
    internal static MatrixAxis<DslFuzzBackend> MySqlBackendAxis() =>
        DslFuzzBackends.Axis(
            DialectNames.MySql,
            [DslFuzzBackend.InMemory],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DslFuzzBackends.Ado] =
                    "QuickER 版 Repository は SQL Server と SQLite の方言しか生成しない（MySQL の方言を RepositoryDialects に指定すると生成時の検証エラー）ので、MySQL の構成は GenerateRepositories を立てない",
                [DslFuzzBackends.EfCore] =
                    "EF Core 版 Repository は実 DB へ接続して流す実装先で、このテストは実 MySQL（Docker）を使わない。MySQL の構成は MySQL だけの列の型（符号なし整数・sbyte）の生成・型の検査と、インメモリでの意味を見るためのもの",
            }
        );

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeedCount = 80;

    /// <summary>
    /// 種の決め方（<c>QUICKER_FUZZ_SEEDS</c> を名前の総当たりと共有する。普段の数は名前と同じ 2・夜間の数はこのテストのもの）
    /// </summary>
    public static SeedSettings Seeds { get; } =
        new(NameFuzz.SeedCountVariable, NameFuzz.DefaultSeedCount, NightlySeedCount);

    [Fact(
        DisplayName = "簡易 DSL の条件の総当たり: 生成→実コンパイル（警告 0）→3 実装先の結果が docs の意味と一致する（既知の失敗の一覧と厳密に一致）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task DslConditions_MatchDocumentedSemantics()
    {
        var ledger = new FuzzFailureLog(DslFuzzKnownFailures.Set);

        var plan = SeedSource.Resolve(Seeds);
        plan.WriteWarnings();
        var coverage = new DslFuzzCoverage(DslFuzzDimensions.SqliteMySqlScopes);

        // 網羅の単位（DslFuzzUnits.Coverage）を種の前に流して数える（種を 1 つだけ流すときは流さない）。単位のクエリは
        // どの実装先でも照合したクエリにならなければ名指しで赤にする
        // 網羅の単位は書式のカルチャをインバリアントにして流し、SQLite の 2 単位の序数のクエリ（KO*）はインメモリの範囲だけ
        // ja-JP・tr-TR でも流す
        // （インメモリの文字列一致は序数＝docs。カルチャで比べると合成済みと分解形の「が」が等しく、ソフトハイフンが無視される）
        var coverageUnits = new List<(DslFuzzUnit Unit, DslFuzzTarget Target, string Culture)>();

        if (!plan.IsSingle)
        {
            coverageUnits.Add(
                (DslFuzzUnits.Coverage(DialectNames.Sqlite), SqliteTarget, InvariantCultureName)
            );
            coverageUnits.Add(
                (
                    DslFuzzUnits.PlainCoverage(DialectNames.Sqlite),
                    SqliteTarget,
                    InvariantCultureName
                )
            );
            coverageUnits.Add(
                (DslFuzzUnits.Coverage(DialectNames.MySql), MySqlTarget, InvariantCultureName)
            );
            coverageUnits.Add(
                (DslFuzzUnits.PlainCoverage(DialectNames.MySql), MySqlTarget, InvariantCultureName)
            );
            coverageUnits.AddRange(
                DslFuzzUnits
                    .FamiliesCoverage(DialectNames.Sqlite)
                    .Select(unit => (unit, SqliteTarget, InvariantCultureName))
            );
            coverageUnits.AddRange(
                DslFuzzUnits
                    .FamiliesCoverage(DialectNames.MySql)
                    .Select(unit => (unit, MySqlTarget, InvariantCultureName))
            );

            foreach (var culture in InMemoryCultures)
            {
                coverageUnits.Add(
                    (
                        OrdinalOnly(DslFuzzUnits.Coverage(DialectNames.Sqlite), culture),
                        SqliteInMemoryTarget,
                        culture
                    )
                );
                coverageUnits.Add(
                    (
                        OrdinalOnly(DslFuzzUnits.PlainCoverage(DialectNames.Sqlite), culture),
                        SqliteInMemoryTarget,
                        culture
                    )
                );
            }
        }

        foreach (var (unit, target, culture) in coverageUnits)
        {
            using var scope = new CultureScope(culture);
            await DslFuzzRunner.RunAsync(ledger, unit, target, Ct, coverage, minimumChecked: 0);
        }

        foreach (var seed in plan.Seeds)
        {
            foreach (var unit in DslFuzzUnits.Sqlite(seed))
            {
                await DslFuzzRunner.RunAsync(ledger, unit, SqliteTarget, Ct, coverage);
            }

            foreach (var unit in DslFuzzUnits.MySql(seed))
            {
                await DslFuzzRunner.RunAsync(ledger, unit, MySqlTarget, Ct, coverage);
            }
        }

        foreach (var unit in DslFuzzUnits.Collisions())
        {
            await DslFuzzRunner.RunAsync(ledger, unit, SqliteTarget, Ct, coverage);
        }

        // 網羅の宣言の誤りは照合の結果より先に赤にする
        var coverageReports = coverage.Reports();
        DslFuzzCoverage.WriteSummaries(coverageReports);

        ledger.AssertMatchesKnown($"{Suite}-generate:", $"{Suite}-compile:", $"{Suite}-exec:");
        coverageUnits
            .SelectMany(pair =>
                coverage.UncheckedProblems(
                    pair.Unit,
                    pair.Target.Dbms,
                    pair.Target.Backends.Select(backend => backend.Name)
                )
            )
            .Should()
            .BeEmpty();

        // 網羅しない組は、照合の結果を表明してから照合する（既定の種の数のときだけ）
        DslFuzzCoverage.AssertGaps(coverageReports, plan);
    }

    /// <summary>網羅の単位を流す書式のカルチャ（インバリアント）の名前</summary>
    private static readonly string InvariantCultureName = CultureInfo.InvariantCulture.Name;

    /// <summary>
    /// SQLite の網羅の単位をインメモリの範囲だけで流し直すカルチャ（合成済みと分解形を等しくみなす日本語と、
    /// i の大文字小文字が違うトルコ語）
    /// </summary>
    private static readonly string[] InMemoryCultures = ["ja-JP", "tr-TR"];

    /// <summary>SQLite の構成のインメモリだけ（網羅の単位をカルチャを変えて流し直す）</summary>
    private static readonly DslFuzzTarget SqliteInMemoryTarget = SqliteTarget with
    {
        Backends = [DslFuzzBackend.InMemory],
    };

    /// <summary>
    /// カルチャを変えて流し直す単位：序数の比較を確かめるクエリ（<see cref="DslFuzzGenerator.OrdinalQueryPrefix"/>）だけに絞り、
    /// 名前にカルチャを添える（失敗のキーと名指し）
    /// </summary>
    /// <remarks>
    /// カルチャで結果が変わり得るのはインメモリの文字列一致の比べ方だけで、ほかのクエリは書式のカルチャに触れない（生成物の
    /// カルチャ不変は <c>CultureInvariantGenerationTests</c> と生成コードのアナライザが見る）。絞った結果が空なら、黙って空振りしない
    /// よう例外にする。
    /// </remarks>
    private static DslFuzzUnit OrdinalOnly(DslFuzzUnit unit, string culture)
    {
        var queries = unit
            .Queries.Where(query =>
                query.Name.StartsWith(DslFuzzGenerator.OrdinalQueryPrefix, StringComparison.Ordinal)
            )
            .ToList();

        if (queries.Count == 0)
        {
            throw new InvalidOperationException(
                $"網羅の単位 {unit.Name} に序数の比較のクエリ（{DslFuzzGenerator.OrdinalQueryPrefix}*）がありません。"
            );
        }

        return unit with
        {
            Name = $"{unit.Name}@{culture}",
            Queries = queries,
        };
    }

    /// <summary>
    /// EF Core（SQLite）で流さないクエリか: decimal の列の順序比較を含むものと、decimal の列で並べるもの
    /// </summary>
    /// <remarks>
    /// EF Core の SQLite プロバイダは decimal のサーバー側の順序比較・並び替えを翻訳できず例外にする（プロバイダの既知の制限＝
    /// CLAUDE.md の名前付きクエリの節「EF Core Sqlite は decimal のサーバーサイド比較・並び替え非対応」）。
    /// QuickER の不具合ではないので、この 1 か所で EF Core（SQLite）の実行から外す（ほかの実装先では流す）。
    /// </remarks>
    private static bool ExcludedOnEfCoreSqlite(DslQuery query) =>
        query.Atoms.Any(atom =>
            atom is DslCompare { Op: not (DslOp.Eq or DslOp.Ne) }
            && atom.Column.Clr == DslClr.Decimal
        ) || query.Orderings.Any(ordering => ordering.Column.Clr == DslClr.Decimal);

    private static CodeGenerationOptions Options(DslFuzzUnit unit, bool ado) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = ado,
            RepositoryDialects = ado ? ["sqlite"] : [],
            GenerateEfCoreRepositories = ado,
            GenerateInMemoryRepositories = true,
            GenerateValueObjects = unit.ValueObjects,
            UseGuidKeyForStringPrimaryKey = unit.Key.Clr == DslClr.GuidKey,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

    /// <summary>単位ごとに新しい SQLite のファイルへ DDL を当てて開く実装先</summary>
    private static DslFuzzBackend SqliteFile(
        string name,
        DslCaseRule rule,
        Func<Type, string, Microsoft.Extensions.DependencyInjection.ServiceProvider> build,
        Func<DslQuery, bool>? excludes
    ) =>
        new(
            name,
            rule,
            DslNullOrder.FirstWhenAscending,
            async (probe, diagram, ct) =>
            {
                var path = Path.Combine(
                    Path.GetTempPath(),
                    $"quicker-dsl-{name}-{Guid.NewGuid():N}.db"
                );
                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = false,
                }.ToString();

                await using (var connection = new SqliteConnection(cs))
                {
                    await connection.OpenAsync(ct);
                    await using var command = connection.CreateCommand();
                    command.CommandText = new SqliteDdlGenerator().Build(diagram);
                    await command.ExecuteNonQueryAsync(ct);
                }

                return new DslFuzzSession(
                    build(probe, cs),
                    () =>
                    {
                        File.Delete(path);
                        return ValueTask.CompletedTask;
                    }
                );
            },
            excludes
        );
}
