using QuickER.Provider.Sync;
using QuickER.Provider.Sync.Assessment;
using QuickER.Provider.Sync.Scheduling;
using QuickER.Provider.Sync.Selection;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.TableRebuilds;

namespace QuickER.Provider;

/// <summary>
/// 差分項目から方言中立の実行計画（<see cref="SyncPlan"/>）を組み立てるサービス。
/// </summary>
/// <remarks>
/// <para>
/// 入口は <see cref="BuildPlan"/> の 1 つ。計画は計画器（<see cref="StepPlanner"/>）が文の並びとして作る。方言別レンダラー
/// （<see cref="ISyncScriptBuilder"/> の実装）はこの計画の文を SQL へ変換するだけでよく、選択フィルタ・
/// 出力順序・種別グループ化の知識を各方言に重複させない。
/// </para>
/// <para>
/// このクラスが持つのは製品の入口だけ。計画の段は <c>Sync/</c> の下にある：選択の前半（<see cref="PlanSelection.PrepareSelection"/>）・
/// 組み立て（<see cref="SequentialScheduler"/>・<see cref="TableRebuildScheduler"/>）・評価（<see cref="AssessmentRules"/>）と、
/// それらが共有する事実の述語（<see cref="PlanFacts"/>）と識別の入口（<see cref="PlanIdentity"/>）。文の種類の順は
/// <see cref="StepOrder"/> が持つ。
/// </para>
/// <para>
/// テーブル再構築方言（SQLite＝<see cref="SyncDialectCapabilities.SupportsAlterColumn"/> または
/// <see cref="SyncDialectCapabilities.SupportsForeignKeyAlter"/> が <c>false</c>）では、逐次 DDL で表現できない
/// 変更（列型変更・列削除・FK 変更・新規テーブルへの後付け FK）をテーブル単位の <see cref="TableRebuildPlan"/> へ
/// 集約する（合成には live スキーマが必須＝<see cref="SyncPlanContext"/> を要求する）。
/// </para>
/// </remarks>
public sealed class SyncPlanner
{
    /// <summary>選択済みの差分項目から実行計画を組み立てる</summary>
    /// <param name="items">全差分項目（未選択・情報表示専用の項目を含んでよい）</param>
    /// <param name="capabilities">対象方言の同期ケーパビリティ（rebuild への振り分けに用いる）</param>
    /// <param name="context">
    /// live スキーマ。rebuild 方言は合成に、列順を同期する逐次 DDL の方言（<see cref="SyncDialectCapabilities.ColumnReorder"/> が
    /// <see cref="ColumnReorderMode.Native"/>＝MySQL）は列順の項目の移動の組み立てに用いる。ほかの逐次 DDL 方言では省いてよい
    /// （<c>null</c> 可）。rebuild 方言で <c>null</c> を渡すか、Native の方言で
    /// 列順の項目を選んで <c>null</c> を渡すと <see cref="InvalidOperationException"/> を投げる（呼び出し側のバグ）。
    /// </param>
    /// <returns>
    /// 選択済み項目から計画器（<see cref="StepPlanner"/>）が作った文の並びの計画（そのまま返す）
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 文に要るメンバーの無い項目を渡したとき（項目の種類と無いメンバーを名指しする。差分〔<see cref="SchemaDiffService.Compute"/>〕が
    /// 作る項目はどれも文で表せる）・再構築の方言で計画の入力か DB の今の図が無いとき・除外の周か取り下げの輪が上限を超えたとき
    /// （どれも呼び出し側のバグ）
    /// </exception>
    public SyncPlan BuildPlan(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context = null
    )
    {
        var planned = StepPlanner.Plan(items, capabilities, context);

        if (planned.Plan is null)
        {
            throw new InvalidOperationException(
                $"The sync plan cannot be built from an incomplete item: {planned.Unrepresentable}. "
                    + "Pass the items that SchemaDiffService.Compute produced."
            );
        }

        return planned.Plan;
    }
}
