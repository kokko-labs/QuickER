using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>差分の段の期待の行（列順の変更＝ReorderColumns・共通列の相対順序の比較）</summary>
/// <remarks>
/// 列順の判定（共通列の相対順序）は、列順の同期を持つ方言の差分が ReorderColumns として出す。判定だけを見る行は
/// 列順の移動を持つ合成の能力（<c>Native</c>）で流し、ReorderColumns の行だけを照合する。
/// </remarks>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>列順の判定（形, DB 側の表名と列, 図の表名と列, 列順の変更を出す表）</summary>
    private static (
        string Shape,
        string Description,
        (string Table, string[] Columns)[] Live,
        (string Table, string[] Columns)[] Target,
        string? Reordered
    )[] ColumnOrderDetections =>
        [
            (
                "order-only",
                "同一列集合で順序のみ異なる場合は列順変更として検知される",
                [("Customer", ["Id", "Name", "Email"])],
                [("Customer", ["Id", "Email", "Name"])],
                "Customer"
            ),
            (
                "case-only-table-name-uses-live-spelling",
                "列順の案内の表名は、大文字小文字だけ違う図でも DB の綴りで出る",
                [("Customer", ["Id", "Name", "Email"])],
                [("customer", ["Id", "Email", "Name"])],
                "Customer"
            ),
            (
                "added-column-only",
                "列追加がある場合は列順変更としては検知しない",
                [("Customer", ["Id", "Name"])],
                [("Customer", ["Id", "Email", "Name"])],
                null
            ),
            (
                "same-order",
                "同一順序なら列順変更としては検知しない",
                [("Customer", ["Id", "Name"])],
                [("Customer", ["Id", "Name"])],
                null
            ),
            (
                "reorder-with-added-column",
                "列追加を伴う並び替えでも共通列の順序が変われば検知する",
                [("Customer", ["Id", "Name", "Email"])],
                [("Customer", ["Id", "Email", "X", "Name"])],
                "Customer"
            ),
            (
                "reorder-with-dropped-column",
                "列削除を伴う並び替えでも共通列の順序が変われば検知する",
                [("Customer", ["Id", "Name", "Old", "Email"])],
                [("Customer", ["Id", "Email", "Name"])],
                "Customer"
            ),
            (
                "pure-insertion-keeping-order",
                "共通列の相対順序が保たれた列追加は検知しない",
                [("Customer", ["Id", "Name", "Email"])],
                [("Customer", ["Id", "Name", "X", "Email"])],
                null
            ),
            (
                "case-insensitive-column-names",
                "列順比較は大文字小文字を無視する",
                [("Customer", ["Id", "Name", "Email"])],
                [("Customer", ["id", "name", "email"])],
                null
            ),
            (
                "fewer-than-two-common-columns",
                "共通列が 1 列以下なら列順変更としては検知しない",
                [("Customer", ["Id", "OldA", "OldB"])],
                [("Customer", ["NewB", "NewA", "Id"])],
                null
            ),
            (
                "case-only-duplicates-match-exactly",
                "大文字小文字だけ違う名前が共存しても例外にならず、序数一致の組で列順を比べる",
                [("Customer", ["Id", "Dup", "dup"]), ("customer", ["Id", "Name"])],
                [("Customer", ["Id", "dup", "Dup"])],
                "Customer"
            ),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> DiffColumnOrderRows() =>
        [
            Row(
                    "diff-reorder-native-is-unselected-reorder-columns",
                    "Native 方言では ReorderColumns が生成され既定で未選択",
                    DialectNames.MySql
                )
                .Live(s => ReorderCustomer(s, "Name", "Email"))
                .Target(s => ReorderCustomer(s, "Email", "Name"))
                .Expect(e =>
                    e.Diff.Single(
                        "ReorderColumns *",
                        "ReorderColumns Customer sel=0 able=1 destr=0 *"
                    )
                ),
            Row(
                    "diff-reorder-rebuild-is-reorder-columns",
                    "Rebuild 方言でも ReorderColumns が生成される",
                    DialectNames.Sqlite
                )
                .Live(s => ReorderCustomer(s, "Name", "Email"))
                .Target(s => ReorderCustomer(s, "Email", "Name"))
                .Expect(e => e.Diff.Contains("ReorderColumns *")),
            // 元のテストは capabilities を省略した既定と明示の None の 2 回を比べていた。どちらも ColumnReorder=None の
            // Neutral で同じ入力なので 1 行にする
            Row(
                    "diff-reorder-none-is-not-generated",
                    "None 方言・capabilities 省略時は ReorderColumns を生成しない",
                    DialectNames.SqlServer
                )
                .Live(s => ReorderCustomer(s, "Name", "Email"))
                .Target(s => ReorderCustomer(s, "Email", "Name"))
                .Expect(e => e.Diff.Absent("ReorderColumns *")),
            .. ColumnOrderDetections.Select(t =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row($"diff-column-order-detection-{t.Shape}", t.Description, DialectNames.MySql)
                        .Live(s => IntColumnTables(s, t.Live))
                        .Target(s => IntColumnTables(s, t.Target))
                        .Expect(e =>
                        {
                            if (t.Reordered is { } table)
                            {
                                e.Diff.Single("ReorderColumns *", $"ReorderColumns {table} *");
                            }
                            else
                            {
                                e.Diff.Absent("ReorderColumns *");
                            }
                        })
            ),
        ];

    /// <summary>Customer(Id 主キー, 2 列 nvarchar(50))（2 列は指定の並び）</summary>
    private static Entity ReorderCustomer(ExpectationSchema s, string first, string second) =>
        s.Tbl(
            "Customer",
            ("Id", "int", true),
            (first, "nvarchar(50)", false),
            (second, "nvarchar(50)", false)
        );

    /// <summary>列がすべて int・NULL 許容・主キーなしの表を並べる</summary>
    private static void IntColumnTables(
        ExpectationSchema s,
        (string Table, string[] Columns)[] tables
    )
    {
        foreach (var (table, columns) in tables)
        {
            s.Tbl(table, [.. columns.Select(c => (c, "int", false))]);
        }
    }
}
