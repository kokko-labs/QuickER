using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>開く処理の前後で、ガードが 2 段目に入った回数と張り直しを流した回数の増え方</summary>
/// <param name="Full">2 段目（読み取りだけの確認）に入った回数の増え方</param>
/// <param name="Repair">張り直しを流した回数の増え方</param>
/// <param name="FullMax">2 段目の回数の増え方の上限（null なら <paramref name="Full"/> ちょうど）</param>
/// <param name="RepairMax">張り直しの回数の増え方の上限（null なら <paramref name="Repair"/> ちょうど）</param>
internal sealed record SyncTriggerGuardCounts(
    int Full,
    int Repair,
    int? FullMax = null,
    int? RepairMax = null
);

/// <summary>開く処理が失敗するときの期待</summary>
/// <param name="ExceptionType">開く処理が投げる例外の型</param>
/// <param name="InnerType">内側の例外の型（その型か派生。null なら内側の例外が無い）</param>
/// <param name="Fragments">例外の文に含まれる断片</param>
/// <param name="FailsAgain">もう一度開いても同じ型で失敗するか</param>
internal sealed record SyncTriggerGuardFailure(
    Type ExceptionType,
    Type? InnerType,
    IReadOnlyList<string> Fragments,
    bool FailsAgain
);

/// <summary>同期のトリガーの確認の表の 1 行の入力</summary>
/// <param name="Fixture">端末の図のフィクスチャ</param>
/// <param name="Prepared">外からの変更の前に、別に作ったガードで包んだ接続を 1 回開いて張っておくか</param>
/// <param name="External">包んでいない接続で流す外からの変更（ファイルの操作を含む。null なら何もしない）</param>
/// <param name="Counts">次に開く処理での、行のガードの回数の増え方</param>
/// <param name="Failure">次に開く処理が失敗するときの期待（null なら成功する）</param>
/// <param name="WriteRecorded">開けた後、包んだ接続ファクトリの Repository の書き込みが行の状態の表に残るか（確かめないなら false）</param>
/// <param name="FastPathAfter">開けた後にもう一度開くと、2 段目に入らないか（確かめないなら false）</param>
/// <param name="Open">次に開く処理を差し替える（null なら行のガードで 1 回開いて閉じる）</param>
/// <param name="Recover">失敗の後に流す戻しの手（null なら戻さない。戻した後は開けて、成功の表明を掛ける）</param>
/// <param name="After">行に固有の後の表明（null なら無い）</param>
/// <param name="ChecksFile">端末の DB のファイルに共通の表明を掛けるか（メモリの DB の行は false）</param>
/// <param name="Inner">行のガードが包む内側の接続ファクトリ</param>
/// <param name="ConnectionString">行のガードの内側へ渡す接続文字列（null なら端末の DB の接続文字列）</param>
/// <param name="GuardTables">行のガードが見る表（null ならフィクスチャの同期する表の全部）</param>
internal sealed record SyncTriggerGuardCase(
    SyncTriggerFixture Fixture,
    bool Prepared,
    Func<SyncTriggerGuardContext, CancellationToken, Task>? External,
    SyncTriggerGuardCounts Counts,
    SyncTriggerGuardFailure? Failure = null,
    bool WriteRecorded = true,
    bool FastPathAfter = true,
    Func<SyncTriggerGuardContext, CancellationToken, Task>? Open = null,
    Func<SyncTriggerGuardContext, CancellationToken, Task>? Recover = null,
    Func<SyncTriggerGuardContext, CancellationToken, Task>? After = null,
    bool ChecksFile = true,
    SyncTriggerGuardInner Inner = SyncTriggerGuardInner.Generated,
    Func<SyncTriggerHarness, string>? ConnectionString = null,
    IReadOnlyList<string>? GuardTables = null
);

