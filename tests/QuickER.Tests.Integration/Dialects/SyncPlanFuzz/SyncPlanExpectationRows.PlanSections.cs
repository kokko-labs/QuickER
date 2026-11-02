using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>計画の段の期待の行（選択による絞り込み・セクションの固定順序・空のセクション・主キーの変更のフェーズ）</summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanSectionRows() =>
        [
            Row("plan-unselected-items-are-excluded", "未選択項目は計画から除外される", C.Neutral)
                .Items(_ =>
                    [
                        I.Item(SchemaDiffKind.AddTable, selected: false),
                        I.Item(SchemaDiffKind.AddColumn, selected: true),
                    ]
                )
                .NoContext()
                .Expect(e => e.Sections.Exact("AddColumn: #1 AddColumn T")),
            Row(
                    "plan-sections-are-in-fixed-order",
                    "セクションは固定順序で並ぶ（主キー変更は 2 フェーズ）",
                    C.Neutral
                )
                // わざと逆順寄りに投入する
                .Items(_ =>
                    [
                        I.Item(SchemaDiffKind.SetColumnDescription),
                        I.Item(SchemaDiffKind.SetTableDescription),
                        I.Item(SchemaDiffKind.AddForeignKey),
                        I.Item(SchemaDiffKind.DropTable),
                        I.Item(SchemaDiffKind.DropColumn),
                        I.AlterPk("T", S.Detached("T", S.Pk("id", "INT"))),
                        I.Item(SchemaDiffKind.DropForeignKey),
                        I.Item(SchemaDiffKind.AlterColumn),
                        I.Item(SchemaDiffKind.AddColumn),
                        I.Item(SchemaDiffKind.AddTable),
                    ]
                )
                .NoContext()
                // DropForeignKey が AlterColumn より先（FK 依存列の型変更を通す）・主キーの変更は解除が AlterColumn の前で
                // 付与が後・DropTable は AddTable より前（名前を変える同期で一意制約の名前がぶつからない）。
                // フェーズは主キーの変更のセクションだけが持つ（ほかのセクション名にフェーズが付かない）
                .Expect(e =>
                    e.Sections.Exact(
                        "DropForeignKey: *",
                        "DropTable: *",
                        "AddTable: *",
                        "AddColumn: *",
                        "AlterPrimaryKey/Drop: *",
                        "AlterColumn: *",
                        "AlterPrimaryKey/Add: *",
                        "DropColumn: *",
                        "AddForeignKey: *",
                        "SetTableDescription: *",
                        "SetColumnDescription: *"
                    )
                ),
            Row(
                    "plan-pk-drop-only-omits-add-phase",
                    "主キー解除のみなら付与フェーズのセクションは出ない",
                    DialectNames.SqlServer
                )
                // 目標に主キー列が無い＝主キーの解除のみ
                .Items(_ => [I.AlterPk("T", S.Detached("T", S.Nullable("id", "INT")))])
                .NoContext()
                .Expect(e => e.Sections.Exact("AlterPrimaryKey/Drop: #0 AlterPrimaryKey T")),
            Row(
                    "plan-section-preserves-input-order",
                    "セクション内は入力の出現順を保持する",
                    C.Neutral
                )
                .Items(_ =>
                    [
                        I.Item(SchemaDiffKind.AddColumn, table: "A"),
                        I.Item(SchemaDiffKind.AddColumn, table: "B"),
                        I.Item(SchemaDiffKind.AddColumn, table: "C"),
                    ]
                )
                .NoContext()
                .Expect(e =>
                    e.Sections.Exact("AddColumn: #0 AddColumn A; #1 AddColumn B; #2 AddColumn C")
                ),
            Row("plan-empty-sections-are-omitted", "空セクションは含まれない", C.Neutral)
                .Items(_ => [I.Item(SchemaDiffKind.AddTable)])
                .NoContext()
                .Expect(e => e.Sections.Exact("AddTable: #0 AddTable T")),
            Row(
                    "plan-rebuild-table-item-is-always-excluded",
                    "RebuildTable は選択されていても除外される",
                    C.Neutral
                )
                .Items(_ =>
                    [
                        I.Item(SchemaDiffKind.RebuildTable, selected: true),
                        I.Item(SchemaDiffKind.AddTable, selected: true),
                    ]
                )
                .NoContext()
                .Expect(e => e.Sections.Exact("AddTable: #1 AddTable T")),
            Row(
                    "plan-all-unselected-is-empty",
                    "全未選択なら計画は空になる",
                    DialectNames.SqlServer
                )
                .Items(_ =>
                    [
                        I.Item(SchemaDiffKind.AddTable, selected: false),
                        I.Item(SchemaDiffKind.DropTable, selected: false),
                    ]
                )
                .NoContext()
                .Expect(e => e.Sections.Empty().Rebuilds.Empty().Reorders.Empty()),
            Row(
                    "plan-non-rebuild-dialect-same-plan-regardless-of-capabilities",
                    "逐次 DDL 方言はケーパビリティ差で計画が変わらない",
                    C.Neutral,
                    C.NeutralWithoutDescriptions
                )
                // SupportsDescriptions=false だけでは再構築の方言にならない（ALTER・FK 変更は可能なまま）
                .Items(_ =>
                    [
                        I.Item(SchemaDiffKind.AddTable),
                        I.Item(SchemaDiffKind.AlterColumn),
                        I.Item(SchemaDiffKind.SetTableDescription),
                    ]
                )
                .NoContext()
                .Expect(e => e.SamePlan().Rebuilds.Empty()),
            Row(
                    "plan-without-pk-change-has-no-phase",
                    "主キー変更を含まない計画にフェーズ表記は出ない",
                    DialectNames.SqlServer
                )
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "T",
                            ColumnName = "Memo",
                            Column = S.Nullable("Memo", "int"),
                            OldColumn = S.NotNull("Memo", "int"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = "T",
                            ColumnName = "Email",
                            Column = S.NotNull("Email", "nvarchar(200)"),
                            IsSelected = true,
                        },
                    ]
                )
                .NoContext()
                .Expect(e =>
                    e.Sections.Exact(
                        "AddColumn: #1 AddColumn T.Email",
                        "AlterColumn: #0 AlterColumn T.Memo"
                    )
                ),
        ];
}
