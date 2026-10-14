using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 一致テストの決まった形のシナリオ。どれも QuickER 版（SQLite 実ファイル）を基準に、ほかの実装先が同じ結果になることを
/// 表明する。今わかっている違いは <see cref="BackendParityKnownDifferences"/> が名指しする。
/// </summary>
public static class BackendParityScenarios
{
    private const string S = "A";

    /// <summary>顧客 1・注文 10（明細 100・101）・注文 11（明細なし）・注文 12（明細 102）の種</summary>
    private static readonly IReadOnlyList<ParityOp> Basic =
    [
        new SeedOp(new CustomerRow(1, "Alice", "vip")),
        new SeedOp(new CustomerRow(2, "Bob")),
        new SeedOp(new OrderRow(10, 1, 100m, "first", Payload: [0x01, 0x02], Thumb: [0x0A])),
        new SeedOp(new OrderRow(11, 1, 50m)),
        new SeedOp(new OrderRow(12, 2, 30m, Thumb: [0x0C])),
        new SeedOp(new LineRow(100, 10, "pen", 2, 0.5m)),
        new SeedOp(new LineRow(101, 10, "ink", 5)),
        new SeedOp(new LineRow(102, 12, "mug", 1)),
    ];

    /// <summary>全シナリオ</summary>
    public static IReadOnlyList<ParityScenario> All { get; } =
    [
        // 直接の CRUD と、読み込み→編集→UpdateAsync（どの実装先も同じ結果になるはずの土台）
        new(
            "crud-basic",
            Basic,
            [
                new InsertOp(new CustomerRow(3, "Carol", null)),
                new InsertOp(new OrderRow(13, 3, 12.5m, "n", Payload: [0x05], Thumb: [0x06])),
                new InsertOp(new LineRow(103, 13, "cup", 3, 1.25m)),
                new LoadOp(S, "orders", 13),
                new EditMemoOp(S, 13, null),
                new UpdateOrderOp(S),
                new LoadOp("B", "orders", 99),
                new DeleteOp("order_lines", 103),
                new DeleteOp("order_lines", 103),
            ]
        ),
        // 根を読み、明細を個別に読んで足したグラフ（リモートでも組める形）を編集・保存する
        new(
            "graph-edit-through-root",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new AttachLineOp(S, 100),
                new AttachLineOp(S, 101),
                new EditMemoOp(S, 10, "edited"),
                new EditQuantityOp(S, 101, 9),
                new AddLineOp(S, new LineRow(104, 10, "new", 1)),
                new SaveOp(S),
            ]
        ),
        // F4: 根を通して明細 1 件を削除する。保存が成功したら、削除した子は親のコレクションから外れ、子の RowState は
        // Removed のまま（決定 2026-10-13＝全実装先を EF Core と EditModelCollection の規則にそろえる）
        new(
            "graph-remove-child-through-root",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new AttachLineOp(S, 100),
                new AttachLineOp(S, 101),
                new KeepOp(S, "order_lines/100", "held100"),
                new MarkRemovedOp(S, "order_lines/100"),
                new SaveOp(S),
                ParityExpect.ChildrenOf("F4-removed-child-leaves-collection", S, 101),
                ParityExpect.RootState(
                    "F4-removed-child-stays-removed",
                    "held100",
                    RowState.Removed
                ),
            ]
        ),
        // F4: 外すのは保存が実際に削除した子だけ。Before フックが飛ばした子はコレクションに Removed のまま残る
        new(
            "graph-remove-children-one-skipped-by-hook",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new AttachLineOp(S, 100),
                new AttachLineOp(S, 101),
                new KeepOp(S, "order_lines/101", "held101"),
                new MarkRemovedOp(S, "order_lines/100"),
                new MarkRemovedOp(S, "order_lines/101"),
                new SaveOp(S),
                ParityExpect.ChildrenOf("F4-skipped-child-stays-in-collection", S, 100),
                ParityExpect.RootState(
                    "F4-deleted-child-stays-removed",
                    "held101",
                    RowState.Removed
                ),
            ],
            new ParityBackendOptions(SkipBefore: ["Delete:order_lines/100"])
        ),
        // F4 の決定で残した差（受容）: 外した子への参照を持ったまま、その子を直接もう一度保存する。QuickER 版と
        // インメモリは 0 行の削除を黙認し、EF Core は行なしとして NotFound にする（「行なし削除」の既知の非対称）
        new(
            "save-removed-child-again",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new AttachLineOp(S, 100),
                new KeepOp(S, "order_lines/100", "held100"),
                new MarkRemovedOp(S, "order_lines/100"),
                new SaveOp(S),
                new SaveMemberOp("held100", "order_lines/100"),
            ]
        ),
        // 根ごと削除する（F5: 削除フックの順序と、Added の子孫にフックが走るか）
        new(
            "graph-remove-root-with-children",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new AttachLineOp(S, 100),
                new AttachLineOp(S, 101),
                new AddLineOp(S, new LineRow(104, 10, "new", 1)),
                new MarkRemovedOp(S, "orders/10"),
                new SaveOp(S),
                // F5: 削除フックは子孫から順に走り、Added の子孫にも走る（決定 2026-10-13＝QuickER 版にそろえる）
                ParityExpect.Hooks(
                    "F5-delete-hooks-children-first",
                    "Before",
                    "Delete:order_lines/100",
                    "Delete:order_lines/101",
                    "Delete:order_lines/104",
                    "Delete:orders/10"
                ),
            ]
        ),
        // 他者が消した行を、insertWhenUpdateMissing で保存し直す（1 行・境界の下）
        new(
            "insert-when-missing-single",
            Basic,
            [
                new NewRootOp(S, new OrderRow(20, 1, 1m, "re", UnsetBlobs: true), RowState.Updated),
                new SaveOp(S, new(InsertWhenUpdateMissing: true)),
            ]
        ),
        // #33: 他者が消した 9 行を、insertWhenUpdateMissing のグラフ保存 1 回で保存し直す（やり直しの上限の下）
        MissingRows("insert-when-missing-9-rows", 9, withHooks: true),
        // #33: 同じく 10 行（やり直しの上限に当たる）
        MissingRows("insert-when-missing-10-rows", 10, withHooks: true),
        // #33: Save フックなしの経路でも同じ
        MissingRows("insert-when-missing-10-rows-no-hooks", 10, withHooks: false),
        // #34: Save フックありの経路で、削除する A と行の無い Modified の B を insertWhenUpdateMissing で保存する
        // （自動セーブポイントあり＝今も通る対照）
        DeletedThenMissing("deleted-then-missing-with-savepoints", disableSavepoints: false),
        // #34: 同じ保存を、自動セーブポイントなし（SQL Server の MARS と同じ状態）で
        DeletedThenMissing("deleted-then-missing-without-savepoints", disableSavepoints: true),
        // #36: 生 SQL で除外 blob を選ばずに読み、UpdateAsync する（QuickER 版と EF Core だけ）。EF Core は全列を書くので、
        // 生 SQL のエンティティ取得で除外 blob も必須にし、選ばない取得は断る（決定 2026-10-14）。QuickER 版は除外 blob を
        // 読まずに更新できる。違いそのものが決めた振る舞いなので、基準と比べず実装先ごとの期待で判定する
        new(
            "raw-sql-without-blob-then-update",
            Basic,
            [
                new QueryOrdersBySqlOp(
                    S,
                    "SELECT order_id, customer_id, amount, memo, ordered_at, is_paid FROM orders WHERE order_id = 10"
                ),
                ParityExpect.RawSqlRead("36-ef-refuses-without-blobs", adoReads: true),
                new EditMemoOp(S, 10, "raw"),
                new UpdateOrderOp(S),
                ParityExpect.OrderRow(
                    "36-blobs-kept",
                    10,
                    ado: "orders/10 customer=1 amount=100 memo='raw' at=null paid=False payload=0102 thumb=0A",
                    efCore: "orders/10 customer=1 amount=100 memo='first' at=null paid=False payload=0102 thumb=0A"
                ),
            ],
            CompareWithBaseline: false
        ),
        // #36: 生 SQL で NOT NULL の除外 blob（thumb）だけを選び、NULL 許容の除外 blob（payload）を選ばずに読んで
        // UpdateAsync する。EF Core は取得の時点で payload が無いことを断り、QuickER 版は値の入った除外列の UPDATE を断る
        // （docs の契約）。どちらも何も書かない
        new(
            "raw-sql-with-one-blob-then-update",
            Basic,
            [
                new QueryOrdersBySqlOp(
                    S,
                    "SELECT order_id, customer_id, amount, memo, ordered_at, is_paid, thumb FROM orders WHERE order_id = 10"
                ),
                ParityExpect.RawSqlRead("36-ef-refuses-without-payload", adoReads: true),
                new EditMemoOp(S, 10, "raw"),
                new UpdateOrderOp(S),
                ParityExpect.OrderRow(
                    "36-nothing-written",
                    10,
                    ado: "orders/10 customer=1 amount=100 memo='first' at=null paid=False payload=0102 thumb=0A",
                    efCore: "orders/10 customer=1 amount=100 memo='first' at=null paid=False payload=0102 thumb=0A"
                ),
            ],
            CompareWithBaseline: false
        ),
        // 列だけの条件でカスケードの一括削除（リモートには無い）
        new(
            "execute-delete-cascade-column-predicate",
            Basic,
            [new ExecuteDeleteOrdersOp(ParityOrderPredicate.AmountAtLeast100, CascadeDelete: true)]
        ),
        // #37: カスケードの一括削除をナビゲーションの条件で流す。EF Core は最初に根のキーを読み、そのキーで子孫から根まで
        // 消す＝条件に合う根（明細を持つ注文 10・12）と明細がすべて消える（決定 2026-10-13）。QuickER 版とインメモリは
        // ナビゲーションの条件を NotSupportedException で断り、何も変えない（docs に書いてある違い）。基準と比べること
        // 自体に意味が無いので、実装先ごとの期待だけで判定する
        new(
            "execute-delete-cascade-navigation-predicate",
            Basic,
            [
                new ExecuteDeleteOrdersOp(ParityOrderPredicate.HasLines, CascadeDelete: true),
                ParityExpect.NavigationPredicateDelete("37-roots-and-descendants-all-or-nothing"),
            ],
            CompareWithBaseline: false
        ),
        // F6: 循環するカスケード（顧客の自己参照）の一括削除は、どの実装先も NotSupportedException で断り、何も変えない
        // （EF Core が断る前に文を流さないことは EfCoreExecuteDeleteRuntimeTests が DELETE の数で確かめる）
        new(
            "execute-delete-cyclic-cascade",
            Basic,
            [new ExecuteDeleteCustomersOp(CascadeDelete: true)]
        ),
        // 除外 blob の Stream アクセサ（EF Core には無い）
        new(
            "blob-accessors",
            Basic,
            [
                new ReadBlobOp(10, "payload"),
                new WriteBlobOp(10, "payload", [0x0F, 0x0E]),
                new WriteBlobOp(11, "payload", null),
                new WriteBlobOp(12, "thumb", [0x77]),
                new ReadBlobOp(12, "thumb"),
                new WriteBlobOp(99, "payload", [0x01]),
                new ReadBlobOp(99, "payload"),
            ]
        ),
        // 制約違反：親の無い明細・無い顧客を指す注文（外部キー）・主キーの重複・一意な名前の重複・グラフ保存の途中の主キーの
        // 重複。どの実装先も断り、何も書かない（例外の型の違いは比べ方がまとめる）
        new(
            "constraint-violations",
            Basic,
            [
                new InsertOp(new LineRow(150, 99, "orphan", 1)),
                new InsertOp(new OrderRow(50, 9, 1m)),
                new InsertOp(new CustomerRow(1, "Dup")),
                new InsertOp(new CustomerRow(5, "Alice")),
                new NewRootOp("x", new OrderRow(60, 1, 1m), RowState.Added),
                new AddLineOp("x", new LineRow(160, 60, "a", 1)),
                new AddLineOp("x", new LineRow(100, 60, "dupkey", 1)),
                new SaveOp("x"),
            ]
        ),
        // 親を DeleteAsync(id) で消す（DB の ON DELETE CASCADE が子を消す）
        new(
            "delete-parent-by-id",
            Basic,
            [new DeleteOp("orders", 10), new DeleteOp("customers", 2)]
        ),
    ];

    /// <summary>名前でシナリオを引く</summary>
    public static ParityScenario Get(string name) => All.Single(s => s.Name == name);

    /// <summary>xunit の MemberData（シナリオ名）</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();

        foreach (var scenario in All)
        {
            data.Add(scenario.Name);
        }

        return data;
    }

    /// <summary>
    /// <paramref name="count"/> 行を種まきし、他者がすべて消した後で、Updated の根の列を insertWhenUpdateMissing の
    /// 複数形のグラフ保存 1 回で保存し直す
    /// </summary>
    private static ParityScenario MissingRows(string name, int count, bool withHooks)
    {
        var ids = Enumerable.Range(30, count).ToList();
        var seeds = new List<ParityOp> { new SeedOp(new CustomerRow(1, "Alice")) };
        seeds.AddRange(ids.Select(id => (ParityOp)new SeedOp(new OrderRow(id, 1, id))));
        var ops = new List<ParityOp>();
        ops.AddRange(ids.Select(id => (ParityOp)new OutsideDeleteOp("orders", id)));
        ops.AddRange(
            ids.Select(id =>
                (ParityOp)
                    new NewRootOp(
                        $"o{id}",
                        new OrderRow(id, 1, id + 1, "back", UnsetBlobs: true),
                        RowState.Updated
                    )
            )
        );
        ops.Add(
            new SaveManyOrdersOp(
                [.. ids.Select(id => $"o{id}")],
                new(InsertWhenUpdateMissing: true)
            )
        );

        return new ParityScenario(name, seeds, ops, new ParityBackendOptions(WithHooks: withHooks));
    }

    /// <summary>
    /// 削除する注文 A と、他者が消した注文 B（Updated）を、insertWhenUpdateMissing の複数形のグラフ保存 1 回で保存する。
    /// </summary>
    /// <remarks>
    /// EF Core（SQLite）は同じ表のコマンドを DELETE → UPDATE → INSERT の順に流すので、A の DELETE が流れた後で B の
    /// UPDATE が 0 行になる。自動セーブポイントが無いと、B を INSERT へ切り替えた再試行が A の DELETE をもう一度流す
    /// （A が Added の形は、INSERT が UPDATE より後ろに並ぶので SQLite では再現しない）。
    /// </remarks>
    private static ParityScenario DeletedThenMissing(string name, bool disableSavepoints) =>
        new(
            name,
            [
                new SeedOp(new CustomerRow(1, "Alice")),
                new SeedOp(new OrderRow(20, 1, 20m)),
                new SeedOp(new OrderRow(21, 1, 21m)),
            ],
            [
                new OutsideDeleteOp("orders", 21),
                new LoadOp("a", "orders", 20),
                new MarkRemovedOp("a", "orders/20"),
                new NewRootOp(
                    "b",
                    new OrderRow(21, 1, 22m, "back", UnsetBlobs: true),
                    RowState.Updated
                ),
                new SaveManyOrdersOp(["a", "b"], new(InsertWhenUpdateMissing: true)),
            ],
            new ParityBackendOptions(WithHooks: true, DisableEfCoreSavepoints: disableSavepoints)
        );
}

