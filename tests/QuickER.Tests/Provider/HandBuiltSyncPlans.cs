using QuickER.Provider;
using QuickER.Provider.Sync.Scheduling;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Tests.Provider;

/// <summary>
/// 計画器を通さずに手で組む同期の計画（方言ビルダーと入口の検証を、計画器が作らない形で流すテストが使う）
/// </summary>
/// <remarks>
/// <para>
/// 計画は文の並び（<see cref="SyncPlan.Steps"/>）だけを持つので、差分の項目・再構築・列順から文を組み立てる。差分の項目は製品の写し
/// （<see cref="SelectedSteps.From"/>＝計画器が選んだ項目を文へ写すのと同じ関数）で文にするので、文の由来は元の項目そのものを持つ。
/// </para>
/// <para>
/// 文の順は計画器と同じく、項目の文 → 列を動かす文 → 表を作り直す文。
/// </para>
/// </remarks>
internal static class HandBuiltSyncPlans
{
    /// <summary>差分の項目の並びから計画を組む（項目の順に文へ写す）</summary>
    /// <param name="items">差分の項目（主キーの変更は解除の文と付与の文になる）</param>
    /// <exception cref="InvalidOperationException">文で表せない項目（文に要るメンバーの無い項目）があるとき</exception>
    public static SyncPlan FromItems(params SchemaDiffItem[] items) => Build(items, [], []);

    /// <summary>表の作り直しの並びから計画を組む</summary>
    /// <param name="rebuilds">表の作り直し</param>
    public static SyncPlan FromRebuilds(params TableRebuildPlan[] rebuilds) =>
        Build([], rebuilds, []);

    /// <summary>表ごとの列順の計画の並びから計画を組む（移動 1 つにつき列を動かす文 1 つ）</summary>
    /// <param name="reorders">表ごとの列順の計画</param>
    public static SyncPlan FromReorders(params TableReorderPlan[] reorders) =>
        Build([], [], reorders);

    /// <summary>文の並びをそのまま計画にする（文の型ごとのメンバーへ値を直接入れる行が使う）</summary>
    /// <param name="steps">文（この順に並べる）</param>
    public static SyncPlan FromSteps(params SyncStep[] steps) => new() { Steps = steps };

    /// <summary>差分の項目・列順・作り直しから計画を組む（文は項目 → 列を動かす文 → 表を作り直す文の順）</summary>
    /// <param name="items">差分の項目</param>
    /// <param name="rebuilds">表の作り直し</param>
    /// <param name="reorders">表ごとの列順の計画</param>
    /// <exception cref="InvalidOperationException">文で表せない項目があるとき</exception>
    public static SyncPlan Build(
        IReadOnlyList<SchemaDiffItem> items,
        IReadOnlyList<TableRebuildPlan> rebuilds,
        IReadOnlyList<TableReorderPlan> reorders
    )
    {
        var (selected, unrepresentable) = SelectedSteps.From(items);

        if (selected is null)
        {
            throw new InvalidOperationException(
                $"手で組んだ項目を文で表せない: {unrepresentable}（項目に文の要るメンバーを足す）"
            );
        }

        List<SyncStep> steps = [.. selected.Select(s => s.Step)];

        foreach (var reorder in reorders)
        {
            StepOrigin origin =
                reorder.SourceItems.Count > 0
                    ? new SelectedOrigin(reorder.SourceItems[0])
                    : new MergedOrigin([]);

            steps.AddRange(
                reorder.Moves.Select(move => new MoveColumnStep(
                    origin,
                    reorder.TableName,
                    move.Column,
                    move.AfterColumn
                ))
            );
        }

        steps.AddRange(
            rebuilds.Select(rebuild => new RebuildTableStep(
                new MergedOrigin(rebuild.SourceItems),
                rebuild
            ))
        );

        return new SyncPlan { Steps = steps };
    }
}
