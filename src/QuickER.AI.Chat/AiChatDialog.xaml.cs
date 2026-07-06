using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickER.AI;
using QuickER.AI.UI;

namespace QuickER.AI.Chat;

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
        ApplyInitialBackendTab();
    }

    /// <summary>
    /// 前回使った接続タブを復元する。SelectedIndex の変更で通常のタブ切替処理
    /// （<see cref="BackendTabs_SelectionChanged"/>）が走り、切替経路を一本に保つ。
    /// </summary>
    private void ApplyInitialBackendTab()
    {
        var index = ViewModel.InitialBackend switch
        {
            ErChatBackendKind.Codex => 1,
            ErChatBackendKind.ClaudeCode => 2,
            _ => 0,
        };

        if (index != BackendTabs.SelectedIndex)
        {
            BackendTabs.SelectedIndex = index;
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

    // ── 添付: ボタン・Ctrl+V・ドラッグ&ドロップ ──

    /// <summary>クリップボタン押下でファイル選択を開き、選択ファイルを添付へ取り込む</summary>
    private void Attachments_AttachRequested(object sender, RoutedEventArgs e) =>
        ViewModel.PickAndAddAttachments();

    /// <summary>
    /// 入力欄フォーカス時の Ctrl+V を捕捉し、クリップボードに画像があればチップ化する。
    /// 画像が無ければ何もせず（テキスト貼付の既定動作を妨げない）。
    /// </summary>
    private void UserInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (!ViewModel.Attachments.IsEnabled)
        {
            return;
        }

        var png = ClipboardImageAttachmentReader.TryGetClipboardPng();

        if (png is null)
        {
            // 画像が無ければ既定のテキスト貼付に委ねる
            return;
        }

        ViewModel.Attachments.AddClipboardImage(png, DateTime.Now);
        // 画像を取り込んだので既定のテキスト貼付は抑止する
        e.Handled = true;
    }

    /// <summary>
    /// チャット領域上のファイルドラッグをトンネル段で先取りして受け入れる。
    /// バブリング段だとメッセージバブル（コピー可能な TextBox）の組み込みドラッグ処理に
    /// 飲み込まれ、バブルの上へのドロップが効かなくなる。テキスト等のドラッグは既定動作に任せる。
    /// </summary>
    private void ChatArea_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Effects = ViewModel.Attachments.IsEnabled ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>ドロップされたファイルを添付へ取り込む（非対応種別は VM がステータス通知する）</summary>
    private void ChatArea_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            ViewModel.AddDroppedFiles(paths);
        }

        e.Handled = true;
    }
}
