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
using Sqlite = QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;

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
                "取得の大きさ・降ろす行の上限が 0 以下・エンジンが同期しない型の除外は、同期・送信だけ・受信だけ・洗い替えのどれも引数の例外で断り、"
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
                "refresh-over-row-limit-with-change-written-during-fetch-is-refused",
                "未送信の無い端末の洗い替え（Force なし）の取得の途中に端末で記録つきの書き込みがあり、取得が降ろす行の上限を"
                    + "超えると、洗い替えは SyncDownloadTooLargeException で断り、何も適用しない（端末の行は洗い替えの前のまま、"
                    + "途中で書いた行とその未送信は残る）",
                RefreshOverRowLimitWithChangeDuringFetchAsync
            ),
            Row(
                "refresh-force-over-row-limit-with-discarded-key-rewritten-during-fetch-is-refused",
                "Force の洗い替えが捨てると決めた未送信の行を、取得の途中に端末がもう一度書く（連番が進む）と、その行は適用が"
                    + "捨てずに残す未送信なので数え、取得が降ろす行の上限を超えた洗い替えは SyncDownloadTooLargeException で断る"
                    + "（端末の行は書き直した内容の未送信のまま、サーバーと再開点は変わらない）",
                RefreshForceOverRowLimitWithDiscardedKeyRewrittenAsync
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
            Row(
                "chunk-failure-between-chunks-keeps-first-chunk-and-next-sync-resumes",
                "塊ごとに確定する経路で 2 ページ目の取得が失敗すると、失敗の時点で最初の塊の行だけが端末に書かれ、表の再開点は"
                    + "最初の塊の最後の行の版まで進んでいる。同期は例外で終わり、次の同期は続きの行だけを取得してそろう",
                ChunkFailureBetweenChunksAsync
            ),
            Row(
                "chunk-failure-after-carry-keeps-resume-point-before-carried-row",
                "最初の塊で親の無い子が持ち越された後、後ろの塊を書いてから取得が失敗すると、表の再開点は持ち越した子の手前で"
                    + "止まっている（後ろの塊の版まで進まない）。次の同期で子が入ってそろう",
                ChunkFailureAfterCarryAsync
            ),
            Row(
                "chunk-unsent-edit-between-chunks-kept-then-serverwins-restores",
                "最初の塊のコミットの後に、まだ書かれていないサーバーの行と同じキーの端末の行を記録つきで編集すると、その実行の後"
                    + "その行は端末の内容のまま未送信で残り、ほかの行は降りている。続く ServerWins の同期でサーバーの内容に戻る",
                (h, ct) => ChunkUnsentEditBetweenChunksAsync(h, SyncConflictPolicy.ServerWins, ct)
            ),
            Row(
                "chunk-unsent-edit-between-chunks-kept-then-localwins-uploads",
                "最初の塊のコミットの後に、まだ書かれていないサーバーの行と同じキーの端末の行を記録つきで編集すると、その実行の後"
                    + "その行は端末の内容のまま未送信で残り、ほかの行は降りている。続く LocalWins の同期で端末の内容がサーバーへ上がる",
                (h, ct) => ChunkUnsentEditBetweenChunksAsync(h, SyncConflictPolicy.LocalWins, ct)
            ),
            Row(
                "chunk-row-gone-after-keys-stays-until-next-sync",
                "塊の経路に入った後、後ろの表（品目）のキーを取った後・その表の行を取る前にサーバーの他者がその表の行を消すと、"
                    + "その実行の後もその行は端末に残り、次の同期の削除伝搬で消える",
                ChunkRowGoneAfterKeysAsync
            ),
            .. RefreshFailureRows(),
            Row(
                "chunk-refresh-force-discard-blobs-failure-keeps-blob-of-unreplaced-discard",
                "塊ごとに確定する Force かつ blob を捨てる洗い替えで、端末が blob を書いた捨てる行のうち、置き換える塊より前で"
                    + "落ちた行は、未送信のまま blob も端末の内容のまま残り（置き換え済みの捨てる行はサーバーの内容で blob が空）、"
                    + "落ちた後の blob を運ぶ同期がその blob をサーバーへ上げる",
                ChunkRefreshForceDiscardBlobsFailureAsync
            ),
            Row(
                "chunk-refresh-force-discard-blobs-completes-with-every-blob-empty",
                "端末が blob を書いた捨てる行のある Force かつ blob を捨てる洗い替えを塊ごとに最後まで流すと、1 回で全部の道と"
                    + "同じく、捨てる行も含めてどの品目の blob も空で、未送信が残らない",
                ChunkRefreshForceDiscardBlobsCompletesAsync
            ),
            Row(
                "chunk-refresh-force-discard-blobs-empties-blob-of-discard-changed-after-ceiling",
                "塊ごとに確定する Force かつ blob を捨てる洗い替えで、端末が blob を書いた捨てる行（品目 12）を、取得の上限の後に"
                    + "サーバーの他者が書き直すと（どの塊もその行を持ってこない）、最後の段がその行の変更を捨てるときに blob も"
                    + "空にする（未送信も写し待ちも残らない）。続く blob を運ばない同期はサーバーの内容を降ろし、blob は空のまま",
                ChunkRefreshForceDiscardBlobsChangedAfterCeilingAsync
            ),
            Row(
                "chunk-refresh-force-discard-gone-after-keys-only-loses-change",
                "塊ごとに確定する Force の洗い替えで、捨てる行（品目 11）がキーを取った後・その表の行を取る前にサーバーで消えると、"
                    + "最後の段がその行の変更だけを捨てる（未送信が外れ、行はその洗い替えでは端末に残る）。次の同期は何も送らず競合も"
                    + "無く、削除伝搬で 11 が消える",
                ChunkRefreshForceDiscardGoneAfterKeysAsync
            ),
            Row(
                "refresh-force-discard-blobs-discard-held-back-is-not-left-unsent-with-empty-blob",
                "Force かつ blob を捨てる洗い替え（1 回で全部の道）で、端末が blob を書いた捨てる行（品目 12）のサーバーの一意な値を、"
                    + "洗い替えの取得の間に端末で書かれた別の行（品目 13）が持っていて 12 を置けないとき、12 を未送信のまま blob だけ"
                    + "空にして残さない（その後に端末が 12 の題名を書いてから流す同期が、サーバーの 12 の blob を空で上書きしない）。"
                    + "同じ洗い替えで、置き換えた捨てる行（10）と未送信の無い行（11）の blob は空になり、13 の blob は残る",
                RefreshForceDiscardBlobsHeldDiscardAsync
            ),
            Row(
                "unsent-rows-condition-reads-state-table-once",
                "未送信の行を外す製品の条件（TrackedSyncEngine.NotUnsentCondition）の実行計画に、表の行ごとに流し直す副問い合わせ"
                    + "（CORRELATED）が無い。行ごとに行の状態の表を引く形（対照の文）には CORRELATED が出る",
                UnsentRowsConditionPlanAsync
            ),
            Row(
                "chunk-download-route-cells",
                "取得の道は（閾値以下・閾値を超え上限以下・上限を超える・上限が閾値以下で上限を超える）×（未送信なし・あり）で、"
                    + "1 回で全部・1 回で全部／塊・1 回で全部／塊・断る／塊・断る（観測の口が取得ごとに 1 回だけ知らせる）。"
                    + "洗い替えは、閾値を超えて未送信なし・Force で捨てる行だけが塊、閾値以下が 1 回で全部",
                ChunkRouteCellsAsync
            ),
            Row(
                "untracked-write-is-not-recorded-and-keeps-earlier-records",
                "記録しない書き込み（SyncLocalTracking.WriteUntrackedAsync）が渡した接続で書いた行は記録されず、前からある"
                    + "未送信の記録・変更の連番・適用中の印は変わらない。その後の普通の書き込みは記録され、同期で送られる",
                UntrackedWriteIsNotRecordedAsync
            ),
            Row(
                "untracked-write-that-throws-leaves-nothing-and-no-transaction",
                "プールの接続で、記録しない書き込みの関数が例外を投げると、同じ例外が出て、書いた行も印も残らず、接続は"
                    + "トランザクションの外に戻る（同じプールの次の接続にトランザクションが残らず、別の接続が待たずに書ける）",
                (h, ct) => UntrackedWriteFailsAsync(h, cancelled: false, ct)
            ),
            Row(
                "untracked-write-cancelled-leaves-nothing-and-no-transaction",
                "プールの接続で、記録しない書き込みが取り消されると、取り消しの例外が出て、書いた行も印も残らず、接続は"
                    + "トランザクションの外に戻る（同じプールの次の接続にトランザクションが残らず、別の接続が待たずに書ける）",
                (h, ct) => UntrackedWriteFailsAsync(h, cancelled: true, ct)
            ),
            Row(
                "untracked-write-holds-off-other-writers-until-commit",
                "記録しない書き込みが流れている間、別の接続の読み取りは待たずにコミットの前の内容（印は 0・書いた行は無い）を"
                    + "読み、別の接続の書き込みはコミットまで進まない。コミットの後に通った書き込みは記録され、記録しない"
                    + "書き込みの行は記録されない",
                UntrackedWriteHoldsOffWritersAsync
            ),
            Row(
                "untracked-write-through-repository-fails-on-lock-wait",
                "記録しない書き込みの関数が、渡された接続でなく生成の Repository（自分の接続を開く）で書くと、書き込みの"
                    + "ロックを Default Timeout まで待って SQLITE_BUSY で失敗し、全体が巻き戻って何も残らない",
                UntrackedWriteThroughRepositoryAsync
            ),
            Row(
                "untracked-write-refuses-connection-in-transaction-and-nested-call",
                "記録しない書き込みは、すでにトランザクションの中の接続を InvalidOperationException で断り、利用者の"
                    + "トランザクションには触らない。関数の中からの入れ子の呼び出しも同じに断り、外側は続けてコミットできる",
                UntrackedWriteRefusesTransactionAsync
            ),
            Row(
                "untracked-write-refuses-closed-connection-and-missing-tracking",
                "記録しない書き込みは、開いていない接続・関数の中で閉じられた接続・状態の表に行の無い DB を "
                    + "InvalidOperationException で断り、どれも何もコミットしない",
                UntrackedWriteRefusesUnusableAsync
            ),
            Row(
                "untracked-write-clears-applying-flag-left-set",
                "適用中の印が 1 のままコミットされて残った DB（その間の書き込みは記録されない）でも、記録しない書き込みは"
                    + "断らずに流れ、終わると印は 0 に戻り、その後の書き込みは記録される",
                UntrackedWriteClearsLeftoverFlagAsync
            ),
            Row(
                "untracked-write-closed-pooled-connection-is-rolled-back-on-next-open",
                "記録しない書き込みの関数がプールの接続を閉じると、名指しの例外が出て、その接続はトランザクションと書き込みの"
                    + "ロックを持ったままプールへ戻る（別の接続の書き込みはロック待ちで失敗する）。同じプールの次の接続を開くと、"
                    + "開くたびの確認がそのトランザクションを巻き戻し、何も残らず、別の接続が書けて記録される",
                UntrackedWriteClosedPooledConnectionAsync
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
                    "row-limit-zero",
                    new SyncOptions { DownloadRowLimit = 0 },
                    typeof(ArgumentOutOfRangeException)
                ),
                (
                    "row-limit-negative",
                    new SyncOptions { DownloadRowLimit = -1 },
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

        // 洗い替えの入口も、取得の大きさ・降ろす行の上限と除外の型を最初に断る（Force と blob を捨てる指定があっても、未送信を捨てず何も変えない）
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
                    "refresh-row-limit-zero",
                    new SyncRefreshOptions
                    {
                        DownloadRowLimit = 0,
                        Force = true,
                        DiscardLocalUnboundedBinaries = true,
                    },
                    typeof(ArgumentOutOfRangeException)
                ),
                (
                    "refresh-row-limit-negative",
                    new SyncRefreshOptions
                    {
                        DownloadRowLimit = -1,
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

    /// <summary>塊の経路で途中で落ちる洗い替えの種類</summary>
    private enum ChunkRefreshKind
    {
        /// <summary>blob を運ぶ普通の洗い替え</summary>
        Plain,

        /// <summary>blob を捨てる洗い替え（<c>DiscardLocalUnboundedBinaries</c>）</summary>
        DiscardBlobs,

        /// <summary>未送信を捨てる洗い替え（<c>Force</c>・blob を運ぶ）</summary>
        Force,
    }

    /// <summary>途中で落ちた洗い替えの次の手</summary>
    private enum ChunkRefreshNext
    {
        /// <summary>同期（blob を運ぶ）</summary>
        Sync,

        /// <summary>同じ洗い替えの流し直し</summary>
        Refresh,
    }

    /// <summary>途中で落ちた洗い替え × 次の手の行（洗い替えの種類 3 つ × 次の手 2 つ）</summary>
    private static IEnumerable<ScenarioRow<SyncEngineFixedCase>> RefreshFailureRows() =>
        from kind in Enum.GetValues<ChunkRefreshKind>()
        from next in Enum.GetValues<ChunkRefreshNext>()
        select Row(
            $"chunk-refresh-{KindName(kind)}-failure-then-{(next == ChunkRefreshNext.Sync ? "sync" : "refresh")}",
            "塊ごとに確定する洗い替え（"
                + KindName(kind)
                + "）がフォルダーの表の 2 ページ目の取得で落ちると、失敗の時点で削除の段（サーバーに無い行・再開点を戻す・"
                + "blob を空にする・サーバーに無い捨てる行の片付け）と最初の塊が済んでいる。次の手が"
                + (
                    next == ChunkRefreshNext.Sync
                        ? "同期なら、続きだけを取得して降ろし、捨てる行の残りはその同期が送る"
                        : "洗い替えの流し直しなら、そろう（Force は残っていた未送信を捨てる）"
                ),
            (h, ct) => ChunkRefreshFailureAsync(h, kind, next, ct)
        );

    /// <summary>洗い替えの種類の行の名前の部分</summary>
    private static string KindName(ChunkRefreshKind kind) =>
        kind switch
        {
            ChunkRefreshKind.Plain => "plain",
            ChunkRefreshKind.DiscardBlobs => "discard-blobs",
            _ => "force",
        };

    /// <summary>
    /// 塊ごとに確定する洗い替えが途中で落ちた後：端末とサーバーにフォルダー 1〜6 と品目 11・12（フォルダー 1 の下・blob つき）が
    /// あり、サーバーの他者がフォルダー 6 を消す（Force の行は、端末がフォルダー 2 と 4 を記録つきで書き直す）。取得の大きさ 3・
    /// 閾値 2・塊 2 の洗い替えは、フォルダーの表の 1 ページ目 [1, 2, 3] で塊の経路へ入り、削除の段を流して塊 [1, 2] を確定し、
    /// 2 ページ目の取得の前で失敗させる
    /// </summary>
    private static async Task ChunkRefreshFailureAsync(
        SyncEngineFixedHarness h,
        ChunkRefreshKind kind,
        ChunkRefreshNext next,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(
            h,
            ct,
            (1, null, "f1"),
            (2, null, "f2"),
            (3, null, "f3"),
            (4, null, "f4"),
            (5, null, "f5"),
            (6, null, "f6")
        );

        foreach (var key in new[] { 11, 12 })
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

        await h.SyncAsync(WithBlobs, ct);
        (await h.Server.Folders.DeleteAsync(6, ct)).Should().BeTrue();

        if (kind == ChunkRefreshKind.Force)
        {
            await h.ExecuteLocalAsync(
                "UPDATE model_folders SET name = 'local-2' WHERE folder_id = 2",
                ct
            );
            await h.ExecuteLocalAsync(
                "UPDATE model_folders SET name = 'local-4' WHERE folder_id = 4",
                ct
            );
        }

        var options = new SyncRefreshOptions
        {
            BatchSize = 3,
            IncludeUnboundedBinary = kind != ChunkRefreshKind.DiscardBlobs,
            DiscardLocalUnboundedBinaries = kind == ChunkRefreshKind.DiscardBlobs,
            Force = kind == ChunkRefreshKind.Force,
        };

        // 失敗の時点の端末の控え（表明は実行の後に行う）
        ModelDbState? atFailure = null;
        string? folderAnchor = null;
        string? itemAnchor = null;
        string? owedCount = null;
        string? dirtyAtFailure = null;
        var pages = 0;
        var engine = SmallChunks(
            h.CreateEngine(async call =>
            {
                if (
                    call.Table == ModelTables.Folders
                    && call.Call == SyncEngineSourceCall.RowsBefore
                    && ++pages == 2
                )
                {
                    atFailure = await h.ReadLocalAsync(ct);
                    folderAnchor = await LocalAnchorAsync(h, ModelTables.Folders, ct);
                    itemAnchor = await LocalAnchorAsync(h, ModelTables.Items, ct);
                    owedCount = await h.ScalarAsync(
                        $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE blob_owed <> ''",
                        ct
                    );
                    dirtyAtFailure = await h.ScalarAsync(
                        $"SELECT group_concat(\"key\", ',') FROM (SELECT \"key\" FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1 ORDER BY \"key\")",
                        ct
                    );

                    throw new InvalidOperationException(InjectedFetchFailure);
                }
            })
        );

        var thrown = await Record.ExceptionAsync(() => h.RefreshAsync(options, ct, engine));

        thrown
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Be(InjectedFetchFailure);
        atFailure.Should().NotBeNull("フォルダーの表の 2 ページ目の取得の前で失敗させた");
        atFailure!
            .Folders.Should()
            .NotContainKey(
                6,
                "失敗の時点で削除の段が済み、サーバーに無いフォルダー 6 が端末から消えている"
            );
        folderAnchor
            .Should()
            .Be(
                await ServerFolderVersionAsync(h, 2, ct),
                "届いた表の再開点は、確定した塊の最後の行（2）の版"
            );
        itemAnchor
            .Should()
            .BeNull("まだ届いていない表（品目）の再開点は、削除の段が戻した「何も取っていない」");

        if (kind == ChunkRefreshKind.DiscardBlobs)
        {
            atFailure
                .Items.Values.Select(item => item.PayloadHex)
                .Should()
                .AllBe("", "blob を捨てる洗い替えは、削除の段で範囲の表の全行の blob を空にする");
            owedCount.Should().Be("0", "blob を捨てる洗い替えの削除の段は写し待ちを消す");
        }
        else
        {
            atFailure
                .Items[11]
                .PayloadHex.Should()
                .Be("0A0B000B", "blob を運ぶ洗い替えは blob に触らない");
        }

        if (kind == ChunkRefreshKind.Force)
        {
            atFailure
                .Folders[2]
                .Name.Should()
                .Be("f2", "置き換え済みの捨てる行（2）はサーバーの内容");
            atFailure
                .Folders[4]
                .Name.Should()
                .Be("local-4", "まだ置き換えていない捨てる行（4）は端末の内容");
            dirtyAtFailure.Should().Be("4", "未送信は置き換え済みの行で外れ、まだの行に残る");
        }
        else
        {
            dirtyAtFailure.Should().BeNull("未送信は無い");
        }

        if (next == ChunkRefreshNext.Sync)
        {
            if (kind == ChunkRefreshKind.DiscardBlobs)
            {
                // この同期が書く行（サーバーの他者が書き直した品目 11）にだけ写し待ちが立って写る
                var item = (await h.Server.Items.GetByIdAsync(11, ct))!;
                item.Title = "t11-edited";
                (await h.Server.Items.UpdateAsync(item, cancellationToken: ct)).Should().BeTrue();
            }

            var fetched = 0;
            var result = await h.SyncAsync(
                WithBlobs,
                ct,
                SmallChunks(
                    h.CreateEngine(call =>
                    {
                        if (
                            call.Table == ModelTables.Folders
                            && call.Call == SyncEngineSourceCall.RowsReturned
                        )
                        {
                            fetched += call.Rows!.Value;
                        }

                        return Task.CompletedTask;
                    })
                )
            );

            fetched
                .Should()
                .Be(3, "次の同期はフォルダーの再開点（2 の版）の続き（3〜5）だけを取得する");
            result.Truncations.Should().BeEmpty();
            result.Conflicts.Should().BeEmpty();

            if (kind == ChunkRefreshKind.Force)
            {
                result
                    .Uploaded.Should()
                    .Be(1, "捨てる行のうちまだ置き換えていなかった 4 は、その同期が普通に送る");
                (await h.Server.Folders.GetByIdAsync(4, ct))!.Name.Should().Be("local-4");
            }

            var local = await h.ReadLocalAsync(ct);
            var server = await h.ReadServerAsync(ct);

            if (kind == ChunkRefreshKind.DiscardBlobs)
            {
                local
                    .Items[11]
                    .PayloadHex.Should()
                    .Be(server.Items[11].PayloadHex, "この同期が書いた品目 11 の blob は写る");
                local
                    .Items[12]
                    .PayloadHex.Should()
                    .Be("", "この同期が書かなかった品目 12 の blob は空のまま");
                (
                    await h.ScalarAsync(
                        $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE blob_owed <> ''",
                        ct
                    )
                )
                    .Should()
                    .Be("0", "写し待ちは写した後に残らない");
                DescribeRows(local)
                    .Should()
                    .Be(DescribeRows(server), "blob の外の内容は端末とサーバーで同じ");
            }
            else
            {
                Describe(local).Should().Be(Describe(server), "端末とサーバーの内容が同じ");
            }
        }
        else
        {
            var result = await h.RefreshAsync(options, ct, SmallChunks(h.CreateEngine()));
            result
                .DiscardedChanges.Should()
                .Be(
                    kind == ChunkRefreshKind.Force ? 1 : 0,
                    "Force の流し直しは残っていた未送信（4）を捨てる"
                );

            var local = await h.ReadLocalAsync(ct);
            var server = await h.ReadServerAsync(ct);

            if (kind == ChunkRefreshKind.DiscardBlobs)
            {
                local.Items.Values.Select(item => item.PayloadHex).Should().AllBe("");
                DescribeRows(local)
                    .Should()
                    .Be(DescribeRows(server), "blob の外の内容は端末とサーバーで同じ");
            }
            else
            {
                Describe(local).Should().Be(Describe(server), "端末とサーバーの内容が同じ");
            }

            (
                await h.ScalarAsync(
                    $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                    ct
                )
            )
                .Should()
                .Be("0");
        }
    }

    /// <summary>
    /// 取得が持ってこなかった捨てる行：端末とサーバーにフォルダー 1 と品目 11・12 があり、端末が 11 を記録つきで書き直す。サーバーの
    /// 他者がフォルダー 2・3 を入れ、Force の洗い替え（閾値 2・塊 2）はフォルダーの表で塊の経路へ入って品目のキーを先に取る。品目の
    /// 表の最初の行の取得の前に、サーバーの他者が 11 を消す
    /// </summary>
    private static async Task ChunkRefreshForceDiscardGoneAfterKeysAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));

        foreach (var key in new[] { 11, 12 })
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

        await h.SyncAsync(WithBlobs, ct);
        await h.ExecuteLocalAsync("UPDATE model_items SET title = 'local' WHERE item_id = 11", ct);
        await InsertServerFoldersAsync(h, ct, (2, null, "f2"), (3, null, "f3"));

        var calls = new List<string>();
        var deleted = false;
        var engine = SmallChunks(
            h.CreateEngine(async call =>
            {
                calls.Add(call.Table + ":" + call.Call);

                if (
                    !deleted
                    && call.Table == ModelTables.Items
                    && call.Call == SyncEngineSourceCall.RowsBefore
                )
                {
                    deleted = true;
                    (await h.Server.Items.DeleteAsync(11, ct)).Should().BeTrue();
                }
            })
        );

        await h.RefreshAsync(
            new SyncRefreshOptions { IncludeUnboundedBinary = true, Force = true },
            ct,
            engine
        );

        deleted.Should().BeTrue("品目の表の行の取得の前に割り込んだ");
        calls
            .IndexOf(ModelTables.Items + ":" + SyncEngineSourceCall.KeysBefore)
            .Should()
            .BeInRange(
                0,
                calls.IndexOf(ModelTables.Items + ":" + SyncEngineSourceCall.RowsBefore) - 1,
                "塊の経路の入口が、品目の表のキーを行より先に取った（11 はキーの集合にあった）"
            );
        (
            await h.ScalarAsync(
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                ct
            )
        )
            .Should()
            .Be("0", "取得が持ってこなかった捨てる行（11）は、最後の段で変更だけを失う");
        (await h.ReadLocalAsync(ct))
            .Items.Should()
            .ContainKey(11, "キーを取った後に消えた行は、その洗い替えでは端末に残る");

        var result = await h.SyncAsync(WithBlobs, ct);

        result.Uploaded.Should().Be(0, "変更を捨てた 11 は送らない");
        result.Conflicts.Should().BeEmpty();
        (await h.ReadLocalAsync(ct)).Items.Should().NotContainKey(11, "次の同期の削除伝搬で消える");
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
    }

    /// <summary>
    /// 置き換える前に落ちた捨てる行の blob：端末とサーバーにフォルダー 1 と品目 10〜13（blob つき）があり、端末が品目 10 と 12 の
    /// blob を記録つきで書く。Force かつ blob を捨てる洗い替え（取得の大きさ 3・閾値 2・塊 2）は、品目の表の 1 ページ目
    /// [10, 11, 12] で塊の経路へ入り、塊 [10, 11] を確定してから 2 ページ目の取得の前で失敗する（12 はまだ置き換えていない）
    /// </summary>
    private static async Task ChunkRefreshForceDiscardBlobsFailureAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedDiscardedBlobWritesAsync(h, ct);

        ModelDbState? atFailure = null;
        string? dirtyAtFailure = null;
        var pages = 0;
        var engine = SmallChunks(
            h.CreateEngine(async call =>
            {
                if (
                    call.Table == ModelTables.Items
                    && call.Call == SyncEngineSourceCall.RowsBefore
                    && ++pages == 2
                )
                {
                    atFailure = await h.ReadLocalAsync(ct);
                    dirtyAtFailure = await h.ScalarAsync(
                        $"SELECT group_concat(\"key\", ',') FROM (SELECT \"key\" FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1 ORDER BY \"key\")",
                        ct
                    );

                    throw new InvalidOperationException(InjectedFetchFailure);
                }
            })
        );

        var thrown = await Record.ExceptionAsync(() =>
            h.RefreshAsync(ForceDiscardBlobs, ct, engine)
        );

        thrown
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Be(InjectedFetchFailure);
        atFailure.Should().NotBeNull("品目の表の 2 ページ目の取得の前で失敗させた");
        dirtyAtFailure
            .Should()
            .Be("12", "置き換え済みの捨てる行（10）は未送信が外れ、まだの行（12）に残る");
        atFailure!
            .Items[10]
            .PayloadHex.Should()
            .Be("", "置き換えた捨てる行の blob は、その塊が空にする");
        atFailure
            .Items[11]
            .PayloadHex.Should()
            .Be("", "捨てる行でない行の blob は、削除の段が空にする");
        atFailure
            .Items[12]
            .PayloadHex.Should()
            .Be("EE12", "まだ置き換えていない捨てる行は、blob も端末で書いた内容のまま残る");

        var result = await h.SyncAsync(WithBlobs, ct);

        result.Conflicts.Should().BeEmpty();
        result
            .Uploaded.Should()
            .Be(1, "まだ置き換えていなかった捨てる行（12）は、その同期が普通に送る");
        (await h.ReadServerAsync(ct))
            .Items[12]
            .PayloadHex.Should()
            .Be(
                "EE12",
                "落ちた後の同期は、端末で書いた blob をサーバーへ上げる（空の blob で上書きしない）"
            );
    }

    /// <summary>Force かつ blob を捨てる、取得の大きさ 3 の洗い替え</summary>
    private static readonly SyncRefreshOptions ForceDiscardBlobs = new()
    {
        BatchSize = 3,
        DiscardLocalUnboundedBinaries = true,
        Force = true,
    };

    /// <summary>
    /// 置き換えた捨てる行の blob：<see cref="ChunkRefreshForceDiscardBlobsFailureAsync"/> と同じ準備で、Force かつ blob を捨てる
    /// 洗い替えを塊ごとに最後まで流すと、1 回で全部の道と同じく、どの品目の blob も空で未送信が残らない（捨てる行 10・12 も、
    /// 置き換えた塊が blob を空にする）
    /// </summary>
    private static async Task ChunkRefreshForceDiscardBlobsCompletesAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedDiscardedBlobWritesAsync(h, ct);
        var routes = new List<TrackedSyncDownloadRoute>();
        var engine = SmallChunks(h.CreateEngine());
        engine.DownloadRouteObserved = routes.Add;

        var result = await h.RefreshAsync(ForceDiscardBlobs, ct, engine);

        routes.Should().Equal([TrackedSyncDownloadRoute.Chunked], "洗い替えは塊の道を通る");
        result.DiscardedChanges.Should().Be(2);
        var local = await h.ReadLocalAsync(ct);
        local
            .Items.OrderBy(pair => pair.Key)
            .Select(pair => pair.Key + "=" + pair.Value.PayloadHex)
            .Should()
            .Equal(
                ["10=", "11=", "12=", "13="],
                "blob を捨てる洗い替えの後、捨てる行も含めてどの品目の blob も空"
            );
        DescribeRows(local)
            .Should()
            .Be(DescribeRows(await h.ReadServerAsync(ct)), "blob の外の内容は端末とサーバーで同じ");
        (
            await h.ScalarAsync(
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                ct
            )
        )
            .Should()
            .Be("0");
    }

    /// <summary>
    /// どの塊も持ってこない捨てる行の blob：<see cref="ChunkRefreshForceDiscardBlobsFailureAsync"/> と同じ準備で、取得の上限を
    /// 洗い替えの始めに固定し（上限の関数がサーバー役の版を 1 つ採って返す）、その直後にサーバーの他者が品目 12 を書き直す。
    /// 12 の版は上限を超えるので、洗い替えの取得は 12 を持ってこない（キーの集合にはある）
    /// </summary>
    private static async Task ChunkRefreshForceDiscardBlobsChangedAfterCeilingAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedDiscardedBlobWritesAsync(h, ct);

        var ceilings = 0;
        var routes = new List<TrackedSyncDownloadRoute>();
        var fetchedItems = 0;
        var engine = SmallChunks(
            h.CreateEngine(
                call =>
                {
                    if (
                        call.Table == ModelTables.Items
                        && call.Call == SyncEngineSourceCall.RowsReturned
                    )
                    {
                        fetchedItems += call.Rows ?? 0;
                    }

                    return Task.CompletedTask;
                },
                async () =>
                {
                    var ceiling = h.Server.TakeVersion();

                    if (++ceilings == 1)
                    {
                        var item = (await h.Server.Items.GetByIdAsync(12, ct))!;
                        item.Title = "t12-after-ceiling";
                        (await h.Server.Items.UpdateAsync(item, cancellationToken: ct))
                            .Should()
                            .BeTrue();
                    }

                    return ceiling;
                }
            )
        );
        engine.DownloadRouteObserved = routes.Add;

        var result = await h.RefreshAsync(ForceDiscardBlobs, ct, engine);

        ceilings.Should().Be(1, "洗い替えは取得の上限を 1 回取り、その直後に 12 を書き直した");
        routes.Should().Equal([TrackedSyncDownloadRoute.Chunked], "洗い替えは塊の道を通る");
        fetchedItems
            .Should()
            .Be(3, "上限の後に書き直した 12 は、洗い替えの取得に入らない（10・11・13 だけ）");
        result.DiscardedChanges.Should().Be(2);
        var local = await h.ReadLocalAsync(ct);
        local.Items[12].Title.Should().Be("t12", "12 はこの洗い替えでは書き直されない");
        local
            .Items.OrderBy(pair => pair.Key)
            .Select(pair => pair.Key + "=" + pair.Value.PayloadHex)
            .Should()
            .Equal(
                ["10=", "11=", "12=", "13="],
                "どの塊も持ってこなかった捨てる行（12）の blob も、変更を捨てるときに空にする"
            );
        (
            await h.ScalarAsync(
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                ct
            )
        )
            .Should()
            .Be("0");
        (
            await h.ScalarAsync(
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE blob_owed <> ''",
                ct
            )
        )
            .Should()
            .Be("0");

        var synced = await h.SyncAsync(null, ct);

        synced.Uploaded.Should().Be(0, "変更を捨てた 12 は送らない");
        synced.Conflicts.Should().BeEmpty();
        local = await h.ReadLocalAsync(ct);
        local.Items[12].Title.Should().Be("t12-after-ceiling", "続く同期がサーバーの内容を降ろす");
        local
            .Items[12]
            .PayloadHex.Should()
            .Be("", "blob を運ばない同期は 12 の blob に触らない（空のまま）");
        DescribeRows(local)
            .Should()
            .Be(DescribeRows(await h.ReadServerAsync(ct)), "blob の外の内容は端末とサーバーで同じ");
    }

    /// <summary>
    /// 置けなかった捨てる行の blob（1 回で全部の道）：<see cref="ChunkRefreshForceDiscardBlobsFailureAsync"/> と同じ準備に加えて、
    /// 端末が品目 12 の code を記録つきで書き換える（12 は blob と code の未送信を持つ捨てる行）。Force かつ blob を捨てる
    /// 洗い替えの品目の最初の取得の前に、端末が品目 13 の code をサーバーの 12 の code（c-12）へ記録つきで書き換える。13 は
    /// 洗い替えが捨てない未送信（端末の内容を残す）なので、サーバーの 12 の行は一意な値がぶつかって置けない。洗い替えの後、
    /// 端末が 12 の題名だけをもう一度書いてから、blob を運ぶ同期を流す
    /// </summary>
    private static async Task RefreshForceDiscardBlobsHeldDiscardAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedDiscardedBlobWritesAsync(h, ct);
        await h.ExecuteLocalAsync("UPDATE model_items SET code = 'tmp-12' WHERE item_id = 12", ct);

        var taken = false;
        var routes = new List<TrackedSyncDownloadRoute>();
        var engine = h.CreateEngine(async call =>
        {
            if (
                !taken
                && call.Table == ModelTables.Items
                && call.Call == SyncEngineSourceCall.RowsBefore
            )
            {
                taken = true;
                await h.ExecuteLocalAsync(
                    "UPDATE model_items SET code = 'c-12' WHERE item_id = 13",
                    ct
                );
            }
        });
        engine.DownloadRouteObserved = routes.Add;

        var thrown = await Record.ExceptionAsync(() =>
            h.RefreshAsync(ForceDiscardBlobs, ct, engine)
        );

        taken.Should().BeTrue("品目の最初の取得の前に、端末の 13 が c-12 を取った");
        routes.Should().OnlyContain(route => route == TrackedSyncDownloadRoute.Single);
        var local = await h.ReadLocalAsync(ct);
        var dirty =
            await h.ScalarAsync(
                $"SELECT group_concat(\"key\" || ':' || blob_columns, ',') FROM (SELECT \"key\", blob_columns FROM {SyncLocalTracking.RowTableName} WHERE table_name = 'model_items' AND dirty = 1 ORDER BY \"key\")",
                ct
            ) ?? "";
        var carried = await h.ScalarAsync(
            $"SELECT sw_seq IS NOT NULL AND sw_seq = change_seq FROM {SyncLocalTracking.RowTableName} WHERE table_name = 'model_items' AND \"key\" = 12",
            ct
        );
        var observed =
            $"洗い替えの結果={(thrown is null ? "成功" : thrown.GetType().Name + ": " + thrown.Message)}／"
            + $"未送信={dirty}／12 の code={local.Items[12].Code}／12 の blob={local.Items[12].PayloadHex}"
            + $"／12 の捨てる決定の持ち越し={carried}";

        // 洗い替えの後、端末が 12 をもう一度書く（題名だけ。blob は書かない）
        await h.ExecuteLocalAsync(
            "UPDATE model_items SET title = 't12-edited' WHERE item_id = 12",
            ct
        );
        var synced = await h.SyncAsync(WithBlobs, ct);
        var server = await h.ReadServerAsync(ct);
        var after = await h.ReadLocalAsync(ct);
        observed +=
            $"／同期が送った数={synced.Uploaded}・競合={string.Join(";", synced.Conflicts.Select(conflict => conflict.Reason))}"
            + $"／サーバーの 12 の code={server.Items[12].Code}・blob={server.Items[12].PayloadHex}"
            + $"／同期の後の端末の 12 の code={after.Items[12].Code}・blob={after.Items[12].PayloadHex}";

        // 表明をまとめて出す（端末に残った形と、続く同期がサーバーへ運んだもの）
        using var scope = new AwesomeAssertions.Execution.AssertionScope();

        local
            .Items.OrderBy(pair => pair.Key)
            .Where(pair => pair.Key != 12)
            .Select(pair => pair.Key + "=" + pair.Value.PayloadHex)
            .Should()
            .Equal(
                ["10=", "11=", "13=0A0B000D"],
                "置き換えた捨てる行（10）と未送信の無い行（11）の blob は空になり、洗い替えの間に書かれた未送信の行（13）の "
                    + "blob は残る（"
                    + observed
                    + "）"
            );

        thrown
            .Should()
            .BeNull("置けなかった捨てる行があっても、洗い替えは成功で終わる（" + observed + "）");
        dirty
            .Should()
            .Be(
                "12:Payload,13:",
                "12 は置けずに未送信のまま残り（blob の書き込みの記録つき＝この行が置けない形に当たった）、13 は洗い替えの間に"
                    + "書かれた未送信（"
                    + observed
                    + "）"
            );
        local
            .Items[12]
            .Code.Should()
            .Be("tmp-12", "置けなかった捨てる行は、行を端末の内容のまま残す（" + observed + "）");
        local
            .Items[12]
            .PayloadHex.Should()
            .Be(
                "EE12",
                "置けなかった捨てる行は、blob も端末で書いた内容のまま残す（" + observed + "）"
            );

        server
            .Items[12]
            .PayloadHex.Should()
            .BeOneOf(
                ["0A0B000C", "EE12"],
                "端末が 12 をもう一度書いた後の同期は、サーバーの 12 の blob を空で上書きしない（"
                    + observed
                    + "）"
            );
    }

    /// <summary>
    /// 未送信の行を外す条件の形：製品の条件を品目の表に掛けた文の実行計画を読む。行の状態の表のキーの列は型の宣言が無いので、
    /// 表のキーの列と行ごとに比べる形は状態の表の索引をキーで引けず、表の行 × 状態の行の総当たりになる（10 万行の表で数分）。
    /// 副問い合わせが表の行に依らなければ、1 回だけ流れて結果を引くだけになる
    /// </summary>
    private static async Task UnsentRowsConditionPlanAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        const string key = "\"model_items\".\"item_id\"";
        var plan = await h.QueryPlanAsync(
            $"UPDATE \"model_items\" SET \"title\" = \"title\" WHERE {TrackedSyncEngine.NotUnsentCondition(key)}",
            ModelTables.Items,
            ct
        );
        var planText = string.Join(" | ", plan);

        // 前提：行の状態の表（別名 s）を読む段が計画にある（SQLite の計画は、表を別名で「SCAN s」「SEARCH s …」と書く）
        plan.Should()
            .Contain(
                line => ReadsStateTable(line),
                "計画が行の状態の表を読んでいる（前提。実際：" + planText + "）"
            );
        plan.Should()
            .NotContain(
                line => line.Contains("CORRELATED", StringComparison.Ordinal),
                "副問い合わせは表の行ごとに流し直さない（実際：" + planText + "）"
            );

        // 対照：行ごとに行の状態の表を引く形は、この SQLite の計画で CORRELATED と出る（上の表明が空振りでない）
        var control = await h.QueryPlanAsync(
            "UPDATE \"model_items\" SET \"title\" = \"title\" WHERE NOT EXISTS (SELECT 1 FROM "
                + SyncLocalTracking.RowTableName
                + $" AS s WHERE s.table_name = @table AND s.dirty = 1 AND {key} = s.\"key\")",
            ModelTables.Items,
            ct
        );
        control
            .Should()
            .Contain(
                line => line.Contains("CORRELATED", StringComparison.Ordinal),
                "行ごとに引く形は CORRELATED と出る（実際：" + string.Join(" | ", control) + "）"
            );
    }

    /// <summary>実行計画の行が、別名 s の表を読む段（SCAN / SEARCH の直後の語が s）か</summary>
    private static bool ReadsStateTable(string line)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(words, "s");

        return at > 0 && words[at - 1] is "SCAN" or "SEARCH";
    }

    /// <summary>
    /// 端末とサーバーにフォルダー 1 と品目 10〜13（blob つき）を置き、端末が品目 10 と 12 の blob を記録つきで書く（取得の順は
    /// 10・11・12・13＝取得の大きさ 3 では 1 ページ目が [10, 11, 12]）
    /// </summary>
    private static async Task SeedDiscardedBlobWritesAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));

        foreach (var key in new[] { 10, 11, 12, 13 })
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

        await h.SyncAsync(WithBlobs, ct);
        byte[] written = [0xEE, 0x12];

        foreach (var key in new[] { 10, 12 })
        {
            await using var source = new System.IO.MemoryStream(written);
            (await h.Items.WritePayloadAsync(key, source, written.Length, ct)).Should().BeTrue();
        }

        (
            await h.ScalarAsync(
                $"SELECT group_concat(blob_columns, ';') FROM {SyncLocalTracking.RowTableName} WHERE table_name = 'model_items' AND dirty = 1",
                ct
            )
        )
            .Should()
            .NotBeNullOrEmpty("品目 10 と 12 は blob の書き込みの未送信を持つ（前提）");
    }

    /// <summary>塊の経路の行で、観測が投げる取得の失敗の文</summary>
    private const string InjectedFetchFailure = "Injected fetch failure between chunks.";

    /// <summary>エンジンの塊へ入る閾値と塊の大きさを、小さい塊の端末と同じ値にする</summary>
    private static TrackedSyncEngine SmallChunks(TrackedSyncEngine engine)
    {
        engine.ChunkThreshold = SyncModelEngineDevice.SmallChunkThreshold;
        engine.ChunkSize = SyncModelEngineDevice.SmallChunkSize;

        return engine;
    }

    /// <summary>サーバーの他者がフォルダーを入れる（並べた順に版が付く）</summary>
    private static async Task InsertServerFoldersAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct,
        params (int Id, int? Parent, string Name)[] folders
    )
    {
        foreach (var (id, parent, name) in folders)
        {
            await h.Server.Folders.InsertAsync(
                new ModelFolderEntity
                {
                    FolderId = id,
                    ParentId = parent,
                    Name = name,
                },
                ct
            );
        }
    }

    /// <summary>サーバーの他者がフォルダーの名前を書き直す（版が進む）</summary>
    private static async Task RenameServerFolderAsync(
        SyncEngineFixedHarness h,
        int id,
        string name,
        CancellationToken ct
    )
    {
        var folder = (await h.Server.Folders.GetByIdAsync(id, ct))!;
        folder.Name = name;
        (await h.Server.Folders.UpdateAsync(folder, cancellationToken: ct)).Should().BeTrue();
    }

    /// <summary>端末の表の再開点（16 進。無ければ null）</summary>
    private static Task<string?> LocalAnchorAsync(
        SyncEngineFixedHarness h,
        string table,
        CancellationToken ct
    ) =>
        h.ScalarAsync(
            $"SELECT CASE WHEN anchor IS NULL THEN NULL ELSE hex(anchor) END FROM {SyncLocalTracking.AnchorTableName} WHERE table_name = '{table}'",
            ct
        );

    /// <summary>サーバーのフォルダーの版（16 進）</summary>
    private static async Task<string> ServerFolderVersionAsync(
        SyncEngineFixedHarness h,
        int id,
        CancellationToken ct
    ) => Convert.ToHexString((await h.Server.Folders.GetByIdAsync(id, ct))!.RowVer!);

    /// <summary>
    /// フォルダーの表の n 回目の行のページの取得の前に、端末のフォルダーのキーと再開点を控えてから投げる観測を作る（控えは
    /// <paramref name="seen"/> へ入れる。表明は実行の後に行う＝観測の中で投げた表明は取得の失敗に紛れるため）
    /// </summary>
    private static Func<SyncEngineSourceCall, Task> FailOnFolderPage(
        SyncEngineFixedHarness h,
        int page,
        List<(IReadOnlyList<int> Keys, string? Anchor)> seen,
        CancellationToken ct
    )
    {
        var pages = 0;

        return async call =>
        {
            if (
                call.Table == ModelTables.Folders
                && call.Call == SyncEngineSourceCall.RowsBefore
                && ++pages == page
            )
            {
                var local = await h.ReadLocalAsync(ct);
                seen.Add(
                    (
                        local.Folders.Keys.Order().ToList(),
                        await LocalAnchorAsync(h, ModelTables.Folders, ct)
                    )
                );

                throw new InvalidOperationException(InjectedFetchFailure);
            }
        };
    }

    /// <summary>
    /// 塊の間で落ちた実行：フォルダー 1〜5 を、取得の大きさ 3・閾値 2・塊 2 で降ろす。1 ページ目 [1, 2, 3] で塊の経路へ入り、塊
    /// [1, 2] を確定してから 2 ページ目の取得で失敗させる。失敗の時点で端末には 1・2 だけがあり、再開点は 2 の版。次の同期は
    /// 3〜5 だけを取得して降ろす
    /// </summary>
    private static async Task ChunkFailureBetweenChunksAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(
            h,
            ct,
            (1, null, "f1"),
            (2, null, "f2"),
            (3, null, "f3"),
            (4, null, "f4"),
            (5, null, "f5")
        );
        var options = new SyncOptions { DownloadBatchSize = 3 };
        var seen = new List<(IReadOnlyList<int> Keys, string? Anchor)>();

        var thrown = await Record.ExceptionAsync(() =>
            h.SyncAsync(options, ct, SmallChunks(h.CreateEngine(FailOnFolderPage(h, 2, seen, ct))))
        );

        thrown
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Be(InjectedFetchFailure);
        seen.Should().ContainSingle("2 ページ目の取得の前で 1 回だけ失敗させた");
        seen[0]
            .Keys.Should()
            .Equal([1, 2], "失敗の時点で、最初の塊（1・2）だけが端末に書かれている");
        seen[0]
            .Anchor.Should()
            .Be(
                await ServerFolderVersionAsync(h, 2, ct),
                "失敗の時点で、表の再開点は最初の塊の最後の行（2）の版まで進んでいる"
            );
        (await LocalAnchorAsync(h, ModelTables.Folders, ct))
            .Should()
            .Be(seen[0].Anchor, "失敗した実行は、確定した塊の再開点を残す");

        var fetched = 0;
        var result = await h.SyncAsync(
            options,
            ct,
            SmallChunks(
                h.CreateEngine(call =>
                {
                    if (
                        call.Table == ModelTables.Folders
                        && call.Call == SyncEngineSourceCall.RowsReturned
                    )
                    {
                        fetched += call.Rows!.Value;
                    }

                    return Task.CompletedTask;
                })
            )
        );

        fetched.Should().Be(3, "次の同期は再開点の後ろ（3〜5）だけを取得する");
        result.Downloaded.Should().Be(3);
        result.Truncations.Should().BeEmpty();
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
    }

    /// <summary>
    /// 持ち越しがある表で落ちた実行：取得の順は 1・3（親 2 の子）・2（子の後に書き直した親）・4・5。取得の大きさ 4・閾値 2・塊 2
    /// で、1 ページ目で塊の経路へ入り、塊 [1, 3] で 3 が持ち越され（再開点は 1 の版）、塊 [2, 4] を書いてから 2 ページ目の取得で
    /// 失敗させる。再開点は 1 の版で止まったまま（4 の版まで進めない）。次の同期は 3 から取り直し、3 と 5 が入る
    /// </summary>
    private static async Task ChunkFailureAfterCarryAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(
            h,
            ct,
            (1, null, "filler"),
            (2, null, "parent"),
            (3, 2, "child")
        );
        await RenameServerFolderAsync(h, 2, "parent-renamed", ct);
        await InsertServerFoldersAsync(h, ct, (4, null, "f4"), (5, null, "f5"));
        var options = new SyncOptions { DownloadBatchSize = 4 };
        var seen = new List<(IReadOnlyList<int> Keys, string? Anchor)>();

        var thrown = await Record.ExceptionAsync(() =>
            h.SyncAsync(options, ct, SmallChunks(h.CreateEngine(FailOnFolderPage(h, 2, seen, ct))))
        );

        thrown
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Be(InjectedFetchFailure);
        seen.Should().ContainSingle("2 ページ目の取得の前で 1 回だけ失敗させた");
        seen[0]
            .Keys.Should()
            .Equal(
                [1, 2, 4],
                "失敗の時点で、塊 [1, 3] の 1 と塊 [2, 4] が書かれ、持ち越した 3 は書かれていない"
            );
        seen[0]
            .Anchor.Should()
            .Be(
                await ServerFolderVersionAsync(h, 1, ct),
                "持ち越しのある表の再開点は、持ち越した最初の行（3）の手前（1 の版）で止まり、後ろの塊の版まで進まない"
            );
        (await LocalAnchorAsync(h, ModelTables.Folders, ct)).Should().Be(seen[0].Anchor);

        var result = await h.SyncAsync(options, ct, SmallChunks(h.CreateEngine()));

        result
            .Downloaded.Should()
            .Be(
                2,
                "次の同期は 3 から取り直し、持ち越していた 3 と続きの 5 が入る（2・4 はエコー）"
            );
        result.Truncations.Should().BeEmpty();
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
    }

    /// <summary>
    /// 塊の間に端末が書いた未送信の行：端末とサーバーにフォルダー 1〜5 があり、サーバーの他者が 1〜5 の名前を順に書き直す。取得の
    /// 大きさ 3・閾値 2・塊 2 の同期の、塊 [1, 2] の確定の後（2 ページ目の取得の前）に、端末がまだ書かれていない 4 を記録つきで
    /// 編集する。その実行の後 4 は端末の内容のまま未送信で、ほかの行はサーバーの内容。続く同期が <paramref name="policy"/> で決める
    /// </summary>
    private static async Task ChunkUnsentEditBetweenChunksAsync(
        SyncEngineFixedHarness h,
        SyncConflictPolicy policy,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(
            h,
            ct,
            (1, null, "f1"),
            (2, null, "f2"),
            (3, null, "f3"),
            (4, null, "f4"),
            (5, null, "f5")
        );
        await h.SyncAsync(null, ct);

        for (var id = 1; id <= 5; id++)
        {
            await RenameServerFolderAsync(h, id, $"s{id}", ct);
        }

        var pages = 0;
        var namesAtEdit = new List<string>();
        var engine = SmallChunks(
            h.CreateEngine(async call =>
            {
                if (
                    call.Table == ModelTables.Folders
                    && call.Call == SyncEngineSourceCall.RowsBefore
                    && ++pages == 2
                )
                {
                    var local = await h.ReadLocalAsync(ct);
                    namesAtEdit.AddRange(
                        local.Folders.OrderBy(pair => pair.Key).Select(pair => pair.Value.Name)
                    );
                    await h.ExecuteLocalAsync(
                        "UPDATE model_folders SET name = 'local-4' WHERE folder_id = 4",
                        ct
                    );
                }
            })
        );

        var first = await h.SyncAsync(new SyncOptions { DownloadBatchSize = 3 }, ct, engine);

        namesAtEdit
            .Should()
            .Equal(
                ["s1", "s2", "f3", "f4", "f5"],
                "編集の時点で、最初の塊（1・2）はコミットされ、3〜5 はまだ書かれていない"
            );
        first.Truncations.Should().BeEmpty();
        first.Conflicts.Should().BeEmpty();
        var local = await h.ReadLocalAsync(ct);
        local
            .Folders.OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.Name)
            .Should()
            .Equal(
                ["s1", "s2", "s3", "local-4", "s5"],
                "4 は端末の内容のまま、ほかの行は降りている"
            );
        (
            await h.ScalarAsync(
                $"SELECT dirty FROM {SyncLocalTracking.RowTableName} WHERE table_name = 'model_folders' AND \"key\" = 4",
                ct
            )
        )
            .Should()
            .Be("1", "4 は未送信のまま残る");
        (await h.Server.Folders.GetByIdAsync(4, ct))!.Name.Should().Be("s4");

        await h.SyncAsync(new SyncOptions { ConflictPolicy = policy }, ct);

        var expected = policy == SyncConflictPolicy.ServerWins ? "s4" : "local-4";
        (await h.ReadLocalAsync(ct)).Folders[4].Name.Should().Be(expected);
        (await h.Server.Folders.GetByIdAsync(4, ct))!.Name.Should().Be(expected);
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

    /// <summary>
    /// キーの後・行の前にサーバーで消えた行：端末とサーバーにフォルダー 1 と品目 11 がある。サーバーの他者がフォルダー 2〜4 を
    /// 入れ、同期（閾値 2・塊 2）はフォルダーの表で塊の経路へ入り、後ろの表のキーを先に取る。品目の表の最初の行の取得の前に
    /// サーバーの他者が品目 11 を消す（品目のキーはもう取ってある）。その実行の後も 11 は端末に残り、次の同期で消える
    /// </summary>
    private static async Task ChunkRowGoneAfterKeysAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.Server.Items.InsertAsync(
            new ModelItemEntity
            {
                ItemId = 11,
                FolderId = 1,
                Code = "c-11",
                Title = "t11",
                Payload = [0x0A, 0x0B, 0x00, 0x0B],
            },
            ct
        );
        await h.SyncAsync(WithBlobs, ct);
        await InsertServerFoldersAsync(h, ct, (2, null, "f2"), (3, null, "f3"), (4, null, "f4"));

        var calls = new List<string>();
        var deleted = false;
        var engine = SmallChunks(
            h.CreateEngine(async call =>
            {
                calls.Add(call.Table + ":" + call.Call);

                if (
                    !deleted
                    && call.Table == ModelTables.Items
                    && call.Call == SyncEngineSourceCall.RowsBefore
                )
                {
                    deleted = true;
                    (await h.Server.Items.DeleteAsync(11, ct)).Should().BeTrue();
                }
            })
        );

        var result = await h.SyncAsync(WithBlobs, ct, engine);

        deleted.Should().BeTrue("品目の表の行の取得の前に割り込んだ");
        calls
            .IndexOf(ModelTables.Items + ":" + SyncEngineSourceCall.KeysBefore)
            .Should()
            .BeInRange(
                0,
                calls.IndexOf(ModelTables.Items + ":" + SyncEngineSourceCall.RowsBefore) - 1,
                "塊の経路の入口が、品目の表のキーを行より先に取った（割り込みは入口の後に当たった）"
            );
        result.Downloaded.Should().Be(3);
        (await h.ReadLocalAsync(ct))
            .Items.Should()
            .ContainKey(11, "キーを取った後に消えた行は、その実行では端末に残る");

        await h.SyncAsync(WithBlobs, ct);

        (await h.ReadLocalAsync(ct)).Items.Should().NotContainKey(11, "次の同期の削除伝搬で消える");
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
    }

    /// <summary>
    /// 取得の道の枠：枠ごとに別の端末とサーバー役を作り、サーバーの他者が入れたフォルダーを受信だけで降ろす（未送信ありの枠は、
    /// 端末がサーバーの知らないフォルダー 50 を未送信のまま持つ）。観測の口が知らせた道が、枠ごとにちょうど 1 つ期待のとおり
    /// </summary>
    private static async Task ChunkRouteCellsAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        (
            string Name,
            int Threshold,
            int Limit,
            int Fetched,
            bool Unsent,
            TrackedSyncDownloadRoute Route
        )[] cells =
        [
            ("below-threshold", 2, 100, 2, false, TrackedSyncDownloadRoute.Single),
            ("below-threshold-unsent", 2, 100, 2, true, TrackedSyncDownloadRoute.Single),
            ("over-threshold-within-limit", 2, 100, 3, false, TrackedSyncDownloadRoute.Chunked),
            (
                "over-threshold-within-limit-unsent",
                2,
                100,
                3,
                true,
                TrackedSyncDownloadRoute.Single
            ),
            ("over-limit", 2, 3, 4, false, TrackedSyncDownloadRoute.Chunked),
            ("over-limit-unsent", 2, 3, 4, true, TrackedSyncDownloadRoute.Refused),
            ("limit-below-threshold-over-limit", 10, 2, 3, false, TrackedSyncDownloadRoute.Chunked),
            (
                "limit-below-threshold-over-limit-unsent",
                10,
                2,
                3,
                true,
                TrackedSyncDownloadRoute.Refused
            ),
        ];

        foreach (var (name, threshold, limit, fetched, unsent, route) in cells)
        {
            // 行に渡された端末は使わず、枠ごとに空の端末とサーバー役を作る（取得する行の数を枠ごとに決めるため）
            await using var cell = await SyncEngineFixedHarness.CreateAsync(ct);

            for (var id = 1; id <= fetched; id++)
            {
                await InsertServerFoldersAsync(cell, ct, (id, null, $"f{id}"));
            }

            if (unsent)
            {
                await cell.ExecuteLocalAsync(
                    "INSERT INTO model_folders (folder_id, name) VALUES (50, 'unsent')",
                    ct
                );
            }

            var routes = new List<TrackedSyncDownloadRoute>();
            var engine = cell.CreateEngine();
            engine.ChunkThreshold = threshold;
            engine.ChunkSize = SyncModelEngineDevice.SmallChunkSize;
            engine.DownloadRouteObserved = routes.Add;

            var thrown = await Record.ExceptionAsync(() =>
                engine.DownloadAsync(new SyncOptions { DownloadRowLimit = limit }, ct)
            );

            routes.Should().Equal([route], $"枠 {name} の取得は 1 回で、道は {route}");

            if (route == TrackedSyncDownloadRoute.Refused)
            {
                thrown.Should().BeOfType<SyncDownloadTooLargeException>($"枠 {name} は断る");
                (await cell.ReadLocalAsync(ct))
                    .Folders.Keys.Should()
                    .Equal([50], $"枠 {name} は何も適用しない");
            }
            else
            {
                thrown.Should().BeNull($"枠 {name} は断らない");
                (await cell.ReadLocalAsync(ct))
                    .Folders.Count.Should()
                    .Be(fetched + (unsent ? 1 : 0), $"枠 {name} は取得した行を全部降ろす");
            }
        }

        // 洗い替えの枠：閾値を超えて未送信なし・Force で捨てる行だけ（未送信に数えない）→ 塊、閾値以下 → 1 回で全部
        (
            string Name,
            int Fetched,
            bool ForceDiscard,
            TrackedSyncDownloadRoute Route
        )[] refreshCells =
        [
            ("refresh-over-threshold", 3, false, TrackedSyncDownloadRoute.Chunked),
            ("refresh-force-discards-only", 3, true, TrackedSyncDownloadRoute.Chunked),
            ("refresh-below-threshold", 2, false, TrackedSyncDownloadRoute.Single),
        ];

        foreach (var (name, fetched, forceDiscard, route) in refreshCells)
        {
            await using var cell = await SyncEngineFixedHarness.CreateAsync(ct);

            for (var id = 1; id <= fetched; id++)
            {
                await InsertServerFoldersAsync(cell, ct, (id, null, $"f{id}"));
            }

            if (forceDiscard)
            {
                await cell.SyncAsync(null, ct);
                await cell.ExecuteLocalAsync(
                    "UPDATE model_folders SET name = 'local-1' WHERE folder_id = 1",
                    ct
                );
            }

            var routes = new List<TrackedSyncDownloadRoute>();
            var engine = cell.CreateEngine();
            engine.ChunkThreshold = 2;
            engine.ChunkSize = SyncModelEngineDevice.SmallChunkSize;
            engine.DownloadRouteObserved = routes.Add;

            var result = await engine.RefreshAsync(
                new SyncRefreshOptions
                {
                    DiscardLocalUnboundedBinaries = true,
                    Force = forceDiscard,
                },
                ct
            );

            routes.Should().Equal([route], $"枠 {name} の洗い替えの取得は 1 回で、道は {route}");
            result.DiscardedChanges.Should().Be(forceDiscard ? 1 : 0);
            (await cell.ReadLocalAsync(ct))
                .Folders.OrderBy(pair => pair.Key)
                .Select(pair => pair.Value.Name)
                .Should()
                .Equal(
                    Enumerable.Range(1, fetched).Select(id => $"f{id}"),
                    $"枠 {name} は端末をサーバーの内容にする"
                );
        }

        _ = h;
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

    /// <summary>
    /// Force の洗い替えが捨てると決めた行を取得の途中に書き直すと、その行は捨てる未送信でなくなるので数え、上限を超えた
    /// 洗い替えを断る
    /// </summary>
    private static async Task RefreshForceOverRowLimitWithDiscardedKeyRewrittenAsync(
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

        foreach (var id in new[] { 2, 3, 4 })
        {
            await h.Server.Folders.InsertAsync(
                new ModelFolderEntity { FolderId = id, Name = $"f{id}" },
                ct
            );
        }

        const string anchorTable = "quicker_sync_table|";
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var serverBefore = Describe(await h.ReadServerAsync(ct));
        var sequenceSql =
            $"SELECT change_seq FROM {SyncLocalTracking.RowTableName} "
            + "WHERE table_name = 'model_folders' AND \"key\" = 1 AND dirty = 1";
        var sequenceBefore = await h.ScalarAsync(sequenceSql, ct);
        sequenceBefore.Should().NotBeNull("前準備の後のフォルダー 1 は未送信");

        // 最初のページ（1 行）を取得した後・適用の前に、捨てると決めたフォルダー 1 を端末でもう一度書く（記録つき）
        var wrote = false;
        h.Faults.ArmInterrupt(
            ModelActor.Device1,
            ModelInterruptPoint.AfterServerFetchBeforeApply,
            ModelActor.Server,
            async () =>
            {
                await h.ExecuteLocalAsync(
                    "UPDATE model_folders SET name = 'rewritten' WHERE folder_id = 1",
                    ct
                );
                wrote = true;
            }
        );

        var thrown = await Record.ExceptionAsync(() =>
            h.RefreshAsync(
                new SyncRefreshOptions
                {
                    BatchSize = 1,
                    DownloadRowLimit = 2,
                    Force = true,
                    DiscardLocalUnboundedBinaries = true,
                },
                ct
            )
        );

        wrote.Should().BeTrue("取得の途中の端末の書き込みが流れた");
        h.Faults.InterruptTimedOut.Should().BeNull("取得の途中の端末の書き込みはロックを待たない");
        thrown
            .Should()
            .BeOfType<SyncDownloadTooLargeException>(
                "書き直した行は捨てる未送信でなく、上限を超えた"
            );
        var tooLarge = (SyncDownloadTooLargeException)thrown!;
        tooLarge.RowLimit.Should().Be(2);
        tooLarge
            .PendingChanges.Should()
            .Equal([new SyncPendingChange("model_folders", 1)], "数えるのは書き直した 1 行");

        var local = await h.ReadLocalAsync(ct);
        local.Folders.Keys.Order().Should().Equal([1], "サーバーの新しい行は適用されない");
        local.Folders[1].Name.Should().Be("rewritten", "書き直した内容のまま残る");
        var sequenceAfter = await h.ScalarAsync(sequenceSql, ct);
        sequenceAfter.Should().NotBeNull("書き直した行は未送信のまま");
        sequenceAfter.Should().NotBe(sequenceBefore, "書き直しで連番が進んだ");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "洗い替えは何も送らない");
        (await h.ReadStateTablesAsync(ct))
            .Where(line => line.StartsWith(anchorTable, StringComparison.Ordinal))
            .Should()
            .Equal(
                stateBefore.Where(line => line.StartsWith(anchorTable, StringComparison.Ordinal)),
                "再開点は進まない"
            );
    }

    /// <summary>
    /// 洗い替えの取得の途中に端末で書いた未送信は、Force が捨てる未送信でないので数え、上限を超えた洗い替えを断る
    /// </summary>
    private static async Task RefreshOverRowLimitWithChangeDuringFetchAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.SyncAsync(null, ct);

        foreach (var id in new[] { 2, 3, 4 })
        {
            await h.Server.Folders.InsertAsync(
                new ModelFolderEntity { FolderId = id, Name = $"f{id}" },
                ct
            );
        }

        var rowTable = SyncLocalTracking.RowTableName + "|";
        const string anchorTable = "quicker_sync_table|";
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var serverBefore = Describe(await h.ReadServerAsync(ct));
        (
            await h.ScalarAsync(
                $"SELECT COUNT(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1",
                ct
            )
        )
            .Should()
            .Be("0", "前準備の後の端末に未送信は無い");

        // 最初のページ（1 行）を取得した後・適用の前に、端末で記録つきの書き込みを 1 つ流す（洗い替えの間は端末の割り込みを
        // 起こさない仕掛けなので、書き手はサーバーの他者として仕掛ける。書くのは端末の DB）
        var wrote = false;
        h.Faults.ArmInterrupt(
            ModelActor.Device1,
            ModelInterruptPoint.AfterServerFetchBeforeApply,
            ModelActor.Server,
            async () =>
            {
                await h.ExecuteLocalAsync(
                    "INSERT INTO model_folders (folder_id, parent_id, name) VALUES (50, NULL, 'during')",
                    ct
                );
                wrote = true;
            }
        );

        var thrown = await Record.ExceptionAsync(() =>
            h.RefreshAsync(
                new SyncRefreshOptions
                {
                    BatchSize = 1,
                    DownloadRowLimit = 2,
                    DiscardLocalUnboundedBinaries = true,
                },
                ct
            )
        );

        wrote.Should().BeTrue("取得の途中の端末の書き込みが流れた");
        h.Faults.InterruptTimedOut.Should().BeNull("取得の途中の端末の書き込みはロックを待たない");
        thrown
            .Should()
            .BeOfType<SyncDownloadTooLargeException>("上限を超え、途中で書いた未送信がある");
        var tooLarge = (SyncDownloadTooLargeException)thrown!;
        tooLarge.RowLimit.Should().Be(2);
        tooLarge.FetchedRowCount.Should().Be(3, "1 行ずつのページの 3 つ目で上限を超える");
        tooLarge
            .PendingChanges.Should()
            .Equal([new SyncPendingChange("model_folders", 1)], "数えるのは途中で書いた 1 行");

        var local = await h.ReadLocalAsync(ct);
        local
            .Folders.Keys.Order()
            .Should()
            .Equal([1, 50], "サーバーの新しい行は適用されず、途中で書いた行は残る");
        local.Folders[50].Name.Should().Be("during");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "洗い替えは何も送らない");

        var stateAfter = await h.ReadStateTablesAsync(ct);
        stateAfter
            .Where(line => line.StartsWith(anchorTable, StringComparison.Ordinal))
            .Should()
            .Equal(
                stateBefore.Where(line => line.StartsWith(anchorTable, StringComparison.Ordinal)),
                "再開点は進まない"
            );
        stateBefore
            .Where(line => line.StartsWith(rowTable, StringComparison.Ordinal))
            .Except(stateAfter)
            .Should()
            .BeEmpty("前からある行の状態は変わらない");
        stateAfter
            .Where(line => line.StartsWith(rowTable, StringComparison.Ordinal))
            .Except(stateBefore)
            .Should()
            .ContainSingle("途中で書いた行の未送信だけが増える")
            .Which.Should()
            .StartWith(rowTable + "model_folders|50|");
    }

    /// <summary>記録しない書き込み：書いた行は記録されず、前からある記録は変わらない</summary>
    private static async Task UntrackedWriteIsNotRecordedAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedFolderAndSyncAsync(h, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-edit' WHERE folder_id = 1",
            ct
        );
        var stateBefore = await h.ReadStateTablesAsync(ct);

        await using (var connection = h.Rows.CreateDedicatedConnection())
        {
            await connection.OpenAsync(ct);
            await SyncLocalTracking.WriteUntrackedAsync(
                connection,
                async (inside, token) =>
                {
                    await h.Rows.InsertAsync(
                        inside,
                        new ModelFolderEntity { FolderId = 9, Name = "untracked" },
                        token
                    );
                    await ExecuteOnAsync(
                        inside,
                        "UPDATE model_folders SET name = 'untracked-edit' WHERE folder_id = 1",
                        token
                    );
                    (await ScalarOnAsync(inside, ApplyingSql, token))
                        .Should()
                        .Be("1", "関数の中の接続からは、適用中の印が立って見える");
                },
                ct
            );
        }

        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(
                stateBefore,
                "書いた行は記録されず、前からある未送信の記録・変更の連番・印は変わらない"
            );
        var local = await h.ReadLocalAsync(ct);
        local.Folders[9].Name.Should().Be("untracked");
        local.Folders[1].Name.Should().Be("untracked-edit");

        // その後の普通の書き込みは記録され、前からの未送信と一緒に送られる
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'tracked' WHERE folder_id = 9",
            ct
        );
        (await RecordedAsync(h, 9, ct)).Should().Be("1", "印を戻した後の書き込みは記録される");
        var result = await h.SyncAsync(null, ct);
        result.Conflicts.Should().BeEmpty();
        Describe(await h.ReadServerAsync(ct)).Should().Be(Describe(await h.ReadLocalAsync(ct)));
        (await h.ReadServerAsync(ct)).Folders[9].Name.Should().Be("tracked");
    }

    /// <summary>
    /// 記録しない書き込みが失敗で終わる（関数の例外・取り消し）：何も残らず、プールの接続にトランザクションが残らない
    /// </summary>
    private static async Task UntrackedWriteFailsAsync(
        SyncEngineFixedHarness h,
        bool cancelled,
        CancellationToken ct
    )
    {
        await SeedFolderAndSyncAsync(h, ct);
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));

        // 端末の接続ファクトリの普通の接続（プールされる）
        await using (var connection = h.Factory.CreateConnection())
        {
            await connection.OpenAsync(ct);

            if (cancelled)
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var thrown = await Record.ExceptionAsync(() =>
                    SyncLocalTracking.WriteUntrackedAsync(
                        connection,
                        async (inside, token) =>
                        {
                            await ExecuteOnAsync(inside, InsertFolderSql(9), token);
                            await cancel.CancelAsync();
                            await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        },
                        cancel.Token
                    )
                );
                thrown
                    .Should()
                    .BeAssignableTo<OperationCanceledException>(
                        "取り消しは取り消しの例外のまま出る"
                    );
            }
            else
            {
                var failure = new InvalidOperationException("the writes failed");
                var thrown = await Record.ExceptionAsync(() =>
                    SyncLocalTracking.WriteUntrackedAsync(
                        connection,
                        async (inside, token) =>
                        {
                            await ExecuteOnAsync(inside, InsertFolderSql(9), token);
                            throw failure;
                        },
                        ct
                    )
                );
                thrown.Should().BeSameAs(failure, "関数の例外はそのまま出る");
            }

            connection.State.Should().Be(System.Data.ConnectionState.Open);
            await ShouldBeOutsideTransactionAsync(
                connection,
                "失敗の後、渡した接続はトランザクションの外に戻る",
                ct
            );
        }

        (await h.ReadStateTablesAsync(ct)).Should().Equal(stateBefore, "印も記録も残らない");
        Describe(await h.ReadLocalAsync(ct)).Should().Be(localBefore, "書いた行は残らない");
        await ShouldLeaveNoTransactionAsync(h, ct);
    }

    /// <summary>記録しない書き込みの間：読み取りは待たず、書き込みはコミットまで進まない</summary>
    private static async Task UntrackedWriteHoldsOffWritersAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedFolderAndSyncAsync(h, ct);
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = h.Rows.CreateDedicatedConnection();
        await connection.OpenAsync(ct);
        var untracked = SyncLocalTracking.WriteUntrackedAsync(
            connection,
            async (held, token) =>
            {
                await ExecuteOnAsync(held, InsertFolderSql(9), token);
                inside.SetResult();
                await release.Task.WaitAsync(token);
            },
            ct
        );

        Task? writer = null;

        try
        {
            await inside.Task.WaitAsync(ReachTimeout, ct);

            await using (var reader = OutsideConnection(h, timeoutSeconds: 1))
            {
                await reader.OpenAsync(ct);
                (await ScalarOnAsync(reader, ApplyingSql, ct))
                    .Should()
                    .Be("0", "別の接続からは、コミットの前の印（0）が待たずに読める");
                (await ScalarOnAsync(reader, CountFolderSql(9), ct))
                    .Should()
                    .Be("0", "別の接続からは、コミットの前に書いた行は見えない");
            }

            // 待ちの長い別の接続の書き込み（コミットまで進まないことは順序で見る。この間は、ロックが無ければ進む余地を作るだけ）
            writer = Task.Run(
                async () =>
                {
                    await using var other = OutsideConnection(h, timeoutSeconds: 30);
                    await other.OpenAsync(ct);
                    await ExecuteOnAsync(other, InsertFolderSql(10), ct);
                },
                ct
            );
            await Task.Delay(SecondRunWindow, ct);
            writer
                .IsCompleted.Should()
                .BeFalse("記録しない書き込みが終わるまで、別の接続の書き込みは進まない");
        }
        finally
        {
            release.TrySetResult();
        }

        await untracked;
        await writer.WaitAsync(ReachTimeout, ct);

        (await h.ScalarAsync(ApplyingSql, ct)).Should().Be("0");
        (await RecordedAsync(h, 9, ct)).Should().Be("0", "記録しない書き込みの行は記録されない");
        (await RecordedAsync(h, 10, ct))
            .Should()
            .Be("1", "コミットの後に通った別の接続の書き込みは記録される");
        var local = await h.ReadLocalAsync(ct);
        local.Folders.Keys.Should().Contain([9, 10]);
    }

    /// <summary>関数が生成の Repository（別の接続）で書く：ロック待ちで失敗し、全体が巻き戻る</summary>
    private static async Task UntrackedWriteThroughRepositoryAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedFolderAndSyncAsync(h, ct);
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));

        await using (var connection = h.Rows.CreateDedicatedConnection())
        {
            await connection.OpenAsync(ct);
            var thrown = await Record.ExceptionAsync(() =>
                SyncLocalTracking.WriteUntrackedAsync(
                    connection,
                    async (inside, token) =>
                    {
                        await ExecuteOnAsync(inside, InsertFolderSql(9), token);
                        await h.Folders.InsertAsync(
                            new ModelFolderEntity { FolderId = 10, Name = "via-repository" },
                            token
                        );
                    },
                    ct
                )
            );

            thrown
                .Should()
                .NotBeNull("別の接続の書き込みは、記録しない書き込みが持つロックを待って失敗する");
            Chain(thrown!)
                .OfType<SqliteException>()
                .Should()
                .Contain(
                    ex => ex.SqliteErrorCode == 5,
                    $"Default Timeout まで待って SQLITE_BUSY で失敗する（{thrown}）"
                );
            await ShouldBeOutsideTransactionAsync(
                connection,
                "失敗の後、渡した接続はトランザクションの外に戻る",
                ct
            );
        }

        (await h.ReadStateTablesAsync(ct)).Should().Equal(stateBefore);
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(localBefore, "渡した接続で書いた行も、Repository で書こうとした行も残らない");
        await ShouldLeaveNoTransactionAsync(h, ct);
    }

    /// <summary>トランザクションの中の接続と入れ子の呼び出しを断る</summary>
    private static async Task UntrackedWriteRefusesTransactionAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedFolderAndSyncAsync(h, ct);
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));

        // 利用者のトランザクションの中の接続
        await using (var connection = h.Rows.CreateDedicatedConnection())
        {
            await connection.OpenAsync(ct);
            await ExecuteOnAsync(connection, "BEGIN IMMEDIATE", ct);
            await ExecuteOnAsync(connection, InsertFolderSql(9), ct);
            var called = false;
            var thrown = await Record.ExceptionAsync(() =>
                SyncLocalTracking.WriteUntrackedAsync(
                    connection,
                    (_, _) =>
                    {
                        called = true;
                        return Task.CompletedTask;
                    },
                    ct
                )
            );

            thrown
                .Should()
                .BeOfType<InvalidOperationException>("トランザクションの中の接続は断る")
                .Which.InnerException.Should()
                .BeAssignableTo<System.Data.Common.DbException>();
            called.Should().BeFalse("断るときは関数を呼ばない");
            (await ScalarOnAsync(connection, CountFolderSql(9), ct))
                .Should()
                .Be("1", "利用者のトランザクションはそのまま続いている");
            await ExecuteOnAsync(connection, "ROLLBACK", ct);
        }

        (await h.ReadStateTablesAsync(ct)).Should().Equal(stateBefore);
        Describe(await h.ReadLocalAsync(ct)).Should().Be(localBefore);

        // 関数の中からの入れ子の呼び出し：断られ、外側は続けてコミットできる
        await using (var connection = h.Rows.CreateDedicatedConnection())
        {
            await connection.OpenAsync(ct);
            Exception? nested = null;
            await SyncLocalTracking.WriteUntrackedAsync(
                connection,
                async (inside, token) =>
                {
                    await ExecuteOnAsync(inside, InsertFolderSql(9), token);
                    nested = await Record.ExceptionAsync(() =>
                        SyncLocalTracking.WriteUntrackedAsync(
                            inside,
                            (_, _) => Task.CompletedTask,
                            token
                        )
                    );
                },
                ct
            );

            nested.Should().BeOfType<InvalidOperationException>("入れ子の呼び出しは断る");
        }

        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(stateBefore, "外側の書き込みは記録されず、印は 0 に戻る");
        (await h.ReadLocalAsync(ct)).Folders.Keys.Should().Contain(9, "外側はコミットされる");
    }

    /// <summary>開いていない接続・関数の中で閉じた接続・状態の表に行の無い DB を断る</summary>
    private static async Task UntrackedWriteRefusesUnusableAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedFolderAndSyncAsync(h, ct);
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var called = false;

        // 開いていない接続
        await using (var connection = h.Rows.CreateDedicatedConnection())
        {
            var thrown = await Record.ExceptionAsync(() =>
                SyncLocalTracking.WriteUntrackedAsync(
                    connection,
                    (_, _) =>
                    {
                        called = true;
                        return Task.CompletedTask;
                    },
                    ct
                )
            );
            thrown.Should().BeOfType<InvalidOperationException>("開いていない接続は断る");
        }

        // 関数の中で接続を閉じる（プールしない接続なので、閉じるとトランザクションも消える）
        await using (var connection = h.Rows.CreateDedicatedConnection())
        {
            await connection.OpenAsync(ct);
            var thrown = await Record.ExceptionAsync(() =>
                SyncLocalTracking.WriteUntrackedAsync(
                    connection,
                    async (inside, token) =>
                    {
                        await ExecuteOnAsync(inside, InsertFolderSql(9), token);
                        await inside.CloseAsync();
                    },
                    ct
                )
            );
            thrown
                .Should()
                .BeOfType<InvalidOperationException>("関数の中で閉じられた接続は名指しで断る")
                .Which.Message.Should()
                .Contain("closed the connection");
        }

        called.Should().BeFalse("断るときは関数を呼ばない");
        (await h.ReadStateTablesAsync(ct)).Should().Equal(stateBefore);
        Describe(await h.ReadLocalAsync(ct)).Should().Be(localBefore, "何もコミットしない");

        // 状態の表に行の無い DB（記録の仕組みが置かれていない）
        await using (var raw = OutsideConnection(h, timeoutSeconds: 1))
        {
            await raw.OpenAsync(ct);
            await ExecuteOnAsync(raw, $"DELETE FROM {SyncLocalTracking.StateTableName}", ct);
            var thrown = await Record.ExceptionAsync(() =>
                SyncLocalTracking.WriteUntrackedAsync(
                    raw,
                    (_, _) =>
                    {
                        called = true;
                        return Task.CompletedTask;
                    },
                    ct
                )
            );
            thrown
                .Should()
                .BeOfType<InvalidOperationException>("状態の表に行の無い DB は断る")
                .Which.Message.Should()
                .Contain("not set up");
            called.Should().BeFalse();
            await ShouldBeOutsideTransactionAsync(
                raw,
                "断った後、接続はトランザクションの外に戻る",
                ct
            );
        }

        Describe(await h.ReadLocalAsync(ct)).Should().Be(localBefore);
    }

    /// <summary>
    /// 関数がプールの接続を閉じる：トランザクションとロックがプールに残り、同じプールの次の接続を開くと確認が巻き戻す
    /// </summary>
    private static async Task UntrackedWriteClosedPooledConnectionAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedFolderAndSyncAsync(h, ct);
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));

        // 製品の接続ファクトリを開くたびの確認で包む（プールする）。接続文字列を端末のほかの接続と変えて、プールを分ける
        // （残ったトランザクションを持つ接続を、この行の次の接続だけが借りる）
        var factory = new Sqlite.ObservedSqlConnectionFactory(
            new Sqlite.SqlConnectionFactory(
                new SqliteConnectionStringBuilder(h.ConnectionString)
                {
                    DefaultTimeout = 2,
                }.ToString()
            ),
            new SyncLocalTrackingGuard(GeneratedSyncTables.All).OnStateChange
        );

        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync(ct);
            var thrown = await Record.ExceptionAsync(() =>
                SyncLocalTracking.WriteUntrackedAsync(
                    connection,
                    async (inside, token) =>
                    {
                        await ExecuteOnAsync(inside, InsertFolderSql(9), token);
                        await inside.CloseAsync();
                    },
                    ct
                )
            );
            thrown
                .Should()
                .BeOfType<InvalidOperationException>("関数の中で閉じられた接続は名指しで断る")
                .Which.Message.Should()
                .Contain("closed the connection");
        }

        // プールへ戻った接続がトランザクションと書き込みのロックを持ったまま（この形が実際に起きていることの確かめ）
        await using (var blocked = OutsideConnection(h, timeoutSeconds: 1))
        {
            await blocked.OpenAsync(ct);
            var busy = await Record.ExceptionAsync(() =>
                ExecuteOnAsync(blocked, InsertFolderSql(78), ct)
            );
            busy.Should().NotBeNull("プールに残ったトランザクションが書き込みのロックを持っている");
            Chain(busy!).OfType<SqliteException>().Should().Contain(ex => ex.SqliteErrorCode == 5);
        }

        // 同じプールの次の接続を開くと、開くたびの確認が残ったトランザクションを巻き戻す
        await using (var next = factory.CreateConnection())
        {
            await next.OpenAsync(ct);
            await ShouldBeOutsideTransactionAsync(
                next,
                "開くたびの確認が、プールに残ったトランザクションを巻き戻す",
                ct
            );
        }

        (await h.ReadStateTablesAsync(ct)).Should().Equal(stateBefore, "印も記録も残らない");
        Describe(await h.ReadLocalAsync(ct)).Should().Be(localBefore, "書いた行は残らない");
        await ShouldLeaveNoTransactionAsync(h, ct);
    }

    /// <summary>コミットされて残った印：記録しない書き込みは断らずに流れ、印を 0 に戻す</summary>
    private static async Task UntrackedWriteClearsLeftoverFlagAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedFolderAndSyncAsync(h, ct);

        // 包んでいない接続で印を 1 にしてコミットする（包んだ接続を開くと、開くたびの確認が戻してしまう）
        await h.ExecuteLocalAsync(
            $"UPDATE {SyncLocalTracking.StateTableName} SET applying = 1",
            ct
        );
        await h.ExecuteLocalAsync(InsertFolderSql(8), ct);
        (await RecordedAsync(h, 8, ct))
            .Should()
            .Be("0", "印が残っている間の書き込みは記録されない");

        await using (var raw = OutsideConnection(h, timeoutSeconds: 1))
        {
            await raw.OpenAsync(ct);
            await SyncLocalTracking.WriteUntrackedAsync(
                raw,
                (inside, token) => ExecuteOnAsync(inside, InsertFolderSql(9), token),
                ct
            );
        }

        (await h.ScalarAsync(ApplyingSql, ct)).Should().Be("0", "終わると印は 0 に戻る");
        (await RecordedAsync(h, 9, ct)).Should().Be("0");
        await h.ExecuteLocalAsync(InsertFolderSql(10), ct);
        (await RecordedAsync(h, 10, ct)).Should().Be("1", "その後の書き込みは記録される");
    }

    /// <summary>適用中の印を読む文</summary>
    private static readonly string ApplyingSql =
        $"SELECT applying FROM {SyncLocalTracking.StateTableName}";

    /// <summary>フォルダーを 1 行入れる文</summary>
    private static string InsertFolderSql(int key) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"INSERT INTO model_folders (folder_id, name) VALUES ({key}, 'folder-{key}')"
        );

    /// <summary>そのキーのフォルダーの行の数を読む文</summary>
    private static string CountFolderSql(int key) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"SELECT count(*) FROM model_folders WHERE folder_id = {key}"
        );

    /// <summary>そのキーのフォルダーが、行の状態の表に送る印つきで載っている数（"0" か "1"）</summary>
    private static Task<string?> RecordedAsync(
        SyncEngineFixedHarness h,
        int key,
        CancellationToken ct
    ) =>
        h.ScalarAsync(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} "
                    + $"WHERE table_name = '{ModelTables.Folders}' AND \"key\" = {key} AND dirty = 1"
            ),
            ct
        );

    /// <summary>サーバーにフォルダー 1 を入れて同期する</summary>
    private static async Task SeedFolderAndSyncAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.SyncAsync(null, ct);
    }

    /// <summary>端末の DB への、包んでいない・プールしない接続（開く前）</summary>
    private static SqliteConnection OutsideConnection(
        SyncEngineFixedHarness h,
        int timeoutSeconds
    ) =>
        new(
            new SqliteConnectionStringBuilder(h.ConnectionString)
            {
                Pooling = false,
                DefaultTimeout = timeoutSeconds,
            }.ToString()
        );

    /// <summary>渡された接続で 1 文を流す</summary>
    private static async Task ExecuteOnAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken ct
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>渡された接続で 1 つの値を読む（文字列へ）</summary>
    private static async Task<string?> ScalarOnAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken ct
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(
            await command.ExecuteScalarAsync(ct),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    /// <summary>接続がトランザクションの外にあることを表明する（中なら BEGIN IMMEDIATE が断られる）</summary>
    private static async Task ShouldBeOutsideTransactionAsync(
        System.Data.Common.DbConnection connection,
        string because,
        CancellationToken ct
    )
    {
        var thrown = await Record.ExceptionAsync(async () =>
        {
            await ExecuteOnAsync(connection, "BEGIN IMMEDIATE", ct);
            await ExecuteOnAsync(connection, "ROLLBACK", ct);
        });
        thrown.Should().BeNull(because);
    }

    /// <summary>
    /// 端末の DB にトランザクションも書き込みのロックも残っていないことを表明する：同じプールの次の接続がトランザクションの外に
    /// あり、プールしない別の接続（待ち 1 秒）の書き込みが通って記録される
    /// </summary>
    private static async Task ShouldLeaveNoTransactionAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await using (var pooled = h.Factory.CreateConnection())
        {
            await pooled.OpenAsync(ct);
            await ShouldBeOutsideTransactionAsync(
                pooled,
                "同じプールの次の接続にトランザクションが残らない",
                ct
            );
        }

        await using var other = OutsideConnection(h, timeoutSeconds: 1);
        await other.OpenAsync(ct);
        var thrown = await Record.ExceptionAsync(() =>
            ExecuteOnAsync(other, InsertFolderSql(77), ct)
        );
        thrown.Should().BeNull("書き込みのロックが残らず、別の接続が待たずに書ける");
        (await RecordedAsync(h, 77, ct)).Should().Be("1", "その書き込みは記録される");
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
    /// <summary>表の行だけ（blob を除く）を並べた文字列</summary>
    private static string DescribeRows(ModelDbState state) =>
        string.Join(
            "\n",
            ModelTables.All.SelectMany(table =>
                state
                    .Rows(table)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{table}/{pair.Key}={pair.Value}")
            )
        );

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
