using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Settings;

/// <summary>設定 JSON ストアの操作列の表（<see cref="SettingsStoreRows"/>）を流す</summary>
public sealed class SettingsStoreTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => SettingsStoreRows.Table.Ids();

    /// <summary>行の手の列を流す（各手の表明は元のテストのまま）</summary>
    [Theory(DisplayName = "設定 JSON ストア: 操作列の表の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id)
    {
        using var temp = new TestTempFolder();
        SettingsStoreRows.Table[id].Input.Run(temp.Path);
    }

    /// <summary>全行を流して網羅を数え、網羅しない組の一覧と照合する</summary>
    [Fact(DisplayName = "設定 JSON ストア: 操作列の表の網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(SettingsStoreDimensions.Table);

        foreach (var row in SettingsStoreRows.Table)
        {
            using var temp = new TestTempFolder();
            SettingsStoreDimensions.Observe(counter, row.Input.Run(temp.Path));
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(SettingsStoreDimensions.Gaps, SettingsStoreDimensions.Scope);
    }
}
