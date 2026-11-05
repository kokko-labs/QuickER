using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>形式の往復のエンジン（カルチャの代表の形・網羅・軸と宣言のガード。形の行・規則の行・マージの行は FormatRoundTripCaseTests.cs のクラス）</summary>
public sealed class FormatRoundTripTests
{
    /// <summary>カルチャの代表の形の行 × 形式</summary>
    public static TheoryData<string, string> CultureCases()
    {
        var data = new TheoryData<string, string>();

        foreach (var row in FormatShapeRows.Table.Where(row => row.Input.CultureProbe))
        {
            foreach (var adapter in FormatAxis.Axis.Adapters)
            {
                data.Add(row.Id, adapter.AxisValue);
            }
        }

        return data;
    }

    [Theory(
        DisplayName = "カルチャの代表の形 × 形式：出力と取込の結果は書式のカルチャに依らずインバリアントと同じ"
    )]
    [MemberData(nameof(CultureCases))]
    public void CultureProbe_IsInvariant(string id, string format)
    {
        // UI のカルチャだけをエンジンの値に置く（CheckCulture は書式のカルチャを動かして比べる）
        var problems = FormatUiCulture.Run(() =>
            FormatRoundTripRunner.CheckCulture(
                FormatAxis.Axis.Get(format),
                FormatShapeRows.Table[id].Input
            )
        );

        Assert.True(problems.Count == 0, $"{id} × {format}:\n  " + string.Join("\n  ", problems));
    }

    [Fact(
        DisplayName = "網羅：形式 × 図の特徴・告知する形式 × 告知の種類・形式 × 規則の種類・マージの経路 × 出どころ"
    )]
    public void Coverage_MatchesGaps()
    {
        var roundTrip = new CoverageCounter(FormatRoundTripDimensions.RoundTripTable);
        var omissions = new CoverageCounter(FormatRoundTripDimensions.OmissionTable);
        var rules = new CoverageCounter(FormatRoundTripDimensions.RuleTable);
        var merges = new CoverageCounter(FormatRoundTripDimensions.MergeTable);

        foreach (var row in FormatMergeRows.Table)
        {
            FormatRoundTripDimensions.ObserveMerge(merges, row.Input);
        }

        foreach (var row in FormatShapeRows.Table)
        {
            foreach (var adapter in FormatAxis.Axis.Adapters)
            {
                var diagram = row.Input.Build();
                FormatRoundTripDimensions.ObserveShape(roundTrip, adapter.AxisValue, diagram);

                if (adapter.DetectOmissions(diagram) is { } kinds)
                {
                    FormatRoundTripDimensions.ObserveOmissions(omissions, adapter.AxisValue, kinds);
                }
            }
        }

        foreach (var row in FormatRuleRows.Table)
        {
            FormatRoundTripDimensions.ObserveRule(rules, row.Input.Format, row.Input.Kind);
        }

        var scope = FormatRoundTripDimensions.Scope;
        roundTrip.Report().AssertGaps(FormatRoundTripDimensions.RoundTripGaps, scope);
        omissions.Report().AssertGaps(FormatRoundTripDimensions.OmissionGaps, scope);
        rules.Report().AssertGaps(FormatRoundTripDimensions.RuleGaps, scope);
        merges.Report().AssertGaps(FormatRoundTripDimensions.MergeGaps, scope);
    }

    /// <summary>機械の UI のカルチャ × 既知の失敗の台帳のケースと、断り方・警告を期待する規則の行</summary>
    public static TheoryData<string, string> MachineUiCultureCases()
    {
        var data = new TheoryData<string, string>();
        var ruleIds = FormatRuleRows
            .Table.Where(row =>
                row.Input.Expectation.Rejects is not null || !row.Input.Expectation.Warnings.IsEmpty
            )
            .Select(row => "rule:" + row.Id)
            .ToList();

        foreach (var culture in new[] { "ja-JP", "en-US" })
        {
            foreach (
                var id in FormatRoundTripKnownFailures
                    .Ledger.Ids.Order(StringComparer.Ordinal)
                    .Concat(ruleIds)
            )
            {
                data.Add(culture, id);
            }
        }

        return data;
    }

