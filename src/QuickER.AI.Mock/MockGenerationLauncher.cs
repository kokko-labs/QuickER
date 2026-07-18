using QuickER.Extensibility;

namespace QuickER.AI.Mock;

/// <summary>AI モック生成ウィンドウ（モードレス・シングルトン）の生存期間を管理するインターフェース</summary>
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

    /// <summary>シングルトンのモック生成ウィンドウ（未生成時は null）</summary>
    private MockGenerationDialog? _dialog;

    /// <summary>現在の ER 図を供給する <see cref="IErDiagramHost"/> を注入して生成する</summary>
    public MockGenerationLauncher(IErDiagramHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public void Open()
    {
        if (_dialog is null)
        {
            var source = new ErDiagramHostMockDiagramSource(_host);
            var viewModel = new MockGenerationDialogViewModel(source);
            _dialog = new MockGenerationDialog(viewModel);
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
