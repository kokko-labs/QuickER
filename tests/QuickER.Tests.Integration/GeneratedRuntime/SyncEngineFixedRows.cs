using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport.Engines;
using Xunit;
using S = QuickER.Tests.GeneratedSyncFixture;
using Sqlite = QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;
using V = QuickER.Tests.GeneratedSyncVoFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>新しいエンジンの決まった形の行の 1 行の入力（端末とサーバー役を受けて、手と表明を流す）</summary>
/// <param name="Run">行の手と表明</param>
internal sealed record SyncEngineFixedCase(
    Func<SyncEngineFixedHarness, CancellationToken, Task> Run
);

/// <summary>
/// 新しいエンジンの決まった形の行の表（影響の地図の名前 <c>table:SyncEngineFixed</c>）。同期のモデルの決定的なシナリオでは
/// 書けない、新しいエンジン（<c>SyncEngine</c>）だけで確かめる形を置く：実行の入口（同じ DB の実行の直列化・保存フックの
/// 中の同期・空にした表・引数）と、行の状態の表の印が決める blob の運び方（主キーを変えた行・一意な値の回転）。
/// </summary>
/// <remarks>
/// 端末とサーバー役は <see cref="SyncEngineFixedHarness"/>（同期のモデルの図）。同期のモデルの図に無い形（NULL 許容の計算列・
/// NULL 許容の除外 blob・値オブジェクトのキーと版）の行は、行の中で SyncFixture・SyncVoFixture の端末
/// （<see cref="SyncFixtureEngineDevice"/>・<see cref="SyncVoFixtureEngineDevice"/>）とサーバー役
/// （<see cref="SyncFixtureTestServer"/>・<see cref="SyncVoFixtureTestServer"/>）を組む。
/// </remarks>
internal static partial class SyncEngineFixedRows
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
                "sync-fetch-failure-leaves-device-as-before",
                "1 回で全部の道の同期が、1 ページ目を取得した後の 2 ページ目の取得の失敗で終わると、端末の行・未送信・行の状態の表・"
                    + "再開点は同期の前のまま残る（取得した 1 ページ目の行も、サーバーで消えた行の削除伝搬も適用しない）。次の同期で"
                    + "サーバーの変更が降り、端末の未送信の編集はサーバーで変わった行なので ModifiedOnServer のまま残る",
                SyncFetchFailureAsync
            ),
            Row(
                "apply-refused-local-write-leaves-device-as-before-then-next-sync-settles",
                "1 回で全部の道の同期の適用の書き込みを端末のトリガーが断ると、同期は例外で終わり、適用は何も残さない（端末の行・"
                    + "行の状態の表・再開点は、先に通る送信の段が決着させた行を除いて同期の前のまま）。断りを外した次の同期で"
                    + "サーバーの変更が降りてそろう",
                ApplyRefusedLocalWriteSyncAsync
            ),
            Row(
                "refresh-apply-refused-local-write-leaves-device-as-before",
                "Force の洗い替えの適用の書き込みを端末のトリガーが断ると、洗い替えは例外で終わり、端末の行・未送信・行の状態の表・"
                    + "再開点は洗い替えの前のまま（捨てると決めた未送信も残る）。断りを外した次の同期で未送信の編集が上がり、"
                    + "サーバーの変更が降りてそろう",
                ApplyRefusedLocalWriteRefreshAsync
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
            .. StaleEntityRows(),
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
            Row(
                "chunk-failure-then-device-deletes-committed-row-next-sync-sends-delete",
                "塊の間で落ちた同期が端末に残した最初の塊の行を、端末で記録つきで消すと、次の同期がその削除をサーバーへ送り"
                    + "（サーバーから行が消える・競合なし）、その後の同期でも行は端末に戻らない",
                ChunkFailureThenDeviceDeletesCommittedRowAsync
            ),
            Row(
                "chunk-stale-row-then-fetch-failure-then-device-edit-collect-reports-conflict",
                "塊の経路で、取得の後・適用の前にサーバーで書き直された行が古い写しのまま塊で確定し、次の取得で同期が落ちた後に、"
                    + "端末がその行を編集して流す Collect の同期は ModifiedOnServer を報告し、サーバーの新しい内容を上書きしない"
                    + "（端末の編集は未送信のまま残る）",
                (h, ct) =>
                    ChunkStaleRowThenFetchFailureThenEditAsync(h, SyncConflictPolicy.Collect, ct)
            ),
            Row(
                "chunk-stale-row-then-fetch-failure-then-device-edit-serverwins-restores",
                "塊の経路で、取得の後・適用の前にサーバーで書き直された行が古い写しのまま塊で確定し、次の取得で同期が落ちた後に、"
                    + "端末がその行を編集して流す ServerWins の同期は編集を捨てて、端末の行をサーバーの新しい内容に戻す（サーバーは"
                    + "変わらない）",
                (h, ct) =>
                    ChunkStaleRowThenFetchFailureThenEditAsync(h, SyncConflictPolicy.ServerWins, ct)
            ),
            Row(
                "chunk-apply-refused-local-write-keeps-committed-chunks-and-next-sync-resumes",
                "塊ごとに確定する経路で、2 つ目の塊の適用の書き込みを端末のトリガーが断ると、同期は例外で終わり、先に確定した"
                    + "最初の塊の行だけが端末にあり、表の再開点はその塊の最後の行の版で止まる。断りを外した次の同期は続きだけを"
                    + "取得してそろう",
                ChunkApplyRefusedLocalWriteAsync
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
                "未送信の行を外す製品の条件（SyncEngine.NotUnsentCondition）の実行計画に、表の行ごとに流し直す副問い合わせ"
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
            Row(
                "refresh-unlisted-referring-table-refuses-while-present-locally",
                "記述子の LocalReferences に載らない外部キーで範囲の表を参照する範囲の外の表は、その表の行が 1 つも無くても端末に"
                    + "表が在るだけで洗い替えを断り（SyncRefreshScopeException・端末と行の状態の表は変わらない）、同じ参照が"
                    + "LocalReferences に載っていれば行を見て通り、一覧に載らない名前の表が端末に無ければ通る",
                RefreshUnlistedReferringTableAsync
            ),
            Row(
                "sync-fixture-blob-cleared-on-server-is-cleared-locally",
                "SyncFixture の端末で、blob を運ぶ同期が降ろした注文の blob を、サーバーで NULL にした（行の版も進む）後の blob を"
                    + "運ぶ同期は、端末の blob も NULL にする（古い中身が残らない）",
                SyncFixtureBlobClearedOnServerAsync
            ),
            Row(
                "sync-fixture-nullable-computed-column-downloads-and-stays-null",
                "SyncFixture の端末で、サーバーで値を持つ NULL 許容の計算列（sync_orders.summary）のある行を降ろせ（注文と明細の"
                    + "2 行・競合なし）、端末の計算列は NULL のまま。続く同期は何も降ろさない",
                SyncFixtureNullableComputedColumnAsync
            ),
            Row(
                "sync-vo-new-row-with-excluded-not-null-blob-downloads",
                "SyncVoFixture の端末で、NOT NULL の除外 blob（photo）を持つサーバーの新しい行を blob を運ばない同期が降ろし、"
                    + "端末の blob は空の値で入る",
                SyncVoNewRowWithExcludedNotNullBlobAsync
            ),
            Row(
                "sync-vo-local-insert-without-version-uploads",
                "SyncVoFixture の端末で、版の列が空の端末の新しい行（値オブジェクトのキーと版）が同期で上がり（競合なし）、"
                    + "未送信が残らない",
                SyncVoLocalInsertUploadsAsync
            ),
            Row(
                "sync-vo-server-version-written-back-through-value-object",
                "SyncVoFixture の端末で、上げた行にサーバーが採番した版が値オブジェクトの版の列へ書き戻り、版つきの更新も上がり、"
                    + "その後の同期は何も送らず何も降ろさない（往復が定常になる）",
                SyncVoServerVersionWrittenBackAsync
            ),
            Row(
                "sync-vo-serverwins-restores-row-with-server-version",
                "SyncVoFixture の端末で、サーバーの他者と端末が同じ行を編集した後の ServerWins の同期は、端末の行をサーバーの"
                    + "内容に戻し、値オブジェクトの版の列もサーバーのその行の版にする。続く同期は何も送らず何も降ろさない",
                SyncVoServerWinsRestoresWithServerVersionAsync
            ),
            Row(
                "stateless-row-update-before-first-sync-is-reported-as-duplicate-on-server",
                "記録の仕組みを通らずに入った行（端末とサーバーに同じ内容・行の状態も再開点も無い）を、最初の同期の前に "
                    + "Repository で更新すると、Collect の同期はその行を DuplicateOnServer で報告し、端末の内容のまま"
                    + "サーバーが知らない未送信として残す。もう一度の Collect の同期も同じに報告する",
                StatelessRowUpdateBeforeFirstSyncAsync
            ),
            Row(
                "stateless-row-delete-before-first-sync-comes-back",
                "記録の仕組みを通らずに入った行を、最初の同期の前に Repository で削除すると、同期は削除を送らず競合も報告せず、"
                    + "サーバーの行は残り、端末に行が戻る",
                StatelessRowDeleteBeforeFirstSyncAsync
            ),
            .. new[] { "sync", "refresh" }.Select(first =>
                Row(
                    $"stateless-rows-take-state-from-first-sync-then-edit-uploads-{first}",
                    $"記録の仕組みを通らずに入った行は、書く前に流した最初の実行（{first}）で何も送らず端末の内容も変わらずに"
                        + "行の状態を得て、その後に Repository で更新した同期は競合なしにサーバーへ上がる",
                    (h, ct) => StatelessRowsTakeStateFromFirstRunAsync(h, first, ct)
                )
            ),
            Row(
                "download-does-not-run-local-save-hooks",
                "端末のフォルダーのリポジトリに行を飛ばす Before の Save フックがあっても（そのリポジトリのグラフ保存では呼ばれて"
                    + "行を飛ばす）、同期が降ろすサーバーの新しい行と更新はフックを通らずに端末へ入る",
                DownloadDoesNotRunLocalSaveHooksAsync
            ),
            .. NonAdvancingSourceRows(),
            .. PendingRows(),
            .. RunConnectionRows(),
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

    /// <summary>
    /// 進まない差分ソースの行（同期・洗い替え × 版ありの表＝品目・版なしの表＝ラベル）。差分ソースが「まだ続きがある」と答え
    /// 続けて同じページを返すと、実行は止まらずに回り続けずに、表の名前を名指しした例外で終わり、何も適用しない
    /// </summary>
    private static IEnumerable<ScenarioRow<SyncEngineFixedCase>> NonAdvancingSourceRows() =>
        from refresh in new[] { false, true }
        from versioned in new[] { true, false }
        select Row(
            $"non-advancing-source-{(refresh ? "refresh" : "sync")}-{(versioned ? "versioned" : "versionless")}"
                + "-fails-instead-of-looping",
            (refresh ? "洗い替え" : "同期")
                + "で、"
                + (
                    versioned
                        ? "版ありの表（品目）の差分ソースが、再開点を進めない同じバッチを"
                        : "版なしの表（ラベル）の差分ソースが、最後のキーを進めない同じページを"
                )
                + "「まだ続きがある」と返し続けると、実行は回り続けずに表の名前と not making progress を含む "
                + "InvalidOperationException で終わり、端末の行・行の状態の表・再開点の表とサーバーの行は実行の前のまま",
            (h, ct) => NonAdvancingSourceAsync(h, refresh, versioned, ct)
        );

    /// <summary>
    /// 進まない差分ソース：端末とサーバーにフォルダー 1・品目 1 と 2・ラベル l1 をそろえた後、サーバーの他者がフォルダー 1・
    /// 品目 1・ラベル l1 を書き換える（先に取得するフォルダーの表の変更が、途中まで適用されていないことを見る）。対象の表の
    /// 差分ソースだけを進まない包みにしたエンジンで実行を流す
    /// </summary>
    private static async Task NonAdvancingSourceAsync(
        SyncEngineFixedHarness h,
        bool refresh,
        bool versioned,
        CancellationToken ct
    )
    {
        var lastWriteWins = new SyncOptions { Mode = SyncMode.LastWriteWins };
        await SeedItemsAsync(h, ct);
        await h.Server.Labels.InsertAsync(
            new ModelLabelEntity
            {
                LabelCode = "l1",
                FolderId = 1,
                Caption = "c1",
            },
            ct
        );
        await h.SyncAsync(lastWriteWins, ct);

        var folder = (await h.Server.Folders.GetByIdAsync(1, ct))!;
        folder.Name = "f1-server";
        (await h.Server.Folders.UpdateAsync(folder, cancellationToken: ct)).Should().BeTrue();
        var item = (await h.Server.Items.GetByIdAsync(1, ct))!;
        item.Title = "server-edit";
        (await h.Server.Items.UpdateAsync(item, cancellationToken: ct)).Should().BeTrue();
        var label = (await h.Server.Labels.GetByIdAsync("l1", ct))!;
        label.Caption = "c1-server";
        (await h.Server.Labels.UpdateAsync(label, cancellationToken: ct)).Should().BeTrue();

        var table = versioned ? ModelTables.Items : ModelTables.Labels;
        var nonAdvancing = new NonAdvancingSource(table);
        var engine = h.CreateEngine(observe: null, nonAdvancing: nonAdvancing);
        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var serverBefore = Describe(await h.ReadServerAsync(ct));

        // 版なしの表は後勝ちのモードでだけ取得する。洗い替えは blob を運ぶ（端末の品目が blob を持つので、捨てる洗い替えは
        // 取得の前に blob の損失で断る）
        var thrown = refresh
            ? await Record.ExceptionAsync(() =>
                h.RefreshAsync(new SyncRefreshOptions { IncludeUnboundedBinary = true }, ct, engine)
            )
            : await Record.ExceptionAsync(() =>
                h.SyncAsync(versioned ? null : lastWriteWins, ct, engine)
            );

        thrown
            .Should()
            .BeOfType<InvalidOperationException>(
                "進まない差分ソースは、回り続けずに（取得の回数の上限の例外にならずに）進まないことの例外で終わる"
            );
        thrown!.Message.Should().Contain(table).And.Contain("not making progress");
        nonAdvancing
            .Calls.Should()
            .BeGreaterThanOrEqualTo(
                2,
                "進まない包みが 2 回目の取得まで呼ばれている（空振りでない）"
            );
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(localBefore, "端末の業務の表の行は実行の前のまま（先に取得した表も適用されない）");
        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(stateBefore, "行の状態の表と再開点の表は実行の前のまま");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "サーバーの行は変わらない");
    }

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

        var refreshed = await h.RefreshAsync(
            new SyncRefreshOptions { IncludeUnboundedBinary = true, Force = true },
            ct,
            engine
        );

        deleted.Should().BeTrue("品目の表の行の取得の前に割り込んだ");
        refreshed
            .DiscardedChanges.Should()
            .Be(1, "最後の段が変更だけを捨てた 11 も、捨てた行に数える");
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
        var routes = new List<SyncDownloadRoute>();
        var engine = SmallChunks(h.CreateEngine());
        engine.DownloadRouteObserved = routes.Add;

        var result = await h.RefreshAsync(ForceDiscardBlobs, ct, engine);

        routes.Should().Equal([SyncDownloadRoute.Chunked], "洗い替えは塊の道を通る");
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
        var routes = new List<SyncDownloadRoute>();
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
        routes.Should().Equal([SyncDownloadRoute.Chunked], "洗い替えは塊の道を通る");
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
        var routes = new List<SyncDownloadRoute>();
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
        routes.Should().OnlyContain(route => route == SyncDownloadRoute.Single);
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
            $"UPDATE \"model_items\" SET \"title\" = \"title\" WHERE {SyncEngine.NotUnsentCondition(key)}",
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
    private static SyncEngine SmallChunks(SyncEngine engine)
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
            SyncDownloadRoute Route
        )[] cells =
        [
            ("below-threshold", 2, 100, 2, false, SyncDownloadRoute.Single),
            ("below-threshold-unsent", 2, 100, 2, true, SyncDownloadRoute.Single),
            ("over-threshold-within-limit", 2, 100, 3, false, SyncDownloadRoute.Chunked),
            ("over-threshold-within-limit-unsent", 2, 100, 3, true, SyncDownloadRoute.Single),
            ("over-limit", 2, 3, 4, false, SyncDownloadRoute.Chunked),
            ("over-limit-unsent", 2, 3, 4, true, SyncDownloadRoute.Refused),
            ("limit-below-threshold-over-limit", 10, 2, 3, false, SyncDownloadRoute.Chunked),
            ("limit-below-threshold-over-limit-unsent", 10, 2, 3, true, SyncDownloadRoute.Refused),
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

            var routes = new List<SyncDownloadRoute>();
            var engine = cell.CreateEngine();
            engine.ChunkThreshold = threshold;
            engine.ChunkSize = SyncModelEngineDevice.SmallChunkSize;
            engine.DownloadRouteObserved = routes.Add;

            var thrown = await Record.ExceptionAsync(() =>
                engine.DownloadAsync(new SyncOptions { DownloadRowLimit = limit }, ct)
            );

            routes.Should().Equal([route], $"枠 {name} の取得は 1 回で、道は {route}");

            if (route == SyncDownloadRoute.Refused)
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
        (string Name, int Fetched, bool ForceDiscard, SyncDownloadRoute Route)[] refreshCells =
        [
            ("refresh-over-threshold", 3, false, SyncDownloadRoute.Chunked),
            ("refresh-force-discards-only", 3, true, SyncDownloadRoute.Chunked),
            ("refresh-below-threshold", 2, false, SyncDownloadRoute.Single),
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

            var routes = new List<SyncDownloadRoute>();
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

    /// <summary>端末の書き込みを断るテストのトリガーの名前（同期のトリガー quicker_sync_trg_* とは別）</summary>
    private const string RefuseWriteTrigger = "test_refuse_apply_write";

    /// <summary>端末の書き込みを断るテストのトリガーが投げる文</summary>
    private const string RefusedWriteMessage = "refused by test trigger";

    /// <summary>端末のフォルダーの表へ、指定したキーの行の挿入を断るトリガーを張る</summary>
    private static Task RefuseLocalFolderInsertAsync(
        SyncEngineFixedHarness h,
        int folderId,
        CancellationToken ct
    ) =>
        h.ExecuteLocalAsync(
            $"CREATE TRIGGER {RefuseWriteTrigger} BEFORE INSERT ON model_folders "
                + $"WHEN NEW.folder_id = {folderId} "
                + $"BEGIN SELECT RAISE(ABORT, '{RefusedWriteMessage}'); END",
            ct
        );

    /// <summary>端末の書き込みを断るテストのトリガーを外す</summary>
    private static Task AllowLocalFolderInsertAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    ) => h.ExecuteLocalAsync($"DROP TRIGGER {RefuseWriteTrigger}", ct);

    /// <summary>端末の未送信の行の数</summary>
    private static Task<string?> DirtyCountAsync(SyncEngineFixedHarness h, CancellationToken ct) =>
        h.ScalarAsync($"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1", ct);

    /// <summary>
    /// 例外が端末のトリガーの断りであること（エンジンが包まずに出す SqliteException と、トリガーが投げた文）を表明する
    /// </summary>
    private static void ShouldBeRefusedWrite(Exception? thrown, string because)
    {
        var refused = thrown.Should().BeOfType<SqliteException>(because).Which;
        refused.SqliteErrorCode.Should().Be(19, because);
        refused.Message.Should().Be($"SQLite Error 19: '{RefusedWriteMessage}'.", because);
    }

    /// <summary>
    /// 塊の間で落ちた同期が残した行の削除：フォルダー 1〜5 を取得の大きさ 3・閾値 2・塊 2 で降ろし、塊 [1, 2] を確定してから
    /// 2 ページ目の取得で失敗させる。端末で 2 を記録つきで消すと、次の同期がその削除を送り、サーバーから 2 が消える
    /// </summary>
    /// <remarks>
    /// 塊で確定した行の状態（サーバーが知っている行）が書かれていないと、端末の削除はサーバーに届いていない行の削除として
    /// 送られずに片付き、サーバーに 2 が残る（再開点は 2 の先へ進んでいるので、2 は端末へも戻らない）。
    /// </remarks>
    private static async Task ChunkFailureThenDeviceDeletesCommittedRowAsync(
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
        seen.Should().ContainSingle();
        seen[0]
            .Keys.Should()
            .Equal([1, 2], "失敗の時点で、最初の塊（1・2）だけが端末に書かれている");

        await h.ExecuteLocalAsync("DELETE FROM model_folders WHERE folder_id = 2", ct);
        (await DirtyCountAsync(h, ct)).Should().Be("1", "端末の削除は未送信として記録される");
        (await h.ReadServerAsync(ct))
            .Folders.Should()
            .ContainKey(2, "削除を送る前のサーバーに 2 がある");

        var result = await h.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1, "端末の削除を送る");
        (await h.ReadServerAsync(ct)).Folders.Should().NotContainKey(2, "削除がサーバーへ届く");
        (await h.ReadLocalAsync(ct)).Folders.Should().NotContainKey(2);
        (await DirtyCountAsync(h, ct)).Should().Be("0");

        var again = await h.SyncAsync(null, ct);

        again.Uploaded.Should().Be(0);
        again.Downloaded.Should().Be(0);
        var local = await h.ReadLocalAsync(ct);
        local.Folders.Should().NotContainKey(2, "その後の同期でも 2 は端末に戻らない");
        local.Folders.Keys.Order().Should().Equal([1, 3, 4, 5]);
        Describe(local)
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
    }

    /// <summary>
    /// 塊で確定した古い写しの行を、取得の失敗の後に端末が編集する：端末とサーバーにフォルダー 1〜5 があり、サーバーの他者が
    /// 1〜5 を s1〜s5 へ書き直す。取得の大きさ 3・閾値 2・塊 2 の同期の 1 ページ目の取得の後（適用の前）に、サーバーの他者が 1 を
    /// もう一度 s1-newer へ書き直し、塊 [1, 2] が 1 を古い写し s1 で確定してから、2 ページ目の取得で失敗させる。端末が 1 を
    /// 編集して流す同期は、サーバーの新しい内容を競合なしに上書きしない
    /// </summary>
    private static async Task ChunkStaleRowThenFetchFailureThenEditAsync(
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

        var returned = 0;
        var before = 0;
        var nameAtFailure = new List<string>();
        var engine = SmallChunks(
            h.CreateEngine(async call =>
            {
                if (call.Table != ModelTables.Folders)
                {
                    return;
                }

                if (call.Call == SyncEngineSourceCall.RowsReturned && ++returned == 1)
                {
                    await RenameServerFolderAsync(h, 1, "s1-newer", ct);
                }

                if (call.Call == SyncEngineSourceCall.RowsBefore && ++before == 2)
                {
                    nameAtFailure.Add((await h.ReadLocalAsync(ct)).Folders[1].Name);

                    throw new InvalidOperationException(InjectedFetchFailure);
                }
            })
        );

        var thrown = await Record.ExceptionAsync(() =>
            h.SyncAsync(new SyncOptions { DownloadBatchSize = 3 }, ct, engine)
        );

        thrown
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Be(InjectedFetchFailure);
        nameAtFailure
            .Should()
            .Equal(["s1"], "失敗の時点で、1 は取得の後に書き直される前の古い写しで確定している");
        (await h.ReadServerAsync(ct)).Folders[1].Name.Should().Be("s1-newer");

        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-1' WHERE folder_id = 1",
            ct
        );

        var result = await h.SyncAsync(new SyncOptions { ConflictPolicy = policy }, ct);

        (await h.ReadServerAsync(ct))
            .Folders[1]
            .Name.Should()
            .Be("s1-newer", "端末の編集はサーバーの新しい内容を上書きしない");
        var local = await h.ReadLocalAsync(ct);

        if (policy == SyncConflictPolicy.Collect)
        {
            result
                .Conflicts.Select(conflict =>
                    (conflict.TableName, conflict.KeyText, conflict.Reason)
                )
                .Should()
                .Equal([(ModelTables.Folders, "1", SyncConflictReason.ModifiedOnServer)]);
            result.Uploaded.Should().Be(0);
            local.Folders[1].Name.Should().Be("local-1", "Collect は端末の編集を残す");
            (await DirtyCountAsync(h, ct)).Should().Be("1", "端末の編集は未送信のまま残る");
        }
        else
        {
            result.Conflicts.Should().BeEmpty();
            result.Uploaded.Should().Be(0);
            local.Folders[1].Name.Should().Be("s1-newer", "ServerWins は端末の編集を捨てて戻す");
            (await DirtyCountAsync(h, ct)).Should().Be("0");
            Describe(local)
                .Should()
                .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
        }

        local
            .Folders.Where(pair => pair.Key >= 2)
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.Name)
            .Should()
            .Equal(["s2", "s3", "s4", "s5"], "ほかの行は降りている");
    }

    /// <summary>
    /// 塊の経路の適用で端末の書き込みが断られる：フォルダー 1〜5 を取得の大きさ 3・閾値 2・塊 2 で降ろし、端末の 4 の挿入を
    /// トリガーで断る。塊 [1, 2] は確定し、4 を含む塊の書き込みで同期が例外で終わる
    /// </summary>
    private static async Task ChunkApplyRefusedLocalWriteAsync(
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
        await RefuseLocalFolderInsertAsync(h, 4, ct);
        var options = new SyncOptions { DownloadBatchSize = 3 };

        var thrown = await Record.ExceptionAsync(() =>
            h.SyncAsync(options, ct, SmallChunks(h.CreateEngine()))
        );

        ShouldBeRefusedWrite(thrown, "4 を含む塊の書き込みが断られる");
        (await h.ReadLocalAsync(ct))
            .Folders.Keys.Order()
            .Should()
            .Equal([1, 2], "先に確定した最初の塊（1・2）だけが端末にある");
        (await LocalAnchorAsync(h, ModelTables.Folders, ct))
            .Should()
            .Be(
                await ServerFolderVersionAsync(h, 2, ct),
                "表の再開点は確定した最初の塊の最後の行（2）の版で止まる"
            );
        (await DirtyCountAsync(h, ct)).Should().Be("0", "エンジンの書き込みは未送信を作らない");

        await AllowLocalFolderInsertAsync(h, ct);
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
        result.Conflicts.Should().BeEmpty();
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
    }

    /// <summary>
    /// 1 回で全部の道の同期の適用で端末の書き込みが断られる：端末とサーバーにフォルダー 1〜3 があり、端末は 2 を編集する
    /// （未送信）。サーバーの他者が 1 を書き直してから新しい 6 を入れ、端末の 6 の挿入をトリガーで断る。送信の段は 2 を送って
    /// 決着させ、適用（1 の更新の後に 6 の挿入）は断られて何も残さない
    /// </summary>
    private static async Task ApplyRefusedLocalWriteSyncAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"), (2, null, "f2"), (3, null, "f3"));
        await h.SyncAsync(null, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-2' WHERE folder_id = 2",
            ct
        );
        await RenameServerFolderAsync(h, 1, "s1", ct);
        await InsertServerFoldersAsync(h, ct, (6, null, "new-6"));
        await RefuseLocalFolderInsertAsync(h, 6, ct);

        const string sentRow = "quicker_sync_row|model_folders|2|";
        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var stateBefore = await h.ReadStateTablesAsync(ct);
        (await DirtyCountAsync(h, ct)).Should().Be("1", "同期の前に 2 が未送信");

        var thrown = await Record.ExceptionAsync(() => h.SyncAsync(null, ct));

        ShouldBeRefusedWrite(thrown, "6 の挿入が断られる");
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(localBefore, "適用は何も残さない（1 は書き直される前・6 は無い）");
        (await h.ReadStateTablesAsync(ct))
            .Where(line => !line.StartsWith(sentRow, StringComparison.Ordinal))
            .Should()
            .Equal(
                stateBefore.Where(line => !line.StartsWith(sentRow, StringComparison.Ordinal)),
                "送信の段が決着させた 2 の行の状態のほかは、行の状態の表・再開点は同期の前のまま"
            );
        (await DirtyCountAsync(h, ct))
            .Should()
            .Be("0", "送信の段は適用より先に 2 を送って決着させる");
        var server = await h.ReadServerAsync(ct);
        server.Folders[2].Name.Should().Be("local-2", "送信の段は通っている");

        await AllowLocalFolderInsertAsync(h, ct);
        var result = await h.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty();
        result.Truncations.Should().BeEmpty();
        result.Uploaded.Should().Be(0, "2 は前の同期で送ってある");
        var local = await h.ReadLocalAsync(ct);
        local.Folders[1].Name.Should().Be("s1");
        local.Folders[6].Name.Should().Be("new-6");
        Describe(local)
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
    }

    /// <summary>
    /// Force の洗い替えの適用で端末の書き込みが断られる：端末とサーバーにフォルダー 1〜3 があり、端末は 2 を編集する（未送信）。
    /// サーバーの他者が 1 を書き直してから新しい 6 を入れ、端末の 6 の挿入をトリガーで断る。洗い替えは送らず、適用が断られて
    /// 何も残さない
    /// </summary>
    private static async Task ApplyRefusedLocalWriteRefreshAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"), (2, null, "f2"), (3, null, "f3"));
        await h.SyncAsync(null, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-2' WHERE folder_id = 2",
            ct
        );
        await RenameServerFolderAsync(h, 1, "s1", ct);
        await InsertServerFoldersAsync(h, ct, (6, null, "new-6"));
        await RefuseLocalFolderInsertAsync(h, 6, ct);

        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var serverBefore = Describe(await h.ReadServerAsync(ct));

        var thrown = await Record.ExceptionAsync(() =>
            h.RefreshAsync(
                new SyncRefreshOptions { Force = true, IncludeUnboundedBinary = true },
                ct
            )
        );

        ShouldBeRefusedWrite(thrown, "6 の挿入が断られる");
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(
                localBefore,
                "端末の行は洗い替えの前のまま（1 は書き直される前・2 は端末の編集・6 は無い）"
            );
        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(
                stateBefore,
                "未送信・行の状態の表・再開点は洗い替えの前のまま（Force でも捨てていない）"
            );
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "洗い替えは送らない");

        await AllowLocalFolderInsertAsync(h, ct);
        var result = await h.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty();
        result.Truncations.Should().BeEmpty();
        result.Uploaded.Should().Be(1, "残っていた 2 の編集が上がる");
        var local = await h.ReadLocalAsync(ct);
        local.Folders[1].Name.Should().Be("s1");
        local.Folders[2].Name.Should().Be("local-2");
        local.Folders[6].Name.Should().Be("new-6");
        Describe(local)
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), "端末とサーバーの内容が同じ");
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
    /// 1 回で全部の道の同期の取得の失敗は何も変えない：端末とサーバーにフォルダー 1〜5 があり、端末は 4 を編集する（未送信）。
    /// サーバーの他者が 1〜4 を書き直し、5 を消し、6 を入れる。取得の大きさ 2 の同期は、送信の段で 4 を ModifiedOnServer として
    /// 残し（サーバーへ書かない）、フォルダーの 1 ページ目を取得した後、2 ページ目の取得で失敗する
    /// </summary>
    /// <remarks>
    /// 新しいエンジンの 1 回で全部の道は全部の表を取得してから 1 つのトランザクションで適用するので、取得の失敗では取得した
    /// ページの行も削除伝搬も適用しない。同期のモデルの割り込みの網が、次の取得の失敗 × 前段 parent-after-child-* × 編集してから
    /// 同期の組を起こり得ないとして外すのは、この仕様に依る（<c>SyncModelScenarios.IsPossibleServerInterrupt</c>）。
    /// </remarks>
    private static async Task SyncFetchFailureAsync(SyncEngineFixedHarness h, CancellationToken ct)
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
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-4' WHERE folder_id = 4",
            ct
        );

        for (var id = 1; id <= 4; id++)
        {
            await RenameServerFolderAsync(h, id, $"s{id}", ct);
        }

        (await h.Server.Folders.DeleteAsync(5, ct)).Should().BeTrue();
        await InsertServerFoldersAsync(h, ct, (6, null, "new-6"));

        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var serverBefore = Describe(await h.ReadServerAsync(ct));
        (await DirtyCountAsync(h, ct)).Should().Be("1", "同期の前に 4 が未送信");

        var returned = 0;
        var before = 0;
        var engine = h.CreateEngine(call =>
        {
            if (call.Table != ModelTables.Folders)
            {
                return Task.CompletedTask;
            }

            if (call.Call == SyncEngineSourceCall.RowsReturned)
            {
                returned += call.Rows!.Value;
            }

            return call.Call == SyncEngineSourceCall.RowsBefore && ++before == 2
                ? throw new InvalidOperationException(InjectedFetchFailure)
                : Task.CompletedTask;
        });

        var thrown = await Record.ExceptionAsync(() =>
            h.SyncAsync(new SyncOptions { DownloadBatchSize = 2 }, ct, engine)
        );

        thrown
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Be(InjectedFetchFailure);
        returned
            .Should()
            .Be(2, "失敗の前に、フォルダーの 1 ページ目（2 行）を取得している（前提）");
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(
                localBefore,
                "端末の行は同期の前のまま（1〜3 は書き直される前・4 は端末の編集・5 は在る・6 は無い）"
            );
        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(stateBefore, "未送信・行の状態の表・再開点は同期の前のまま");
        Describe(await h.ReadServerAsync(ct))
            .Should()
            .Be(serverBefore, "送信の段は 4 をサーバーへ書かない（前提）");

        var result = await h.SyncAsync(null, ct);

        result
            .Conflicts.Select(conflict => (conflict.TableName, conflict.KeyText, conflict.Reason))
            .Should()
            .Equal([(ModelTables.Folders, "4", SyncConflictReason.ModifiedOnServer)]);
        result.Uploaded.Should().Be(0);
        var local = await h.ReadLocalAsync(ct);
        local
            .Folders.OrderBy(pair => pair.Key)
            .Select(pair => (pair.Key, pair.Value.Name))
            .Should()
            .Equal(
                [(1, "s1"), (2, "s2"), (3, "s3"), (4, "local-4"), (6, "new-6")],
                "次の同期でサーバーの変更が降り、4 は端末の編集のまま残る"
            );
        (await DirtyCountAsync(h, ct)).Should().Be("1", "端末の編集は未送信のまま残る");
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
            ModelActor.Device1,
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

        // 最初のページ（1 行）を取得した後・適用の前に、端末で記録つきの書き込みを 1 つ流す（書くのは端末の DB）
        var wrote = false;
        h.Faults.ArmInterrupt(
            ModelActor.Device1,
            ModelInterruptPoint.AfterServerFetchBeforeApply,
            ModelActor.Device1,
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

    /// <summary>
    /// 一覧に載らない参照：ラベルの記述子を写し、品目の表からの参照を <c>LocalReferences</c> から
    /// <c>UnlistedReferencingTableNames</c> へ移したもので組んだエンジンの洗い替え（範囲はフォルダーとラベル）を、品目の行が
    /// 無い端末で断る。対照として、元の記述子（参照が一覧に載る）のエンジンと、端末に無い表の名前を一覧に載せないエンジンは通る
    /// </summary>
    /// <remarks>
    /// 同期のモデルの図にはそうした外部キーが無いので、記述子を写して作る（記述子の init のプロパティを反射で書き換える）
    /// </remarks>
    private static async Task RefreshUnlistedReferringTableAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.Server.Labels.InsertAsync(
            new ModelLabelEntity
            {
                LabelCode = "l1",
                FolderId = 1,
                Caption = "c1",
            },
            ct
        );
        await h.SyncAsync(null, ct);

        SyncEngine EngineWithLabels(SyncTableDescriptor<ModelLabelEntity, string> labels) =>
            new([
                new SyncTable<ModelFolderEntity, int>(
                    GeneratedSyncTables.ModelFolder,
                    h.Server.FolderSource,
                    h.Rows
                ),
                new SyncTable<ModelLabelEntity, string>(labels, h.Server.LabelSource, h.Rows),
                new SyncTable<ModelItemEntity, int>(
                    GeneratedSyncTables.ModelItem,
                    h.Server.ItemSource,
                    h.Rows
                ),
                new SyncTable<ModelEventEntity, DateTime>(
                    GeneratedSyncTables.ModelEvent,
                    h.Server.EventSource,
                    h.Rows
                ),
            ]);

        var outsideItems = new SyncRefreshOptions
        {
            ExcludedEntityTypes = [typeof(ModelItemEntity), typeof(ModelEventEntity)],
        };

        // 品目の行は無い（行を見れば通る）が、一覧に載らない参照は照会できないので表が在るだけで断る
        (await h.ScalarAsync($"SELECT count(*) FROM {ModelTables.Items}", ct))
            .Should()
            .Be("0");
        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var unlisted = EngineWithLabels(LabelsReferredOutsideTheList([ModelTables.Items]));
        var thrown = await Record.ExceptionAsync(() => h.RefreshAsync(outsideItems, ct, unlisted));
        thrown
            .Should()
            .BeOfType<SyncRefreshScopeException>()
            .Which.References.Should()
            .Equal(new SyncRefreshScopeReference(ModelTables.Items, ModelTables.Labels));
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(localBefore, "断った洗い替えは端末を変えない");
        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(stateBefore, "断った洗い替えは行の状態の表を変えない");

        // 参照が一覧に載る元の記述子なら、品目の行を照会して通る
        (await h.RefreshAsync(outsideItems, ct))
            .Tables.Select(table => table.TableName)
            .Should()
            .Equal(ModelTables.Folders, ModelTables.Labels);

        // 一覧に載らない参照でも、その名前の表が端末に無ければ通る
        var absent = EngineWithLabels(LabelsReferredOutsideTheList(["model_absent"]));
        (await h.RefreshAsync(outsideItems, ct, absent))
            .Tables.Select(table => table.TableName)
            .Should()
            .Equal(ModelTables.Folders, ModelTables.Labels);
        (await h.ReadLocalAsync(ct)).Labels.Keys.Should().Equal("l1");
    }

    /// <summary>
    /// ラベルの記述子の写しで、品目の表からの参照を <c>LocalReferences</c> から外し、<c>UnlistedReferencingTableNames</c> を
    /// <paramref name="unlisted"/> にしたもの
    /// </summary>
    private static SyncTableDescriptor<ModelLabelEntity, string> LabelsReferredOutsideTheList(
        IReadOnlyList<string> unlisted
    )
    {
        var copy =
            (SyncTableDescriptor<ModelLabelEntity, string>)
                typeof(object)
                    .GetMethod(
                        "MemberwiseClone",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )!
                    .Invoke(GeneratedSyncTables.ModelLabel, null)!;
        var type = typeof(SyncTableDescriptor<ModelLabelEntity, string>);
        type.GetProperty(nameof(copy.LocalReferences))!
            .SetValue(
                copy,
                copy.LocalReferences.Where(reference => reference.TableName != ModelTables.Items)
                    .ToList()
            );
        type.GetProperty(nameof(copy.UnlistedReferencingTableNames))!
            .SetValue(copy, unlisted.ToList());

        return copy;
    }

    /// <summary>SyncFixture の端末：サーバーで NULL にした blob は端末でも NULL になる</summary>
    private static async Task SyncFixtureBlobClearedOnServerAsync(
        SyncEngineFixedHarness _,
        CancellationToken ct
    )
    {
        using var server = await SyncFixtureTestServer.CreateAsync(ct);
        using var device = await SyncFixtureEngineDevice.CreateAsync(server.Sources, ct);
        var blobs = new S.SyncOptions { IncludeUnboundedBinary = true };
        await server.Orders.InsertAsync(
            new S.SyncOrderEntity { OrderId = 1, CustomerName = "alice" },
            ct
        );

        using (var content = new MemoryStream([1, 2, 3]))
        {
            (await server.OrderBlobs.WriteUnboundedBinaryAsync("Attachment", 1, content, 3, ct))
                .Should()
                .BeTrue();
        }

        (await device.Engine.SyncAsync(blobs, ct)).Conflicts.Should().BeEmpty();
        (await ReadOrderBlobAsync(device, 1, ct)).Should().Equal([1, 2, 3]);

        // サーバーで列を NULL にする（行の版も進む）
        (await server.OrderBlobs.WriteUnboundedBinaryAsync("Attachment", 1, null, null, ct))
            .Should()
            .BeTrue();

        var cleared = await device.Engine.SyncAsync(blobs, ct);

        cleared.Conflicts.Should().BeEmpty();
        (await ReadOrderBlobAsync(device, 1, ct))
            .Should()
            .BeNull("blob のコピーは両側をそろえるので、サーバーに無い blob は端末でも消す");
        (await device.ReadUnsentAsync(ct)).Should().BeEmpty();
    }

    /// <summary>SyncFixture の端末の注文の blob を読む（行なし・NULL は null）</summary>
    private static async Task<byte[]?> ReadOrderBlobAsync(
        SyncFixtureEngineDevice device,
        int orderId,
        CancellationToken ct
    )
    {
        using var destination = new MemoryStream();

        return await device.Orders.ReadAttachmentAsync(orderId, destination, ct)
            ? destination.ToArray()
            : null;
    }

    /// <summary>SyncFixture の端末：NULL 許容の計算列のある行を降ろせ、端末の計算列は NULL のまま</summary>
    /// <remarks>
    /// 計算列は差分の SELECT に載る（行として届く）が、ローカルの行の口（<c>SqliteLocalRowStore</c>）は計算列を書き込みから外すので、
    /// 式を持たない端末の列は NULL のまま。NOT NULL なら書かない列の制約で降ろせない（NULL 許容だから成り立つ）
    /// </remarks>
    private static async Task SyncFixtureNullableComputedColumnAsync(
        SyncEngineFixedHarness _,
        CancellationToken ct
    )
    {
        using var server = await SyncFixtureTestServer.CreateAsync(ct);
        using var device = await SyncFixtureEngineDevice.CreateAsync(server.Sources, ct);
        await server.Orders.InsertAsync(
            new S.SyncOrderEntity { OrderId = 1, CustomerName = "alice" },
            ct
        );
        await server.Lines.InsertAsync(
            new S.SyncOrderLineEntity
            {
                LineId = 11,
                OrderId = 1,
                Product = "widget",
            },
            ct
        );
        await server.Sql.ExecuteSqlAsync(
            "UPDATE sync_orders SET summary = 'server-side summary' WHERE order_id = 1",
            null,
            ct
        );

        var result = await device.Engine.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty();
        result.Downloaded.Should().Be(2, "注文 1 行と明細 1 行が降りる");
        var order = await device.Orders.GetByIdAsync(1, ct);
        order.Should().NotBeNull();
        order!.CustomerName.Should().Be("alice");
        order.Summary.Should().BeNull("式を持たない端末へは計算列を書かない");
        (
            await server.Sql.ExecuteScalarSqlAsync<string>(
                "SELECT summary FROM sync_orders WHERE order_id = 1",
                null,
                ct
            )
        )
            .Should()
            .Be("server-side summary", "サーバーには降ろす値があった");

        var again = await device.Engine.SyncAsync(null, ct);

        again.Downloaded.Should().Be(0);
        again.Uploaded.Should().Be(0);
        (await device.ReadUnsentAsync(ct)).Should().BeEmpty();
    }

    /// <summary>SyncVoFixture の注文を 1 行作る（NOT NULL の除外列 photo も埋める＝アプリが挿入するときの形）</summary>
    private static V.SyncvoOrderEntity NewVoOrder(int id, string title) =>
        new()
        {
            OrderId = V.OrderIdValue.Create(id),
            Title = V.TitleValue.Create(title),
            Photo = V.PhotoValue.Create([1, 2, 3]),
        };

    /// <summary>SyncVoFixture の端末：NOT NULL の除外 blob を持つ新しい行が降り、blob は空の値で入る</summary>
    private static async Task SyncVoNewRowWithExcludedNotNullBlobAsync(
        SyncEngineFixedHarness _,
        CancellationToken ct
    )
    {
        using var server = await SyncVoFixtureTestServer.CreateAsync(ct);
        using var device = await SyncVoFixtureEngineDevice.CreateAsync(server.Orders, ct);
        await server.Writer.InsertAsync(NewVoOrder(7, "from-server"), ct);

        var result = await device.Engine.SyncAsync(null, ct);

        result.Downloaded.Should().Be(1);
        (await device.Orders.GetByIdAsync(V.OrderIdValue.Create(7), ct))!
            .Title.Value.Should()
            .Be("from-server");
        using var photo = new MemoryStream();
        (await device.Orders.ReadPhotoAsync(V.OrderIdValue.Create(7), photo, ct)).Should().BeTrue();
        photo.Length.Should().Be(0, "blob を運ばない同期では、新しい行の除外列は空の値で入る");
        (await device.ReadUnsentAsync(ct)).Should().BeEmpty();
    }

    /// <summary>SyncVoFixture の端末：版の列が空の端末の新しい行が上がる</summary>
    private static async Task SyncVoLocalInsertUploadsAsync(
        SyncEngineFixedHarness _,
        CancellationToken ct
    )
    {
        using var server = await SyncVoFixtureTestServer.CreateAsync(ct);
        using var device = await SyncVoFixtureEngineDevice.CreateAsync(server.Orders, ct);
        await device.Orders.InsertAsync(NewVoOrder(5, "dave"), ct);
        (await device.Orders.GetByIdAsync(V.OrderIdValue.Create(5), ct))!
            .RowVer.Should()
            .BeNull("まだ一度も上げていない行に版は無い");

        var result = await device.Engine.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await server.OrdersRaw.GetByIdAsync(V.OrderIdValue.Create(5), ct))!
            .Title.Value.Should()
            .Be("dave");
        (await device.ReadUnsentAsync(ct)).Should().BeEmpty();
    }

    /// <summary>SyncVoFixture の端末：サーバーが採番した版が値オブジェクトの版の列へ書き戻り、往復が定常になる</summary>
    private static async Task SyncVoServerVersionWrittenBackAsync(
        SyncEngineFixedHarness _,
        CancellationToken ct
    )
    {
        using var server = await SyncVoFixtureTestServer.CreateAsync(ct);
        using var device = await SyncVoFixtureEngineDevice.CreateAsync(server.Orders, ct);
        await device.Orders.InsertAsync(NewVoOrder(5, "dave"), ct);
        await device.Engine.SyncAsync(null, ct);

        var key = V.OrderIdValue.Create(5);
        var mirrored = await device.Orders.GetByIdAsync(key, ct);
        var stored = await server.OrdersRaw.GetByIdAsync(key, ct);
        mirrored!.RowVer.Should().NotBeNull("サーバーが採番した版を端末の版の列へ書き戻す");
        mirrored.RowVer.Value.Should().Equal(stored!.RowVer.Value);

        // 版を持つ行の更新（版で守る送信）も値オブジェクトの型のまま往復する
        mirrored.Title = V.TitleValue.Create("dave-updated");
        await device.Orders.UpdateAsync(mirrored, cancellationToken: ct);

        var second = await device.Engine.SyncAsync(null, ct);

        second.Conflicts.Should().BeEmpty();
        second.Uploaded.Should().Be(1);
        (await server.OrdersRaw.GetByIdAsync(key, ct))!.Title.Value.Should().Be("dave-updated");
        (await device.Orders.GetByIdAsync(key, ct))!
            .RowVer!.Value.Should()
            .Equal((await server.OrdersRaw.GetByIdAsync(key, ct))!.RowVer!.Value);

        var third = await device.Engine.SyncAsync(null, ct);

        third.Uploaded.Should().Be(0);
        third.Downloaded.Should().Be(0, "自分の変更を取り戻さない");
        third.DeletedLocally.Should().Be(0);
        (await device.ReadUnsentAsync(ct)).Should().BeEmpty();
    }

    /// <summary>SyncVoFixture の端末：ServerWins で戻した行の値オブジェクトの版の列はサーバーの版になる</summary>
    private static async Task SyncVoServerWinsRestoresWithServerVersionAsync(
        SyncEngineFixedHarness _,
        CancellationToken ct
    )
    {
        using var server = await SyncVoFixtureTestServer.CreateAsync(ct);
        using var device = await SyncVoFixtureEngineDevice.CreateAsync(server.Orders, ct);
        await device.Orders.InsertAsync(NewVoOrder(5, "dave"), ct);
        (await device.Engine.SyncAsync(null, ct)).Conflicts.Should().BeEmpty();

        var key = V.OrderIdValue.Create(5);
        var versionBefore = (await device.Orders.GetByIdAsync(key, ct))!.RowVer!.Value;

        // サーバーの他者が同じ行を編集する（サーバーの版が進む）
        var onServer = (await server.OrdersRaw.GetByIdAsync(key, ct))!;
        onServer.Title = V.TitleValue.Create("server-edit");
        (await server.Writer.UpdateAsync(onServer, cancellationToken: ct)).Should().BeTrue();

        // 端末も同じ行を編集する
        var local = (await device.Orders.GetByIdAsync(key, ct))!;
        local.Title = V.TitleValue.Create("local-edit");
        await device.Orders.UpdateAsync(local, cancellationToken: ct);

        var result = await device.Engine.SyncAsync(
            new V.SyncOptions { ConflictPolicy = V.SyncConflictPolicy.ServerWins },
            ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(0, "ServerWins は端末の編集を送らない");
        var restored = (await device.Orders.GetByIdAsync(key, ct))!;
        var stored = (await server.OrdersRaw.GetByIdAsync(key, ct))!;
        restored.Title.Value.Should().Be("server-edit", "端末の行はサーバーの内容に戻る");
        restored
            .RowVer!.Value.Should()
            .Equal(stored.RowVer!.Value, "値オブジェクトの版の列もサーバーのその行の版になる");
        restored
            .RowVer.Value.Should()
            .NotEqual(versionBefore, "サーバーの他者の編集で版は進んでいた");
        (await device.ReadUnsentAsync(ct)).Should().BeEmpty();

        var again = await device.Engine.SyncAsync(null, ct);

        again.Uploaded.Should().Be(0, "戻した行を送り直さない");
        again.Downloaded.Should().Be(0, "戻した行を取り直さない");
        again.DeletedLocally.Should().Be(0);
        again.Conflicts.Should().BeEmpty();
        (await device.ReadUnsentAsync(ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 記録の仕組みを通らずに入った行を作る：サーバーにフォルダー 1 を入れ、端末へは同じキー・同じ内容の行を記録しない書き込みで
    /// 入れる（同期は流さないので、行の状態も再開点も無い）
    /// </summary>
    private static async Task SeedStatelessFolderAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.ExecuteUntrackedAsync(
            "INSERT INTO model_folders (folder_id, name) VALUES (1, 'f1')",
            ct
        );
        (await h.ScalarAsync($"SELECT count(*) FROM {SyncLocalTracking.RowTableName}", ct))
            .Should()
            .Be("0", "行の状態が無い");
        (await h.ScalarAsync("SELECT count(*) FROM quicker_sync_table", ct))
            .Should()
            .Be("0", "再開点が無い");
        DescribeRows(await h.ReadLocalAsync(ct))
            .Should()
            .Be(DescribeRows(await h.ReadServerAsync(ct)), "端末とサーバーに同じ行がある");
    }

    /// <summary>記録の仕組みを通らずに入った行の観測（同期の結果・端末とサーバーの行・未送信の一覧）を 1 つの文字列にする</summary>
    private static async Task<string> ObserveStatelessAsync(
        SyncEngineFixedHarness h,
        string label,
        SyncResult result,
        CancellationToken ct
    ) =>
        $"{label}: uploaded={result.Uploaded} downloaded={result.Downloaded} deleted={result.DeletedLocally} "
        + $"conflicts=[{string.Join(", ", result.Conflicts.Select(c => $"{c.TableName}/{c.KeyText}:{c.Reason}"))}] "
        + $"changes=[{string.Join(", ", result.LocalRowChanges.Select(c => $"{c.TableName}/{c.KeyText}:{c.Reason}"))}]\n"
        + $"local:\n{DescribeRows(await h.ReadLocalAsync(ct))}\nserver:\n{DescribeRows(await h.ReadServerAsync(ct))}\n"
        + $"pending=[{string.Join(", ", (await PendingAsync(h, ct)).Select(p => p.ToString()))}]";

    /// <summary>記録の仕組みを通らずに入った行を最初の同期の前に更新すると、同期は DuplicateOnServer を報告し続ける</summary>
    private static async Task StatelessRowUpdateBeforeFirstSyncAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedStatelessFolderAsync(h, ct);
        var folder = (await h.Folders.GetByIdAsync(1, ct))!;
        folder.Name = "local-edit";
        (await h.Folders.UpdateAsync(folder, cancellationToken: ct)).Should().BeTrue();

        foreach (var label in new[] { "first", "second" })
        {
            var result = await h.SyncAsync(null, ct);
            var observed = await ObserveStatelessAsync(h, label, result, ct);

            result
                .Conflicts.Select(conflict =>
                    (conflict.TableName, conflict.KeyText, conflict.Reason)
                )
                .Should()
                .Equal(
                    [(ModelTables.Folders, "1", SyncConflictReason.DuplicateOnServer)],
                    $"サーバーが知らない行の挿入として送り、同じキーの行がサーバーにある（{observed}）"
                );
            (await h.ReadLocalAsync(ct))
                .Folders[1]
                .Name.Should()
                .Be("local-edit", $"Collect は端末の内容を残す（{observed}）");
            (await h.ReadServerAsync(ct))
                .Folders[1]
                .Name.Should()
                .Be("f1", $"サーバーは変わらない（{observed}）");
            (await PendingAsync(h, ct))
                .Should()
                .Equal(
                    [(ModelTables.Folders, "1", false, false, false)],
                    $"サーバーが知らない未送信として残る（{observed}）"
                );
        }
    }

    /// <summary>記録の仕組みを通らずに入った行を最初の同期の前に削除すると、削除は送られず行が戻る</summary>
    private static async Task StatelessRowDeleteBeforeFirstSyncAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedStatelessFolderAsync(h, ct);
        (await h.Folders.DeleteAsync(1, ct)).Should().BeTrue();

        var result = await h.SyncAsync(null, ct);
        var observed = await ObserveStatelessAsync(h, "sync", result, ct);

        result.Conflicts.Should().BeEmpty($"競合は報告されない（{observed}）");
        result.Uploaded.Should().Be(0, $"削除は送られない（{observed}）");
        (await h.ReadServerAsync(ct))
            .Folders.Should()
            .ContainKey(1, $"サーバーの行は残る（{observed}）");
        (await h.ReadLocalAsync(ct))
            .Folders.Should()
            .ContainKey(1, $"端末に行が戻る（{observed}）");
        await ShouldMatchServerWithNothingPendingAsync(
            h,
            $"端末とサーバーが同じ（{observed}）",
            ct
        );
    }

    /// <summary>記録の仕組みを通らずに入った行は、最初の実行（同期か洗い替え）で行の状態を得て、その後の編集が上がる</summary>
    private static async Task StatelessRowsTakeStateFromFirstRunAsync(
        SyncEngineFixedHarness h,
        string first,
        CancellationToken ct
    )
    {
        await SeedStatelessFolderAsync(h, ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));
        var serverBefore = Describe(await h.ReadServerAsync(ct));

        if (first == "sync")
        {
            var initial = await h.SyncAsync(null, ct);
            var observed = await ObserveStatelessAsync(h, "first-sync", initial, ct);
            initial.Conflicts.Should().BeEmpty(observed);
            initial.Uploaded.Should().Be(0, $"何も送らない（{observed}）");
        }
        else
        {
            await h.RefreshAsync(new SyncRefreshOptions { IncludeUnboundedBinary = true }, ct);
        }

        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(localBefore, "最初の実行は端末の内容を変えない");
        Describe(await h.ReadServerAsync(ct))
            .Should()
            .Be(serverBefore, "最初の実行はサーバーを変えない");
        (await PendingAsync(h, ct)).Should().BeEmpty("最初の実行の後に未送信は無い");

        var folder = (await h.Folders.GetByIdAsync(1, ct))!;
        folder.Name = "local-edit";
        (await h.Folders.UpdateAsync(folder, cancellationToken: ct)).Should().BeTrue();

        var result = await h.SyncAsync(null, ct);
        var afterEdit = await ObserveStatelessAsync(h, "edit-sync", result, ct);

        result.Conflicts.Should().BeEmpty($"競合なしに上がる（{afterEdit}）");
        result.Uploaded.Should().Be(1, afterEdit);
        (await h.ReadServerAsync(ct)).Folders[1].Name.Should().Be("local-edit", afterEdit);
        await ShouldMatchServerWithNothingPendingAsync(
            h,
            $"端末とサーバーが同じ（{afterEdit}）",
            ct
        );
    }

    /// <summary>適用はローカルの Save フックを通らない（同じフックは端末のグラフ保存では呼ばれて行を飛ばす）</summary>
    private static async Task DownloadDoesNotRunLocalSaveHooksAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.SyncAsync(null, ct);

        var calls = 0;
        h.BeforeFolderSave = (_, _) =>
        {
            Interlocked.Increment(ref calls);

            return Task.FromResult(false);
        };

        try
        {
            // 空振りでないことの確かめ：端末のリポジトリのグラフ保存ではフックが呼ばれ、行を飛ばす
            var skipped = new ModelFolderEntity { FolderId = 9, Name = "skipped" };
            skipped.MarkAdded();
            await h.Folders.SaveAsync(skipped, cancellationToken: ct);
            Volatile.Read(ref calls).Should().Be(1, "端末のグラフ保存ではフックが呼ばれる");
            (await h.ReadLocalAsync(ct))
                .Folders.Should()
                .NotContainKey(9, "フックが飛ばした行は書かれない");

            // 本題：サーバーの新しい行と更新を同期で降ろす
            await h.Server.Folders.InsertAsync(
                new ModelFolderEntity { FolderId = 2, Name = "f2" },
                ct
            );
            var onServer = (await h.Server.Folders.GetByIdAsync(1, ct))!;
            onServer.Name = "server-edit";
            (await h.Server.Folders.UpdateAsync(onServer, cancellationToken: ct)).Should().BeTrue();

            var result = await h.SyncAsync(null, ct);

            result.Conflicts.Should().BeEmpty();
            result.Downloaded.Should().Be(2, "新しい行と更新の 2 行が降りる");
            var local = await h.ReadLocalAsync(ct);
            local.Folders[2].Name.Should().Be("f2", "新しい行はフックに飛ばされずに入る");
            local.Folders[1].Name.Should().Be("server-edit", "更新もフックに飛ばされずに入る");
            Volatile.Read(ref calls).Should().Be(1, "適用はローカルの Save フックを呼ばない");
        }
        finally
        {
            h.BeforeFolderSave = null;
        }

        await ShouldMatchServerWithNothingPendingAsync(h, "端末とサーバーが同じ", ct);
    }

    /// <summary>未送信の行の一覧と、行を捨てる口の行</summary>
    private static IEnumerable<ScenarioRow<SyncEngineFixedCase>> PendingRows() =>
        [
            Row(
                "pending-list-names-unsent-rows",
                "サーバーが知っている品目の更新・端末で作ったフォルダーの挿入・サーバーが知っている品目の削除の後、未送信の"
                    + "一覧は変更の順に、表・キーのテキスト・削除か・サーバーが知っているか・捨てる印なしを返す。同期の後は空",
                PendingListAsync
            ),
            Row(
                "pending-list-does-not-wait-for-run-and-discard-does",
                "同期がサーバー役の待ちの点で止まっている間、未送信の一覧は同期を待たずに読めてその時点の未送信を返し、"
                    + "全部を捨てる口は同期の終わりを待つ（同期を進めた後に完了する）",
                PendingListDoesNotWaitForRunAsync
            ),
            Row(
                "pending-discard-settles-collect-conflict-with-server-row",
                "サーバーの他者と端末が同じ品目を編集して Collect の同期が ModifiedOnServer を報告した後、一覧から選んで捨てると"
                    + "（戻り値 1・一覧の印が立つ）、次の Collect の同期は競合なし・何も送らずにサーバーの内容へ戻し、"
                    + "LocalRowChanges に DiscardedByServerWins で載せる。一覧は空",
                DiscardSettlesCollectConflictAsync
            ),
            .. new[] { "sync", "upload-only", "download-only" }.Select(run =>
                Row(
                    $"pending-discard-row-unchanged-on-server-is-read-back-{run}",
                    $"サーバーでは変わっていない（再開点を過ぎた）品目を端末だけが編集して捨てると、次の実行（{run}）が"
                        + "サーバーの内容へ戻し、サーバーは変わらず、一覧は空",
                    (h, ct) => DiscardUnchangedRowAsync(h, run, ct)
                )
            ),
            Row(
                "pending-discard-then-rewrite-is-sent",
                "捨てた後に同じ品目をもう一度書くと、一覧の捨てる印が外れ、次の同期が書いた内容をサーバーへ送る",
                DiscardThenRewriteAsync
            ),
            Row(
                "pending-discard-insert-unknown-to-server-removes-row",
                "端末で作っただけのフォルダーを捨てると、次の同期の後、その行は端末にもサーバーにも無い",
                DiscardUnknownInsertAsync
            ),
            Row(
                "pending-discard-delete-brings-row-back",
                "サーバーが知っている品目の削除を捨てると、次の同期の後、端末に行が blob ごと戻り、サーバーの行は残る",
                DiscardDeleteAsync
            ),
            Row(
                "pending-discard-with-stale-row-is-not-discarded",
                "一覧を読んだ後に品目をもう一度書き、古い一覧の行を渡して捨てると、戻り値 0 で印は立たず、次の同期が書いた"
                    + "内容を送る",
                DiscardStaleRowAsync
            ),
            Row(
                "pending-discard-by-table-and-all",
                "2 つの表に未送信があるとき、型を渡す口はその表の行だけに印を立て、全部を捨てる口は残りにも立てる。次の同期の後、"
                    + "どの行もサーバーの内容で、サーバーは変わらない",
                DiscardByTableAndAllAsync
            ),
            Row(
                "pending-discard-is-kept-for-run-that-excludes-table",
                "品目を捨てた後、品目の表を外した同期は行を端末の内容のまま残し、一覧に捨てる印つきで残る。外さない次の同期で"
                    + "サーバーの内容に戻る",
                DiscardKeptForExcludingRunAsync
            ),
            Row(
                "pending-discard-refuses-unknown-table-and-type",
                "エンジンの表でない表の名前の行・エンジンの表でない型・null を渡すと、それぞれ ArgumentException・"
                    + "ArgumentNullException で断り、行の状態の表は変わらない",
                DiscardRefusesAsync
            ),
            Row(
                "pending-list-and-discard-cover-only-engine-tables",
                "フォルダーと品目の 2 表だけで組んだエンジンでは、ラベルの未送信の行は一覧に出ず、全部を捨てる口も印を立てず、"
                    + "ラベルの表の名前の行を渡すと ArgumentException で断る。4 表のエンジンの一覧には、ラベルの行が捨てる印なしで残る",
                CoverOnlyEngineTablesAsync
            ),
            Row(
                "pending-discard-is-replaced-by-refresh-without-force",
                "端末だけが編集した品目を捨てた後、Force なしの洗い替えは断らずに終わり（DiscardedChanges 1）、品目はサーバーの"
                    + "内容になり、一覧は空で、サーバーは変わらない。端末で作っただけのフォルダーを捨てた後の Force なしの洗い替えも"
                    + "断らず（DiscardedChanges 1）、そのフォルダーは端末に無い",
                DiscardReplacedByRefreshAsync
            ),
            Row(
                "pending-discard-rewritten-row-still-refuses-refresh",
                "品目を捨てた後に同じ品目をもう一度書くと、Force なしの洗い替えは SyncPendingChangesException（PendingCount 1）で"
                    + "断り、端末と行の状態の表は変わらない",
                DiscardRewrittenRefusesRefreshAsync
            ),
            Row(
                "pending-discard-does-not-hide-other-unsent-rows-from-refresh",
                "フォルダーと品目に未送信があり品目だけを捨てると、Force なしの洗い替えは SyncPendingChangesException で断り、"
                    + "例外の件数は捨てる印の無いフォルダーの 1 件だけ。端末と行の状態の表は変わらない",
                DiscardDoesNotHideOtherUnsentAsync
            ),
            Row(
                "pending-discard-is-replaced-by-chunked-refresh",
                "捨てる印のある品目（題名の編集と blob の書き込み）を持つ端末の、塊ごとに確定する blob を捨てる Force なしの"
                    + "洗い替えは、塊の道を通って断らずに終わり、捨てた品目はサーバーの内容で、どの品目の blob も空になり、"
                    + "未送信が残らない",
                DiscardReplacedByChunkedRefreshAsync
            ),
            Row(
                "pending-discard-is-not-counted-for-refresh-row-limit",
                "捨てる印のある行だけが未送信の端末の、降ろす行の上限を超える Force なしの洗い替えは "
                    + "SyncDownloadTooLargeException で断らずに終わり、捨てた行はサーバーの内容になり、サーバーの新しい行も入る",
                DiscardNotCountedForRowLimitAsync
            ),
            Row(
                "pending-discard-rewritten-during-fetch-is-counted-for-refresh-row-limit",
                "捨てる印のある行を、Force なしの洗い替えの取得の途中に端末がもう一度書く（連番が進む）と、その行は未送信に"
                    + "数え、降ろす行の上限を超えた洗い替えは SyncDownloadTooLargeException で断る（端末の行は書き直した内容の"
                    + "未送信のまま、サーバーと再開点は変わらない）",
                DiscardRewrittenDuringFetchCountedForRowLimitAsync
            ),
        ];

    /// <summary>サーバーにフォルダー 1 と品目 1・2 を入れ、blob を運ぶ同期で端末へ降ろす</summary>
    private static async Task SeedPendingAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await SeedItemsAsync(h, ct);
        await h.SyncAsync(WithBlobs, ct);
    }

    /// <summary>未送信の一覧（表・キー・削除か・サーバーが知っているか・捨てる印）</summary>
    private static async Task<
        List<(string Table, string Key, bool IsDelete, bool Known, bool Discarded)>
    > PendingAsync(SyncEngineFixedHarness h, CancellationToken ct) =>
        (await h.Engine.GetPendingChangesAsync(ct))
            .Select(row =>
                (row.TableName, row.KeyText, row.IsDelete, row.KnownToServer, row.IsDiscarded)
            )
            .ToList();

    /// <summary>一覧から 1 行を選ぶ</summary>
    private static async Task<SyncPendingRow> PendingRowAsync(
        SyncEngineFixedHarness h,
        string table,
        string key,
        CancellationToken ct
    ) =>
        (await h.Engine.GetPendingChangesAsync(ct)).Single(row =>
            row.TableName == table && row.KeyText == key
        );

    private static async Task SetServerTitleAsync(
        SyncEngineFixedHarness h,
        int key,
        string title,
        CancellationToken ct
    )
    {
        var item = (await h.Server.Items.GetByIdAsync(key, ct))!;
        item.Title = title;
        (await h.Server.Items.UpdateAsync(item, cancellationToken: ct)).Should().BeTrue();
    }

    private static Task SetLocalTitleAsync(
        SyncEngineFixedHarness h,
        int key,
        string title,
        CancellationToken ct
    ) => h.ExecuteLocalAsync($"UPDATE model_items SET title = '{title}' WHERE item_id = {key}", ct);

    /// <summary>端末とサーバーの内容が同じで、未送信が無いこと</summary>
    private static async Task ShouldMatchServerWithNothingPendingAsync(
        SyncEngineFixedHarness h,
        string because,
        CancellationToken ct
    )
    {
        Describe(await h.ReadLocalAsync(ct))
            .Should()
            .Be(Describe(await h.ReadServerAsync(ct)), because);
        (await h.Engine.GetPendingChangesAsync(ct)).Should().BeEmpty("未送信は残らない");
    }

    /// <summary>未送信の一覧の中身と順と、同期の後に空になること</summary>
    private static async Task PendingListAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await SeedPendingAsync(h, ct);
        (await h.Engine.GetPendingChangesAsync(ct)).Should().BeEmpty("同期の直後は未送信なし");

        await SetLocalTitleAsync(h, 1, "local-edit", ct);
        await h.ExecuteLocalAsync(
            "INSERT INTO model_folders (folder_id, name) VALUES (2, 'local-new')",
            ct
        );
        await h.ExecuteLocalAsync("DELETE FROM model_items WHERE item_id = 2", ct);

        var listed = await h.Engine.GetPendingChangesAsync(ct);
        listed
            .Select(row =>
                (row.TableName, row.KeyText, row.IsDelete, row.KnownToServer, row.IsDiscarded)
            )
            .Should()
            .Equal(
                [
                    (ModelTables.Items, "1", false, true, false),
                    (ModelTables.Folders, "2", false, false, false),
                    (ModelTables.Items, "2", true, true, false),
                ],
                "変更の順に、表・キー・削除か・サーバーが知っているか・捨てる印を返す"
            );
        listed
            .Select(row => row.ChangeNumber)
            .Should()
            .BeInAscendingOrder("変更の連番の順")
            .And.OnlyHaveUniqueItems();

        var result = await h.SyncAsync(WithBlobs, ct);
        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(3);
        await ShouldMatchServerWithNothingPendingAsync(h, "同期で 3 つとも上がる", ct);
    }

    /// <summary>
    /// 一覧は実行を待たず、全部を捨てる口は実行の終わりを待つ：同期をサーバーへの書き込みの直前で止め、その間の一覧と捨てる口の
    /// 完了と、手順の順序を記録する
    /// </summary>
    private static async Task PendingListDoesNotWaitForRunAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await SetLocalTitleAsync(h, 1, "local-edit", ct);

        var log = new List<string>();

        void Note(string step)
        {
            lock (log)
            {
                log.Add(step);
            }
        }

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

        Task<IReadOnlyList<SyncPendingRow>> list;
        bool listDoneWhileHeld;
        bool discardDoneWhileHeld;
        SyncResult synced;
        int discarded;

        try
        {
            var sync = Task.Run(
                async () =>
                {
                    var result = await h.Engine.SyncAsync(WithBlobs, ct);
                    Note("sync-done");

                    return result;
                },
                ct
            );
            await reached.Task.WaitAsync(ReachTimeout, ct);

            // 一覧：同期が止まっている間に完了するか（上限は安全のためだけ）
            list = h.Engine.GetPendingChangesAsync(ct);
            listDoneWhileHeld = await Task.WhenAny(list, Task.Delay(ReachTimeout, ct)) == list;
            Note("listed");

            // 全部を捨てる口：同期が止まっている間は完了しない
            var discard = Task.Run(
                async () =>
                {
                    var count = await h.Engine.DiscardAllPendingChangesAsync(ct);
                    Note("discard-done");

                    return count;
                },
                ct
            );
            discardDoneWhileHeld =
                await Task.WhenAny(discard, Task.Delay(SecondRunWindow, ct)) == discard;
            Note("release");
            release.TrySetResult();
            synced = await sync.WaitAsync(ReachTimeout, ct);
            discarded = await discard.WaitAsync(ReachTimeout, ct);
        }
        finally
        {
            release.TrySetResult();
            h.Faults.Syncing = null;
        }

        listDoneWhileHeld.Should().BeTrue("未送信の一覧は、同期の終わりを待たずに読める");
        (await list)
            .Select(row => (row.TableName, row.KeyText, row.IsDiscarded))
            .Should()
            .Equal(
                [(ModelTables.Items, "1", false)],
                "同期の最中に読んだ一覧は、その時点までにコミットされた未送信を映す"
            );
        discardDoneWhileHeld.Should().BeFalse("全部を捨てる口は、同期が止まっている間は完了しない");
        log.IndexOf("listed")
            .Should()
            .BeLessThan(
                log.IndexOf("release"),
                $"一覧は同期を進める前に済む（記録：{string.Join(", ", log)}）"
            );
        log.IndexOf("discard-done")
            .Should()
            .BeGreaterThan(
                log.IndexOf("release"),
                $"捨てる口は同期を進めた後に完了する（記録：{string.Join(", ", log)}）"
            );
        synced.Uploaded.Should().Be(1, "止めていた同期は編集を送り終える");
        discarded.Should().Be(0, "同期の終わりを待った捨てる口には、もう捨てる未送信が無い");
        await ShouldMatchServerWithNothingPendingAsync(h, "編集がサーバーへ上がっている", ct);
    }

    /// <summary>Collect の競合を報告された行を捨てると、次の Collect の同期がサーバーの内容で決着する</summary>
    private static async Task DiscardSettlesCollectConflictAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await SetServerTitleAsync(h, 1, "server-edit", ct);
        await SetLocalTitleAsync(h, 1, "local-edit", ct);

        var collect = await h.SyncAsync(WithBlobs, ct);
        collect
            .Conflicts.Select(conflict => (conflict.TableName, conflict.KeyText, conflict.Reason))
            .Should()
            .Equal([(ModelTables.Items, "1", SyncConflictReason.ModifiedOnServer)]);
        (await h.ReadLocalAsync(ct))
            .Items[1]
            .Title.Should()
            .Be("local-edit", "Collect は端末の内容を残す");
        (await PendingAsync(h, ct))
            .Should()
            .Equal([(ModelTables.Items, "1", false, true, false)], "未送信のまま残る");

        var row = await PendingRowAsync(h, ModelTables.Items, "1", ct);
        (await h.Engine.DiscardPendingChangesAsync([row], ct)).Should().Be(1);
        (await PendingAsync(h, ct))
            .Should()
            .Equal(
                [(ModelTables.Items, "1", false, true, true)],
                "捨てる印が立ち、未送信のまま残る"
            );

        var next = await h.SyncAsync(WithBlobs, ct);
        next.Conflicts.Should().BeEmpty("捨てた行は競合にならない");
        next.Uploaded.Should().Be(0, "捨てた行は送らない");
        next.LocalRowChanges.Should()
            .Contain(
                new SyncLocalRowChange(
                    ModelTables.Items,
                    "1",
                    SyncLocalRowChangeReason.DiscardedByServerWins
                )
            );
        (await h.ReadLocalAsync(ct)).Items[1].Title.Should().Be("server-edit");
        await ShouldMatchServerWithNothingPendingAsync(h, "端末がサーバーの内容になる", ct);
    }

    /// <summary>サーバーで変わっていない行を捨てると、どの実行の種類もサーバーからキーで読んで戻す</summary>
    private static async Task DiscardUnchangedRowAsync(
        SyncEngineFixedHarness h,
        string run,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await SetLocalTitleAsync(h, 1, "local-edit", ct);
        var serverBefore = Describe(await h.ReadServerAsync(ct));

        (await h.Engine.DiscardAllPendingChangesAsync(ct)).Should().Be(1);

        switch (run)
        {
            case "sync":
                (await h.SyncAsync(WithBlobs, ct)).Conflicts.Should().BeEmpty();

                break;

            case "upload-only":
                var conflicts = new List<SyncConflict>();
                (await h.Engine.UploadAsync(WithBlobs, conflicts, ct)).Uploaded.Should().Be(0);
                conflicts.Should().BeEmpty();

                break;

            default:
                await h.Engine.DownloadAsync(WithBlobs, ct);

                break;
        }

        (await h.ReadLocalAsync(ct))
            .Items[1]
            .Title.Should()
            .Be("t1", "捨てた行はサーバーの内容に戻る");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "サーバーは変わらない");
        await ShouldMatchServerWithNothingPendingAsync(h, "端末がサーバーの内容になる", ct);
    }

    /// <summary>捨てた後に書き直すと、印が外れて書いた内容が送られる</summary>
    private static async Task DiscardThenRewriteAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await SetLocalTitleAsync(h, 1, "first-edit", ct);
        (
            await h.Engine.DiscardPendingChangesAsync(
                [await PendingRowAsync(h, ModelTables.Items, "1", ct)],
                ct
            )
        )
            .Should()
            .Be(1);
        await SetLocalTitleAsync(h, 1, "second-edit", ct);
        (await PendingAsync(h, ct))
            .Should()
            .Equal(
                [(ModelTables.Items, "1", false, true, false)],
                "書き直すと捨てる印は効かなくなる"
            );

        var result = await h.SyncAsync(WithBlobs, ct);
        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1, "書き直した内容が送られる");
        (await h.ReadServerAsync(ct)).Items[1].Title.Should().Be("second-edit");
        await ShouldMatchServerWithNothingPendingAsync(h, "端末とサーバーが同じ", ct);
    }

    /// <summary>サーバーが知らない挿入を捨てると、端末から消える</summary>
    private static async Task DiscardUnknownInsertAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await h.ExecuteLocalAsync(
            "INSERT INTO model_folders (folder_id, name) VALUES (5, 'local-only')",
            ct
        );
        (await PendingAsync(h, ct))
            .Should()
            .Equal([(ModelTables.Folders, "5", false, false, false)]);
        (
            await h.Engine.DiscardPendingChangesAsync(
                [await PendingRowAsync(h, ModelTables.Folders, "5", ct)],
                ct
            )
        )
            .Should()
            .Be(1);

        var result = await h.SyncAsync(WithBlobs, ct);
        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(0);
        (await h.ReadLocalAsync(ct)).Folders.Should().NotContainKey(5, "端末から消える");
        (await h.ReadServerAsync(ct)).Folders.Should().NotContainKey(5, "サーバーへ送らない");
        await ShouldMatchServerWithNothingPendingAsync(h, "端末とサーバーが同じ", ct);
    }

    /// <summary>サーバーが知っている行の削除を捨てると、行が端末へ戻る</summary>
    private static async Task DiscardDeleteAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await SeedPendingAsync(h, ct);
        await h.ExecuteLocalAsync("DELETE FROM model_items WHERE item_id = 2", ct);
        (await PendingAsync(h, ct)).Should().Equal([(ModelTables.Items, "2", true, true, false)]);
        (
            await h.Engine.DiscardPendingChangesAsync(
                [await PendingRowAsync(h, ModelTables.Items, "2", ct)],
                ct
            )
        )
            .Should()
            .Be(1);

        var result = await h.SyncAsync(WithBlobs, ct);
        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(0);
        (await h.ReadServerAsync(ct)).Items.Should().ContainKey(2, "サーバーの行は残る");
        (await h.ReadLocalAsync(ct)).Items.Should().ContainKey(2, "端末に行が戻る");
        await ShouldMatchServerWithNothingPendingAsync(h, "blob ごと端末とサーバーが同じ", ct);
    }

    /// <summary>一覧の後に書き直した行は、古い一覧の行では捨てられない</summary>
    private static async Task DiscardStaleRowAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await SeedPendingAsync(h, ct);
        await SetLocalTitleAsync(h, 1, "first-edit", ct);
        var stale = await PendingRowAsync(h, ModelTables.Items, "1", ct);
        await SetLocalTitleAsync(h, 1, "second-edit", ct);

        (await h.Engine.DiscardPendingChangesAsync([stale], ct))
            .Should()
            .Be(0, "連番が進んだ行には印を立てない");
        (await PendingAsync(h, ct))
            .Should()
            .Equal([(ModelTables.Items, "1", false, true, false)], "印は立たない");

        var result = await h.SyncAsync(WithBlobs, ct);
        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await h.ReadServerAsync(ct)).Items[1].Title.Should().Be("second-edit", "書いた内容を送る");
        await ShouldMatchServerWithNothingPendingAsync(h, "端末とサーバーが同じ", ct);
    }

    /// <summary>型を渡す口はその表だけ、全部を捨てる口は残りにも印を立てる</summary>
    private static async Task DiscardByTableAndAllAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-folder' WHERE folder_id = 1",
            ct
        );
        await SetLocalTitleAsync(h, 1, "local-item", ct);
        var serverBefore = Describe(await h.ReadServerAsync(ct));

        (await h.Engine.DiscardPendingChangesAsync(typeof(ModelItemEntity), ct))
            .Should()
            .Be(1, "品目の表の行だけに立てる");
        (await PendingAsync(h, ct))
            .Should()
            .Equal(
                [
                    (ModelTables.Folders, "1", false, true, false),
                    (ModelTables.Items, "1", false, true, true),
                ],
                "フォルダーの行には印が立たない"
            );

        (
            await h.Engine.DiscardPendingChangesAsync(
                [await PendingRowAsync(h, ModelTables.Items, "1", ct)],
                ct
            )
        )
            .Should()
            .Be(1, "すでに印のある行も、捨てる行として数える");

        (await h.Engine.DiscardAllPendingChangesAsync(ct))
            .Should()
            .Be(2, "全部の未送信の行に立てる");
        (await PendingAsync(h, ct))
            .Should()
            .Equal([
                (ModelTables.Folders, "1", false, true, true),
                (ModelTables.Items, "1", false, true, true),
            ]);

        var result = await h.SyncAsync(WithBlobs, ct);
        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(0);
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "サーバーは変わらない");
        await ShouldMatchServerWithNothingPendingAsync(h, "どの行もサーバーの内容", ct);
    }

    /// <summary>表を外した実行は、捨てる印の行に触らない</summary>
    private static async Task DiscardKeptForExcludingRunAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await SetLocalTitleAsync(h, 1, "local-edit", ct);
        (await h.Engine.DiscardAllPendingChangesAsync(ct)).Should().Be(1);

        var excluding = await h.SyncAsync(
            new SyncOptions
            {
                IncludeUnboundedBinary = true,
                ExcludedEntityTypes = [typeof(ModelItemEntity)],
            },
            ct
        );
        excluding.Conflicts.Should().BeEmpty();
        (await h.ReadLocalAsync(ct))
            .Items[1]
            .Title.Should()
            .Be("local-edit", "表を外した同期は行を端末の内容のまま残す");
        (await h.ReadServerAsync(ct)).Items[1].Title.Should().Be("t1");
        (await PendingAsync(h, ct))
            .Should()
            .Equal([(ModelTables.Items, "1", false, true, true)], "捨てる印つきで残る");

        (await h.SyncAsync(WithBlobs, ct)).Conflicts.Should().BeEmpty();
        (await h.ReadLocalAsync(ct)).Items[1].Title.Should().Be("t1");
        await ShouldMatchServerWithNothingPendingAsync(h, "外さない同期でサーバーの内容に戻る", ct);
    }

    /// <summary>エンジンの表でない表・型と null を断り、何も変えない</summary>
    private static async Task DiscardRefusesAsync(SyncEngineFixedHarness h, CancellationToken ct)
    {
        await SeedPendingAsync(h, ct);
        await SetLocalTitleAsync(h, 1, "local-edit", ct);
        var known = await PendingRowAsync(h, ModelTables.Items, "1", ct);
        var stateBefore = await h.ReadStateTablesAsync(ct);

        var unknownTable = await Record.ExceptionAsync(() =>
            h.Engine.DiscardPendingChangesAsync(
                [known, known with { TableName = "no_such_table" }],
                ct
            )
        );
        unknownTable
            .Should()
            .BeOfType<ArgumentException>()
            .Which.Message.Should()
            .Contain("no_such_table", "表の名前を名指しする");

        var unknownType = await Record.ExceptionAsync(() =>
            h.Engine.DiscardPendingChangesAsync(typeof(string), ct)
        );
        unknownType
            .Should()
            .BeOfType<ArgumentException>()
            .Which.Message.Should()
            .Contain(nameof(String));

        (
            await Record.ExceptionAsync(() =>
                h.Engine.DiscardPendingChangesAsync((IEnumerable<SyncPendingRow>)null!, ct)
            )
        )
            .Should()
            .BeOfType<ArgumentNullException>();
        (await Record.ExceptionAsync(() => h.Engine.DiscardPendingChangesAsync((Type)null!, ct)))
            .Should()
            .BeOfType<ArgumentNullException>();

        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(stateBefore, "断った呼び出しは行の状態の表を変えない");
    }

    /// <summary>一覧と捨てる口は、そのエンジンの表の行だけを扱う</summary>
    private static async Task CoverOnlyEngineTablesAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await h.ExecuteLocalAsync(
            "INSERT INTO model_labels (label_code, folder_id, caption) VALUES ('L1', 1, 'local')",
            ct
        );
        await SetLocalTitleAsync(h, 1, "local-edit", ct);
        var narrow = h.CreateFolderAndItemEngine();

        (await narrow.GetPendingChangesAsync(ct))
            .Select(row => (row.TableName, row.KeyText))
            .Should()
            .Equal([(ModelTables.Items, "1")], "エンジンの表でないラベルの行は一覧に出ない");
        (await narrow.DiscardAllPendingChangesAsync(ct))
            .Should()
            .Be(1, "エンジンの表の行だけに印を立てる");

        var label = await PendingRowAsync(h, ModelTables.Labels, "L1", ct);
        (await Record.ExceptionAsync(() => narrow.DiscardPendingChangesAsync([label], ct)))
            .Should()
            .BeOfType<ArgumentException>()
            .Which.Message.Should()
            .Contain(ModelTables.Labels);

        (await PendingAsync(h, ct))
            .Should()
            .Equal(
                [
                    (ModelTables.Labels, "L1", false, false, false),
                    (ModelTables.Items, "1", false, true, true),
                ],
                "ラベルの行には印が立たず、品目の行にだけ立つ"
            );
    }

    /// <summary>blob を運ぶ、Force なしの洗い替え</summary>
    private static readonly SyncRefreshOptions RefreshWithBlobs = new()
    {
        IncludeUnboundedBinary = true,
    };

    /// <summary>
    /// 捨てる印のある行は、Force なしの洗い替えが未送信に数えずにサーバーの内容で置き換える（サーバーに無い行は端末から消す）
    /// </summary>
    private static async Task DiscardReplacedByRefreshAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        var serverBefore = Describe(await h.ReadServerAsync(ct));

        // サーバーが知っている行：端末だけが編集して捨てる
        await SetLocalTitleAsync(h, 1, "local-edit", ct);
        (
            await h.Engine.DiscardPendingChangesAsync(
                [await PendingRowAsync(h, ModelTables.Items, "1", ct)],
                ct
            )
        )
            .Should()
            .Be(1);

        var refreshed = await h.RefreshAsync(RefreshWithBlobs, ct);

        refreshed.DiscardedChanges.Should().Be(1, "捨てる印のある行の変更を捨てる");
        (await h.ReadLocalAsync(ct))
            .Items[1]
            .Title.Should()
            .Be("t1", "捨てた行はサーバーの内容になる");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "サーバーは変わらない");
        await ShouldMatchServerWithNothingPendingAsync(h, "端末がサーバーの内容になる", ct);

        // サーバーが知らない行：端末で作っただけのフォルダーを捨てる
        await h.ExecuteLocalAsync(
            "INSERT INTO model_folders (folder_id, name) VALUES (5, 'local-only')",
            ct
        );
        (
            await h.Engine.DiscardPendingChangesAsync(
                [await PendingRowAsync(h, ModelTables.Folders, "5", ct)],
                ct
            )
        )
            .Should()
            .Be(1);

        var second = await h.RefreshAsync(RefreshWithBlobs, ct);

        second.DiscardedChanges.Should().Be(1, "捨てる印のある挿入の変更を捨てる");
        (await h.ReadLocalAsync(ct))
            .Folders.Should()
            .NotContainKey(5, "サーバーに無い行は端末から消える");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "サーバーは変わらない");
        await ShouldMatchServerWithNothingPendingAsync(h, "端末がサーバーの内容になる", ct);
    }

    /// <summary>捨てた後に書き直した行は、Force なしの洗い替えが未送信に数えて断る</summary>
    private static async Task DiscardRewrittenRefusesRefreshAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await SetLocalTitleAsync(h, 1, "first-edit", ct);
        (await h.Engine.DiscardAllPendingChangesAsync(ct)).Should().Be(1);
        await SetLocalTitleAsync(h, 1, "second-edit", ct);
        (await PendingAsync(h, ct))
            .Should()
            .Equal(
                [(ModelTables.Items, "1", false, true, false)],
                "書き直すと捨てる印は効かなくなる（前提）"
            );
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));

        var thrown = await Record.ExceptionAsync(() => h.RefreshAsync(RefreshWithBlobs, ct));

        var refused = thrown
            .Should()
            .BeOfType<SyncPendingChangesException>("書き直した行は未送信に数える")
            .Which;
        refused.PendingCount.Should().Be(1);
        refused.PendingChanges.Should().Equal([new SyncPendingChange(ModelTables.Items, 1)]);
        Describe(await h.ReadLocalAsync(ct)).Should().Be(localBefore, "端末は変わらない");
        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(stateBefore, "断った洗い替えは行の状態の表を変えない");
    }

    /// <summary>
    /// 捨てる印のある行があっても、印の無いほかの未送信の行で Force なしの洗い替えは断り、例外の件数は印の無い行だけを数える
    /// </summary>
    private static async Task DiscardDoesNotHideOtherUnsentAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-folder' WHERE folder_id = 1",
            ct
        );
        await SetLocalTitleAsync(h, 1, "local-item", ct);
        (
            await h.Engine.DiscardPendingChangesAsync(
                [await PendingRowAsync(h, ModelTables.Items, "1", ct)],
                ct
            )
        )
            .Should()
            .Be(1);
        (await PendingAsync(h, ct))
            .Should()
            .Equal(
                [
                    (ModelTables.Folders, "1", false, true, false),
                    (ModelTables.Items, "1", false, true, true),
                ],
                "品目にだけ捨てる印がある（前提）"
            );
        var stateBefore = await h.ReadStateTablesAsync(ct);
        var localBefore = Describe(await h.ReadLocalAsync(ct));

        var thrown = await Record.ExceptionAsync(() => h.RefreshAsync(RefreshWithBlobs, ct));

        var refused = thrown
            .Should()
            .BeOfType<SyncPendingChangesException>("印の無いフォルダーの未送信がある")
            .Which;
        refused.PendingCount.Should().Be(1, "数えるのは印の無い行だけ");
        refused
            .PendingChanges.Should()
            .Equal(
                [new SyncPendingChange(ModelTables.Folders, 1)],
                "表ごとの内訳にも捨てる印のある品目は入らない"
            );
        Describe(await h.ReadLocalAsync(ct)).Should().Be(localBefore, "端末は変わらない");
        (await h.ReadStateTablesAsync(ct))
            .Should()
            .Equal(stateBefore, "断った洗い替えは行の状態の表を変えない");
    }

    /// <summary>
    /// 塊ごとに確定する経路：<see cref="SeedDiscardedBlobWritesAsync"/> の準備に品目 10 の題名の編集を足し、未送信の全部（品目 10・
    /// 12）を捨ててから、Force なしの blob を捨てる洗い替え（取得の大きさ 3・閾値 2・塊 2）を流す。印のある行は Force で捨てる行と
    /// 同じに扱われ、置き換えた塊がサーバーの内容を書いて blob を空にする
    /// </summary>
    private static async Task DiscardReplacedByChunkedRefreshAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedDiscardedBlobWritesAsync(h, ct);
        await SetLocalTitleAsync(h, 10, "local-edit", ct);
        (await h.Engine.DiscardAllPendingChangesAsync(ct)).Should().Be(2, "品目 10 と 12 を捨てる");
        var serverBefore = Describe(await h.ReadServerAsync(ct));
        var routes = new List<SyncDownloadRoute>();
        var engine = SmallChunks(h.CreateEngine());
        engine.DownloadRouteObserved = routes.Add;

        var result = await h.RefreshAsync(
            new SyncRefreshOptions { BatchSize = 3, DiscardLocalUnboundedBinaries = true },
            ct,
            engine
        );

        routes.Should().Equal([SyncDownloadRoute.Chunked], "洗い替えは塊の道を通る");
        result.DiscardedChanges.Should().Be(2);
        var local = await h.ReadLocalAsync(ct);
        local.Items[10].Title.Should().Be("t10", "捨てた品目はサーバーの内容になる");
        local
            .Items.OrderBy(pair => pair.Key)
            .Select(pair => pair.Key + "=" + pair.Value.PayloadHex)
            .Should()
            .Equal(
                ["10=", "11=", "12=", "13="],
                "blob を捨てる洗い替えの後、捨てた行も含めてどの品目の blob も空"
            );
        DescribeRows(local)
            .Should()
            .Be(DescribeRows(await h.ReadServerAsync(ct)), "blob の外の内容は端末とサーバーで同じ");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "サーバーは変わらない");
        (await h.Engine.GetPendingChangesAsync(ct)).Should().BeEmpty("未送信は残らない");
    }

    /// <summary>
    /// 降ろす行の上限：フォルダー 1 を降ろして端末で編集し、捨ててから、サーバーにフォルダー 2〜4 を足し、取得の大きさ 1・上限 2
    /// の Force なしの洗い替えを流す。印のある行は上限の未送信に数えないので、断らずに 4 行が入る
    /// </summary>
    private static async Task DiscardNotCountedForRowLimitAsync(
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
        (await h.Engine.DiscardAllPendingChangesAsync(ct)).Should().Be(1);

        foreach (var id in new[] { 2, 3, 4 })
        {
            await h.Server.Folders.InsertAsync(
                new ModelFolderEntity { FolderId = id, Name = $"f{id}" },
                ct
            );
        }

        var serverBefore = Describe(await h.ReadServerAsync(ct));

        var thrown = await Record.ExceptionAsync(async () =>
        {
            var result = await h.RefreshAsync(
                new SyncRefreshOptions
                {
                    BatchSize = 1,
                    DownloadRowLimit = 2,
                    DiscardLocalUnboundedBinaries = true,
                },
                ct
            );
            result.DiscardedChanges.Should().Be(1, "捨てる印のある行の変更を捨てる");
        });

        thrown.Should().BeNull("捨てる印のある行は上限の未送信に数えない");
        var local = await h.ReadLocalAsync(ct);
        local.Folders.Keys.Order().Should().Equal([1, 2, 3, 4], "サーバーの行が全部入る");
        local.Folders[1].Name.Should().Be("f1", "捨てた行はサーバーの内容になる");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "サーバーは変わらない");
        await ShouldMatchServerWithNothingPendingAsync(h, "端末がサーバーの内容になる", ct);
    }

    /// <summary>
    /// 捨てる印のある行を Force なしの洗い替えの取得の途中に書き直すと、その行は未送信に数え、上限を超えた洗い替えを断る
    /// </summary>
    private static async Task DiscardRewrittenDuringFetchCountedForRowLimitAsync(
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
        (await h.Engine.DiscardAllPendingChangesAsync(ct)).Should().Be(1);

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

        // 最初のページ（1 行）を取得した後・適用の前に、捨てる印のあるフォルダー 1 を端末でもう一度書く（記録つき。書くのは
        // 端末の DB）
        var wrote = false;
        h.Faults.ArmInterrupt(
            ModelActor.Device1,
            ModelInterruptPoint.AfterServerFetchBeforeApply,
            ModelActor.Device1,
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
                "書き直した行は捨てる印が効かず、上限を超えた"
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
        (await PendingAsync(h, ct))
            .Should()
            .Equal([(ModelTables.Folders, "1", false, true, false)], "捨てる印は効かない");
        Describe(await h.ReadServerAsync(ct)).Should().Be(serverBefore, "洗い替えは何も送らない");
        (await h.ReadStateTablesAsync(ct))
            .Where(line => line.StartsWith(anchorTable, StringComparison.Ordinal))
            .Should()
            .Equal(
                stateBefore.Where(line => line.StartsWith(anchorTable, StringComparison.Ordinal)),
                "再開点は進まない"
            );
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
    /// <summary>実行の接続の行（1 回の実行が専用の接続を何本開くか・閉じるか・トランザクションの間にロックを持たないか）</summary>
    private static IEnumerable<ScenarioRow<SyncEngineFixedCase>> RunConnectionRows() =>
        [
            Row(
                "run-connection-is-opened-once-per-run",
                "何も無い同期・更新と挿入と削除を混ぜた 5 行を送る同期・6 行を降ろす同期・blob を送って運んで降ろす同期・送信だけ・"
                    + "受信だけ・blob を運ぶ洗い替え・blob を捨てる洗い替え・塊ごとに確定する同期（塊 4 つ）・Collect の競合のある"
                    + "同期のどれも、1 回の実行が専用の接続をちょうど 1 本開き、実行の後にその接続が閉じている",
                RunConnectionOpenedOncePerRunAsync
            ),
            Row(
                "run-connection-is-closed-after-failed-run",
                "取得の失敗で例外になった同期も、専用の接続を 1 本だけ開き、実行の後に閉じている",
                RunConnectionClosedAfterFailedRunAsync
            ),
            Row(
                "run-connection-list-opens-its-own-and-discard-opens-one",
                "未送信の一覧は専用の接続を 1 本開いて閉じ（実行の外で読む）、全部を捨てる口も 1 本開いて閉じる",
                RunConnectionListAndDiscardAsync
            ),
            Row(
                "run-connection-does-not-block-other-writers-between-transactions",
                "同期がサーバーへの書き込みの直前と、取得の後で適用の前に止まっている間、端末の別の接続（Repository・待ち 1 秒）"
                    + "の書き込みは SQLITE_BUSY にならずに通り、次の同期でサーバーへ上がる",
                RunConnectionDoesNotBlockWritersAsync
            ),
            Row(
                "run-connection-temp-table-rotation-in-two-chunks",
                "塊ごとに確定する同期で、2 つの塊のそれぞれが一意な値の入れ替え（回転）を降ろすと、どちらの塊でも入れ替えが"
                    + "端末に入り、4 行の blob は回転の前のまま（同じ接続で回転の一時表と鍵の一時表を 2 回ずつ通る）",
                RunConnectionTempTableRotationInTwoChunksAsync
            ),
        ];

    /// <summary>
    /// 数える口のエンジンで 1 回の実行を流し、専用の接続をちょうど 1 本開いて実行の後に閉じていることを表明する
    /// </summary>
    private static async Task<T> OnceAsync<T>(
        CountingLocalRowStore counter,
        string shape,
        Func<Task<T>> run
    )
    {
        counter.Reset();
        var result = await run();
        AssertOneClosed(counter, shape);

        return result;
    }

    /// <summary>数えた接続がちょうど 1 本で、閉じていることを表明する（失敗の文に形の名前を入れる）</summary>
    private static void AssertOneClosed(CountingLocalRowStore counter, string shape)
    {
        counter.Count.Should().Be(1, $"{shape}：1 回の実行が開く専用の接続は 1 本");
        counter
            .Created.Should()
            .OnlyContain(
                connection => connection.State == System.Data.ConnectionState.Closed,
                $"{shape}：実行の後に専用の接続は閉じている"
            );
    }

    /// <summary>実行の接続：形ごとに、1 回の実行が専用の接続を 1 本だけ開いて閉じる</summary>
    private static async Task RunConnectionOpenedOncePerRunAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        var counter = new CountingLocalRowStore(h.Rows);
        var engine = h.CreateCountedEngine(counter);

        // 何も無い同期
        var empty = await OnceAsync(counter, "何も無い同期", () => h.SyncAsync(null, ct, engine));
        (empty.Uploaded, empty.Downloaded).Should().Be((0, 0));

        // 準備：フォルダー 1 と品目 1〜4 を降ろす
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);

        foreach (var key in new[] { 1, 2, 3, 4 })
        {
            await h.Server.Items.InsertAsync(
                new ModelItemEntity
                {
                    ItemId = key,
                    FolderId = 1,
                    Code = $"c-{key}",
                    Title = $"t{key}",
                    Payload = [0x0A, (byte)key],
                },
                ct
            );
        }

        await h.SyncAsync(WithBlobs, ct);

        // 5 行を送る同期（更新 2・削除 1・挿入 2）
        await SetLocalTitleAsync(h, 1, "u1", ct);
        await SetLocalTitleAsync(h, 2, "u2", ct);
        await h.ExecuteLocalAsync("DELETE FROM model_items WHERE item_id = 3", ct);
        await h.Folders.InsertAsync(new ModelFolderEntity { FolderId = 20, Name = "n20" }, ct);
        await h.Folders.InsertAsync(new ModelFolderEntity { FolderId = 21, Name = "n21" }, ct);
        var sent = await OnceAsync(counter, "5 行を送る同期", () => h.SyncAsync(null, ct, engine));
        sent.Uploaded.Should().Be(5, "5 行を送る同期：5 行が届く");
        sent.Conflicts.Should().BeEmpty();

        // 6 行を降ろす同期
        await InsertServerFoldersAsync(
            h,
            ct,
            (30, null, "s30"),
            (31, null, "s31"),
            (32, null, "s32"),
            (33, null, "s33"),
            (34, null, "s34"),
            (35, null, "s35")
        );
        var received = await OnceAsync(
            counter,
            "6 行を降ろす同期",
            () => h.SyncAsync(null, ct, engine)
        );
        received.Downloaded.Should().Be(6, "6 行を降ろす同期：6 行が降りる");

        // blob を送り、blob を運んで降ろす同期（端末は品目 1 の blob を書き、サーバーは blob を持つ品目 5 を入れる）
        byte[] written = [0xEE, 0x01, 0x02];

        await using (var source = new MemoryStream(written))
        {
            (await h.Items.WritePayloadAsync(1, source, written.Length, ct)).Should().BeTrue();
        }

        await h.Server.Items.InsertAsync(
            new ModelItemEntity
            {
                ItemId = 5,
                FolderId = 1,
                Code = "c-5",
                Title = "t5",
                Payload = [0x0B, 0x05, 0x00],
            },
            ct
        );
        await OnceAsync(
            counter,
            "blob を送って降ろす同期",
            () => h.SyncAsync(WithBlobs, ct, engine)
        );
        var afterBlobs = await h.ReadServerAsync(ct);
        afterBlobs
            .Items[1]
            .PayloadHex.Should()
            .Be(
                Convert.ToHexString(written),
                "blob を送って降ろす同期：端末の blob がサーバーへ届く"
            );
        (await h.ReadLocalAsync(ct))
            .Items[5]
            .PayloadHex.Should()
            .Be(
                afterBlobs.Items[5].PayloadHex,
                "blob を送って降ろす同期：サーバーの blob が端末へ降りる"
            );

        // 送信だけ
        await SetLocalTitleAsync(h, 2, "upload-only", ct);
        var uploaded = await OnceAsync(
            counter,
            "送信だけ",
            () => engine.UploadAsync(new SyncOptions(), new List<SyncConflict>(), ct)
        );
        uploaded.Uploaded.Should().Be(1, "送信だけ：1 行が届く");

        // 受信だけ
        await RenameServerFolderAsync(h, 30, "s30-renamed", ct);
        var downloaded = await OnceAsync(
            counter,
            "受信だけ",
            () => engine.DownloadAsync(new SyncOptions(), ct)
        );
        downloaded.Downloaded.Should().Be(1, "受信だけ：1 行が降りる");

        // blob を運ぶ洗い替え・blob を捨てる洗い替え
        await OnceAsync(
            counter,
            "blob を運ぶ洗い替え",
            () =>
                h.RefreshAsync(new SyncRefreshOptions { IncludeUnboundedBinary = true }, ct, engine)
        );
        await OnceAsync(
            counter,
            "blob を捨てる洗い替え",
            () =>
                h.RefreshAsync(
                    new SyncRefreshOptions { DiscardLocalUnboundedBinaries = true },
                    ct,
                    engine
                )
        );

        // 塊ごとに確定する同期（閾値 2・塊の大きさ 2 で 7 行＝塊 4 つ）
        var chunked = SmallChunks(h.CreateCountedEngine(counter));
        var routes = new List<SyncDownloadRoute>();
        chunked.DownloadRouteObserved = routes.Add;
        await InsertServerFoldersAsync(
            h,
            ct,
            (40, null, "s40"),
            (41, null, "s41"),
            (42, null, "s42"),
            (43, null, "s43"),
            (44, null, "s44"),
            (45, null, "s45"),
            (46, null, "s46")
        );
        var inChunks = await OnceAsync(
            counter,
            "塊ごとに確定する同期",
            () => h.SyncAsync(null, ct, chunked)
        );
        routes
            .Should()
            .Equal([SyncDownloadRoute.Chunked], "塊ごとに確定する同期：塊の道を通る（前提）");
        inChunks.Downloaded.Should().Be(7, "塊ごとに確定する同期：7 行が降りる");

        // Collect の競合のある同期
        await RenameServerFolderAsync(h, 1, "server-edit", ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_folders SET name = 'local-edit' WHERE folder_id = 1",
            ct
        );
        var conflicted = await OnceAsync(
            counter,
            "Collect の競合のある同期",
            () =>
                h.SyncAsync(
                    new SyncOptions { ConflictPolicy = SyncConflictPolicy.Collect },
                    ct,
                    engine
                )
        );
        conflicted
            .Conflicts.Select(conflict => conflict.Reason)
            .Should()
            .Equal(
                [SyncConflictReason.ModifiedOnServer],
                "Collect の競合のある同期：競合が 1 つ（前提）"
            );
    }

    /// <summary>実行の接続：取得の失敗で例外になった同期も、開いた 1 本を閉じる</summary>
    private static async Task RunConnectionClosedAfterFailedRunAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        var counter = new CountingLocalRowStore(h.Rows);
        var engine = h.CreateCountedEngine(
            counter,
            call =>
                call.Table == ModelTables.Folders && call.Call == SyncEngineSourceCall.RowsBefore
                    ? throw new InvalidOperationException(InjectedFetchFailure)
                    : Task.CompletedTask
        );

        var thrown = await Record.ExceptionAsync(() => h.SyncAsync(null, ct, engine));

        thrown.Should().NotBeNull("取得の失敗で同期は例外になる（前提）");
        Chain(thrown!)
            .Select(exception => exception.Message)
            .Should()
            .Contain(InjectedFetchFailure, "注入した取得の失敗が届く（前提）");
        AssertOneClosed(counter, "取得の失敗で終わった同期");
    }

    /// <summary>実行の接続：未送信の一覧は自分の接続を 1 本開いて閉じ、全部を捨てる口は 1 本開いて閉じる</summary>
    private static async Task RunConnectionListAndDiscardAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await SeedPendingAsync(h, ct);
        await SetLocalTitleAsync(h, 1, "local-edit", ct);
        var counter = new CountingLocalRowStore(h.Rows);
        var engine = h.CreateCountedEngine(counter);

        var listed = await OnceAsync(
            counter,
            "未送信の一覧",
            () => engine.GetPendingChangesAsync(ct)
        );
        listed
            .Select(row => (row.TableName, row.KeyText))
            .Should()
            .Equal([(ModelTables.Items, "1")], "未送信の一覧：品目 1 が載る（前提）");

        var discarded = await OnceAsync(
            counter,
            "全部を捨てる口",
            () => engine.DiscardAllPendingChangesAsync(ct)
        );
        discarded.Should().Be(1, "全部を捨てる口：1 行に印を立てる");
    }

    /// <summary>
    /// 実行の接続：同期がサーバー役の点で止まっている間、端末の別の接続の書き込みが待たずに通る（実行はトランザクションの
    /// 外でローカルのロックを持たない）
    /// </summary>
    private static async Task RunConnectionDoesNotBlockWritersAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 2, Name = "f2" }, ct);
        await h.SyncAsync(null, ct);

        // 送る行 2 つと降ろす行 1 つ（取得の後の点は、送った 2 行の決着がコミットされた後に来る）
        await h.ExecuteLocalAsync("UPDATE model_folders SET name = 'l1' WHERE folder_id = 1", ct);
        await h.ExecuteLocalAsync("UPDATE model_folders SET name = 'l2' WHERE folder_id = 2", ct);
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 3, Name = "f3" }, ct);

        var points = new[]
        {
            (Point: ModelInterruptPoint.BeforeServerWrite, Key: 60),
            (Point: ModelInterruptPoint.AfterServerFetchBeforeApply, Key: 61),
        };
        var failures = new List<string>();
        var fired = new List<ModelInterruptPoint>();

        // 1 つ目の点の書き込みの中で 2 つ目の点を仕掛ける（同じ 1 回の同期で両方の点に止まる）
        void Arm(int index)
        {
            var (point, key) = points[index];
            h.Faults.ArmInterrupt(
                ModelActor.Device1,
                point,
                ModelActor.Device1,
                async () =>
                {
                    lock (fired)
                    {
                        fired.Add(point);
                    }

                    try
                    {
                        await h.Folders.InsertAsync(
                            new ModelFolderEntity { FolderId = key, Name = $"w{key}" },
                            CancellationToken.None
                        );
                    }
                    catch (Exception exception)
                    {
                        lock (failures)
                        {
                            failures.Add(
                                $"{point}: {exception.GetType().Name}: {exception.Message}"
                            );
                        }
                    }

                    if (index + 1 < points.Length)
                    {
                        Arm(index + 1);
                    }
                }
            );
        }

        Arm(0);
        SyncResult result;

        try
        {
            result = await h.SyncAsync(null, ct);
        }
        finally
        {
            h.Faults.Syncing = null;
        }

        fired
            .Should()
            .Equal(points.Select(entry => entry.Point), "2 つの点の両方で止まった（前提）");
        failures
            .Should()
            .BeEmpty(
                "止まっている同期はトランザクションの外にあり、別の接続の書き込みを待たせない"
            );
        result.Uploaded.Should().Be(2);
        result.Conflicts.Should().BeEmpty();

        var next = await h.SyncAsync(null, ct);
        next.Uploaded.Should().Be(2, "止まっている間に書いた 2 行は次の同期で上がる");
        var server = await h.ReadServerAsync(ct);
        server.Folders.Should().ContainKeys(60, 61);
        Describe(await h.ReadLocalAsync(ct)).Should().Be(Describe(server));
    }

    /// <summary>
    /// 実行の接続の一時表：塊ごとに確定する同期の 2 つの塊が、それぞれ一意な値の入れ替えを回転で降ろす（回転の一時表
    /// <c>quicker_sync_rotate</c> と鍵の一時表 <c>quicker_sync_stored_keys</c> を、同じ接続で別々のトランザクションから 2 回通る）
    /// </summary>
    private static async Task RunConnectionTempTableRotationInTwoChunksAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await h.Server.Folders.InsertAsync(new ModelFolderEntity { FolderId = 1, Name = "f1" }, ct);

        foreach (var key in new[] { 1, 2, 3, 4 })
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
        var before = await h.ReadLocalAsync(ct);

        // サーバーで 1・2 と 3・4 の code を入れ替える。取得の順（版の順）は 2・1・4・3 で、塊の大きさ 2 の塊は [2, 1] と [4, 3]
        await SetServerCodeAsync(h, 1, "tmp-a", ct);
        await SetServerCodeAsync(h, 2, "c-1", ct);
        await SetServerCodeAsync(h, 1, "c-2", ct);
        await SetServerCodeAsync(h, 3, "tmp-b", ct);
        await SetServerCodeAsync(h, 4, "c-3", ct);
        await SetServerCodeAsync(h, 3, "c-4", ct);

        var engine = SmallChunks(h.CreateEngine());
        var routes = new List<SyncDownloadRoute>();
        engine.DownloadRouteObserved = routes.Add;

        var result = await h.SyncAsync(WithoutBlobs, ct, engine);

        routes.Should().Equal([SyncDownloadRoute.Chunked], "塊の道を通る（前提）");
        result.Conflicts.Should().BeEmpty();
        result.Truncations.Should().BeEmpty("どちらの塊の入れ替えも、その塊の回転で降りる");
        var local = await h.ReadLocalAsync(ct);
        (local.Items[1].Code, local.Items[2].Code, local.Items[3].Code, local.Items[4].Code)
            .Should()
            .Be(("c-2", "c-1", "c-4", "c-3"), "2 つの入れ替えが端末に入る");

        foreach (var key in new[] { 1, 2, 3, 4 })
        {
            local
                .Items[key]
                .PayloadHex.Should()
                .Be(before.Items[key].PayloadHex, $"品目 {key} の blob は回転の前のまま");
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
