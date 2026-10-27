using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 名前の総当たり（<see cref="NameFuzz"/>）の名前を図のテーブル名・列名・一意制約名・外部キー名に置き、その方言の DDL を実 DB に
/// 適用して取り込み直すと、図との差分（<see cref="SchemaDiffService"/>）が空になることを確かめる共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 図は MCP のツールで作る（<see cref="NameFuzzDiagrams.BuildForRole"/>＝ツールが受け付けた名前だけが図に入る）。
/// 名前 1 つ × 置き場所 1 つごとに DB を空にして流すので、失敗はその名前とその置き場所へ帰着する。
/// 結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。
/// </para>
/// <para>
/// 設計上の制約として流さない組（理由つき）:
/// (1) ドットを含むテーブル名＝5 方言とも DDL が最初のドットでスキーマ修飾として分割して引用する（CLAUDE.md のプロバイダの対称性の
/// 「既知の割り切り」）。(2) Oracle の識別子は二重引用符を含められない（引用識別子の規則）。
/// </para>
/// <para>
/// 方言の識別子の長さの上限を超える名前と、方言が表せない文字を含む名前（MySQL の BMP の外の文字＝MySQL は断らずに黙って
/// <c>?</c> へ置き換える）は、DDL 生成の入口が場所を名指しして断る（<see cref="IdentifierLengthLimit.Accepts"/> /
/// <see cref="IdentifierLengthLimit.AcceptsTableName"/>）。総当たりはそれらも流し、「Model の宣言が断る名前は入口で断られ、
/// 受け付ける名前は実 DB に適用できて取り込み直すと差分が出ない」ことを表明する（断られなかった・断られたのに受け付ける名前だった、の
/// どちらも失敗として記録する）。
/// </para>
/// </remarks>
public abstract class NameFuzzDdlRoundTripTestsBase
{
    /// <summary>派生クラスが受け持つ方言の軸の名前（値は <see cref="DialectNames"/>）</summary>
    public const string DialectAxis = "dialect";

    /// <summary>方言のプロバイダ（DB には触れない）</summary>
    protected abstract IDatabaseProvider Provider { get; }

    /// <summary>方言の名前（プロバイダの名前。DB に触れないので、Docker なしで読める）</summary>
    internal string DialectName => Provider.Name;

    /// <summary>DB が使えるか（Docker の無い環境では false）</summary>
    protected abstract bool IsAvailable { get; }

    /// <summary>使えないときの理由</summary>
    protected virtual string UnavailableReason => string.Empty;

    /// <summary>DB を空にする</summary>
    protected abstract Task ResetAsync(CancellationToken ct);

    /// <summary>DDL を流す</summary>
    protected abstract Task ApplyAsync(string ddl, CancellationToken ct);

    /// <summary>取込の接続文字列</summary>
    protected abstract string ConnectionString { get; }

    /// <summary>その方言では設計上置けない名前か（理由は型の remarks）</summary>
    protected virtual bool IsOutOfDesign(string name, NameRole role) =>
        role == NameRole.Table && name.Contains('.');

