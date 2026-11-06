using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI.ImportSync;

/// <summary>
/// DB 取込・DB 同期のコマンドと同期のダイアログの操作の列の表（図・接続の答え・取込と実行の結果・確認の答え × 手の列 →
/// 図の置き換え・通知・差分・スクリプト・閉じ方）と、どの手の後にも掛ける不変条件
/// </summary>
public sealed class ImportSyncTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => ImportSyncRows.Table.Ids();

    [Theory(DisplayName = "取込と同期の表：手の列の後の状態が期待どおりで、不変条件を破らない")]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var row = ImportSyncRows.Table[id];
        var harness = new ImportSyncHarness();

        await row.Input.Body(harness);
        await harness.FinishAsync();
    }

    [Fact(DisplayName = "網羅：取込と同期の状態 × 手の種類")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ImportSyncDimensions.Table);

        foreach (var row in ImportSyncRows.Table)
        {
            foreach (var (state, op) in row.Input.Touches)
            {
                ImportSyncDimensions.Observe(counter, state, op);
            }
        }

        counter.Report().AssertGaps(ImportSyncDimensions.Gaps, ImportSyncDimensions.Scope);
    }
}
