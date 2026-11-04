using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>グラフ保存の書く順（削除を含む保存で、新しい行を後回しにしない形）のシナリオ</summary>
public static partial class BackendParityScenarios
{
    /// <summary>
    /// 削除を含む保存で、削除する行と値の重ならない新しい行は後回しにせず、グラフの順のまま書く。後回しにすると、元の親の削除が
    /// 子の付け替え（UPDATE）より先に走るか、新しい行を指す更新が新しい行の挿入より先に走り、NO ACTION の外部キーに阻まれる。
    /// 種はどれも顧客 Dan(4)・Eve(5) と、Dan を紹介者に持つ Fay(6)。
    /// </summary>
    private static IEnumerable<ParityScenario> GraphSaveOrderScenarios()
    {
        // 子を新しい親（Added）へ付け替えてから元の親を消す
        yield return new(
            "graph-move-child-to-new-parent-and-delete-old-parent",
            MoveSeeds,
            [
                new NewRootOp("B", new CustomerRow(7, "Gil"), RowState.Added),
                new AddChildOp(
                    "B",
                    "customers/7",
                    new CustomerRow(6, "Fay", Referrer: 7),
                    RowState.Updated
                ),
                new NewRootOp("A", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp(["B", "A"]),
                ParityExpect.Succeeds("order-move-to-new-parent-succeeds"),
                ParityExpect.StateKeys(
                    "order-move-to-new-parent-rows",
                    KeysWithCustomers(1, 2, 5, 6, 7)
                ),
                ParityExpect.StateRow(
                    "order-move-to-new-parent-referrer",
                    "customers/6",
                    "referrer=7"
                ),
            ],
            new ParityBackendOptions(WithHooks: false)
        );

        // 新しい親と、それを指すよう更新した別の根（子を新しい親のコレクションに入れない）を、行の削除と一緒に保存する。
        // 消すのは元の親 Dan(4) か、関係のない Eve(5)。どちらも新しい親とは値が重ならない
        yield return NewRowReferencedByAnotherRoot(
            "graph-new-row-referenced-by-another-root-deletes-old-parent",
            new CustomerRow(4, "Dan")
        );
        yield return NewRowReferencedByAnotherRoot(
            "graph-new-row-referenced-by-another-root-deletes-unrelated-row",
            new CustomerRow(5, "Eve")
        );
    }

    /// <summary>付け替えのシナリオの種（<see cref="Basic"/>＋Dan(4)・Eve(5)・Dan を紹介者に持つ Fay(6)）</summary>
    private static IReadOnlyList<ParityOp> MoveSeeds =>
        [
            .. Basic,
            new SeedOp(new CustomerRow(4, "Dan")),
            new SeedOp(new CustomerRow(5, "Eve")),
            new SeedOp(new CustomerRow(6, "Fay", Referrer: 4)),
        ];

    /// <summary>顧客 <paramref name="customers"/> と、<see cref="Basic"/> の注文・明細が残った保存先のキー</summary>
    private static string[] KeysWithCustomers(params int[] customers) =>
        [
            .. customers.Select(id => $"customers/{id}"),
            "orders/10",
            "orders/11",
            "orders/12",
            "order_lines/100",
            "order_lines/101",
            "order_lines/102",
        ];

    /// <summary>新しい親 Gil(7)・それを指す Fay(6)・消す行 <paramref name="deleted"/> を、別々の根として 1 回で保存する</summary>
    private static ParityScenario NewRowReferencedByAnotherRoot(string name, CustomerRow deleted) =>
        new(
            name,
            MoveSeeds,
            [
                new NewRootOp("D", new CustomerRow(7, "Gil"), RowState.Added),
                new NewRootOp("C", new CustomerRow(6, "Fay", Referrer: 7), RowState.Updated),
                new NewRootOp("X", deleted, RowState.Removed),
                new SaveManyCustomersOp(["D", "C", "X"]),
                ParityExpect.Succeeds("order-referenced-new-row-succeeds"),
                ParityExpect.StateKeys(
                    "order-referenced-new-row-rows",
                    deleted.Id == 4
                        ? KeysWithCustomers(1, 2, 5, 6, 7)
                        : KeysWithCustomers(1, 2, 4, 6, 7)
                ),
                ParityExpect.StateRow(
                    "order-referenced-new-row-referrer",
                    "customers/6",
                    "referrer=7"
                ),
            ],
            new ParityBackendOptions(WithHooks: false)
        );

    /// <summary>
    /// グラフ保存が書くのはカスケードの閉包だけで、親への逆参照（<c>line.Order</c>・<c>order.Customer</c>）は辿らない。
    /// 逆参照まで辿ると、子を 1 件削除したときに親と兄弟まで削除し、別インスタンスの
    /// 同じ親を 2 つ持つグラフで追跡が衝突し、保存を頼んでいない親を書く。EF Core が逆参照を埋める形（Include で読む＝fix-up）に
    /// しないと EF Core が TrackGraph へ戻った退行を捕まえないので、読みは Include の手（全機能面・リモートなし）にする。
    /// 元のテストは Save フックを登録しないので、フックなしで流す。
    /// </summary>
    private static IEnumerable<ParityScenario> GraphSaveRuntimeScenarios()
    {
        var noHooks = new ParityBackendOptions(WithHooks: false);

        // 親のコレクション経由で読んだ明細 1 件を、明細のリポジトリから直接保存すると、その 1 行だけが消える
        yield return new(
            "graph-include-delete-child-saved-from-child",
            Basic,
            [
                new LoadOrderWithLinesOp(S, 10),
                new MarkRemovedOp(S, "order_lines/100"),
                new SaveMemberOp(S, "order_lines/100"),
                ParityExpect.Outcome("include-child-saved-from-child-rows", "rows=1"),
                ParityExpect.StateKeys(
                    "include-child-saved-from-child-left",
                    LeftAfter(lines: [101, 102])
                ),
            ],
            noHooks
        );

        // 親を Include して読んだ明細を削除して保存しても、親は消えない
        yield return new(
            "graph-include-delete-child-with-parent-included",
            Basic,
            [
                new LoadWithParentOp(S, "order_lines", 100, ParityParentInclude.LineOrder),
                new MarkRemovedOp(S, "order_lines/100"),
                new SaveOp(S),
                ParityExpect.Outcome("include-child-with-parent-rows", "rows=1"),
                ParityExpect.StateKeys(
                    "include-child-with-parent-left",
                    LeftAfter(lines: [101, 102])
                ),
            ],
            noHooks
        );

        // 注文を根に明細 1 件を削除して保存すると、兄弟の明細は残る
        yield return new(
            "graph-include-delete-child-through-root-keeps-siblings",
            Basic,
            [
                new LoadOrderWithLinesOp(S, 10),
                new MarkRemovedOp(S, "order_lines/101"),
                new SaveOp(S),
                ParityExpect.Outcome("include-child-through-root-rows", "rows=1"),
                ParityExpect.StateKeys(
                    "include-child-through-root-left",
                    LeftAfter(lines: [100, 102])
                ),
            ],
            noHooks
        );

        // 同じ顧客を別インスタンスで持つ注文 2 件をまとめて保存できる（追跡が衝突しない）
        yield return new(
            "graph-include-save-roots-sharing-parent-key",
            Basic,
            [
                new LoadWithParentOp("O1", "orders", 10, ParityParentInclude.OrderCustomer),
                new LoadWithParentOp("O2", "orders", 11, ParityParentInclude.OrderCustomer),
                ParityExpect.DistinctParents(
                    "include-sharing-parent-separate-instances",
                    "O1",
                    "orders/10",
                    "O2",
                    "orders/11"
                ),
                new EditMemoOp("O1", 10, "first-edited"),
                new EditMemoOp("O2", 11, "second-edited"),
                new SaveManyOrdersOp(["O1", "O2"]),
                ParityExpect.Outcome("include-sharing-parent-rows", "rows=2"),
                ParityExpect.StateRow(
                    "include-sharing-parent-first",
                    "orders/10",
                    "memo='first-edited'"
                ),
                ParityExpect.StateRow(
                    "include-sharing-parent-second",
                    "orders/11",
                    "memo='second-edited'"
                ),
            ],
            noHooks
        );

        // Include した親の変更は書かず、親の RowState も触らない
        yield return new(
            "graph-include-save-child-does-not-write-parent",
            Basic,
            [
                new LoadWithParentOp(S, "orders", 10, ParityParentInclude.OrderCustomer),
                new EditIncludedParentOp(S, "orders/10", "changed"),
                new EditMemoOp(S, 10, "edited"),
                new SaveOp(S),
                ParityExpect.Outcome("include-parent-not-written-rows", "rows=1"),
                ParityExpect.RootState(
                    "include-parent-not-written-root-unchanged",
                    S,
                    RowState.Unchanged
                ),
                ParityExpect.ParentRowState(
                    "include-parent-not-written-parent-updated",
                    S,
                    "orders/10",
                    RowState.Updated
                ),
                ParityExpect.StateRow(
                    "include-parent-not-written-memo",
                    "orders/10",
                    "memo='edited'"
                ),
                ParityExpect.StateRow(
                    "include-parent-not-written-parent-note",
                    "customers/1",
                    "note='vip'"
                ),
            ],
            noHooks
        );

        // cascadeSave=false でも、削除の根の子孫は cascadeDelete で子から消える（Include で読んだ形と、リモートでも組める形）
        yield return new(
            "graph-include-delete-root-without-cascade-save",
            Basic,
            [
                new LoadOrderWithLinesOp(S, 10),
                new MarkRemovedOp(S, "orders/10"),
                new SaveOp(S, new ParitySaveOptions(CascadeSave: false, CascadeDelete: true)),
                ParityExpect.Outcome("delete-root-without-cascade-save-rows", "rows=3"),
                ParityExpect.StateKeys(
                    "delete-root-without-cascade-save-left",
                    "customers/1",
                    "customers/2",
                    "orders/11",
                    "orders/12",
                    "order_lines/102"
                ),
            ],
            noHooks
        );
        yield return new(
            "graph-delete-root-without-cascade-save-attached",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new AttachLineOp(S, 100),
                new AttachLineOp(S, 101),
                new MarkRemovedOp(S, "orders/10"),
                new SaveOp(S, new ParitySaveOptions(CascadeSave: false, CascadeDelete: true)),
                ParityExpect.Outcome("delete-attached-root-without-cascade-save-rows", "rows=3"),
                ParityExpect.StateKeys(
                    "delete-attached-root-without-cascade-save-left",
                    "customers/1",
                    "customers/2",
                    "orders/11",
                    "orders/12",
                    "order_lines/102"
                ),
            ],
            noHooks
        );

        // 注文を別の顧客へ付け替えて元の顧客を削除する（顧客 → 注文は CASCADE。削除が先に走ると付け替えた注文まで消える）
        yield return MoveOrderAndDeleteOldCustomer(
            "graph-move-order-and-delete-old-customer-removed-first",
            ["A", "B"]
        );
        yield return MoveOrderAndDeleteOldCustomer(
            "graph-move-order-and-delete-old-customer-kept-first",
            ["B", "A"]
        );

        // J3：削除する行の一意な値（顧客の name）を別の行の更新が取る。削除する顧客は子（注文と明細）を持つ
        yield return UpdatedTakesUniqueValueOfDeletedRowWithChildren(
            "graph-updated-row-takes-unique-value-of-deleted-row-with-children-updated-first",
            ["U", "R"]
        );
        yield return UpdatedTakesUniqueValueOfDeletedRowWithChildren(
            "graph-updated-row-takes-unique-value-of-deleted-row-with-children-removed-first",
            ["R", "U"]
        );

        // 削除する行の代替キーの値（顧客の code＝連絡先の customer_code が参照する）を、別の行の更新が取る。どの実装先も削除の後に
        // 代替キーを書いて成立する（EF Core は代替キーを自分で書くので、後回しの行を 2 つめの文脈で削除の後に保存する）
        yield return new(
            "graph-updated-row-takes-alternate-key-of-deleted-row",
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Dan")),
                new SeedOp(new CustomerRow(5, "Eve")),
            ],
            [
                new NewRootOp("A", new CustomerRow(5, "Eve", Code: "K4"), RowState.Updated),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp(["A", "B"]),
                ParityExpect.Outcome("alternate-key-taken-saves-two-rows", "rows=2"),
                ParityExpect.StateRow("alternate-key-taken-by-update", "customers/5", "code='K4'"),
            ],
            new ParityBackendOptions(WithHooks: false)
        );

