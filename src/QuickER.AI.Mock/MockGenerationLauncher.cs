using QuickER.Extensibility;
using QuickER.Gui.Abstractions;

namespace QuickER.AI.Mock;

/// <summary>AI モック生成ウィンドウ（モードレス・シングルトン）の生存期間を管理するインターフェイス</summary>
/// <remarks>
/// <see cref="AiChatLauncher"/> と同じパターンで、表示しっぱなしで再利用される単一ウィンドウの
/// ライフサイクルをここに隔離する。現在の ER 図は、コンストラクタ注入された <see cref="IErDiagramHost"/>
/// 契約から得る（アプリ本体の具象 ViewModel には依存しない）。
/// </remarks>
public interface IMockGenerationLauncher
{
    /// <summary>モック生成ウィンドウを開く（既存があれば再利用し、前面へ出す）</summary>
    void Open();

    /// <summary>モック生成ウィンドウを実際に閉じる（アプリ終了時などに呼ぶ）</summary>
    void Close();
}

/// <summary><c>MockGenerationDialog</c> を保持・再利用する <see cref="IMockGenerationLauncher"/> の既定実装</summary>
/// <remarks>
/// 現在の ER 図の供給元は <see cref="IErDiagramHost"/> 契約から取り、
/// <see cref="ErDiagramHostMockDiagramSource"/> でモック固有の <see cref="IMockDiagramSource"/> へ適合させる。
/// </remarks>
public sealed class MockGenerationLauncher : IMockGenerationLauncher
{
    private readonly IErDiagramHost _host;

    /// <summary>終了の連鎖で受け止めた失敗の記録先（ダイアログへ引き渡す）</summary>
    private readonly IShutdownFailureReporter _shutdownFailureReporter;

    /// <summary>シングルトンのモック生成ウィンドウ（未生成時は null）</summary>
    private MockGenerationDialog? _dialog;

    /// <summary>現在の ER 図を供給する <see cref="IErDiagramHost"/> を注入して生成する</summary>
    /// <param name="host">現在の ER 図の供給元</param>
    /// <param name="shutdownFailureReporter">
    /// アプリ終了時の後始末で受け止めた失敗の記録先。必須依存にして、登録漏れを起動時の例外で表に出す
    /// （黙って捨てる既定を置くと、終了の連鎖が壊れても誰も気付けない）
    /// </param>
    public MockGenerationLauncher(
        IErDiagramHost host,
        IShutdownFailureReporter shutdownFailureReporter
    )
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
            var source = new ErDiagramHostMockDiagramSource(_host);
            var viewModel = new MockGenerationDialogViewModel(source);
            _dialog = new MockGenerationDialog(viewModel, _shutdownFailureReporter);
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
