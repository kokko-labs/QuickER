using System.Reflection;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// 分岐の軸の台帳（<see cref="BranchAxisLedgerEntries"/>）の Integration 側の照合（DB なし）：構造・標準の軸への答え・根拠の実在と、
/// 結び済みの項目の生成の表への結び（部分・水準・指摘のセル・証拠の名前・水準が出る方言）
/// </summary>
/// <remarks>
/// 判定役の実在と影響の地図の名前は QuickER.Tests の <c>BranchAxisLedgerGuardTests</c> が見る（CLAUDE.md の照合の関数がそこにだけある）。
/// 形の行の証拠そのもの（指摘のセルが流れて分岐に入ったか）は、能力の組の網の方言の派生が決定的な行の結果に掛ける
/// （<see cref="SyncPlanBranchEvidence.NetProblems"/>）。
/// </remarks>
public sealed class BranchAxisLedgerTests
{
    /// <summary>結べる生成の表（台帳の部分の名前 → 次元表・必ず含む行・方言ごとの水準の出方・方言の一覧・証拠の名前）</summary>
    private static IReadOnlyDictionary<string, BoundTable> BoundTables { get; } =
        new Dictionary<string, BoundTable>(StringComparer.Ordinal)
        {
            [SyncPlanBranchEvidence.TablePart] = new(
                SyncPlanShapeRows.Table,
                [.. SyncPlanShapeRows.RequiredRows.Select(row => row.Id)],
                DialectNames.All,
                SyncPlanShapeRows.CreatesLevels,
                SyncPlanBranchEvidence.ByName.Keys.ToHashSet(StringComparer.Ordinal)
            ),
        };

    [Fact(DisplayName = "[台帳] 台帳と標準の軸の一覧は整い、根拠は実在する")]
    public void Ledger_IsWellFormed()
    {
        var items = BranchAxisLedgerEntries.Items;
        var problems = BranchAxisLedgerChecks
            .Problems(items, BranchAxisLedgerEntries.Areas)
            .Concat(BranchAxisLedgerChecks.BasisProblems(items, BasisExists))
            .ToList();

        NotGeneratedGuard.Verify("分岐の軸の台帳が整っていない", problems);
    }

    [Fact(
        DisplayName = "[台帳] 結び済みの項目は、登録簿の生成の表の水準・必ず含む行・証拠の名前に結び、水準が出ない方言を理由と根拠つきで書く"
    )]
    public void BoundItems_ReferToGeneratedTables()
    {
        var output = TestContext.Current.TestOutputHelper;
        var problems = BindingProblems(BranchAxisLedgerEntries.Items, output).ToList();

        NotGeneratedGuard.Verify("分岐の軸の台帳の結びが生成の表と合わない", problems);
    }

    /// <summary>結び済みの項目の結びの問題（水準が出る方言を出力へ書く）</summary>
    internal static IEnumerable<string> BindingProblems(
        IReadOnlyList<BranchAxisItem> items,
        ITestOutputHelper? output
    )
    {
        var registered = EngineRegistry
            .All.Select(descriptor => ProtectionNames.EnginePart(descriptor.Id, descriptor.Part))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var item in items.Where(item => item?.Binding is not null))
        {
            var binding = item.Binding!;

            if (!registered.Contains(binding.TablePart))
            {
                yield return $"項目 {item.Id}: 生成の表 {binding.TablePart} が Integration の登録簿に無い";
                continue;
            }

            if (!BoundTables.TryGetValue(binding.TablePart, out var table))
            {
                yield return $"項目 {item.Id}: 生成の表 {binding.TablePart} は台帳に結べる表でない（{nameof(BoundTables)} に足す）";
                continue;
            }

            var levelsOk = true;

            foreach (var level in binding.Levels)
            {
                if (
                    table.Dimensions.IndexOf(level.Factor) < 0
                    || !table
                        .Dimensions.Factor(level.Factor)
                        .Levels.Any(candidate => candidate.Name == level.Level)
                )
                {
                    levelsOk = false;
                    yield return $"項目 {item.Id}: 入力の水準 {level.Factor}={level.Level} が {binding.TablePart} の次元表に無い";
                }
            }

            if (!table.RequiredRows.Contains(binding.CellRow, StringComparer.Ordinal))
            {
                yield return $"項目 {item.Id}: 指摘のセル {binding.CellRow} が {binding.TablePart} の必ず含む行に無い";
            }

            if (!table.Evidence.Contains(binding.Evidence))
            {
                yield return $"項目 {item.Id}: 証拠 {binding.Evidence} が {binding.TablePart} の証拠の表に無い";
            }

            foreach (
                var target in binding.CellTargets.Where(target =>
                    !table.Targets.Contains(target, StringComparer.Ordinal)
                )
            )
            {
                yield return $"項目 {item.Id}: 指摘のセルの範囲の {target} は {binding.TablePart} の方言・実装先でない";
            }

            if (!levelsOk)
            {
                continue;
            }

            var levels = binding.Levels.ToDictionary(
                level => level.Factor,
                level => level.Level,
                StringComparer.Ordinal
            );
            var present = table
                .Targets.Where(target => table.CreatesLevels(target, levels))
                .ToList();
            output?.WriteLine($"ledger {item.Id}: levels appear on {string.Join(", ", present)}");
            var absent = binding
                .AbsentTargets.Select(target => target.Target)
                .ToHashSet(StringComparer.Ordinal);

            foreach (
                var target in table.Targets.Where(target =>
                    !present.Contains(target) && !absent.Contains(target)
                )
            )
            {
                yield return $"項目 {item.Id}: 水準が {target} に出ない（水準が出ない方言・実装先に理由と根拠を書く）";
            }

            foreach (var target in absent.Where(present.Contains))
            {
                yield return $"項目 {item.Id}: 水準が出る {target} を水準が出ないと書いている";
            }
        }
    }

    /// <summary>根拠の実在（相対パスと「見出し」はファイルの見出しの行、型.メンバーは製品と Integration の型のメンバー）</summary>
    internal static bool BasisExists(string basis)
    {
        var (path, heading, type, member) = BranchAxisLedgerChecks.ParseBasis(basis);

        if (path is not null && heading is not null)
        {
            return BranchAxisLedgerChecks.HeadingExists(EngineRepository.Root(), path, heading);
        }

        if (type is null || member is null)
        {
            return false;
        }

        return new[]
        {
            typeof(SyncDialectCapabilities).Assembly,
            typeof(DialectNames).Assembly,
            typeof(BranchAxisLedgerTests).Assembly,
        }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(candidate => candidate.Name == type)
            .Any(candidate =>
                candidate
                    .GetMember(
                        member,
                        BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.Instance
                            | BindingFlags.Static
                    )
                    .Length > 0
            );
    }

    /// <summary>台帳に結べる生成の表</summary>
    private sealed record BoundTable(
        DimensionTable Dimensions,
        IReadOnlyList<string> RequiredRows,
        IReadOnlyList<string> Targets,
        Func<string, IReadOnlyDictionary<string, string>, bool> CreatesLevels,
        IReadOnlySet<string> Evidence
    );
}
