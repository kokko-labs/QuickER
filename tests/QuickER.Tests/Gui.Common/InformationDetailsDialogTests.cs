using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Gui.Common;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Gui.Common;

/// <summary>
/// <see cref="InformationDetailsDialog"/> の BAML 読み込み（InitializeComponent）が成功し、
/// 要約メッセージ・詳細・タイトルが各コントロールへ流し込まれること、および
/// 続行確認モード（<see cref="InformationDetailsDialog.CreateWarningConfirmation"/>）の構成を検証する。
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

    /// <summary>続行確認モードはキャンセルボタンが可視になり、Esc の割当が OK からキャンセルへ移ることを検証する</summary>
    [Fact(
        DisplayName = "InformationDetailsDialog: 続行確認モードはキャンセル可視・Esc はキャンセルへ割当"
    )]
    public void CreateWarningConfirmation_ShowsCancelAndReassignsEscape()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                InformationDetailsDialog.CreateWarningConfirmation("message", "details", "title")
            );

            // キャンセルボタンが現れ、文言はリソース（DetailsDialog_Cancel）から解決される
            var cancelButton = (Button)dialog.FindName("CancelButton");
            cancelButton.Visibility.Should().Be(System.Windows.Visibility.Visible);
            cancelButton
                .Content.Should()
                .Be(QuickER.Gui.Common.Resources.Strings.DetailsDialog_Cancel);

            // Esc の割当は OK からキャンセルへ移る（OK は Enter＝IsDefault のまま）
            var okButton = (Button)dialog.FindName("OkButton");
            okButton.IsCancel.Should().BeFalse();
            okButton.IsDefault.Should().BeTrue();
            cancelButton.IsCancel.Should().BeTrue();

            // 続行前の注意＝Warning 意味論のグリフ
            ((TextBlock)dialog.FindName("HeaderIcon"))
                .Text.Should()
                .Be("⚠");
        });
    }

    /// <summary>情報／エラー表示（従来モード）ではキャンセルボタンが現れないことを検証する</summary>
    [Fact(DisplayName = "InformationDetailsDialog: 情報・エラー表示ではキャンセルは非表示のまま")]
    public void InformationMode_KeepsCancelCollapsed()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new InformationDetailsDialog("要約", "詳細", "タイトル", isError: false)
            );

            // 従来モードは OK 単独（Esc も OK が兼ねる）＝既存挙動を変えない
            ((Button)dialog.FindName("CancelButton"))
                .Visibility.Should()
                .Be(System.Windows.Visibility.Collapsed);
            ((Button)dialog.FindName("OkButton")).IsCancel.Should().BeTrue();
        });
    }
}
