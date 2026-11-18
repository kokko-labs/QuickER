using System.Collections.Generic;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 計画器（<see cref="SyncPlanner"/>）の途中の結果を受け取る読み取り専用の入れ物。逐次 DDL の方言の組み立てを、今の計画器と
/// 新しい組み立て（<see cref="SequentialScheduler"/>）で比べるために使う
/// </summary>
/// <remarks>
/// 渡すのはテストだけ（製品のコードは作らない）。渡しても計画は変わらない。再構築の方言の計画では、どちらのメンバーも null のまま。
/// </remarks>
internal sealed class SyncPlanProbe
{
    /// <summary>
    /// 今の計画器が組み立てたセクション（種類の固定の順に並べ、DDL が暗黙コミットされる方言では外部キーの追加を並べ終えた後。
    /// 必ず断られる文を先頭へ移す前）
    /// </summary>
    public IReadOnlyList<SyncPlanSection>? ScheduledSections { get; set; }

    /// <summary>新しい組み立てが、同じ同期後の事実と DB の今の図から組み立てた結果</summary>
    public SequentialSchedule? Schedule { get; set; }
}
