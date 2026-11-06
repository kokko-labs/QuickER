using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

/// <summary>接続プロファイルの操作列の表（<see cref="ProfileStoreRows"/>）を流す</summary>
public sealed class ProfileStoreTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => ProfileStoreRows.Table.Ids();

    /// <summary>行の手の列を流す（各手の表明は元のテストのまま。一時ファイルの残りと暗号ファイルの組は全行／印付きの行に共通）</summary>
    [Theory(DisplayName = "OS との境界: 接続プロファイルの保存の表の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id)
    {
        using var temp = new TestTempFolder();
        ProfileStoreRows.Table[id].Input.Run(temp.Path);
    }

    /// <summary>全行を流して網羅を数え、網羅しない組の一覧と照合する</summary>
    /// <remarks>
    /// 行は自分の一時フォルダだけを使い（ロック・ACL もそのフォルダの中）、網羅の計数はスレッドセーフなので、行を並列に流す。
    /// ロック中の書き込みは AtomicFile の再試行を待つので、順に流すとこの Fact だけで行の数ぶんの待ちが積み重なる。
    /// </remarks>
    [Fact(DisplayName = "OS との境界: 接続プロファイルの保存の表の網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ProfileStoreDimensions.Table);

        Parallel.ForEach(
            ProfileStoreRows.Table,
            row =>
            {
                using var temp = new TestTempFolder();
                ProfileStoreDimensions.Observe(counter, row.Input.Run(temp.Path));
            }
        );

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(ProfileStoreDimensions.Gaps, ProfileStoreDimensions.Scope);
    }
}
