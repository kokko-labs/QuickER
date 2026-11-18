using System.Collections.Generic;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 計画器（<see cref="SyncPlanner"/>）の途中の結果を受け取る読み取り専用の入れ物。逐次 DDL の方言の計画を、今の計画器と
/// 新しい計画器（<see cref="SequentialPlanner"/>）で比べるために使う
/// </summary>
/// <remarks>
/// 渡すのはテストだけ（製品のコードは作らない）。渡しても計画は変わらない。再構築の方言の計画では、どのメンバーも null のまま。
/// </remarks>
internal sealed class SyncPlanProbe
{
    /// <summary>
    /// 今の計画器が組み立てたセクション（種類の固定の順に並べ、DDL が暗黙コミットされる方言では外部キーの追加を並べ終えた後。
    /// 必ず断られる文を先頭へ移す前）
    /// </summary>
    public IReadOnlyList<SyncPlanSection>? ScheduledSections { get; set; }

    /// <summary>今の計画器が、必ず断られる文を先頭へ移す前までに積んだ警告（積んだ順）</summary>
    public IReadOnlyList<SyncPlanWarning>? ScheduledWarnings { get; set; }

    /// <summary>今の計画器が、必ず断られる文を先頭へ移す前までに外した項目（外した順）</summary>
    public IReadOnlyList<SchemaDiffItem>? ScheduledExcludedItems { get; set; }

    /// <summary>新しい計画器が、同じ差分の項目・方言の宣言・計画の入力から作った結果</summary>
    public SequentialPlan? NewPlan { get; set; }
}
