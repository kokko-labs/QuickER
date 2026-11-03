using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 直接の CRUD・BulkInsert・クエリ（条件・並び・窓・射影）・Include・一意制約のシナリオ（インメモリ固有のテストと、SQLite の
/// QuickER 版と EF Core の 2 者比較から移した行）。全機能面を使う手（BulkInsert・Query()・Include・射影）を含むシナリオは
/// リモートで流さない。
/// </summary>
public static partial class BackendParityScenarios
{
    private static IEnumerable<ParityScenario> CrudRuntimeScenarios()
    {
        // 直接の CRUD：読んだ行は Unchanged・無いキーの取得は null・無いキーの UpdateAsync は false・
        // 同じキーの 2 回目の DeleteAsync は false
        yield return new(
            "crud-update-missing-and-delete-twice",
            Basic,
            [
                new InsertOp(new CustomerRow(3, "Carol", "n")),
                new LoadOp(S, "customers", 3),
                ParityExpect.RootState("crud-loaded-row-is-unchanged", S, RowState.Unchanged),
                new LoadOp("B", "customers", 999),
                ParityExpect.Outcome("crud-missing-key-reads-null", "null"),
                new EditCustomerOp(S, 3, Note: "changed"),
                new UpdateCustomerOp(S),
                ParityExpect.Outcome("crud-update-succeeds", "updated=True"),
                ParityExpect.RootState("crud-update-keeps-updated", S, RowState.Updated),
                ParityExpect.StateRow("crud-update-is-stored", "customers/3", "note='changed'"),
                new NewRootOp("G", new CustomerRow(42, "Ghost"), RowState.Updated),
                new UpdateCustomerOp("G"),
                ParityExpect.Outcome("crud-update-missing-is-false", "updated=False"),
                ParityExpect.StateRows("crud-update-missing-writes-nothing", 9),
                new DeleteOp("customers", 3),
                ParityExpect.Outcome("crud-delete-succeeds", "deleted=True"),
                new DeleteOp("customers", 3),
                ParityExpect.Outcome("crud-delete-again-is-false", "deleted=False"),
                ParityExpect.StateKeys("crud-delete-rows", KeysWithCustomers(1, 2)),
            ]
        );

        // 直接の挿入は RowState を触らない：挿入した同じインスタンスは Added のままで、続けてグラフ保存すると主キーの重複で
        // 断られる（実 DB と同じ帰結）。グラフ保存は成功の後に Unchanged へ確定する
        yield return new(
            "direct-crud-leaves-row-state",
            Basic,
            [
                new InsertToSlotOp(S, new CustomerRow(3, "Carol")),
                ParityExpect.RootState("direct-insert-keeps-added", S, RowState.Added),
                new SaveOp(S),
                ParityExpect.Throws("direct-insert-then-save-is-duplicate", "store-rejected"),
                ParityExpect.StateRows("direct-insert-then-save-writes-nothing", 9),
                new NewRootOp("V", new CustomerRow(4, "Dan"), RowState.Added),
                new SaveOp("V"),
                ParityExpect.Outcome("graph-save-new-row", "rows=1"),
                ParityExpect.RootState("graph-save-settles-unchanged", "V", RowState.Unchanged),
            ]
        );

        // BulkInsertAsync の共通契約：戻り値は挿入した行数・null の要素は飛ばす・空は 0・取り消し済みのトークンは何も書かない・
        // RowState を触らない。数十件もまとめて入る
        yield return new(
            "bulk-insert-contract",
            Basic,
            [
                new BulkInsertOp(
                    [
                        new CustomerRow(3, "Carol"),
                        new CustomerRow(4, "Dan"),
                        new CustomerRow(5, "Eve"),
                    ],
                    KeepAs: "k"
                ),
                ParityExpect.Outcome("bulk-insert-counts-rows", "inserted=3"),
                ParityExpect.RootState("bulk-insert-keeps-added", "k0", RowState.Added),
                ParityExpect.StateKeys("bulk-insert-rows", KeysWithCustomers(1, 2, 3, 4, 5)),
                new BulkInsertOp([new CustomerRow(6, "Fay"), null, new CustomerRow(7, "Gil")]),
                ParityExpect.Outcome("bulk-insert-skips-null", "inserted=2"),
                ParityExpect.StateKeys(
                    "bulk-insert-skips-null-rows",
                    KeysWithCustomers(1, 2, 3, 4, 5, 6, 7)
                ),
                new BulkInsertOp([]),
                ParityExpect.Outcome("bulk-insert-empty-is-zero", "inserted=0"),
                new CanceledOp(new BulkInsertOp([new CustomerRow(8, "Hal")])),
                ParityExpect.Outcome("bulk-insert-canceled", "throws canceled"),
                ParityExpect.StateRows("bulk-insert-canceled-writes-nothing", 13),
                new BulkInsertOp([
                    .. Enumerable
                        .Range(101, 50)
                        .Select(id => (ParityRow?)new CustomerRow(id, $"Customer{id}")),
                ]),
                ParityExpect.Outcome("bulk-insert-fifty-rows", "inserted=50"),
                ParityExpect.StateRows("bulk-insert-fifty-rows-stored", 63),
            ]
        );

        // BulkInsertAsync の 1 回は 1 つの単位：保存先の行と主キーが重なる・バッチの中の行や保存先の行と一意な値が重なると全体を
        // 断り、1 行も残さない。一意な値は名前（顧客コードは連絡先が参照する代替キーで、EF Core はバッチの中の同じ代替キーを
        // 書く前に追跡の衝突で断る＝例外の型が違う）
        yield return new(
            "bulk-insert-duplicate-rejects-whole-batch",
            Basic,
            [
                new BulkInsertOp([
                    new CustomerRow(3, "Carol"),
                    new CustomerRow(1, "Dup"),
                    new CustomerRow(4, "Dan"),
                ]),
                ParityExpect.Throws("bulk-insert-stored-key-refused", "store-rejected"),
                ParityExpect.StateRows("bulk-insert-stored-key-writes-nothing", 8),
                new BulkInsertOp([
                    new CustomerRow(3, "Same"),
                    new CustomerRow(4, "Dan"),
                    new CustomerRow(5, "Same"),
                ]),
                ParityExpect.Throws("bulk-insert-batch-unique-refused", "store-rejected"),
                ParityExpect.StateRows("bulk-insert-batch-unique-writes-nothing", 8),
                new BulkInsertOp([new CustomerRow(3, "Carol"), new CustomerRow(4, "Bob")]),
                ParityExpect.Throws("bulk-insert-stored-unique-refused", "store-rejected"),
                ParityExpect.StateRows("bulk-insert-stored-unique-writes-nothing", 8),
            ]
        );

        // 保存は列だけを写すので、保存の後で呼び出し側のエンティティ（列・子のコレクション）を書き換えても保存先は変わらない
        yield return new(
            "save-leaves-store-independent-of-caller",
            Basic,
            [
                new NewRootOp(S, new CustomerRow(3, "Carol"), RowState.Added),
                new AddChildOp(S, "customers/3", new OrderRow(20, 3, 5m), RowState.Added),
                new SaveOp(S),
                ParityExpect.Outcome("snapshot-save", "rows=2"),
                new EditCustomerOp(S, 3, Note: "mutated"),
                ParityExpect.StateRow("snapshot-column-edit-stays", "customers/3", "note=null"),
                new AddChildOp(S, "customers/3", new OrderRow(99, 3, 1m), RowState.Added),
                ParityExpect.StateKeys(
                    "snapshot-child-add-stays",
                    "customers/1",
                    "customers/2",
                    "customers/3",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "orders/20",
                    "order_lines/100",
                    "order_lines/101",
                    "order_lines/102"
                ),
            ]
        );

        // 取得したエンティティを書き換えても保存先は変わらず、読み直した行は元の値
        yield return new(
            "get-by-id-returns-independent-copy",
            Basic,
            [
                new LoadOp(S, "customers", 1),
                new EditCustomerOp(S, 1, Note: "mutated"),
                ParityExpect.StateRow("copy-edit-stays", "customers/1", "note='vip'"),
                new LoadOp("B", "customers", 1),
                ParityExpect.SlotRow(
                    "copy-reload-is-stored-value",
                    "B",
                    "customers/1",
                    "note='vip'"
                ),
            ]
        );

        // グラフ保存の根の主キーの重複（Added）は断り、保存先の行を上書きしない
        yield return new(
            "save-added-duplicate-key-is-refused",
            Basic,
            [
                new NewRootOp(S, new CustomerRow(1, "Zed"), RowState.Added),
                new SaveOp(S),
                ParityExpect.Throws("save-added-duplicate-key", "store-rejected"),
                ParityExpect.StateRow(
                    "save-added-duplicate-keeps-row",
                    "customers/1",
                    "name=Alice"
                ),
            ]
        );

        // 行の無い Updated のグラフ保存は、既定では NotFound の競合。insertWhenUpdateMissing なら INSERT へ切り替える
        yield return new(
            "save-missing-row-conflicts-unless-insert-when-missing",
            Basic,
            [
                new NewRootOp(S, new OrderRow(20, 1, 1m, "g", UnsetBlobs: true), RowState.Updated),
                new SaveOp(S),
                ParityExpect.Throws(
                    "save-missing-row-conflicts",
                    "SaveConflictException reason=NotFound"
                ),
                ParityExpect.StateRows("save-missing-row-writes-nothing", 8),
                new NewRootOp(
                    "T",
                    new OrderRow(21, 1, 2m, "g2", UnsetBlobs: true),
                    RowState.Updated
                ),
                new SaveOp("T", new(InsertWhenUpdateMissing: true)),
                ParityExpect.Outcome("save-missing-row-inserts", "rows=1"),
                ParityExpect.StateRow("save-missing-row-inserted", "orders/21", "memo='g2'"),
            ]
        );

        // 新しい親と子 2 件のグラフ保存（rows=3・Unchanged へ確定）の後、子の更新と削除（rows=2）、親の更新と子の削除と追加
        // （rows=3）を 1 回ずつ保存する
        yield return new(
            "graph-save-added-tree-then-mixed-changes",
            Basic,
            [
                new NewRootOp(S, new CustomerRow(3, "Carol"), RowState.Added),
                new AddChildOp(S, "customers/3", new OrderRow(20, 3, 10m), RowState.Added),
                new AddChildOp(S, "customers/3", new OrderRow(21, 3, 20m), RowState.Added),
                new SaveOp(S),
                ParityExpect.Outcome("tree-save-rows", "rows=3"),
                ParityExpect.RootState("tree-save-root-unchanged", S, RowState.Unchanged),
                ParityExpect.SlotRowState(
                    "tree-save-child-unchanged",
                    S,
                    "orders/21",
                    RowState.Unchanged
                ),
                new LoadWithParentOp("L", "customers", 3, ParityParentInclude.CustomerOrders),
                new EditMemoOp("L", 20, "x"),
                new MarkRemovedOp("L", "orders/21"),
                new SaveOp("L"),
                ParityExpect.Outcome("tree-child-update-and-delete-rows", "rows=2"),
                ParityExpect.StateRow("tree-child-updated", "orders/20", "memo='x'"),
                new LoadWithParentOp("M", "customers", 3, ParityParentInclude.CustomerOrders),
                new EditCustomerOp("M", 3, Note: "upd"),
                new MarkRemovedOp("M", "orders/20"),
                new AddChildOp("M", "customers/3", new OrderRow(22, 3, 30m), RowState.Added),
                new SaveOp("M"),
                ParityExpect.Outcome("tree-mixed-rows", "rows=3"),
                ParityExpect.StateRow("tree-mixed-parent-updated", "customers/3", "note='upd'"),
                ParityExpect.StateKeys(
                    "tree-mixed-rows-left",
                    "customers/1",
                    "customers/2",
                    "customers/3",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "orders/22",
                    "order_lines/100",
                    "order_lines/101",
                    "order_lines/102"
                ),
            ]
        );

        // Include：親から子のコレクション（子の無い親は空）・子から親への参照・ThenInclude で 2 段の親（親からその子のコレクションへ
        // 戻る ThenInclude は、EF Core の追跡しないクエリが循環として断るので、ここに置かない）
        yield return new(
            "include-children-and-parents",
            [.. Basic, new SeedOp(new CustomerRow(3, "Carol"))],
            [
                new LoadWithParentOp(S, "customers", 1, ParityParentInclude.CustomerOrders),
                ParityExpect.SlotKeys(
                    "include-children",
                    S,
                    "customers/1",
                    "orders/10",
                    "orders/11"
                ),
                new LoadWithParentOp("B", "customers", 3, ParityParentInclude.CustomerOrders),
                ParityExpect.SlotKeys("include-no-children-is-empty", "B", "customers/3"),
                new LoadWithParentOp("C", "orders", 10, ParityParentInclude.OrderCustomer),
                ParityExpect.Outcome(
                    "include-parent",
                    "loaded parent=[customers/1 name=Alice note='vip' code='K1' referrer=null]"
                ),
                new LoadWithParentOp(
                    "E",
                    "order_lines",
                    102,
                    ParityParentInclude.LineOrderCustomer
                ),
                ParityExpect.Outcome(
                    "include-grandparent",
                    "loaded parent=[orders/12 customer=2 amount=30 memo=null at=null paid=False] "
                        + "grandparent=[customers/2 name=Bob note=null code='K2' referrer=null]"
                ),
            ]
        );

        // NULL 許容の列の != は NULL の行も含める（C# の意味論）。== は含めない。文字列は序数順に並び、NULL は昇順で先頭・降順で末尾。
        // 条件と並びと窓（Skip / Take）・件数・存在・先頭の 1 件
        yield return new(
            "query-null-comparison-and-ordering",
            QuerySeeds,
            [
                new QueryOrdersOp(ParityOrderFilter.MemoNotEquals, "x"),
                ParityExpect.Outcome("query-not-equal-includes-null", "ids=[20,21,22]"),
                new QueryOrdersOp(ParityOrderFilter.MemoEquals, "x"),
                ParityExpect.Outcome("query-equal-excludes-null", "ids=[23]"),
                new QueryOrdersOp(ParityOrderFilter.MemoIsNotNull, Sort: ParityOrderSort.MemoAsc),
                ParityExpect.Outcome("query-not-null-ordered", "ids=[21,20,23]"),
                new QueryOrdersOp(Sort: ParityOrderSort.MemoDesc, Skip: 1, Take: 1),
                ParityExpect.Outcome("query-desc-window", "ids=[20]"),
                new QueryOrdersOp(Sort: ParityOrderSort.MemoAsc),
                ParityExpect.Outcome("query-ordinal-null-first", "ids=[22,21,20,23]"),
                new QueryOrdersOp(Sort: ParityOrderSort.MemoDesc),
                ParityExpect.Outcome("query-ordinal-null-last", "ids=[23,20,21,22]"),
                new QueryOrdersOp(
                    ParityOrderFilter.AmountAtLeast,
                    Amount: 200m,
                    Terminal: ParityQueryTerminal.Count
                ),
                ParityExpect.Outcome("query-count", "count=2"),
                new QueryOrdersOp(
                    ParityOrderFilter.MemoEquals,
                    "Banana",
                    Terminal: ParityQueryTerminal.Any
                ),
                ParityExpect.Outcome("query-any-true", "any=True"),
                new QueryOrdersOp(
                    ParityOrderFilter.MemoEquals,
                    "Zed",
                    Terminal: ParityQueryTerminal.Any
                ),
                ParityExpect.Outcome("query-any-false", "any=False"),
                new QueryOrdersOp(
                    ParityOrderFilter.MemoEquals,
                    "Banana",
                    Terminal: ParityQueryTerminal.First
                ),
                ParityExpect.Outcome("query-first", "first=21"),
                new QueryOrdersOp(
                    ParityOrderFilter.MemoEquals,
                    "Zed",
                    Terminal: ParityQueryTerminal.First
                ),
                ParityExpect.Outcome("query-first-none", "first=none"),
            ]
        );

        // Where の式木（値オブジェクトの ==・.Value を開いた比較と && の連結・Contains の LIKE〔% と _ のエスケープ〕を定数と変数で・
        // IN・.Value の等値）、null のパターンを Where の時点で断ること、並び・窓（Skip / Take の不正な値を断る）・Include と窓の併用
        yield return new(
            "query-where-expression-and-paging",
            WhereSeeds,
            [
                new QueryOrdersOp(ParityOrderFilter.MemoEquals, "Bob", Literal: true),
                ParityExpect.Outcome("where-vo-equals", "ids=[21]"),
                new QueryOrdersOp(
                    ParityOrderFilter.AmountAtLeastAndMemoContains,
                    "Alic",
                    Literal: true,
                    Amount: 300m
                ),
                ParityExpect.Outcome("where-and", "ids=[22]"),
                new QueryOrdersOp(ParityOrderFilter.MemoContains, "Ali", Literal: true),
                ParityExpect.Outcome("where-contains", "ids=[20,22]"),
                new QueryOrdersOp(ParityOrderFilter.MemoContains, "0%", Literal: true),
                ParityExpect.Outcome("where-contains-percent-literal", "ids=[23]"),
                new QueryOrdersOp(ParityOrderFilter.MemoContains, "A_B", Literal: true),
                ParityExpect.Outcome("where-contains-underscore-literal", "ids=[24]"),
                new QueryOrdersOp(ParityOrderFilter.MemoContains, "0%"),
                ParityExpect.Outcome("where-contains-percent-variable", "ids=[23]"),
                new QueryOrdersOp(ParityOrderFilter.IdIn, Ids: [21, 25]),
                ParityExpect.Outcome("where-in", "ids=[21,25]"),
                new QueryOrdersOp(ParityOrderFilter.MemoValueEquals, "Bob", Literal: true),
                ParityExpect.Outcome("where-value-equals", "ids=[21]"),
                new QueryOrdersOp(ParityOrderFilter.MemoContains, null, Literal: true),
                ParityExpect.Outcome(
                    "where-contains-null",
                    "throws ArgumentNullException param=value"
                ),
                new QueryOrdersOp(ParityOrderFilter.MemoStartsWith, null, Literal: true),
                ParityExpect.Outcome(
                    "where-starts-with-null",
                    "throws ArgumentNullException param=value"
                ),
                new QueryOrdersOp(ParityOrderFilter.MemoEndsWith, null, Literal: true),
                ParityExpect.Outcome(
                    "where-ends-with-null",
                    "throws ArgumentNullException param=value"
                ),
                new QueryOrdersOp(ParityOrderFilter.MemoContains, "A", Literal: true),
                ParityExpect.Outcome("where-contains-non-null-passes", "ids=[20,22,24,25]"),
                new QueryOrdersOp(),
                ParityExpect.Outcome("order-id-asc", "ids=[20,21,22,23,24,25]"),
                new QueryOrdersOp(Sort: ParityOrderSort.IdDesc),
                ParityExpect.Outcome("order-id-desc", "ids=[25,24,23,22,21,20]"),
                new QueryOrdersOp(Sort: ParityOrderSort.CustomerThenIdDesc),
                ParityExpect.Outcome("order-then-by", "ids=[25,24,23,22,20,21]"),
                new QueryOrdersOp(Skip: 2, Take: 3),
                ParityExpect.Outcome("window-skip-take", "ids=[22,23,24]"),
                new QueryOrdersOp(Take: 2),
                ParityExpect.Outcome("window-take", "ids=[20,21]"),
                new QueryOrdersOp(Skip: 4),
                ParityExpect.Outcome("window-skip", "ids=[24,25]"),
                new QueryOrdersOp(Skip: 0),
                ParityExpect.Outcome("window-skip-zero", "ids=[20,21,22,23,24,25]"),
                new QueryOrdersOp(Take: 0),
                ParityExpect.Outcome(
                    "window-take-zero",
                    "throws ArgumentOutOfRangeException param=count"
                ),
                new QueryOrdersOp(Take: -1),
                ParityExpect.Outcome(
                    "window-take-negative",
                    "throws ArgumentOutOfRangeException param=count"
                ),
                new QueryOrdersOp(Skip: -1),
                ParityExpect.Outcome(
                    "window-skip-negative",
                    "throws ArgumentOutOfRangeException param=count"
                ),
                new QueryOrdersOp(
                    ParityOrderFilter.MemoContains,
                    "Alic",
                    Literal: true,
                    Take: 1,
                    Terminal: ParityQueryTerminal.IdsWithLines
                ),
                ParityExpect.Outcome("include-with-where-and-take", "ids=[20:200+201]"),
            ]
        );

        // 射影：列だけ（刈り込める経路）と、Include したナビゲーションを読む射影（刈り込めない経路）
        yield return new(
            "projection-columns-and-navigation",
            Basic,
            [
                new ProjectOrdersOp(ParityProjection.Columns, Customer: 1),
                ParityExpect.Outcome("projection-columns", "rows=[10:100,11:50]"),
                new ProjectOrdersOp(ParityProjection.CustomerName, Customer: 1),
                ParityExpect.Outcome("projection-navigation", "rows=[10:Alice,11:Alice]"),
            ]
        );

        // 循環するカスケードの一括削除は、条件に合う行が 1 つも無くても断る（行が 1 つあるが条件に合わない形）
        yield return new(
            "execute-delete-cyclic-cascade-matches-nothing",
            [new SeedOp(new CustomerRow(0, "Zed"))],
            [
                new ExecuteDeleteCustomersOp(CascadeDelete: true),
                ParityExpect.Throws(
                    "cyclic-cascade-matching-nothing-is-refused",
                    "NotSupportedException"
                ),
                ParityExpect.StateKeys("cyclic-cascade-matching-nothing-keeps-rows", "customers/0"),
            ]
        );

        // 一意な値：ほかの行と同じ値への UpdateAsync は断る。自分自身と同じ値のままの UpdateAsync は通る（主キーで自分を除く）
        yield return new(
            "unique-update-collides-or-keeps-own-value",
            Basic,
            [
                new LoadOp(S, "customers", 2),
                new EditCustomerOp(S, 2, Code: "K1"),
                new UpdateCustomerOp(S),
                ParityExpect.Throws("unique-update-collides", "store-rejected"),
                ParityExpect.StateRow(
                    "unique-update-collides-writes-nothing",
                    "customers/2",
                    "code='K2'"
                ),
                new LoadOp("B", "customers", 1),
                new EditCustomerOp("B", 1, Code: "K1"),
                new UpdateCustomerOp("B"),
                ParityExpect.Outcome("unique-update-own-value", "updated=True"),
                ParityExpect.StateRow("unique-update-own-value-kept", "customers/1", "code='K1'"),
            ]
        );

        // 一意な値：グラフの中の 2 行が同じ値・保存先の行と同じ値の新しい行は、保存ごと断り親も書かない
        yield return new(
            "unique-graph-save-duplicate-is-refused",
            Basic,
            [
                new NewRootOp(S, new CustomerRow(10, "Ten"), RowState.Added),
                new AddChildCustomerOp(S, new CustomerRow(11, "Dup", Referrer: 10)),
                new AddChildCustomerOp(S, new CustomerRow(12, "Dup", Referrer: 10)),
                new SaveOp(S),
                ParityExpect.Throws("unique-within-graph-refused", "store-rejected"),
                ParityExpect.StateRows("unique-within-graph-writes-nothing", 8),
                new NewRootOp("B", new CustomerRow(13, "Thirteen"), RowState.Added),
                new AddChildCustomerOp("B", new CustomerRow(14, "Bob", Referrer: 13)),
                new SaveOp("B"),
                ParityExpect.Throws("unique-with-stored-row-refused", "store-rejected"),
                ParityExpect.StateRows("unique-with-stored-row-writes-nothing", 8),
            ]
        );
    }

