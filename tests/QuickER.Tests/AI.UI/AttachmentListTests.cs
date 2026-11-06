using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>添付リストの表（受け付ける範囲 × ファイルの種類の受け付け・操作の列・API キー接続の範囲の規則）</summary>
public sealed class AttachmentListTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => AttachmentListRows.Table.Ids();

    [Theory(DisplayName = "添付リストの表：範囲と操作に応じて受け付け・断り・知らせる")]
    [MemberData(nameof(Ids))]
    public void Row_Holds(string id)
    {
        var row = AttachmentListRows.Table[id];
        using var harness = new AttachmentListHarness(
            AttachmentListRows.SupportOf(row.Input.Support)
        );

        row.Input.Body(harness);
    }

    [Fact(DisplayName = "網羅：受け付ける範囲 × ファイルの種類")]
    public void Coverage_IsComplete()
    {
        var counter = new CoverageCounter(AttachmentListDimensions.Table);

        foreach (var row in AttachmentListRows.Table.Where(row => row.Input.Kind is not null))
        {
            AttachmentListDimensions.Observe(counter, row.Input.Support, row.Input.Kind!.Value);
        }

        counter
            .Report()
            .AssertGaps(CoverageGap.Set(EngineIds.Ai, []), AttachmentListDimensions.Scope);
    }
}
