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
        };

    /// <summary>
    /// 方言の網の決定的な行の結果へ、形の行に結んだ台帳の項目を照合する：修正済みなら範囲の方言で指摘のセルの行が流れて証拠が出る。
    /// 結び済み・未修正の項目の指摘のセルの行が流れていたら赤（状態の切り替え忘れ）
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="scenarioOutcomes">決定的な行の結果</param>
    /// <param name="items">台帳の項目（既定は <see cref="BranchAxisLedgerEntries.Items"/>）</param>
    public static IEnumerable<string> NetProblems(
        string dialect,
        IReadOnlyCollection<FuzzSeedOutcome> scenarioOutcomes,
        IReadOnlyList<BranchAxisItem>? items = null
    )
    {
        var byId = scenarioOutcomes
            .Where(outcome => outcome.ScenarioId is not null)
            .ToDictionary(outcome => outcome.ScenarioId!, StringComparer.Ordinal);

        foreach (
            var item in (items ?? BranchAxisLedgerEntries.Items).Where(item =>
                item.Binding is { } binding
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

            if (item.Status == BranchAxisStatus.BoundUnfixed)
            {
                if (rowId is not null && byId.ContainsKey(rowId))
                {
                    yield return $"ledger {item.Id}: the finding's cell {rowId} ran on {dialect} while the item is bound but unfixed (switch the status to fixed)";
                }

                continue;
            }

            if (item.Status != BranchAxisStatus.BoundFixed)
            {
                continue;
            }

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
