using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表を当てる（1 行 1 規則）・自己検査の行・表の整合・網羅の照合</summary>
/// <remarks>
/// 規則の宣言は <see cref="SourceRules"/>、自己検査の行は <see cref="SourceRuleProbes"/>、1 回の走査は <see cref="GuardScan"/>、
/// 次元表は <see cref="GuardDimensions"/>。
/// </remarks>
public sealed class SourceRuleTests(ITestOutputHelper output)
{
    /// <summary>規則の Id</summary>
    public static TheoryData<string> RuleIds => [.. SourceRules.All.Select(rule => rule.Id)];

    /// <summary>自己検査の行の Id</summary>
    public static TheoryData<string> ProbeIds =>
        [.. SourceRuleProbes.All.Select(probe => probe.Id)];

    [Theory(DisplayName = "ソースの規則：見つけた出現は既知の一覧と件数まで一致する")]
    [MemberData(nameof(RuleIds))]
    public void Rule_Holds(string id)
    {
        var rule = SourceRules.ById(id);
        var outcome = GuardScan.For(rule);

        outcome
            .FileCount.Should()
            .BeGreaterThanOrEqualTo(
                rule.MinimumFiles,
                $"[{rule.Id}] 範囲 {rule.Scope} に入るファイルが少なすぎる（範囲の書き間違いで空振りしていないか）"
            );

        AssertHits(
            outcome.Hits,
            rule.Known,
            rule.Expectation == GuardExpectation.Forbidden
                ? $"[{rule.Id}] {rule.Because}（一覧に無い出現。正当な出現なら理由を添えて既知の一覧へ）"
                : $"[{rule.Id}] 必須の出現の件数が違うか、宣言に無い出現がある（{rule.Because}）",
            rule.Expectation == GuardExpectation.Forbidden
                ? $"[{rule.Id}] 一覧にあるのに無い出現（件数の違いを含む）は一覧から消す"
                : $"[{rule.Id}] {rule.Because}（必須の出現が無い）"
        );

        if (rule.Witness is { } witness)
        {
            AssertHits(
                outcome.WitnessHits,
                witness.Expected,
                $"[{rule.Id}] 空振りの検知の範囲 {witness.Scope} で、期待に無い出現を拾った",
                $"[{rule.Id}] 空振りの検知の範囲 {witness.Scope} で、拾えるはずの出現を拾えない（走査が空振りしている）"
            );
        }

        if (rule.Floor is { } floor)
        {
            outcome
                .FloorCount.Should()
                .BeGreaterThanOrEqualTo(
                    floor.AtLeast,
                    $"[{rule.Id}] 範囲の中の{floor.What}が少なすぎる（走査が候補を見つけられず空振りしていないか）"
                );
        }
    }

    [Theory(DisplayName = "ソースの規則の自己検査：合成のソースで拾う出現が並びまで一致する")]
    [MemberData(nameof(ProbeIds))]
    public void Probe_Holds(string id)
    {
        var probe = SourceRuleProbes.ById(id);
        var rule = SourceRules.ById(probe.RuleId);
        var cancellationToken = TestContext.Current.CancellationToken;

        if (probe.InactiveBranch)
        {
            probe
                .Files.Select(file =>
                    ConditionalSourceParse
                        .ConditionalSymbols(
                            ConditionalSourceParse.Parse(
                                file.Text,
                                cancellationToken: cancellationToken
                            )[0],
                            cancellationToken
                        )
                        .Count
                )
                .Should()
                .Contain(count => count > 0, "外れる分岐の行は、条件コンパイルを持つソースを含む");
        }

        var actual = rule.Finder is SymbolFinder symbols
            ? ProbeSymbols(probe, symbols, cancellationToken)
            : probe.Files.ToDictionary(
                file => file.Path,
                file =>
                    GuardScan
                        .Find(rule.Finder, new GuardFile(file.Path, file.Text))
                        .Select(finding => finding.What)
                        .ToList()
            );

        foreach (var file in probe.Files)
        {
            actual
                .GetValueOrDefault(file.Path, [])
                .Should()
                .Equal(file.Expected, $"[{probe.Id}] {file.Path} で拾う出現の並び");
        }
    }

