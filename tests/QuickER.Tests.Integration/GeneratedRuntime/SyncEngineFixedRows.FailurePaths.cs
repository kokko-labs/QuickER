using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のエンジンが失敗の途中で取る道の行：断られた書き込みの理由の照会が時間切れになる・書いた直後にサーバーで消えた行の版の
/// 読み戻し・塊の Force の洗い替えで捨てる行が行とキーの取得の間に消える・写し待ちの blob の読み取りが失敗する
/// </summary>
internal static partial class SyncEngineFixedRows
{
    private static IEnumerable<ScenarioRow<SyncEngineFixedCase>> FailurePathRows() =>
        [
            Row(
                "refused-write-explanation-lookup-timeout-is-aggregated-with-write-failure",
                "サーバーへの書き込みが失敗し、断られた理由の照会が呼び出し側の取り消しでない取り消し（HttpClient の時間切れの形）で"
                    + "失敗すると、同期は取り消しとして抜けず、書き込みの失敗と照会の失敗をまとめた AggregateException で終わる",
                RefusedWriteLookupTimeoutAsync
            ),
            Row(
                "blob-upload-then-row-deleted-on-server-leaves-change-unsent-and-next-sync-reports-missing",
                "blob を送った直後（版の読み戻しの前）にサーバーで行が消えると、同期は例外にならずにその行を未送信のまま残し、"
                    + "次の同期が MissingOnServer の競合で報告する",
                BlobUploadThenRowDeletedAsync
            ),
            .. new[] { (false, "keep-blobs"), (true, "discard-blobs") }.Select(shape =>
                Row(
                    $"chunk-refresh-force-discard-gone-between-rows-and-keys-counted-once-{shape.Item2}",
                    "塊ごとに確定する Force の洗い替えで、捨てる行の行を取った後・キーを取る前にサーバーでその行が消えても、"
                        + $"捨てた数はその行を 1 回だけ数える（{(shape.Item1 ? "blob を捨てる" : "blob を運ぶ")}洗い替え）",
                    (h, ct) => ChunkRefreshDiscardGoneBetweenRowsAndKeysAsync(h, shape.Item1, ct)
                )
            ),
            Row(
                "refresh-force-second-fetch-does-not-count-unplaced-discard-as-unsent",
                "Force の洗い替えの 1 回目の取得で、捨てる行が親の無い行として保留になり（取得の途中にサーバーが新しいフォルダーを"
                    + "作って品目を移す）、ほかに未送信が無いと、保留を取り直す 2 回目の取得はその捨てる行を未送信に数えず、塊の道を通る",
                RefreshSecondFetchUnplacedDiscardAsync
            ),
            Row(
                "owed-blob-copy-read-failure-closes-temporary-file",
                "降ろした行の写し待ちの blob を読む途中でサーバーの読み取りが失敗すると、同期は例外で終わり、作った一時ファイルは"
                    + "閉じられて残らない",
                OwedBlobCopyReadFailureAsync
            ),
        ];

