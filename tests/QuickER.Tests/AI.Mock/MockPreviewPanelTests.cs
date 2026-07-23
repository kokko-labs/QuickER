using System.IO;
using FluentAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockPreviewPanel.IsNavigationAllowed"/>（プレビューの遷移許可判定・純粋関数）を検証するテストクラス。
/// 許可フォルダ直下の <c>*.html</c>（#fragment を含む同一ファイル）のみ許可し、フォルダ外・<c>..</c>・http(s)・
/// サブフォルダ・非 HTML は拒否する。
/// </summary>
public class MockPreviewPanelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "QuickERMockRoot");

    /// <summary>許可フォルダ直下の *.html は許可される</summary>
    [Fact(DisplayName = "許可フォルダ直下の *.html は許可")]
    public void FileDirectlyUnderFolder_IsAllowed()
    {
        var target = new Uri(Path.Combine(Root, "OrderList.html"));

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeTrue();
    }

    /// <summary>#fragment 付き（同一ファイル）の遷移は許可される</summary>
    [Fact(DisplayName = "#fragment 付き同一ファイルは許可")]
    public void FragmentNavigation_IsAllowed()
    {
        var baseUri = new Uri(Path.Combine(Root, "OrderList.html"));
        var target = new Uri(baseUri.AbsoluteUri + "#section");

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeTrue();
    }

    /// <summary>サブフォルダ配下（直下でない）は拒否される</summary>
    [Fact(DisplayName = "サブフォルダ配下は拒否")]
    public void FileInSubFolder_IsRejected()
    {
        var target = new Uri(Path.Combine(Root, "sub", "OrderList.html"));

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeFalse();
    }

    /// <summary>フォルダ外（親フォルダ）は拒否される</summary>
    [Fact(DisplayName = "フォルダ外は拒否")]
    public void FileOutsideFolder_IsRejected()
    {
        var parent = Directory.GetParent(Root)!.FullName;
        var target = new Uri(Path.Combine(parent, "Secret.html"));

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeFalse();
    }

    /// <summary><c>..</c> でフォルダ外へ抜ける遷移は拒否される</summary>
    [Fact(DisplayName = ".. でフォルダ外へ抜ける遷移は拒否")]
    public void ParentTraversal_IsRejected()
    {
        var baseUri = new Uri(Path.Combine(Root, "index.html"));
        var target = new Uri(baseUri, "../Secret.html");

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeFalse();
    }

    /// <summary>http(s) など file 以外は拒否される</summary>
    [Theory(DisplayName = "http(s) など file 以外は拒否")]
    [InlineData("https://example.com/OrderList.html")]
    [InlineData("http://example.com/x.html")]
    public void NonFileScheme_IsRejected(string url)
    {
        MockPreviewPanel.IsNavigationAllowed(new Uri(url), Root).Should().BeFalse();
    }

    /// <summary>非 HTML ファイルは拒否される</summary>
    [Fact(DisplayName = "非 HTML ファイルは拒否")]
    public void NonHtmlFile_IsRejected()
    {
        var target = new Uri(Path.Combine(Root, "data.txt"));

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeFalse();
    }

    /// <summary>許可フォルダが空なら拒否される</summary>
    [Fact(DisplayName = "許可フォルダが空なら拒否")]
    public void EmptyAllowedRootFolder_IsRejected()
    {
        var target = new Uri(Path.Combine(Root, "OrderList.html"));

        MockPreviewPanel.IsNavigationAllowed(target, string.Empty).Should().BeFalse();
        MockPreviewPanel.IsNavigationAllowed(target, null).Should().BeFalse();
    }
}
