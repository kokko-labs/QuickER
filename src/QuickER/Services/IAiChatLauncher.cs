using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>AI チャットウィンドウ（モードレス・シングルトン）の生存期間を管理するインターフェース</summary>
/// <remarks>
/// モーダルダイアログ（<see cref="IAppDialogService"/>）とは異なり、表示しっぱなしで再利用される
/// 単一ウィンドウのライフサイクルをここに隔離し、ViewModel から <c>Views.AiChatDialog</c> 参照を除去する。
/// </remarks>
public interface IAiChatLauncher
{
    /// <summary>AI チャットウィンドウを開く（既存があれば再利用し、前面へ出す）</summary>
    /// <param name="host">AI ツール操作の対象となる主 ViewModel</param>
    void Open(MainViewModel host);

    /// <summary>AI チャットウィンドウを実際に閉じる（アプリ終了時などに呼ぶ）</summary>
    void Close();
}

/// <summary><c>Views.AiChatDialog</c> を保持・再利用する <see cref="IAiChatLauncher"/> の既定実装</summary>
public sealed class AiChatLauncher : IAiChatLauncher
{
    /// <summary>シングルトンの AI チャットウィンドウ（未生成時は null）</summary>
    private Views.AiChatDialog? _dialog;

    /// <inheritdoc />
    public void Open(MainViewModel host)
    {
        _dialog ??= new Views.AiChatDialog(host);
        _dialog.Owner = null;
        _dialog.Show();
        _dialog.Activate();
    }

    /// <inheritdoc />
    public void Close()
    {
        _dialog?.ForceClose();
        _dialog = null;
    }
}
