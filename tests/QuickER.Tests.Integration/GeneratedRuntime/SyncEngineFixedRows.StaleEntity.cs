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
using V = QuickER.Tests.GeneratedSyncVoFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 表「新しいエンジンの決まった形の行」のうち、古い読みの保存（同期が他者の内容を降ろす前に読んだ実体を、降ろした後に
/// SQLite の Repository で保存する書き込み）を送信の段が見分ける形と見分けない形の行
/// </summary>
/// <remarks>
/// SQLite の Repository の UPDATE は版を照合せず、版の列を普通の列として書くので、古い実体の保存は端末の行の版の列を古い版へ
/// 戻す。版で守る送信の段は、端末の行が持つ版が、エンジンがその行へ最後に入れた他者の内容の版より古ければ、その版を元の版に
/// して送り、サーバーの版の照合が ModifiedOnServer を返す。端末自身が上げた版・同じ版の書き直し・強制の上書きの後は、他者の
/// 内容に数えない。
/// </remarks>
internal static partial class SyncEngineFixedRows
{
    /// <summary>古い読みの保存の後に、エンジンが他者の内容を入れる経路</summary>
    private enum StaleApply
    {
        /// <summary>塊ごとに確定する同期の取得</summary>
        ChunkedDownload,

        /// <summary>洗い替え（端末の基準の版と違う版の行を持ってくる）</summary>
        Refresh,

        /// <summary>ServerWins が端末の未送信の編集を捨てて、他者の内容で戻す</summary>
        ServerWinsPutBack,
    }

