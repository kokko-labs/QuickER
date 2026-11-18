using System.Collections.Generic;
using System.Linq;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 必ず断られる文（<see cref="Refusals.FindCertain"/>）を計画の先頭へ移す（DDL が暗黙コミットされる方言の前寄せ）
/// </summary>
/// <remarks>
/// 移す文を判定が決めた順に先頭へ置き、残りの文は元の順のまま続ける。先頭へ移した文の数は、文の並びの計画の
/// <see cref="StepPlan.MovedFirstCount"/> に入れる（計画の形へ写すと、先頭のその数の文が前寄せのセクションになる）。
/// </remarks>
internal static class MoveFirst
{
    /// <summary>判定が決めた文を先頭へ移した文の並びを返す（元の並びは変えない）</summary>
    /// <param name="steps">文の並び</param>
    /// <param name="refusal">必ず断られる文の判定</param>
    public static IReadOnlyList<SyncStep> Apply(
        IReadOnlyList<SyncStep> steps,
        CertainRefusal refusal
    )
    {
        var moved = refusal.Moved.ToHashSet();

        return
        [
            .. refusal.Moved.Select(index => steps[index]),
            .. steps.Where((_, index) => !moved.Contains(index)),
        ];
    }
}
