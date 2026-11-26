using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 主キーでない一意な列（品目の <c>code</c>）を参照する外部キーの子を、同期のエンジンが書かない形で端末が持つ行：サーバーが
/// 参照先の値を変えると、エンジンはその親の行を保留にして（書かずに打ち切りで名指しし）、ほかの行を降ろして同期を終える
/// </summary>
/// <remarks>
/// <para>
/// 参照する側は 3 通り：同期しないローカルの表（<c>local_notes</c>）・その実行で除外した同期する表（<c>ExcludedEntityTypes</c> の
/// イベント）・受信だけの実行の未送信の子（イベント）。イベントの参照の列は、端末の SQLite で <c>ALTER TABLE … ADD COLUMN …
/// REFERENCES model_items(code)</c> で足す。生成の記述子は一意な列への外部キーを <c>ParentReferences</c> に載せないので、エンジンから
/// 見えるものは、図にその外部キーがあるときと同じ（同期のモデルの図にはこの外部キーが無い）。
/// </para>
/// <para>
/// 道は 1 回で全部と塊（未送信の子の形は未送信があるので塊の道を通らない＝製品の仕様で、その組は作らない）。サーバーは品目 11 の
/// code を変え、ほかの行（品目 12 の題名）も変え、品目 11 を主キーで参照する新しいイベントも作る（参照先は端末に在るので保留しない）。
/// 子が参照を外した次の実行で、保留していた品目 11 が降りる。
/// </para>
/// </remarks>
internal static partial class SyncEngineFixedRows
{
    /// <summary>一意な列を参照する子の形</summary>
    private enum UniqueReferrer
    {
        /// <summary>同期しないローカルの表</summary>
        LocalTable,

        /// <summary>その実行で除外した同期する表</summary>
        ExcludedChild,

        /// <summary>受信だけの実行の未送信の子</summary>
        UnsentChildDownloadOnly,
    }

    private static IEnumerable<ScenarioRow<SyncEngineFixedCase>> UniqueReferenceRows() =>
        new[]
        {
            (UniqueReferrer.LocalTable, "local-table", false),
            (UniqueReferrer.LocalTable, "local-table", true),
            (UniqueReferrer.ExcludedChild, "excluded-child", false),
            (UniqueReferrer.ExcludedChild, "excluded-child", true),
            (UniqueReferrer.UnsentChildDownloadOnly, "unsent-child-download-only", false),
        }.Select(shape =>
            Row(
                $"unique-reference-{shape.Item2}-{(shape.Item3 ? "chunked" : "single")}-holds-parent-until-reference-released",
                $"主キーでない一意な列（品目の code）を参照する子（{Describe(shape.Item1)}）を端末が持つまま、サーバーが参照先の値を変えると、"
                    + $"{(shape.Item3 ? "塊ごとに確定する" : "1 回で全部の")}道の実行はその品目を書かずに保留して止まらずに終わり、ほかの行と、"
                    + "品目を主キーで参照する新しいイベントは降り、子の行と同期しない表には触らない。子が参照を外した次の実行で品目が降りる",
                (h, ct) => UniqueReferenceHoldsParentAsync(h, shape.Item1, shape.Item3, ct)
            )
        );

    private static string Describe(UniqueReferrer referrer) =>
        referrer switch
        {
            UniqueReferrer.LocalTable => "同期しないローカルの表",
            UniqueReferrer.ExcludedChild => "その実行で除外した同期する表",
            _ => "受信だけの実行の未送信の子",
        };

