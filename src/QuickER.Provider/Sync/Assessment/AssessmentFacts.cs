using QuickER.Dialects;
using QuickER.Provider.Sync.Selection;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.TableRebuilds;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 警告だけを作る規則（<see cref="AssessmentRules"/>）が読む、計画の事実の束
/// </summary>
/// <remarks>
/// 規則はここから事実を読み、自分で同期後の事実を求め直さない。値は計画の組み立てが 1 回ずつ決めて渡す。
/// </remarks>
internal class AssessmentFacts
{
    /// <summary>除外の周が止まったときの状態（外し終えた項目とその同期後の事実。作り直しは展開前）</summary>
    public required SelectionState Settled { get; init; }

    /// <summary>選んだ項目の最終形の同期後の事実（<see cref="PlanStage.Selected"/>）</summary>
    public required PostSyncSchema PostSync { get; init; }

    /// <summary>DB の今の図（文脈が無ければ <c>null</c>）</summary>
    public required LiveSchema? Live { get; init; }

    /// <summary>方言のケーパビリティ</summary>
    public required SyncDialectCapabilities Capabilities { get; init; }

    /// <summary>live の図と図の外の情報を持つ計画の入力（無ければ <c>null</c>）</summary>
    public required SyncPlanContext? Context { get; init; }

    /// <summary>
    /// テーブル再構築の方言か（<see cref="TableRebuildDialects.Requires"/>。方言の宣言から求める）
    /// </summary>
    public bool IsTableRebuildDialect => TableRebuildDialects.Requires(Capabilities);
}
