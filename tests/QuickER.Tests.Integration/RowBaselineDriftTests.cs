using System.IO;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// 新しい行の札と減るだけの写し（Integration の表）：写しに無い行は札を持ち、写しは今の行から消えた Id を落とすだけで書き直す
/// </summary>
/// <remarks>
/// 書き直し（<see cref="RegenerationRequest"/>）は写しのうち今もある Id だけを書く。札の問題は書き直しでも消えない（行を足して
/// 書き直しを流しても赤のまま）。照合の出力に「写しに無い行 N 本（台帳 M・理由 K）」と札の一覧を書く（review-gate が毎回読む）。
/// </remarks>
public sealed class RowBaselineDriftTests
{
    private static readonly string RegenerateSteps = RegenerationRequest.Command(
        ("tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj", "RowBaselineDrift")
    );

    [Fact(
        DisplayName = "[札] Integration の札の対象の表：写しに無い行は札を持ち、写しは消えた Id だけを落として書き直す"
    )]
    public void Baseline_MatchesTablesAndNewRowsAreTagged()
    {
        var path = Path.Combine(EngineRepository.Root(), RowBaselineTables.BaselinePath);
        var baseline = File.Exists(path)
            ? ScenarioRowBaseline.Parse(File.ReadAllText(path))
            : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var ledger = BranchAxisLedgerEntries
            .Items.Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var result = ScenarioRowBaseline.Check(RowBaselineTables.All(), baseline, ledger.Contains);
        TestContext.Current.TestOutputHelper?.WriteLine(result.Summary());

        var newExpectationRows = result
            .NewEntries.Where(entry => entry.Table == RowBaselineTables.ExpectationRows)
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        var problems = result
            .Problems.Concat(RowBaselineTables.NewExpectationRowDialectProblems(newExpectationRows))
            .ToList();

        // 写しのずれ（消えた Id）を先に照合し、書き直しの指定があれば消えた Id だけを落とす（写しに無い表・Id は書かない）
        try
        {
            GeneratedTextFile.Verify(
                path,
                ScenarioRowBaseline.Render(result.Kept),
                "Integration の札の対象の表の写し",
                RegenerateSteps
            );
        }
        catch (Xunit.Sdk.XunitException ex)
        {
            problems.Insert(0, ex.Message);
        }

        NotGeneratedGuard.Verify("札の対象の表の写しと新しい行の札に問題がある", problems);
    }
}
