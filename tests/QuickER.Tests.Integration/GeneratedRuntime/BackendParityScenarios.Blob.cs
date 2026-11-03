using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 無制限バイナリ列の除外（<c>ExcludeUnboundedBinaryColumns</c>）と Stream アクセサのシナリオ：読み取りで除外列が未取得になること・
/// 除外列に値を残した更新を断ること・INSERT と BulkInsert は全列を書くこと・<c>WithUnboundedBinary()</c> の取得と Include との併用の拒否・
/// 射影・生 SQL のエンティティ取得・Stream の契約（行なしと NULL・NULL の設定・空の値・シークできない Stream の長さ・長さの食い違い・
/// 渡した Stream を閉じないこと・ファイル糖衣）。
/// </summary>
/// <remarks>
/// 生成コードのランタイムテスト（BinaryColumn の系統）から移した。元の図（BinaryFixture の <c>documents</c>＝payload は NULL 許容・
/// thumb は NOT NULL の除外 blob → <c>document_notes</c>）は、注文（同じ形の payload・thumb を持つ）→ 明細へ写した。元の有界バイナリ
/// <c>checksum</c> と <c>row_ver</c>・名前付きクエリ・EditModel は、この図に無いので元のテストに残した。EF Core は除外列も全列で読み書きし、
/// Stream アクセサを持たない（docs に書いてある違い）ので、その上に立つシナリオは基準と比べず、実装先ごとの期待で書く。
/// </remarks>
public static partial class BackendParityScenarios
{
    /// <summary>
    /// 読み取りが返す未取得状態の除外 blob（payload＝null。NOT NULL の thumb も値オブジェクトの列なので null＝初期化子の <c>null!</c>）
    /// </summary>
    private const string UnsetBlobs = "payload=null thumb=null";

    /// <summary>Stream の往復に使う大きな blob（Stream のコピーの区切り 81,920 バイトを 2 度またぐ。元のテストの 3 MB から下げた）</summary>
    private static byte[] LargeBlob => RandomBytes(200_000, 1234);

    /// <summary>ファイル糖衣の往復に使う blob（同じく区切りを 2 度またぐ）</summary>
    private static byte[] FileBlob => RandomBytes(200_000, 99);

