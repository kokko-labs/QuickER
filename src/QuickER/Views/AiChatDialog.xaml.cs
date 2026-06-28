using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickER.Services.Chat;
using QuickER.ViewModels;

namespace QuickER.Views;

/// <summary>AI チャット（API キー接続 / Codex 接続）の統合ウィンドウのコードビハインド</summary>
public partial class AiChatDialog : Window
{
    /// <summary>このウィンドウの ViewModel</summary>
    public AiChatDialogViewModel ViewModel { get; }

    /// <summary>アプリ終了などで強制クローズ中かどうか（×ボタンの非表示化を抑止する）</summary>
    private bool _isForceClosing;

    /// <summary>MainViewModel を伴わずにウィンドウを生成する</summary>
    public AiChatDialog()
        : this(null) { }

    /// <summary>MainViewModel を受け取ってウィンドウを生成する</summary>
    public AiChatDialog(MainViewModel? mainViewModel)
    {
        InitializeComponent();
        ViewModel = new AiChatDialogViewModel(mainViewModel);
        DataContext = ViewModel;

        ViewModel.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
    }

    /// <summary>ViewModel 側で API キーが変化（プロバイダー切替時の読み直し等）したら PasswordBox へ反映する</summary>
    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
    {
        if (
            e.PropertyName == nameof(AiChatDialogViewModel.ApiKey)
            && ApiKeyBox.Password != ViewModel.ApiKey
        )
        {
            ApiKeyBox.Password = ViewModel.ApiKey;
        }
    }

    /// <summary>初回表示時に ViewModel を初期化し、保存済み API キーを PasswordBox へ反映する</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();

        if (!string.IsNullOrEmpty(ViewModel.ApiKey))
        {
            ApiKeyBox.Password = ViewModel.ApiKey;
        }
    }

    /// <summary>×ボタンでは閉じず、設定を保存して非表示にし状態を維持する（シングルトン動作）</summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isForceClosing)
        {
            return;
        }

        e.Cancel = true;
        ViewModel.SaveSettings();
        Hide();
    }

    /// <summary>アプリ終了時などにウィンドウを実際に閉じる</summary>
    public void ForceClose()
    {
        _isForceClosing = true;
        ViewModel.SaveSettings();
        Close();
    }

    /// <summary>確定済み（切り替え済み）の接続タブのインデックス</summary>
    private int _committedBackendIndex;

    /// <summary>タブ選択の復帰中か（復帰による再入を無視するためのガード）</summary>
    private bool _revertingBackendTab;

    /// <summary>
    /// タブ選択に応じて接続方式を切り替える。会話中はクリア確認を出し、
    /// OK の場合は会話をクリアして切り替え、キャンセルの場合は元のタブへ戻す。
    /// </summary>
    private void BackendTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 子 Selector（ComboBox 等）のバブリングや、復帰による再入は無視する
        if (!ReferenceEquals(e.OriginalSource, BackendTabs) || _revertingBackendTab)
        {
            return;
        }

        var newIndex = BackendTabs.SelectedIndex;

        if (newIndex == _committedBackendIndex)
        {
            return;
        }

        if (ViewModel.HasConversation)
        {
            var result = MessageBox.Show(
                this,
                "現在の会話をクリアして接続方式を切り替えますか？",
                "会話のクリア",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.OK)
            {
                RevertBackendSelection();
                return;
            }

            ViewModel.ClearConversation();
        }

        _committedBackendIndex = newIndex;
        ViewModel.SelectedBackend = newIndex switch
        {
            1 => ErChatBackendKind.Codex,
            2 => ErChatBackendKind.ClaudeCode,
            _ => ErChatBackendKind.ApiKey,
        };
    }

    /// <summary>
    /// 確定済みタブへ選択を戻す。<see cref="SelectionChanged"/> 処理中・モーダルループ直後の
    /// 同期的な選択戻しは TabControl の選択状態を不整合にし、以降のクリックで再発火するため、
    /// イベント完了後にディスパッチャ経由で戻し、戻し中の再入はガードで無視する。
    /// </summary>
    private void RevertBackendSelection()
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _revertingBackendTab = true;
                try
                {
                    BackendTabs.SelectedIndex = _committedBackendIndex;
                }
                finally
                {
                    _revertingBackendTab = false;
                }
            })
        );
    }

    /// <summary>API キー接続の PasswordBox 変更を ViewModel へ転送する</summary>
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel.ApiKey = passwordBox.Password;
        }
    }

    /// <summary>Ctrl+Enter でメッセージを送信する</summary>
    private void UserInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (
            e.Key == Key.Enter
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
        )
        {
            if (ViewModel.SendMessageCommand.CanExecute(null))
            {
                ViewModel.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    /// <summary>チャット表示を最下部へスクロールする</summary>
    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(
            () =>
            {
                ChatScrollViewer?.ScrollToEnd();
            },
            System.Windows.Threading.DispatcherPriority.Background
        );
    }
}
