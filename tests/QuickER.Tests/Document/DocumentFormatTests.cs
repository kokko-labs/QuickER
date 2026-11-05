using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

/// <summary>図ファイルの形式の表（<see cref="DocumentFormatRows"/>）を流す</summary>
public sealed class DocumentFormatTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => DocumentFormatRows.Table.Ids();

    /// <summary>行を流す（宣言した結果の種類と元のテストの表明）</summary>
    [Theory(DisplayName = "図ファイルの形式: 表の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id) => DocumentFormatRunner.Run(DocumentFormatRows.Table[id]);

    /// <summary>行の宣言の網羅を数え、網羅しない組の一覧と照合する</summary>
    [Fact(DisplayName = "図ファイルの形式: 表の網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(DocumentFormatDimensions.Table);
        DocumentFormatDimensions.ObserveDeclarations(counter);
        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(DocumentFormatDimensions.Gaps, DocumentFormatDimensions.Scope);
    }
}