    [Theory(
        DisplayName = "機械の UI のカルチャ（ja-JP・en-US）に依らず、既知の失敗の台帳と規則の行の断り方・警告の照合が一致する"
    )]
    [MemberData(nameof(MachineUiCultureCases))]
    public void KnownComparisons_DoNotDependOnMachineUiCulture(string culture, string caseId)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            if (caseId.StartsWith("rule:", StringComparison.Ordinal))
            {
                FormatRuleTests.RunRuleCase(caseId["rule:".Length..]);
            }
            else
            {
                var separator = caseId.LastIndexOf('|');
                FormatShapeTestsBase.RunShapeCase(caseId[..separator], caseId[(separator + 1)..]);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact(
        DisplayName = "形の行のテストのクラスは、形式の軸のアダプタを持つ値ごとにちょうど 1 つあり、名乗る値で流す"
    )]
    public void ShapeTestClasses_CoverFormatAxis()
    {
        MatrixAxisGuard.DerivedClassesCover(
            typeof(FormatShapeTestsBase),
            FormatRoundTripDimensions.Format,
            FormatAxis.Names,
            FormatAxis.Excluded
        );
        MatrixAxisGuard.DeclaredValuesMatch(
            typeof(FormatShapeTestsBase),
            FormatRoundTripDimensions.Format,
            type => ((FormatShapeTestsBase)Activator.CreateInstance(type)!).Format
        );
    }

    [Fact(DisplayName = "形式の軸：正本の値ごとにアダプタか理由つきの除外がちょうど 1 つ")]
    public void Axis_CoversAuthoritativeValues() =>
        FormatAxis
            .Axis.Adapters.Select(adapter => adapter.AxisValue)
            .Concat(FormatAxis.Axis.Excluded.Keys)
            .Should()
            .BeEquivalentTo(FormatAxis.Names);

    [Fact(
        DisplayName = "取込ができる形式は、製品の取込の形式の列挙と C# リバースにちょうど一致する"
    )]
    public void ImportableAdapters_MatchProductImportFormats()
    {
        FormatAxis
            .Axis.Adapters.Where(adapter => adapter.CanImport)
            .Select(adapter => adapter.AxisValue)
            .Should()
            .BeEquivalentTo(FormatAxis.ImportableNames);
    }

    [Fact(DisplayName = "告知の種類はどれも、説明できる面を 1 つ以上持つ")]
    public void FacetMap_CoversEveryOmissionKind()
    {
        Enum.GetValues<ExportOmissionKind>()
            .Where(kind =>
                !FacetKinds.OmissionFacets.TryGetValue(kind, out var facets) || facets.Count == 0
            )
            .Should()
            .BeEmpty();
        FacetKinds
            .OmissionFacets.Values.SelectMany(facets => facets)
            .Should()
            .NotContain(Facet.Structure, "表・列の増減は告知の対象でない");
    }

    [Fact(
        DisplayName = "運ばない面の宣言は告知で説明できない面だけで、根拠の出どころのファイルが実在する"
    )]
    public void NotCarriedDeclarations_HaveEvidence()
    {
        var root = EngineRepository.Root();
        var problems = new List<string>();

        foreach (var adapter in FormatAxis.Axis.Adapters)
        {
            foreach (var item in adapter.NotCarried)
            {
                // 形式が告知しうる種類が説明できる面は、宣言で黙らせない（告知が無ければ取りこぼしとして赤にする）
                if (
                    adapter.ReportableOmissions.Any(kind =>
                        FacetKinds.OmissionFacets[kind].Contains(item.Facet)
                    )
                )
                {
                    problems.Add(
                        $"{adapter.AxisValue}: 告知する形式が、告知で説明できる面 {item.Facet} を運ばないと宣言している"
                    );
                }

                var path = item.Evidence.Split('：', '「', '（', ' ')[0];

                if (!path.Contains('/') || !File.Exists(Path.Combine(root, path)))
                {
                    problems.Add($"{adapter.AxisValue}: {item.Facet} の根拠 {path} が実在しない");
                }

                if (item.Facet == Facet.Structure)
                {
                    problems.Add($"{adapter.AxisValue}: 表・列そのものを運ばないとは宣言できない");
                }
            }

            foreach (var item in adapter.Reinferred)
            {
                var path = item.Evidence.Split('：', '「', '（', ' ')[0];

                if (!path.Contains('/') || !File.Exists(Path.Combine(root, path)))
                {
                    problems.Add($"{adapter.AxisValue}: {item.Kind} の根拠 {path} が実在しない");
                }

                if (!adapter.ReportableOmissions.Contains(item.Kind))
                {
                    problems.Add($"{adapter.AxisValue}: 推論し直す {item.Kind} を告知しない");
                }
            }

            if (!adapter.MergeRestores.IsSubsetOf(adapter.NotCarriedFacets))
            {
                problems.Add($"{adapter.AxisValue}: マージが戻す面に、運ぶ面が混ざっている");
            }
        }

        problems.Should().BeEmpty();
    }
}