    /// <summary>種から決まるバイト列</summary>
    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);

        return bytes;
    }

    /// <summary><see cref="ReadBlobOp"/> が読めたときの戻り値</summary>
    private static string ReadBytes(byte[] bytes) =>
        $"read=True bytes={Convert.ToHexString(bytes)}";

    private static IEnumerable<ParityScenario> BlobRuntimeScenarios()
    {
        // 読み取り（GetById・GetAll）は除外 blob を未取得状態で返し、INSERT は全列を書き、除外 blob が未取得のままの UpdateAsync と
        // グラフ保存は blob を残し、除外列に値を残した UpdateAsync とグラフ保存は断る（リモートは送る前にクライアントで断る）。
        // EF Core は除外列も全列で読み書きする（docs に書いてある違い）ので、基準と比べず実装先ごとの期待で書く
        yield return new(
            "blob-excluded-columns-read-and-write-guard",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                ParityExpect.SlotBlobs(
                    "excluded-get-by-id-unset",
                    S,
                    10,
                    UnsetBlobs,
                    efCore: Order10Blobs
                ),
                new GetAllOrdersOp(),
                ParityExpect.OutcomeByKind(
                    "excluded-get-all-unset",
                    $"orders=[10:{UnsetBlobs},11:{UnsetBlobs},12:{UnsetBlobs}]",
                    efCore: $"orders=[10:{Order10Blobs},11:payload=null thumb=EE,12:payload=null thumb=0C]"
                ),
                new InsertOp(new OrderRow(13, 1, 5m, "n", Payload: [0x05], Thumb: [0x06])),
                ParityExpect.StateRow(
                    "excluded-insert-writes-all-columns",
                    "orders/13",
                    "payload=05",
                    "thumb=06"
                ),
                new LoadOp("I", "orders", 13),
                ParityExpect.SlotBlobs(
                    "excluded-inserted-row-reads-unset",
                    "I",
                    13,
                    UnsetBlobs,
                    efCore: "payload=05 thumb=06"
                ),
                new EditMemoOp(S, 10, "edited"),
                new UpdateOrderOp(S),
                ParityExpect.Outcome("excluded-unset-update-succeeds", "updated=True"),
                ParityExpect.StateRow(
                    "excluded-unset-update-keeps-blobs",
                    "orders/10",
                    "memo='edited'",
                    Order10Blobs
                ),
                new LoadOp("G", "orders", 12),
                new AttachLineOp("G", 102),
                new EditMemoOp("G", 12, "graph"),
                new SaveOp("G"),
                ParityExpect.Outcome("excluded-unset-graph-save-succeeds", "rows=1"),
                ParityExpect.StateRow(
                    "excluded-unset-graph-save-keeps-blobs",
                    "orders/12",
                    "memo='graph'",
                    "payload=null thumb=0C"
                ),
                new LoadOp("U", "orders", 10),
                new AssignBlobOp("U", 10, "payload", [0x01, 0x02, 0x03]),
                new MessageOp(new UpdateOrderOp("U")),
                ParityExpect.OutcomeByKind(
                    "excluded-assigned-update-is-refused",
                    "throws InvalidOperationException",
                    efCore: "updated=True"
                ),
                ParityExpect.MessageContains(
                    "excluded-assigned-update-names-column-and-raw-sql",
                    kind => kind != ParityBackendKind.EfCore,
                    "Payload",
                    "ExecuteSqlAsync"
                ),
                ParityExpect.Either(
                    "excluded-assigned-update-stored",
                    kind => kind == ParityBackendKind.EfCore,
                    ParityExpect.StateRow("written", "orders/10", "payload=010203"),
                    ParityExpect.StateRow("refused", "orders/10", Order10Blobs)
                ),
                new LoadOp("V", "orders", 12),
                new AttachLineOp("V", 102),
                new AssignBlobOp("V", 12, "payload", [0x07, 0x07]),
                new EditMemoOp("V", 12, "assigned"),
                new SaveOp("V"),
                ParityExpect.OutcomeByKind(
                    "excluded-assigned-graph-save-is-refused",
                    "throws InvalidOperationException",
                    efCore: "rows=1"
                ),
                ParityExpect.Either(
                    "excluded-assigned-graph-save-stored",
                    kind => kind == ParityBackendKind.EfCore,
                    ParityExpect.StateRow(
                        "written",
                        "orders/12",
                        "memo='assigned'",
                        "payload=0707"
                    ),
                    ParityExpect.StateRow(
                        "refused",
                        "orders/12",
                        "memo='graph'",
                        "payload=null thumb=0C"
                    )
                ),
            ],
            CompareWithBaseline: false
        );

        // 全機能面（リモートには無い）：Include の取得・Query() の一覧も除外 blob を未取得で返す。WithUnboundedBinary() は除外列を
        // 実データで返し（Unchanged・返却は保存先と配列を共有しない）、Include との併用は順序と終端に依らず断る。射影は除外列を参照すれば
        // 値を読み、フォールバックの経路は WithUnboundedBinary() のときだけ読む。BulkInsert は全列を書く。WithUnboundedBinary() で読んだ行の
        // UpdateAsync は除外列のガードで断り、除外列を未取得へ戻せば通って blob を残す（EF Core は全列を読み書きする）
        yield return new(
            "blob-excluded-columns-full-surface",
            Basic,
            [
                new LoadOrderWithLinesOp(S, 10),
                ParityExpect.SlotBlobs(
                    "excluded-include-unset",
                    S,
                    10,
                    UnsetBlobs,
                    efCore: Order10Blobs
                ),
                ParityExpect.ChildrenOf("excluded-include-reads-children", S, 100, 101),
                new WithBinaryReadOp(ParityBinaryRead.PlainToList),
                ParityExpect.OutcomeByKind(
                    "excluded-query-list-unset",
                    $"[10:{UnsetBlobs} state=Unchanged,11:{UnsetBlobs} state=Unchanged,12:{UnsetBlobs} state=Unchanged]",
                    efCore: $"[10:{Order10Blobs} state=Unchanged,11:payload=null thumb=EE state=Unchanged,12:payload=null thumb=0C state=Unchanged]"
                ),
                new WithBinaryReadOp(ParityBinaryRead.First, Slot: "W"),
                ParityExpect.Outcome(
                    "with-binary-first-reads-blobs",
                    $"[10:{Order10Blobs} state=Unchanged]"
                ),
                new WithBinaryReadOp(ParityBinaryRead.ToList),
                ParityExpect.Outcome(
                    "with-binary-list-reads-blobs",
                    $"[10:{Order10Blobs} state=Unchanged,11:payload=null thumb=EE state=Unchanged,12:payload=null thumb=0C state=Unchanged]"
                ),
                new WithBinaryReadOp(ParityBinaryRead.FirstMutateReread),
                ParityExpect.Outcome(
                    "with-binary-returns-independent-copy",
                    $"[10:{Order10Blobs} state=Unchanged] reread=[10:{Order10Blobs} state=Unchanged]"
                ),
                new MessageOp(new WithBinaryReadOp(ParityBinaryRead.IncludeThenWithFirst)),
                ParityExpect.Throws(
                    "with-binary-include-then-with-is-refused",
                    "InvalidOperationException"
                ),
                ParityExpect.MessageContains(
                    "with-binary-include-refusal-names-include",
                    _ => true,
                    "Include"
                ),
                new WithBinaryReadOp(ParityBinaryRead.WithThenIncludeToList),
                ParityExpect.Throws(
                    "with-binary-with-then-include-is-refused",
                    "InvalidOperationException"
                ),
                new WithBinaryReadOp(ParityBinaryRead.IncludeThenWithCount),
                ParityExpect.Throws(
                    "with-binary-include-count-is-refused",
                    "InvalidOperationException"
                ),
                new ProjectOrdersOp(ParityProjection.Payload, Customer: 1),
                ParityExpect.Outcome(
                    "projection-of-excluded-column-reads-it",
                    "rows=[10:0102,11:null]"
                ),
                new ProjectOrdersOp(ParityProjection.PayloadFallback, Customer: 1),
                ParityExpect.OutcomeByKind(
                    "projection-fallback-leaves-excluded-unset",
                    "rows=[10:null,11:null]",
                    efCore: "rows=[10:0102,11:null]"
                ),
                new ProjectOrdersOp(
                    ParityProjection.PayloadFallbackWithUnboundedBinary,
                    Customer: 1
                ),
                ParityExpect.Outcome(
                    "projection-fallback-honors-with-binary",
                    "rows=[10:0102,11:null]"
                ),
                new BulkInsertOp([
                    new OrderRow(14, 1, 7m, Payload: [0x07, 0x07, 0x07], Thumb: [0x01]),
                ]),
                ParityExpect.Outcome("bulk-insert-with-blobs", "inserted=1"),
                ParityExpect.StateRow(
                    "bulk-insert-writes-all-columns",
                    "orders/14",
                    "payload=070707",
                    "thumb=01"
                ),
                new EditMemoOp("W", 10, "with-binary"),
                new MessageOp(new UpdateOrderOp("W")),
                ParityExpect.OutcomeByKind(
                    "with-binary-then-update-is-refused",
                    "throws InvalidOperationException",
                    efCore: "updated=True"
                ),
                ParityExpect.MessageContains(
                    "with-binary-then-update-names-raw-sql",
                    kind => kind != ParityBackendKind.EfCore,
                    "ExecuteSqlAsync"
                ),
                ParityExpect.Either(
                    "with-binary-then-update-stored",
                    kind => kind == ParityBackendKind.EfCore,
                    ParityExpect.StateRow(
                        "written",
                        "orders/10",
                        "memo='with-binary'",
                        Order10Blobs
                    ),
                    ParityExpect.StateRow("refused", "orders/10", "memo='first'", Order10Blobs)
                ),
                new WithBinaryReadOp(ParityBinaryRead.First, Slot: "R"),
                new AssignBlobOp("R", 10, "payload", null),
                new AssignBlobOp("R", 10, "thumb", null),
                new EditMemoOp("R", 10, "reset"),
                new UpdateOrderOp("R"),
                ParityExpect.Outcome("with-binary-reset-then-update-succeeds", "updated=True"),
                ParityExpect.Either(
                    "with-binary-reset-then-update-stored",
                    kind => kind == ParityBackendKind.EfCore,
                    ParityExpect.StateRow(
                        "written",
                        "orders/10",
                        "memo='reset'",
                        "payload=null thumb=empty"
                    ),
                    ParityExpect.StateRow("kept", "orders/10", "memo='reset'", Order10Blobs)
                ),
            ],
            CompareWithBaseline: false
        );

        // 生 SQL のエンティティ取得（QuickER 版と EF Core だけ）：SELECT * は除外列も取り込み、除外列を選ばない取得は QuickER 版では
        // 未取得のまま読め（EF Core は除外列を必須にして断る＝#36）、必須の列の足りない取得は断る
        yield return new(
            "blob-raw-sql-entity-retrieval",
            Basic,
            [
                new QueryOrdersBySqlOp(S, "SELECT * FROM orders WHERE order_id = 10"),
                ParityExpect.Outcome("raw-sql-select-star-reads-row", "rows=1"),
                ParityExpect.SlotBlobs("raw-sql-select-star-maps-blobs", S, 10, Order10Blobs),
                new QueryOrdersBySqlOp(
                    "B",
                    "SELECT order_id, customer_id, amount, memo, ordered_at, is_paid FROM orders WHERE order_id = 10"
                ),
                ParityExpect.RawSqlRead("raw-sql-without-blobs", adoReads: true),
                ParityExpect.ForKind(
                    ParityBackendKind.Ado,
                    ParityExpect.SlotBlobs("raw-sql-without-blobs-unset", "B", 10, UnsetBlobs)
                ),
                ParityExpect.ForKind(
                    ParityBackendKind.Ado,
                    ParityExpect.SlotRow(
                        "raw-sql-without-blobs-values",
                        "B",
                        "orders/10",
                        "memo='first'"
                    )
                ),
                new QueryOrdersBySqlOp("C", "SELECT order_id FROM orders"),
                ParityExpect.Throws(
                    "raw-sql-missing-required-columns-is-refused",
                    "InvalidOperationException"
                ),
            ],
            CompareWithBaseline: false
        );

        // Stream アクセサの契約（EF Core には無い）：長さの食い違いは ArgumentException（length）で断り blob を変えない・シークできない
        // Stream は length が要る・行なしと NULL は false・source=null で NULL にし行は残る・空の Stream は 0 バイトを書く（NULL と区別）・
        // 大きな blob の往復・ファイル糖衣の往復・渡した Stream を閉じない（巻き戻して別の列へ再送できる）・小さい値の再書き込みは全置換
        var nonSeekable = RandomBytes(8192, 7);
        var reusable = RandomBytes(4096, 77);
        var thumbLarge = RandomBytes(64 * 1024, 20260719);
        var thumbSmall = RandomBytes(1024, 20260720);

        yield return new(
            "blob-stream-contract",
            Basic,
            [
                new WriteBlobStreamOp(10, "payload", ParityBlobSource.MismatchSeekable),
                ParityExpect.Outcome(
                    "stream-length-differs-from-seekable-is-refused",
                    "throws ArgumentException param=length"
                ),
                ParityExpect.StateRow(
                    "stream-length-differs-keeps-blob",
                    "orders/10",
                    Order10Blobs
                ),
                new WriteBlobStreamOp(10, "payload", ParityBlobSource.MismatchShort),
                ParityExpect.Outcome(
                    "stream-shorter-than-length-is-refused",
                    "throws ArgumentException param=length"
                ),
                ParityExpect.StateRow("stream-shorter-keeps-blob", "orders/10", Order10Blobs),
                new WriteBlobStreamOp(10, "payload", ParityBlobSource.MismatchLong),
                ParityExpect.Outcome(
                    "stream-longer-than-length-is-refused",
                    "throws ArgumentException param=length"
                ),
                new ReadBlobOp(10, "payload"),
                ParityExpect.Outcome("stream-mismatch-keeps-blob", "read=True bytes=0102"),
                new MessageOp(
                    new WriteBlobStreamOp(10, "payload", ParityBlobSource.NonSeekable, nonSeekable)
                ),
                ParityExpect.Outcome(
                    "stream-non-seekable-without-length-is-refused",
                    "throws ArgumentException param=length"
                ),
                ParityExpect.MessageContains(
                    "stream-non-seekable-refusal-names-length",
                    _ => true,
                    "length"
                ),
                new WriteBlobStreamOp(
                    10,
                    "payload",
                    ParityBlobSource.NonSeekable,
                    nonSeekable,
                    nonSeekable.Length
                ),
                ParityExpect.Outcome("stream-non-seekable-with-length-writes", "written=True"),
                new ReadBlobOp(10, "payload"),
                ParityExpect.Outcome("stream-non-seekable-round-trips", ReadBytes(nonSeekable)),
                new ReadBlobOp(11, "payload"),
                ParityExpect.Outcome("stream-read-null-column-is-false", "read=False bytes="),
                new ReadBlobOp(99, "payload"),
                ParityExpect.Outcome("stream-read-missing-row-is-false", "read=False bytes="),
                new WriteBlobOp(99, "payload", [0x01, 0x02, 0x03]),
                ParityExpect.Outcome("stream-write-missing-row-is-false", "written=False"),
                new WriteBlobOp(99, "payload", null),
                ParityExpect.Outcome("stream-clear-missing-row-is-false", "written=False"),
                ParityExpect.StateRows("stream-missing-row-writes-nothing", 8),
                new WriteBlobOp(10, "payload", null),
                ParityExpect.Outcome("stream-clear-sets-null", "written=True"),
                new ReadBlobOp(10, "payload"),
                ParityExpect.Outcome("stream-cleared-column-reads-false", "read=False bytes="),
                ParityExpect.StateRow(
                    "stream-clear-keeps-row",
                    "orders/10",
                    "memo='first'",
                    "payload=null thumb=0A"
                ),
                new WriteBlobOp(10, "payload", []),
                ParityExpect.Outcome("stream-empty-writes", "written=True"),
                new ReadBlobOp(10, "payload"),
                ParityExpect.Outcome("stream-empty-is-zero-bytes-not-null", "read=True bytes="),
                ParityExpect.StateRow("stream-empty-is-stored-empty", "orders/10", "payload=empty"),
                new WriteBlobStreamOp(10, "payload", ParityBlobSource.Seekable, LargeBlob),
                ParityExpect.Outcome(
                    "stream-large-write-leaves-source-open",
                    "written=True open=True position=200000"
                ),
                new ReadBlobOp(10, "payload"),
                ParityExpect.Outcome("stream-large-round-trips", ReadBytes(LargeBlob)),
                new BlobFileOp(10, "payload", Write: true, FileBlob),
                ParityExpect.Outcome("file-sugar-writes", "written=True"),
                new BlobFileOp(10, "payload", Write: false),
                ParityExpect.Outcome("file-sugar-round-trips", ReadBytes(FileBlob)),
                new WriteBlobStreamOp(
                    10,
                    "payload",
                    ParityBlobSource.Seekable,
                    reusable,
                    ResendTo: "thumb"
                ),
                ParityExpect.Outcome(
                    "stream-source-stays-open-and-can-be-resent",
                    "written=True open=True position=4096 resent=True"
                ),
                ParityExpect.StateRow(
                    "stream-resent-to-second-column",
                    "orders/10",
                    $"payload={Convert.ToHexString(reusable)}",
                    $"thumb={Convert.ToHexString(reusable)}"
                ),
                new WriteBlobStreamOp(12, "thumb", ParityBlobSource.Seekable, thumbLarge),
                new ReadBlobOp(12, "thumb"),
                ParityExpect.Outcome("stream-thumb-round-trips", ReadBytes(thumbLarge)),
                new WriteBlobOp(12, "thumb", thumbSmall),
                new ReadBlobOp(12, "thumb"),
                ParityExpect.Outcome("stream-rewrite-replaces-whole-blob", ReadBytes(thumbSmall)),
            ]
        );

        // Stream で書いた blob は WithUnboundedBinary() の取得でも見え、それより前に WithUnboundedBinary() で読んでいた行は書き込みの
        // 影響を受けない（インメモリのストアの行を直接書き換えない）。Stream アクセサが無い EF Core・全機能面が無いリモートでは流さない
        yield return new(
            "blob-stream-write-then-with-unbounded-binary",
            Basic,
            [
                new WithBinaryReadOp(ParityBinaryRead.First, Slot: "W"),
                new WriteBlobOp(10, "payload", [0x5A, 0x5B]),
                ParityExpect.Outcome("stream-write-before-with-binary-read", "written=True"),
                ParityExpect.SlotBlobs("stream-write-leaves-held-row", "W", 10, Order10Blobs),
                new WithBinaryReadOp(ParityBinaryRead.First),
                ParityExpect.Outcome(
                    "with-binary-sees-stream-write",
                    "[10:payload=5A5B thumb=0A state=Unchanged]"
                ),
            ]
        );

        // EF Core の Stream アクセサは NotSupportedException（文言に EF Core を含む）で断る。ほかの実装先は読み書きできる
        yield return new(
            "blob-stream-not-supported-on-ef-core",
            Basic,
            [
                new MessageOp(new ProbeBlobStreamOp(Write: false)),
                ParityExpect.OutcomeByKind(
                    "stream-read-on-ef-core-is-not-supported",
                    "read=True bytes=0102",
                    efCore: "throws NotSupportedException"
                ),
                ParityExpect.MessageContains(
                    "stream-ef-core-refusal-names-ef-core",
                    kind => kind == ParityBackendKind.EfCore,
                    "EF Core"
                ),
                new ProbeBlobStreamOp(Write: true),
                ParityExpect.OutcomeByKind(
                    "stream-write-on-ef-core-is-not-supported",
                    "written=True",
                    efCore: "throws NotSupportedException"
                ),
            ],
            CompareWithBaseline: false
        );
    }
}

