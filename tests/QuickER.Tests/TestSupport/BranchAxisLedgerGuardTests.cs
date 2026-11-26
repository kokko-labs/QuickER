using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 分岐の軸の台帳（Integration の <c>BranchAxisLedgerEntries.cs</c> を逆向きにリンクしたもの）の QuickER.Tests 側の照合：
/// 構造と標準の軸への答え・結び済みの判定役の実在・結び済みの生成の表が影響の地図にあること
/// </summary>
/// <remarks>
/// 判定役は作らない形の宣言の受け持つ守りと同じ書き方で、同じ関数（<see cref="NotGeneratedGuardTests.GuardChecker"/>＝CLAUDE.md の照合の
/// 関数）で読む。予定の項目の判定役は文のままでよい（照合しない）。生成の表への結び（水準・指摘のセル・証拠）は Integration の
/// <c>BranchAxisLedgerTests</c> が見る。
/// </remarks>
public sealed class BranchAxisLedgerGuardTests
{
    [Fact(DisplayName = "[台帳] 分岐の軸の台帳と標準の軸の一覧は整っている")]
    public void Ledger_IsWellFormed() =>
        NotGeneratedGuard.Verify(
            "分岐の軸の台帳が整っていない",
            BranchAxisLedgerChecks.Problems(
                BranchAxisLedgerEntries.Items,
                BranchAxisLedgerEntries.Areas
            )
        );

    [Fact(
        DisplayName = "[台帳] 結び済みの項目の判定役は実在する名前で書き、生成の表は影響の地図の守りにある"
    )]
    public void BoundItems_ReferToExistingProtections() =>
        NotGeneratedGuard.Verify(
            "分岐の軸の台帳の結び済みの項目が実在しないものを指す",
            BoundItemProblems(BranchAxisLedgerEntries.Items, NotGeneratedGuardTests.GuardChecker())
        );

    /// <summary>結び済みの項目の判定役と生成の表の問題</summary>
    /// <param name="items">台帳の項目</param>
    /// <param name="guardProblems">判定役の本文 → 照合の問題</param>
    internal static IReadOnlyList<string> BoundItemProblems(
        IReadOnlyList<BranchAxisItem> items,
        Func<string, IReadOnlyList<string>> guardProblems
    )
    {
        var names = ImpactMapSources
            .Protections(EngineRepository.Root())
            .Select(protection => protection.Name)
            .ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var item in items.Where(item => item.Status != BranchAxisStatus.Planned))
        {
            if (!string.IsNullOrWhiteSpace(item.Judge))
            {
                problems.AddRange(
                    guardProblems(item.Judge)
                        .Select(problem => $"項目 {item.Id} の判定役: {problem}")
                );
            }

            if (item.Binding is { } binding && !names.Contains(binding.TablePart))
            {
                problems.Add(
                    $"項目 {item.Id}: 生成の表 {binding.TablePart} が影響の地図の守りに無い"
                );
            }
        }

        return problems;
    }
}
