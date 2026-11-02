using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>差分の段の期待の行（主キーの膜と順序＝AlterPrimaryKey）</summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> DiffPrimaryKeyRows() =>
        [
            Row(
                    "diff-pk-added-is-unselected-destructive-alter",
                    "主キーの追加は AlterPrimaryKey になり、既定では未選択かつ破壊的",
                    DialectNames.SqlServer
                )
                .Live(s => KeyedTable(s, "Customer", ["Id", "Name"]))
                .Target(s => KeyedTable(s, "Customer", ["Id", "Name"], "Id"))
                .Expect(e =>
                    e.Diff.Exact(
                        "AlterPrimaryKey Customer sel=0 able=1 destr=1 *desc=\""
                            + string.Format(
                                ProviderStrings.Diff_AlterPrimaryKey,
                                "Customer",
                                ProviderStrings.Diff_PrimaryKey_None,
                                "Id"
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-pk-removed-is-alter",
                    "主キーの解除は AlterPrimaryKey になる",
                    DialectNames.SqlServer
                )
                .Live(s => KeyedTable(s, "Customer", ["Id", "Name"], "Id"))
                .Target(s => KeyedTable(s, "Customer", ["Id", "Name"]))
                .Expect(e =>
                    e.Diff.Single(
                        "AlterPrimaryKey *",
                        "AlterPrimaryKey *desc=\""
                            + string.Format(
                                ProviderStrings.Diff_AlterPrimaryKey,
                                "Customer",
                                "Id",
                                ProviderStrings.Diff_PrimaryKey_None
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-pk-composition-change-is-single-alter",
                    "単一主キー → 複合主キーの構成変更は 1 件の AlterPrimaryKey になる",
                    DialectNames.SqlServer
                )
                .Live(s => KeyedTable(s, "OrderLine", ["OrderId", "LineNo"], "OrderId"))
                .Target(s => KeyedTable(s, "OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"))
                .Expect(e =>
                    e.Diff.Exact(
                        "AlterPrimaryKey *desc=\""
                            + string.Format(
                                ProviderStrings.Diff_AlterPrimaryKey,
                                "OrderLine",
                                "OrderId",
                                "OrderId, LineNo"
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-pk-order-change-is-alter",
                    "複合主キーの順序変更も AlterPrimaryKey になる",
                    DialectNames.SqlServer
                )
                .Live(s => KeyedTable(s, "OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"))
                .Target(s => KeyedTable(s, "OrderLine", ["LineNo", "OrderId"], "OrderId", "LineNo"))
                .Expect(e =>
                    e.Diff.Single(
                        "AlterPrimaryKey *",
                        "AlterPrimaryKey *desc=\""
                            + string.Format(
                                ProviderStrings.Diff_AlterPrimaryKey,
                                "OrderLine",
                                "OrderId, LineNo",
                                "LineNo, OrderId"
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-pk-same-is-not-altered",
                    "主キー構成が同じなら AlterPrimaryKey は出ない",
                    DialectNames.SqlServer
                )
                .Live(s => KeyedTable(s, "OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"))
                // 列名の大文字小文字だけが異なる（列差分と同じ規則で同一とみなす）
                .Target(s => KeyedTable(s, "OrderLine", ["orderid", "lineno"], "orderid", "lineno"))
                .Expect(e => e.Diff.Absent("AlterPrimaryKey *")),
            Row(
                    "diff-pk-order-only-difference-is-alter",
                    "主キーの順序だけが違えば AlterPrimaryKey になる",
                    DialectNames.SqlServer
                )
                // 列定義順はどちらも (a, b)。主キーの実効順だけが (b, a) と (a, b) で食い違う
                .Live(s => OrderedKeyTable(s, "Pair", ["a", "b"], "b", "a"))
                .Target(s => OrderedKeyTable(s, "Pair", ["a", "b"], "a", "b"))
                .Expect(e => e.Diff.Exact("AlterPrimaryKey Pair *")),
            Row(
                    "diff-pk-same-order-is-empty",
                    "主キーの順序が双方同一なら差分は出ない",
                    DialectNames.SqlServer
                )
                .Live(s => OrderedKeyTable(s, "Pair", ["a", "b"], "b", "a"))
                .Target(s => OrderedKeyTable(s, "Pair", ["a", "b"], "b", "a"))
                .Expect(e => e.Diff.Empty()),
            Row(
                    "diff-pk-empty-order-matches-explicit-declaration-order",
                    "順序リスト空 vs 同順の明示指定では差分が出ない",
                    DialectNames.SqlServer
                )
                // DB 側は順序リストなし（＝列宣言順 (a, b)）、図は同じ並びを明示指定
                .Live(s => KeyedTable(s, "Pair", ["a", "b"], "a", "b"))
                .Target(s => OrderedKeyTable(s, "Pair", ["a", "b"], "a", "b"))
                .Expect(e => e.Diff.Empty()),
            Row(
                    "diff-pk-column-nullability-compared-as-not-null",
                    "NULL 許容: 主キーの列は実効の値（NOT NULL）で比べる",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("Item", ("Id", "int", true)))
                .Target(s => s.Tbl("Item", ("Id", "int", true)).Columns[0].IsNullable = true)
                .Expect(e => e.Diff.Empty()),
        ];

    /// <summary>
    /// 列がすべて int・NOT NULL の表（主キーは指定の列。型・NULL 許容を固定して主キー以外の差分を起こさない）
    /// </summary>
    private static Entity KeyedTable(
        ExpectationSchema s,
        string name,
        string[] columns,
        params string[] keys
    ) =>
        s.Table(
            name,
            [
                .. columns.Select(c =>
                    keys.Contains(c, StringComparer.OrdinalIgnoreCase)
                        ? ExpectationSchema.Pk(c, "int")
                        : ExpectationSchema.NotNull(c, "int")
                ),
            ]
        );

    /// <summary><see cref="KeyedTable"/> に主キーの順序（実効順）を宣言する</summary>
    private static void OrderedKeyTable(
        ExpectationSchema s,
        string name,
        string[] columns,
        params string[] keyOrder
    )
    {
        KeyedTable(s, name, columns, keyOrder);
        s.PrimaryKeyOrder(name, keyOrder);
    }
}