    private static IEnumerable<ScenarioRow<SyncEngineFixedCase>> StaleEntityRows() =>
        [
            Row(
                "stale-entity-saved-after-download-collect-reports-modified-on-server",
                "同期がサーバーの他者の内容を降ろした後に、降ろす前に読んだ古い実体を Repository で保存すると、Collect の同期は"
                    + "ModifiedOnServer を報告し、サーバーの他者の内容を上書きしない。行は未送信のまま残り、次の Collect でも報告する。"
                    + "対照：降ろす前に保存した古い読みの編集も ModifiedOnServer になる",
                (h, ct) => StaleAfterDownloadAsync(h, SyncConflictPolicy.Collect, ct)
            ),
            Row(
                "stale-entity-saved-after-download-serverwins-restores-server-content",
                "同期がサーバーの他者の内容を降ろした後の古い実体の保存は、ServerWins の同期が捨てて（DiscardedByServerWins）、"
                    + "端末の行をサーバーの内容とサーバーの版に戻す。続く同期は何も送らず何も降ろさない",
                (h, ct) => StaleAfterDownloadAsync(h, SyncConflictPolicy.ServerWins, ct)
            ),
            Row(
                "stale-entity-saved-after-download-localwins-uploads-then-resave-is-not-a-conflict",
                "同期がサーバーの他者の内容を降ろした後の古い実体の保存は、LocalWins の同期が上書きで上げる。その後に同じ古い"
                    + "実体をもう一度保存した Collect の同期は、強制の上書きの後なので競合なしに上がる",
                (h, ct) => StaleAfterDownloadAsync(h, SyncConflictPolicy.LocalWins, ct)
            ),
            Row(
                "stale-entity-saved-after-download-last-write-wins-uploads-without-conflict",
                "後勝ちの同期は古い読みの保存を見分けず、降ろした他者の内容を競合なしに上書きする",
                StaleAfterDownloadLastWriteWinsAsync
            ),
            .. new[]
            {
                StaleApply.ChunkedDownload,
                StaleApply.Refresh,
                StaleApply.ServerWinsPutBack,
            }.Select(apply =>
                Row(
                    $"stale-entity-saved-after-{StaleApplyName(apply)}-collect-reports-modified-on-server",
                    $"{StaleApplyDescription(apply)}の後に、その前に読んだ古い実体を保存すると、Collect の同期は "
                        + "ModifiedOnServer を報告し、サーバーの他者の内容を上書きしない（行は未送信のまま残る）",
                    (h, ct) => StaleAfterApplyAsync(h, apply, ct)
                )
            ),
            Row(
                "stale-entity-saved-after-own-upload-is-not-a-conflict",
                "編集して同期で上げた後に、同期の前に読んだ同じ実体をもう一度編集して保存した Collect の同期は、端末自身が"
                    + "上げた版を他者の内容に数えないので、競合なしに上がる",
                StaleAfterOwnUploadAsync
            ),
            Row(
                "stale-entity-read-between-own-uploads-is-not-a-conflict",
                "端末が 2 回上げた間に読んだ実体（1 回目の版を持つ）を、2 回目の後に保存した Collect の同期は、どちらの版も端末"
                    + "自身のものなので競合なしに上がる",
                StaleReadBetweenOwnUploadsAsync
            ),
            Row(
                "stale-entity-saved-after-refresh-rewrote-own-upload-is-not-a-conflict",
                "端末が上げた版をサーバーが変えないまま洗い替えが同じ版で書き直した後に、上げる前に読んだ実体を保存した Collect "
                    + "の同期は、同じ版の書き直しを他者の内容に数えないので、競合なしに上がる",
                StaleAfterRefreshOfOwnUploadAsync
            ),
            Row(
                "stale-entity-saved-after-serverwins-put-back-own-upload-is-not-a-conflict",
                "端末が上げた行の次の編集を、サーバーが変えていないまま ServerWins の同期が捨てて同じ版で戻した後に、上げる前に"
                    + "読んだ実体を保存した Collect の同期は、競合なしに上がる",
                StaleAfterServerWinsPutBackOwnUploadAsync
            ),
            Row(
                "stale-entity-read-before-other-content-saved-after-later-own-upload-collect-reports-modified-on-server",
                "他者の内容が降りた後に、それを読んだ実体の編集を端末が上げても、他者の内容より前に読んだ実体の保存は Collect "
                    + "の同期で ModifiedOnServer になる（端末自身の後の書き込みで、他者の内容の記録を忘れない）",
                StaleBeforeOtherContentAfterOwnUploadAsync
            ),
            Row(
                "stale-entity-saved-then-raw-sql-update-collect-reports-modified-on-server",
                "古い実体の保存の後に、版の列を書かない生の SQL の UPDATE が同じ行に続いても、行は古い版を持ったままなので、"
                    + "Collect の同期は ModifiedOnServer を報告する",
                StaleThenRawSqlUpdateAsync
            ),
            Row(
                "stale-entity-delete-after-download-is-not-distinguished-and-deletes-server-row",
                "見分けない形（今の振る舞いの固定）：他者の内容が降りた後に、古い実体を MarkRemoved してグラフ保存した行と、"
                    + "キーで DeleteAsync した行は、基準の版で守った削除がそのまま通り、サーバーの行が消える（競合なし）",
                StaleDeleteAfterDownloadAsync
            ),
            Row(
                "stale-entity-without-version-saved-after-download-is-not-distinguished-and-uploads",
                "見分けない形（今の振る舞いの固定）：端末で作って保存した版を持たない実体を、上げて他者の内容が降りた後に"
                    + "もう一度保存すると、Collect の同期は競合なしに上がり、他者の内容を上書きする",
                StaleWithoutVersionAfterDownloadAsync
            ),
            Row(
                "stale-entity-saved-after-failed-settle-and-serverwins-put-back-collect-reports-modified-on-server",
                "サーバーへの書き込みの後に送信の決着が失敗し、次の ServerWins の同期がサーバーの行（端末自身の内容）で戻した後に、"
                    + "落ちる前の保存に使った実体を保存すると、Collect の同期は ModifiedOnServer を報告する（サーバーの内容が端末"
                    + "自身のものでも、決着の無い版は他者の内容に数える）",
                StaleAfterFailedSettleAsync
            ),
            Row(
                "stale-entity-saved-after-download-value-object-version-collect-reports-modified-on-server",
                "SyncVoFixture の端末（値オブジェクトの版の列）で、他者の内容が降りた後に古い実体を保存すると、Collect の同期は"
                    + "ModifiedOnServer を報告し、サーバーの他者の内容を上書きしない",
                StaleValueObjectVersionAsync
            ),
            Row(
                "stale-entity-version-order-reads-versions-as-big-endian-numbers",
                "古い読みの判定が使う版の新旧の比べ方は、版を big-endian の符号なし整数として読む：先頭の 0 のバイトは数えず、"
                    + "残りが短いほうが古く、同じ長さならバイトの辞書式で比べる。同じ値は古くない",
                (_, _) =>
                {
                    VersionOrderShouldReadBigEndianNumbers();

                    return Task.CompletedTask;
                }
            ),
        ];

