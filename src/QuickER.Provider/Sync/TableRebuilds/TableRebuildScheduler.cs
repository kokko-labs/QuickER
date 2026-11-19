using System.Linq;
using QuickER.Dialects;
using QuickER.Provider.Sync.Scheduling;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.TableRebuilds;

/// <summary>
/// テーブル再構築の方言（SQLite）の文の並びを組み立てる（選んだ項目の最終形の同期後の事実と、DB の今の図と、計画の入力と、方言の宣言から）
/// </summary>
/// <remarks>
/// <para>組み立ての段：</para>
/// <list type="number">
/// <item>表ごとの再構築を合成する（<see cref="TableRebuildSynthesis.Synthesize"/>）。</item>
/// <item>
/// 再構築へ畳まなかった項目（選んだ項目の最終形から <see cref="TableRebuildSynthesisResult.Folded"/> を除いた並び）を、項目の順のまま文へ
/// 写す（<see cref="SelectedSteps"/>）。文で表せない項目があれば、理由だけを返す。
/// </item>
/// <item>種類の固定の順に、安定に並べる（<see cref="StepOrder"/>）。</item>
/// <item>末尾へ、再構築の文（<see cref="RebuildTableStep"/>・由来は畳んだ項目の並び）を合成の順に足す。</item>
/// </list>
/// <para>
/// 外部キーの並べ替え・外し直し・列を動かす文は組まない（再構築の方言は表ごと作り直し、列順の変更も合成が列を並べ替える）。外し直しの
/// 宣言は集めないので、結果の <see cref="StepSchedule.Required"/> は null。同期スクリプトの中の再構築と残りのセクションの順は、
/// 方言のスクリプトビルダーが決める。
/// </para>
/// <para>
/// 結果は文の並びだけで、差分の項目も警告も作らない。同期後の事実は受けたものを読み、作り直さない。
/// </para>
/// </remarks>
internal static class TableRebuildScheduler
{
    /// <summary>文の並びを組み立てる</summary>
    /// <param name="postSync">選んだ項目の最終形（<see cref="PlanStage.Selected"/>）の同期後の事実</param>
    /// <param name="live">DB の今の図（合成の土台）</param>
    /// <param name="context">計画の入力（合成が補助オブジェクトを読む）</param>
    /// <param name="capabilities">方言の宣言</param>
    /// <returns>組み立ての結果</returns>
    public static StepSchedule Schedule(
        PostSyncSchema postSync,
        LiveSchema live,
        SyncPlanContext context,
        SyncDialectCapabilities capabilities
    )
    {
        var synthesis = TableRebuildSynthesis.Synthesize(postSync, live, context, capabilities);

        // 再構築へ畳まなかった項目（項目の順のまま）を文にする
        var (remaining, unrepresentable) = SelectedSteps.From([
            .. postSync.Items.Where(i => !synthesis.Folded.Contains(i)),
        ]);

        if (remaining is null)
        {
            return new StepSchedule(null, unrepresentable);
        }

        var ordered = StepOrder.Arrange(remaining);

        return new StepSchedule(
            [
                .. ordered.Select(s => s.Step),
                .. synthesis.Rebuilds.Select(rebuild => new RebuildTableStep(
                    new MergedOrigin(rebuild.SourceItems),
                    rebuild
                )),
            ],
            null
        )
        {
            SelectedUniqueConstraintDrops = SelectedSteps.UniqueConstraintDrops(postSync.Items),
        };
    }
}
