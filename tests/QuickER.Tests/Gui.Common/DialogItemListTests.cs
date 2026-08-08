using AwesomeAssertions;
using QuickER.Gui.Abstractions;

namespace QuickER.Tests.Gui.Common;

/// <summary>
/// ダイアログ本文へ載せる項目一覧の整形（<see cref="DialogItemList"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 件数が多いとダイアログが縦に伸び、標準の MessageBox はスクロールしないためボタンが画面外へ出る。
/// 上限までを並べて超過分を「他 N 件」へ畳む挙動を固定する。
/// </remarks>
public class DialogItemListTests
{
    /// <summary>上限以下なら全件をそのまま並べ、「他 N 件」の行を足さないことを検証する</summary>
    [Fact(DisplayName = "上限以下なら全件を並べる")]
    public void Format_WithinLimit_ListsAllItems()
    {
        var lines = Enumerable.Range(1, DialogItemList.MaxItems).Select(n => $"- item{n}").ToList();

        var body = DialogItemList.Format(lines, "…and {0} more");

        body.Should().Be(string.Join(Environment.NewLine, lines));
        body.Should().NotContain("more");
    }

    /// <summary>上限を超えたら先頭のみを並べ、超過件数を「他 N 件」の 1 行へ畳むことを検証する</summary>
    [Fact(DisplayName = "上限超過は先頭のみ並べて残件数を畳む")]
    public void Format_ExceedingLimit_TruncatesWithCount()
    {
        var lines = Enumerable
            .Range(1, DialogItemList.MaxItems + 5)
            .Select(n => $"- item{n}")
            .ToList();

        var body = DialogItemList.Format(lines, "…and {0} more");

        var rendered = body.Split(Environment.NewLine);
        rendered.Should().HaveCount(DialogItemList.MaxItems + 1);
        rendered[0].Should().Be("- item1");
        rendered[DialogItemList.MaxItems - 1].Should().Be($"- item{DialogItemList.MaxItems}");
        rendered[^1].Should().Be("…and 5 more");
    }

    /// <summary>項目が無いときは空文字を返すことを検証する</summary>
    [Fact(DisplayName = "項目が無ければ空文字を返す")]
    public void Format_Empty_ReturnsEmpty()
    {
        DialogItemList.Format([], "…and {0} more").Should().BeEmpty();
    }
}
