using System.Collections.Generic;
using System.Linq;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>文を種類の固定の順に並べる（同じ種類の文は、受けた並びの順のまま）</summary>
/// <remarks>
/// <para>
/// 順は、外部キーを外す → 一意制約を外す → 表の削除 → 表の追加 → 列の追加 → 主キーを外す → 列の変更 → 主キーを付ける →
/// 列の削除 → 一意制約を付ける → 外部キーを付ける → 表の説明 → 列の説明。
/// </para>
/// <para>
/// 外部キーは、参照される主キー・一意制約・列が変わる区間の外側で外して付ける。一意制約は外部キーの内側（外すのは外部キーを
/// 外した直後、付けるのは外部キーを付ける直前）。主キーは、列の変更を挟んで外して付ける（旧い主キーの列の NULL 許容化と、新しい
/// 主キーの列の NOT NULL 化を 1 回の同期で通す）。表の削除は表の追加より前（新しい表が古い表の制約の名前を引き継げる）。
/// </para>
/// </remarks>
internal static class StepOrder
{
    /// <summary>文を種類の固定の順に並べる</summary>
    /// <param name="steps">文の並び</param>
    /// <returns>種類の固定の順に並べた文（同じ種類の中は受けた順）</returns>
    public static List<ScheduledStep> Arrange(IEnumerable<ScheduledStep> steps) =>
        [.. steps.OrderBy(s => RankOf(s.Step))];

    /// <summary>文の種類の順位</summary>
    private static int RankOf(SyncStep step) =>
        step switch
        {
            DropForeignKeyStep => 0,
            DropUniqueConstraintStep => 1,
            DropTableStep => 2,
            CreateTableStep => 3,
            AddColumnStep => 4,
            DropPrimaryKeyStep => 5,
            AlterColumnStep => 6,
            AddPrimaryKeyStep => 7,
            DropColumnStep => 8,
            AddUniqueConstraintStep => 9,
            AddForeignKeyStep => 10,
            SetTableDescriptionStep => 11,
            SetColumnDescriptionStep => 12,
            _ => throw new System.InvalidOperationException(
                $"The step {step.GetType().Name} has no place in the fixed order."
            ),
        };
}
