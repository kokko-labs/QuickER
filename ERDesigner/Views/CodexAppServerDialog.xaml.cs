using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ERDesigner.Services;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>Codex App Server の接続設定・認証・対話を行うウィンドウです。</summary>
public partial class CodexAppServerDialog : Window
{
    /// <summary>このウィンドウの ViewModel です。</summary>
    public CodexAppServerDialogViewModel ViewModel { get; }

    /// <summary>アプリ終了など強制クローズ中かどうかです。</summary>
    private bool _isForceClosing;

    /// <summary>新しいウィンドウを生成します（MainViewModel なし）。</summary>
    public CodexAppServerDialog()
        : this(null) { }

    /// <summary>MainViewModel を受け取って新しいウィンドウを生成します。</summary>
    public CodexAppServerDialog(MainViewModel? mainViewModel)
    {
        InitializeComponent();
        ViewModel = new CodexAppServerDialogViewModel(client: null, settingsStore: null, apiKeyStoreName: "CodexAppServerApiKey", mainViewModel: mainViewModel);
        DataContext = ViewModel;

        // メッセージが追加されたら末尾へスクロールする
        ViewModel.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        Loaded += OnLoaded;

        // ×ボタンで閉じる代わりに非表示にする（状態を維持するシングルトン動作）
        Closing += OnWindowClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();

        if (!string.IsNullOrEmpty(ViewModel.ApiKey))
        {
            ApiKeyBox.Password = ViewModel.ApiKey;
        }
    }

    /// <summary>ウィンドウを閉じる代わりに非表示にして状態を維持します。</summary>
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

    /// <summary>アプリ終了時などに強制的にウィンドウを閉じます。</summary>
    public void ForceClose()
    {
        _isForceClosing = true;
        ViewModel.SaveSettingsPublic();
        Close();
    }

    /// <summary>PasswordBox の変更を ViewModel に転送します。</summary>
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel.ApiKey = passwordBox.Password;
        }
    }

    /// <summary>Ctrl+Enter でメッセージを送信します。</summary>
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

    private void ScrollToBottom()
    {
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