/// <summary>決定した振る舞いへの期待（<see cref="ExpectOp"/>）を組み立てる</summary>
public static class ParityExpect
{
    /// <summary>スロットの根（注文）のコレクションに、指定の明細がこの順でちょうど並ぶ</summary>
    public static ExpectOp ChildrenOf(string label, string slot, params int[] lineIds) =>
        new(
            label,
            e =>
            {
                var actual = ((OrderEntity)e.Backend.Slots[slot])
                    .OrderLines.Select(l => l.LineId.Value)
                    .ToList();

                return actual.SequenceEqual(lineIds)
                    ? null
                    : $"children [{string.Join(",", actual)}], expected [{string.Join(",", lineIds)}]";
            }
        );

    /// <summary>スロットの根の RowState</summary>
    public static ExpectOp RootState(string label, string slot, RowState expected) =>
        new(
            label,
            e =>
                e.Backend.Slots[slot].RowState == expected
                    ? null
                    : $"RowState {e.Backend.Slots[slot].RowState}, expected {expected}"
        );

    /// <summary>直前の手の、指定した段（Before / After）のフックの呼び出しがこの順でちょうど並ぶ（繰り返しは最初の 1 回へ畳む）</summary>
    public static ExpectOp Hooks(string label, string phase, params string[] calls) =>
        new(
            label,
            e =>
            {
                var actual = e
                    .Previous.RawHooks.Where(h =>
                        h.StartsWith(phase + ":", StringComparison.Ordinal)
                    )
                    .Select(h => h[(phase.Length + 1)..])
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                return actual.SequenceEqual(calls, StringComparer.Ordinal)
                    ? null
                    : $"{phase} [{string.Join(", ", actual)}], expected [{string.Join(", ", calls)}]";
            }
        );

