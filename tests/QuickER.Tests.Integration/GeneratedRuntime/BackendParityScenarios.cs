using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 一致テストの決まった形のシナリオ。どれも QuickER 版（SQLite 実ファイル）を基準に、ほかの実装先が同じ結果になることを
/// 表明する。今わかっている違いは <see cref="BackendParityKnownDifferences"/> が名指しする。
/// </summary>
public static partial class BackendParityScenarios
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
                ParityExpect.RootState("crud-basic-loaded-unchanged", S, RowState.Unchanged),
                ParityExpect.SlotRow(
                    "crud-basic-loaded-values",
                    S,
                    "orders/13",
                    "customer=3",
                    "amount=12.5",
                    "memo='n'"
                ),
                new EditMemoOp(S, 13, null),
                new UpdateOrderOp(S),
                ParityExpect.Outcome("crud-basic-update", "updated=True"),
                ParityExpect.StateRow("crud-basic-update-to-null", "orders/13", "memo=null"),
                new LoadOp("B", "orders", 99),
                ParityExpect.Outcome("crud-basic-missing-key", "null"),
                new DeleteOp("order_lines", 103),
                ParityExpect.Outcome("crud-basic-delete", "deleted=True"),
                new DeleteOp("order_lines", 103),
                ParityExpect.Outcome("crud-basic-delete-again", "deleted=False"),
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
        // 一意な値を持つ行を消し、同じ値の新しい行を足して 1 回で保存する（複数の根。新しい行が先・消す行が先の両方）。
        // 削除が挿入より先に走らないと、一意制約に阻まれる（区切り 39＝新しい行だけの部分木を削除と更新の後へ回す）
        new(
            "graph-delete-then-add-same-unique-value-roots-added-first",
            [.. Basic, new SeedOp(new CustomerRow(4, "Dan"))],
            [
                new NewRootOp(S, new CustomerRow(5, "Dan"), RowState.Added),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp([S, "B"]),
                new LoadOp("C", "customers", 5),
            ]
        ),
        new(
            "graph-delete-then-add-same-unique-value-roots-removed-first",
            [.. Basic, new SeedOp(new CustomerRow(4, "Dan"))],
            [
                new NewRootOp(S, new CustomerRow(5, "Dan"), RowState.Added),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp(["B", S]),
                new LoadOp("C", "customers", 5),
            ]
        ),
        // 同じことを子のコレクションの中で行う（EditModel の Remove() は消した行を後ろへ並べるので、新しい行が先に並ぶ形）
        new(
            "graph-delete-then-add-same-unique-value-children",
            [.. Basic, new SeedOp(new CustomerRow(5, "Eve", Referrer: 1))],
            [
                new LoadOp(S, "customers", 1),
                new AddChildCustomerOp(S, new CustomerRow(6, "Eve", Referrer: 1)),
                new AttachChildCustomerOp(S, 5),
                new MarkRemovedOp(S, "customers/5"),
                new SaveOp(S),
                ParityExpect.Succeeds("unique-value-freed-by-delete-succeeds"),
                new LoadOp("C", "customers", 6),
                ParityExpect.StateKeys(
                    "unique-value-freed-by-delete-rows",
                    "customers/1",
                    "customers/2",
                    "customers/6",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "order_lines/100",
                    "order_lines/101",
                    "order_lines/102"
                ),
                ParityExpect.StateRow("unique-value-taken-by-new-row", "customers/6", "name=Eve"),
            ]
        ),
        // DB にある行を消し、同じキーの新しい行を足して 1 回で保存すると、削除が先に走り、行が置き換わる（ルートの並びに依らない）
        new(
            "graph-remove-and-add-same-key-roots-added-first",
            [.. Basic, new SeedOp(new CustomerRow(4, "Dan"))],
            [
                new NewRootOp(S, new CustomerRow(4, "Dana"), RowState.Added),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp([S, "B"]),
                new LoadOp("C", "customers", 4),
            ]
        ),
        new(
            "graph-remove-and-add-same-key-roots-removed-first",
            [.. Basic, new SeedOp(new CustomerRow(4, "Dan"))],
            [
                new NewRootOp(S, new CustomerRow(4, "Dana"), RowState.Added),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp(["B", S]),
                new LoadOp("C", "customers", 4),
            ]
        ),
        // 削除を含む保存でも、削除する行と主キー・一意な値の重ならない新しい行は、並びどおりに保存する
        new(
            "graph-save-new-row-without-shared-value-keeps-order",
            [.. Basic, new SeedOp(new CustomerRow(4, "Dan"))],
            [
                new NewRootOp(S, new CustomerRow(5, "Eve"), RowState.Added),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp([S, "B"]),
                ParityExpect.Hooks(
                    "graph-order-new-row-without-shared-value",
                    "Before",
                    "Insert:customers/5",
                    "Delete:customers/4"
                ),
            ]
        ),
        // 区切り 43（判断 D）: 削除を含む保存は、削除しない書き込み → 削除 → 後回しの行の 3 段で保存する。子を別の親へ
        // 付け替えて元の親を消す保存は、根の並びに依らず、付け替え（更新）が削除より先に走る
        MoveChildAndDeleteOldParent(
            "graph-move-child-and-delete-old-parent-removed-first",
            ["A", "B"]
        ),
        MoveChildAndDeleteOldParent(
            "graph-move-child-and-delete-old-parent-kept-first",
            ["B", "A"]
        ),
        // 同じ親のコレクションの中で、先に並ぶ兄弟を消し、その子を後ろの兄弟の下へ移す
        new(
            "graph-move-grandchild-between-siblings-and-delete-old-sibling",
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Root")),
                new SeedOp(new CustomerRow(5, "Old", Referrer: 4)),
                new SeedOp(new CustomerRow(6, "New", Referrer: 4)),
                new SeedOp(new CustomerRow(7, "Leaf", Referrer: 5)),
            ],
            [
                new NewRootOp(S, new CustomerRow(4, "Root"), RowState.Unchanged),
                new AddChildOp(
                    S,
                    "customers/4",
                    new CustomerRow(5, "Old", Referrer: 4),
                    RowState.Removed
                ),
                new AddChildOp(
                    S,
                    "customers/4",
                    new CustomerRow(6, "New", Referrer: 4),
                    RowState.Unchanged
                ),
                new AddChildOp(
                    S,
                    "customers/6",
                    new CustomerRow(7, "Leaf", Referrer: 6),
                    RowState.Updated
                ),
                new SaveOp(S),
                ParityExpect.Succeeds("D-sibling-move-succeeds"),
                ParityExpect.Hooks(
                    "D-sibling-move-update-before-delete",
                    "Before",
                    "Update:customers/7",
                    "Delete:customers/5"
                ),
                new LoadOp("C", "customers", 7),
            ]
        ),
        // 明細を別の注文へ付け替え、元の注文を消す（注文→明細は CASCADE。削除が先に走ると付け替えた明細まで消え、続く
        // 更新が「行なし」になる）
        MoveLineAndDeleteOldOrder("graph-move-line-and-delete-old-order-removed-first", ["A", "B"]),
        MoveLineAndDeleteOldOrder("graph-move-line-and-delete-old-order-kept-first", ["B", "A"]),
        // 削除する行の一意な値（主キーでない）を取る更新する行は、削除の後へ回す（J3）
        UpdatedTakesDeletedUniqueValue(
            "graph-updated-row-takes-deleted-unique-value-updated-first",
            ["A", "B"]
        ),
        UpdatedTakesDeletedUniqueValue(
            "graph-updated-row-takes-deleted-unique-value-removed-first",
            ["B", "A"]
        ),
        // 区切り 43（外1）: Count・Any は Take・Skip を、取得と同じ意味で当てる（取得の件数とそろう）。Take・Skip つきの
        // ExecuteDelete は NotSupportedException で断り、何も消さない
        new(
            "paged-count-and-any-follow-fetch",
            Basic,
            [
                new PagedOrdersOp(ParityPagedTerminal.Fetch, Take: 1),
                ParityExpect.Outcome("paged-fetch-take-1", "fetched=1"),
                new PagedOrdersOp(ParityPagedTerminal.Count, Take: 1),
                ParityExpect.Outcome("paged-count-take-1", "count=1"),
                new PagedOrdersOp(ParityPagedTerminal.Count, Skip: 1),
                ParityExpect.Outcome("paged-count-skip-1", "count=2"),
                new PagedOrdersOp(ParityPagedTerminal.Count, Skip: 1, Take: 1),
                ParityExpect.Outcome("paged-count-skip-1-take-1", "count=1"),
                new PagedOrdersOp(ParityPagedTerminal.Count, Skip: 3),
                ParityExpect.Outcome("paged-count-skip-all", "count=0"),
                new PagedOrdersOp(ParityPagedTerminal.Count, Skip: 1, MinAmount: 50m),
                ParityExpect.Outcome("paged-count-condition-skip-1", "count=1"),
                new PagedOrdersOp(ParityPagedTerminal.Any, Skip: 2),
                ParityExpect.Outcome("paged-any-skip-2", "any=True"),
                new PagedOrdersOp(ParityPagedTerminal.Any, Skip: 3),
                ParityExpect.Outcome("paged-any-skip-all", "any=False"),
                new PagedOrdersOp(ParityPagedTerminal.Any, Skip: 2, Take: 1, MinAmount: 50m),
                ParityExpect.Outcome("paged-any-condition-skip-past", "any=False"),
            ]
        ),
        new(
            "paged-execute-delete-is-refused",
            Basic,
            [
                new PagedOrdersOp(ParityPagedTerminal.Delete, Take: 1),
                ParityExpect.Throws("paged-delete-take-refused", "NotSupportedException"),
                new PagedOrdersOp(ParityPagedTerminal.Delete, Skip: 2),
                ParityExpect.Throws("paged-delete-skip-refused", "NotSupportedException"),
                new PagedOrdersOp(ParityPagedTerminal.CascadeDelete, Skip: 1, Take: 1),
                ParityExpect.Throws("paged-cascade-delete-refused", "NotSupportedException"),
                ParityExpect.StateRows("paged-delete-changes-nothing", 8),
            ]
        ),
        // 区切り 43（#7・判断 B）: null のキーの GetById・Delete は、どの実装先も ArgumentNullException（ParamName = "id"）
        new(
            "null-key-is-refused",
            Basic,
            [
                new NullKeyOp("customers", Delete: false),
                ParityExpect.Outcome(
                    "null-key-get-customer",
                    "throws ArgumentNullException param=id"
                ),
                new NullKeyOp("customers", Delete: true),
                ParityExpect.Outcome(
                    "null-key-delete-customer",
                    "throws ArgumentNullException param=id"
                ),
                new NullKeyOp("orders", Delete: false),
                ParityExpect.Outcome("null-key-get-order", "throws ArgumentNullException param=id"),
                new NullKeyOp("orders", Delete: true),
                ParityExpect.Outcome(
                    "null-key-delete-order",
                    "throws ArgumentNullException param=id"
                ),
                new NullKeyBlobOp(Write: false, ViaFile: false),
                ParityExpect.Outcome("null-key-read-blob", "throws ArgumentNullException param=id"),
                new NullKeyBlobOp(Write: true, ViaFile: false),
                ParityExpect.Outcome(
                    "null-key-write-blob",
                    "throws ArgumentNullException param=id"
                ),
                new NullKeyBlobOp(Write: false, ViaFile: true),
                ParityExpect.Outcome(
                    "null-key-read-blob-to-file",
                    "throws ArgumentNullException param=id"
                ),
                new NullKeyBlobOp(Write: true, ViaFile: true),
                ParityExpect.Outcome(
                    "null-key-write-blob-from-file",
                    "throws ArgumentNullException param=id"
                ),
                ParityExpect.StateRows("null-key-changes-nothing", 8),
            ]
        ),
        // 区切り 43（#6）: 取り消し済みのトークンの操作は、どの実装先も OperationCanceledException で終わり、何も書かない
        // （読み取りも取り消しで終わる）
        new(
            "canceled-token-is-honored",
            Basic,
            [
                new CanceledOp(new InsertOp(new CustomerRow(3, "Carol"))),
                ParityExpect.Outcome("canceled-insert", "throws canceled"),
                new CanceledOp(new LoadOp(S, "orders", 10)),
                ParityExpect.Outcome("canceled-get-by-id", "throws canceled"),
                new LoadOp(S, "orders", 10),
                new EditMemoOp(S, 10, "edited"),
                new CanceledOp(new UpdateOrderOp(S)),
                ParityExpect.Outcome("canceled-update", "throws canceled"),
                new CanceledOp(new SaveOp(S)),
                ParityExpect.Outcome("canceled-save", "throws canceled"),
                new CanceledOp(new SaveManyOrdersOp([S])),
                ParityExpect.Outcome("canceled-save-many", "throws canceled"),
                new CanceledOp(new DeleteOp("order_lines", 100)),
                ParityExpect.Outcome("canceled-delete", "throws canceled"),
                new CanceledOp(
                    new ExecuteDeleteOrdersOp(
                        ParityOrderPredicate.AmountAtLeast100,
                        CascadeDelete: true
                    )
                ),
                ParityExpect.Outcome("canceled-execute-delete", "throws canceled"),
                new CanceledOp(new PagedOrdersOp(ParityPagedTerminal.Fetch)),
                ParityExpect.Outcome("canceled-fetch", "throws canceled"),
                new CanceledOp(new PagedOrdersOp(ParityPagedTerminal.Count)),
                ParityExpect.Outcome("canceled-count", "throws canceled"),
                new CanceledOp(new PagedOrdersOp(ParityPagedTerminal.Any)),
                ParityExpect.Outcome("canceled-any", "throws canceled"),
                ParityExpect.StateRows("canceled-changes-nothing", 8),
            ]
        ),
        // 区切り 43（#1）: 主キー 0 の行も、アプリが決めたキーのまま挿入・グラフ保存する。同じ 0 の 2 行目は主キーの重複。
        // このフィクスチャのキーは値オブジェクト（EF Core は変換のある列の値を作らない）。素の int のキーは
        // BinaryColumnRuntimeTestsBase と SqlServerIdentityColumnRuntimeTests が見る
        new(
            "insert-and-save-key-zero",
            Basic,
            [
                new InsertOp(new CustomerRow(0, "Zed")),
                ParityExpect.Succeeds("key-zero-insert-succeeds"),
                new NewRootOp(S, new OrderRow(0, 0, 1m), RowState.Added),
                new SaveOp(S),
                ParityExpect.Succeeds("key-zero-save-succeeds"),
                new LoadOp("C", "orders", 0),
                ParityExpect.Outcome("key-zero-row-is-stored", "loaded"),
                new InsertOp(new CustomerRow(0, "Again")),
                ParityExpect.Throws("key-zero-duplicate-is-refused", "store-rejected"),
            ]
        ),
        // 何も削除しない保存は、新しい行も並びどおりに保存する（後回しにするのは削除を含む保存だけ）
        new(
            "graph-save-without-deletion-keeps-order",
            Basic,
            [
                new NewRootOp(S, new OrderRow(13, 1, 5m), RowState.Added),
                new LoadOp("B", "orders", 10),
                new EditMemoOp("B", 10, "edited"),
                new SaveManyOrdersOp([S, "B"]),
                ParityExpect.Hooks(
                    "graph-order-without-deletion",
                    "Before",
                    "Insert:orders/13",
                    "Update:orders/10"
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
                // 更新の対象が無く挿入へ切り替わると、Before は Update で 1 回・After は実際の操作の Insert で走り、行ができる
                ParityExpect.Succeeds("insert-missing-succeeds"),
                ParityExpect.RawHooks(
                    "insert-missing-before-update-after-insert",
                    ["Before:Update:orders/20", "After:Insert:orders/20"]
                ),
                ParityExpect.StateRow("insert-missing-row", "orders/20", "memo='re'"),
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
            [
                new ExecuteDeleteOrdersOp(
                    ParityOrderPredicate.AmountAtLeast100,
                    CascadeDelete: true
                ),
                ParityExpect.Outcome("cascade-delete-counts-root-and-children", "rows=3"),
                ParityExpect.StateKeys(
                    "cascade-delete-rows-left",
                    "customers/1",
                    "customers/2",
                    "orders/11",
                    "orders/12",
                    "order_lines/102"
                ),
            ]
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
            [
                new ExecuteDeleteCustomersOp(CascadeDelete: true),
                ParityExpect.Throws("cyclic-cascade-is-refused", "NotSupportedException"),
                ParityExpect.StateRows("cyclic-cascade-changes-nothing", 8),
            ]
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
        // 制約違反：主キーの重複・一意な名前の重複・グラフ保存の途中の主キーの重複・親の無い明細・無い顧客を指す注文（外部キー）。
        // どの実装先も断り、何も書かない（例外の型の違いは比べ方がまとめる）。外部キーの 2 手はインメモリの受け入れた差（P2）
        // なので末尾に置き、インメモリはその手から後を比べない（重複の手はインメモリでも比べる）
        new(
            "constraint-violations",
            Basic,
            [
                new InsertOp(new CustomerRow(1, "Dup")),
                ParityExpect.Throws("insert-duplicate-key-refused", "store-rejected"),
                ParityExpect.StateRow(
                    "insert-duplicate-key-keeps-row",
                    "customers/1",
                    "name=Alice"
                ),
                new InsertOp(new CustomerRow(5, "Alice")),
                ParityExpect.Throws("insert-duplicate-unique-refused", "store-rejected"),
                ParityExpect.StateRows("insert-duplicate-unique-writes-nothing", 8),
                new NewRootOp("x", new OrderRow(60, 1, 1m), RowState.Added),
                new AddLineOp("x", new LineRow(160, 60, "a", 1)),
                new AddLineOp("x", new LineRow(100, 60, "dupkey", 1)),
                new SaveOp("x"),
                new DeviatingOp(
                    new InsertOp(new LineRow(150, 99, "orphan", 1)),
                    ParityDeviation.OrphanInsert
                ),
                new DeviatingOp(
                    new InsertOp(new OrderRow(50, 9, 1m)),
                    ParityDeviation.OrphanInsert
                ),
            ]
        ),
        // 親を DeleteAsync(id) で消す（DB の ON DELETE CASCADE が子を消す。インメモリの受け入れた差＝P1）
        new(
            "delete-parent-by-id",
            Basic,
            [
                new DeviatingOp(
                    new DeleteOp("orders", 10),
                    ParityDeviation.ParentDeleteWithChildren
                ),
                new DeviatingOp(
                    new DeleteOp("customers", 2),
                    ParityDeviation.ParentDeleteWithChildren
                ),
            ]
        ),
        // 紹介者として参照されている顧客を DeleteAsync(id) で消す（紹介者の外部キーは NO ACTION＝DB は断り、何も消さない。
        // インメモリの受け入れた差＝P1 なので、インメモリはこの手から後を比べず、EF Core・リモートを比べる）
        new(
            "delete-referrer-by-id",
            [.. Basic, new SeedOp(new CustomerRow(6, "Fay", Referrer: 1))],
            [
                new DeviatingOp(
                    new DeleteOp("customers", 1),
                    ParityDeviation.ParentDeleteWithChildren
                ),
                new LoadOp("A", "orders", 10),
            ]
        ),
        // 連絡先（主キーと列名の違う値オブジェクトの外部キー 5 本）の挿入・読み込み・付け替え・保存・削除
        new(
            "contacts-crud",
            [.. Basic, new SeedOp(new CustomerRow(3, "Cy"))],
            [
                new InsertOp(
                    new ContactRow(
                        500,
                        "c",
                        1,
                        Watcher: 2,
                        Backup: 3,
                        Fallback: 2,
                        CustomerCode: "K3"
                    )
                ),
                new InsertOp(new ContactRow(501, "d", 2)),
                new LoadOp(S, "contacts", 500),
                new EditContactOp(
                    S,
                    500,
                    "c2",
                    new ParityContactTarget(ParityContactRelation.Watcher, 3)
                ),
                new SaveOp(S),
                new EditContactOp(
                    S,
                    500,
                    Target: new ParityContactTarget(ParityContactRelation.Backup, null)
                ),
                new EditContactOp(
                    S,
                    500,
                    Target: new ParityContactTarget(ParityContactRelation.CustomerCode, null, "K1")
                ),
                new SaveOp(S),
                new LoadOp("B", "customers", 2),
                new AttachContactOp("B", "customers/2", 501, ParityContactRelation.Owner),
                // 持っている親（顧客 2）と食い違わない付け替え（watcher を 3 へ）。食い違う形は n2-navigation-overrides-foreign-key
                new EditContactOp(
                    "B",
                    501,
                    "d2",
                    new ParityContactTarget(ParityContactRelation.Watcher, 3)
                ),
                new SaveOp("B"),
                new DeleteOp("contacts", 500),
                new LoadOp("C", "contacts", 501),
            ]
        ),
        // P2-5-N2: 顧客 2 の owner のコレクションに持った連絡先の owner を 3 へ書き換え、顧客 2 から保存する（持っている親と
        // 外部キーの値が食い違うグラフ＝全実装先が保存の前に同じ例外で断る）
        new(
            "n2-navigation-overrides-foreign-key",
            [
                .. Basic,
                new SeedOp(new CustomerRow(3, "Cy")),
                new SeedOp(new ContactRow(501, "d", 2)),
            ],
            [
                new LoadOp("B", "customers", 2),
                new AttachContactOp("B", "customers/2", 501, ParityContactRelation.Owner),
                new EditContactOp(
                    "B",
                    501,
                    "d2",
                    new ParityContactTarget(ParityContactRelation.Owner, 3)
                ),
                new SaveOp("B"),
                new LoadOp("C", "contacts", 501),
            ]
        ),
        // 顧客を連絡先と紹介先ごと Include して読み、グラフの連絡先を編集して顧客から保存する（リモートには無い）。連絡先 501 の
        // watcher を、持っている顧客 1 と違う顧客 2 へ書き換えるので、全実装先が保存の前に同じ例外で断る
        new(
            "contacts-include-and-save",
            [
                .. Basic,
                new SeedOp(new CustomerRow(3, "Cy", Referrer: 1)),
                new SeedOp(
                    new ContactRow(
                        500,
                        "c",
                        1,
                        Watcher: 1,
                        Backup: 1,
                        Fallback: 1,
                        CustomerCode: "K1"
                    )
                ),
                new SeedOp(new ContactRow(501, "d", 2, Watcher: 1)),
            ],
            [
                new LoadCustomerWithContactsOp(S, 1),
                new EditContactOp(S, 500, "c2"),
                new EditContactOp(
                    S,
                    501,
                    Target: new ParityContactTarget(ParityContactRelation.Watcher, 2)
                ),
                new EditCustomerOp(S, 3, Note: "r"),
                new SaveOp(S),
                new LoadCustomerWithContactsOp("B", 1),
            ]
        ),
        // P2-5-N2: 変更の無い子でも、持っている親と外部キーが食い違えば断る（EF Core は追跡の fix-up で外部キーを書き換える）
        new(
            "n2-unchanged-child-foreign-key-differs",
            [
                .. Basic,
                new SeedOp(new CustomerRow(3, "Cy")),
                new SeedOp(new ContactRow(501, "d", 2)),
            ],
            [
                new LoadOp("B", "customers", 2),
                new EditCustomerOp("B", 2, Note: "n"),
                new AddChildOp(
                    "B",
                    "customers/2",
                    new ContactRow(501, "d", 3),
                    RowState.Unchanged,
                    ParityContactRelation.Owner
                ),
                new SaveOp("B"),
                new LoadOp("C", "contacts", 501),
            ]
        ),
        // P2-5-N2: 子の親への参照が同じ保存の行を指し、外部キーと食い違えば断る
        new(
            "n2-parent-reference-differs",
            [.. Basic, new SeedOp(new ContactRow(500, "c", 1, Watcher: 2))],
            [
                new LoadOp("S", "customers", 1),
                new EditCustomerOp("S", 1, Note: "n"),
                new AttachContactOp("S", "customers/1", 500, ParityContactRelation.Owner),
                new PointContactAtOp("S", 500, ParityContactRelation.Watcher, 1),
                new SaveOp("S"),
                new LoadOp("C", "contacts", 500),
            ]
        ),
        // P2-5-N2: 新しい子の未設定の外部キーは親のキーで埋まる（新しい顧客・注文・明細の 3 段。Save フックなし＝まとめて埋める）
        new(
            "n2-new-children-take-parent-keys",
            [.. Basic],
            [
                new NewRootOp("S", new CustomerRow(7, "Seven"), RowState.Added),
                new AddChildOp("S", "customers/7", new OrderRow(70, 0, 5m), RowState.Added),
                new AddChildOp("S", "orders/70", new LineRow(71, 0, "y", 2), RowState.Added),
                new SaveOp("S"),
                // フックを登録しなくてもグラフ保存は動き、行を保存して Unchanged にする
                ParityExpect.Outcome("n2-no-hooks-save-rows", "rows=3"),
                ParityExpect.RootState("n2-no-hooks-root-unchanged", "S", RowState.Unchanged),
                ParityExpect.StateRow("n2-no-hooks-row-stored", "customers/7", "name=Seven"),
                new LoadOp("C", "orders", 70),
                new LoadOp("D", "order_lines", 71),
            ],
            new ParityBackendOptions(WithHooks: false)
        ),
        // P2-5-N2: 親の Before フックが親のキーを決める形。新しい子の未設定の外部キーは、子の Before の直前に決まったキーで埋まり、
        // 子の Before が埋まった外部キーを記録する（hooks-before。EF Core も追跡の時点の値でなく決まったキーを見る）
        new(
            "n2-hook-assigned-keys-reach-new-children",
            [.. Basic],
            [
                new NewRootOp("S", new CustomerRow(0, "Zero", Code: "KZ"), RowState.Added),
                new AddChildOp("S", "customers/0", new OrderRow(0, 0, 5m), RowState.Added),
                new AddChildOp("S", "orders/0", new LineRow(31, 0, "z", 1), RowState.Added),
                new SaveOp("S"),
                // リモートはサーバーのフックが決めたキーを、保存の応答の値で呼び出し側のグラフへ書き戻す（根は Unchanged に確定する）
                ParityExpect.RootState("n2-assigned-key-root-settles", "S", RowState.Unchanged),
                ParityExpect.SlotKeys(
                    "n2-assigned-keys-reach-caller",
                    "S",
                    "customers/900",
                    "orders/9000",
                    "order_lines/31"
                ),
                new LoadOp("C", "customers", ParityHookRecorder.AssignedCustomerKey),
                new LoadOp("D", "orders", ParityHookRecorder.AssignedOrderKey),
                new LoadOp("E", "order_lines", 31),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Insert:customers", ParityHookBehavior.AssignKey),
                    new ParityHookRule("Insert:orders", ParityHookBehavior.AssignKey),
                    new ParityHookRule("Insert:order_lines", ParityHookBehavior.AssignKey),
                ]
            )
        ),
        // P2-5-N3: 同じ連絡先の 2 つのインスタンスのうち、変更を持つのが 1 つなら保存する（EF Core は変更の無いほうを追跡しない）
        new(
            "n3-same-row-one-instance-changed",
            [.. Basic, new SeedOp(new ContactRow(500, "c", 1, Watcher: 1))],
            [
                new LoadOp("S", "customers", 1),
                new AttachContactOp("S", "customers/1", 500, ParityContactRelation.Owner),
                new AddChildOp(
                    "S",
                    "customers/1",
                    new ContactRow(500, "c2", 1, Watcher: 1),
                    RowState.Updated,
                    ParityContactRelation.Watcher
                ),
                new SaveOp("S"),
                new LoadOp("C", "contacts", 500),
            ]
        ),
        // P2-5-N3: 同じ連絡先の 2 つのインスタンスがどちらも変更を持てば、行を 2 度書くことになるので断る
        new(
            "n3-same-row-both-instances-changed",
            [.. Basic, new SeedOp(new ContactRow(500, "c", 1, Watcher: 1))],
            [
                new LoadOp("S", "customers", 1),
                new AddChildOp(
                    "S",
                    "customers/1",
                    new ContactRow(500, "c2", 1, Watcher: 1),
                    RowState.Updated,
                    ParityContactRelation.Owner
                ),
                new AddChildOp(
                    "S",
                    "customers/1",
                    new ContactRow(500, "c3", 1, Watcher: 1),
                    RowState.Updated,
                    ParityContactRelation.Watcher
                ),
                new SaveOp("S"),
                new LoadOp("C", "contacts", 500),
            ]
        ),
        // N3（同じインスタンス）: 変更を持つ同じインスタンスに 2 つの道（顧客の owner と watcher のコレクション）で届く保存は、
        // 新しい行・更新・削除のどれでも、書く前・フックの前に断る（断らないと、行を道ごとに書く実装先と 1 回だけ書く実装先とで
        // 結果が割れる）
        SameInstanceTwoCollections("n3-same-instance-two-collections-new", RowState.Added),
        SameInstanceTwoCollections("n3-same-instance-two-collections-update", RowState.Updated),
        SameInstanceTwoCollections("n3-same-instance-two-collections-delete", RowState.Removed),
        // 同じく、変更を持つ同じ根を複数形の保存の一覧に 2 回渡す保存も断る
        new(
            "n3-same-instance-twice-in-roots",
            Basic,
            [
                new NewRootOp("A", new CustomerRow(1, "Alice2", "vip"), RowState.Updated),
                new SaveManyCustomersOp(["A", "A"]),
                ParityExpect.Throws("twice-in-roots-refused", "InvalidOperationException"),
                ParityExpect.RawHooks("twice-in-roots-fires-no-hook", []),
                ParityExpect.StateRow("twice-in-roots-writes-nothing", "customers/1", "name=Alice"),
            ]
        ),
        // H5: 任意の自己参照（NO ACTION）で参照されている顧客を、参照する顧客と一緒に 1 回の保存で消す（再現パッチの形）
        ReferencedReferrerDelete("h5-referrer-delete-dependent-modified", modifyDependent: true),
        ReferencedReferrerDelete("h5-referrer-delete-dependent-unmodified", modifyDependent: false),
        // H5 と、参照アクションごとの同じ形：連絡先が参照している顧客を、その連絡先を持つ別の顧客と一緒に 1 回の保存で消す
        ReferencedContactDelete(
            "h5-contact-watcher-delete-dependent-modified",
            ParityContactRelation.Watcher,
            true
        ),
        ReferencedContactDelete(
            "h5-contact-watcher-delete-dependent-unmodified",
            ParityContactRelation.Watcher,
            false
        ),
        ReferencedContactDelete(
            "contact-owner-delete-dependent-modified",
            ParityContactRelation.Owner,
            true
        ),
        ReferencedContactDelete(
            "contact-owner-delete-dependent-unmodified",
            ParityContactRelation.Owner,
            false
        ),
        ReferencedContactDelete(
            "contact-backup-delete-dependent-modified",
            ParityContactRelation.Backup,
            true
        ),
        ReferencedContactDelete(
            "contact-backup-delete-dependent-unmodified",
            ParityContactRelation.Backup,
            false
        ),
        ReferencedContactDelete(
            "contact-fallback-delete-dependent-modified",
            ParityContactRelation.Fallback,
            true
        ),
        ReferencedContactDelete(
            "contact-fallback-delete-dependent-unmodified",
            ParityContactRelation.Fallback,
            false
        ),
        ReferencedContactDelete(
            "h5-contact-code-delete-dependent-modified",
            ParityContactRelation.CustomerCode,
            true
        ),
        ReferencedContactDelete(
            "h5-contact-code-delete-dependent-unmodified",
            ParityContactRelation.CustomerCode,
            false
        ),
        // 実行戦略のやり直し：連絡先 500 を顧客 2 のグラフに持ったまま、その連絡先の backup（SET NULL）が指す顧客 3 を消す保存の、
        // EF Core の 1 回目のコミットが一時的に失敗する。2 回目の試行は、1 回目の追跡で EF Core が書き換えたナビゲーション
        // （3 の連絡先のコレクションに入った 500）を戻してからグラフを辿る（戻さないと 500 を 3 の子として消す）
        new(
            "contact-backup-delete-retried-commit",
            [
                .. Basic,
                new SeedOp(new CustomerRow(3, "Cy")),
                new SeedOp(new ContactRow(500, "c", 2, Backup: 3)),
            ],
            [
                new LoadOp("A", "customers", 2),
                new AttachContactOp("A", "customers/2", 500, ParityContactRelation.Owner),
                // 文を 2 つにする（EF Core は文が 1 つの SaveChanges ではトランザクションを開かず、失敗させるコミットが無い）
                new EditContactOp("A", 500, "edited"),
                new LoadOp("B", "customers", 3),
                new MarkRemovedOp("B", "customers/3"),
                // 顧客 3 は連絡先 500 に参照されている（インメモリの受け入れた差＝P1）
                new DeviatingOp(
                    new SaveManyCustomersOp(["A", "B"]),
                    ParityDeviation.DependentHeldParentDelete
                ),
                new LoadOp("C", "contacts", 500),
            ],
            new ParityBackendOptions(WithHooks: false, RetryEfCoreFirstCommit: true)
        ),
        // H1: 一意制約を参照する外部キーの親の列（代替キー）を更新する（参照あり・なし × UpdateAsync・グラフ保存）
        AlternateKeyUpdate("h1-alternate-key-referenced-update", referenced: true, viaSave: false),
        AlternateKeyUpdate("h1-alternate-key-referenced-save", referenced: true, viaSave: true),
        AlternateKeyUpdate(
            "h1-alternate-key-unreferenced-update",
            referenced: false,
            viaSave: false
        ),
        AlternateKeyUpdate("h1-alternate-key-unreferenced-save", referenced: false, viaSave: true),
        // H1 × 同じ保存のほかの行：代替キーを変える顧客 3 と、同じグラフ保存の紹介先の顧客 4（読んだ後に他者が消した・
        // insertWhenUpdateMissing あり／なし、Before のフックが更新を飛ばす）。版の無い代替キーなので、代替キーを書いた後の
        // 試行のやり直しで手元の版を持ち越す経路は無い（版のある形は SQL Server の系統の alternate-key-*-with-*）
        AlternateKeyUpdateWithChild(
            "h1-alternate-key-save-with-gone-child-insert-when-missing",
            AlternateKeyChild.GoneInsertWhenMissing
        ),
        AlternateKeyUpdateWithChild(
            "h1-alternate-key-save-with-gone-child",
            AlternateKeyChild.Gone
        ),
        AlternateKeyUpdateWithChild(
            "h1-alternate-key-save-with-skipped-child",
            AlternateKeyChild.HookSkipped
        ),
        // H1: 古いコードを参照する依存行を同じグラフ保存に持ったまま、代替キーを変える（保存の前に変える・Before のフックが
        // 追跡の後で変える）。古いコードは参照されているので全実装先が断り、メモリの依存行の外部キーとナビゲーションは元のまま
        AlternateKeyUpdateWithDependents(
            "h1-alternate-key-referenced-save-with-dependents",
            byHook: false
        ),
        AlternateKeyUpdateWithDependents("h1-hook-rewrites-referenced-alternate-key", byHook: true),
        // H1: Before のフックが追跡の後で代替キーを書き換える（参照なし）。書き換えた値が全実装先で保存先に入る
        new(
            "h1-hook-rewrites-alternate-key",
            [.. Basic, new SeedOp(new CustomerRow(3, "Cy"))],
            [
                new LoadOp("A", "customers", 3),
                new EditCustomerOp("A", 3, Note: "n"),
                new SaveOp("A"),
                new LoadOp("C", "customers", 3),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule(
                        "Update:customers/3",
                        ParityHookBehavior.EditKeyThenContinue
                    ),
                ]
            )
        ),
        // H3:Before が値を書き換えてから false を返す（更新・削除）
        HookOnCustomer("h3-edit-then-skip-update", ParityHookBehavior.EditThenSkip, delete: false),
        HookOnCustomer("h3-edit-then-skip-delete", ParityHookBehavior.EditThenSkip, delete: true),
        // H3 の挿入の形：新しい顧客 9 をグラフ保存で挿入し、Before が書き換えてから false を返す（乱数の列の末尾の手で見つけた形）
        new(
            "h3-edit-then-skip-insert",
            [.. Basic, new SeedOp(new CustomerRow(4, "Dan"))],
            [
                new NewRootOp("A", new CustomerRow(9, "Ivy"), RowState.Added),
                new LoadOp("B", "customers", 4),
                new EditCustomerOp("B", 4, Note: "m"),
                new SaveManyCustomersOp(["A", "B"]),
                new LoadOp("C", "customers", 9),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Insert:customers/9", ParityHookBehavior.EditThenSkip),
                ]
            )
        ),
        // フックのほかの振る舞い（書き換えて続ける・Before で投げる・After で投げる）
        HookOnCustomer(
            "hook-edit-then-continue-update",
            ParityHookBehavior.EditThenContinue,
            delete: false
        ),
        HookOnCustomer(
            "hook-edit-then-continue-delete",
            ParityHookBehavior.EditThenContinue,
            delete: true
        ),
        HookOnCustomer("hook-throw-before-update", ParityHookBehavior.ThrowBefore, delete: false),
        HookOnCustomer("hook-throw-before-delete", ParityHookBehavior.ThrowBefore, delete: true),
        // After が投げると、保存の段の更新は全実装先で残らない（リモートはサーバーの 500）
        HookOnCustomer(
            "hook-throw-after-update",
            ParityHookBehavior.ThrowAfter,
            delete: false,
            afterSave:
            [
                ParityExpect.OutcomeByKind(
                    "throw-after-update-fails",
                    ParityStep.HookFailure,
                    remote: ParityStep.StoreRejected,
                    remoteEfCoreOnly: ParityStep.StoreRejected
                ),
                ParityExpect.StateRow("throw-after-update-rolled-back", "customers/3", "note=null"),
                ParityExpect.StateRow(
                    "throw-after-update-other-row-rolled-back",
                    "customers/4",
                    "note=null"
                ),
            ]
        ),
        HookOnCustomer("hook-throw-after-delete", ParityHookBehavior.ThrowAfter, delete: true),
        // P3-3a: 生成コードのランタイムテストから移した行（系統ごとのファイル）
        .. GraphSaveOrderScenarios(),
        .. EfCoreRuntimeScenarios(),
        .. CrudRuntimeScenarios(),
        .. GraphSaveRuntimeScenarios(),
        .. SaveHookRuntimeScenarios(),
        .. BlobRuntimeScenarios(),
        .. NullIntoNotNullScenarios(),
        .. RemoteRuntimeScenarios(),
        .. KeyShapeScenarios(),
    ];

    /// <summary>
    /// 顧客 40 の owner のコレクションに連絡先 500（<paramref name="state"/>・owner も watcher も 40）を置き、同じインスタンスを
    /// watcher のコレクションへも足して保存する（外部キーとナビゲーションは食い違わない）。どの実装先も書く前に断り、フックは
    /// 鳴らず、保存先は変わらない
    /// </summary>
    private static ParityScenario SameInstanceTwoCollections(string name, RowState state) =>
        new(
            name,
            state == RowState.Added
                ? Basic
                :
                [
                    .. Basic,
                    new SeedOp(new CustomerRow(40, "Dee")),
                    new SeedOp(new ContactRow(500, "x", 40, Watcher: 40)),
                ],
            [
                new NewRootOp(
                    "A",
                    new CustomerRow(40, "Dee"),
                    state == RowState.Added ? RowState.Added : RowState.Unchanged
                ),
                new AddChildOp(
                    "A",
                    "customers/40",
                    new ContactRow(500, "y", 40, Watcher: 40),
                    state,
                    ParityContactRelation.Owner
                ),
                new LinkContactOp(
                    "A",
                    "customers/40",
                    "contacts/500",
                    ParityContactRelation.Watcher
                ),
                new SaveOp("A"),
                ParityExpect.Throws($"{name}-refused", "InvalidOperationException"),
                ParityExpect.RawHooks($"{name}-fires-no-hook", []),
                ParityExpect.StateRows($"{name}-writes-nothing", state == RowState.Added ? 8 : 10),
            ]
        );

    /// <summary>
    /// 顧客 3（注文なし）を紹介者に持つ顧客 4 を読み（<paramref name="modifyDependent"/> のときはメモを編集）、3 に削除の印を付けて、
    /// 4・3 の並びで 1 回の複数形のグラフ保存にする。紹介者の外部キーは任意 × NO ACTION（DB は断る）
    /// </summary>
    private static ParityScenario ReferencedReferrerDelete(string name, bool modifyDependent) =>
        new(
            name,
            [
                .. Basic,
                new SeedOp(new CustomerRow(3, "Cy")),
                new SeedOp(new CustomerRow(4, "Dan", Referrer: 3)),
            ],
            [
                new LoadOp("A", "customers", 4),
                .. modifyDependent
                    ? [new EditCustomerOp("A", 4, Note: "edited")]
                    : Array.Empty<ParityOp>(),
                new LoadOp("B", "customers", 3),
                new MarkRemovedOp("B", "customers/3"),
                // 顧客 3 は顧客 4 に参照されている（インメモリの受け入れた差＝P1）
                new DeviatingOp(
                    new SaveManyCustomersOp(["A", "B"]),
                    ParityDeviation.ParentDeleteWithChildren
                ),
                new LoadOp("C", "customers", 4),
            ]
        );

    /// <summary>
    /// 連絡先 500 の <paramref name="relation"/> の外部キーが顧客 3（注文なし）を指す。顧客 2 を読んで連絡先を別の関係
    /// （owner か watcher＝2 を指す関係）のコレクションに持ち（<paramref name="modifyDependent"/> のときはラベルを編集）、3 に削除の印を
    /// 付けて、2・3 の並びで 1 回の複数形のグラフ保存にする
    /// </summary>
    private static ParityScenario ReferencedContactDelete(
        string name,
        ParityContactRelation relation,
        bool modifyDependent
    )
    {
        var hold =
            relation == ParityContactRelation.Owner
                ? ParityContactRelation.Watcher
                : ParityContactRelation.Owner;
        var contact = relation switch
        {
            ParityContactRelation.Owner => new ContactRow(500, "c", 3, Watcher: 2),
            ParityContactRelation.Watcher => new ContactRow(500, "c", 2, Watcher: 3),
            ParityContactRelation.Backup => new ContactRow(500, "c", 2, Backup: 3),
            ParityContactRelation.Fallback => new ContactRow(500, "c", 2, Fallback: 3),
            _ => new ContactRow(500, "c", 2, CustomerCode: CustomerRow.DefaultCode(3)),
        };

        return new(
            name,
            [.. Basic, new SeedOp(new CustomerRow(3, "Cy")), new SeedOp(contact)],
            [
                new LoadOp("A", "customers", 2),
                new AttachContactOp("A", "customers/2", 500, hold),
                .. modifyDependent
                    ? [new EditContactOp("A", 500, "edited")]
                    : Array.Empty<ParityOp>(),
                new LoadOp("B", "customers", 3),
                new MarkRemovedOp("B", "customers/3"),
                // 顧客 3 は連絡先 500 に参照されている（インメモリの受け入れた差＝P1）
                new DeviatingOp(
                    new SaveManyCustomersOp(["A", "B"]),
                    ParityDeviation.ParentDeleteWithChildren
                ),
                new LoadOp("C", "contacts", 500),
            ]
        );
    }

    /// <summary>
    /// 顧客 3 のコード（連絡先の customer_code が参照する代替キー）を変える。<paramref name="referenced"/> のときは連絡先 500 が
    /// そのコードを参照している（外部キーは ON UPDATE NO ACTION＝DB は断る）
    /// </summary>
    /// <summary>代替キーを変える顧客と同じグラフ保存に入れる紹介先の顧客の状態</summary>
    private enum AlternateKeyChild
    {
        /// <summary>読んだ後に他者が消した（insertWhenUpdateMissing なし）</summary>
        Gone,

        /// <summary>読んだ後に他者が消した行を insertWhenUpdateMissing で保存する</summary>
        GoneInsertWhenMissing,

        /// <summary>Before のフックが更新を飛ばす</summary>
        HookSkipped,
    }

    /// <summary>
    /// 顧客 3 のコードを変え、紹介先の顧客 4（顧客 3 を紹介者に持つ）の備考を変えて、顧客 3 を根にグラフ保存 1 回で保存する。
    /// 顧客 4 は <paramref name="child"/> の状態にする
    /// </summary>
    private static ParityScenario AlternateKeyUpdateWithChild(
        string name,
        AlternateKeyChild child
    ) =>
        new(
            name,
            [
                .. Basic,
                new SeedOp(new CustomerRow(3, "Cy")),
                new SeedOp(new CustomerRow(4, "Di", Referrer: 3)),
            ],
            [
                new LoadOp("A", "customers", 3),
                new AttachChildCustomerOp("A", 4),
                .. child == AlternateKeyChild.HookSkipped
                    ? Array.Empty<ParityOp>()
                    : [new OutsideDeleteOp("customers", 4)],
                new EditCustomerOp("A", 3, Note: "n", Code: "NEW3"),
                new EditCustomerOp("A", 4, Note: "m"),
                new SaveOp(
                    "A",
                    new(InsertWhenUpdateMissing: child == AlternateKeyChild.GoneInsertWhenMissing)
                ),
                .. child switch
                {
                    // 消えた顧客 4 は挿入に切り替わり、顧客 3 の新しいコードと一緒に入る
                    AlternateKeyChild.GoneInsertWhenMissing => (ParityOp[])
                        [
                            ParityExpect.Succeeds("gone-child-inserted-succeeds"),
                            ParityExpect.StateRow(
                                "gone-child-writer",
                                "customers/3",
                                "code='NEW3'"
                            ),
                            ParityExpect.StateRow(
                                "gone-child-inserted",
                                "customers/4",
                                "note='m'",
                                "referrer=3"
                            ),
                        ],
                    // insertWhenUpdateMissing なしでは NotFound で全体が巻き戻り、顧客 3 のコードは元のまま
                    AlternateKeyChild.Gone =>
                    [
                        ParityExpect.Throws("gone-child-refused", "SaveConflictException"),
                        ParityExpect.StateRow(
                            "gone-child-writer-unchanged",
                            "customers/3",
                            "code='K3'"
                        ),
                    ],
                    // 飛ばした顧客 4 は書かず、顧客 3 の新しいコードだけが入る
                    _ =>
                    [
                        ParityExpect.Succeeds("skipped-child-succeeds"),
                        ParityExpect.StateRow("skipped-child-writer", "customers/3", "code='NEW3'"),
                        ParityExpect.StateRow(
                            "skipped-child-unchanged",
                            "customers/4",
                            "note=null"
                        ),
                    ],
                },
                new LoadOp("C", "customers", 3),
                new LoadOp("D", "customers", 4),
            ],
            child == AlternateKeyChild.HookSkipped
                ? new ParityBackendOptions(
                    HookRules: [new ParityHookRule("Update:customers/4", ParityHookBehavior.Skip)]
                )
                : null
        );

    private static ParityScenario AlternateKeyUpdate(string name, bool referenced, bool viaSave) =>
        new(
            name,
            [
                .. Basic,
                new SeedOp(new CustomerRow(3, "Cy")),
                .. referenced
                    ?
                    [
                        new SeedOp(
                            new ContactRow(500, "c", 2, CustomerCode: CustomerRow.DefaultCode(3))
                        ),
                    ]
                    : Array.Empty<ParityOp>(),
            ],
            [
                new LoadOp("A", "customers", 3),
                new EditCustomerOp("A", 3, Note: "n", Code: "NEW3"),
                // 参照されているコードの更新はインメモリの受け入れた差（P2）
                referenced
                    ? new DeviatingOp(Save(viaSave), ParityDeviation.ReferencedKeyUpdate)
                    : Save(viaSave),
                new LoadOp("C", "customers", 3),
            ]
        );

    /// <summary>
    /// 顧客 3 のコードを変えてグラフ保存する。顧客 3 は連絡先 500（変更なし）と 501（ラベルを編集）を owner のコレクションに持ち、
    /// 2 つとも customer_code で顧客 3 の古いコードを参照する。<paramref name="byHook"/> のときは Before のフックが追跡の後で
    /// コードを書き換え、そうでなければ保存の前に書き換える。保存の後、メモリの連絡先の customer_code とその親への参照が元のままかを
    /// 実装先ごとに確かめる（EF Core が代替キーの変更を拾って依存行を付け替えない）
    /// </summary>
    private static ParityScenario AlternateKeyUpdateWithDependents(string name, bool byHook)
    {
        var code = CustomerRow.DefaultCode(3);

        return new(
            name,
            [
                .. Basic,
                new SeedOp(new CustomerRow(3, "Cy")),
                new SeedOp(new ContactRow(500, "c", 3, CustomerCode: code)),
                new SeedOp(new ContactRow(501, "d", 3, CustomerCode: code)),
            ],
            [
                new LoadOp("A", "customers", 3),
                new AttachContactOp("A", "customers/3", 500, ParityContactRelation.Owner),
                new AttachContactOp("A", "customers/3", 501, ParityContactRelation.Owner),
                new EditContactOp("A", 501, "d2"),
                new EditCustomerOp("A", 3, Note: "n", Code: byHook ? null : "NEW3"),
                // 参照されているコードの更新はインメモリの受け入れた差（P2）
                new DeviatingOp(new SaveOp("A"), ParityDeviation.ReferencedKeyUpdate),
                new ExpectOp("dependents-unchanged", e => DependentsKeepOldCode(e, code)),
                new LoadOp("C", "customers", 3),
                new LoadOp("D", "contacts", 500),
                new LoadOp("E", "contacts", 501),
            ],
            byHook
                ? new ParityBackendOptions(
                    HookRules:
                    [
                        new ParityHookRule(
                            "Update:customers/3",
                            ParityHookBehavior.EditKeyThenContinue
                        ),
                    ]
                )
                // フックの無いグラフ保存（Save フックの段の並びの違いを持ち込まない）
                : new ParityBackendOptions(WithHooks: false)
        );
    }

    /// <summary>
    /// スロット A の顧客が owner で持つ連絡先が、customer_code に <paramref name="code"/> を持ったままで、その親への参照が空か
    /// （顧客の customer_code のコレクションも空か）。満たさなければ理由を返す
    /// </summary>
    private static string? DependentsKeepOldCode(ParityExpectation e, string code)
    {
        var customer = (CustomerEntity)e.Backend.Slots["A"];

        if (customer.ContactsByCustomerCodeCustomer.Count != 0)
        {
            return $"the customer holds {customer.ContactsByCustomerCodeCustomer.Count} contact(s) by its code";
        }

        foreach (var contact in customer.ContactsByOwner)
        {
            if (contact.CustomerCode?.Value != code || contact.CustomerCodeCustomer is not null)
            {
                return $"contacts/{contact.ContactId.Value} customer_code={contact.CustomerCode?.Value} "
                    + $"navigation={(contact.CustomerCodeCustomer is null ? "null" : "set")}";
            }
        }

        return customer.ContactsByOwner.Count == 2
            ? null
            : $"the customer holds {customer.ContactsByOwner.Count} contact(s) by owner";
    }

    /// <summary>顧客 3 を、グラフ保存か UpdateAsync で保存する手</summary>
    private static ParityOp Save(bool viaSave) =>
        viaSave ? new SaveOp("A") : new UpdateCustomerOp("A");

    /// <summary>
    /// 顧客 3（注文なし）の更新か削除に、Save フックの振る舞い <paramref name="behavior"/> を当てる（同じ保存の顧客 4 には当てない）
    /// </summary>
    /// <param name="afterSave">保存の直後に差し込む期待（手の並びは変えない）</param>
    private static ParityScenario HookOnCustomer(
        string name,
        ParityHookBehavior behavior,
        bool delete,
        IReadOnlyList<ExpectOp>? afterSave = null
    ) =>
        new(
            name,
            [.. Basic, new SeedOp(new CustomerRow(3, "Cy")), new SeedOp(new CustomerRow(4, "Dan"))],
            [
                new LoadOp("A", "customers", 3),
                delete
                    ? new MarkRemovedOp("A", "customers/3")
                    : new EditCustomerOp("A", 3, Note: "n"),
                new LoadOp("B", "customers", 4),
                new EditCustomerOp("B", 4, Note: "m"),
                new SaveManyCustomersOp(["A", "B"]),
                .. afterSave ?? [],
                new LoadOp("C", "customers", 3),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule((delete ? "Delete" : "Update") + ":customers/3", behavior),
                ]
            )
        );

    /// <summary>名前でシナリオを引く</summary>
    public static ParityScenario Get(string name) => All.Single(s => s.Name == name);

    /// <summary>
    /// 顧客 6 を紹介者 4 から 5 へ付け替え、4 を消す（紹介者の外部キーは NO ACTION。削除が先に走ると外部キーに断られる）。
    /// 根の並びは <paramref name="roots"/>（A＝消す顧客・B＝付け替え先）
    /// </summary>
    private static ParityScenario MoveChildAndDeleteOldParent(
        string name,
        IReadOnlyList<string> roots
    ) =>
        new(
            name,
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Dan")),
                new SeedOp(new CustomerRow(5, "Eve")),
                new SeedOp(new CustomerRow(6, "Fay", Referrer: 4)),
            ],
            [
                new NewRootOp("A", new CustomerRow(4, "Dan"), RowState.Removed),
                new NewRootOp("B", new CustomerRow(5, "Eve"), RowState.Unchanged),
                new AddChildOp(
                    "B",
                    "customers/5",
                    new CustomerRow(6, "Fay", Referrer: 5),
                    RowState.Updated
                ),
                new SaveManyCustomersOp(roots),
                ParityExpect.Succeeds("D-move-succeeds"),
                ParityExpect.Hooks(
                    "D-move-update-before-delete",
                    "Before",
                    "Update:customers/6",
                    "Delete:customers/4"
                ),
                ParityExpect.StateKeys("D-move-rows", KeysWithCustomers(1, 2, 5, 6)),
                ParityExpect.StateRow("D-move-referrer", "customers/6", "referrer=5"),
                new LoadOp("C", "customers", 6),
            ]
        );

    /// <summary>
    /// 明細 100 を注文 10 から 11 へ付け替え、10 を、残った明細 101 ごと消す。根の並びは <paramref name="roots"/>
    /// （A＝消す注文・B＝付け替え先）
    /// </summary>
    private static ParityScenario MoveLineAndDeleteOldOrder(
        string name,
        IReadOnlyList<string> roots
    ) =>
        new(
            name,
            Basic,
            [
                new NewRootOp(
                    "A",
                    new OrderRow(10, 1, 100m, "first", Payload: [0x01, 0x02], Thumb: [0x0A]),
                    RowState.Removed
                ),
                new AddChildOp(
                    "A",
                    "orders/10",
                    new LineRow(101, 10, "ink", 5),
                    RowState.Unchanged
                ),
                new NewRootOp("B", new OrderRow(11, 1, 50m), RowState.Unchanged),
                new AddChildOp(
                    "B",
                    "orders/11",
                    new LineRow(100, 11, "pen", 2, 0.5m),
                    RowState.Updated
                ),
                new SaveManyOrdersOp(roots),
                ParityExpect.Succeeds("D-cascade-move-succeeds"),
                ParityExpect.Hooks(
                    "D-cascade-move-update-before-delete",
                    "Before",
                    "Update:order_lines/100",
                    "Delete:order_lines/101",
                    "Delete:orders/10"
                ),
                new LoadOp("C", "order_lines", 100),
            ]
        );

    /// <summary>
    /// 顧客 5 の名前を、消す顧客 4 の名前（一意制約）に変える。根の並びは <paramref name="roots"/>（A＝更新する顧客・B＝消す顧客）。
    /// EF Core は 1 回の SaveChanges の中で文の順を自分で決める（このシナリオは、EF Core の並べ方がそれを通すことを固定する）
    /// </summary>
    private static ParityScenario UpdatedTakesDeletedUniqueValue(
        string name,
        IReadOnlyList<string> roots
    ) =>
        new(
            name,
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Dan")),
                new SeedOp(new CustomerRow(5, "Eve")),
            ],
            [
                new NewRootOp("A", new CustomerRow(5, "Dan"), RowState.Updated),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp(roots),
                ParityExpect.Succeeds("J3-succeeds"),
                ParityExpect.Hooks(
                    "J3-delete-before-update",
                    "Before",
                    "Delete:customers/4",
                    "Update:customers/5"
                ),
                new LoadOp("C", "customers", 5),
            ]
        );

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
public static partial class ParityExpect
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

    /// <summary>直前の手の戻り値</summary>
    public static ExpectOp Outcome(string label, string expected) =>
        new(
            label,
            e =>
                e.Previous.Outcome == expected
                    ? null
                    : $"outcome {e.Previous.Outcome}, expected {expected}"
        );

    /// <summary>直前の手が、指定の型の例外で終わった</summary>
    public static ExpectOp Throws(string label, string exceptionType) =>
        new(
            label,
            e =>
                e.Previous.Outcome.StartsWith($"throws {exceptionType}", StringComparison.Ordinal)
                    ? null
                    : $"outcome {e.Previous.Outcome}, expected throws {exceptionType}"
        );

    /// <summary>直前の手の後の保存先が、指定の数の行を持つ</summary>
    public static ExpectOp StateRows(string label, int count) =>
        new(
            label,
            e =>
                e.Previous.State.Count == count
                    ? null
                    : $"rows {e.Previous.State.Count}, expected {count}"
        );

    /// <summary>直前の手が例外にならずに終わった</summary>
    public static ExpectOp Succeeds(string label) =>
        new(
            label,
            e =>
                e.Previous.Outcome.StartsWith("throws", StringComparison.Ordinal)
                    ? $"outcome {e.Previous.Outcome}"
                    : null
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
