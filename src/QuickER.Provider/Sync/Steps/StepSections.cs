using System.Collections.Generic;

namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 同期スクリプトの 1 セクション分（隣り合う、種類・主キーのフェーズ・前寄せかが同じ文の並び）
/// </summary>
/// <remarks>
/// 方言ビルダーはセクションごとに見出し（種類と主キーのフェーズ・文の数）を書き、文を順に書く。同じ種類のセクションが複数回・別の位置に
/// 現れることがある（主キーの変更の解除と付与・前寄せ）。
/// </remarks>
public sealed class StepSection
{
    /// <summary>このセクションの文の種類</summary>
    public SchemaDiffKind Kind { get; init; }

    /// <summary>主キーの変更のフェーズ（主キーを外す文は解除、付ける文は付与。ほかの種類は <see cref="PrimaryKeyPhase.None"/>）</summary>
    public PrimaryKeyPhase PrimaryKeyPhase { get; init; } = PrimaryKeyPhase.None;

    /// <summary>このセクションの文（計画の文の順）</summary>
    public IReadOnlyList<SyncStep> Steps { get; init; } = [];

    /// <summary>
    /// 必ず断られる文として計画の先頭へ移した文のセクションか（<see cref="SyncPlan.MovedFirstCount"/> の数の文）
    /// </summary>
    /// <remarks>方言ビルダーは見ない（文の並びはセクションの順のまま）。</remarks>
    internal bool IsMovedFirst { get; init; }
}

/// <summary>
/// 計画の文の並びをセクションへまとめる（方言ビルダー・同期の名前の入口〔<see cref="SqlNameText"/>〕と、テストが計画をセクションと
/// 差分の項目の形で読むための写しが共有する 1 か所）
/// </summary>
/// <remarks>
/// <para>
/// 計画の文を順に見て、隣り合う文を「種類・主キーのフェーズ・前寄せか（文の位置が <see cref="SyncPlan.MovedFirstCount"/> より前か）」が
/// 同じ間 1 つのセクションにまとめる。空のセクションは作らない。
/// </para>
/// <para>
/// 表を作り直す文（<see cref="RebuildTableStep"/>＝<see cref="SyncPlan.Rebuilds"/>）と列を動かす文（<see cref="MoveColumnStep"/>＝
/// <see cref="SyncPlan.Reorders"/>）はセクションに入れない。この 2 つは前後のセクションを区切らず、位置の数には数える。
/// </para>
/// </remarks>
public static class StepSections
{
    /// <summary>計画の文の並びを、セクションの並びへまとめる</summary>
    /// <param name="plan">計画</param>
    /// <returns>セクションの並び（計画の文の順）</returns>
    /// <exception cref="System.InvalidOperationException">セクションの種類を持たない文の型があるとき</exception>
    public static IReadOnlyList<StepSection> Of(SyncPlan plan)
    {
        var sections = new List<StepSection>();
        SectionKey? currentKey = null;
        var currentSteps = new List<SyncStep>();

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];

            if (step is RebuildTableStep or MoveColumnStep)
            {
                continue;
            }

            var (kind, phase) = SectionOf(step);
            var key = new SectionKey(kind, phase, i < plan.MovedFirstCount);

            if (key != currentKey)
            {
                Flush();
                currentKey = key;
            }

            currentSteps.Add(step);
        }

        Flush();

        return sections;

        void Flush()
        {
            if (currentKey is { } closing && currentSteps.Count > 0)
            {
                sections.Add(
                    new StepSection
                    {
                        Kind = closing.Kind,
                        PrimaryKeyPhase = closing.Phase,
                        Steps = [.. currentSteps],
                        IsMovedFirst = closing.MovedFirst,
                    }
                );
            }

            currentSteps.Clear();
        }
    }

    /// <summary>文が入るセクションの種類と主キーのフェーズ</summary>
    /// <param name="step">セクションに入る文</param>
    /// <exception cref="System.InvalidOperationException">セクションの種類を持たない文の型のとき</exception>
    internal static (SchemaDiffKind Kind, PrimaryKeyPhase Phase) SectionOf(SyncStep step) =>
        step switch
        {
            CreateTableStep => (SchemaDiffKind.AddTable, PrimaryKeyPhase.None),
            DropTableStep => (SchemaDiffKind.DropTable, PrimaryKeyPhase.None),
            AddColumnStep => (SchemaDiffKind.AddColumn, PrimaryKeyPhase.None),
            AlterColumnStep => (SchemaDiffKind.AlterColumn, PrimaryKeyPhase.None),
            DropColumnStep => (SchemaDiffKind.DropColumn, PrimaryKeyPhase.None),
            DropPrimaryKeyStep => (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Drop),
            AddPrimaryKeyStep => (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Add),
            DropUniqueConstraintStep => (SchemaDiffKind.DropUniqueConstraint, PrimaryKeyPhase.None),
            AddUniqueConstraintStep => (SchemaDiffKind.AddUniqueConstraint, PrimaryKeyPhase.None),
            DropForeignKeyStep => (SchemaDiffKind.DropForeignKey, PrimaryKeyPhase.None),
            AddForeignKeyStep => (SchemaDiffKind.AddForeignKey, PrimaryKeyPhase.None),
            SetTableDescriptionStep => (SchemaDiffKind.SetTableDescription, PrimaryKeyPhase.None),
            SetColumnDescriptionStep => (SchemaDiffKind.SetColumnDescription, PrimaryKeyPhase.None),
            _ => throw new System.InvalidOperationException(
                $"The step {step.GetType().Name} does not belong to a section."
            ),
        };

    /// <summary>セクションをまとめる鍵（種類・主キーのフェーズ・前寄せか）</summary>
    private readonly record struct SectionKey(
        SchemaDiffKind Kind,
        PrimaryKeyPhase Phase,
        bool MovedFirst
    );
}