    [Fact(DisplayName = "ソースの規則の表：Id・理由・件数・範囲の除外・自己検査の行が整っている")]
    public void Table_IsWellFormed()
    {
        var problems = new List<string>();

        problems.AddRange(
            SourceRules
                .All.GroupBy(rule => rule.Id)
                .Where(group => group.Count() > 1)
                .Select(group => $"規則の Id が重複: {group.Key}")
        );
        problems.AddRange(
            SourceRuleProbes
                .All.GroupBy(probe => probe.Id)
                .Where(group => group.Count() > 1)
                .Select(group => $"自己検査の行の Id が重複: {group.Key}")
        );
        problems.AddRange(
            SourceRuleProbes
                .All.Where(probe => SourceRules.All.All(rule => rule.Id != probe.RuleId))
                .Select(probe => $"{probe.Id}: 規則 {probe.RuleId} が無い")
        );

        foreach (var rule in SourceRules.All)
        {
            if (string.IsNullOrWhiteSpace(rule.Because))
            {
                problems.Add($"{rule.Id}: 守ることが空");
            }

            if (rule.Expectation == GuardExpectation.Required && rule.Known.Count == 0)
            {
                problems.Add($"{rule.Id}: 必須の規則にあるべき出現が無い");
            }

            problems.AddRange(KnownProblems(rule.Id, rule.Known));

            if (rule.Witness is { } witness)
            {
                if (witness.Expected.Count == 0)
                {
                    problems.Add($"{rule.Id}: 空振りの検知の期待が空");
                }

                problems.AddRange(KnownProblems(rule.Id + "（空振りの検知）", witness.Expected));
            }

            if (rule.Floor is { } floor)
            {
                if (floor.AtLeast < 1 || string.IsNullOrWhiteSpace(floor.What))
                {
                    problems.Add($"{rule.Id}: 候補の数の下限が 1 未満か、候補の名前が空");
                }

                if ((floor.Candidates is SymbolFinder) != (rule.Finder is SymbolFinder))
                {
                    problems.Add(
                        $"{rule.Id}: 候補を見つける形と規則の見つける形の読み方の種類（シンボルかファイル）が違う"
                    );
                }
            }

            problems.AddRange(
                rule.Scope.Exclusions.Where(exclusion =>
                        string.IsNullOrWhiteSpace(exclusion.Reason)
                    )
                    .Select(exclusion => $"{rule.Id}: 範囲の除外 {exclusion.AreaPrefix} の理由が空")
            );
        }

        problems.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "ソースの規則の走査の土台：どのプロジェクトでも BCL の呼び出しを解決できている"
    )]
    public void SymbolScan_ResolvesBclCallsInEveryProject()
    {
        SourceSymbolScan
            .SrcProjects.Where(project => project.ResolvedBclInvocationCount == 0)
            .Select(project => project.Name)
            .Should()
            .BeEmpty("参照の組み立てが壊れると、すべての呼び出しが未解決になって走査が空振りする");
        SourceSymbolScan
            .SrcProjects.SelectMany(project =>
                project
                    .Variants.Where(variant => variant.ResolvedBclInvocationCount == 0)
                    .Select(variant => $"{project.Name} [{variant.Symbols}]")
            )
            .Should()
            .BeEmpty(
                "条件コンパイルの変種のコンパイルも、差し替えた構文木の BCL の呼び出しを解決できる"
            );
        SourceSymbolScan.SrcProjects.Should().HaveCountGreaterThan(30);
        output.WriteLine(
            $"projects={SourceSymbolScan.SrcProjects.Count} load={SourceSymbolScan.LoadElapsed.TotalSeconds:F1}s"
        );
    }

    [Fact(
        DisplayName = "[網羅] ソースの規則の自己検査は、読み方 × 照合の形 × 置き場の組を網羅する"
    )]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(GuardDimensions.Table);

        foreach (var probe in SourceRuleProbes.All)
        {
            GuardDimensions.Observe(counter, probe);
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(GuardDimensions.Gaps, GuardDimensions.Scope);
    }

    /// <summary>見つけた出現を、ファイル・何かのキーごとの件数にして既知の一覧と照合する</summary>
    private static void AssertHits(
        IReadOnlyList<GuardHit> hits,
        IReadOnlyList<GuardKnown> known,
        string unexpectedBecause,
        string staleBecause
    )
    {
        var found = hits.GroupBy(hit => (hit.File, hit.What))
            .ToDictionary(group => group.Key, group => group.Count());
        var lines = hits.ToLookup(hit => (hit.File, hit.What), hit => hit.Line);

        GuardKnownList.AssertMatches(
            found,
            known.Select(entry => ((entry.File, entry.What), entry.Count)),
            (key, count) => $"{key.File}: {key.What} x{count}",
            unexpectedBecause,
            staleBecause,
            (key, count) =>
                $"{key.File}: {key.What} x{count}（行 {string.Join(", ", lines[key])}）",
            sortLines: true
        );
    }

    /// <summary>既知の一覧の項目の問題（理由・件数・重複）</summary>
    private static IEnumerable<string> KnownProblems(string id, IReadOnlyList<GuardKnown> known)
    {
        foreach (var entry in known)
        {
            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                yield return $"{id}: {entry.File}: {entry.What} の理由が空";
            }

            if (entry.Count < 1)
            {
                yield return $"{id}: {entry.File}: {entry.What} の件数が 1 未満";
            }
        }

        foreach (
            var duplicate in known
                .GroupBy(entry => (entry.File, entry.What))
                .Where(group => group.Count() > 1)
        )
        {
            yield return $"{id}: {duplicate.Key.File}: {duplicate.Key.What} が重複";
        }
    }

    /// <summary>シンボルの規則の自己検査：合成のソースを 1 プロジェクトとしてコンパイルして当てる</summary>
    private static Dictionary<string, List<string>> ProbeSymbols(
        SourceRuleProbe probe,
        SymbolFinder finder,
        CancellationToken cancellationToken
    )
    {
        var project = SourceSymbolScan.CompileProbeProject(
            "QuickER.Tests.Probe",
            [.. probe.Files.Select(file => (file.Path, file.Text))]
        );

        project
            .Compilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                && !probe.IgnoredDiagnostics.Contains(diagnostic.Id)
            )
            .Should()
            .BeEmpty($"[{probe.Id}] 合成のソースはコンパイルできる");

        return project
            .ScanNodes(cancellationToken)
            .Select(item => (item.File.RelativePath, What: finder.Describe(item.Model, item.Node)))
            .Where(item => item.What is not null)
            .GroupBy(item => item.RelativePath)
            .ToDictionary(group => group.Key, group => group.Select(item => item.What!).ToList());
    }
}
