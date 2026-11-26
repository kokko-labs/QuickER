using System.IO;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 新しい行の札と減るだけの写し（QuickER.Tests の表）：写しに無い行は札を持ち、写しは今の行から消えた Id を落とすだけで書き直す
/// </summary>
/// <remarks>
/// 札の台帳の項目の Id は、Integration から逆向きにリンクした台帳（<c>BranchAxisLedgerEntries</c>）で引く。照合の出力に
/// 「写しに無い行 N 本（台帳 M・理由 K）」と札の一覧を書く。
/// </remarks>
public sealed class RowBaselineDriftTests
{
    private static readonly string RegenerateSteps = RegenerationRequest.Command(
        ("tests/QuickER.Tests/QuickER.Tests.csproj", "RowBaselineDrift")
    );

    [Fact(
        DisplayName = "[札] QuickER.Tests の札の対象の表：写しに無い行は札を持ち、写しは消えた Id だけを落として書き直す"
    )]
    public void Baseline_MatchesTablesAndNewRowsAreTagged()
    {
        var path = Path.Combine(EngineRepository.Root(), RowBaselineTables.BaselinePath);
        var baseline = File.Exists(path)
            ? ScenarioRowBaseline.Parse(File.ReadAllText(path))
            : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var ledger = QuickER
            .Tests.Integration.BranchAxisLedgerEntries.Items.Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var result = ScenarioRowBaseline.Check(RowBaselineTables.All(), baseline, ledger.Contains);
        TestContext.Current.TestOutputHelper?.WriteLine(result.Summary());
        var problems = result.Problems.ToList();

        try
        {
            GeneratedTextFile.Verify(
                path,
                ScenarioRowBaseline.Render(result.Kept),
                "QuickER.Tests の札の対象の表の写し",
                RegenerateSteps
            );
        }
        catch (XunitException ex)
        {
            problems.Insert(0, ex.Message);
        }

        NotGeneratedGuard.Verify("札の対象の表の写しと新しい行の札に問題がある", problems);
    }
}
