using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（既定値のある列の型の種類を変える列の変更を、計画が事前に警告する＝J2。宣言した方言＝SQLite 以外の 4 方言だけ。
/// 既定値は計画の文脈の ColumnDefaults＝取込の結果から読む）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>型の種類を変えない・既定値の無い列の変更の形（形, 変更前の型, 変更後の型, 列 v に既定値があるか）</summary>
    private static (
        string Shape,
        string OldType,
        string NewType,
        bool HasDefault
    )[] DefaultNoWarningShapes =>
        [
            ("same-kind", "varchar(20)", "varchar(40)", true),
            ("without-default", "varchar(20)", "int", false),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanDefaultRows() =>
        [
            DefaultTypeChange(
                    Row(
                        "plan-default-type-kind-change-warns-only-where-declared",
                        "既定値のある列の型の種類の変更は、SQLite 以外の 4 方言の計画だけが警告する",
                        AllDialects
                    ),
                    "varchar(20)",
                    "int",
                    hasDefault: true
                )
                .Expect(e =>
                {
                    foreach (
                        var dialect in AllDialects.Where(d => d != DialectNames.Sqlite).ToArray()
                    )
                    {
                        e.For(
                            dialect,
                            d =>
                                d.Warnings.Count("DefaultOnColumnTypeKindChange*", 1)
                                    .Warnings.Single(
                                        "DefaultOnColumnTypeKindChange(item): v (varchar(20) \u2192 int)"
                                    )
                        );
                    }

                    e.For(
                        DialectNames.Sqlite,
                        d => d.Warnings.Absent("DefaultOnColumnTypeKindChange*")
                    );
                }),
            .. DefaultNoWarningShapes.Select(shape =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    DefaultTypeChange(
                            Row(
                                $"plan-default-no-warning-{shape.Shape}",
                                "型の種類が変わらない変更と既定値の無い列の変更は警告しない",
                                DialectNames.SqlServer
                            ),
                            shape.OldType,
                            shape.NewType,
                            shape.HasDefault
                        )
                        .Expect(e => e.Warnings.Absent("DefaultOnColumnTypeKindChange*"))
            ),
        ];

    /// <summary>
    /// 行に、item(id, v) の列 v の型を変える列の変更を選んだ入力を書く（既定値の無い場合も、別の列 id の既定値は持たせる＝列ごとの照合を
    /// 通す）
    /// </summary>
    private static ExpectationRow DefaultTypeChange(
        ExpectationRow row,
        string oldType,
        string newType,
        bool hasDefault
    ) =>
        row.Live(s => s.Table("item", S.Pk("id", "int"), S.NotNull("v", oldType)))
            .Items(w =>
                [
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.AlterColumn,
                        TableName = "item",
                        ColumnName = "v",
                        Column = S.NotNull("v", newType),
                        OldColumn = w.Live.Col("item", "v"),
                        IsSelected = true,
                    },
                ]
            )
            .Context(w => new SyncPlanContext
            {
                LiveEntities = w.Live.Entities,
                ColumnDefaults = hasDefault
                    ? [new SchemaColumnDefault("item", "v", "'abc'")]
                    : [new SchemaColumnDefault("item", "id", "0")],
            });
}
