using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>差分の段の期待の行（外部キーの組み方・名前・参照アクション・作り直しへの畳み込み）</summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>外部キーの差分の 3 種類（追加・削除・作り直し）</summary>
    private static string[] ForeignKeyDiffKinds =>
        ["AddForeignKey *", "DropForeignKey *", "AlterForeignKey *"];

    /// <summary>名前を保存しない方言の外部キー（DB 側の ON DELETE の並び, 図の ON DELETE の並び, 作り直し・追加・削除の数）</summary>
    private static (
        string Shape,
        ForeignKeyReferentialAction[] Live,
        ForeignKeyReferentialAction[] Target,
        int Alters,
        int Adds,
        int Drops
    )[] UnpersistedNameForeignKeyChanges =>
        [
            (
                "one-changed",
                [ForeignKeyReferentialAction.NoAction],
                [ForeignKeyReferentialAction.Cascade],
                1,
                0,
                0
            ),
            (
                "two-one-changed",
                [ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.Cascade],
                [ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.Cascade],
                1,
                0,
                0
            ),
            (
                "two-both-changed",
                [ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.NoAction],
                [ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.Cascade],
                2,
                0,
                0
            ),
            (
                "two-same",
                [ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.Cascade],
                [ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.Cascade],
                0,
                0,
                0
            ),
            (
                "one-to-two",
                [ForeignKeyReferentialAction.Cascade],
                [ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.Cascade],
                0,
                1,
                0
            ),
            (
                "two-to-one",
                [ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.Cascade],
                [ForeignKeyReferentialAction.Cascade],
                0,
                0,
                1
            ),
            (
                "two-changed-to-one",
                [ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.NoAction],
                [ForeignKeyReferentialAction.Cascade],
                1,
                0,
                1
            ),
        ];

    /// <summary>表せない参照アクション（図の ON DELETE, ON UPDATE）と行の形</summary>
    private static (
        string Shape,
        ForeignKeyReferentialAction OnDelete,
        ForeignKeyReferentialAction OnUpdate
    )[] UnsupportedReferentialActions =>
        [
            (
                "on-update-cascade",
                ForeignKeyReferentialAction.NoAction,
                ForeignKeyReferentialAction.Cascade
            ),
            (
                "on-delete-set-default",
                ForeignKeyReferentialAction.SetDefault,
                ForeignKeyReferentialAction.NoAction
            ),
            (
                "on-delete-set-default-on-update-set-null",
                ForeignKeyReferentialAction.SetDefault,
                ForeignKeyReferentialAction.SetNull
            ),
        ];

    /// <summary>MySQL の参照アクションの畳み込み（図の ON DELETE, ON UPDATE, 外部キーの差分が出るか）と行の形</summary>
    private static (
        string Shape,
        ForeignKeyReferentialAction OnDelete,
        ForeignKeyReferentialAction OnUpdate,
        bool ExpectsAlter
    )[] MySqlReferentialActions =>
        [
            (
                "on-delete-set-default",
                ForeignKeyReferentialAction.SetDefault,
                ForeignKeyReferentialAction.NoAction,
                false
            ),
            (
                "on-update-set-default",
                ForeignKeyReferentialAction.NoAction,
                ForeignKeyReferentialAction.SetDefault,
                false
            ),
            (
                "on-update-cascade",
                ForeignKeyReferentialAction.NoAction,
                ForeignKeyReferentialAction.Cascade,
                true
            ),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> DiffForeignKeyRows() =>
        [
            Row(
                    "diff-fk-composite-pair-order-is-not-a-difference",
                    "複合外部キーの列ペアの並び: 5 方言とも差分にならない",
                    AllDialects
                )
                .Live(s =>
                    CompositeForeignKey(s, reversed: true, ForeignKeyReferentialAction.NoAction)
                )
                .Target(s =>
                    CompositeForeignKey(s, reversed: false, ForeignKeyReferentialAction.NoAction)
                )
                .Expect(e => e.Diff.Empty()),
            Row(
                    "diff-fk-composite-pair-order-is-not-described-as-reference-change",
                    "複合外部キーの列ペアの並び: 作り直しの説明に並びだけの違いを参照の変更として出さない（5 方言）",
                    AllDialects
                )
                .Live(s =>
                    CompositeForeignKey(s, reversed: true, ForeignKeyReferentialAction.NoAction)
                )
                .TargetWith(w =>
                {
                    // 元のテストは DB 側と図に同じ表のオブジェクトを入れていた
                    w.Target.Entities.AddRange(w.Live.Entities);
                    w.Target.Fk(
                        "P",
                        "C",
                        "FK_C_P",
                        ForeignKeyReferentialAction.Cascade,
                        ("A", "X"),
                        ("B", "Y")
                    );
                })
                .Expect(e =>
                    e.Diff.Exact("AlterForeignKey *")
                        .Diff.Absent(
                            "* desc=\"*"
                                + ProviderStrings.Diff_ForeignKeyChange_Reference.Split('{')[0]
                                + "*"
                        )
                ),
            Row(
                    "diff-fk-new-relationship-is-add-foreign-key",
                    "新しい外部キーは AddForeignKey になる",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Tbl("Customer", ("Id", "int", true));
                    s.Tbl("Order", ("Id", "int", true), ("Customer_Id", "int", false));
                })
                .Target(s =>
                {
                    s.Tbl("Customer", ("Id", "int", true));
                    s.Tbl("Order", ("Id", "int", true), ("Customer_Id", "int", false));
                    s.Fk("Customer", "Order", null, ("Id", "Customer_Id"));
                })
                .Expect(e => e.Diff.Single("AddForeignKey *", "AddForeignKey *.Customer_Id *")),
            Row(
                    "diff-fk-column-change-is-drop-and-add",
                    "同一テーブル間で FK 列が変わると DropForeignKey と AddForeignKey になる",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    CustomerOrderTwoColumns(s);
                    s.Fk("Customer", "Order", "FK_Order_CustomerId1", ("Id", "CustomerId1"));
                })
                .Target(s =>
                {
                    CustomerOrderTwoColumns(s);
                    s.Fk("Customer", "Order", null, ("Id", "CustomerId2"));
                })
                .Expect(e =>
                    e.Diff.Single(
                        "DropForeignKey * fk=FK_Order_CustomerId1 *",
                        "AddForeignKey *.CustomerId2 *"
                    )
                ),
            Row(
                    "diff-fk-referential-action-change-is-single-alter",
                    "参照アクションだけが変わると 1 つの AlterForeignKey になる",
                    DialectNames.SqlServer
                )
                .Live(s =>
                    ParentChildForeignKey(
                        s,
                        "FK_Child_Parent",
                        ForeignKeyReferentialAction.NoAction,
                        ForeignKeyReferentialAction.NoAction
                    )
                )
                .Target(s =>
                    ParentChildForeignKey(
                        s,
                        "FK_Child_Parent",
                        ForeignKeyReferentialAction.Cascade,
                        ForeignKeyReferentialAction.SetNull
                    )
                )
                .Expect(e =>
                    e.Diff.Absent("AddForeignKey *", "DropForeignKey *")
                        .Diff.Single(
                            "AlterForeignKey *",
                            "AlterForeignKey * sel=0 * destr=1 *",
                            "AlterForeignKey * del=Cascade upd=SetNull *replaced-fk=[*",
                            "AlterForeignKey * replaced-fk=[DropForeignKey * fk=FK_Child_Parent * del=NoAction *]*"
                        )
                ),
            Row(
                    "diff-fk-unnamed-matches-default-named-live",
                    "名前の無いリレーションは既定名の live 外部キーと一致し差分を出さない",
                    DialectNames.SqlServer
                )
                .Live(s => ParentChildForeignKey(s, "FK_Child_Parent"))
                .Target(s => ParentChildForeignKey(s, null))
                .Expect(e => e.Diff.Absent(ForeignKeyDiffKinds)),
            .. UnpersistedNameForeignKeyChanges.Select(t =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"diff-fk-without-persisted-names-pairs-by-count-{t.Shape}",
                            "名前を保存しない方言でも同じ子列の外部キーの変更は AlterForeignKey になる（同じシグネチャは本数で組む）",
                            DialectNames.Sqlite
                        )
                        .Live(s => ParentChildForeignKeys(s, "FK_Child_Parent_0", t.Live))
                        .Target(s => ParentChildForeignKeys(s, null, t.Target))
                        .Expect(e =>
                            e.Diff.Count("AlterForeignKey *", t.Alters)
                                .Diff.Count("AddForeignKey *", t.Adds)
                                .Diff.Count("DropForeignKey *", t.Drops)
                        )
            ),
            Row(
                    "diff-fk-different-names-stay-drop-and-add",
                    "名前の違う外部キーは AlterForeignKey に畳まない",
                    DialectNames.SqlServer
                )
                .Live(s => ParentChildForeignKey(s, "fk_old"))
                .Target(s => ParentChildForeignKey(s, "fk_new"))
                .Expect(e =>
                    e.Diff.Single("DropForeignKey *", "AddForeignKey *")
                        .Diff.Absent("AlterForeignKey *")
                ),
            Row(
                    "diff-fk-name-only-difference-suppressed-without-persisted-names",
                    "FK 制約名を永続化しない方言では名前差だけの FK 差分は出ない",
                    DialectNames.Sqlite
                )
                .Live(s => ParentChildForeignKey(s, "FK_Child_Parent_0"))
                .Target(s => ParentChildForeignKey(s, null))
                .Expect(e => e.Diff.Absent("AddForeignKey *", "DropForeignKey *")),
            Row(
                    "diff-fk-name-change-is-drop-and-add-by-default",
                    "既定では FK 制約名の差で Drop+Add が出る",
                    DialectNames.SqlServer
                )
                .Live(s => ParentChildForeignKey(s, "FK_Old"))
                .Target(s => ParentChildForeignKey(s, "FK_New"))
                .Expect(e => e.Diff.Contains("DropForeignKey *", "AddForeignKey *")),
            Row(
                    "diff-fk-names-differing-only-by-lowercasing-are-different",
                    "外部キーの照合: 小文字化だけで同じになる別の名前の外部キーは、名前の変更として削除と追加になる",
                    DialectNames.SqlServer
                )
                .Live(s => ParentChildForeignKey(s, "FK_\u212A"))
                .Target(s => ParentChildForeignKey(s, "FK_k"))
                .Expect(e =>
                    e.Diff.Contains("AddForeignKey * fk=FK_k *", "DropForeignKey * fk=FK_\u212A *")
                ),
            Row(
                    "diff-fk-signature-names-with-separators-are-not-confused",
                    "外部キーの署名: 名前に区切り文字があっても、親子の違う外部キーを取り違えない",
                    C.Neutral
                )
                .Live(s =>
                {
                    SeparatorTables(s);
                    s.Fk("P|Q", "C", "FK_X", ("Id", "Ref"));
                })
                .Target(s =>
                {
                    SeparatorTables(s);
                    s.Fk("P", "Q|C", "FK_X", ("Id", "Ref"));
                })
                .Expect(e =>
                    e.Diff.Unordered("AddForeignKey * child=Q|C *", "DropForeignKey * child=C *")
                ),
            Row(
                    "diff-fk-retargeted-without-persisted-names-is-alter",
                    "外部キー変更: SQLite で参照先だけを変えた外部キーは 1 つの作り直しにまとまる",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Tbl("A", ("Id", "int", true));
                    s.Tbl("B", ("Id", "int", true));
                    s.Tbl("C", ("Id", "int", true), ("Ref", "int", false));
                    s.Fk("A", "C", null, ("Id", "Ref"));
                })
                .TargetWith(w =>
                {
                    // 元のテストは DB 側の表を Id を保って複製し、外部キーだけを B へ向け直していた
                    w.Target.CopyFrom(w.Live, preserveIds: true);
                    w.Target.Relationships.Clear();
                    w.Target.Fk("B", "C", null, ("Id", "Ref"));
                })
                .Expect(e => e.Diff.Exact("AlterForeignKey *")),
            .. UnsupportedReferentialActions.Select(t =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"diff-fk-unsupported-referential-action-is-folded-{t.Shape}",
                            "表せない参照アクションは方言の実効値へ畳んで比べる",
                            DialectNames.Oracle,
                            DialectNames.SqlServer
                        )
                        .Live(s =>
                            IdValueForeignKey(
                                s,
                                ForeignKeyReferentialAction.NoAction,
                                ForeignKeyReferentialAction.NoAction
                            )
                        )
                        .Target(s => IdValueForeignKey(s, t.OnDelete, t.OnUpdate))
                        .Expect(e =>
                            e.For(DialectNames.Oracle, d => d.Diff.Absent(ForeignKeyDiffKinds))
                                .For(
                                    DialectNames.SqlServer,
                                    d => d.Diff.Single("AlterForeignKey *")
                                )
                        )
            ),
            Row(
                    "diff-fk-alter-describes-effective-actions-only",
                    "外部キー変更の文言は参照アクションを実効値で書く",
                    DialectNames.Oracle
                )
                .Live(s =>
                    IdValueForeignKey(
                        s,
                        ForeignKeyReferentialAction.NoAction,
                        ForeignKeyReferentialAction.NoAction
                    )
                )
                .Target(s =>
                    IdValueForeignKey(
                        s,
                        ForeignKeyReferentialAction.Cascade,
                        ForeignKeyReferentialAction.Cascade
                    )
                )
                .Expect(e =>
                    e.Diff.Single("AlterForeignKey *", "AlterForeignKey * desc=\"*ON DELETE*\"")
                        .Diff.Absent("AlterForeignKey * desc=\"*ON UPDATE*\"")
                ),
            .. MySqlReferentialActions.Select(t =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"diff-fk-mysql-folds-only-set-default-{t.Shape}",
                            "MySQL は SET DEFAULT だけを ON DELETE・ON UPDATE とも NO ACTION へ畳んで比べる",
                            DialectNames.MySql
                        )
                        .Live(s =>
                            IdValueForeignKey(
                                s,
                                ForeignKeyReferentialAction.NoAction,
                                ForeignKeyReferentialAction.NoAction
                            )
                        )
                        .Target(s => IdValueForeignKey(s, t.OnDelete, t.OnUpdate))
                        .Expect(e =>
                        {
                            if (t.ExpectsAlter)
                            {
                                e.Diff.Contains("AlterForeignKey *");
                            }
                            else
                            {
                                e.Diff.Absent(ForeignKeyDiffKinds);
                            }
                        })
            ),
        ];

    /// <summary>複合主キーの親 P(A, B) と子 C(Id, X, Y)・外部キー FK_C_P（列ペアは並びを逆にもできる）</summary>
    private static void CompositeForeignKey(
        ExpectationSchema s,
        bool reversed,
        ForeignKeyReferentialAction onDelete
    )
    {
        s.Tbl("P", ("A", "int", true), ("B", "int", true));
        s.Tbl("C", ("Id", "int", true), ("X", "int", false), ("Y", "int", false));
        (string, string)[] pairs = [("A", "X"), ("B", "Y")];
        s.Fk("P", "C", "FK_C_P", onDelete, reversed ? [.. Enumerable.Reverse(pairs)] : pairs);
    }

    /// <summary>Customer(Id) と、Customer を指せる列 2 本の Order(Id, CustomerId1, CustomerId2)</summary>
    private static void CustomerOrderTwoColumns(ExpectationSchema s)
    {
        s.Tbl("Customer", ("Id", "int", true));
        s.Tbl(
            "Order",
            ("Id", "int", true),
            ("CustomerId1", "int", false),
            ("CustomerId2", "int", false)
        );
    }

    /// <summary>親 Parent(Id)・子 Child(Id, ParentId) と、ParentId → Id の外部キー</summary>
    private static void ParentChildForeignKey(
        ExpectationSchema s,
        string? name,
        ForeignKeyReferentialAction onDelete = ForeignKeyReferentialAction.NoAction,
        ForeignKeyReferentialAction onUpdate = ForeignKeyReferentialAction.NoAction
    )
    {
        s.Tbl("Parent", ("Id", "int", true));
        s.Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        s.Fk("Parent", "Child", name, onDelete, onUpdate, ("Id", "ParentId"));
    }

    /// <summary>
    /// 親 Parent(Id)・子 Child(Id, ParentId) と、同じ列ペアの外部キーを ON DELETE ごとに並べる
    /// （1 本目だけ名前を付ける）
    /// </summary>
    private static void ParentChildForeignKeys(
        ExpectationSchema s,
        string? firstName,
        ForeignKeyReferentialAction[] onDeletes
    )
    {
        s.Tbl("Parent", ("Id", "int", true));
        s.Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));

        for (var i = 0; i < onDeletes.Length; i++)
        {
            s.Fk("Parent", "Child", i == 0 ? firstName : null, onDeletes[i], ("Id", "ParentId"));
        }
    }

    /// <summary>名前に区切り文字を含む表 P|Q(Id)・C(Id, Ref)・P(Id)・Q|C(Id, Ref)</summary>
    private static void SeparatorTables(ExpectationSchema s)
    {
        s.Tbl("P|Q", ("Id", "int", true));
        s.Tbl("C", ("Id", "int", true), ("Ref", "int", false));
        s.Tbl("P", ("Id", "int", true));
        s.Tbl("Q|C", ("Id", "int", true), ("Ref", "int", false));
    }

    /// <summary>Parent(id, value) ← Child(id, value) の外部キー FK_Child_Parent（親の id → 子の value）</summary>
    private static void IdValueForeignKey(
        ExpectationSchema s,
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    )
    {
        ExpectationScenarios.IdValueTable(s, "Parent", "int");
        ExpectationScenarios.IdValueTable(s, "Child", "int");
        s.Fk("Parent", "Child", "FK_Child_Parent", onDelete, onUpdate, ("id", "value"));
    }
}