    /// <summary>断られた書き込みの理由の照会が時間切れになる（<see cref="FailurePathRows"/>）</summary>
    private static async Task RefusedWriteLookupTimeoutAsync(
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
                Payload = [0x0A],
            },
            ct
        );
        await h.SyncAsync(WithoutBlobs, ct);
        await h.ExecuteLocalAsync("UPDATE model_items SET title = 'local' WHERE item_id = 11", ct);

        // 書き込みは注入した失敗で断られ、理由の照会の最初の読み取りは時間切れの形の取り消しで失敗する
        h.Faults.Arm(ModelFaultKind.ServerWrite, ModelActor.Device1);
        h.Faults.FailNextServerRead(() => new TaskCanceledException("The lookup timed out."));

        var thrown = await Record.ExceptionAsync(() => h.SyncAsync(WithoutBlobs, ct));

        thrown
            .Should()
            .BeOfType<AggregateException>(
                $"照会の時間切れは取り消しとして抜けない（例外：{thrown?.GetType().Name}: {thrown?.Message}）"
            )
            .Which.InnerExceptions.Select(inner => inner.GetType())
            .Should()
            .Equal(
                [typeof(InjectedFaultException), typeof(TaskCanceledException)],
                "書き込みの失敗と照会の失敗の両方を運ぶ"
            );
    }

    /// <summary>blob を送った直後にサーバーで行が消える（<see cref="FailurePathRows"/>）</summary>
    private static async Task BlobUploadThenRowDeletedAsync(
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
                Payload = [0x0A],
            },
            ct
        );
        await h.SyncAsync(WithBlobs, ct);
        await h.ExecuteLocalAsync(
            "UPDATE model_items SET title = 'local', payload = x'0A0B' WHERE item_id = 11",
            ct
        );

        var deleted = false;
        h.Faults.AfterBlobUpload = async id =>
        {
            if (id == 11 && !deleted)
            {
                deleted = true;
                (await h.Server.Items.DeleteAsync(11, ct)).Should().BeTrue();
            }
        };

        SyncResult? first = null;

        try
        {
            var thrown = await Record.ExceptionAsync(async () =>
                first = await h.SyncAsync(WithBlobs, ct)
            );
            thrown
                .Should()
                .BeNull(
                    $"書いた直後に消えた行の読み戻しで止まらない（例外：{thrown?.GetType().Name}: {thrown?.Message}）"
                );
        }
        finally
        {
            h.Faults.AfterBlobUpload = null;
        }

        deleted.Should().BeTrue("blob を送った直後に行を消した");
        first!.Conflicts.Should().BeEmpty("その実行は競合を報告しない");

        var second = await h.SyncAsync(WithBlobs, ct);
        second
            .Conflicts.Select(conflict =>
                $"{conflict.TableName}/{conflict.KeyText}/{conflict.Reason}"
            )
            .Should()
            .Equal(
                [$"{ModelTables.Items}/11/{SyncConflictReason.MissingOnServer}"],
                "未送信のまま残った行を、次の同期がサーバーに無い行として報告する"
            );
    }

    /// <summary>
    /// 塊の Force の洗い替えで捨てる行が行とキーの取得の間に消える（<see cref="FailurePathRows"/>）：品目 11 を端末で編集し、
    /// 品目の行を取った後・キーを取る前にサーバーが品目 11 を消す
    /// </summary>
    private static async Task ChunkRefreshDiscardGoneBetweenRowsAndKeysAsync(
        SyncEngineFixedHarness h,
        bool discardBlobs,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));

        foreach (var key in new[] { 11, 12, 13 })
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

        await h.SyncAsync(WithoutBlobs, ct);
        await h.ExecuteLocalAsync("UPDATE model_items SET title = 'local' WHERE item_id = 11", ct);

        var deleted = false;
        var routes = new List<SyncDownloadRoute>();
        var engine = SmallChunks(
            h.CreateEngine(async call =>
            {
                if (
                    !deleted
                    && call.Table == ModelTables.Items
                    && call.Call == SyncEngineSourceCall.KeysBefore
                )
                {
                    deleted = true;
                    (await h.Server.Items.DeleteAsync(11, ct)).Should().BeTrue();
                }
            })
        );
        engine.DownloadRouteObserved = routes.Add;

        var refreshed = await h.RefreshAsync(
            discardBlobs
                ? new SyncRefreshOptions { Force = true, DiscardLocalUnboundedBinaries = true }
                : new SyncRefreshOptions { Force = true, IncludeUnboundedBinary = true },
            ct,
            engine
        );

        var observed = $"道={string.Join(",", routes)}／捨てた数={refreshed.DiscardedChanges}";
        deleted.Should().BeTrue(observed);
        routes.Should().Contain(SyncDownloadRoute.Chunked, observed);
        refreshed.DiscardedChanges.Should().Be(1, "捨てた行は品目 11 の 1 つ（" + observed + "）");
    }

    /// <summary>
    /// 洗い替えの 2 回目の取得の未送信の数え方（<see cref="FailurePathRows"/>）：端末が品目 11 を編集し、Force の洗い替えのフォルダーの
    /// 取得の後・品目の取得の前に、サーバーがフォルダー 2 を作って品目 11 を移す（1 回目の取得では品目 11 の親が無いので保留＝置けない
    /// 捨てる行）。移した後に品目 12・13 も書き直して品目 11 より後の版にするので、2 回目の取得は保留の行の手前から閾値（2 行）を超える行を取り直す
    /// </summary>
    private static async Task RefreshSecondFetchUnplacedDiscardAsync(
        SyncEngineFixedHarness h,
        CancellationToken ct
    )
    {
        await InsertServerFoldersAsync(h, ct, (1, null, "f1"));

        foreach (var key in new[] { 11, 12, 13, 14 })
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

        await h.SyncAsync(WithoutBlobs, ct);
        await h.ExecuteLocalAsync("UPDATE model_items SET title = 'local' WHERE item_id = 11", ct);

        var moved = false;
        var routes = new List<SyncDownloadRoute>();
        var engine = SmallChunks(
            h.CreateEngine(async call =>
            {
                if (
                    !moved
                    && call.Table == ModelTables.Items
                    && call.Call == SyncEngineSourceCall.RowsBefore
                )
                {
                    moved = true;
                    await InsertServerFoldersAsync(h, ct, (2, null, "f2"));
                    var item = (await h.Server.Items.GetByIdAsync(11, ct))!;
                    item.FolderId = 2;
                    (await h.Server.Items.UpdateAsync(item, cancellationToken: ct))
                        .Should()
                        .BeTrue();

                    // 品目 12・13 を品目 11 より後の版にする（2 回目の取得が保留の行の手前から取り直す行を閾値より多くする）
                    foreach (var key in new[] { 12, 13 })
                    {
                        var later = (await h.Server.Items.GetByIdAsync(key, ct))!;
                        later.Title = $"t{key}-server";
                        (await h.Server.Items.UpdateAsync(later, cancellationToken: ct))
                            .Should()
                            .BeTrue();
                    }
                }
            })
        );
        engine.DownloadRouteObserved = routes.Add;

        var thrown = await Record.ExceptionAsync(() =>
            h.RefreshAsync(
                new SyncRefreshOptions { Force = true, IncludeUnboundedBinary = true },
                ct,
                engine
            )
        );

        var observed =
            $"例外={thrown?.GetType().Name}: {thrown?.Message}／道={string.Join(",", routes)}／移した={moved}";
        moved.Should().BeTrue(observed);
        routes.Should().HaveCount(2, "保留があるので 2 回取得する（" + observed + "）");
        routes[^1]
            .Should()
            .Be(
                SyncDownloadRoute.Chunked,
                "置けなかった捨てる行は 2 回目の取得の未送信に数えない（" + observed + "）"
            );
    }

    /// <summary>写し待ちの blob の読み取りが失敗する（<see cref="FailurePathRows"/>）</summary>
    private static async Task OwedBlobCopyReadFailureAsync(
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
                Payload = [0x0A, 0x0B],
            },
            ct
        );

        using var temp = new TestTempFolder();
        var engine = h.CreateEngine();
        engine.TemporaryFileFolder = temp.Path;
        h.Faults.Arm(ModelFaultKind.BlobDownload, ModelActor.Device1);

        var thrown = await Record.ExceptionAsync(() => h.SyncAsync(WithBlobs, ct, engine));

        InjectedFaultException
            .IsInjected(thrown)
            .Should()
            .BeTrue($"blob の読み取りの失敗で終わる（例外：{thrown?.Message}）");
        Directory
            .GetFiles(temp.Path)
            .Should()
            .BeEmpty(
                "読み取りが失敗した一時ファイルも閉じられて消える（閉じれば消える一時ファイル）"
            );
    }
}
