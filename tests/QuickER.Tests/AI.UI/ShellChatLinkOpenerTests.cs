using AwesomeAssertions;
using QuickER.AI.UI;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="ShellChatLinkOpener"/> が、http / https だけをシェルへ渡し、失敗をエラーのモーダルで知らせることを検証するテストクラス。
/// </summary>
public class ShellChatLinkOpenerTests
{
    /// <summary>http / https の URL はそのままシェルへ渡ることを検証する</summary>
    [Fact(DisplayName = "http / https の URL はシェルへ渡る")]
    public void SafeUri_IsLaunched()
    {
        var launched = new List<string>();
        var dialogs = new StubDialogService();
        var opener = new ShellChatLinkOpener(dialogs, "Title", launched.Add);

        opener.Open(new Uri("https://example.com/a?b=1"));

        launched.Should().Equal("https://example.com/a?b=1");
        dialogs.ErrorMessages.Should().BeEmpty();
    }

    /// <summary>規則を外れた URL は、渡されてもシェルへ渡さないことを検証する（組み立て側との二重化）</summary>
    [Theory(DisplayName = "http / https 以外の URL は渡されてもシェルへ渡さない")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData(@"\\server\share\x.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    public void UnsafeUri_IsNotLaunched(string url)
    {
        var launched = new List<string>();
        var dialogs = new StubDialogService();
        var opener = new ShellChatLinkOpener(dialogs, "Title", launched.Add);

        opener.Open(new Uri(url, UriKind.Absolute));

        launched.Should().BeEmpty();
        dialogs.ErrorMessages.Should().BeEmpty();
    }

    /// <summary>開けなかったときは、URL と原因の例外文を添えてエラーのモーダルで知らせることを検証する</summary>
    [Fact(DisplayName = "開けなかったときは URL と原因を添えてエラーで知らせる")]
    public void LaunchFailure_IsReportedAsError()
    {
        var dialogs = new StubDialogService();
        var opener = new ShellChatLinkOpener(
            dialogs,
            "Title",
            _ => throw new InvalidOperationException("no browser")
        );

        opener.Open(new Uri("https://example.com/"));

        dialogs.ErrorMessages.Should().ContainSingle();
        dialogs.ErrorMessages[0].Should().Contain("https://example.com/");
        dialogs.ErrorMessages[0].Should().Contain("no browser");
    }
}
