using System.Collections.Generic;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 組み立てた計画を評価して、項目を外さずに積む警告を返す規則
/// </summary>
/// <param name="assessment">計画の事実と、組み立ての結果</param>
/// <returns>警告の並び（積む順。無ければ空）</returns>
/// <remarks>
/// 規則は受け取ったものも警告を積む先も書き換えない。積むのは <see cref="AssessmentRules.Add"/> で、同じ値の警告を重ねるかは
/// 警告の種別で決まる。規則は同期後の事実を自分で求め直さず、差分の項目を作らない。規則の並びは規則の表
/// （<see cref="AssessmentRules.All"/>）が決める。
/// </remarks>
internal delegate IEnumerable<SyncPlanWarning> AssessmentRule(PlanAssessment assessment);