/// <summary>無制限バイナリ列のシナリオの期待</summary>
public static partial class ParityExpect
{
    /// <summary>
    /// スロットのグラフの注文（<paramref name="orderId"/>）の除外 blob が、<paramref name="expected"/>（<c>payload=0102 thumb=0A</c> の形・
    /// 未取得は <c>payload=null thumb=null</c>）である（EF Core は <paramref name="efCore"/>＝全列を読むので値が入る）
    /// </summary>
    public static ExpectOp SlotBlobs(
        string label,
        string slot,
        int orderId,
        string expected,
        string? efCore = null
    ) =>
        new(
            label,
            e =>
            {
                if (!e.Backend.Slots.TryGetValue(slot, out var root))
                {
                    return $"slot '{slot}' is empty";
                }

                var key = $"orders/{orderId}";
                var order = Walk(root)
                    .OfType<OrderEntity>()
                    .FirstOrDefault(o => ParityFormat.KeyOf(o) == key);

                if (order is null)
                {
                    return $"'{key}' is not in slot '{slot}'";
                }

                var wanted =
                    e.Kind == ParityBackendKind.EfCore && efCore is not null ? efCore : expected;
                var actual = ParityBlobs.Of(order);

                return actual == wanted ? null : $"blobs {actual}, expected {wanted}";
            }
        );

    /// <summary>
    /// 直前の <see cref="MessageOp"/> が受けた例外の文言が、断片をすべて含む（<paramref name="when"/> を満たす実装先だけを見る。ほかの
    /// 実装先は満たしたとみなす）
    /// </summary>
    public static ExpectOp MessageContains(
        string label,
        Func<ParityBackendKind, bool> when,
        params string[] fragments
    ) =>
        new(
            label,
            e =>
            {
                if (!when(e.Kind))
                {
                    return null;
                }

                var message = e.Backend.LastExceptionMessage;
                var missing = fragments
                    .Where(f => message is null || !message.Contains(f, StringComparison.Ordinal))
                    .ToList();

                return missing.Count == 0
                    ? null
                    : $"message '{message}' lacks [{string.Join(", ", missing)}]";
            }
        );
}
