using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport;
using AiUiStrings = QuickER.AI.UI.Resources.Strings;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="AttachmentPanel"/> の XAML バインディング配線を検証する。
/// チップの × 削除ボタンは ItemsControl のテンプレート内から ElementName 経由で
/// <c>AttachmentList.RemoveCommand</c> へ束縛するため、VM テストでは配線切れを検出できない
/// （実際に「DataContext.RemoveCommand」への誤束縛で × が無反応になる不具合が出た）。
/// ここではビジュアルツリーを実体化して、× ボタンのコマンド解決と削除動作を検証する。
/// </summary>
public class AttachmentPanelTests
{
    /// <summary>1x1 PNG（マジックバイト付き・ファクトリの検証を通る最小データ）</summary>
    private static readonly byte[] TinyPng =
    [
        0x89,
        0x50,
        0x4E,
        0x47,
        0x0D,
        0x0A,
        0x1A,
        0x0A,
        0x00,
        0x00,
        0x00,
        0x0D,
        0x49,
        0x48,
        0x44,
        0x52,
        0x00,
        0x00,
        0x00,
        0x01,
        0x00,
        0x00,
        0x00,
        0x01,
        0x08,
        0x06,
        0x00,
        0x00,
        0x00,
        0x1F,
        0x15,
        0xC4,
        0x89,
        0x00,
        0x00,
        0x00,
        0x0D,
        0x49,
        0x44,
        0x41,
        0x54,
        0x78,
        0x9C,
        0x62,
        0x00,
        0x01,
        0x00,
        0x00,
        0x05,
        0x00,
        0x01,
        0x0D,
        0x0A,
        0x2D,
        0xB4,
        0x00,
        0x00,
        0x00,
        0x00,
        0x49,
        0x45,
        0x4E,
        0x44,
        0xAE,
        0x42,
        0x60,
        0x82,
    ];

    /// <summary>× ボタンのコマンドが AttachmentList.RemoveCommand へ解決され、実行で項目が消えることを検証する</summary>
    [Fact(DisplayName = "添付チップの × ボタンが RemoveCommand へ束縛され削除できる")]
    public void RemoveButton_IsWiredToRemoveCommand()
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                WpfApplicationTestSupport.EnsureApplicationResources();

                var list = new AttachmentListViewModel(_ => { })
                {
                    Support =
                        QuickER.AI.AttachmentSupport.Images | QuickER.AI.AttachmentSupport.Pdf,
                };
                list.AddClipboardImage(TinyPng, new DateTime(2026, 7, 6, 12, 0, 0));
                list.Items.Should().HaveCount(1, "前提: 添付が 1 件追加されていること");

                // BAML ロードは並列テストと競合しないよう直列化する
                var panel = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AttachmentPanel { AttachmentList = list }
                );

                // ビジュアルツリーを実体化して ItemsControl のコンテナとテンプレートを生成する
                var window = new Window
                {
                    Content = panel,
                    Width = 400,
                    Height = 200,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();

                try
                {
                    panel.UpdateLayout();

                    var removeButton = FindRemoveButton(panel);
                    removeButton
                        .Should()
                        .NotBeNull("チップの × ボタンがビジュアルツリーに存在すること");

                    // 誤束縛（存在しないパス）だと Command が null になり × が無反応になる
                    removeButton!
                        .Command.Should()
                        .NotBeNull("× ボタンのコマンド束縛が解決されていること");

                    removeButton.Command!.Execute(removeButton.CommandParameter);
                    list.Items.Should().BeEmpty("× の実行で添付が削除されること");
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured.Should().BeNull();
    }

    /// <summary>ビジュアルツリーから ✕ ボタン（ToolTip="削除"）を探す</summary>
    private static Button? FindRemoveButton(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (
                child is Button button
                && Equals(button.ToolTip, AiUiStrings.Attachment_RemoveTooltip)
            )
            {
                return button;
            }

            if (FindRemoveButton(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
