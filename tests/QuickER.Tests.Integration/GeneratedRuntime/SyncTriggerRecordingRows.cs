using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using Xunit;
using M = QuickER.Tests.GeneratedSyncModelFixture;
using S = QuickER.Tests.GeneratedSyncFixture;
using V = QuickER.Tests.GeneratedSyncVoFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>行の状態の表に残るはずの 1 行（変更の番号は並びの順で表す）</summary>
/// <param name="Table">表の名前</param>
/// <param name="KeyType">キーの格納の型（SQLite の <c>typeof</c>）</param>
/// <param name="KeyText">キーの値のテキスト</param>
/// <param name="Dirty">送る印</param>
/// <param name="Deleted">削除の印</param>
/// <param name="BlobColumns">書いた除外 blob の列（アクセサの名前の <c>,</c> 区切り）</param>
internal sealed record SyncTriggerExpectedRow(
    string Table,
    string KeyType,
    string KeyText,
    long Dirty,
    long Deleted,
    string BlobColumns
);

/// <summary>同期のトリガーの記録の表の 1 行の入力</summary>
/// <param name="Fixture">書き込む先のフィクスチャ</param>
/// <param name="Prepare">トリガーを張る前に入れておく行（記録に残らない。null なら何もしない）</param>
/// <param name="Act">書き込みの手</param>
/// <param name="Expected">行の状態の表に残るはずの行（変更の番号の小さい順）</param>
/// <param name="TrackedTables">トリガーを張る表の名前（null ならフィクスチャの同期する表の全部）</param>
/// <param name="AlterDiagram">DDL を作る前に SQLite 方言の図の写しを変える処理（null なら変えない）</param>
internal sealed record SyncTriggerRecordingCase(
    SyncTriggerFixture Fixture,
    Func<SyncTriggerHarness, CancellationToken, Task>? Prepare,
    Func<SyncTriggerHarness, CancellationToken, Task> Act,
    IReadOnlyList<SyncTriggerExpectedRow> Expected,
    IReadOnlyList<string>? TrackedTables = null,
    Action<ErDiagram>? AlterDiagram = null
);

