using System;
using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>差分の段の期待の行（一意制約の組み方＝名前の段と列集合の段・作り直しへの畳み込み・主キーと同じ列集合）</summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> DiffUniqueConstraintRows() =>
        [
            UniqueOnPrimaryKeyFoldRow(),
            Row(
                    "diff-uq-on-primary-key-existing-in-live-is-not-dropped",
                    "主キーと同じ列集合の一意制約: SQLite・Oracle は比較から外す（5 方言）",
                    AllDialects
                )
                .Live(s =>
                {
                    s.Tbl("Item", ("Id", "int", true), ("Code", "int", false));
                    s.Unique("Item", "UQ_Item_Id", "Id");
                })
                .Target(UniqueOnPrimaryKeyAndCode)
                .Expect(e => e.Diff.Absent("DropUniqueConstraint *")),
            Row(
                    "diff-uq-only-in-target-is-selected-add",
                    "図にだけ在る一意制約は AddUniqueConstraint になり既定で選択される",
                    DialectNames.SqlServer
                )
                .Live(s => UniqueCustomer(s))
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", null, "Code");
                })
                .Expect(e =>
                    e.Diff.Single(
                        "AddUniqueConstraint *",
                        "AddUniqueConstraint Customer *sel=1 * destr=0 *uqcols=(Code) *desc=\""
                            + string.Format(
                                ProviderStrings.Diff_AddUniqueConstraint,
                                "Customer",
                                "Code"
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-uq-only-in-live-is-unselected-drop",
                    "DB にだけ在る一意制約は DropUniqueConstraint になり既定で未選択",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_Legacy", "Code", "Kind");
                })
                .Target(s => UniqueCustomer(s))
                .Expect(e =>
                    e.Diff.Single(
                        "DropUniqueConstraint *",
                        "DropUniqueConstraint Customer uq=UQ_Legacy sel=0 * destr=1 *uqcols=(Code,Kind) *desc=\""
                            + string.Format(
                                ProviderStrings.Diff_DropUniqueConstraint,
                                "Customer",
                                "Code, Kind"
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-uq-columns-differing-only-by-lowercasing-follow-column-identity",
                    "一意制約の照合: 小文字化だけで同じになる別名の列の一意制約は、列とそろって追加と削除になる",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Tbl("Customer", ("Id", "int", true), ("\u212A", "nvarchar(20)", false));
                    s.Unique("Customer", "UQ_Customer_Kelvin", "\u212A");
                })
                .Target(s =>
                {
                    s.Tbl("Customer", ("Id", "int", true), ("k", "nvarchar(20)", false));
                    s.Unique("Customer", "UQ_Customer_k", "k");
                })
                .Expect(e =>
                    e.Diff.Contains(
                        "AddColumn *.k *",
                        "AddUniqueConstraint * uqcols=(k) *",
                        "DropUniqueConstraint * uq=UQ_Customer_Kelvin *"
                    )
                ),
            Row(
                    "diff-uq-same-name-column-change-is-single-alter",
                    "一意制約の変更: 同じ名前で列を変えると AlterUniqueConstraint 1 項目にまとまり既定で未選択",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_Code", "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", " uq_code ", "Code", "Kind");
                })
                .Expect(e =>
                    e.Diff.Exact(
                        "AlterUniqueConstraint * uq=uq_code sel=0 * destr=1 *uqcols=(Code,Kind) *"
                            + "replaced-uq=[* uq=UQ_Code uqcols=(Code)]* desc=\""
                            + string.Format(
                                ProviderStrings.Diff_AlterUniqueConstraint,
                                "Customer",
                                "uq_code",
                                "Code",
                                "Code, Kind"
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-uq-synthesized-name-matching-live-is-alter",
                    "一意制約の変更: 図の名前が未設定でも合成名が DB の名前と同じなら組む",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_Customer_Code", "Code", "Kind");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", null, "Code");
                })
                .Expect(e => e.Diff.Exact("AlterUniqueConstraint *")),
            Row(
                    "diff-uq-name-step-pairs-diagram-name-first-seed75",
                    "一意制約の名前の段: 図の名前を先に DB の同じ名前と組み、残りを列集合で組む",
                    AllDialects
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_x", "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_x", "Kind");
                    s.Unique("Customer", null, "Code");
                })
                .Expect(e =>
                    e.Diff.Exact(
                        "AlterUniqueConstraint * uq=UQ_x sel=* uqcols=(Kind) *replaced-uq=[* uqcols=(Code)]*",
                        "AddUniqueConstraint * uq=UQ_Customer_Code sel=* uqcols=(Code) *"
                    )
                ),
            Row(
                    "diff-uq-name-step-same-name-different-columns-is-alter",
                    "一意制約の名前の段: 図の名前を先に DB の同じ名前と組み、残りを列集合で組む",
                    AllDialects
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_x", "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "uq_X", "Kind");
                })
                .Expect(e => e.Diff.Exact("AlterUniqueConstraint *")),
            Row(
                    "diff-uq-name-step-different-name-same-columns-is-empty",
                    "一意制約の名前の段: 図の名前を先に DB の同じ名前と組み、残りを列集合で組む",
                    AllDialects
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_x", "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_y", "Code");
                })
                .Expect(e => e.Diff.Empty()),
            Row(
                    "diff-uq-name-step-skips-unnamed-live",
                    "一意制約の名前の段: 図の名前を先に DB の同じ名前と組み、残りを列集合で組む",
                    AllDialects
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", null, "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_x", "Kind");
                    s.Unique("Customer", null, "Code");
                })
                .Expect(e => e.Diff.Exact("AddUniqueConstraint * uq=UQ_x sel=*")),
            Row(
                    "diff-uq-name-step-ignores-synthesized-names",
                    "一意制約の名前の段: 名前の無い制約の合成名では組まず、列集合で満たされていれば作り直さない",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_Customer_Code", "Kind");
                    s.Unique("Customer", "UQ_other", "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", null, "Code");
                })
                .Expect(e => e.Diff.Exact("DropUniqueConstraint * uq=UQ_Customer_Code sel=*")),
            Row(
                    "diff-uq-different-names-are-not-paired",
                    "一意制約の変更: 名前違い・DB 側の名前なしは組まず、持てない方言の主キーと同じ列集合は削除だけ",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_A", "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_B", "Kind");
                })
                .Expect(e => e.Diff.Exact("AddUniqueConstraint *", "DropUniqueConstraint *")),
            Row(
                    "diff-uq-unnamed-live-is-not-paired",
                    "一意制約の変更: 名前違い・DB 側の名前なしは組まず、持てない方言の主キーと同じ列集合は削除だけ",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", null, "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", null, "Kind");
                })
                .Expect(e => e.Diff.Exact("AddUniqueConstraint *", "DropUniqueConstraint *")),
            Row(
                    "diff-uq-on-primary-key-unsupported-leaves-drop-only",
                    "一意制約の変更: 名前違い・DB 側の名前なしは組まず、持てない方言の主キーと同じ列集合は削除だけ",
                    DialectNames.Oracle
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_X", "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_X", "Id");
                })
                .Expect(e => e.Diff.Exact("DropUniqueConstraint *")),
            Row(
                    "diff-uq-name-only-difference-is-not-diffed",
                    "制約名の差だけでは一意制約の差分にならない",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_From_Db", "Code");
                })
                .Target(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", null, "Code");
                })
                .Expect(e => e.Diff.Absent("AddUniqueConstraint *", "DropUniqueConstraint *")),
            Row(
                    "diff-uq-column-order-or-case-only-is-not-diffed",
                    "構成列の順序・大文字小文字の差だけでは一意制約の差分にならない",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    UniqueCustomer(s);
                    s.Unique("Customer", "UQ_A", "Code", "Kind");
                })
                .Target(s =>
                {
                    s.Tbl(
                        "Customer",
                        ("Id", "int", true),
                        ("code", "nvarchar(20)", false),
                        ("kind", "nvarchar(10)", false)
                    );
                    s.Unique("Customer", null, "kind", "code");
                })
                .Expect(e => e.Diff.Absent("AddUniqueConstraint *", "DropUniqueConstraint *")),
            Row(
                    "diff-uq-unresolvable-columns-are-not-diffed",
                    "構成列が解決できない一意制約は差分にならない",
                    DialectNames.SqlServer
                )
                .Live(s => UniqueCustomer(s))
                .Target(s =>
                {
                    var customer = UniqueCustomer(s);
                    // 空の制約と、このエンティティに存在しない列を指す制約（いずれも DDL 生成側も無視する）
                    customer.UniqueConstraints.Add(new UniqueConstraint());
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint { ColumnIds = [Guid.NewGuid()] }
                    );
                })
                .Expect(e => e.Diff.Absent("AddUniqueConstraint *")),
        ];

    /// <summary>Customer(Id, Code nvarchar(20), Kind nvarchar(10))（一意制約の行の共通土台）</summary>
    private static Entity UniqueCustomer(ExpectationSchema s) =>
        s.Tbl(
            "Customer",
            ("Id", "int", true),
            ("Code", "nvarchar(20)", false),
            ("Kind", "nvarchar(10)", false)
        );

    /// <summary>Item(Id, Code) に、主キーと同じ列集合の一意制約と Code の一意制約（どちらも名前なし）</summary>
    private static void UniqueOnPrimaryKeyAndCode(ExpectationSchema s)
    {
        s.Tbl("Item", ("Id", "int", true), ("Code", "int", false));
        s.Unique("Item", null, "Id");
        s.Unique("Item", null, "Code");
    }

    private static ExpectationRow UniqueOnPrimaryKeyFoldRow() =>
        Row(
                "diff-uq-on-primary-key-folded-where-unsupported",
                "主キーと同じ列集合の一意制約: SQLite・Oracle は比較から外す（5 方言）",
                AllDialects
            )
            .Live(s => s.Tbl("Item", ("Id", "int", true), ("Code", "int", false)))
            .Target(UniqueOnPrimaryKeyAndCode)
            .Expect(e =>
            {
                e.Diff.Single("AddUniqueConstraint * uqcols=(Code) *");

                foreach (var dialect in AllDialects)
                {
                    // 主キーと同じ列集合の一意制約を別の制約として持てないのは Oracle と SQLite だけ
                    var adds = dialect is DialectNames.Oracle or DialectNames.Sqlite ? 1 : 2;
                    e.For(
                        dialect,
                        d =>
                            d.Diff.Count("AddUniqueConstraint *", adds)
                                .Diff.Count("AddUniqueConstraint * sel=1 able=1 *", adds)
                    );
                }
            });
}
