namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 計画器（<see cref="SyncPlanner"/>）が、同じ引数から新しい計画器（<see cref="SequentialPlanner"/>）が作った結果を置く入れ物。逐次 DDL の
/// 方言の計画を、今の計画器の最終の計画と新しい計画器の計画で比べるために使う
/// </summary>
/// <remarks>
/// 渡すのはテストだけ（製品のコードは作らない）。渡しても計画は変わらない。再構築の方言の計画では null のまま。
/// </remarks>
internal sealed class SyncPlanProbe
{
    /// <summary>新しい計画器が、同じ差分の項目・方言の宣言・計画の入力から作った結果</summary>
    public SequentialPlan? NewPlan { get; set; }
}