/// <summary>
/// 同期のトリガーの記録の表：1 行＝「どのフィクスチャのどの表に、どの書き込みの道で書くと、<c>quicker_sync_row</c> に何が残るか」。
/// <see cref="SyncSupportAdoSqliteTriggerRuntimeTests"/> が 1 行ずつ流す。
/// </summary>
/// <remarks>
/// <para>
/// 書き込みは記録デコレータで包まない素の生成 Repository・生 SQL・接続の上の文で行い、記録は <c>SyncLocalTracking.Ensure</c>
/// が張ったトリガーだけが行う。行はキーの形（int・文字列・datetime2・値オブジェクト）、版なしの表、書き込みの道（直接の
/// CRUD・一括挿入・グラフ保存・生 SQL・条件つきの一括削除・条件なしの削除・Stream・Save フックの中の blob）、主キーの変更、
/// 失敗した書き込み、外部キーの動作による連鎖、適用中の印、トリガーを張らない表で分ける。
/// </para>
/// <para>
/// どの行にも掛ける表明（期待の並びとの一致・変更の番号の増え方・張った直後の状態・触らない前準備の行が記録に無いこと・
/// キーで元の表の行を引き直せること）はランナーの側が持つ。ランナーは手の前後で内容の同じ前準備の行を「触らない行」とみなすので、
/// 前準備の行を書き換える手は、内容の変わる値で書く（同じ値の UPDATE はトリガーを発火させるが、触らない行と見分けられない）。
/// </para>
/// </remarks>
internal static class SyncTriggerRecordingRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<SyncTriggerRecordingCase> Table { get; } = Build();

    /// <summary>表の Id（Theory のデータ）</summary>
    public static TheoryData<string> Ids() => Table.Ids();

    private const string Folders = "model_folders";
    private const string Labels = "model_labels";
    private const string Items = "model_items";
    private const string Events = "model_events";
    private const string Orders = "sync_orders";
    private const string Lines = "sync_order_lines";
    private const string Notes = "sync_notes";
    private const string VoOrders = "syncvo_orders";
    private const string VoLines = "syncvo_lines";

    /// <summary>出来事のキーにする日時</summary>
    private static readonly DateTime EventAt = new(2026, 1, 2, 3, 4, 5);

    /// <summary>出来事のキーの格納のテキスト</summary>
    private const string EventAtText = "2026-01-02 03:04:05";

    /// <summary>出来事のキーを変えた先の格納のテキスト</summary>
    private const string LaterEventAtText = "2026-01-02 03:04:06";

    private static ScenarioRow<SyncTriggerRecordingCase> Row(
        string id,
        string description,
        SyncTriggerRecordingCase input
    ) => new(id, null, description, input);

    /// <summary>送る行（削除でない）</summary>
    private static SyncTriggerExpectedRow Live(string table, object key, string blobColumns = "") =>
        Expected(table, key, deleted: 0, blobColumns);

    /// <summary>送る削除の行</summary>
    private static SyncTriggerExpectedRow Gone(string table, object key, string blobColumns = "") =>
        Expected(table, key, deleted: 1, blobColumns);

    private static SyncTriggerExpectedRow Expected(
        string table,
        object key,
        long deleted,
        string blobColumns
    ) =>
        key switch
        {
            int number => new(
                table,
                "integer",
                number.ToString(CultureInfo.InvariantCulture),
                1,
                deleted,
                blobColumns
            ),
            string text => new(table, "text", text, 1, deleted, blobColumns),
            double real => new(
                table,
                "real",
                real.ToString("R", CultureInfo.InvariantCulture),
                1,
                deleted,
                blobColumns
            ),
            byte[] bytes => new(table, "blob", Convert.ToHexString(bytes), 1, deleted, blobColumns),
            _ => throw new ArgumentException($"知らないキーの型 {key.GetType()}", nameof(key)),
        };

    private static M.ModelFolderEntity Folder(int id, string name, int? parentId = null) =>
        new()
        {
            FolderId = id,
            Name = name,
            ParentId = parentId,
        };

    private static M.ModelLabelEntity Label(string code, int folderId, string caption) =>
        new()
        {
            LabelCode = code,
            FolderId = folderId,
            Caption = caption,
        };

    private static M.ModelItemEntity Item(
        int id,
        int folderId,
        string code,
        string title,
        byte[]? payload = null
    ) =>
        new()
        {
            ItemId = id,
            FolderId = folderId,
            Code = code,
            Title = title,
            Payload = payload ?? [1, 2, 3],
        };

    private static M.ModelEventEntity Event(DateTime at, int itemId, string memo) =>
        new()
        {
            EventAt = at,
            ItemId = itemId,
            Memo = memo,
        };

    private static S.SyncOrderEntity Order(int id, string customer) =>
        new() { OrderId = id, CustomerName = customer };

    private static V.SyncvoOrderEntity VoOrder(int id, string title) =>
        new()
        {
            OrderId = V.OrderIdValue.Create(id),
            Title = V.TitleValue.Create(title),
            Photo = V.PhotoValue.Create([1, 2, 3]),
        };

    private static V.SyncvoLineEntity VoLine(int id, int orderId, int qty) =>
        new()
        {
            LineId = V.LineIdValue.Create(id),
            OrderId = V.OrderIdValue.Create(orderId),
            Qty = V.QtyValue.Create(qty),
        };

    private static MemoryStream Bytes(params byte[] bytes) => new(bytes);

    /// <summary>フォルダー 1 を入れる前準備</summary>
    private static Task PrepareFolderAsync(SyncTriggerHarness h, CancellationToken ct) =>
        h.Model.Folders.InsertAsync(Folder(1, "root"), ct);

    /// <summary>フォルダー 1 と品目 10 を入れる前準備</summary>
    private static async Task PrepareItemAsync(SyncTriggerHarness h, CancellationToken ct)
    {
        await PrepareFolderAsync(h, ct);
        await h.Model.Items.InsertAsync(Item(10, 1, "c10", "ten"), ct);
    }

    /// <summary>フォルダー 1・品目 10・出来事 1 つを入れる前準備</summary>
    private static async Task PrepareEventAsync(SyncTriggerHarness h, CancellationToken ct)
    {
        await PrepareItemAsync(h, ct);
        await h.Model.Events.InsertAsync(Event(EventAt, 10, "first"), ct);
    }

    /// <summary>外部キーの動作だけを変える（制約の名前で引く）</summary>
    private static Action<ErDiagram> WithActions(
        params (string ConstraintName, ForeignKeyReferentialAction OnDelete)[] actions
    ) =>
        diagram =>
        {
            foreach (var (name, onDelete) in actions)
            {
                diagram
                    .Relationships.Single(r =>
                        string.Equals(r.ConstraintName, name, StringComparison.Ordinal)
                    )
                    .OnDelete = onDelete;
            }
        };

    /// <summary>SQLite 方言の図の写しで、表の列の型を書き換える（キーの列を別の格納の型にする）</summary>
    /// <param name="tableName">表の名前</param>
    /// <param name="columnName">列の名前</param>
    /// <param name="dataType">SQLite の型の表記</param>
    private static Action<ErDiagram> WithColumnType(
        string tableName,
        string columnName,
        string dataType
    ) =>
        diagram =>
            diagram
                .Entities.Single(e =>
                    string.Equals(e.TableName, tableName, StringComparison.Ordinal)
                )
                .Columns.Single(c => string.Equals(c.Name, columnName, StringComparison.Ordinal))
                .DataType = dataType;

    /// <summary>
    /// 前段の書き込みで記録した行のエンジンだけが書く列へ値を入れてから手を流し、手の後も前段で記録した行（表とキー）の
    /// エンジンの列が、入れた値のまま残ることを表明する手
    /// </summary>
    /// <param name="seed">前段の書き込み（トリガーを張った後に流すので記録に残る）</param>
    /// <param name="act">エンジンの列を保つはずの書き込み</param>
    private static Func<SyncTriggerHarness, CancellationToken, Task> KeepsEngineColumns(
        Func<SyncTriggerHarness, CancellationToken, Task> seed,
        Func<SyncTriggerHarness, CancellationToken, Task> act
    ) =>
        async (h, ct) =>
        {
            await seed(h, ct);
            await h.SetEngineColumnsAsync(ct);
            var before = await h.ReadEngineColumnsAsync(ct);
            before.Should().NotBeEmpty("前段の書き込みが記録した行にエンジンの列の値を入れる");

            await act(h, ct);

            var after = await h.ReadEngineColumnsAsync(ct);

            foreach (var (key, columns) in before)
            {
                after
                    .Should()
                    .ContainKey(key, $"前段で記録した行 {key} は手の後も行の状態の表にある");
                after[key]
                    .Should()
                    .Be(
                        columns,
                        $"前段で記録した行 {key} のエンジンの列は、トリガーの記録の後も保たれる"
                    );
            }
        };

    /// <summary>フォルダー 1 の名前を変える前段の書き込み</summary>
    private static async Task RenameFolderAsync(SyncTriggerHarness h, CancellationToken ct) =>
        (await h.Model.Folders.UpdateAsync(Folder(1, "seeded"), cancellationToken: ct))
            .Should()
            .BeTrue();

    /// <summary>品目 10 の題名を変える前段の書き込み</summary>
    private static async Task RetitleItemAsync(SyncTriggerHarness h, CancellationToken ct) =>
        (await h.Model.Items.UpdateAsync(Item(10, 1, "c10", "seeded", []), cancellationToken: ct))
            .Should()
            .BeTrue();

    /// <summary>接続の上で文を 1 つ実行する</summary>
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static ScenarioTable<SyncTriggerRecordingCase> Build()
    {
        List<ScenarioRow<SyncTriggerRecordingCase>> rows =
        [
            // キーの形ごとの挿入・更新・削除
            Row(
                "int-key-insert",
                "int のキーの表への挿入は、そのキーを整数で記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    null,
                    (h, ct) => h.Model.Folders.InsertAsync(Folder(1, "root"), ct),
                    [Live(Folders, 1)]
                )
            ),
            Row(
                "int-key-update",
                "int のキーの表の更新は、そのキーを送る行として記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Folders.UpdateAsync(
                                Folder(1, "renamed"),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue(),
                    [Live(Folders, 1)]
                )
            ),
            Row(
                "int-key-delete",
                "int のキーの表の削除は、そのキーを削除の行として記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) => (await h.Model.Folders.DeleteAsync(1, ct)).Should().BeTrue(),
                    [Gone(Folders, 1)]
                )
            ),
            Row(
                "text-key-versionless-insert",
                "文字列のキーの版なしの表への挿入は、そのキーをテキストで記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    (h, ct) => h.Model.Labels.InsertAsync(Label("L1", 1, "one"), ct),
                    [Live(Labels, "L1")]
                )
            ),
            Row(
                "text-key-versionless-update",
                "文字列のキーの版なしの表の更新も、版のある表と同じく記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareFolderAsync(h, ct);
                        await h.Model.Labels.InsertAsync(Label("L1", 1, "one"), ct);
                    },
                    async (h, ct) =>
                        (
                            await h.Model.Labels.UpdateAsync(
                                Label("L1", 1, "uno"),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue(),
                    [Live(Labels, "L1")]
                )
            ),
            Row(
                "text-key-versionless-delete",
                "文字列のキーの版なしの表の削除も、削除の行として記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareFolderAsync(h, ct);
                        await h.Model.Labels.InsertAsync(Label("L1", 1, "one"), ct);
                    },
                    async (h, ct) => (await h.Model.Labels.DeleteAsync("L1", ct)).Should().BeTrue(),
                    [Gone(Labels, "L1")]
                )
            ),
            Row(
                "datetime-key-insert",
                "datetime2 のキーの表への挿入は、表が格納したとおりのテキストで記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    (h, ct) => h.Model.Events.InsertAsync(Event(EventAt, 10, "first"), ct),
                    [Live(Events, EventAtText)]
                )
            ),
            Row(
                "datetime-key-update",
                "datetime2 のキーの表の更新は、表が格納したとおりのテキストで記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareEventAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Events.UpdateAsync(
                                Event(EventAt, 10, "changed"),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue(),
                    [Live(Events, EventAtText)]
                )
            ),
            Row(
                "datetime-key-delete",
                "datetime2 のキーの表の削除は、表が格納していたとおりのテキストで削除を記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareEventAsync,
                    async (h, ct) =>
                        (await h.Model.Events.DeleteAsync(EventAt, ct)).Should().BeTrue(),
                    [Gone(Events, EventAtText)]
                )
            ),
            Row(
                "real-stored-key-insert-and-update",
                "数の親和性のキーの列（NUMERIC）へテキストで入れた小数のキーは、表が実数で格納し、記録も同じ実数で残る"
                    + "（記録したキーと表のキーの列を SQLite の = で突き合わせて元の行を引ける）",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                    {
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "INSERT INTO model_events (event_at, item_id, memo) VALUES ('12.50', 10, 'first')",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1);
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "UPDATE model_events SET memo = 'changed' WHERE event_at = 12.5",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1);
                    },
                    [Live(Events, 12.5)],
                    AlterDiagram: WithColumnType(Events, "event_at", "NUMERIC")
                )
            ),
            Row(
                "blob-stored-key-insert-and-update",
                "blob で格納するキーの列（BLOB）のキーは、記録も同じ blob で残る"
                    + "（記録したキーと表のキーの列を SQLite の = で突き合わせて元の行を引ける）",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                    {
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "INSERT INTO model_events (event_at, item_id, memo) VALUES (x'0A0B00', 10, 'first')",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1);
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "UPDATE model_events SET memo = 'changed' WHERE event_at = x'0A0B00'",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1);
                    },
                    [Live(Events, new byte[] { 0x0A, 0x0B, 0x00 })],
                    AlterDiagram: WithColumnType(Events, "event_at", "BLOB")
                )
            ),
            Row(
                "value-object-key-insert",
                "値オブジェクトのキーの表への挿入は、中の値を整数で記録し、除外 blob の名前を全部付ける",
                new(
                    SyncTriggerFixture.SyncVo,
                    null,
                    (h, ct) => h.Vo.Orders.InsertAsync(VoOrder(1, "alice"), ct),
                    [Live(VoOrders, 1, "Photo")]
                )
            ),
            Row(
                "value-object-key-update",
                "値オブジェクトのキーの表の更新は、中の値を整数で記録し、blob の列は付けない",
                new(
                    SyncTriggerFixture.SyncVo,
                    (h, ct) => h.Vo.Orders.InsertAsync(VoOrder(1, "alice"), ct),
                    async (h, ct) =>
                    {
                        var order = VoOrder(1, "bob");
                        order.Photo = V.PhotoValue.Create([]);
                        (await h.Vo.Orders.UpdateAsync(order, cancellationToken: ct))
                            .Should()
                            .BeTrue();
                    },
                    [Live(VoOrders, 1)]
                )
            ),
            Row(
                "value-object-key-delete",
                "値オブジェクトのキーの表の削除は、中の値を整数で削除として記録する",
                new(
                    SyncTriggerFixture.SyncVo,
                    (h, ct) => h.Vo.Orders.InsertAsync(VoOrder(1, "alice"), ct),
                    async (h, ct) =>
                        (await h.Vo.Orders.DeleteAsync(V.OrderIdValue.Create(1), ct))
                            .Should()
                            .BeTrue(),
                    [Gone(VoOrders, 1)]
                )
            ),
            Row(
                "nullable-blob-insert-names-all-blob-columns",
                "NULL 許容の除外 blob を持つ表への挿入も、blob を NULL で入れたときに除外 blob の名前を全部付ける",
                new(
                    SyncTriggerFixture.Sync,
                    null,
                    (h, ct) => h.Sync.Orders.InsertAsync(Order(1, "alice"), ct),
                    [Live(Orders, 1, "Attachment")]
                )
            ),
            Row(
                "versionless-child-insert-and-delete",
                "版なしの子の表の挿入と削除も記録する（親の挿入の後に並ぶ）",
                new(
                    SyncTriggerFixture.Sync,
                    null,
                    async (h, ct) =>
                    {
                        await h.Sync.Orders.InsertAsync(Order(1, "alice"), ct);
                        await h.Sync.Notes.InsertAsync(
                            new S.SyncNoteEntity
                            {
                                NoteId = 20,
                                OrderId = 1,
                                Body = "memo",
                            },
                            ct
                        );
                        await h.Sync.Notes.InsertAsync(
                            new S.SyncNoteEntity
                            {
                                NoteId = 21,
                                OrderId = 1,
                                Body = "memo2",
                            },
                            ct
                        );
                        (await h.Sync.Notes.DeleteAsync(21, ct)).Should().BeTrue();
                    },
                    [Live(Orders, 1, "Attachment"), Live(Notes, 20), Gone(Notes, 21)]
                )
            ),
            // 除外 blob のある表
            Row(
                "blob-table-insert-names-all-blob-columns",
                "NOT NULL の除外 blob を持つ表への挿入は、除外 blob の名前を全部付けて記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    (h, ct) => h.Model.Items.InsertAsync(Item(10, 1, "c10", "ten"), ct),
                    [Live(Items, 10, "Payload")]
                )
            ),
            Row(
                "blob-table-non-blob-update-keeps-blob-columns-empty",
                "除外 blob を持つ表の blob でない列の更新は、blob の列を付けない",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Items.UpdateAsync(
                                Item(10, 1, "c10", "renamed", []),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue(),
                    [Live(Items, 10)]
                )
            ),
            Row(
                "stream-write-names-blob-column",
                "Stream で除外 blob を書くと、その列の名前を付けて記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Items.WritePayloadAsync(
                                10,
                                Bytes(7, 7),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue(),
                    [Live(Items, 10, "Payload")]
                )
            ),
            Row(
                "stream-write-twice-names-blob-column-once",
                "同じ除外 blob を 2 回書いても、列の名前は 1 回だけ付く",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                    {
                        (
                            await h.Model.Items.WritePayloadAsync(
                                10,
                                Bytes(7, 7),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue();
                        (
                            await h.Model.Items.WritePayloadAsync(
                                10,
                                Bytes(8, 8, 8),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue();
                    },
                    [Live(Items, 10, "Payload")]
                )
            ),
            Row(
                "stream-write-then-non-blob-update-keeps-blob-column",
                "除外 blob を書いた後の blob でない列の更新は、付いた blob の列を変えない",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                    {
                        (
                            await h.Model.Items.WritePayloadAsync(
                                10,
                                Bytes(7, 7),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue();
                        (
                            await h.Model.Items.UpdateAsync(
                                Item(10, 1, "c10", "renamed", []),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue();
                    },
                    [Live(Items, 10, "Payload")]
                )
            ),
            Row(
                "stream-write-then-delete-then-reinsert",
                "除外 blob を書いた行を消して同じキーで入れ直すと、削除の印が外れ、除外 blob の名前を全部付ける",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                    {
                        (
                            await h.Model.Items.WritePayloadAsync(
                                10,
                                Bytes(7, 7),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue();
                        (await h.Model.Items.DeleteAsync(10, ct)).Should().BeTrue();
                        await h.Model.Items.InsertAsync(Item(10, 1, "c10", "again"), ct);
                    },
                    [Live(Items, 10, "Payload")]
                )
            ),
            Row(
                "delete-then-reinsert-without-blob-write-names-all-blob-columns",
                "除外 blob を書いていない行（blob の列の記録が空）を消して同じキーで入れ直すと、除外 blob の名前を全部付ける",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                    {
                        (await h.Model.Items.DeleteAsync(10, ct)).Should().BeTrue();
                        await h.Model.Items.InsertAsync(Item(10, 1, "c10", "again"), ct);
                    },
                    [Live(Items, 10, "Payload")]
                )
            ),
            Row(
                "nullable-blob-stream-write-and-null-write",
                "NULL 許容の除外 blob を Stream で書いても、NULL を書いても、その列の名前を付けて記録する",
                new(
                    SyncTriggerFixture.Sync,
                    async (h, ct) =>
                    {
                        await h.Sync.Orders.InsertAsync(Order(1, "alice"), ct);
                        await h.Sync.Orders.InsertAsync(Order(2, "bob"), ct);
                        (
                            await h.Sync.Orders.WriteAttachmentAsync(
                                2,
                                Bytes(1),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue();
                    },
                    async (h, ct) =>
                    {
                        (
                            await h.Sync.Orders.WriteAttachmentAsync(
                                1,
                                Bytes(5),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue();
                        (await h.Sync.Orders.WriteAttachmentAsync(2, null, cancellationToken: ct))
                            .Should()
                            .BeTrue();
                    },
                    [Live(Orders, 1, "Attachment"), Live(Orders, 2, "Attachment")]
                )
            ),
            Row(
                "value-object-stream-write",
                "値オブジェクトのキーの表でも、Stream で書いた除外 blob の名前を付けて記録する",
                new(
                    SyncTriggerFixture.SyncVo,
                    (h, ct) => h.Vo.Orders.InsertAsync(VoOrder(1, "alice"), ct),
                    async (h, ct) =>
                        (
                            await h.Vo.Orders.WritePhotoAsync(
                                V.OrderIdValue.Create(1),
                                Bytes(9, 9),
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .BeTrue(),
                    [Live(VoOrders, 1, "Photo")]
                )
            ),
            Row(
                "save-hook-blob-write",
                "グラフ保存の Save フックの中で書いた除外 blob も、その列の名前を付けて記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                    {
                        h.Model.ItemHook.After = async (entity, operation, context, token) =>
                        {
                            if (operation == M.SaveOperation.Update)
                            {
                                (
                                    await context.WriteBinaryColumnAsync(
                                        nameof(M.ModelItemEntity.Payload),
                                        entity.ItemId,
                                        Bytes(4, 4),
                                        cancellationToken: token
                                    )
                                )
                                    .Should()
                                    .BeTrue();
                            }
                        };

                        var item = Item(10, 1, "c10", "renamed", []);
                        item.MarkUpdated();
                        await h.Model.Items.SaveAsync(item, cancellationToken: ct);
                    },
                    [Live(Items, 10, "Payload")]
                )
            ),
            // 書き込みの道
            Row(
                "bulk-insert-records-each-row-with-blob-columns",
                "一括挿入は行ごとに、渡した順に、除外 blob の名前を全部付けて記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Items.BulkInsertAsync(
                                [
                                    Item(12, 1, "c12", "twelve"),
                                    Item(10, 1, "c10", "ten"),
                                    Item(11, 1, "c11", "eleven"),
                                ],
                                ct
                            )
                        )
                            .Should()
                            .Be(3),
                    [
                        Live(Items, 12, "Payload"),
                        Live(Items, 10, "Payload"),
                        Live(Items, 11, "Payload"),
                    ]
                )
            ),
            Row(
                "bulk-insert-versionless",
                "版なしの表への一括挿入も行ごとに記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Labels.BulkInsertAsync(
                                [Label("L2", 1, "two"), Label("L1", 1, "one")],
                                ct
                            )
                        )
                            .Should()
                            .Be(2),
                    [Live(Labels, "L2"), Live(Labels, "L1")]
                )
            ),
            Row(
                "bulk-insert-value-object-key",
                "値オブジェクトのキーの表への一括挿入も行ごとに、除外 blob の名前を付けて記録する",
                new(
                    SyncTriggerFixture.SyncVo,
                    null,
                    async (h, ct) =>
                        (await h.Vo.Orders.BulkInsertAsync([VoOrder(2, "b"), VoOrder(1, "a")], ct))
                            .Should()
                            .Be(2),
                    [Live(VoOrders, 2, "Photo"), Live(VoOrders, 1, "Photo")]
                )
            ),
            Row(
                "graph-save-insert-update-delete",
                "1 回のグラフ保存の親の更新・子の更新・子の挿入・子の削除を、保存が書いた順に記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareItemAsync(h, ct);
                        await h.Model.Items.InsertAsync(Item(11, 1, "c11", "eleven"), ct);
                    },
                    async (h, ct) =>
                    {
                        var folder = Folder(1, "renamed");
                        folder.MarkUpdated();
                        var updated = Item(10, 1, "c10", "ten!", []);
                        updated.MarkUpdated();
                        var removed = Item(11, 1, "c11", "eleven", []);
                        removed.MarkRemoved();
                        var added = Item(12, 1, "c12", "twelve");
                        added.MarkAdded();
                        folder.ModelItems.Add(updated);
                        folder.ModelItems.Add(removed);
                        folder.ModelItems.Add(added);
                        await h.Model.Folders.SaveAsync(folder, cancellationToken: ct);
                    },
                    [Live(Folders, 1), Live(Items, 10), Live(Items, 12, "Payload"), Gone(Items, 11)]
                )
            ),
            Row(
                "graph-save-parent-and-child-insert",
                "グラフ保存の親と子の挿入を、親・子の順に記録する（親は除外 blob の名前つき）",
                new(
                    SyncTriggerFixture.Sync,
                    null,
                    async (h, ct) =>
                    {
                        var order = Order(1, "alice");
                        order.SyncOrderLines.Add(
                            new S.SyncOrderLineEntity
                            {
                                LineId = 10,
                                OrderId = 1,
                                Product = "pen",
                            }
                        );
                        order.MarkAdded(includeChildren: true);
                        await h.Sync.Orders.SaveAsync(order, cancellationToken: ct);
                    },
                    [Live(Orders, 1, "Attachment"), Live(Lines, 10)]
                )
            ),
            Row(
                "graph-save-delete-closure-value-object-key",
                "値オブジェクトのキーの親をグラフ保存で消すと、消えた子と親を削除として記録する",
                new(
                    SyncTriggerFixture.SyncVo,
                    async (h, ct) =>
                    {
                        await h.Vo.Orders.InsertAsync(VoOrder(1, "alice"), ct);
                        await h.Vo.Lines.InsertAsync(VoLine(10, 1, 3), ct);
                        await h.Vo.Lines.InsertAsync(VoLine(11, 1, 4), ct);
                    },
                    async (h, ct) =>
                    {
                        var order = new V.SyncvoOrderEntity { OrderId = V.OrderIdValue.Create(1) };
                        var first = new V.SyncvoLineEntity
                        {
                            LineId = V.LineIdValue.Create(10),
                            OrderId = V.OrderIdValue.Create(1),
                        };
                        var second = new V.SyncvoLineEntity
                        {
                            LineId = V.LineIdValue.Create(11),
                            OrderId = V.OrderIdValue.Create(1),
                        };
                        order.SyncvoLines.Add(first);
                        order.SyncvoLines.Add(second);
                        order.MarkRemoved();
                        first.MarkRemoved();
                        second.MarkRemoved();
                        await h.Vo.Orders.SaveAsync(order, cancellationToken: ct);
                    },
                    [Gone(VoLines, 10), Gone(VoLines, 11), Gone(VoOrders, 1)]
                )
            ),
            Row(
                "raw-sql-insert-update-delete",
                "生 SQL の挿入・更新・削除を、実行した順に記録する（挿入は除外 blob の名前つき）",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareItemAsync(h, ct);
                        await h.Model.Items.InsertAsync(Item(11, 1, "c11", "eleven"), ct);
                    },
                    async (h, ct) =>
                    {
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "INSERT INTO model_items (item_id, folder_id, code, title, payload) VALUES (12, 1, 'c12', 'twelve', x'00')",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1);
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "UPDATE model_items SET title = 'ten!' WHERE item_id = 10",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1);
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "DELETE FROM model_items WHERE item_id = 11",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1);
                    },
                    [Live(Items, 12, "Payload"), Live(Items, 10), Gone(Items, 11)]
                )
            ),
            Row(
                "raw-sql-blob-column-update",
                "生 SQL で除外 blob の列を更新すると、その列の名前を付けて記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "UPDATE model_items SET payload = x'0102' WHERE item_id = 10",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1),
                    [Live(Items, 10, "Payload")]
                )
            ),
            Row(
                "execute-delete-with-condition",
                "条件つきの一括削除は、条件に合った行だけを削除として記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareItemAsync(h, ct);
                        await h.Model.Items.InsertAsync(Item(11, 1, "c11", "eleven"), ct);
                        await h.Model.Items.InsertAsync(Item(12, 1, "c12", "twelve"), ct);
                    },
                    async (h, ct) =>
                        (
                            await h
                                .Model.Items.Query()
                                .Where(i => i.ItemId >= 11)
                                .ExecuteDeleteAsync(cancellationToken: ct)
                        )
                            .Should()
                            .Be(2),
                    [Gone(Items, 11), Gone(Items, 12)]
                )
            ),
            Row(
                "delete-all-without-condition",
                "条件なしの DELETE も行ごとに削除として記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareFolderAsync(h, ct);
                        await h.Model.Labels.InsertAsync(Label("L1", 1, "one"), ct);
                        await h.Model.Labels.InsertAsync(Label("L2", 1, "two"), ct);
                        await h.Model.Labels.InsertAsync(Label("L3", 1, "three"), ct);
                    },
                    async (h, ct) =>
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "DELETE FROM model_labels",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(3),
                    [Gone(Labels, "L1"), Gone(Labels, "L2"), Gone(Labels, "L3")]
                )
            ),
            // 主キーの変更
            Row(
                "primary-key-change-int",
                "int の主キーを変える更新は、新しいキーを送る行、古いキーを削除の行として、この順に記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "UPDATE model_folders SET folder_id = 5 WHERE folder_id = 1",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1),
                    [Live(Folders, 5), Gone(Folders, 1)]
                )
            ),
            Row(
                "primary-key-change-text",
                "文字列の主キーを変える更新も、新しいキーを送る行、古いキーを削除の行として記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareFolderAsync(h, ct);
                        await h.Model.Labels.InsertAsync(Label("L1", 1, "one"), ct);
                    },
                    async (h, ct) =>
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "UPDATE model_labels SET label_code = 'L2' WHERE label_code = 'L1'",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1),
                    [Live(Labels, "L2"), Gone(Labels, "L1")]
                )
            ),
            Row(
                "primary-key-change-datetime",
                "datetime2 の主キーを変える更新も、新しいキーを送る行、古いキーを削除の行として記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareEventAsync,
                    async (h, ct) =>
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                $"UPDATE model_events SET event_at = '{LaterEventAtText}' WHERE event_at = '{EventAtText}'",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1),
                    [Live(Events, LaterEventAtText), Gone(Events, EventAtText)]
                )
            ),
            // エンジンだけが書く列（トリガーの ON CONFLICT が名指ししない列）
            Row(
                "engine-columns-kept-by-update",
                "エンジンの列に値を入れた行を更新しても、エンジンの列は保たれる",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    KeepsEngineColumns(
                        RenameFolderAsync,
                        async (h, ct) =>
                            (
                                await h.Model.Folders.UpdateAsync(
                                    Folder(1, "renamed"),
                                    cancellationToken: ct
                                )
                            )
                                .Should()
                                .BeTrue()
                    ),
                    [Live(Folders, 1)]
                )
            ),
            Row(
                "engine-columns-kept-by-delete-then-reinsert",
                "エンジンの列に値を入れた行を消して同じキーで入れ直しても、エンジンの列は保たれる",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    KeepsEngineColumns(
                        RetitleItemAsync,
                        async (h, ct) =>
                        {
                            (await h.Model.Items.DeleteAsync(10, ct)).Should().BeTrue();
                            await h.Model.Items.InsertAsync(Item(10, 1, "c10", "again"), ct);
                        }
                    ),
                    [Live(Items, 10, "Payload")]
                )
            ),
            Row(
                "engine-columns-kept-on-old-key-by-primary-key-change",
                "エンジンの列に値を入れた行の主キーを変えても、古いキーの行（削除の記録）のエンジンの列は保たれる",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    KeepsEngineColumns(
                        RenameFolderAsync,
                        async (h, ct) =>
                            (
                                await h.Model.Sql.ExecuteSqlAsync(
                                    "UPDATE model_folders SET folder_id = 5 WHERE folder_id = 1",
                                    cancellationToken: ct
                                )
                            )
                                .Should()
                                .Be(1)
                    ),
                    [Live(Folders, 5), Gone(Folders, 1)]
                )
            ),
            Row(
                "engine-columns-kept-by-blob-write",
                "エンジンの列に値を入れた行の除外 blob を Stream で書いても、エンジンの列は保たれる",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareItemAsync,
                    KeepsEngineColumns(
                        RetitleItemAsync,
                        async (h, ct) =>
                            (
                                await h.Model.Items.WritePayloadAsync(
                                    10,
                                    Bytes(7, 7),
                                    cancellationToken: ct
                                )
                            )
                                .Should()
                                .BeTrue()
                    ),
                    [Live(Items, 10, "Payload")]
                )
            ),
            // 失敗した書き込み
            Row(
                "failed-insert-unique-violation",
                "一意制約の違反で断られた挿入は何も記録しない",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                        await FluentActions
                            .Awaiting(() => h.Model.Folders.InsertAsync(Folder(2, "root"), ct))
                            .Should()
                            .ThrowAsync<SqliteException>(),
                    []
                )
            ),
            Row(
                "failed-bulk-insert",
                "途中の行で断られた一括挿入は、前の行も含めて何も記録しない",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                        await FluentActions
                            .Awaiting(() =>
                                h.Model.Items.BulkInsertAsync(
                                    [Item(10, 1, "c10", "ten"), Item(11, 1, "c10", "dup")],
                                    ct
                                )
                            )
                            .Should()
                            .ThrowAsync<SqliteException>(),
                    []
                )
            ),
            Row(
                "failed-graph-save",
                "途中の書き込みで例外になったグラフ保存は、前の書き込みも含めて何も記録しない",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                    {
                        var folder = Folder(2, "child", parentId: 1);
                        folder.ModelItems.Add(Item(10, 2, "same", "ten"));
                        folder.ModelItems.Add(Item(11, 2, "same", "eleven"));
                        folder.MarkAdded(includeChildren: true);
                        await FluentActions
                            .Awaiting(() =>
                                h.Model.Folders.SaveAsync(folder, cancellationToken: ct)
                            )
                            .Should()
                            .ThrowAsync<Exception>();
                    },
                    []
                )
            ),
            // 外部キーの動作による連鎖
            Row(
                "cascade-delete-and-set-null",
                "外部キーの CASCADE と SET NULL で連鎖した子の削除と更新も記録する",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareEventAsync(h, ct);
                        await h.Model.Folders.InsertAsync(Folder(2, "child", parentId: 1), ct);
                    },
                    // 連鎖は DB の外部キーの動作だけが行う（生 SQL で親だけを消す）
                    async (h, ct) =>
                        (
                            await h.Model.Sql.ExecuteSqlAsync(
                                "DELETE FROM model_folders WHERE folder_id = 1",
                                cancellationToken: ct
                            )
                        )
                            .Should()
                            .Be(1),
                    // SQLite は親の行の外部キーの動作（子・孫の削除、子の NULL 化）を、親の AFTER DELETE のトリガーより先に行う
                    [
                        Gone(Events, EventAtText),
                        Gone(Items, 10),
                        Live(Folders, 2),
                        Gone(Folders, 1),
                    ],
                    AlterDiagram: WithActions(
                        ("FK_model_items_model_folders", ForeignKeyReferentialAction.Cascade),
                        ("FK_model_events_model_items", ForeignKeyReferentialAction.Cascade),
                        ("FK_model_folders_model_folders", ForeignKeyReferentialAction.SetNull)
                    )
                )
            ),
            // 適用中の印
            Row(
                "applying-within-transaction-is-not-recorded",
                "同じトランザクションで適用中の印を立てて書き、印を下ろしてコミットした書き込みは記録しない（後の書き込みは記録する）",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                    {
                        await h.Model.Sql.ExecuteSqlBatchAsync(
                            [
                                "UPDATE quicker_sync_state SET applying = 1",
                                "INSERT INTO model_labels (label_code, folder_id, caption) VALUES ('L9', 1, 'applied')",
                                "UPDATE model_folders SET name = 'applied' WHERE folder_id = 1",
                                "UPDATE quicker_sync_state SET applying = 0",
                            ],
                            ct
                        );
                        await h.Model.Labels.InsertAsync(Label("L2", 1, "local"), ct);
                    },
                    [Live(Labels, "L2")]
                )
            ),
            Row(
                "applying-rolled-back-restores-flag",
                "適用中の印を立てて書いた後にロールバックすると、印は 0 に戻り、何も残らない（後の書き込みは記録する）",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                    {
                        await FluentActions
                            .Awaiting(() =>
                                h.Model.Sql.ExecuteSqlBatchAsync(
                                    [
                                        "UPDATE quicker_sync_state SET applying = 1",
                                        "INSERT INTO model_labels (label_code, folder_id, caption) VALUES ('L9', 1, 'applied')",
                                        "INSERT INTO model_labels (label_code, folder_id, caption) VALUES ('L9', 1, 'duplicate')",
                                    ],
                                    ct
                                )
                            )
                            .Should()
                            .ThrowAsync<SqliteException>();
                        (await h.ReadStateAsync(ct))
                            .Applying.Should()
                            .Be(0, "ロールバックで印も戻る");
                        await h.Model.Labels.InsertAsync(Label("L2", 1, "local"), ct);
                    },
                    [Live(Labels, "L2")]
                )
            ),
            Row(
                "applying-held-blocks-other-connection",
                "適用中の印を立てたトランザクションを持つ間、別の接続の書き込みは終わらず、コミットの後に終わって記録される",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                    {
                        await using var holder = await h.OpenAsync(ct);
                        Execute(holder, "BEGIN IMMEDIATE");
                        Execute(holder, "UPDATE quicker_sync_state SET applying = 1");
                        Execute(
                            holder,
                            "INSERT INTO model_labels (label_code, folder_id, caption) VALUES ('L9', 1, 'applied')"
                        );

                        // SQLite の非同期の口は同期で走るので、待ちが呼び出しのスレッドを塞がないよう別のスレッドで書く
                        var write = Task.Run(
                            () => h.Model.Labels.InsertAsync(Label("L2", 1, "local"), ct),
                            ct
                        );
                        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
                        write
                            .IsCompleted.Should()
                            .BeFalse(
                                "印を立てたトランザクションが書き込みのロックを持っている間は終わらない"
                            );

                        Execute(holder, "UPDATE quicker_sync_state SET applying = 0");
                        Execute(holder, "COMMIT");
                        await write;
                    },
                    [Live(Labels, "L2")]
                )
            ),
            // トリガーを張らない表・張る前の行
            Row(
                "untracked-table-is-not-recorded",
                "Ensure に渡さなかった表にはトリガーが無く、書いても記録しない",
                new(
                    SyncTriggerFixture.SyncModel,
                    PrepareFolderAsync,
                    async (h, ct) =>
                    {
                        await h.Model.Labels.InsertAsync(Label("L1", 1, "one"), ct);
                        await h.Model.Folders.InsertAsync(Folder(2, "second"), ct);
                    },
                    [Live(Folders, 2)],
                    TrackedTables: [Folders, Items, Events]
                )
            ),
            Row(
                "prepared-rows-are-not-recorded",
                "トリガーを張る前からあった行は、触らない限り記録に無い",
                new(
                    SyncTriggerFixture.SyncModel,
                    async (h, ct) =>
                    {
                        await PrepareEventAsync(h, ct);
                        await h.Model.Folders.InsertAsync(Folder(2, "second"), ct);
                        await h.Model.Labels.InsertAsync(Label("L1", 1, "one"), ct);
                    },
                    (h, ct) => h.Model.Folders.InsertAsync(Folder(3, "third"), ct),
                    [Live(Folders, 3)]
                )
            ),
        ];

        return new ScenarioTable<SyncTriggerRecordingCase>(EngineIds.GeneratedRuntime, rows);
    }
}
