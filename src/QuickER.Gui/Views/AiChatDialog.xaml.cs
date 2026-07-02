using System.Windows;
using System.Windows.Controls;
using QuickER.AI;
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

    /// <summary>注入された ViewModel を結び付けてウィンドウを生成する</summary>
    public AiChatDialog(AiChatDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        ViewModel.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
    }

    /// <summary>初回表示時に ViewModel を初期化する（API キーは添付ビヘイビアが PasswordBox へ同期する）</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
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
    /// タブ選択を接続方式へ変換し、切替可否を ViewModel に委ねる。
    /// 切替できた場合は確定インデックスを更新し、拒否（会話クリアのキャンセル）なら元のタブへ戻す。
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

        var newBackend = newIndex switch
        {
            1 => ErChatBackendKind.Codex,
            2 => ErChatBackendKind.ClaudeCode,
            _ => ErChatBackendKind.ApiKey,
        };

        if (ViewModel.TryChangeBackend(newBackend))
        {
            _committedBackendIndex = newIndex;
        }
        else
        {
            RevertBackendSelection();
        }
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