    [Fact(
        DisplayName = "[Integration] 名前の総当たり: DDL を適用して取り込み直すと図との差分が空になる"
    )]
    public async Task NamesRoundTripThroughDdl()
    {
        Assert.SkipUnless(IsAvailable, UnavailableReason);

        var ct = TestContext.Current.CancellationToken;
        var ledger = new FuzzFailureLog(NameFuzzKnownFailures.Set);
        var dialect = Provider.Name;
        var executed = 0;
        var plan = SeedSource.Resolve(NameFuzz.SeedSettings);
        var coverage = new CoverageCounter(NameFuzzDdlRoundTripDimensions.Table);

        foreach (var name in NameFuzzDiagrams.NamesForRun())
        {
            foreach (var role in Enum.GetValues<NameRole>())
            {
                if (
                    IsOutOfDesign(name, role)
                    || NameFuzzDiagrams.BuildForRole(dialect, name, role) is not { } diagram
                )
                {
                    continue;
                }

                executed++;

                // 実際に流す名前と置き場所だけを数える（読むだけ）
                NameFuzzDdlRoundTripDimensions.Observe(coverage, name, role);
                await RoundTripAsync(
                    ledger,
                    $"ddl:{dialect}:{role}",
                    NameFuzzDiagrams.Classify(name),
                    diagram,
                    $"[{NameFuzz.Describe(name)}]",
                    role == NameRole.Table ? Limit.AcceptsTableName(name) : Limit.Accepts(name),
                    ct
                );
            }
        }

        // 長さの上限の後ろでだけ違う 2 つの名前（DB が上限で切り詰めると同じ名前になる）
        foreach (var (first, second, shape) in NameFuzzDiagrams.LongPairs)
        {
            if (NameFuzzDiagrams.BuildTables(dialect, first, second) is not { } diagram)
            {
                continue;
            }

            executed++;
            await RoundTripAsync(
                ledger,
                $"ddl:{dialect}:LongPair",
                shape,
                diagram,
                $"[{NameFuzz.Describe(first)}] / [{NameFuzz.Describe(second)}]",
                Limit.AcceptsTableName(first) && Limit.AcceptsTableName(second),
                ct
            );
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{dialect}: {executed} ケースを流した");

        // 網羅の宣言の誤りは往復の結果より先に赤にする
        var coverageReport = coverage.Report();
        coverageReport.WriteSummary();
        coverageReport.WriteGapUsage(NameFuzzDdlRoundTripDimensions.Gaps, dialect);

        // 図を作れずに流れなかった組ばかりで緑にならないよう、代表の名前の半分以上 × 置き場所の数は流す
        CoverageGuard.AtLeast(
            $"{dialect}: 流したケース",
            executed,
            NameFuzzDiagrams.Representative.Count / 2 * Enum.GetValues<NameRole>().Length
        );
        ledger.AssertMatchesKnown($"ddl:{dialect}:");

        // 網羅しない組は、往復の結果を表明してから照合する（既定の種の数のときだけ）
        coverageReport.AssertGaps(NameFuzzDdlRoundTripDimensions.Gaps, dialect, plan);
    }

    /// <summary>この方言の識別子の長さの上限（入口が断る名前の判定）</summary>
    private IdentifierLengthLimit Limit => Provider.SyncCapabilities.IdentifierLengthLimit;

    /// <summary>DB を空にして図の DDL を流し、取り込み直した結果と図の差分を失敗として記録する</summary>
    /// <param name="accepted">
    /// 試す名前を方言が受け付けるか（Model の宣言）。受け付けないなら DDL 生成の入口で断られることだけを確かめる
    /// </param>
    private async Task RoundTripAsync(
        FuzzFailureLog ledger,
        string entrance,
        string shape,
        ErDiagram diagram,
        string example,
        bool accepted,
        CancellationToken ct
    )
    {
        await ResetAsync(ct);
        string ddl;

        try
        {
            ddl = Provider.DdlGenerator.Build(diagram);
        }
        catch (InvalidOperationException ex)
        {
            if (accepted)
            {
                ledger.Record(new(entrance, "ddl-rejected", shape), $"{example} {ex.Message}");
            }

            return;
        }

        if (!accepted)
        {
            ledger.Record(new(entrance, "not-rejected", shape), example);
            return;
        }

        try
        {
            await ApplyAsync(ddl, ct);
        }
        catch (Exception ex)
        {
            ledger.Record(
                new(entrance, "apply-failed", shape),
                $"{example} {ex.GetType().Name}: {ex.Message}"
            );
            return;
        }

        SchemaImportResult imported;

        try
        {
            imported = await Provider.SchemaImporter.ImportAsync(
                ConnectionString,
                DbCommands.DefaultTimeoutSeconds,
                ct
            );
        }
        catch (Exception ex)
        {
            ledger.Record(
                new(entrance, "import-failed", shape),
                $"{example} {ex.GetType().Name}: {ex.Message}"
            );
            return;
        }

        var diff = new SchemaDiffService().Compute(
            imported.Entities,
            imported.Relationships,
            diagram.Entities,
            diagram.Relationships,
            Provider.SyncCapabilities
        );

        foreach (var item in diff.Items)
        {
            ledger.Record(
                new(entrance, $"diff:{item.Kind}", shape),
                $"{example} {item.Kind}: {item.Description}"
            );
        }
    }
}

/// <summary>SQLite（実ファイル・Docker 不要）の名前の総当たりの DDL の往復</summary>
[Trait("Category", "Integration")]
[MatrixAxisValue(NameFuzzDdlRoundTripTestsBase.DialectAxis, DialectNames.Sqlite)]
public sealed class SqliteNameFuzzDdlRoundTripIntegrationTests
    : NameFuzzDdlRoundTripTestsBase,
        IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    protected override IDatabaseProvider Provider { get; } = new SqliteProvider();

