using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Gui.Common;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Gui.Common;

/// <summary>
/// <see cref="InformationDetailsDialog"/> の BAML 読み込み（InitializeComponent）が成功し、
/// 要約メッセージ・詳細・タイトルが各コントロールへ流し込まれることを検証する。
/// </summary>
public class InformationDetailsDialogTests
{
    /// <summary>情報種別で構築し、InitializeComponent が例外を投げず各コントロールに値が入ることを検証する</summary>
    [Fact(DisplayName = "InformationDetailsDialog（情報）の構築が成功し各コントロールに値が入る")]
    public void Construct_Information_PopulatesControls()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new InformationDetailsDialog(
                    "要約メッセージ",
                    "行1" + System.Environment.NewLine + "行2",
                    "情報タイトル",
                    isError: false
                )
            );

            dialog.Title.Should().Be("情報タイトル");
            // 情報は Information 意味論のグリフ（完了・案内）
            ((TextBlock)dialog.FindName("HeaderIcon"))
                .Text.Should()
                .Be("ℹ");
            ((TextBlock)dialog.FindName("MessageText")).Text.Should().Be("要約メッセージ");
            ((TextBox)dialog.FindName("DetailsText"))
                .Text.Should()
                .Contain("行1")
                .And.Contain("行2");
            // 読み取り専用領域であること（コピー可能・編集不可）
            ((TextBox)dialog.FindName("DetailsText"))
                .IsReadOnly.Should()
                .BeTrue();
        });
    }

    /// <summary>エラー種別でも構築が成功することを検証する（ヘッダのアイコン種別だけが異なる）</summary>
    [Fact(DisplayName = "InformationDetailsDialog（エラー）の構築が成功する")]
    public void Construct_Error_DoesNotThrow()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new InformationDetailsDialog(
                    "失敗しました。",
                    "[Error] 詳細",
                    "エラー",
                    isError: true
                )
            );

            dialog.Title.Should().Be("エラー");
            // エラーはすでに発生した失敗の報告＝Error 意味論のグリフ（続行前の注意を表す警告 ⚠ にしない）
            ((TextBlock)dialog.FindName("HeaderIcon"))
                .Text.Should()
                .Be("✖");
            ((TextBlock)dialog.FindName("MessageText")).Text.Should().Be("失敗しました。");
            ((TextBox)dialog.FindName("DetailsText")).Text.Should().Be("[Error] 詳細");
        });
    }

    /// <summary>コピーボタン文言を省略したときにボタンが非表示のままであることを検証する（既存挙動）</summary>
    [Fact(
        DisplayName = "InformationDetailsDialog: copyButtonText 省略時はコピーボタンを表示しない"
    )]
    public void Construct_WithoutCopyButtonText_HidesCopyButton()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new InformationDetailsDialog("要約", "詳細", "タイトル", isError: false)
            );

            ((Button)dialog.FindName("CopyButton"))
                .Visibility.Should()
                .Be(System.Windows.Visibility.Collapsed);
        });
    }

    /// <summary>コピーボタン文言を指定したときにボタンが表示され、文言が反映されることを検証する</summary>
    /// <remarks>
    /// クリップボードへの実書き込みはシステムグローバルな状態のため断定しない（実起動で確認する）。
    /// </remarks>
    [Fact(DisplayName = "InformationDetailsDialog: copyButtonText 指定時はコピーボタンを表示する")]
    public void Construct_WithCopyButtonText_ShowsCopyButton()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new InformationDetailsDialog(
                    "要約",
                    "詳細",
                    "タイトル",
                    isError: true,
                    copyButtonText: "詳細をコピー"
                )
            );

            var copyButton = (Button)dialog.FindName("CopyButton");
            copyButton.Visibility.Should().Be(System.Windows.Visibility.Visible);
            copyButton.Content.Should().Be("詳細をコピー");
        });
    }
}