    /// <summary>
    /// 版の新旧の比べ方（古い読みの判定が使う 1 か所）を、長さの違う版・先頭に 0 のある版・同じ値の版の組で確かめる。
    /// サーバー役の版はどれも 8 バイトなので、長さの枝と先頭の 0 の扱いは、同期を流す行からは見えない
    /// </summary>
    private static void VersionOrderShouldReadBigEndianNumbers()
    {
        (byte[] Version, byte[] Than, bool Older, string Why)[] pairs =
        [
            ([0, 0, 0, 1], [0, 0, 0, 2], true, "同じ長さは小さいほうが古い"),
            ([0, 0, 0, 2], [0, 0, 0, 1], false, "同じ長さの大きいほうは古くない"),
            ([0, 0, 0, 2], [0, 0, 0, 2], false, "同じ値は古くない"),
            ([0xFF], [1, 0], true, "255 は 256 より古い（短いほうが古い）"),
            ([1, 0], [0xFF], false, "256 は 255 より古くない（先頭のバイトだけで比べない）"),
            ([0, 0, 5], [6], true, "先頭の 0 は数えない（5 は 6 より古い）"),
            ([6], [0, 0, 5], false, "先頭の 0 は数えない（6 は 5 より古くない）"),
            ([0, 0, 5], [5], false, "先頭の 0 だけが違う版は同じ値"),
            ([5], [0, 0, 5], false, "先頭の 0 だけが違う版は同じ値（逆向き）"),
            (
                [0, 0, 0, 0, 0, 0, 0, 0xFF],
                [0, 0, 0, 0, 0, 0, 1, 0],
                true,
                "8 バイトの 255 は 256 より古い"
            ),
            (
                [0, 0, 0, 0, 0, 0, 1, 0],
                [0, 0, 0, 0, 0, 0, 0, 0xFF],
                false,
                "8 バイトの 256 は 255 より古くない"
            ),
            ([], [1], true, "空の版は 0"),
            ([], [0], false, "空の版と 0 は同じ値"),
        ];

        foreach (var (version, than, older, why) in pairs)
        {
            SyncState
                .IsOlderVersion(version, than)
                .Should()
                .Be(older, $"{Convert.ToHexString(version)} と {Convert.ToHexString(than)}：{why}");
        }
    }

    private static string StaleApplyName(StaleApply apply) =>
        apply switch
        {
            StaleApply.ChunkedDownload => "chunked-download",
            StaleApply.Refresh => "refresh-brought-other-content",
            StaleApply.ServerWinsPutBack => "serverwins-put-back-other-content",
            _ => throw new ArgumentOutOfRangeException(nameof(apply)),
        };

    private static string StaleApplyDescription(StaleApply apply) =>
        apply switch
        {
            StaleApply.ChunkedDownload => "塊ごとに確定する同期がサーバーの他者の内容を降ろした",
            StaleApply.Refresh =>
                "洗い替えがサーバーの他者の内容（端末の基準の版と違う版）を持ってきた",
            StaleApply.ServerWinsPutBack =>
                "端末の未送信の編集を ServerWins の同期が捨て、サーバーの他者が変えていた内容で戻した",
            _ => throw new ArgumentOutOfRangeException(nameof(apply)),
        };

    /// <summary>競合の表・キー・理由（キーの順）</summary>
    private static IEnumerable<(string Table, string Key, SyncConflictReason Reason)> Reasons(
        SyncResult result
    ) =>
        result
            .Conflicts.Select(conflict => (conflict.TableName, conflict.KeyText, conflict.Reason))
            .OrderBy(conflict => conflict.KeyText, StringComparer.Ordinal);

    /// <summary>端末のフォルダーの版の列（16 進。NULL は null）</summary>
    private static Task<string?> LocalFolderVersionAsync(
        SyncEngineFixedHarness h,
        int id,
        CancellationToken ct
    ) =>
        h.ScalarAsync(
            $"SELECT CASE WHEN row_ver IS NULL THEN NULL ELSE hex(row_ver) END FROM model_folders WHERE folder_id = {id}",
            ct
        );

    /// <summary>端末のフォルダーの名前</summary>
    private static async Task<string> LocalFolderNameAsync(
        SyncEngineFixedHarness h,
        int id,
        CancellationToken ct
    ) => (await h.ReadLocalAsync(ct)).Folders[id].Name;

    /// <summary>サーバーのフォルダーの名前</summary>
    private static async Task<string> ServerFolderNameAsync(
        SyncEngineFixedHarness h,
        int id,
        CancellationToken ct
    ) => (await h.ReadServerAsync(ct)).Folders[id].Name;

