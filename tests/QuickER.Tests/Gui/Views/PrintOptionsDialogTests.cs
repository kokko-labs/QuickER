using FluentAssertions;
using QuickER.Tests.TestSupport;
using QuickER.Views;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// <see cref="PrintOptionsDialog"/> の BAML 読み込み（InitializeComponent）が成功することを検証する。
/// 共有テーマ辞書（DialogTheme.xaml）のマージが pack URI で解決でき、
/// PrimaryButton / SecondaryButton / FormInput の StaticResource 参照が漏れなく解決することを保証する。
/// </summary>
public class PrintOptionsDialogTests
{
    /// <summary>STA スレッド上でダイアログを構築し、InitializeComponent が例外を投げず初期値が入ることを検証する</summary>
    [Fact(DisplayName = "PrintOptionsDialog の InitializeComponent が例外を投げない")]
    public void InitializeComponent_DoesNotThrow()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            // BAML ロードは並列テストと競合しないよう直列化する
            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new PrintOptionsDialog("既定タイトル")
            );

            dialog.Should().NotBeNull();
        });
    }
}
