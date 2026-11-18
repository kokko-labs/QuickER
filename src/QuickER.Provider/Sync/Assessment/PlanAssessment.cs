using System.Diagnostics.CodeAnalysis;
using QuickER.Provider.Sync.Scheduling;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 組み立てた計画を評価する規則が読むもの：計画の事実（<see cref="AssessmentFacts"/>）と、組み立ての結果
/// </summary>
/// <remarks>
/// 組み立ての結果を読まない規則は <see cref="AssessmentFacts"/> を受け、読む規則はこの型を受ける。規則の表
/// （<see cref="AssessmentRules.All"/>）の型はこの型を受ける関数で、どちらの規則もそのまま載る。
/// </remarks>
internal sealed class PlanAssessment : AssessmentFacts
{
    /// <summary>計画の事実に、組み立ての結果を添える</summary>
    /// <param name="facts">計画の事実</param>
    /// <param name="schedule">同じ事実から組み立てた結果</param>
    [SetsRequiredMembers]
    public PlanAssessment(AssessmentFacts facts, SequentialSchedule schedule)
    {
        Settled = facts.Settled;
        PostSync = facts.PostSync;
        Live = facts.Live;
        Capabilities = facts.Capabilities;
        Context = facts.Context;
        Schedule = schedule;
    }

    /// <summary>組み立ての結果（文の並び）</summary>
    public SequentialSchedule Schedule { get; }
}