        // 同じ形を、EF Core の DbContext を利用者が持つ 2 本の既存の接続から交互に作るファクトリで流し、続けて同じ実装先で読み、
        // もう 1 回保存する（後回しの行の 2 つめの文脈は、1 つめの接続を借りた後、自分の接続を利用者の持ち物のまま戻す。持ち主と
        // して戻すと、文脈の破棄が利用者の接続を破棄し、その接続を渡された次の文脈が開けない）
        yield return new(
            "graph-updated-row-takes-alternate-key-of-deleted-row-user-connections",
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Dan")),
                new SeedOp(new CustomerRow(5, "Eve")),
            ],
            [
                new NewRootOp("A", new CustomerRow(5, "Eve", Code: "K4"), RowState.Updated),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp(["A", "B"]),
                ParityExpect.Outcome("user-connections-saves-two-rows", "rows=2"),
                new LoadOp("C", "customers", 5),
                new NewRootOp("D", new CustomerRow(5, "Eve2", Code: "K4"), RowState.Updated),
                new SaveManyCustomersOp(["D"]),
                ParityExpect.Outcome("user-connections-saves-again", "rows=1"),
                ParityExpect.StateRow(
                    "user-connections-written-again",
                    "customers/5",
                    "name=Eve2",
                    "code='K4'"
                ),
            ],
            new ParityBackendOptions(WithHooks: false),
            EfCoreUserConnections: true
        );

        // 同じ形で、値を取る行の下に、その新しい値を customer_code で参照する新しい連絡先を足す。後回しの行の下の行は、代替キーを
        // 書いた後に書く（EF Core が代替キーを SaveChanges の後に書くと、連絡先の挿入が先に走って外部キーに断られる）。連絡先の
        // watcher は同じ保存の別の根（顧客 1＝EF Core では 1 つめの文脈が追跡する）を指す。2 つめの文脈がその参照を辿って顧客 1 を
        // 追跡すると、顧客 1 を 2 度書くか挿入しようとして断られる
        yield return new(
            "graph-updated-row-takes-alternate-key-of-deleted-row-with-new-dependent",
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Dan")),
                new SeedOp(new CustomerRow(5, "Eve")),
            ],
            [
                new NewRootOp("A", new CustomerRow(5, "Eve", Code: "K4"), RowState.Updated),
                new AddChildOp(
                    "A",
                    "customers/5",
                    new ContactRow(600, "n", 5, Watcher: 1, CustomerCode: "K4"),
                    RowState.Added,
                    ParityContactRelation.CustomerCode
                ),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new NewRootOp("C", new CustomerRow(1, "Alice", "gold"), RowState.Updated),
                new PointContactAtOp("A", 600, ParityContactRelation.Watcher, 1, CustomerSlot: "C"),
                new SaveManyCustomersOp(["A", "B", "C"]),
                ParityExpect.Outcome(
                    "alternate-key-taken-with-dependent-saves-four-rows",
                    "rows=4"
                ),
                ParityExpect.StateRow(
                    "alternate-key-taken-other-root-written-once",
                    "customers/1",
                    "note='gold'"
                ),
                ParityExpect.StateRow(
                    "alternate-key-taken-with-dependent-by-update",
                    "customers/5",
                    "code='K4'"
                ),
                ParityExpect.StateRow(
                    "alternate-key-taken-dependent-refers-new-value",
                    "contacts/600",
                    "customer_code='K4'"
                ),
            ],
            new ParityBackendOptions(WithHooks: false)
        );
    }

    /// <summary><see cref="Basic"/> の顧客と注文に、明細 <paramref name="lines"/> だけが残った保存先のキー</summary>
    private static string[] LeftAfter(int[] lines) =>
        [
            "customers/1",
            "customers/2",
            "orders/10",
            "orders/11",
            "orders/12",
            .. lines.Select(id => $"order_lines/{id}"),
        ];

    /// <summary>
    /// 注文 12 を顧客 2 から 3 へ付け替え、顧客 2 を消す（読んだ顧客と読んだ注文で組む）。根の並びは <paramref name="roots"/>
    /// （A＝消す顧客・B＝付け替え先）
    /// </summary>
    private static ParityScenario MoveOrderAndDeleteOldCustomer(
        string name,
        IReadOnlyList<string> roots
    ) =>
        new(
            name,
            [.. Basic, new SeedOp(new CustomerRow(3, "Carol"))],
            [
                new LoadOp("A", "customers", 2),
                new MarkRemovedOp("A", "customers/2"),
                new LoadOp("B", "customers", 3),
                new AttachMovedOrderOp("B", "customers/3", 12, 3),
                new SaveManyCustomersOp(roots),
                ParityExpect.Outcome("move-order-delete-old-customer-rows", "rows=2"),
                ParityExpect.StateKeys(
                    "move-order-delete-old-customer-left",
                    KeysWithCustomers(1, 3)
                ),
                ParityExpect.StateRow(
                    "move-order-delete-old-customer-moved",
                    "orders/12",
                    "customer=3"
                ),
            ],
            new ParityBackendOptions(WithHooks: false)
        );

    /// <summary>
    /// 顧客 5（Eve）の名前を、消す顧客 4（Dan＝注文 13 と明細 103 を持つ）の名前（一意制約）に変える。根の並びは
    /// <paramref name="roots"/>（U＝更新する顧客・R＝消す顧客）。削除は子（明細 → 注文 → 顧客）から順に走るので、EF Core の
    /// 1 回の SaveChanges では、一意インデックス（<c>HasIndex(Name).IsUnique()</c>）が無いと、どの行にも依存しない更新が
    /// 最初の段（明細の削除と同じ段）に並び、顧客 4 の削除より先に走る（子を持たない行どうしなら、同じ表の削除が更新より先に
    /// 並ぶので、一意インデックスが無くても通ってしまう）
    /// </summary>
    private static ParityScenario UpdatedTakesUniqueValueOfDeletedRowWithChildren(
        string name,
        IReadOnlyList<string> roots
    ) =>
        new(
            name,
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Dan")),
                new SeedOp(new CustomerRow(5, "Eve")),
                new SeedOp(new OrderRow(13, 4, 20m)),
                new SeedOp(new LineRow(103, 13, "cap", 1)),
            ],
            [
                new NewRootOp("U", new CustomerRow(5, "Dan"), RowState.Updated),
                new NewRootOp("R", new CustomerRow(4, "Dan"), RowState.Removed),
                new AddChildOp("R", "customers/4", new OrderRow(13, 4, 20m), RowState.Unchanged),
                new AddChildOp(
                    "R",
                    "orders/13",
                    new LineRow(103, 13, "cap", 1),
                    RowState.Unchanged
                ),
                new SaveManyCustomersOp(roots),
                ParityExpect.Succeeds("J3-children-succeeds"),
                ParityExpect.Outcome("J3-children-rows", "rows=4"),
                ParityExpect.StateKeys("J3-children-left", KeysWithCustomers(1, 2, 5)),
                ParityExpect.StateRow("J3-children-taken", "customers/5", "name=Dan"),
            ],
            new ParityBackendOptions(WithHooks: false)
        );
}
