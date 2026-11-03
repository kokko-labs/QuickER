using System.Collections.Generic;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモート面の CRUD・グラフ保存・null の引数と要素・保存の応答の書き戻し（サーバーの Save フックが書いた値・位置での照合・
/// cascadeSave: false・除外 blob を運ばないこと）・除外 blob を未取得のまま送った更新のシナリオ。どれもリモート面の手だけで組むので、
/// 5 つの実装先（直結の 3 つとリモートの 2 つ）で流れる。
/// </summary>
/// <remarks>
/// 生成コードのランタイムテスト（RemoteService の CRUD と保存・RemoteSaveRowOrder・RemoteSaveWriteBack・RemoteEfCoreUnboundedBinary）から
/// 移した。元の図（RemoteServiceFixture の customers → orders・自己参照の nodes、BinaryFixture の documents → document_notes）は、
/// 顧客（自己参照の紹介者を持つ）→ 注文（除外 blob を持つ）→ 明細へ写した。元のサーバーのフックの値（Label への "!"・IsPublished・
/// Title への "#{Id}"・Note への "!"・Payload）は、記録するフックの書き換え（顧客の note・注文の memo・明細の discount・注文の除外
/// blob）へ写した。HTTP 固有（400・413・500 のフック・HttpClient の寿命）と名前付きクエリは元のテストに残した。
/// </remarks>
public static partial class BackendParityScenarios
{
    private static IEnumerable<ParityScenario> RemoteRuntimeScenarios()
    {
        // リモート面の CRUD：挿入・GetById の値・更新・更新を読み直した値・削除・削除の後の GetAll と GetById。EF Core は GetAll で
        // 除外 blob も読む（docs に書いてある違い）ので、基準と比べず手ごとに値を表明する
        yield return new(
            "remote-crud-round-trip",
            Basic,
            [
                new InsertOp(new OrderRow(13, 1, 100m, "apple pie")),
                ParityExpect.Succeeds("round-trip-insert"),
                new LoadOp(S, "orders", 13),
                ParityExpect.Outcome("round-trip-get-by-id", "loaded"),
                ParityExpect.SlotRow(
                    "round-trip-get-by-id-values",
                    S,
                    "orders/13",
                    "customer=1",
                    "amount=100",
                    "memo='apple pie'"
                ),
                new EditMemoOp(S, 13, "apple tart"),
                new UpdateOrderOp(S),
                ParityExpect.Outcome("round-trip-update", "updated=True"),
                new LoadOp("R", "orders", 13),
                ParityExpect.SlotRow(
                    "round-trip-reread-after-update",
                    "R",
                    "orders/13",
                    "memo='apple tart'"
                ),
                new DeleteOp("orders", 13),
                ParityExpect.Outcome("round-trip-delete", "deleted=True"),
                new GetAllOrdersOp(),
                ParityExpect.OutcomeByKind(
                    "round-trip-get-all-after-delete",
                    $"orders=[10:{UnsetBlobs},11:{UnsetBlobs},12:{UnsetBlobs}]",
                    efCore: $"orders=[10:{Order10Blobs},11:payload=null thumb=EE,12:payload=null thumb=0C]"
                ),
                new LoadOp("Z", "orders", 13),
                ParityExpect.Outcome("round-trip-get-by-id-after-delete", "null"),
            ],
            CompareWithBaseline: false
        );

        // グラフ保存：新しい行の単数形の保存（Unchanged に確定）・確定した同じインスタンスを編集した保存し直し・複数形の保存
        // （Added 2 件、続けて Updated と Added の混在）。どれも成功の後に各根の RowState が Unchanged に確定する
        yield return new(
            "remote-save-round-trip",
            Basic,
            [
                new NewRootOp("A", new CustomerRow(3, "Carol"), RowState.Added),
                new SaveOp("A"),
                ParityExpect.Outcome("save-round-trip-insert", "rows=1"),
                ParityExpect.RootState("save-round-trip-insert-settles", "A", RowState.Unchanged),
                new EditCustomerOp("A", 3, Note: "tart"),
                new SaveOp("A"),
                ParityExpect.Outcome("save-round-trip-update", "rows=1"),
                ParityExpect.RootState("save-round-trip-update-settles", "A", RowState.Unchanged),
                ParityExpect.StateRow(
                    "save-round-trip-update-stored",
                    "customers/3",
                    "note='tart'"
                ),
                new NewRootOp("M1", new CustomerRow(4, "Dan"), RowState.Added),
                new NewRootOp("M2", new CustomerRow(5, "Eve"), RowState.Added),
                new SaveManyCustomersOp(["M1", "M2"]),
                ParityExpect.Outcome("save-many-round-trip-insert", "rows=2"),
                ParityExpect.RootState("save-many-round-trip-first", "M1", RowState.Unchanged),
                ParityExpect.RootState("save-many-round-trip-second", "M2", RowState.Unchanged),
                new EditCustomerOp("M1", 4, Note: "upd"),
                new NewRootOp("M3", new CustomerRow(6, "Fay"), RowState.Added),
                new SaveManyCustomersOp(["M1", "M3"]),
                ParityExpect.Outcome("save-many-round-trip-mixed", "rows=2"),
                ParityExpect.RootState("save-many-mixed-updated", "M1", RowState.Unchanged),
                ParityExpect.RootState("save-many-mixed-added", "M3", RowState.Unchanged),
                ParityExpect.StateRow("save-many-mixed-update-stored", "customers/4", "note='upd'"),
                ParityExpect.StateKeys("save-round-trip-rows", KeysWithCustomers(1, 2, 3, 4, 5, 6)),
            ]
        );

        // null の引数：直結の実装は入口で ArgumentNullException を投げ、リモートのクライアントも HTTP を投げる前に同じ型・同じ引数名で
        // 断る（実装を差し替えても catch が変わらない契約）。何も書かない
        yield return new(
            "null-argument-is-refused",
            Basic,
            [
                new NullArgumentOp(ParityNullCall.Insert),
                ParityExpect.Outcome(
                    "null-argument-insert",
                    "throws ArgumentNullException param=entity"
                ),
                new NullArgumentOp(ParityNullCall.Update),
                ParityExpect.Outcome(
                    "null-argument-update",
                    "throws ArgumentNullException param=entity"
                ),
                new NullArgumentOp(ParityNullCall.Save),
                ParityExpect.Outcome(
                    "null-argument-save",
                    "throws ArgumentNullException param=entity"
                ),
                new NullArgumentOp(ParityNullCall.SaveMany),
                ParityExpect.Outcome(
                    "null-argument-save-many",
                    "throws ArgumentNullException param=entities"
                ),
                new NullArgumentOp(ParityNullCall.CheckUniqueness),
                ParityExpect.Outcome(
                    "null-argument-check-uniqueness",
                    "throws ArgumentNullException param=entity"
                ),
                ParityExpect.StateRows("null-argument-changes-nothing", 8),
            ]
        );

        // 保存の応答の書き戻し（顧客の挿入では Before フックが note を書く）：(1) 単数形の保存でフックの値が呼び出し側へ戻り、確定した
        // 同じインスタンスを編集して保存し直しても消えない (2) null の要素を含む複数形の保存は null を飛ばし、値はそれぞれの根へ戻る
        // (3) 自己参照の親と子を別々の根として 1 回で保存しても、値はそれぞれの行へ戻り、子は親のコレクションへ入らない
        // （値は行と位置で突き合わせ、並びはサーバーが保存の前に受け取ったグラフで決める）
        yield return new(
            "remote-save-writes-back-hook-values",
            Basic,
            [
                new NewRootOp(S, new CustomerRow(3, "Carol"), RowState.Added),
                new SaveOp(S),
                ParityExpect.SlotRow(
                    "hook-stamp-written-back",
                    S,
                    "customers/3",
                    "note='hook-Insert'"
                ),
                new EditCustomerOp(S, 3, Referrer: 1),
                new SaveOp(S),
                ParityExpect.Outcome("hook-stamp-next-save", "rows=1"),
                ParityExpect.StateRow(
                    "hook-stamp-survives-next-save",
                    "customers/3",
                    "note='hook-Insert'",
                    "referrer=1"
                ),
                new NewRootOp("A", new CustomerRow(4, "Dan"), RowState.Added),
                new NewRootOp("B", new CustomerRow(5, "Eve"), RowState.Added),
                new SaveManyCustomersWithNullsOp([null, "A", null, "B"]),
                ParityExpect.Outcome("save-many-skips-null-elements", "rows=2"),
                ParityExpect.RootState("save-many-null-first-settles", "A", RowState.Unchanged),
                ParityExpect.RootState("save-many-null-second-settles", "B", RowState.Unchanged),
                ParityExpect.SlotRow(
                    "save-many-null-first-written-back",
                    "A",
                    "customers/4",
                    "name=Dan",
                    "note='hook-Insert'"
                ),
                ParityExpect.SlotRow(
                    "save-many-null-second-written-back",
                    "B",
                    "customers/5",
                    "name=Eve",
                    "note='hook-Insert'"
                ),
                ParityExpect.StateKeys("save-many-null-rows", KeysWithCustomers(1, 2, 3, 4, 5)),
                new NewRootOp("P", new CustomerRow(20, "Pat"), RowState.Added),
                new NewRootOp("C", new CustomerRow(21, "Quinn", Referrer: 20), RowState.Added),
                new SaveManyCustomersOp(["P", "C"]),
                ParityExpect.Outcome("self-referencing-roots-saved", "rows=2"),
                ParityExpect.SlotKeys("self-referencing-roots-stay-apart", "P", "customers/20"),
                ParityExpect.SlotRow(
                    "self-referencing-parent-written-back",
                    "P",
                    "customers/20",
                    "note='hook-Insert'"
                ),
                ParityExpect.SlotRow(
                    "self-referencing-child-written-back",
                    "C",
                    "customers/21",
                    "note='hook-Insert'",
                    "referrer=20"
                ),
                ParityExpect.RootState("self-referencing-parent-settles", "P", RowState.Unchanged),
                ParityExpect.RootState("self-referencing-child-settles", "C", RowState.Unchanged),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Insert:customers", ParityHookBehavior.EditThenContinue),
                ]
            )
        );

