using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Extensibility;

/// <summary>機能モジュールの契約の表（<see cref="FeatureModuleContractRows"/>）を流す</summary>
public sealed class FeatureModuleContractTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => FeatureModuleContractRows.Table.Ids();

    /// <summary>1 行を流す</summary>
    [Theory(DisplayName = "機能モジュールの契約: 表の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id) =>
        FeatureModuleContractRows.Run(FeatureModuleContractRows.Table[id].Input);

    /// <summary>全行を流して網羅を数え、一覧と照合する</summary>
    [Fact(DisplayName = "機能モジュールの契約: 表の網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(FeatureModuleContractDimensions.Table);

        foreach (var row in FeatureModuleContractRows.Table)
        {
            if (FeatureModuleContractRows.Run(row.Input))
            {
                FeatureModuleContractDimensions.Observe(counter, row.Input);
            }
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(
            FeatureModuleContractDimensions.Gaps,
            FeatureModuleContractDimensions.Scope
        );
    }

    /// <summary>軸は、カタログの正本の値ごとにアダプタか理由つきの除外がちょうど 1 つある（いまは除外が無い）</summary>
    [Fact(
        DisplayName = "機能モジュールの軸: 正本の値ごとにアダプタか理由つきの除外がちょうど 1 つ"
    )]
    public void Axis_CoversCatalog() =>
        FeatureModuleAxis
            .Axis.Adapters.Select(adapter => adapter.AxisValue)
            .Concat(FeatureModuleAxis.Axis.Excluded.Keys)
            .Should()
            .BeEquivalentTo(FeatureModuleAxis.Names);
}