    /// <summary>
    /// 条件・並びのシナリオの種：顧客 1・2、注文 20（顧客 1・300・apple）・21（顧客 1・100・Banana）・22（顧客 2・200・メモなし）・
    /// 23（顧客 1・50・x）
    /// </summary>
    private static IReadOnlyList<ParityOp> QuerySeeds =>
        [
            new SeedOp(new CustomerRow(1, "Alice")),
            new SeedOp(new CustomerRow(2, "Bob")),
            new SeedOp(new OrderRow(20, 1, 300m, "apple")),
            new SeedOp(new OrderRow(21, 1, 100m, "Banana")),
            new SeedOp(new OrderRow(22, 2, 200m)),
            new SeedOp(new OrderRow(23, 1, 50m, "x")),
        ];

    /// <summary>
    /// Where の式木のシナリオの種：顧客 1・2、メモがどれも NULL でない注文 20〜25（顧客 2 の 21 だけ）と、注文 20 の明細 200・201
    /// </summary>
    private static IReadOnlyList<ParityOp> WhereSeeds =>
        [
            new SeedOp(new CustomerRow(1, "Alice")),
            new SeedOp(new CustomerRow(2, "Bob")),
            new SeedOp(new OrderRow(20, 1, 100m, "Alice")),
            new SeedOp(new OrderRow(21, 2, 200m, "Bob")),
            new SeedOp(new OrderRow(22, 1, 300m, "Alicia")),
            new SeedOp(new OrderRow(23, 1, 1m, "50%OFF")),
            new SeedOp(new OrderRow(24, 1, 1m, "A_B")),
            new SeedOp(new OrderRow(25, 1, 1m, "AxB")),
            new SeedOp(new LineRow(200, 20, "pen", 1)),
            new SeedOp(new LineRow(201, 20, "ink", 2)),
        ];
}
