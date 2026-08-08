using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Tests.AI;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// モデル名 ComboBox（API キー接続の ApiModelBox / Codex 接続の CodexModelBox）で、ドロップダウン
/// 項目右端の × ボタンが実 XAML の束縛（項目の IsRemovable による表示・テンプレート内
/// <c>RelativeSource AncestorType=ComboBox</c> 経由の削除コマンド）を通じて「カタログ候補では
/// 非表示・履歴候補では表示され、実行で履歴のみ消える」ことを検証する。
/// </summary>
/// <remarks>
/// テンプレート内 RelativeSource 束縛はサイレントに失敗しうる（このリポジトリの既知教訓＝
/// <see cref="AttachmentPanelTests"/> と同じ理由）。VM 単体テストでは配線切れを検出できない。
/// ダイアログ全体を実体化して TabControl 内のドロップダウンをヘッドレスで開くのは困難なため、
/// 実ダイアログから **本物の ItemTemplate / ItemContainerStyle**（× ボタンの束縛を含む XAML）を取り出し、
/// それを直接ロードされる最小ウィンドウの ComboBox へ適用してドロップダウンを開き、束縛を検証する。
/// 検証対象は自作の束縛ではなく、あくまで実 XAML のテンプレートである。
/// </remarks>
public class ModelHistoryComboBoxTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>
    /// API キー接続のモデル ComboBox（OpenAI＝カタログ＋履歴の 2 層）で、カタログ候補の × が
    /// Collapsed・履歴候補の × が Visible かつ Command 解決・Execute で履歴項目のみ消えることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "API モデルドロップダウンの × はカタログ非表示・履歴のみ表示され削除できる"
    )]
    public void ApiRemoveButton_VisibleOnlyForHistory_AndRemovesHistoryItem()
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
                // openai のカタログ外履歴を 1 件仕込んでおく（カタログの下に × 付きで並ぶ状態）
                var store = new AiSettingsStore(folder);
                var seeded = store.Load();
                seeded.ApiModelHistory.Touch("openai", "custom-model");
                store.Save(seeded);

                // API キーは実 %APPDATA% の ApiKeyStore ではなくメモリ上のストアへ隔離する
                var keyStore = new InMemoryApiKeyStore();
                var vm = new AiChatDialogViewModel(
                    host: null,
                    dispatcher: new SyncUiDispatcher(),
                    settingsStore: store,
                    codexClient: new FakeCodexAppServerClient(),
                    apiKeyLoader: keyStore.Load,
                    apiKeySaver: keyStore.Save
                );

                // 前提: 既定プロバイダ（OpenAI）の候補はカタログ（× なし）＋履歴（× あり）の 2 層
                vm.Connection.ApiModelCandidates.Select(c => c.Name)
                    .Should()
                    .Equal([.. AiModelCatalog.OpenAiModels, "custom-model"]);
                var historyIndex = vm.Connection.ApiModelCandidates.Count - 1;

                // 実ダイアログのモデル名 ComboBox から本物の ItemTemplate / ItemContainerStyle を取り出す
                // （ここに × ボタンの束縛が入っている＝検証対象は実 XAML のテンプレート。
                // 　BAML ロードは並列テストと競合しないよう直列化する）
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AiChatDialog(vm)
                );
                var sourceCombo = dialog.FindName("ApiModelBox") as ComboBox;
                sourceCombo.Should().NotBeNull("実 XAML にモデル名 ComboBox が存在すること");
                var itemTemplate = sourceCombo!.ItemTemplate;
                var itemContainerStyle = sourceCombo.ItemContainerStyle;
                itemTemplate
                    .Should()
                    .NotBeNull("× ボタンの束縛を含む実 XAML の ItemTemplate が取得できること");

                // 本物のテンプレートを、直接ロードされる最小ウィンドウの ComboBox へ適用する
                var combo = new ComboBox
                {
                    IsEditable = true,
                    ItemsSource = vm.Connection.ApiModelCandidates,
                    ItemTemplate = itemTemplate,
                    ItemContainerStyle = itemContainerStyle,
                    DataContext = vm,
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
                // テンプレートが参照する StaticResource（BoolToVisibilityConverter は App 側で供給済み）
                window.Resources["MutedText"] = new SolidColorBrush(Colors.Gray);
                window.Show();

                try
                {
                    window.UpdateLayout();
                    DoEvents();

                    // ドロップダウンを開いて項目コンテナ（ComboBoxItem）を生成させる
                    combo.IsDropDownOpen = true;
                    combo.UpdateLayout();
                    DoEvents();

                    // カタログ候補（先頭）: × ボタンは Collapsed
                    var catalogButton = FindRemoveButton(RealizeContainer(combo, 0));
                    catalogButton
                        .Should()
                        .NotBeNull("カタログ候補にも × ボタン自体はテンプレートから生成されること");
                    catalogButton!
                        .Visibility.Should()
                        .Be(
                            Visibility.Collapsed,
                            "カタログ候補（IsRemovable=false）の × は非表示であること"
                        );

                    // 履歴候補（末尾）: × ボタンが Visible・Command 解決・Execute で削除できる
                    var removeButton = FindRemoveButton(RealizeContainer(combo, historyIndex));
                    removeButton
                        .Should()
                        .NotBeNull("履歴候補の × ボタンがテンプレートから生成されていること");
                    removeButton!
                        .Visibility.Should()
                        .Be(
                            Visibility.Visible,
                            "履歴候補（IsRemovable=true）の × は表示されること"
                        );

                    // 誤束縛（RelativeSource 解決失敗）だと Command が null になり × が無反応になる
                    removeButton
                        .Command.Should()
                        .NotBeNull("× ボタンの Command が RelativeSource 経由で解決されていること");

                    removeButton.Command!.Execute(removeButton.CommandParameter);

                    // 履歴項目のみ消え、カタログは残る（JSON 側も消える）
                    vm.Connection.ApiModelCandidates.Select(c => c.Name)
                        .Should()
                        .Equal(AiModelCatalog.OpenAiModels);
                    store.Load().ApiModelHistory.ModelsFor("openai").Should().BeEmpty();
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

    /// <summary>
    /// Codex モデル ComboBox（非 openai プロバイダ＝候補はすべて履歴）で、× ボタンが Visible かつ
    /// Command 解決・Execute で履歴項目が消えることを、実 XAML のテンプレートで検証する。
    /// openai プロバイダの静的カタログ候補（IsRemovable=false）では × が Collapsed であることも確認する。
    /// </summary>
    /// <remarks>
    /// 候補を決定的にするため config.toml は seam で注入し、Connection は単体で構築する。
    /// ComboBox の DataContext は実ダイアログと同じ形（<c>Connection</c> プロパティ経由）のホストを使い、
    /// テンプレート内 <c>DataContext.Connection.RemoveCodexModelHistoryCommand</c> の束縛パスをそのまま検証する。
    /// </remarks>
    [Fact(
        DisplayName = "Codex 候補ドロップダウンの × は履歴のみ表示され RemoveCommand で削除できる"
    )]
    public void CodexRemoveButton_VisibleOnlyForHistory_AndRemovesHistoryItem()
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
                // 履歴に 2 件を仕込み、非 openai プロバイダの候補が履歴のみになる状態を作る
                var codexStore = new AiSettingsStore(folder);
                var seeded = codexStore.Load();
                seeded.CodexModelHistory.Providers["test-provider"] = new List<string>
                {
                    "hist-model",
                    "hist-model-2",
                };
                codexStore.Save(seeded);

                // 候補を決定的にするため config.toml を seam で注入して Connection を単体構築する
                var connection = new ChatConnectionSettingsViewModel(
                    AiDialogKind.AiChat,
                    settingsStore: codexStore,
                    codexConfigReader: () =>
                        new CodexConfigToml
                        {
                            ProviderNames = new List<string> { "test-provider" },
                        },
                    apiKeyLoader: _ => string.Empty,
                    apiKeySaver: (_, _) => { }
                );
                connection.LoadSettings();
                connection.SelectedBackend = ErChatBackendKind.Codex;
                connection.CodexModelProvider = "test-provider";

                // 前提: 非 openai の候補は履歴のみ（すべて × あり）
                connection
                    .CodexModelCandidates.Select(c => (c.Name, c.IsRemovable))
                    .Should()
                    .Equal(("hist-model", true), ("hist-model-2", true));

                // 実ダイアログの Codex モデル ComboBox から本物の ItemTemplate / ItemContainerStyle を取り出す
                // （ダイアログ構築用 VM は隔離ストアで生成。テンプレート抽出にのみ使う。
                // 　BAML ロードは並列テストと競合しないよう直列化する）
                var keyStore = new InMemoryApiKeyStore();
                var dialogVm = new AiChatDialogViewModel(
                    host: null,
                    dispatcher: new SyncUiDispatcher(),
                    settingsStore: new AiSettingsStore(folder),
                    codexClient: new FakeCodexAppServerClient(),
                    apiKeyLoader: keyStore.Load,
                    apiKeySaver: keyStore.Save
                );
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AiChatDialog(dialogVm)
                );
                var sourceCombo = dialog.FindName("CodexModelBox") as ComboBox;
                sourceCombo.Should().NotBeNull("実 XAML に Codex モデル ComboBox が存在すること");
                var itemTemplate = sourceCombo!.ItemTemplate;
                var itemContainerStyle = sourceCombo.ItemContainerStyle;
                itemTemplate
                    .Should()
                    .NotBeNull("× ボタンの束縛を含む実 XAML の ItemTemplate が取得できること");

                // 本物のテンプレートを、直接ロードされる最小ウィンドウの ComboBox へ適用する。
                // DataContext は実ダイアログと同じ「Connection プロパティを持つ」形のホストにする
                var combo = new ComboBox
                {
                    IsEditable = true,
                    ItemsSource = connection.CodexModelCandidates,
                    ItemTemplate = itemTemplate,
                    ItemContainerStyle = itemContainerStyle,
                    DataContext = new ConnectionHost(connection),
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
                // テンプレートが参照する StaticResource（BoolToVisibilityConverter は App 側で供給済み）
                window.Resources["MutedText"] = new SolidColorBrush(Colors.Gray);
                window.Show();

                try
                {
                    window.UpdateLayout();
                    DoEvents();

                    // ドロップダウンを開いて項目コンテナ（ComboBoxItem）を生成させる
                    combo.IsDropDownOpen = true;
                    combo.UpdateLayout();
                    DoEvents();

                    // 履歴候補（先頭）: × ボタンが Visible・Command 解決・Execute で削除できる
                    var removeButton = FindRemoveButton(RealizeContainer(combo, 0));
                    removeButton
                        .Should()
                        .NotBeNull("履歴候補の × ボタンがテンプレートから生成されていること");
                    removeButton!
                        .Visibility.Should()
                        .Be(
                            Visibility.Visible,
                            "履歴候補（IsRemovable=true）の × は表示されること"
                        );

                    // 誤束縛（RelativeSource 解決失敗）だと Command が null になり × が無反応になる
                    removeButton
                        .Command.Should()
                        .NotBeNull("× ボタンの Command が RelativeSource 経由で解決されていること");

                    removeButton.Command!.Execute(removeButton.CommandParameter);

                    // 実行した項目（先頭＝hist-model）のみ消える（JSON 側も消える）
                    connection
                        .CodexModelCandidates.Select(c => c.Name)
                        .Should()
                        .Equal("hist-model-2");
                    codexStore
                        .Load()
                        .CodexModelHistory.ModelsFor("test-provider")
                        .Should()
                        .Equal("hist-model-2");

                    // openai プロバイダへ切替: 静的カタログ候補（IsRemovable=false）の × は Collapsed
                    connection.CodexModelProvider = "openai";
                    combo.UpdateLayout();
                    DoEvents();

                    var catalogButton = FindRemoveButton(RealizeContainer(combo, 0));
                    catalogButton
                        .Should()
                        .NotBeNull("カタログ候補にも × ボタン自体はテンプレートから生成されること");
                    catalogButton!
                        .Visibility.Should()
                        .Be(
                            Visibility.Collapsed,
                            "カタログ候補（IsRemovable=false）の × は非表示であること"
                        );
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

    /// <summary>
    /// Copilot モデル ComboBox（候補＝実行時列挙＋MRU 履歴の 2 層）で、実行時列挙候補の × が Collapsed・
    /// 履歴候補の × が Visible かつ Command 解決・Execute で履歴項目のみ消えることを、実 XAML の
    /// テンプレートで検証する。
    /// </summary>
    /// <remarks>
    /// Copilot は静的カタログを持たず、実行時列挙は接続後にしか得られないため、列挙相当の候補は
    /// <see cref="ChatConnectionSettingsViewModel.CopilotAvailableModels"/> へ直接流し込んで決定的にする。
    /// </remarks>
    [Fact(
        DisplayName = "Copilot 候補ドロップダウンの × は履歴のみ表示され RemoveCommand で削除できる"
    )]
    public void CopilotRemoveButton_VisibleOnlyForHistory_AndRemovesHistoryItem()
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
                // 履歴に 2 件を仕込む（実行時列挙の下に × 付きで並ぶ状態）
                var copilotStore = new AiSettingsStore(folder);
                var seeded = copilotStore.Load();
                seeded.CopilotModelHistory.Providers[CopilotSettings.HistoryProviderKey] =
                    new List<string> { "hist-model", "hist-model-2" };
                copilotStore.Save(seeded);

                var connection = new ChatConnectionSettingsViewModel(
                    AiDialogKind.AiChat,
                    settingsStore: copilotStore,
                    codexConfigReader: () => new CodexConfigToml(),
                    apiKeyLoader: _ => string.Empty,
                    apiKeySaver: (_, _) => { }
                );
                connection.LoadSettings();
                connection.SelectedBackend = ErChatBackendKind.Copilot;
                connection.CopilotAvailableModels = new List<string> { "enum-model" };

                // 前提: 実行時列挙（× なし）＋履歴（× あり）の 2 層
                connection
                    .CopilotModelCandidates.Select(c => (c.Name, c.IsRemovable))
                    .Should()
                    .Equal(("enum-model", false), ("hist-model", true), ("hist-model-2", true));

                // 実ダイアログの Copilot モデル ComboBox から本物の ItemTemplate / ItemContainerStyle を取り出す
                // （BAML ロードは並列テストと競合しないよう直列化する）
                var keyStore = new InMemoryApiKeyStore();
                var dialogVm = new AiChatDialogViewModel(
                    host: null,
                    dispatcher: new SyncUiDispatcher(),
                    settingsStore: new AiSettingsStore(folder),
                    codexClient: new FakeCodexAppServerClient(),
                    copilotClient: new FakeCopilotRuntimeClient(),
                    apiKeyLoader: keyStore.Load,
                    apiKeySaver: keyStore.Save
                );
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AiChatDialog(dialogVm)
                );
                var sourceCombo = dialog.FindName("CopilotModelBox") as ComboBox;
                sourceCombo.Should().NotBeNull("実 XAML に Copilot モデル ComboBox が存在すること");
                var itemTemplate = sourceCombo!.ItemTemplate;
                var itemContainerStyle = sourceCombo.ItemContainerStyle;
                itemTemplate
                    .Should()
                    .NotBeNull("× ボタンの束縛を含む実 XAML の ItemTemplate が取得できること");

                // 本物のテンプレートを、直接ロードされる最小ウィンドウの ComboBox へ適用する
                var combo = new ComboBox
                {
                    IsEditable = true,
                    ItemsSource = connection.CopilotModelCandidates,
                    ItemTemplate = itemTemplate,
                    ItemContainerStyle = itemContainerStyle,
                    DataContext = new ConnectionHost(connection),
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
                // テンプレートが参照する StaticResource（BoolToVisibilityConverter は App 側で供給済み）
                window.Resources["MutedText"] = new SolidColorBrush(Colors.Gray);
                window.Show();

                try
                {
                    window.UpdateLayout();
                    DoEvents();

                    // ドロップダウンを開いて項目コンテナ（ComboBoxItem）を生成させる
                    combo.IsDropDownOpen = true;
                    combo.UpdateLayout();
                    DoEvents();

                    // 実行時列挙候補（先頭）: × ボタンは Collapsed
                    var enumeratedButton = FindRemoveButton(RealizeContainer(combo, 0));
                    enumeratedButton
                        .Should()
                        .NotBeNull("列挙候補にも × ボタン自体はテンプレートから生成されること");
                    enumeratedButton!
                        .Visibility.Should()
                        .Be(
                            Visibility.Collapsed,
                            "実行時列挙の候補（IsRemovable=false）の × は非表示であること"
                        );

                    // 履歴候補（2 番目）: × ボタンが Visible・Command 解決・Execute で削除できる
                    var removeButton = FindRemoveButton(RealizeContainer(combo, 1));
                    removeButton
                        .Should()
                        .NotBeNull("履歴候補の × ボタンがテンプレートから生成されていること");
                    removeButton!
                        .Visibility.Should()
                        .Be(
                            Visibility.Visible,
                            "履歴候補（IsRemovable=true）の × は表示されること"
                        );

                    // 誤束縛（RelativeSource 解決失敗）だと Command が null になり × が無反応になる
                    removeButton
                        .Command.Should()
                        .NotBeNull("× ボタンの Command が RelativeSource 経由で解決されていること");

                    removeButton.Command!.Execute(removeButton.CommandParameter);

                    // 実行した履歴項目のみ消え、実行時列挙は残る（JSON 側も消える）
                    connection
                        .CopilotModelCandidates.Select(c => c.Name)
                        .Should()
                        .Equal("enum-model", "hist-model-2");
                    copilotStore
                        .Load()
                        .CopilotModelHistory.ModelsFor(CopilotSettings.HistoryProviderKey)
                        .Should()
                        .Equal("hist-model-2");
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

    /// <summary>
    /// 実ダイアログの DataContext と同じ形（<c>Connection</c> プロパティ）で Connection を公開するホスト
    /// （テンプレート内の <c>DataContext.Connection.*</c> 束縛パスをそのまま検証するために使う）。
    /// </summary>
    private sealed class ConnectionHost
    {
        public ConnectionHost(ChatConnectionSettingsViewModel connection) =>
            Connection = connection;

        public ChatConnectionSettingsViewModel Connection { get; }
    }

    /// <summary>ドロップダウン項目のコンテナを実体化して返す（テンプレート適用・レイアウト込み）</summary>
    private static ComboBoxItem RealizeContainer(ComboBox combo, int index)
    {
        var container = combo.ItemContainerGenerator.ContainerFromIndex(index) as ComboBoxItem;
        container.Should().NotBeNull($"ドロップダウン項目 {index} のコンテナが生成されていること");
        container!.ApplyTemplate();
        container.UpdateLayout();
        return container;
    }

    /// <summary>ディスパッチャにキューされた処理（Loaded・ポップアップ生成・コンテナ生成・レイアウト）を流す</summary>
    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false)
        );
        Dispatcher.PushFrame(frame);
    }

    /// <summary>ビジュアルツリーから × ボタン（ToolTip="履歴から削除"）を探す</summary>
    private static Button? FindRemoveButton(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (
                child is Button button
                && Equals(button.ToolTip, ChatStrings.Chat_RemoveModelFromHistory)
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