    /// <summary>
    /// #36 の期待：直前の生 SQL の取得が、EF Core では除外 blob の欠けを InvalidOperationException で断り、QuickER 版では
    /// 1 行読める
    /// </summary>
    public static ExpectOp RawSqlRead(string label, bool adoReads) =>
        new(
            label,
            e =>
            {
                var expected =
                    e.Kind == ParityBackendKind.EfCore || !adoReads
                        ? "throws InvalidOperationException"
                        : "rows=1";

                return e.Previous.Outcome == expected
                    ? null
                    : $"outcome {e.Previous.Outcome}, expected {expected}";
            }
        );

    /// <summary>直前の手の後の保存先で、注文の 1 行が実装先ごとに決めた値であること</summary>
    public static ExpectOp OrderRow(string label, int orderId, string ado, string efCore) =>
        new(
            label,
            e =>
            {
                var expected = e.Kind == ParityBackendKind.EfCore ? efCore : ado;
                var actual = e.Previous.State.SingleOrDefault(row =>
                    row.StartsWith($"orders/{orderId} ", StringComparison.Ordinal)
                );

                return actual == expected ? null : $"row {actual ?? "(none)"}, expected {expected}";
            }
        );

    /// <summary>
    /// #37 の期待：EF Core は明細を持つ注文（10・12）と、その明細をすべて消し、件数は消した行の数（根＋子孫）。
    /// QuickER 版とインメモリはナビゲーションの条件を NotSupportedException で断り、何も変えない
    /// </summary>
    public static ExpectOp NavigationPredicateDelete(string label) =>
        new(
            label,
            e =>
            {
                var keys = e.Previous.State.Select(row => row.Split(' ')[0]).ToList();

                if (e.Kind == ParityBackendKind.EfCore)
                {
                    string[] remaining = ["customers/1", "customers/2", "orders/11"];

                    return e.Previous.Outcome == "rows=5" && keys.SequenceEqual(remaining)
                        ? null
                        : $"outcome {e.Previous.Outcome}, rows [{string.Join(", ", keys)}]";
                }

                string[] unchanged =
                [
                    "customers/1",
                    "customers/2",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "order_lines/100",
                    "order_lines/101",
                    "order_lines/102",
                ];

                return
                    e.Previous.Outcome.StartsWith(
                        "throws NotSupportedException",
                        StringComparison.Ordinal
                    ) && keys.SequenceEqual(unchanged)
                    ? null
                    : $"outcome {e.Previous.Outcome}, rows [{string.Join(", ", keys)}]";
            }
        );
}
