using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Tests.Services.Chat;

namespace QuickER.Tests.Views;

/// <summary>
/// Codex プロバイダー ComboBox（リスト選択のみ）が、実 XAML の ItemTemplate＋
/// <see cref="CodexProviderDisplayNameConverter"/> を通じて内部値 "openai" を "OpenAI" と表示し、
/// config.toml 由来のプロバイダー ID をそのまま表示することを検証する。
/// </summary>
/// <remarks>
/// テンプレート内の StaticResource／Converter 束縛はサイレントに失敗しうるため、
/// <see cref="ModelHistoryComboBoxTests"/> と同じ方式で実ダイアログからテンプレートを取り出し、
/// 画面外の最小ウィンドウで実体化して描画結果を検証する。
/// </remarks>
public class CodexProviderComboBoxTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    [Fact(DisplayName = "Codex プロバイダーはリスト選択のみで openai は OpenAI と表示される")]
    public void ProviderComboBox_IsSelectOnly_AndDisplaysOpenAiCasing()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var folder = Path.Combine(
                Path.GetTempPath(),
                "QuickERTests",
                Guid.NewGuid().ToString("N")
            );

            try
            {
                var vm = new AiChatDialogViewModel(
                    host: null,
                    dispatcher: new SyncUiDispatcher(),
                    settingsStore: new CodexAppServerSettingsStore(folder),
                    codexClient: new FakeCodexAppServerClient(),
                    apiModelHistoryStore: new ApiModelHistoryStore(folder),
                    codexModelHistoryStore: new CodexModelHistoryStore(folder)
                );

                // 実ダイアログのプロバイダー ComboBox から本物のテンプレートと入力方式を確認する
                // （BAML ロードは並列テストと競合しないよう直列化する）
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AiChatDialog(vm)
                );
                var sourceCombo = dialog.FindName("CodexProviderBox") as ComboBox;
                sourceCombo
                    .Should()
                    .NotBeNull("実 XAML に Codex プロバイダー ComboBox が存在すること");

                // リスト選択のみ（自由入力不可）
                sourceCombo!.IsEditable.Should().BeFalse("プロバイダーはリスト選択のみであること");

                var itemTemplate = sourceCombo.ItemTemplate;
                itemTemplate
                    .Should()
                    .NotBeNull("表示名変換を含む実 XAML の ItemTemplate が取得できること");

                // 本物のテンプレートを、直接ロードされる最小ウィンドウの ComboBox へ適用する
                var combo = new ComboBox
                {
                    ItemsSource = new[] { "openai", "custom-provider" },
                    ItemTemplate = itemTemplate,
                };
                // 画面外＋非アクティブで Show し、テスト実行中にユーザーの画面を邪魔しない（lessons 2026-07-10）
                var window = new Window
                {
                    Content = combo,
                    Width = 400,
                    Height = 200,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                };
                // テンプレートが参照する StaticResource を実ダイアログと同じキーで供給する
                window.Resources["CodexProviderDisplayNameConverter"] =
                    new CodexProviderDisplayNameConverter();
                window.Show();

                try
                {
                    window.UpdateLayout();
                    DoEvents();

                    combo.IsDropDownOpen = true;
                    combo.UpdateLayout();
                    DoEvents();

                    // "openai" は表示のみ "OpenAI"・他プロバイダー ID は素通し
                    FindItemText(combo, index: 0).Should().Be("OpenAI");
                    FindItemText(combo, index: 1).Should().Be("custom-provider");
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        });
    }

    /// <summary>ドロップダウン項目のコンテナを実体化し、テンプレートが描画した TextBlock の文字列を得る</summary>
    private static string? FindItemText(ComboBox combo, int index)
    {
        var container = combo.ItemContainerGenerator.ContainerFromIndex(index) as ComboBoxItem;
        container.Should().NotBeNull($"ドロップダウン項目 {index} のコンテナが生成されていること");
        container!.ApplyTemplate();
        container.UpdateLayout();
        return FindTextBlock(container)?.Text;
    }

    /// <summary>ビジュアルツリーから最初の TextBlock を探す</summary>
    private static TextBlock? FindTextBlock(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is TextBlock textBlock)
            {
                return textBlock;
            }

            if (FindTextBlock(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>ディスパッチャにキューされた処理（コンテナ生成・レイアウト）を流す</summary>
    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false)
        );
        Dispatcher.PushFrame(frame);
    }
}
