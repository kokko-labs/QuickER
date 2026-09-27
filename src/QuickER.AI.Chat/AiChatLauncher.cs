using QuickER.Extensibility;
using QuickER.Gui.Abstractions;

namespace QuickER.AI.Chat;

/// <summary>AI チャットウィンドウ（モードレス・シングルトン）の生存期間を管理するインターフェイス</summary>
/// <remarks>
/// モーダルダイアログとは異なり、表示しっぱなしで再利用される単一ウィンドウのライフサイクルをここに隔離する。
/// 操作対象の ER 図は、コンストラクタ注入された <see cref="IErDiagramHost"/> 契約から得る
/// （アプリ本体の具象 ViewModel には依存しない）。
/// </remarks>
public interface IAiChatLauncher
{
    /// <summary>AI チャットウィンドウを開く（既存があれば再利用し、前面へ出す）</summary>
    void Open();

    /// <summary>AI チャットウィンドウを実際に閉じる（アプリ終了時などに呼ぶ）</summary>
    void Close();
}

/// <summary><c>AiChatDialog</c> を保持・再利用する <see cref="IAiChatLauncher"/> の既定実装</summary>
/// <remarks>
/// 操作対象の ER 図能力は <see cref="IErDiagramHost"/> 契約から取り、
/// <see cref="ErDiagramHostChatAdapter"/> でチャット固有の <see cref="IErDiagramChatHost"/> へ適合させる。
/// </remarks>
public sealed class AiChatLauncher : IAiChatLauncher
{
    private readonly IErDiagramHost _host;

    /// <summary>終了の連鎖で受け止めた失敗の記録先（ダイアログへ引き渡す）</summary>
    private readonly IShutdownFailureReporter _shutdownFailureReporter;

    /// <summary>シングルトンの AI チャットウィンドウ（未生成時は null）</summary>
    private AiChatDialog? _dialog;

    /// <summary>操作対象の ER 図能力を提供する <see cref="IErDiagramHost"/> を注入して生成する</summary>
    /// <param name="host">操作対象の ER 図能力</param>
    /// <param name="shutdownFailureReporter">
    /// アプリ終了時の後始末で受け止めた失敗の記録先。必須依存にして、登録漏れを起動時の例外で表に出す
    /// （黙って捨てる既定を置くと、終了の連鎖が壊れても誰も気付けない）
    /// </param>
    public AiChatLauncher(IErDiagramHost host, IShutdownFailureReporter shutdownFailureReporter)
    {
        ArgumentNullException.ThrowIfNull(shutdownFailureReporter);

        _host = host;
        _shutdownFailureReporter = shutdownFailureReporter;
    }

    /// <inheritdoc />
    public void Open()
    {
        if (_dialog is null)
        {
            var chatHost = new ErDiagramHostChatAdapter(_host);
            var viewModel = new AiChatDialogViewModel(chatHost);
            _dialog = new AiChatDialog(viewModel, _shutdownFailureReporter);
        }

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
