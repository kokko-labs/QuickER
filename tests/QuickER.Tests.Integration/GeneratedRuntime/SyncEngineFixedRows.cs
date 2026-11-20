using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>新しいエンジンの決まった形の行の 1 行の入力（端末とサーバー役を受けて、手と表明を流す）</summary>
/// <param name="Run">行の手と表明</param>
internal sealed record SyncEngineFixedCase(
    Func<SyncEngineFixedHarness, CancellationToken, Task> Run
);

/// <summary>
/// 新しいエンジンの決まった形の行の表（影響の地図の名前 <c>table:SyncEngineFixed</c>）。同期のモデルの決定的なシナリオでは
/// 書けない、新しいエンジン（<c>TrackedSyncEngine</c>）だけで確かめる形を置く：実行の入口（同じ DB の実行の直列化・保存フックの
/// 中の同期・空にした表・引数）と、行の状態の表の印が決める blob の運び方（主キーを変えた行・一意な値の回転）。
/// </summary>
/// <remarks>
/// 端末とサーバー役は <see cref="SyncEngineFixedHarness"/>。行は今のエンジンでは流さない（同じ形の今のエンジンの振る舞いは、
/// 同期のモデルと <see cref="SyncRuntimeTestsBase"/> が持つ）。
/// </remarks>
internal static class SyncEngineFixedRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<SyncEngineFixedCase> Table { get; } = Build();

    /// <summary>表の Id（Theory のデータ）</summary>
    public static TheoryData<string> Ids() => Table.Ids();

    /// <summary>待ちの点に来るのを待つ上限（来なければ行の誤り）</summary>
    private static readonly TimeSpan ReachTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 直列化の行で、2 本目の実行がサーバー役に来るかを見る間（2 本目が進まないことの表明は順序で行い、この間は直列化を外した
    /// ときに 2 本目が来る余地を作るだけ）
    /// </summary>
    private static readonly TimeSpan SecondRunWindow = TimeSpan.FromSeconds(1);

    private static ScenarioRow<SyncEngineFixedCase> Row(
        string id,
        string description,
        Func<SyncEngineFixedHarness, CancellationToken, Task> run
    ) => new(id, null, description, new SyncEngineFixedCase(run));

    private static ScenarioTable<SyncEngineFixedCase> Build()
    {
        List<ScenarioRow<SyncEngineFixedCase>> rows =
        [
            Row(
                "runs-on-same-database-are-serialized",
                "同じ端末の DB の 2 本の実行（別のエンジンのインスタンス）は、1 本目がサーバー役の待ちの点で止まっている間 2 本目が"
                    + "サーバー役に来ず、1 本目の後に流れて同じ変更を二度送らない",
                RunsAreSerializedAsync
            ),
            Row(
                "sync-inside-save-hook-fails-on-lock-wait-and-leaves-nothing",
                "業務の保存フックの中の同期は、保存が持つ書き込みのロックを待って Default Timeout で失敗し、保存を巻き戻し、"
                    + "行の状態の表・端末・サーバーに途中の状態を残さない。実行のロックは手放され、フックの外の次の同期が収束する",
                SyncInsideSaveHookAsync
            ),
            Row(
                "emptied-table-refuses-every-entry",
                "サーバーが知っていて送るものの無い行を持つ表を外で空にすると、同期・送信だけ・受信だけのどれも "
                    + "SyncLocalTableEmptiedException で断り、何も送らず、何も降ろさず、行の状態の表を変えない",
                EmptiedTableAsync
            ),
            Row(
                "invalid-arguments-refuse-before-anything",
                "取得の大きさが 0 以下・エンジンが同期しない型の除外は、同期・送信だけ・受信だけのどれも引数の例外で断り、"
                    + "何も送らず、端末と行の状態の表を変えない",
                InvalidArgumentsAsync
            ),
            Row(
                "primary-key-changed-row-sends-its-blob",
                "生の SQL で主キーを変えた行の blob は、blob を運ぶ同期で新しいキーの行と一緒にサーバーへ届く（古いキーは"
                    + "サーバーから消え、新しいキーの blob は端末の blob と同じ）",
                PrimaryKeyChangedRowAsync
            ),
            Row(
                "unique-value-rotation-keeps-local-blobs",
                "サーバーで一意な値を入れ替えた 2 行を blob を運ばない同期が回転で降ろすと、端末の 2 行の blob は回転の前のまま"
                    + "（空にも NULL にもならない）で、入れ替えは端末に入る",
                RotationKeepsBlobsAsync
            ),
            Row(
                "failure-after-server-write-before-settle-is-reported-until-local-wins",
                "サーバーへの書き込みが通った後に送信の決着が失敗すると（別の接続が書き込みのロックを持ち続ける）、次の Collect の"
                    + "同期は自分が書いた行を相手に ModifiedOnServer を報告して未送信を残し、もう一度の Collect でも同じに報告し、"
                    + "LocalWins の同期で収束する（端末とサーバーの内容が同じ・未送信なし・競合なし・消失なし）",
                FailureAfterServerWriteAsync
            ),
            Row(
                "refresh-fetch-failure-leaves-device-as-before",
                "洗い替えが取得の失敗で終わると、端末の行・未送信・行の状態の表は洗い替えの前のまま残り（Force でも未送信を"
                    + "捨てない）、次の洗い替えが通って未送信を捨て、端末がサーバーの内容になる",
                RefreshFetchFailureAsync
            ),
            Row(
                "refresh-and-sync-leave-nothing-to-fetch-again",
                "洗い替えの直後の同期と、同期の直後の同期は、サーバーから 1 行も取得しない（洗い替えも同期も再開点を書く）。"
                    + "サーバーの他者が 1 行変えた後の同期は、その 1 行を取得して降ろす",
                ResumePointsAsync
            ),
            Row(
                "unsent-insert-then-delete-of-unknown-key-takes-server-row-on-download",
                "端末が作って消した行（サーバーに一度も届いていない・未送信のまま）と同じキーで、サーバーの他者が行を作ると、"
                    + "送信を挟まない受信だけの実行がサーバーの行を端末に入れる（端末に送るものは無い）。続く同期は何も送らず、"
                    + "競合も打ち切りも無く、端末とサーバーの内容が同じ",
                UnsentGoneRowOfUnknownKeyAsync
            ),
        ];

        return new ScenarioTable<SyncEngineFixedCase>(EngineIds.GeneratedRuntime, rows);
    }

    private static readonly SyncOptions WithBlobs = new() { IncludeUnboundedBinary = true };

    private static readonly SyncOptions WithoutBlobs = new() { IncludeUnboundedBinary = false };

    /// <summary>直列化：1 本目をサーバーへの書き込みの直前で止め、2 本目がサーバー役に来る順序を記録する</summary>
    private static async Task RunsAreSerializedAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.SyncAsync(null, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-edit' WHERE folder_id = 1",
            ct
        );

        var log = new List<(string Run, ModelInterruptPoint Point)>();
        var tag = new AsyncLocal<string?>();
        var secondSeen = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        h.Faults.ObservePoint = point =>
        {
            if (tag.Value is not { } run)
            {
                return;
            }

            lock (log)
            {
                log.Add((run, point));
            }

            if (run == "B")
            {
                secondSeen.TrySetResult();
            }
        };
        h.Faults.Syncing = ModelActor.Device1;
        h.Faults.ArmInterrupt(
            ModelActor.Device1,
            ModelInterruptPoint.BeforeServerWrite,
            ModelActor.Server,
            async () =>
            {
                reached.TrySetResult();
                await release.Task;
            }
        );

        var second = h.CreateEngine();
        SyncResult first;
        SyncResult next;
        bool secondCameEarly;

        try
        {
            var a = Task.Run(
                async () =>
                {
                    tag.Value = "A";

                    return await h.Engine.SyncAsync(null, ct);
                },
                ct
            );
            await reached.Task.WaitAsync(ReachTimeout, ct);
            var b = Task.Run(
                async () =>
                {
                    tag.Value = "B";

                    return await second.SyncAsync(null, ct);
                },
                ct
            );
            secondCameEarly =
                await Task.WhenAny(secondSeen.Task, Task.Delay(SecondRunWindow, ct))
                == secondSeen.Task;
            release.TrySetResult();
            first = await a;
            next = await b;
        }
        finally
        {
            release.TrySetResult();
            h.Faults.Syncing = null;
            h.Faults.ObservePoint = null;
        }

        secondCameEarly
            .Should()
            .BeFalse("1 本目がサーバー役の待ちの点で止まっている間、2 本目はサーバー役に来ない");
        var lastFirst = log.FindLastIndex(entry => entry.Run == "A");
        var firstSecond = log.FindIndex(entry => entry.Run == "B");
        firstSecond
            .Should()
            .BeGreaterThan(
                lastFirst,
                $"2 本目のサーバー役の呼び出しはどれも 1 本目の後（記録：{string.Join(", ", log)}）"
            );
        log.Count(entry => entry.Run == "B" && entry.Point == ModelInterruptPoint.BeforeServerWrite)
            .Should()
            .Be(0, "1 本目が送った変更を 2 本目は送らない");
        first.Uploaded.Should().Be(1);
        next.Uploaded.Should().Be(0);
        (await h.ReadServerAsync(ct)).Folders[1].Name.Should().Be("local-edit");
        (
            await h.ScalarAsync(
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                ct
            )
        )
            .Should()
            .Be("0");
    }

    /// <summary>保存フックの中の同期：保存の書き込みのロックを待って失敗し、何も残さない</summary>
    private static async Task SyncInsideSaveHookAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 2, Name = "f2" }, ct);
        await h.SyncAsync(null, ct);

        // 端末に送るものは無く、サーバーに降ろすものがある（同期は降ろす行の適用で書き込みのロックを取りに行く）
        var onServer = (await h.Server.Folders.GetByIdAsync(1, ct))!;
        onServer.Name = "server-edit";
        (await h.Server.Folders.UpdateAsync(onServer, cancellationToken: ct)).Should().BeTrue();

        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = await h.ReadLocalAsync(ct);
        var serverBefore = await h.ReadServerAsync(ct);
        var calls = 0;
        h.AfterFolderSave = async (_, hookCt) =>
        {
            calls++;
            await h.Engine.SyncAsync(null, hookCt);
        };

        Exception? thrown;

        try
        {
            var folder = (await h.Folders.GetByIdAsync(2, ct))!;
            folder.Name = "business";
            folder.MarkUpdated();
            thrown = await Record.ExceptionAsync(() =>
                h.Folders.SaveAsync(folder, cancellationToken: ct)
            );
        }
        finally
        {
            h.AfterFolderSave = null;
        }

        calls.Should().Be(1, "保存フックは 1 回呼ばれて、その中で同期を呼ぶ");
        thrown.Should().NotBeNull("保存フックの中の同期は失敗する");
        Chain(thrown!)
            .OfType<SqliteException>()
            .Should()
            .Contain(
                ex => ex.SqliteErrorCode == 5,
                $"保存が持つ書き込みのロックを Default Timeout まで待って SQLITE_BUSY で失敗する（{thrown}）"
            );
        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(stateBefore, "行の状態の表・状態の表・再開点の表に途中の状態を残さない");
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(Describe(localBefore), "保存は巻き戻り、同期は端末に何も適用しない");
        Describe(await h.ReadServerAsync(ct))
            .Should()
            .Be(Describe(serverBefore), "同期はサーバーに何も書かない");

        // 実行のロックは手放されている：フックの外の次の同期が流れて収束する（流れなければ上限で止める）
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(ReachTimeout);
        await h.SyncAsync(null, limit.Token);
        var local = await h.ReadLocalAsync(ct);
        Describe(local).Should().Be(Describe(await h.ReadServerAsync(ct)));
        local.Folders[1].Name.Should().Be("server-edit");
    }

    /// <summary>空にした表：どの入口も断り、何も変えない</summary>
    private static async Task EmptiedTableAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await SeedItemsAsync(h, ct);
        await h.SyncAsync(WithBlobs, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-edit' WHERE folder_id = 1",
            ct
        );

        // アプリの外で品目の表を作り直した形（トリガーは記録しない）
        await h.ExecuteUntrackedAsync("DELETE FROM model_items", ct);
        var onServer = (await h.Server.Items.GetByIdAsync(1, ct))!;
        onServer.Title = "server-edit";
        (await h.Server.Items.UpdateAsync(onServer, cancellationToken: ct)).Should().BeTrue();

        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var serverBefore = Describe(await h.ReadServerAsync(ct));

        foreach (
            var (entry, call) in new (string, Func<Task>)[]
            {
                ("SyncAsync", () => h.SyncAsync(WithBlobs, ct)),
                (
                    "UploadAsync",
                    () => h.Engine.UploadAsync(WithBlobs, new List<SyncConflict>(), ct)
                ),
                ("DownloadAsync", () => h.Engine.DownloadAsync(WithBlobs, ct)),
            }
        )
        {
            var thrown = await Record.ExceptionAsync(call);
            thrown
                .Should()
                .BeOfType<SyncLocalTableEmptiedException>($"{entry} は空にした表を断る")
                .Which.TableNames.Should()
                .Equal([ModelTables.Items], $"{entry} は空にした表を名指しする");
            (await h.ReadStateTablesAsync(ct))
                .Should()
                .Equal(stateBefore, $"{entry} は行の状態の表を変えない");
            Describe(await h.ReadLocalAsync(ct))
                .Should()
                .Be(localBefore, $"{entry} は何も降ろさず、表を洗い替えない");
            Describe(await h.ReadServerAsync(ct))
                .Should()
                .Be(serverBefore, $"{entry} は何も送らない");
        }
    }

    /// <summary>引数の検査：どの入口も何も始めずに断る</summary>
    private static async Task InvalidArgumentsAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.SyncAsync(null, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-edit' WHERE folder_id = 1",
            ct
        );

        // 送るもの（フォルダー 1）と降ろすもの（フォルダー 2）を置いておく
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 2, Name = "f2" }, ct);

        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var serverBefore = Describe(await h.ReadServerAsync(ct));

        foreach (
            var (name, options, expected) in new (string, SyncOptions, Type)[]
            {
                (
                    "batch-zero",
                    new SyncOptions { DownloadBatchSize = 0 },
                    typeof(ArgumentOutOfRangeException)
                ),
                (
                    "batch-negative",
                    new SyncOptions { DownloadBatchSize = -1 },
                    typeof(ArgumentOutOfRangeException)
                ),
                (
                    "unknown-excluded-type",
                    new SyncOptions { ExcludedEntityTypes = [typeof(string)] },
                    typeof(ArgumentException)
                ),
            }
        )
        {
            foreach (
                var (entry, call) in new (string, Func<Task>)[]
                {
                    ("SyncAsync", () => h.SyncAsync(options, ct)),
                    (
                        "UploadAsync",
                        () => h.Engine.UploadAsync(options, new List<SyncConflict>(), ct)
                    ),
                    ("DownloadAsync", () => h.Engine.DownloadAsync(options, ct)),
                }
            )
            {
                var thrown = await Record.ExceptionAsync(call);
                thrown.Should().NotBeNull($"{entry} は {name} を断る");
                thrown!
                    .GetType()
                    .Should()
                    .Be(expected, $"{entry} は {name} を {expected.Name} で断る");
                (await h.ReadStateTablesAsync(ct))
                    .Should()
                    .Equal(stateBefore, $"{entry}（{name}）は行の状態の表を変えない");
                Describe(await h.ReadLocalAsync(ct))
                    .Should()
                    .Be(localBefore, $"{entry}（{name}）は何も降ろさない");
                Describe(await h.ReadServerAsync(ct))
                    .Should()
                    .Be(serverBefore, $"{entry}（{name}）は何も送らない");
            }
        }

        // 洗い替えの入口も、取得の大きさと除外の型を最初に断る（Force と blob を捨てる指定があっても、未送信を捨てず何も変えない）
        foreach (
            var (name, options, expected) in new (string, SyncRefreshOptions, Type)[]
            {
                (
                    "refresh-batch-zero",
                    new SyncRefreshOptions
                    {
                        BatchSize = 0,
                        Force = true,
                        DiscardLocalUnboundedBinaries = true,
                    },
                    typeof(ArgumentOutOfRangeException)
                ),
                (
                    "refresh-batch-negative",
                    new SyncRefreshOptions
                    {
                        BatchSize = -1,
                        Force = true,
                        DiscardLocalUnboundedBinaries = true,
                    },
                    typeof(ArgumentOutOfRangeException)
                ),
                (
                    "refresh-unknown-excluded-type",
                    new SyncRefreshOptions
                    {
                        ExcludedEntityTypes = [typeof(string)],
                        Force = true,
                        DiscardLocalUnboundedBinaries = true,
                    },
                    typeof(ArgumentException)
                ),
            }
        )
        {
            var thrown = await Record.ExceptionAsync(() => h.RefreshAsync(options, ct));
            thrown.Should().NotBeNull($"RefreshAsync は {name} を断る");
            thrown!
                .GetType()
                .Should()
                .Be(expected, $"RefreshAsync は {name} を {expected.Name} で断る");
            (await h.ReadStateTablesAsync(ct))
                .Should()
                .Equal(stateBefore, $"RefreshAsync（{name}）は行の状態の表を変えない");
            Describe(await h.ReadLocalAsync(ct))
                .Should()
                .Be(localBefore, $"RefreshAsync（{name}）は端末を変えない");
            Describe(await h.ReadServerAsync(ct))
                .Should()
                .Be(serverBefore, $"RefreshAsync（{name}）は何も送らない");
        }
    }

    /// <summary>
    /// 洗い替えの後と同期の後の再開点：洗い替えの直後の同期と、同期の直後の同期は、サーバーから 1 行も取得しない（取得が 1 行以上を
    /// 返した後の点が来ない）。対照として、サーバーの他者が 1 行変えた後の同期ではその点が来る
    /// </summary>
    /// <remarks>
    /// 再開点を書かないと、次の同期は古い再開点から取り直す。取り直した行は基準の版が同じなのでエコーとして書かれず、端末と
    /// サーバーの内容には出ない（観測できるのは取得した行の有無だけ）
    /// </remarks>
    private static async Task ResumePointsAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 2, Name = "f2" }, ct);
        await h.Server.Items.InsertAsync(
            new ModelItemEntity
            {
                ItemId = 11,
                FolderId = 1,
                Code = "c-11",
                Title = "t11",
            },
            ct
        );

        var fetchedRows = 0;
        h.Faults.ObservePoint = point =>
        {
            if (point == ModelInterruptPoint.AfterServerFetchBeforeApply)
            {
                Interlocked.Increment(ref fetchedRows);
            }
        };

        try
        {
            await h.RefreshAsync(
                new SyncRefreshOptions { DiscardLocalUnboundedBinaries = true },
                ct
            );
            fetchedRows
                .Should()
                .BeGreaterThan(0, "洗い替えはサーバーの行を取得する（観測が生きている）");

            fetchedRows = 0;
            await h.SyncAsync(null, ct);
            fetchedRows
                .Should()
                .Be(0, "洗い替えの直後の同期は、洗い替えが書いた再開点から取り、何も取得しない");

            var onServer = (await h.Server.Folders.GetByIdAsync(2, ct))!;
            onServer.Name = "f2-edited";
            (await h.Server.Folders.UpdateAsync(onServer, cancellationToken: ct)).Should().BeTrue();
            fetchedRows = 0;
            var result = await h.SyncAsync(null, ct);
            fetchedRows.Should().BeGreaterThan(0, "サーバーの他者が変えた行は、次の同期が取得する");
            result.Downloaded.Should().Be(1, "変わった 1 行だけが降りる");

            fetchedRows = 0;
            await h.SyncAsync(null, ct);
            fetchedRows
                .Should()
                .Be(0, "同期の直後の同期は、その同期が書いた再開点から取り、何も取得しない");
        }
        finally
        {
            h.Faults.ObservePoint = null;
        }
    }

    /// <summary>
    /// 端末が作って消した、サーバーが知らないキーの未送信の行：受信だけの実行で、同じキーのサーバーの行が端末に入る
    /// </summary>
    /// <remarks>
    /// 同期の送信の段は、ローカルに無くサーバーが知らない行の記録を送らずに片付けるので、受信の段まで未送信が残るのは
    /// 受信だけの実行のとき。今のエンジンは未送信の記録のある行をその実行では降ろさない（次の同期で降りる）ので、同期の
    /// モデルの決定的なシナリオには置かず、この表に置く。
    /// </remarks>
    private static async Task UnsentGoneRowOfUnknownKeyAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.SyncAsync(null, ct);
        await h.ExecuteLocalAsync(
            "INSERT INTO model_folders (folder_id, name) VALUES (8, 'local-only')",
            ct
        );
        await h.ExecuteLocalAsync("DELETE FROM model_folders WHERE folder_id = 8", ct);
        (
            await h.ScalarAsync(
                $"SELECT dirty || ':' || known FROM {SyncLocalTracking.RowTableName} WHERE table_name = 'model_folders' AND \"key\" = 8",
                ct
            )
        )
            .Should()
            .Be(
                "1:0",
                "作って消した行は未送信のまま、サーバーが知らない行として残っている（前提）"
            );
        await h.Server.Folders.InsertAsync(
            new ModelFolderEntity { FolderId = 8, Name = "server-side" },
            ct
        );

        await h.Engine.DownloadAsync(new SyncOptions(), ct);

        var local = await h.ReadLocalAsync(ct);
        local
            .Folders.Should()
            .ContainKey(
                8,
                "受信だけの実行が、サーバーが知らないキーの消えた未送信の行の代わりにサーバーの行を入れる"
            );
        local.Folders[8].Name.Should().Be("server-side");

        var result = await h.SyncAsync(null, ct);
        result.Conflicts.Should().BeEmpty();
        result.Truncations.Should().BeEmpty();
        result.Uploaded.Should().Be(0, "端末に送るものは無い");
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
        (
            await h.ScalarAsync(
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                ct
            )
        )
            .Should()
            .Be("0");
    }

    /// <summary>主キーを変えた行の blob</summary>
    /// <remarks>
    /// 生の SQL での主キーの変更は、新しい端末ではトリガーに記録される（利用者の書き込みとして送る）。新しいキーの記録は
    /// サーバーに無い行の挿入として全列を送るので、blob も届く。古いキーはサーバーから消える（一意な code は古いキーの行が持つ
    /// ので、新しいキーの挿入は最初の巡で断られ、削除が通った次の巡で通る）。
    /// </remarks>
    private static async Task PrimaryKeyChangedRowAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedItemsAsync(h, ct);
        await h.SyncAsync(WithBlobs, ct);
        await h.ExecuteLocalAsync("UPDATE model_items SET item_id = 101 WHERE item_id = 1", ct);
        var localBefore = (await h.ReadLocalAsync(ct)).Items[101];

        var result = await h.SyncAsync(WithBlobs, ct);

        result.Conflicts.Should().BeEmpty();
        var server = await h.ReadServerAsync(ct);
        server.Items.Should().NotContainKey(1, "古いキーはサーバーから消える");
        server.Items.Should().ContainKey(101, "新しいキーの行がサーバーへ届く");
        server
            .Items[101]
            .PayloadHex.Should()
            .Be(localBefore.PayloadHex, "新しいキーの blob は端末の blob");
        Describe(await h.ReadLocalAsync(ct)).Should().Be(Describe(server));
        (
            await h.ScalarAsync(
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                ct
            )
        )
            .Should()
            .Be("0");
    }

    /// <summary>一意な値の回転は端末の blob を保つ</summary>
    private static async Task RotationKeepsBlobsAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedItemsAsync(h, ct);
        await h.SyncAsync(WithBlobs, ct);
        var before = await h.ReadLocalAsync(ct);

        // サーバーで品目 1・2 の code を入れ替える（途中は仮の値）
        await SetServerCodeAsync(h, 1, "tmp", ct);
        await SetServerCodeAsync(h, 2, "c-1", ct);
        await SetServerCodeAsync(h, 1, "c-2", ct);

        var result = await h.SyncAsync(WithoutBlobs, ct);

        result.Conflicts.Should().BeEmpty();
        result.Truncations.Should().BeEmpty();
        var local = await h.ReadLocalAsync(ct);
        local.Items[1].Code.Should().Be("c-2", "入れ替えは端末に入る");
        local.Items[2].Code.Should().Be("c-1", "入れ替えは端末に入る");

        foreach (var key in new[] { 1, 2 })
        {
            local
                .Items[key]
                .PayloadHex.Should()
                .NotBeNullOrEmpty($"品目 {key} の blob は空にならない");
            local
                .Items[key]
                .PayloadHex.Should()
                .Be(before.Items[key].PayloadHex, $"品目 {key} の blob は回転の前のまま");
        }
    }

    /// <summary>
    /// サーバーへの書き込みの後・送信の決着の前の失敗（docs の既知の制限「クラッシュが落ち得る隙間が 1 つだけ残ります」の形）
    /// </summary>
    /// <remarks>
    /// 決着の前に失敗すると、行の状態の表の基準版は書く前の版のまま残る。次の同期はその基準版で版を守って送るので、自分が
    /// 書いた版とぶつかる。内容は同じでも競合として報告し続け、LocalWins（か ServerWins）で利用者が決めるまで未送信が残る。
    /// </remarks>
    private static async Task FailureAfterServerWriteAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.SyncAsync(null, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-edit' WHERE folder_id = 1",
            ct
        );

        // 別の接続が書き込みのロックを持ち続ける間に同期する：サーバーへの書き込みは通り、決着の BEGIN IMMEDIATE が
        // Default Timeout（1 秒）で SQLITE_BUSY になる
        Exception? thrown;

        await using (var holder = new SqliteConnection(h.ConnectionString))
        {
            await holder.OpenAsync(ct);
            await using (var begin = holder.CreateCommand())
            {
                begin.CommandText = "BEGIN IMMEDIATE";
                await begin.ExecuteNonQueryAsync(ct);
            }

            thrown = await Record.ExceptionAsync(() => h.SyncAsync(null, ct));

            await using var rollback = holder.CreateCommand();
            rollback.CommandText = "ROLLBACK";
            await rollback.ExecuteNonQueryAsync(ct);
        }

        Chain(thrown ?? new InvalidOperationException("no failure"))
            .OfType<SqliteException>()
            .Should()
            .Contain(
                ex => ex.SqliteErrorCode == 5,
                "送信の決着は書き込みのロックを待って SQLITE_BUSY で失敗する"
            );
        (await h.ReadServerAsync(ct))
            .Folders[1]
            .Name.Should()
            .Be("local-edit", "決着の前の失敗でも、サーバーへの書き込みは通っている");

        foreach (var attempt in new[] { 1, 2 })
        {
            var collect = await h.SyncAsync(null, ct);
            collect
                .Conflicts.Select(conflict =>
                    (conflict.TableName, conflict.KeyText, conflict.Reason)
                )
                .Should()
                .Equal(
                    [(ModelTables.Folders, "1", SyncConflictReason.ModifiedOnServer)],
                    $"{attempt} 回目の Collect は、自分が書いた行を相手に競合を報告する"
                );
            collect
                .Conflicts[0]
                .ServerEntity.Should()
                .BeOfType<ModelFolderEntity>()
                .Which.Name.Should()
                .Be("local-edit", "競合の相手側は、決着の前に自分が書いた行");
            collect.Uploaded.Should().Be(0);
            (
                await h.ScalarAsync(
                    $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                    ct
                )
            )
                .Should()
                .Be("1", $"{attempt} 回目の Collect の後も未送信が残る");
        }

        var localWins = await h.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.LocalWins },
            ct
        );
        localWins.Conflicts.Should().BeEmpty("LocalWins は自分の内容で決着する");
        (
            await h.ScalarAsync(
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                ct
            )
        )
            .Should()
            .Be("0", "LocalWins の後に未送信は残らない");
        var local = await h.ReadLocalAsync(ct);
        Describe(local)
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
        local.Folders[1].Name.Should().Be("local-edit", "端末の編集は消えない");
    }

    /// <summary>洗い替えの取得の失敗は何も変えない</summary>
    /// <remarks>
    /// 新しいエンジンの洗い替えは全部を取得してから 1 つのトランザクションで適用するので、取得の失敗では何も変わらない（今の
    /// エンジンは表を空にしてから流し込むので、同じ失敗の後の端末は途中まで作り直した形になる。この表に置くのはそのため）。
    /// </remarks>
    private static async Task RefreshFetchFailureAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedItemsAsync(h, ct);
        await h.SyncAsync(WithBlobs, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-edit' WHERE folder_id = 1",
            ct
        );
        var onServer = (await h.Server.Items.GetByIdAsync(1, ct))!;
        onServer.Title = "server-edit";
        (await h.Server.Items.UpdateAsync(onServer, cancellationToken: ct)).Should().BeTrue();

        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var forced = new SyncRefreshOptions { Force = true, IncludeUnboundedBinary = true };

        h.Faults.Arm(ModelFaultKind.ServerFetch, ModelActor.Device1);
        var thrown = await Record.ExceptionAsync(() => h.RefreshAsync(forced, ct));

        thrown.Should().NotBeNull("取得の失敗で洗い替えは失敗する");
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(localBefore, "端末の行は洗い替えの前のまま");
        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(
                stateBefore,
                "未送信と行の状態の表は洗い替えの前のまま（Force でも捨てていない）"
            );

        var result = await h.RefreshAsync(forced, ct);
        result.DiscardedChanges.Should().Be(1, "次の洗い替えが未送信を捨てる");
        var local = await h.ReadLocalAsync(ct);
        Describe(local)
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末がサーバーの内容になる");
        local.Folders[1].Name.Should().Be("f1");
        local.Items[1].Title.Should().Be("server-edit");
    }

    /// <summary>サーバーにフォルダー 1 と品目 1・2（blob つき）を入れる</summary>
    private static async Task SeedItemsAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);

        foreach (var key in new[] { 1, 2 })
        {
            await h.Server.Items.InsertAsync(
                new ModelItemEntity
                {
                    ItemId = key,
                    FolderId = 1,
                    Code = $"c-{key}",
                    Title = $"t{key}",
                    Payload = [0x0A, 0x0B, 0x00, (byte)key],
                },
                ct
            );
        }
    }

    private static async Task SetServerCodeAsync(
        SyncEngineFixedHarness h,
        int key,
        string code,
        CancellationToken ct
    )
    {
        var item = (await h.Server.Items.GetByIdAsync(key, ct))!;
        item.Code = code;
        (await h.Server.Items.UpdateAsync(item, cancellationToken: ct)).Should().BeTrue();
    }

    /// <summary>例外とその内側の例外の並び</summary>
    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    /// <summary>DB の内容を比べられる文字列にする（4 表と blob）</summary>
    private static string Describe(ModelDbState state) =>
        string.Join(
            "\n",
            ModelTables
                .All.SelectMany(table =>
                    state
                        .Rows(table)
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{table}/{pair.Key}={pair.Value}")
                )
                .Concat(
                    state
                        .Items.OrderBy(pair => pair.Key)
                        .Select(pair => $"payload/{pair.Key}={pair.Value.PayloadHex}")
                )
        );
}