/// <summary>同期のトリガーの確認の表の 1 行が使う端末・ガードと、行の手どうしで受け渡す値</summary>
internal sealed class SyncTriggerGuardContext(
    SyncTriggerHarness harness,
    SyncTriggerGuardProbe guard
) : IAsyncDisposable
{
    private readonly List<SqliteConnection> _kept = [];
    private int _nextKey = 9000;

    /// <summary>端末</summary>
    public SyncTriggerHarness Harness { get; } = harness;

    /// <summary>行のガード（回数を数える相手）</summary>
    public SyncTriggerGuardProbe Guard { get; } = guard;

    /// <summary>準備で張った直後の DB の識別子（準備しない行は null）</summary>
    public string? DbIdBefore { get; set; }

    /// <summary>行が開いたまま持つ包んでいない接続（ロックを持つ外の接続。行の終わりに破棄する）</summary>
    public SqliteConnection? Held { get; set; }

    /// <summary>書き込みに使う新しいキー</summary>
    public int NextKey() => _nextKey++;

    /// <summary>行の終わりまで開いたまま持つ接続を預ける（メモリの DB を保つ接続）</summary>
    public void Keep(SqliteConnection connection) => _kept.Add(connection);

    /// <summary>行のガードで包んだ接続を開いて閉じる。失敗したら例外を返し、接続が閉じていることを表明する</summary>
    /// <param name="ct">キャンセルトークン</param>
    public Task<Exception?> TryOpenAsync(CancellationToken ct) =>
        TryOpenAsync(Guard.CreateConnection, ct);

    /// <summary>包んだ接続を作って開いて閉じる。失敗したら例外を返し、接続が閉じていることを表明する</summary>
    /// <param name="create">包んだ接続を作る処理</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<Exception?> TryOpenAsync(
        Func<SqliteConnection> create,
        CancellationToken ct
    )
    {
        await using var connection = create();

        try
        {
            await connection.OpenAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            connection
                .State.Should()
                .Be(ConnectionState.Closed, "確認に失敗した接続は閉じてから例外が出る");

            return exception;
        }

        return null;
    }

    /// <summary>行のガードで包んだ接続を開いて閉じる（失敗したら表明で落とす）</summary>
    /// <param name="because">失敗したときの理由</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task OpenAsync(string because, CancellationToken ct) =>
        (await TryOpenAsync(ct)).Should().BeNull(because);

    /// <summary>行のガードが見る表のうち DB にある表のトリガーが、<c>TriggersFor</c> の名前と文に一致することを表明する</summary>
    /// <param name="because">理由</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task ShouldHaveExpectedTriggersAsync(string because, CancellationToken ct)
    {
        foreach (var table in Guard.Tables)
        {
            if (!await Harness.TableExistsAsync(table, ct))
            {
                continue;
            }

            (await Harness.ReadTriggerTextsAsync(table, ct))
                .Should()
                .BeEquivalentTo(
                    Harness.ExpectedTriggerTexts(table),
                    $"{because}（表 {table} のトリガー）"
                );
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Held is { } held)
        {
            await held.DisposeAsync();
        }

        foreach (var connection in _kept)
        {
            await connection.DisposeAsync();
        }
    }
}

