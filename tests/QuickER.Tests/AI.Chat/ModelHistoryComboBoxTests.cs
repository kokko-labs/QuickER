using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    /// <summary>モデル欄の × を検証する接続方式</summary>
    public enum ModelRemoveKind
    {
        /// <summary>API キー接続（OpenAI＝カタログ＋履歴の 2 層）</summary>
        Api,

        /// <summary>Codex 接続（非 openai プロバイダ＝候補はすべて履歴）</summary>
        Codex,

        /// <summary>Copilot 接続（実行時列挙＋MRU 履歴の 2 層）</summary>
        Copilot,
    }

    /// <summary>
    /// 接続方式ごとに違う部分（VM の作り方・候補・履歴の位置・削除後の期待）をまとめた 1 件分のシナリオ。
    /// </summary>
    /// <param name="ComboName">実ダイアログから本物のテンプレートを取り出すモデル名 ComboBox の名前</param>
    /// <param name="DialogVm">ダイアログ構築（テンプレート抽出）用の VM</param>
    /// <param name="Candidates">最小ウィンドウの ComboBox へ流す候補</param>
    /// <param name="DataContext">最小ウィンドウの ComboBox の DataContext</param>
    /// <param name="NonRemovableIndex">先に × が Collapsed であることを確かめる × なし候補の位置（無ければ null）</param>
    /// <param name="NonRemovableExistsMessage">× なし候補にも × ボタン自体があることの表明の理由</param>
    /// <param name="NonRemovableCollapsedMessage">× なし候補の × が非表示であることの表明の理由</param>
    /// <param name="HistoryIndex">× を実行する履歴候補の位置</param>
    /// <param name="AssertAfterRemove">実行後の候補と JSON 側の期待を表明する</param>
    /// <param name="AfterRemove">表明の後に続ける接続方式固有の確認（無ければ null）</param>
    private sealed record ModelRemoveScenario(
        string ComboName,
        AiChatDialogViewModel DialogVm,
        System.Collections.IEnumerable Candidates,
        object DataContext,
        int? NonRemovableIndex,
        string NonRemovableExistsMessage,
        string NonRemovableCollapsedMessage,
        int HistoryIndex,
        Action AssertAfterRemove,
        Action<ComboBox>? AfterRemove
    );

    private const string CatalogExistsMessage =
        "カタログ候補にも × ボタン自体はテンプレートから生成されること";
    private const string CatalogCollapsedMessage =
        "カタログ候補（IsRemovable=false）の × は非表示であること";

    /// <summary>候補が × なし（IsRemovable=false）なら × ボタンはあっても Collapsed であることを表明する</summary>
    private static void AssertRemoveButtonCollapsed(
        ComboBox combo,
        int index,
        string existsMessage,
        string collapsedMessage
    )
    {
        var button = FindRemoveButton(RealizeContainer(combo, index));
        button.Should().NotBeNull(existsMessage);
        button!.Visibility.Should().Be(Visibility.Collapsed, collapsedMessage);
    }

    /// <summary>ダイアログ構築用の VM を、メモリ上のキーストアで作る</summary>
    private static AiChatDialogViewModel CreateDialogVm(
        AiSettingsStore store,
        FakeCopilotRuntimeClient? copilotClient = null
    )
    {
        // API キーは実 %LOCALAPPDATA% の ApiKeyStore ではなくメモリ上のストアへ隔離する
        var keyStore = new InMemoryApiKeyStore();

        return new AiChatDialogViewModel(
            host: null,
            dispatcher: new SyncUiDispatcher(),
            settingsStore: store,
            codexClient: new FakeCodexAppServerClient(),
            copilotClient: copilotClient,
            apiKeyLoader: keyStore.Load,
            apiKeySaver: keyStore.Save
        );
    }

    /// <summary>接続方式ごとのシナリオを作る（履歴の仕込み・前提の確認を含む）</summary>
    private static ModelRemoveScenario CreateScenario(ModelRemoveKind kind, string folder)
    {
        var store = new AiSettingsStore(folder);

        switch (kind)
        {
            case ModelRemoveKind.Api:
            {
                // openai のカタログ外履歴を 1 件仕込んでおく（カタログの下に × 付きで並ぶ状態）
                var seeded = store.Load();
                seeded.ApiModelHistory.Touch("openai", "custom-model");
                store.Save(seeded);

                var vm = CreateDialogVm(store);

                // 前提: 既定プロバイダ（OpenAI）の候補はカタログ（× なし）＋履歴（× あり）の 2 層
                vm.Connection.ApiModelCandidates.Select(c => c.Name)
                    .Should()
                    .Equal([.. AiModelCatalog.OpenAiModels, "custom-model"]);

                return new ModelRemoveScenario(
                    ComboName: "ApiModelBox",
                    DialogVm: vm,
                    Candidates: vm.Connection.ApiModelCandidates,
                    DataContext: vm,
                    NonRemovableIndex: 0,
                    NonRemovableExistsMessage: CatalogExistsMessage,
                    NonRemovableCollapsedMessage: CatalogCollapsedMessage,
                    HistoryIndex: vm.Connection.ApiModelCandidates.Count - 1,
                    AssertAfterRemove: () =>
                    {
                        // 履歴項目のみ消え、カタログは残る（JSON 側も消える）
                        vm.Connection.ApiModelCandidates.Select(c => c.Name)
                            .Should()
                            .Equal(AiModelCatalog.OpenAiModels);
                        store.Load().ApiModelHistory.ModelsFor("openai").Should().BeEmpty();
                    },
                    AfterRemove: null
                );
            }

            case ModelRemoveKind.Codex:
            {
                // 履歴に 2 件を仕込み、非 openai プロバイダの候補が履歴のみになる状態を作る
                var seeded = store.Load();
                seeded.CodexModelHistory.Providers["test-provider"] = new List<string>
                {
                    "hist-model",
                    "hist-model-2",
                };
                store.Save(seeded);

                // 候補を決定的にするため config.toml を seam で注入して Connection を単体構築する
                var connection = new ChatConnectionSettingsViewModel(
                    AiDialogKind.AiChat,
                    settingsStore: store,
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

                return new ModelRemoveScenario(
                    ComboName: "CodexModelBox",
                    // ダイアログ構築用 VM は隔離ストアで生成する（テンプレート抽出にのみ使う）
                    DialogVm: CreateDialogVm(new AiSettingsStore(folder)),
                    Candidates: connection.CodexModelCandidates,
                    // DataContext は実ダイアログと同じ「Connection プロパティを持つ」形のホストにする
                    DataContext: new ConnectionHost(connection),
                    NonRemovableIndex: null,
                    NonRemovableExistsMessage: CatalogExistsMessage,
                    NonRemovableCollapsedMessage: CatalogCollapsedMessage,
                    HistoryIndex: 0,
                    AssertAfterRemove: () =>
                    {
                        // 実行した項目（先頭＝hist-model）のみ消える（JSON 側も消える）
                        connection
                            .CodexModelCandidates.Select(c => c.Name)
                            .Should()
                            .Equal("hist-model-2");
                        store
                            .Load()
                            .CodexModelHistory.ModelsFor("test-provider")
                            .Should()
                            .Equal("hist-model-2");
                    },
                    AfterRemove: combo =>
                    {
                        // openai プロバイダへ切替: 静的カタログ候補（IsRemovable=false）の × は Collapsed。
                        // 実際の画面と同じく、ドロップダウンを閉じてからプロバイダを切り替え、開き直して項目を作らせる
                        // （開いたまま候補を作り直すと、全件の並列実行の負荷の下で閉じることがあり、
                        // 閉じていると項目がレイアウトされず × ボタンが作られない）
                        combo.IsDropDownOpen = false;
                        DoEvents();
                        connection.CodexModelProvider = "openai";
                        OpenDropDown(combo);

                        AssertRemoveButtonCollapsed(
                            combo,
                            0,
                            CatalogExistsMessage,
                            CatalogCollapsedMessage
                        );
                    }
                );
            }

            case ModelRemoveKind.Copilot:
            {
                // 履歴に 2 件を仕込む（実行時列挙の下に × 付きで並ぶ状態）
                var seeded = store.Load();
                seeded.CopilotModelHistory.Providers[CopilotSettings.HistoryProviderKey] =
                    new List<string> { "hist-model", "hist-model-2" };
                store.Save(seeded);

                var connection = new ChatConnectionSettingsViewModel(
                    AiDialogKind.AiChat,
                    settingsStore: store,
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

                return new ModelRemoveScenario(
                    ComboName: "CopilotModelBox",
                    DialogVm: CreateDialogVm(
                        new AiSettingsStore(folder),
                        new FakeCopilotRuntimeClient()
                    ),
                    Candidates: connection.CopilotModelCandidates,
                    DataContext: new ConnectionHost(connection),
                    NonRemovableIndex: 0,
                    NonRemovableExistsMessage: "列挙候補にも × ボタン自体はテンプレートから生成されること",
                    NonRemovableCollapsedMessage: "実行時列挙の候補（IsRemovable=false）の × は非表示であること",
                    HistoryIndex: 1,
                    AssertAfterRemove: () =>
                    {
                        // 実行した履歴項目のみ消え、実行時列挙は残る（JSON 側も消える）
                        connection
                            .CopilotModelCandidates.Select(c => c.Name)
                            .Should()
                            .Equal("enum-model", "hist-model-2");
                        store
                            .Load()
                            .CopilotModelHistory.ModelsFor(CopilotSettings.HistoryProviderKey)
                            .Should()
                            .Equal("hist-model-2");
                    },
                    AfterRemove: null
                );
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>
    /// API キー・Codex・Copilot のモデル ComboBox で、ドロップダウン項目の × ボタンが、× なし候補（カタログ・実行時列挙）では
    /// Collapsed、履歴候補では Visible かつ Command 解決で、Execute すると履歴項目のみ消えることを、
    /// 実 XAML のテンプレートで検証する。
    /// </summary>
    /// <remarks>
    /// 接続方式ごとの違い（VM の作り方・候補・履歴の位置・削除後の期待）は <see cref="ModelRemoveScenario"/> に置く。
    /// Codex だけは、非 openai プロバイダの候補が履歴のみなので × なしの確認を先に行わず、
    /// 削除の後に openai プロバイダへ切り替えて静的カタログ候補の × が Collapsed であることを確かめる
    /// （候補を決定的にするため config.toml は seam で注入する）。
    /// Copilot は静的カタログを持たず、実行時列挙は接続後にしか得られないため、列挙相当の候補は
    /// <see cref="ChatConnectionSettingsViewModel.CopilotAvailableModels"/> へ直接流し込む。
    /// </remarks>
    [Theory(
        DisplayName = "モデルドロップダウンの × は × なし候補で非表示・履歴のみ表示され削除できる"
    )]
    [InlineData(ModelRemoveKind.Api)]
    [InlineData(ModelRemoveKind.Codex)]
    [InlineData(ModelRemoveKind.Copilot)]
    public void RemoveButton_VisibleOnlyForHistory_AndRemovesHistoryItem(ModelRemoveKind kind)
    {
        using var temp = new TestTempFolder();
        var folder = Path.Combine(temp.Path, "settings");

        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var scenario = CreateScenario(kind, folder);

            // 実ダイアログのモデル名 ComboBox から本物の ItemTemplate / ItemContainerStyle を取り出す
            // （ここに × ボタンの束縛が入っている＝検証対象は実 XAML のテンプレート。
            // 　BAML ロードは並列テストと競合しないよう直列化する）
            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new AiChatDialog(scenario.DialogVm, new RecordingShutdownFailureReporter())
            );
            var sourceCombo = dialog.FindName(scenario.ComboName) as ComboBox;
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
                ItemsSource = scenario.Candidates,
                ItemTemplate = itemTemplate,
                ItemContainerStyle = itemContainerStyle,
                DataContext = scenario.DataContext,
            };
            // 画面外＋非アクティブで Show し、テスト実行中にユーザーの画面を邪魔しない
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
                OpenDropDown(combo);

                // × なし候補: × ボタンは Collapsed
                if (scenario.NonRemovableIndex is { } nonRemovableIndex)
                {
                    AssertRemoveButtonCollapsed(
                        combo,
                        nonRemovableIndex,
                        scenario.NonRemovableExistsMessage,
                        scenario.NonRemovableCollapsedMessage
                    );
                }

                // 履歴候補: × ボタンが Visible・Command 解決・Execute で削除できる
                var removeButton = FindRemoveButton(RealizeContainer(combo, scenario.HistoryIndex));
                removeButton
                    .Should()
                    .NotBeNull("履歴候補の × ボタンがテンプレートから生成されていること");
                removeButton!
                    .Visibility.Should()
                    .Be(Visibility.Visible, "履歴候補（IsRemovable=true）の × は表示されること");

                // 誤束縛（RelativeSource 解決失敗）だと Command が null になり × が無反応になる
                removeButton
                    .Command.Should()
                    .NotBeNull("× ボタンの Command が RelativeSource 経由で解決されていること");

                removeButton.Command!.Execute(removeButton.CommandParameter);

                scenario.AssertAfterRemove();
                scenario.AfterRemove?.Invoke(combo);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Copilot モデル ComboBox で、リストから選択したモデルが「成功ターンの MRU 記録」と
    /// 「実行時列挙の更新」の候補再構築後も空へ戻らないことを検証する（実機報告の回帰テスト）。
    /// </summary>
    /// <remarks>
    /// 候補の Clear で編集可能 ComboBox は選択中項目を失い、Text（＝双方向バインドの CopilotModel）を
    /// 空へ押し戻す。Copilot は成功ターンごとに必ず記録→再構築が走るため（静的カタログ無し）、
    /// 再構築側の退避・復元が無いと「チャットするたびに選択モデルが空欄へ戻る」。この機構は
    /// ComboBox の実挙動に依存するため、実ダイアログと同じ束縛（編集可能・TextSearch.TextPath=Name・
    /// Text は CopilotModel と双方向）を組んだ実 ComboBox で検証する。
    /// </remarks>
    [Fact(DisplayName = "Copilot の選択モデルは成功ターン記録・列挙更新後も空へ戻らない")]
    public void CopilotSelectedModel_SurvivesRecordAndReenumeration()
    {
        using var temp = new TestTempFolder();
        var folder = Path.Combine(temp.Path, "settings");

        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var connection = new ChatConnectionSettingsViewModel(
                AiDialogKind.AiChat,
                settingsStore: new AiSettingsStore(folder),
                codexConfigReader: () => new CodexConfigToml(),
                apiKeyLoader: _ => string.Empty,
                apiKeySaver: (_, _) => { }
            );
            connection.LoadSettings();
            connection.SelectedBackend = ErChatBackendKind.Copilot;
            connection.CopilotAvailableModels = new List<string> { "enum-model", "enum-model-2" };

            // 実ダイアログの CopilotModelBox と同じ束縛を最小ウィンドウの ComboBox へ組む
            var combo = new ComboBox
            {
                IsEditable = true,
                ItemsSource = connection.CopilotModelCandidates,
                DataContext = new ConnectionHost(connection),
            };
            TextSearch.SetTextPath(combo, "Name");
            combo.SetBinding(
                ComboBox.TextProperty,
                new Binding("Connection.CopilotModel")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                }
            );
            // 画面外＋非アクティブで Show し、テスト実行中にユーザーの画面を邪魔しない
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
            window.Show();

            try
            {
                window.UpdateLayout();
                DoEvents();

                // 実機の報告手順どおり、ドロップダウン項目の選択でモデルを決める
                combo.SelectedItem = connection.CopilotModelCandidates[0];
                DoEvents();
                connection
                    .CopilotModel.Should()
                    .Be("enum-model", "リスト選択が Text 経由で VM へ届くこと");

                // 成功ターン → MRU 記録 → 候補再構築（修正前はここで Text が空へ戻っていた）
                connection.RecordSuccessfulModel();
                DoEvents();
                connection
                    .CopilotModel.Should()
                    .Be("enum-model", "MRU 記録の候補再構築で選択モデルが消えないこと");
                combo.Text.Should().Be("enum-model");

                // 再接続などによる実行時列挙の更新でも保たれる
                connection.CopilotAvailableModels = new List<string>
                {
                    "enum-model",
                    "enum-model-3",
                };
                DoEvents();
                connection
                    .CopilotModel.Should()
                    .Be("enum-model", "実行時列挙の更新で選択モデルが消えないこと");
                combo.Text.Should().Be("enum-model");
            }
            finally
            {
                window.Close();
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

    /// <summary>ドロップダウンを開き、先頭の項目が作られて画面に載るまで待つ</summary>
    /// <remarks>
    /// 全件の並列実行の負荷の下では、開いた直後のドロップダウンが、このテストの外の要因で閉じることがある
    /// （何が閉じるのかは特定できていない。別スレッドのウィンドウのアクティブ化・マウスのキャプチャでは閉じないことを確かめた）。
    /// 閉じていると項目がレイアウトされず、テンプレートの中の × ボタンが作られない。このテストが確かめるのは項目の
    /// テンプレートの束縛なので、開いた状態で項目が揃うまで開き直す
    /// </remarks>
    private static void OpenDropDown(ComboBox combo)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            combo.IsDropDownOpen = true;
            combo.UpdateLayout();
            DoEvents();

            if (
                combo.IsDropDownOpen
                && combo.ItemContainerGenerator.ContainerFromIndex(0)
                    is ComboBoxItem { IsLoaded: true }
            )
            {
                return;
            }
        }

        combo.IsDropDownOpen.Should().BeTrue("ドロップダウンが開いて項目が作られる");
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
