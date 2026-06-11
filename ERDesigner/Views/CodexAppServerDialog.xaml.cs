using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ERDesigner.Services;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>Codex App Server の接続設定・認証・対話を行うウィンドウのコードビハインド</summary>
public partial class CodexAppServerDialog : Window
{
    /// <summary>このウィンドウの ViewModel</summary>
    public CodexAppServerDialogViewModel ViewModel { get; }

    /// <summary>アプリ終了などで強制クローズ中かどうか（×ボタンの非表示化を抑止する）</summary>
    private bool _isForceClosing;

    /// <summary>MainViewModel を伴わずにウィンドウを生成する</summary>
    public CodexAppServerDialog()
        : this(null) { }

    /// <summary>MainViewModel を受け取ってウィンドウを生成し、初回自動接続を開始する</summary>
    public CodexAppServerDialog(MainViewModel? mainViewModel)
    {
        InitializeComponent();
        ViewModel = new CodexAppServerDialogViewModel(client: null, settingsStore: null, apiKeyStoreName: "CodexAppServerApiKey", mainViewModel: mainViewModel);
        ViewModel.BeginInitialAutoConnect();
        DataContext = ViewModel;

        // メッセージが追加されたら末尾へスクロールする
        ViewModel.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        Loaded += OnLoaded;

        // ×ボタンで閉じる代わりに非表示にする（状態を維持するシングルトン動作）
        Closing += OnWindowClosing;
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
        ViewModel.SaveSettingsPublic();
        Hide();
    }

    /// <summary>アプリ終了時などにウィンドウを実際に閉じる（非表示化の抑止フラグを立ててから閉じる）</summary>
    public void ForceClose()
    {
        _isForceClosing = true;
        ViewModel.SaveSettingsPublic();
        Close();
    }

    /// <summary>PasswordBox の変更内容を ViewModel へ転送する</summary>
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
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (ViewModel.SendMessageCommand.CanExecute(null))
            {
                ViewModel.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    /// <summary>チャット表示を最下部へスクロールする（メッセージ追加時の追従用）</summary>
    private void ScrollToBottom()
    {
        // レイアウト反映後にスクロールするよう低優先度でディスパッチする
        Dispatcher.InvokeAsync(
            () =>
            {
                if (ChatScrollViewer is not null)
                {
                    ChatScrollViewer.ScrollToEnd();
                }
            },
            System.Windows.Threading.DispatcherPriority.Background
        );
    }
}