    /// <summary>一意な列を参照する子が親の値の変更を保留させる形（<see cref="UniqueReferenceRows"/>）</summary>
    private static async Task UniqueReferenceHoldsParentAsync(
        SyncEngineFixedHarness h,
        UniqueReferrer referrer,
        bool chunked,
        CancellationToken ct
    )
    {
        var eventAt = new DateTime(2026, 3, 1, 9, 0, 0);
        var serverEventAt = new DateTime(2026, 3, 2, 9, 0, 0);
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
                    Payload = [0x0A, (byte)key],
                },
                ct
            );
        }

        await h.Server.Events.InsertAsync(
            new ModelEventEntity
            {
                EventAt = eventAt,
                ItemId = 11,
                Memo = "e1",
            },
            ct
        );
        await h.SyncAsync(WithoutBlobs, ct);

        // 子が品目 11 の code を参照する
        string referrerSql;

        if (referrer == UniqueReferrer.LocalTable)
        {
            await h.ExecuteLocalAsync(
                "CREATE TABLE local_notes (note_id INTEGER PRIMARY KEY, item_code TEXT REFERENCES model_items(code))",
                ct
            );
            await h.ExecuteLocalAsync(
                "INSERT INTO local_notes (note_id, item_code) VALUES (1, 'c-11')",
                ct
            );
            referrerSql = "SELECT item_code FROM local_notes WHERE note_id = 1";
        }
        else
        {
            await h.ExecuteLocalAsync(
                "ALTER TABLE model_events ADD COLUMN item_code TEXT REFERENCES model_items(code)",
                ct
            );
            var setReference = "UPDATE model_events SET item_code = 'c-11'";

            // 除外する子は記録させずに書き（その表を除外した実行は記録を見ない）、未送信の子は記録つきで書く
            if (referrer == UniqueReferrer.ExcludedChild)
            {
                await h.ExecuteUntrackedAsync(setReference, ct);
            }
            else
            {
                await h.ExecuteLocalAsync(setReference, ct);
            }

            referrerSql = "SELECT item_code FROM model_events";
        }

        // サーバーが品目 11 の code を変え、品目 12 の題名を変え、品目 11 を主キーで参照する新しいイベントを作る
        var item11 = (await h.Server.Items.GetByIdAsync(11, ct))!;
        item11.Code = "c-11b";
        (await h.Server.Items.UpdateAsync(item11, cancellationToken: ct)).Should().BeTrue();
        var item12 = (await h.Server.Items.GetByIdAsync(12, ct))!;
        item12.Title = "t12-server";
        (await h.Server.Items.UpdateAsync(item12, cancellationToken: ct)).Should().BeTrue();

        // 取得が塊へ入る閾値（2 行）を超えるよう、新しい品目も作る（除外した子の形でも 3 行を取得する）
        await h.Server.Items.InsertAsync(
            new ModelItemEntity
            {
                ItemId = 13,
                FolderId = 1,
                Code = "c-13",
                Title = "t13",
                Payload = [0x0A, 13],
            },
            ct
        );

        if (referrer != UniqueReferrer.ExcludedChild)
        {
            await h.Server.Events.InsertAsync(
                new ModelEventEntity
                {
                    EventAt = serverEventAt,
                    ItemId = 11,
                    Memo = "server-child-by-key",
                },
                ct
            );
        }

        var engine = chunked ? SmallChunks(h.CreateEngine()) : h.CreateEngine();
        var routes = new List<SyncDownloadRoute>();
        engine.DownloadRouteObserved = routes.Add;
        var options =
            referrer == UniqueReferrer.ExcludedChild
                ? new SyncOptions
                {
                    IncludeUnboundedBinary = false,
                    ExcludedEntityTypes = [typeof(ModelEventEntity)],
                }
                : WithoutBlobs;

        IReadOnlyList<SyncDownloadTruncation>? truncations = null;
        var thrown = await Record.ExceptionAsync(async () =>
        {
            if (referrer == UniqueReferrer.UnsentChildDownloadOnly)
            {
                await engine.DownloadAsync(options, ct);
            }
            else
            {
                truncations = (await h.SyncAsync(options, ct, engine)).Truncations;
            }
        });

        var local = await h.ReadLocalAsync(ct);
        var observed =
            $"例外={thrown?.GetType().Name}: {thrown?.Message}／道={string.Join(",", routes)}"
            + $"／打ち切り={string.Join(",", (truncations ?? []).Select(t => $"{t.TableName}/{t.PendingKeyText}/{t.MissingParentTableName ?? "-"}"))}"
            + $"／品目 11={(local.Items.TryGetValue(11, out var i11) ? i11.Code : "無い")}";

        thrown.Should().BeNull(observed);
        routes
            .Should()
            .Contain(chunked ? SyncDownloadRoute.Chunked : SyncDownloadRoute.Single, observed);
        local
            .Items[11]
            .Code.Should()
            .Be("c-11", "参照先の値を変えた親は保留され、端末の古い内容のまま（" + observed + "）");
        local.Items[12].Title.Should().Be("t12-server", "ほかの行は降りる（" + observed + "）");
        local.Items.Should().ContainKey(13, "ほかの行は降りる（" + observed + "）");
        (await h.ScalarAsync(referrerSql, ct))
            .Should()
            .Be("c-11", "エンジンは子の行を書かない（" + observed + "）");

        if (referrer != UniqueReferrer.ExcludedChild)
        {
            local
                .Events.Values.Should()
                .Contain(
                    e => e.Memo == "server-child-by-key" && e.ItemId == 11,
                    "親を主キーで参照する子は、親が端末に在るので降りる（" + observed + "）"
                );
        }

        if (truncations is not null)
        {
            truncations
                .Should()
                .ContainSingle(
                    t => t.TableName == ModelTables.Items && t.PendingKeyText == "11",
                    "保留した親は打ち切りで名指しされる（" + observed + "）"
                )
                .Which.MissingParentTableName.Should()
                .BeNull("親が無いのではなく、子が参照するので保留した（" + observed + "）");
        }

        if (referrer == UniqueReferrer.LocalTable)
        {
            (
                await h.ScalarAsync(
                    "SELECT count(*) FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'local_notes'",
                    ct
                )
            )
                .Should()
                .Be("0", "同期しない表にトリガーを張らない");
            (
                await h.ScalarAsync(
                    "SELECT count(*) FROM quicker_sync_row WHERE table_name = 'local_notes'",
                    ct
                )
            )
                .Should()
                .Be("0", "同期しない表の行を記録しない");
        }

        // 子が参照を外すと、次の実行で保留していた品目 11 が降りる
        var release =
            referrer == UniqueReferrer.LocalTable
                ? "UPDATE local_notes SET item_code = NULL"
                : "UPDATE model_events SET item_code = NULL";

        if (referrer == UniqueReferrer.ExcludedChild)
        {
            await h.ExecuteUntrackedAsync(release, ct);
        }
        else
        {
            await h.ExecuteLocalAsync(release, ct);
        }

        IReadOnlyList<SyncDownloadTruncation>? after = null;

        if (referrer == UniqueReferrer.UnsentChildDownloadOnly)
        {
            await engine.DownloadAsync(options, ct);
        }
        else
        {
            after = (await h.SyncAsync(options, ct, engine)).Truncations;
        }

        (await h.ReadLocalAsync(ct))
            .Items[11]
            .Code.Should()
            .Be("c-11b", "参照を外した次の実行で親が降りる");
        (after ?? []).Should().BeEmpty("保留する行が残らない");
    }
}