    protected override bool IsAvailable => true;

    protected override Task ResetAsync(CancellationToken ct) => _db.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string ddl, CancellationToken ct) =>
        _db.ApplyDdlAsync(ddl, ct);

    protected override string ConnectionString => _db.ReadWriteConnectionString;

    public void Dispose() => _db.Dispose();
}

/// <summary>SQL Server（Testcontainers）の名前の総当たりの DDL の往復</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
[MatrixAxisValue(NameFuzzDdlRoundTripTestsBase.DialectAxis, DialectNames.SqlServer)]
public sealed class SqlServerNameFuzzDdlRoundTripIntegrationTests(SqlServerContainerFixture fixture)
    : NameFuzzDdlRoundTripTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new SqlServerProvider();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string ddl, CancellationToken ct) =>
        fixture.ExecuteAsync(ddl, ct);

    protected override string ConnectionString => fixture.ConnectionString;
}

/// <summary>PostgreSQL（Testcontainers）の名前の総当たりの DDL の往復</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
[MatrixAxisValue(NameFuzzDdlRoundTripTestsBase.DialectAxis, DialectNames.PostgreSql)]
public sealed class PostgreSqlNameFuzzDdlRoundTripIntegrationTests(
    PostgreSqlContainerFixture fixture
) : NameFuzzDdlRoundTripTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new PostgreSqlProvider();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string ddl, CancellationToken ct) =>
        fixture.ExecuteAsync(ddl, ct);

    protected override string ConnectionString => fixture.ConnectionString;
}

/// <summary>MySQL（Testcontainers）の名前の総当たりの DDL の往復</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
[MatrixAxisValue(NameFuzzDdlRoundTripTestsBase.DialectAxis, DialectNames.MySql)]
public sealed class MySqlNameFuzzDdlRoundTripIntegrationTests(MySqlContainerFixture fixture)
    : NameFuzzDdlRoundTripTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new MySqlProvider();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string ddl, CancellationToken ct) =>
        fixture.ExecuteAsync(ddl, ct);

    protected override string ConnectionString => fixture.ConnectionString;
}

/// <summary>Oracle（Testcontainers）の名前の総当たりの DDL の往復</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
[MatrixAxisValue(NameFuzzDdlRoundTripTestsBase.DialectAxis, DialectNames.Oracle)]
public sealed class OracleNameFuzzDdlRoundTripIntegrationTests(OracleContainerFixture fixture)
    : NameFuzzDdlRoundTripTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new OracleProvider();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string ddl, CancellationToken ct) =>
        fixture.ExecuteAsync(ddl, ct);

    protected override string ConnectionString => fixture.ConnectionString;

    /// <summary>Oracle の識別子は二重引用符を含められない</summary>
    protected override bool IsOutOfDesign(string name, NameRole role) =>
        base.IsOutOfDesign(name, role) || name.Contains('"');
}