        // 保存の応答の値を位置で突き合わせる：(1) 同じ親の下に、削除する子（100）・削除を飛ばされる子（103）・変更のない子（101）・
        // 飛ばされる新しい子（110）・入る新しい子（111＝Before が discount を書く）が混ざっても、値は正しい子へ戻り、削除した子だけが
        // コレクションから外れる (2) cascadeSave: false の保存は根だけに値を戻し、新しい子は Added・値のまま (3) サーバーのフックが
        // 除外 blob を書いても、応答は除外列を運ばないので、リモートのクライアントの値は送ったまま（直結は同じインスタンスなのでフックの値）
        yield return new(
            "remote-write-back-by-position",
            [.. Basic, new SeedOp(new LineRow(103, 10, "pin", 1))],
            [
                new LoadOp(S, "orders", 10),
                new AttachLineOp(S, 100),
                new AttachLineOp(S, 101),
                new AttachLineOp(S, 103),
                new MarkRemovedOp(S, "order_lines/100"),
                new MarkRemovedOp(S, "order_lines/103"),
                new AddLineOp(S, new LineRow(110, 10, "skip", 1)),
                new AddLineOp(S, new LineRow(111, 10, "added", 1)),
                new SaveOp(S),
                ParityExpect.Outcome("mixed-children-rows", "rows=2"),
                ParityExpect.ChildrenOf(
                    "mixed-children-deleted-child-leaves",
                    S,
                    101,
                    103,
                    110,
                    111
                ),
                ParityExpect.SlotRowState(
                    "mixed-children-unchanged-child",
                    S,
                    "order_lines/101",
                    RowState.Unchanged
                ),
                ParityExpect.SlotRowState(
                    "mixed-children-delete-skipped-stays-removed",
                    S,
                    "order_lines/103",
                    RowState.Removed
                ),
                ParityExpect.SlotRowState(
                    "mixed-children-insert-skipped-stays-added",
                    S,
                    "order_lines/110",
                    RowState.Added
                ),
                ParityExpect.SlotRowState(
                    "mixed-children-inserted-settles",
                    S,
                    "order_lines/111",
                    RowState.Unchanged
                ),
                ParityExpect.SlotRow(
                    "mixed-children-unchanged-keeps-values",
                    S,
                    "order_lines/101",
                    "item=ink",
                    "discount=null"
                ),
                ParityExpect.SlotRow(
                    "mixed-children-skipped-keeps-values",
                    S,
                    "order_lines/110",
                    "item=skip",
                    "discount=null"
                ),
                ParityExpect.SlotRow(
                    "mixed-children-inserted-written-back",
                    S,
                    "order_lines/111",
                    "item=added",
                    "discount=0.5"
                ),
                ParityExpect.StateKeys(
                    "mixed-children-rows-left",
                    "customers/1",
                    "customers/2",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "order_lines/101",
                    "order_lines/102",
                    "order_lines/103",
                    "order_lines/111"
                ),
                new NewRootOp("N", new OrderRow(20, 1, 1m), RowState.Added),
                new AddLineOp("N", new LineRow(21, 20, "x", 1)),
                new SaveOp("N", new(CascadeSave: false)),
                ParityExpect.Outcome("without-cascade-rows", "rows=1"),
                ParityExpect.SlotRow(
                    "without-cascade-root-written-back",
                    "N",
                    "orders/20",
                    "memo='hook-Insert'"
                ),
                ParityExpect.SlotRowState(
                    "without-cascade-child-stays-added",
                    "N",
                    "order_lines/21",
                    RowState.Added
                ),
                ParityExpect.SlotRow(
                    "without-cascade-child-keeps-values",
                    "N",
                    "order_lines/21",
                    "discount=null"
                ),
                new NewRootOp("X", new OrderRow(30, 1, 3m, Thumb: [0x01]), RowState.Added),
                new SaveOp("X"),
                ParityExpect.Outcome("excluded-blobs-save", "rows=1"),
                ParityExpect.Either(
                    "excluded-blobs-not-carried-back",
                    ParityBackendKinds.IsRemote,
                    ParityExpect.SlotBlobs("as-sent", "X", 30, "payload=null thumb=01"),
                    ParityExpect.SlotBlobs("hook-edited-instance", "X", 30, HookBlobs)
                ),
                ParityExpect.StateRow("excluded-blobs-stored-by-hook", "orders/30", HookBlobs),
                ParityExpect.StateKeys(
                    "by-position-rows-left",
                    "customers/1",
                    "customers/2",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "orders/20",
                    "orders/30",
                    "order_lines/101",
                    "order_lines/102",
                    "order_lines/103",
                    "order_lines/111"
                ),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Delete:order_lines/103", ParityHookBehavior.Skip),
                    new ParityHookRule("Insert:order_lines/110", ParityHookBehavior.Skip),
                    new ParityHookRule(
                        "Insert:order_lines/111",
                        ParityHookBehavior.EditThenContinue
                    ),
                    new ParityHookRule("Insert:orders/20", ParityHookBehavior.EditThenContinue),
                    new ParityHookRule(
                        "Insert:order_lines/21",
                        ParityHookBehavior.EditThenContinue
                    ),
                    new ParityHookRule(
                        "Insert:orders/30",
                        ParityHookBehavior.EditBlobsThenContinue
                    ),
                ]
            )
        );

        // 除外 blob を未取得のまま組み立てた（読んでいない）エンティティの UpdateAsync（Optimistic と ForceOverwrite）・単数形と
        // 複数形のグラフ保存の更新は、保存先の blob を消さない（リモートは EF Core のサーバーでも書かない）。直結の EF Core は全列を
        // 書く（docs に書いてある違い）ので、基準と比べず実装先ごとの保存先の値で書く
        yield return new(
            "blob-unset-update-from-new-entity-keeps-blobs",
            [.. Basic, new SeedOp(new OrderRow(13, 1, 5m, Payload: [0x0D], Thumb: [0x0E]))],
            [
                new NewRootOp(
                    S,
                    new OrderRow(10, 1, 100m, "unset", UnsetBlobs: true),
                    RowState.Updated
                ),
                new UpdateOrderOp(S),
                ParityExpect.Outcome("unset-update-optimistic", "updated=True"),
                UnsetUpdateStored("unset-update-optimistic-blobs", 10, "unset", Order10Blobs),
                new NewRootOp(
                    "F",
                    new OrderRow(11, 1, 50m, "forced", UnsetBlobs: true),
                    RowState.Updated
                ),
                new UpdateOrderOp("F", ConcurrencyMode.ForceOverwrite),
                ParityExpect.Outcome("unset-update-force-overwrite", "updated=True"),
                UnsetUpdateStored(
                    "unset-update-force-overwrite-blobs",
                    11,
                    "forced",
                    "payload=null thumb=EE"
                ),
                new NewRootOp(
                    "G",
                    new OrderRow(12, 2, 30m, "saved", UnsetBlobs: true),
                    RowState.Updated
                ),
                new SaveOp("G"),
                ParityExpect.Outcome("unset-save", "rows=1"),
                UnsetUpdateStored("unset-save-blobs", 12, "saved", "payload=null thumb=0C"),
                new NewRootOp(
                    "H",
                    new OrderRow(13, 1, 5m, "saved-many", UnsetBlobs: true),
                    RowState.Updated
                ),
                new SaveManyOrdersOp(["H"]),
                ParityExpect.Outcome("unset-save-many", "rows=1"),
                UnsetUpdateStored("unset-save-many-blobs", 13, "saved-many", "payload=0D thumb=0E"),
            ],
            CompareWithBaseline: false
        );
    }

    /// <summary>
    /// 除外 blob を未取得のまま送った更新の後の保存先の注文：メモは送った値、blob は <paramref name="keptBlobs"/> のまま
    /// （直結の EF Core は全列を書くので blob が未取得の値＝payload は null・thumb は空になる）
    /// </summary>
    private static ExpectOp UnsetUpdateStored(
        string label,
        int orderId,
        string memo,
        string keptBlobs
    ) =>
        ParityExpect.Either(
            label,
            kind => kind == ParityBackendKind.EfCore,
            ParityExpect.StateRow(
                "written",
                $"orders/{orderId}",
                $"memo='{memo}'",
                "payload=null thumb=empty"
            ),
            ParityExpect.StateRow("kept", $"orders/{orderId}", $"memo='{memo}'", keptBlobs)
        );
}