/// <summary>
/// 同期のトリガーの確認の表：1 行＝「包んだ接続ファクトリで開いて使っている DB に、外から何かが起きた後、次に開いた接続で
/// 何が起きるか」。<see cref="SyncSupportAdoSqliteTriggerRuntimeTests"/> が 1 行ずつ流す。
/// </summary>
/// <remarks>
/// <para>
/// 行のガードは <c>SyncLocalTrackingGuard</c> を行ごとに新しく作り、<c>ObservedSqlConnectionFactory</c> で内側の接続ファクトリを
/// 包む。準備は別に作ったガードで張る（覚えた組は型の静的な表にあるので、行のガードも準備の結果を使う）。外からの変更は
/// 包んでいない接続で流す。覚えた組はプロセスの中で残るので、行ごとに新しいパスの一時の DB を使う。
/// </para>
/// <para>
/// どの行にも掛ける表明はランナーの側が持つ：成功した開く処理の後は、行のガードが見る表のうち DB にある表のトリガーが
/// <c>TriggersFor</c> と一致し、状態の表に 1 行がある。失敗した開く処理の後は、接続が閉じていて、DB のスキーマ
/// （<c>sqlite_master</c> の全行）が開く前と同じ。次に開く処理で、行のガードの回数が期待どおりに増える。
/// </para>
/// </remarks>
internal static class SyncTriggerGuardRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<SyncTriggerGuardCase> Table { get; } = Build();

    /// <summary>表の Id（Theory のデータ）</summary>
    public static TheoryData<string> Ids() => Table.Ids();

    private const string Events = "model_events";

    private static ScenarioRow<SyncTriggerGuardCase> Row(
        string id,
        string description,
        SyncTriggerGuardCase input
    ) => new(id, null, description, input);

    private static SyncTriggerGuardCounts Counts(int full, int repair) => new(full, repair);

    /// <summary>外の接続で文を流す手</summary>
    private static Func<SyncTriggerGuardContext, CancellationToken, Task> Outside(string sql) =>
        (ctx, ct) => ctx.Harness.ExecuteOutsideAsync(sql, ct);

    /// <summary>表のトリガーのうち、前置きで始まるものを外の接続で全部落とす手</summary>
    private static Func<SyncTriggerGuardContext, CancellationToken, Task> DropAllTriggers(
        string table
    ) =>
        async (ctx, ct) =>
        {
            foreach (var name in (await ctx.Harness.ReadTriggerTextsAsync(table, ct)).Keys)
            {
                await ctx.Harness.ExecuteOutsideAsync($"DROP TRIGGER \"{name}\"", ct);
            }
        };

    /// <summary>状態の表の DB の識別子が、準備で張った直後の値と違うことを表明する手</summary>
    private static async Task DbIdChangedAsync(SyncTriggerGuardContext ctx, CancellationToken ct)
    {
        var state = await ctx.Harness.ReadStateAsync(ct);
        state.DbId.Should().NotBeNullOrWhiteSpace("状態の表を作り直した後の DB の識別子");
        state
            .DbId.Should()
            .NotBe(ctx.DbIdBefore, "状態の表を作り直すと DB の識別子は新しい値になる");
    }

    /// <summary>出来事の表を作り直す（新しい表を作る → 行を写す → 元の表を落とす → 元の名前へ変える）</summary>
    private static async Task RebuildEventsAsync(SyncTriggerGuardContext ctx, CancellationToken ct)
    {
        await ctx.Harness.ExecuteOutsideAsync(
            "INSERT INTO model_folders (folder_id, name) VALUES (1, 'root'); "
                + "INSERT INTO model_items (item_id, folder_id, code, title, payload) VALUES (10, 1, 'c10', 'ten', x'01'); "
                + "INSERT INTO model_events (event_at, item_id, memo) VALUES ('2026-01-02 03:04:05', 10, 'first');",
            ct
        );

        var sql = (string)
            (
                await ctx.Harness.ScalarOutsideAsync(
                    "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'model_events'",
                    ct
                )
            )!;
        var rebuilt = Regex.Replace(
            sql,
            "^CREATE TABLE \"?model_events\"?",
            "CREATE TABLE \"model_events_rebuild\"",
            RegexOptions.CultureInvariant
        );
        rebuilt.Should().NotBe(sql, "作り直す表の CREATE 文の表の名前を置き換える");

        await ctx.Harness.ExecuteOutsideAsync(
            rebuilt
                + "; INSERT INTO model_events_rebuild SELECT * FROM model_events; "
                + "DROP TABLE model_events; "
                + "ALTER TABLE model_events_rebuild RENAME TO model_events; "
                + "INSERT INTO model_events (event_at, item_id, memo) VALUES ('2026-01-02 03:04:06', 10, 'outside');",
            ct
        );
    }

    private static ScenarioTable<SyncTriggerGuardCase> Build()
    {
        List<ScenarioRow<SyncTriggerGuardCase>> rows =
        [
            // 何も起きない・最初に開く
            Row(
                "nothing-happens-hundred-opens-stay-on-fast-path",
                "張った DB を続けて 100 回開いても、2 段目に入らない（速い道）",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    External: null,
                    Counts(0, 0),
                    Open: async (ctx, ct) =>
                    {
                        for (var i = 0; i < 100; i++)
                        {
                            await ctx.OpenAsync("張った DB を続けて開く", ct);
                        }
                    }
                )
            ),
            Row(
                "first-open-puts-tracking-in-place",
                "張る前の DB を最初に開くと、状態の表とトリガーができる",
                new(SyncTriggerFixture.SyncModel, Prepared: false, External: null, Counts(1, 1))
            ),
            Row(
                "separate-scope-uses-remembered-pair",
                "同じ DB へ新しく作ったガードと包み（別のスコープ）で開くと、そのガードは 2 段目に入らない（覚えた組は型の静的な表にある）",
                new(SyncTriggerFixture.SyncModel, Prepared: true, External: null, Counts(0, 0))
            ),
            // 無関係な DDL
            Row(
                "unrelated-create-table-checks-without-repair",
                "無関係な表を作るとスキーマの版が進み、2 段目に入るが張り直さない",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("CREATE TABLE guard_unrelated (x INTEGER)"),
                    Counts(1, 0)
                )
            ),
            Row(
                "unrelated-create-index-checks-without-repair",
                "同期する表に索引を作るとスキーマの版が進み、2 段目に入るが張り直さない",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("CREATE INDEX ix_guard_items_title ON model_items (title)"),
                    Counts(1, 0)
                )
            ),
            // トリガーが変わる
            Row(
                "one-trigger-dropped-is-repaired",
                "外でトリガーを 1 本落とすと、次に開いたときに張り直す",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("DROP TRIGGER \"quicker_sync_trg_model_items_au\""),
                    Counts(1, 1)
                )
            ),
            Row(
                "all-triggers-dropped-are-repaired",
                "外で同期する表のトリガーを全部落とすと、次に開いたときに張り直す",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    async (ctx, ct) =>
                    {
                        foreach (var table in ctx.Harness.AllTableNames)
                        {
                            await DropAllTriggers(table)(ctx, ct);
                        }
                    },
                    Counts(1, 1)
                )
            ),
            Row(
                "trigger-recreated-with-other-body-is-repaired",
                "外でトリガーを 1 本、同じ名前・別の本文で作り直すと、次に開いたときに期待の本文へ張り直す",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside(
                        "DROP TRIGGER \"quicker_sync_trg_model_items_ad\"; "
                            + "CREATE TRIGGER \"quicker_sync_trg_model_items_ad\" AFTER DELETE ON \"model_items\" "
                            + "BEGIN SELECT 1; END"
                    ),
                    Counts(1, 1)
                )
            ),
            Row(
                "extra-prefixed-trigger-is-removed",
                "外で前置きで始まる余計なトリガーを同期する表に足すと、次に開いたときに張り直して余計なトリガーは無くなる",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside(
                        "CREATE TRIGGER \"quicker_sync_trg_model_items_extra\" AFTER INSERT ON \"model_items\" "
                            + "BEGIN SELECT 1; END"
                    ),
                    Counts(1, 1)
                )
            ),
            Row(
                "sync-fixture-blob-trigger-dropped-is-repaired",
                "同期のパリティの図で、NULL 許容の除外 blob のトリガーを外で落とすと、次に開いたときに張り直す",
                new(
                    SyncTriggerFixture.Sync,
                    Prepared: true,
                    Outside("DROP TRIGGER \"quicker_sync_trg_sync_orders_b1\""),
                    Counts(1, 1)
                )
            ),
            Row(
                "vo-fixture-all-triggers-of-table-dropped-are-repaired",
                "値オブジェクトの図で、表のトリガーを外で全部落とすと、次に開いたときに張り直す",
                new(
                    SyncTriggerFixture.SyncVo,
                    Prepared: true,
                    DropAllTriggers("syncvo_orders"),
                    Counts(1, 1)
                )
            ),
            // 表の作り直し
            Row(
                "table-rebuilt-is-repaired-without-catching-up-outside-writes",
                "外で表を作り直す（作る → 写す → 落とす → 元の名前へ変える）とトリガーが消え、次に開いたときに張り直す。"
                    + "行は残る。開くまでの間の外の書き込みは拾わない（作り直しの後に外の接続が書いた行は記録に無い）",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    RebuildEventsAsync,
                    Counts(1, 1),
                    After: async (ctx, ct) =>
                    {
                        (
                            await ctx.Harness.ScalarOutsideAsync(
                                "SELECT count(*) FROM model_events",
                                ct
                            )
                        )
                            .Should()
                            .Be(2L, "作り直した表の行は残る");
                        (
                            await ctx.Harness.ScalarOutsideAsync(
                                "SELECT count(*) FROM quicker_sync_row WHERE table_name = 'model_events' "
                                    + "AND \"key\" = '2026-01-02 03:04:05'",
                                ct
                            )
                        )
                            .Should()
                            .Be(1L, "作り直しの前に外で書いた行はトリガーが記録した");
                        (
                            await ctx.Harness.ScalarOutsideAsync(
                                "SELECT count(*) FROM quicker_sync_row WHERE table_name = 'model_events' "
                                    + "AND \"key\" = '2026-01-02 03:04:06'",
                                ct
                            )
                        )
                            .Should()
                            .Be(0L, "作り直しの後・次に開く前に外で書いた行は記録に無い");
                    }
                )
            ),
            // 状態の表
            Row(
                "state-table-dropped-is-recreated-with-new-db-id",
                "外で状態の表（quicker_sync_state）だけを落とすと、次に開いたときに作り直し、DB の識別子は新しい値になる",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("DROP TABLE quicker_sync_state"),
                    Counts(1, 1),
                    After: DbIdChangedAsync
                )
            ),
            Row(
                "both-state-tables-dropped-are-recreated-with-new-db-id",
                "外で状態の表と行の状態の表を両方落とすと、次に開いたときに作り直し、DB の識別子は新しい値になる",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("DROP TABLE quicker_sync_state; DROP TABLE quicker_sync_row"),
                    Counts(1, 1),
                    After: DbIdChangedAsync
                )
            ),
            Row(
                "vacuum-checks-without-repair",
                "外で VACUUM を流しても開ける。SQLite は VACUUM でスキーマの版を進めるので、2 段目に入るが張り直さない",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    async (ctx, ct) =>
                    {
                        var before = await ctx.Harness.ScalarOutsideAsync(
                            "PRAGMA schema_version",
                            ct
                        );
                        await ctx.Harness.ExecuteOutsideAsync("VACUUM", ct);
                        (await ctx.Harness.ScalarOutsideAsync("PRAGMA schema_version", ct))
                            .Should()
                            .NotBe(before, "VACUUM はスキーマの版を進める");
                    },
                    Counts(1, 0)
                )
            ),
            // 列・表の名前
            Row(
                "key-column-renamed-refuses-open-until-renamed-back",
                "外でキーの列の名前を変えると、開く処理が表・無い列の名前・戻し方の文つきで断り、接続は閉じる。"
                    + "もう一度開いても同じ。名前を戻すと開ける",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("ALTER TABLE model_events RENAME COLUMN event_at TO happened_at"),
                    Counts(1, 1),
                    Failure: new(
                        typeof(InvalidOperationException),
                        null,
                        [
                            "'model_events'",
                            "'event_at'",
                            "Make the column names of the diagram and of the database agree",
                        ],
                        FailsAgain: true
                    ),
                    Recover: Outside(
                        "ALTER TABLE model_events RENAME COLUMN happened_at TO event_at"
                    )
                )
            ),
            Row(
                "tracked-table-renamed-is-skipped-and-keeps-its-triggers",
                "外で同期する表の名前を変えると、その表は DB に無い表として飛ばされて開ける（張り直さない）。"
                    + "古い名前を名乗るトリガーは新しい名前の表に残り、その表への書き込みは古い表の名前で記録される",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("ALTER TABLE model_events RENAME TO model_events_moved"),
                    Counts(1, 0),
                    After: async (ctx, ct) =>
                    {
                        (await ctx.Harness.ReadTriggerTextsAsync("model_events_moved", ct))
                            .Keys.Order(StringComparer.Ordinal)
                            .Should()
                            .Equal(
                                ctx.Harness.ExpectedTriggerTexts(Events)
                                    .Keys.Order(StringComparer.Ordinal),
                                "名前を変えた表に、古い名前を名乗るトリガーが残る"
                            );
                        await ctx.Harness.ExecuteOutsideAsync(
                            "INSERT INTO model_folders (folder_id, name) VALUES (1, 'root'); "
                                + "INSERT INTO model_items (item_id, folder_id, code, title, payload) VALUES (10, 1, 'c10', 'ten', x'01'); "
                                + "INSERT INTO model_events_moved (event_at, item_id, memo) VALUES ('2026-01-02 03:04:05', 10, 'moved');",
                            ct
                        );
                        (
                            await ctx.Harness.ScalarOutsideAsync(
                                "SELECT table_name FROM quicker_sync_row WHERE \"key\" = '2026-01-02 03:04:05'",
                                ct
                            )
                        )
                            .Should()
                            .Be(
                                Events,
                                "名前を変えた表への書き込みは、トリガーが名乗る古い表の名前で記録される"
                            );
                    }
                )
            ),
            // ファイル
            Row(
                "database-file-replaced-is-found-and-put-in-place",
                "接続を全部閉じ、同じパスにトリガーの無い別の DB（スキーマの版は元と同じ）を置くと、次に開いたときに見つけて張る"
                    + "（DB の識別子が違うので速い道を通らない）",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    (ctx, ct) => ctx.Harness.ReplaceWithFreshDatabaseAsync(ct),
                    Counts(1, 1),
                    After: DbIdChangedAsync
                )
            ),
            Row(
                "database-file-replaced-by-database-with-state-tables-is-found-by-database-id",
                "接続を全部閉じ、同じパスに、状態の表はあるがトリガーの無い別の DB（スキーマの版は元と同じ）を置くと、"
                    + "次に開いたときに見つけて張る（速い道の 1 文は読めるので、見分けるのは DB の識別子だけ）",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    (ctx, ct) =>
                        ctx.Harness.ReplaceWithFreshDatabaseAsync(ct, withStateTables: true),
                    Counts(1, 1),
                    After: DbIdChangedAsync
                )
            ),
            Row(
                "backup-restored-over-dropped-trigger-opens-on-fast-path",
                "張った後の DB の写しを取り、外でトリガーを落としてから写しを戻すと、覚えた組と同じなので速い道で開け、トリガーは期待どおり",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    async (ctx, ct) =>
                    {
                        var backup = Path.Combine(
                            Path.GetDirectoryName(ctx.Harness.DatabasePath)!,
                            "backup.db"
                        );
                        ctx.Harness.CopyFileTo(backup);
                        await ctx.Harness.ExecuteOutsideAsync(
                            "DROP TRIGGER \"quicker_sync_trg_model_items_au\"",
                            ct
                        );
                        ctx.Harness.RestoreFileFrom(backup);
                    },
                    Counts(0, 0)
                )
            ),
            Row(
                "backup-restored-after-repair-is-checked-without-repair",
                "写しを取り、外でトリガーを落とし、別のガードで開いて張り直した後に写しを戻すと、覚えた組と違うので 2 段目に入るが、"
                    + "写しはそろっているので張り直さない",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    async (ctx, ct) =>
                    {
                        var backup = Path.Combine(
                            Path.GetDirectoryName(ctx.Harness.DatabasePath)!,
                            "backup.db"
                        );
                        ctx.Harness.CopyFileTo(backup);
                        await ctx.Harness.ExecuteOutsideAsync(
                            "DROP TRIGGER \"quicker_sync_trg_model_items_au\"",
                            ct
                        );
                        var other = ctx.Harness.CreateGuard();
                        (await SyncTriggerGuardContext.TryOpenAsync(other.CreateConnection, ct))
                            .Should()
                            .BeNull("別のガードで開いて張り直す");
                        other.RepairCount.Should().Be(1, "別のガードが張り直した");
                        ctx.Harness.RestoreFileFrom(backup);
                    },
                    Counts(1, 0)
                )
            ),
            // ガードの組み合わせ
            Row(
                "guards-with-different-table-sets-keep-each-other",
                "同期する表の集合が違う 2 つのガード（全部の表と、1 表を除いた表）を同じ DB に向けても、互いに相手のトリガーを"
                    + "落とさず、それぞれが自分の表だけを見て速い道へ入る（覚える鍵に定義のハッシュが入る）",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    External: null,
                    Counts(0, 0),
                    Open: async (ctx, ct) =>
                    {
                        var narrow = ctx.Harness.CreateGuard(
                            ctx.Harness.AllTableNames.Where(t => t != Events).ToList()
                        );

                        for (var i = 0; i < 3; i++)
                        {
                            (
                                await SyncTriggerGuardContext.TryOpenAsync(
                                    narrow.CreateConnection,
                                    ct
                                )
                            )
                                .Should()
                                .BeNull("1 表を除いたガードで開く");
                            await ctx.OpenAsync("全部の表のガードで開く", ct);
                        }

                        narrow
                            .FullCheckCount.Should()
                            .Be(
                                1,
                                "1 表を除いたガードは最初の 1 回だけ 2 段目に入る（定義が違うので全部の表の組を使わない）"
                            );
                        narrow
                            .RepairCount.Should()
                            .Be(
                                0,
                                "1 表を除いたガードの表はそろっているので張り直さない（除いた表のトリガーを落とさない）"
                            );
                    }
                )
            ),
            Row(
                "concurrent-opens-after-dropped-trigger-both-succeed",
                "外でトリガーを落とした後、2 つのスレッドで同時に開く（10 回繰り返す）と、どちらも成功し、トリガーは期待どおりで"
                    + "重複が無い。2 段目と張り直しの回数は、2 つの開く処理が覚えた組を見る順で 1 回ごとに 1〜2 回",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    External: null,
                    new(10, 10, FullMax: 20, RepairMax: 20),
                    Open: async (ctx, ct) =>
                    {
                        for (var i = 0; i < 10; i++)
                        {
                            await ctx.Harness.ExecuteOutsideAsync(
                                "DROP TRIGGER \"quicker_sync_trg_model_items_au\"",
                                ct
                            );

                            using var barrier = new Barrier(2);
                            var opens = Enumerable
                                .Range(0, 2)
                                .Select(_ =>
                                    Task.Run(
                                        () =>
                                        {
                                            barrier.SignalAndWait(ct);
                                            using var connection = ctx.Guard.CreateConnection();
                                            connection.Open();
                                        },
                                        ct
                                    )
                                )
                                .ToArray();
                            await Task.WhenAll(opens);
                            await ctx.ShouldHaveExpectedTriggersAsync(
                                "同時に開いた後のトリガー",
                                ct
                            );
                        }
                    }
                )
            ),
            Row(
                "lock-timeout-closes-connection-and-recovers-after-rollback",
                "外の接続が BEGIN IMMEDIATE を持ったまま張り直しが要る状態で Default Timeout=1 の接続を開くと、包んだ例外"
                    + "（内側は DB の例外）で断り、接続は閉じる。外の接続を ROLLBACK した後は開けて張り直す",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    async (ctx, ct) =>
                    {
                        await ctx.Harness.ExecuteOutsideAsync(
                            "DROP TRIGGER \"quicker_sync_trg_model_items_au\"",
                            ct
                        );
                        ctx.Held = await ctx.Harness.OpenOutsideAsync(ct);
                        await using var begin = ctx.Held.CreateCommand();
                        begin.CommandText = "BEGIN IMMEDIATE";
                        await begin.ExecuteNonQueryAsync(ct);
                    },
                    Counts(1, 1),
                    Failure: new(
                        typeof(InvalidOperationException),
                        typeof(System.Data.Common.DbException),
                        [
                            "could not be put in place",
                            "The connection has been closed",
                            "open the connection again once it has finished",
                        ],
                        FailsAgain: false
                    ),
                    Recover: async (ctx, ct) =>
                    {
                        await using var rollback = ctx.Held!.CreateCommand();
                        rollback.CommandText = "ROLLBACK";
                        await rollback.ExecuteNonQueryAsync(ct);
                    },
                    ConnectionString: h =>
                        new SqliteConnectionStringBuilder(h.ConnectionString)
                        {
                            DefaultTimeout = 1,
                        }.ConnectionString
                )
            ),
            // 接続の形
            Row(
                "read-only-connection-opens-when-in-place",
                "読み取り専用の接続（Mode=ReadOnly）は、無関係な DDL の後でもそろっている DB なら 2 段目で確かめて開ける（張り直さない）",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("CREATE TABLE guard_unrelated (x INTEGER)"),
                    Counts(1, 0),
                    WriteRecorded: false,
                    ConnectionString: h =>
                        new SqliteConnectionStringBuilder(h.ConnectionString)
                        {
                            Mode = SqliteOpenMode.ReadOnly,
                        }.ConnectionString
                )
            ),
            Row(
                "read-only-connection-refuses-when-repair-is-needed",
                "読み取り専用の接続は、張り直しが要る DB では包んだ例外（内側は DB の例外・書ける接続で開き直す趣旨の文）で断り、接続は閉じる",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: true,
                    Outside("DROP TRIGGER \"quicker_sync_trg_model_items_au\""),
                    Counts(1, 1),
                    Failure: new(
                        typeof(InvalidOperationException),
                        typeof(System.Data.Common.DbException),
                        ["could not be put in place", "opened read-only"],
                        FailsAgain: true
                    ),
                    WriteRecorded: false,
                    ConnectionString: h =>
                        new SqliteConnectionStringBuilder(h.ConnectionString)
                        {
                            Mode = SqliteOpenMode.ReadOnly,
                        }.ConnectionString
                )
            ),
            Row(
                "in-memory-database-is-checked-every-open",
                "メモリの DB（Data Source=:memory:）は覚えないので、開くたびに 2 段目に入る。"
                    + "開くたびに別の空の DB に当たる（同じ接続文字列でもプールから前の DB が戻らない）ので、毎回張る",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: false,
                    External: null,
                    Counts(3, 3),
                    WriteRecorded: false,
                    FastPathAfter: false,
                    Open: async (ctx, ct) =>
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            await using var connection = ctx.Guard.CreateConnection();
                            await connection.OpenAsync(ct);
                            await using var command = connection.CreateCommand();
                            command.CommandText = "SELECT count(*) FROM quicker_sync_state";
                            (await command.ExecuteScalarAsync(ct))
                                .Should()
                                .Be(1L, "メモリの DB を開いた後は状態の表に 1 行がある");
                        }
                    },
                    ChecksFile: false,
                    ConnectionString: _ => "Data Source=:memory:"
                )
            ),
            Row(
                "two-in-memory-databases-alternate-and-are-both-put-in-place",
                "名前つきの共有のメモリの DB を 2 つ（どちらも図の DDL を当てて保つ接続あり）交互に開くと、どちらも開くたびに"
                    + "2 段目に入り、最初の 1 回だけ張り、トリガーは毎回期待どおり",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: false,
                    External: null,
                    Counts(6, 2),
                    WriteRecorded: false,
                    FastPathAfter: false,
                    Open: async (ctx, ct) =>
                    {
                        var other = SharedMemory();
                        var creates = new[] { ctx.Guard.CreateConnection, ctx.Guard.Wrap(other) };

                        foreach (
                            var connectionString in new[] { ctx.Guard.ConnectionString, other }
                        )
                        {
                            var keeper = new SqliteConnection(connectionString);
                            await keeper.OpenAsync(ct);
                            ctx.Keep(keeper);
                            await using var ddl = keeper.CreateCommand();
                            ddl.CommandText = ctx.Harness.Ddl;
                            await ddl.ExecuteNonQueryAsync(ct);
                        }

                        for (var i = 0; i < 6; i++)
                        {
                            await using var connection = creates[i % 2]();
                            await connection.OpenAsync(ct);

                            foreach (var table in ctx.Guard.Tables)
                            {
                                SyncTriggerHarness
                                    .ReadTriggerTexts(connection, table)
                                    .Should()
                                    .BeEquivalentTo(
                                        ctx.Harness.ExpectedTriggerTexts(table),
                                        $"メモリの DB {i % 2} の表 {table} のトリガー（{i} 回目）"
                                    );
                            }
                        }
                    },
                    ChecksFile: false,
                    ConnectionString: _ => SharedMemory()
                )
            ),
            Row(
                "custom-connection-factory-is-wrapped",
                "テストの中の自前の接続ファクトリ（busy_timeout を設定しない・new SqliteConnection を返すだけ）を包んでも張られ、書き込みが残る",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: false,
                    External: null,
                    Counts(1, 1),
                    Inner: SyncTriggerGuardInner.Custom
                )
            ),
            Row(
                "busy-timeout-is-set-before-the-check",
                "生成の接続ファクトリを包むと、確認が走る時点（包みが足したハンドラの位置）で busy_timeout が Default Timeout の値に設定済み",
                new(
                    SyncTriggerFixture.SyncModel,
                    Prepared: false,
                    External: null,
                    Counts(1, 1),
                    Open: async (ctx, ct) =>
                    {
                        long? seen = null;
                        var create = ctx.Guard.Wrap(
                            ctx.Harness.ConnectionString,
                            (sender, e) =>
                            {
                                if (
                                    e.CurrentState != ConnectionState.Open
                                    || e.OriginalState == ConnectionState.Open
                                )
                                {
                                    return;
                                }

                                using var command = ((SqliteConnection)sender!).CreateCommand();
                                command.CommandText = "PRAGMA busy_timeout";
                                seen = (long)command.ExecuteScalar()!;
                            }
                        );
                        (await SyncTriggerGuardContext.TryOpenAsync(create, ct))
                            .Should()
                            .BeNull("busy_timeout を見る包みで開く");
                        seen.Should()
                            .Be(
                                30000L,
                                "確認の時点で busy_timeout は Default Timeout（既定 30 秒）のミリ秒"
                            );
                    }
                )
            ),
        ];

        return new ScenarioTable<SyncTriggerGuardCase>(EngineIds.GeneratedRuntime, rows);
    }

    /// <summary>名前つきの共有のメモリの DB の接続文字列（名前はプロセスの中で一意）</summary>
    private static string SharedMemory() =>
        new SqliteConnectionStringBuilder
        {
            DataSource = "guard-memory-" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ConnectionString;
}
