using QuickER.Provider;
using QuickER.Provider.Sync.Steps;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 形の行（<see cref="SyncPlanShapeRows"/>）に結んだ分岐の軸の台帳の項目の、分岐に入った証拠の判定（名前で引く）と、能力の組の網の
/// 決定的な行の結果への照合
/// </summary>
/// <remarks>
/// 台帳（<see cref="BranchAxisLedgerEntries"/>）は純粋なデータで、証拠を名前でしか持たない。判定の関数はここに置き、網の方言の派生が
/// 決定的な行を流した結果（<see cref="FuzzSeedOutcome"/>）へ掛ける（DB なし）。
/// </remarks>
internal static class SyncPlanBranchEvidence
{
    /// <summary>形の行の台帳の部分の名前</summary>
    public static string TablePart { get; } =
        ProtectionNames.EnginePart(
            SyncPlanShapeRows.Descriptor.Id,
            SyncPlanShapeRows.Descriptor.Part
        );

    /// <summary>証拠の名前 → 判定（決定的な行の結果が分岐に入ったか）</summary>
    public static IReadOnlyDictionary<string, Func<FuzzSeedOutcome, bool>> ByName { get; } =
        new Dictionary<string, Func<FuzzSeedOutcome, bool>>(StringComparer.Ordinal)
        {
            // 前寄せのある計画（暗黙コミットの方言だけで出る＝製品の MoveFirst が文を先頭へ移した）
            ["moved-first-plan"] = outcome => outcome.Plan is { MovedFirstCount: > 0 },

            // 前寄せの先頭の文が、参照される親の列（形の行の p）を変える文（外し直しの方言で、子の端の受ける変更でなく断られる親の
            // 変更を移した＝項目 pe1）
            ["moved-first-parent-change"] = outcome =>
                outcome.Plan is { MovedFirstCount: > 0 } plan
                && plan.Steps[0] is AlterColumnStep { TableName: "p" },

            // 既存の行で断られうる文の前後の名指しが、u と x の一意制約の追加を挙げる（一部が NULL の重複を重なりとみなす方言で、
            // NULL 許容の列の一意制約を証拠に使わなかった＝項目 pa1）
            ["row-dependent-named-widened-unique"] = outcome =>
                outcome.Plan is { } plan
                && plan.Warnings.Any(w =>
                    w.Kind == SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange
                    && w.Detail.Contains("UNIQUE p (u, x)", StringComparison.Ordinal)
                ),

            // SET DEFAULT の外部キーの付け直しを、子の列が同期後に NOT NULL だと名指しする（暗黙の付け直しの腕＝項目 pe3）
            ["set-default-readd-warned"] = outcome =>
                outcome.Plan is { } plan
                && plan.Warnings.Any(w =>
                    w.Kind == SyncPlanWarningKind.SetDefaultOnNotNullColumn
                    && w.Detail.StartsWith("fk_c1_p ", StringComparison.Ordinal)
                ),
        };

    /// <summary>
    /// 方言の網の決定的な行の結果へ、形の行に結んだ台帳の項目を照合する。指摘のセルの行は範囲の方言で流れなければならず、
    /// 修正済みなら証拠が出る。結び済み・未修正なら、その行は違反を持ち、違反はその項目を指摘の名前に持つ既知の失敗の一覧の
    /// Pending（直す区切りが項目の直す区切りと同じ）に当たる。違反なしで流れたら赤（直ったのに状態を切り替えていない）
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="scenarioOutcomes">決定的な行の結果</param>
    /// <param name="items">台帳の項目（既定は <see cref="BranchAxisLedgerEntries.Items"/>）</param>
    /// <param name="known">既知の失敗の一覧の項目（既定は <see cref="SyncPlanCapabilityNetKnownFailures.Entries"/>）</param>
    public static IEnumerable<string> NetProblems(
        string dialect,
        IReadOnlyCollection<FuzzSeedOutcome> scenarioOutcomes,
        IReadOnlyList<BranchAxisItem>? items = null,
        IReadOnlyList<KnownEntry>? known = null
    )
    {
        var byId = scenarioOutcomes
            .Where(outcome => outcome.ScenarioId is not null)
            .ToDictionary(outcome => outcome.ScenarioId!, StringComparer.Ordinal);
        var entries = known ?? SyncPlanCapabilityNetKnownFailures.Entries;

        foreach (
            var item in (items ?? BranchAxisLedgerEntries.Items).Where(item =>
                item.Status is BranchAxisStatus.BoundUnfixed or BranchAxisStatus.BoundFixed
                && item.Binding is { } binding
                && string.Equals(binding.TablePart, TablePart, StringComparison.Ordinal)
                && binding.CellTargets.Contains(dialect, StringComparer.Ordinal)
            )
        )
        {
            var binding = item.Binding!;
            var rowId = SyncPlanShapeRows.RequiredRows.Any(row =>
                string.Equals(row.Id, binding.CellRow, StringComparison.Ordinal)
            )
                ? SyncPlanShapeRows.RequiredRowIdFor(dialect, binding.CellRow)
                : null;

            if (rowId is null)
            {
                yield return $"ledger {item.Id}: the finding's cell {binding.CellRow} is not a required shape row that {dialect} creates";
                continue;
            }

            if (!byId.TryGetValue(rowId, out var outcome))
            {
                yield return $"ledger {item.Id}: the finding's cell {rowId} did not run on {dialect}";
                continue;
            }

            if (item.Status == BranchAxisStatus.BoundUnfixed)
            {
                if (outcome.Violations.Count == 0)
                {
                    yield return $"ledger {item.Id}: the finding's cell {rowId} ran on {dialect} without violations while the item is bound but unfixed (switch the status to fixed)";
                    continue;
                }

                var failure = SyncPlanCapabilityNetKnownFailures.ToFailure(outcome);

                if (
                    !entries.Any(entry =>
                        entry.Kind == KnownKind.Pending
                        && string.Equals(entry.Finding, item.Id, StringComparison.Ordinal)
                        && string.Equals(entry.FixedIn, item.FixedIn, StringComparison.Ordinal)
                        && string.Equals(entry.Scope, failure.Scope, StringComparison.Ordinal)
                        && entry.Predicate?.Invoke(failure) == true
                    )
                )
                {
                    yield return $"ledger {item.Id}: the finding's cell {rowId} on {dialect} has violations that no pending known failure named {item.Id} (fixed in {item.FixedIn}) receives: {string.Join(" / ", outcome.Violations)}";
                }

                continue;
            }

            if (!ByName.TryGetValue(binding.Evidence, out var evidence))
            {
                yield return $"ledger {item.Id}: unknown evidence {binding.Evidence}";
                continue;
            }

            if (!evidence(outcome))
            {
                yield return $"ledger {item.Id}: the finding's cell {rowId} ran on {dialect} without the evidence {binding.Evidence} (the fixed branch was not entered)";
            }
        }
    }
}