    /// <summary>未送信の一覧のフォルダーのキー（序数の順）</summary>
    private static async Task<IReadOnlyList<string>> PendingFolderKeysAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    ) =>
        (await h.Engine.GetPendingChangesAsync(ct))
            .Where(row => row.TableName == ModelTables.Folders)
            .Select(row => row.KeyText)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>端末の Repository でフォルダーを読む（アプリが手元に持つ実体）</summary>
    private static async Task<ModelFolderEntity> ReadFolderAsync(
        SyncEngineFixedHarness h,
        int id,
        CancellationToken ct
    ) => (await h.Folders.GetByIdAsync(id, ct))!;

    /// <summary>手元の実体の名前を変えて、端末の Repository の UpdateAsync で保存する（版を照合せずに成功する）</summary>
    private static async Task SaveFolderAsync(
        SyncEngineFixedHarness h,
        ModelFolderEntity entity,
        string name,
        CancellationToken ct
    )
    {
        entity.Name = name;
        (await h.Folders.UpdateAsync(entity, cancellationToken: ct))
            .Should()
            .BeTrue("SQLite の Repository の UpdateAsync は版を照合せずに成功する");
    }

    /// <summary>
    /// 古い実体の保存が Collect の同期で ModifiedOnServer になる：保存すると端末の行は古い実体の版を持ち、Collect の同期は
    /// その行の ModifiedOnServer だけを報告し、何も上げず、サーバーは他者の内容のまま、行は未送信のまま残る
    /// </summary>
    private static async Task ShouldReportStaleSaveAsync(
        SyncEngineFixedHarness h,
        ModelFolderEntity stale,
        string serverName,
        CancellationToken ct
    )
    {
        var staleVersion = Convert.ToHexString(stale.RowVer!);
        await SaveFolderAsync(h, stale, "stale-1", ct);
        (await LocalFolderVersionAsync(h, 1, ct))
            .Should()
            .Be(staleVersion, "端末の行の版の列は古い実体の版へ戻る（前提）");

        var result = await h.SyncAsync(null, ct);

        Reasons(result)
            .Should()
            .Equal([(ModelTables.Folders, "1", SyncConflictReason.ModifiedOnServer)]);
        result.Uploaded.Should().Be(0, "古い読みの保存は上がらない");
        (await ServerFolderNameAsync(h, 1, ct))
            .Should()
            .Be(serverName, "サーバーの他者の内容は上書きされない");
        (await LocalFolderNameAsync(h, 1, ct)).Should().Be("stale-1", "端末の編集は残る");
        (await PendingFolderKeysAsync(h, ct)).Should().Equal(["1"], "行は未送信のまま残る");
    }

    /// <summary>
    /// 降ろした後の古い実体の保存：端末とサーバーにフォルダー 1・2 があり、端末のアプリが 1・2 を読んで手元に持つ。サーバーの
    /// 他者が 1・2 を s1・s2 へ書き直し、同期が降ろした後に 1 の古い実体を保存して、指定のポリシーの同期を流す（Collect では
    /// 対照として、2 の古い実体を降ろす前に保存する）
    /// </summary>
    private static async Task StaleAfterDownloadAsync(
        SyncEngineFixedHarness h,
        SyncConflictPolicy policy,
        CancellationToken ct
    )
    {
        var collect = policy == SyncConflictPolicy.Collect;
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"), (2, null, "f2"));
        await h.SyncAsync(null, ct);
        var stale1 = await ReadFolderAsync(h, 1, ct);
        var stale2 = await ReadFolderAsync(h, 2, ct);
        await RenameServerFolderAsync(h, 1, "s1", ct);
        await RenameServerFolderAsync(h, 2, "s2", ct);

        if (collect)
        {
            // 対照：降ろす前に保存した古い読みの編集
            await SaveFolderAsync(h, stale2, "local-2", ct);
        }

        var first = await h.SyncAsync(null, ct);

        Reasons(first)
            .Should()
            .Equal(
                collect
                    ? new[] { (ModelTables.Folders, "2", SyncConflictReason.ModifiedOnServer) }
                    : []
            );
        (await LocalFolderNameAsync(h, 1, ct)).Should().Be("s1", "他者の内容が降りる");
        (await LocalFolderVersionAsync(h, 1, ct))
            .Should()
            .Be(await ServerFolderVersionAsync(h, 1, ct), "端末の行は他者の内容の版になる");

        // 降ろした後に、手元の古い実体を保存する
        await SaveFolderAsync(h, stale1, "stale-1", ct);
        (await LocalFolderVersionAsync(h, 1, ct))
            .Should()
            .Be(Convert.ToHexString(stale1.RowVer!), "端末の行の版の列は古い実体の版へ戻る");

        var second = await h.SyncAsync(new SyncOptions { ConflictPolicy = policy }, ct);

        switch (policy)
        {
            case SyncConflictPolicy.Collect:
                foreach (
                    var (run, attempt) in new[] { (second, 1), (await h.SyncAsync(null, ct), 2) }
                )
                {
                    Reasons(run)
                        .Should()
                        .Equal(
                            [
                                (ModelTables.Folders, "1", SyncConflictReason.ModifiedOnServer),
                                (ModelTables.Folders, "2", SyncConflictReason.ModifiedOnServer),
                            ],
                            $"{attempt} 回目の Collect は古い読みの保存（1）と対照（2）の両方を報告する"
                        );
                    run.Uploaded.Should().Be(0);
                }

                (await ServerFolderNameAsync(h, 1, ct))
                    .Should()
                    .Be("s1", "他者の内容は上書きされない");
                (await ServerFolderNameAsync(h, 2, ct)).Should().Be("s2");
                (await LocalFolderNameAsync(h, 1, ct)).Should().Be("stale-1");
                (await PendingFolderKeysAsync(h, ct)).Should().Equal(["1", "2"]);
                break;

            case SyncConflictPolicy.ServerWins:
                second.Conflicts.Should().BeEmpty();
                second.Uploaded.Should().Be(0, "ServerWins は端末の編集を送らない");
                second
                    .LocalRowChanges.Should()
                    .Contain(
                        new SyncLocalRowChange(
                            ModelTables.Folders,
                            "1",
                            SyncLocalRowChangeReason.DiscardedByServerWins
                        )
                    );
                (await LocalFolderNameAsync(h, 1, ct))
                    .Should()
                    .Be("s1", "端末の行はサーバーの内容に戻る");
                (await LocalFolderVersionAsync(h, 1, ct))
                    .Should()
                    .Be(await ServerFolderVersionAsync(h, 1, ct), "戻した行の版の列はサーバーの版");
                (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
                var after = await h.SyncAsync(null, ct);
                after.Conflicts.Should().BeEmpty();
                after.Uploaded.Should().Be(0, "戻した行を送らない");
                after.Downloaded.Should().Be(0);
                break;

            default:
                second.Conflicts.Should().BeEmpty("LocalWins は上書きで決着する");
                second.Uploaded.Should().Be(1);
                (await ServerFolderNameAsync(h, 1, ct))
                    .Should()
                    .Be("stale-1", "端末の内容がサーバーへ上がる");
                (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();

                // 強制の上書きの後は、同じ古い実体をもう一度保存しても競合にしない
                await SaveFolderAsync(h, stale1, "stale-1-again", ct);
                var again = await h.SyncAsync(null, ct);
                again.Conflicts.Should().BeEmpty("強制の上書きの後は他者の内容の記録が無い");
                again.Uploaded.Should().Be(1);
                (await ServerFolderNameAsync(h, 1, ct)).Should().Be("stale-1-again");
                (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
                break;
        }
    }

    /// <summary>後勝ちの同期は、降ろした後の古い実体の保存を見分けずに上げる</summary>
    private static async Task StaleAfterDownloadLastWriteWinsAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.SyncAsync(null, ct);
        var stale = await ReadFolderAsync(h, 1, ct);
        await RenameServerFolderAsync(h, 1, "s1", ct);
        await h.SyncAsync(null, ct);
        (await LocalFolderNameAsync(h, 1, ct)).Should().Be("s1", "他者の内容が降りる（前提）");
        await SaveFolderAsync(h, stale, "stale-1", ct);

        var result = await h.SyncAsync(new SyncOptions { Mode = SyncMode.LastWriteWins }, ct);

        result.Conflicts.Should().BeEmpty("後勝ちは見分けない");
        result.Uploaded.Should().Be(1);
        (await ServerFolderNameAsync(h, 1, ct)).Should().Be("stale-1", "他者の内容を上書きする");
        (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 他者の内容を入れる経路ごとの古い実体の保存：端末とサーバーにフォルダーがあり、端末のアプリがフォルダー 1 を読んで手元に
    /// 持つ。サーバーの他者が 1 を書き直し、指定の経路がその内容を端末へ入れた後に、手元の古い実体を保存して Collect の同期を流す
    /// </summary>
    private static async Task StaleAfterApplyAsync(
        SyncEngineFixedHarness h,
        StaleApply apply,
        CancellationToken ct
    )
    {
        var ids = apply == StaleApply.ChunkedDownload ? new[] { 1, 2, 3, 4 } : [1];
        await InsertServerFoldersAsync(h, ct, [.. ids.Select(id => (id, (int?)null, $"f{id}"))]);
        await h.SyncAsync(null, ct);
        var stale = await ReadFolderAsync(h, 1, ct);

        foreach (var id in ids)
        {
            await RenameServerFolderAsync(h, id, $"s{id}", ct);
        }

        switch (apply)
        {
            case StaleApply.ChunkedDownload:
            {
                var engine = SmallChunks(h.CreateEngine());
                var routes = new List<SyncDownloadRoute>();
                engine.DownloadRouteObserved = routes.Add;
                var chunked = await h.SyncAsync(null, ct, engine);
                routes.Should().Equal([SyncDownloadRoute.Chunked], "同期は塊の道を通る（前提）");
                chunked.Downloaded.Should().Be(4);
                break;
            }

            case StaleApply.Refresh:
                await h.RefreshAsync(RefreshWithBlobs, ct);
                break;

            default:
            {
                var edit = await ReadFolderAsync(h, 1, ct);
                await SaveFolderAsync(h, edit, "local-edit", ct);
                var putBack = await h.SyncAsync(
                    new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
                    ct
                );
                putBack
                    .LocalRowChanges.Should()
                    .Contain(
                        new SyncLocalRowChange(
                            ModelTables.Folders,
                            "1",
                            SyncLocalRowChangeReason.DiscardedByServerWins
                        )
                    );
                break;
            }
        }

        (await LocalFolderNameAsync(h, 1, ct)).Should().Be("s1", "他者の内容が端末に入る（前提）");
        await ShouldReportStaleSaveAsync(h, stale, "s1", ct);
    }

    /// <summary>編集して上げた後に、同期の前に読んだ同じ実体をもう一度保存する</summary>
    private static async Task StaleAfterOwnUploadAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.SyncAsync(null, ct);
        var entity = await ReadFolderAsync(h, 1, ct);
        await SaveFolderAsync(h, entity, "mine", ct);
        var first = await h.SyncAsync(null, ct);
        first.Conflicts.Should().BeEmpty();
        first.Uploaded.Should().Be(1);

        await SaveFolderAsync(h, entity, "mine-2", ct);
        var second = await h.SyncAsync(null, ct);

        second.Conflicts.Should().BeEmpty("端末自身が上げた版は他者の内容でない");
        second.Uploaded.Should().Be(1);
        (await ServerFolderNameAsync(h, 1, ct)).Should().Be("mine-2");
        (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
    }

    /// <summary>端末が 2 回上げた間に読んだ実体を、2 回目の後に保存する</summary>
    private static async Task StaleReadBetweenOwnUploadsAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.SyncAsync(null, ct);
        await SaveFolderAsync(h, await ReadFolderAsync(h, 1, ct), "first", ct);
        (await h.SyncAsync(null, ct)).Uploaded.Should().Be(1);
        var between = await ReadFolderAsync(h, 1, ct);
        await SaveFolderAsync(h, await ReadFolderAsync(h, 1, ct), "second", ct);
        (await h.SyncAsync(null, ct)).Uploaded.Should().Be(1);
        Convert
            .ToHexString(between.RowVer!)
            .Should()
            .NotBe(
                await ServerFolderVersionAsync(h, 1, ct),
                "間に読んだ実体の版は 1 回目の版（前提）"
            );

        await SaveFolderAsync(h, between, "between", ct);
        var result = await h.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty("どちらの版も端末自身が上げたもの");
        result.Uploaded.Should().Be(1);
        (await ServerFolderNameAsync(h, 1, ct)).Should().Be("between");
        (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
    }

    /// <summary>端末が上げた版を洗い替えが同じ版で書き直した後に、上げる前に読んだ実体を保存する</summary>
    private static async Task StaleAfterRefreshOfOwnUploadAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.SyncAsync(null, ct);
        var stale = await ReadFolderAsync(h, 1, ct);
        await SaveFolderAsync(h, await ReadFolderAsync(h, 1, ct), "mine", ct);
        (await h.SyncAsync(null, ct)).Uploaded.Should().Be(1);
        var uploaded = await ServerFolderVersionAsync(h, 1, ct);
        await h.RefreshAsync(RefreshWithBlobs, ct);
        (await LocalFolderNameAsync(h, 1, ct)).Should().Be("mine");
        (await ServerFolderVersionAsync(h, 1, ct))
            .Should()
            .Be(uploaded, "サーバーは変わっていない（洗い替えは同じ版の書き直し）");

        await SaveFolderAsync(h, stale, "stale", ct);
        var result = await h.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty("同じ版の書き直しは他者の内容でない");
        result.Uploaded.Should().Be(1);
        (await ServerFolderNameAsync(h, 1, ct)).Should().Be("stale");
        (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 端末が上げた行の次の編集を、サーバーが変えていないまま ServerWins の同期が捨てて同じ版で戻した後に、上げる前に読んだ実体を
    /// 保存する
    /// </summary>
    private static async Task StaleAfterServerWinsPutBackOwnUploadAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.SyncAsync(null, ct);
        var stale = await ReadFolderAsync(h, 1, ct);
        await SaveFolderAsync(h, await ReadFolderAsync(h, 1, ct), "mine", ct);
        (await h.SyncAsync(null, ct)).Uploaded.Should().Be(1);
        await SaveFolderAsync(h, await ReadFolderAsync(h, 1, ct), "discarded", ct);
        var putBack = await h.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            ct
        );
        putBack
            .LocalRowChanges.Should()
            .Contain(
                new SyncLocalRowChange(
                    ModelTables.Folders,
                    "1",
                    SyncLocalRowChangeReason.DiscardedByServerWins
                )
            );
        (await LocalFolderNameAsync(h, 1, ct))
            .Should()
            .Be("mine", "端末の行は変わっていないサーバーの内容に戻る");

        await SaveFolderAsync(h, stale, "stale", ct);
        var result = await h.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty("同じ版で戻した行は他者の内容でない");
        result.Uploaded.Should().Be(1);
        (await ServerFolderNameAsync(h, 1, ct)).Should().Be("stale");
        (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 他者の内容より前に読んだ実体 A と、他者の内容を読んだ実体 B：B の編集を端末が上げた後に、A を保存する
    /// </summary>
    private static async Task StaleBeforeOtherContentAfterOwnUploadAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.SyncAsync(null, ct);
        var before = await ReadFolderAsync(h, 1, ct);
        await RenameServerFolderAsync(h, 1, "s1", ct);
        (await h.SyncAsync(null, ct)).Downloaded.Should().Be(1);
        var afterOther = await ReadFolderAsync(h, 1, ct);
        await SaveFolderAsync(h, afterOther, "b-edit", ct);
        var own = await h.SyncAsync(null, ct);
        own.Conflicts.Should().BeEmpty("他者の内容を読んだ実体の編集は上がる");
        own.Uploaded.Should().Be(1);

        await ShouldReportStaleSaveAsync(h, before, "b-edit", ct);
    }

    /// <summary>古い実体の保存の後に、版の列を書かない生の SQL の UPDATE が同じ行に続く</summary>
    private static async Task StaleThenRawSqlUpdateAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.SyncAsync(null, ct);
        var stale = await ReadFolderAsync(h, 1, ct);
        await RenameServerFolderAsync(h, 1, "s1", ct);
        await h.SyncAsync(null, ct);
        await SaveFolderAsync(h, stale, "stale-1", ct);
        await h.ExecuteLocalAsync("UPDATE model_folders SET name = 'raw' WHERE folder_id = 1", ct);
        (await LocalFolderVersionAsync(h, 1, ct))
            .Should()
            .Be(Convert.ToHexString(stale.RowVer!), "生の SQL は版の列を書かない（前提）");

        var result = await h.SyncAsync(null, ct);

        Reasons(result)
            .Should()
            .Equal([(ModelTables.Folders, "1", SyncConflictReason.ModifiedOnServer)]);
        result.Uploaded.Should().Be(0);
        (await ServerFolderNameAsync(h, 1, ct)).Should().Be("s1");
        (await LocalFolderNameAsync(h, 1, ct)).Should().Be("raw");
        (await PendingFolderKeysAsync(h, ct)).Should().Equal(["1"]);
    }

    /// <summary>
    /// 見分けない形：他者の内容が降りた後に、古い実体 1 を MarkRemoved してグラフ保存し、2 をキーで DeleteAsync で消す
    /// </summary>
    /// <remarks>
    /// 端末の行が無い削除は、行の状態の表の基準の版（降ろした他者の内容の版）で守ってサーバーへ送るので、古い実体で消したかを
    /// 見分けない。
    /// </remarks>
    private static async Task StaleDeleteAfterDownloadAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"), (2, null, "f2"));
        await h.SyncAsync(null, ct);
        var stale1 = await ReadFolderAsync(h, 1, ct);
        await RenameServerFolderAsync(h, 1, "s1", ct);
        await RenameServerFolderAsync(h, 2, "s2", ct);
        (await h.SyncAsync(null, ct)).Downloaded.Should().Be(2);

        stale1.MarkRemoved();
        (await h.Folders.SaveAsync(stale1, cancellationToken: ct)).Should().Be(1);
        (await h.Folders.DeleteAsync(2, ct)).Should().BeTrue();

        var result = await h.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty("基準の版で守った削除は通る");
        result.Uploaded.Should().Be(2);
        var server = await h.ReadServerAsync(ct);
        server.Folders.Should().NotContainKey(1, "古い実体で消した行もサーバーから消える");
        server.Folders.Should().NotContainKey(2);
        (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 見分けない形：端末で作って保存した版を持たない実体を、上げて他者の内容が降りた後にもう一度保存する
    /// </summary>
    /// <remarks>
    /// SQLite の Repository の挿入は実体に版を書かないので、実体は版を持たないまま残り、保存は端末の行の版の列を NULL にする。
    /// 送信の段は、行が版を持たないときは基準の版で守るので、古い読みかを見分けない。
    /// </remarks>
    private static async Task StaleWithoutVersionAfterDownloadAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        var created = new ModelFolderEntity { FolderId = 7, Name = "mine" };
        await h.Folders.InsertAsync(created, ct);
        (await h.SyncAsync(null, ct)).Uploaded.Should().Be(1);
        created.RowVer.Should().BeNull("挿入した実体は版を持たないまま（前提）");
        await RenameServerFolderAsync(h, 7, "other", ct);
        (await h.SyncAsync(null, ct)).Downloaded.Should().Be(1);
        (await LocalFolderNameAsync(h, 7, ct)).Should().Be("other", "他者の内容が降りる（前提）");

        created.Name = "again";
        (await h.Folders.UpdateAsync(created, cancellationToken: ct)).Should().BeTrue();
        (await LocalFolderVersionAsync(h, 7, ct))
            .Should()
            .BeNull("保存は端末の行の版の列を NULL にする");
        var result = await h.SyncAsync(null, ct);

        result.Conflicts.Should().BeEmpty("版を持たない保存は見分けない");
        result.Uploaded.Should().Be(1);
        (await ServerFolderNameAsync(h, 7, ct)).Should().Be("again", "他者の内容を上書きする");
        (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 送信の決着の前に落ちた実行の後：決着の前の失敗でサーバーには端末の編集が届き、次の ServerWins の同期がその行で戻した後に、
    /// 落ちる前の保存に使った実体をもう一度保存する
    /// </summary>
    private static async Task StaleAfterFailedSettleAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));
        await h.SyncAsync(null, ct);
        var entity = await ReadFolderAsync(h, 1, ct);
        await SaveFolderAsync(h, entity, "local-edit", ct);

        // 別の接続が書き込みのロックを持ち続ける間に同期する：サーバーへの書き込みは通り、決着が SQLITE_BUSY になる
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
            .Contain(ex => ex.SqliteErrorCode == 5, "送信の決着は SQLITE_BUSY で失敗する（前提）");
        (await ServerFolderNameAsync(h, 1, ct))
            .Should()
            .Be("local-edit", "サーバーへの書き込みは通っている");

        var putBack = await h.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            ct
        );
        putBack.Conflicts.Should().BeEmpty();
        (await LocalFolderNameAsync(h, 1, ct)).Should().Be("local-edit");
        (await LocalFolderVersionAsync(h, 1, ct))
            .Should()
            .Be(await ServerFolderVersionAsync(h, 1, ct), "戻した行の版の列はサーバーの版");
        (await PendingFolderKeysAsync(h, ct)).Should().BeEmpty();

        await SaveFolderAsync(h, entity, "again", ct);
        var result = await h.SyncAsync(null, ct);

        Reasons(result)
            .Should()
            .Equal(
                [(ModelTables.Folders, "1", SyncConflictReason.ModifiedOnServer)],
                "決着の無い版で戻した行は他者の内容に数える"
            );
        result.Uploaded.Should().Be(0);
        (await ServerFolderNameAsync(h, 1, ct)).Should().Be("local-edit");
        (await PendingFolderKeysAsync(h, ct)).Should().Equal(["1"]);
    }

    /// <summary>SyncVoFixture の端末：他者の内容が降りた後に、値オブジェクトの版を持つ古い実体を保存する</summary>
    private static async Task StaleValueObjectVersionAsync(
        SyncEngineFixedHarness _,
        CancellationToken ct
    )
    {
        using var server = await SyncVoFixtureTestServer.CreateAsync(ct);
        using var device = await SyncVoFixtureEngineDevice.CreateAsync(server.Orders, ct);
        var key = V.OrderIdValue.Create(5);
        await device.Orders.InsertAsync(NewVoOrder(5, "dave"), ct);
        (await device.Engine.SyncAsync(null, ct)).Uploaded.Should().Be(1);
        var stale = (await device.Orders.GetByIdAsync(key, ct))!;
        stale.RowVer.Should().NotBeNull("上げた行の版が書き戻っている（前提）");

        var onServer = (await server.OrdersRaw.GetByIdAsync(key, ct))!;
        onServer.Title = V.TitleValue.Create("server-edit");
        (await server.Writer.UpdateAsync(onServer, cancellationToken: ct)).Should().BeTrue();
        (await device.Engine.SyncAsync(null, ct)).Downloaded.Should().Be(1);

        stale.Title = V.TitleValue.Create("stale");
        (await device.Orders.UpdateAsync(stale, cancellationToken: ct)).Should().BeTrue();
        var result = await device.Engine.SyncAsync(null, ct);

        result
            .Conflicts.Select(conflict => conflict.Reason)
            .Should()
            .Equal([V.SyncConflictReason.ModifiedOnServer]);
        result.Uploaded.Should().Be(0);
        (await server.OrdersRaw.GetByIdAsync(key, ct))!
            .Title.Value.Should()
            .Be("server-edit", "他者の内容は上書きされない");
        (await device.ReadUnsentAsync(ct)).Should().HaveCount(1, "行は未送信のまま残る");
    }
}
