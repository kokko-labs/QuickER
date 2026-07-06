using QuickER.Services.Chat;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>AI モック生成ウィンドウ（モードレス・シングルトン）の生存期間を管理するインターフェース</summary>
/// <remarks>
/// <see cref="IAiChatLauncher"/> と同じパターンで、表示しっぱなしで再利用される単一ウィンドウの
/// ライフサイクルをここに隔離し、ViewModel から <c>Views.MockGenerationDialog</c> 参照を除去する。
/// </remarks>
public interface IMockGenerationLauncher
{
    /// <summary>モック生成ウィンドウを開く（既存があれば再利用し、前面へ出す）</summary>
    /// <param name="host">現在の ER 図を供給する主 ViewModel</param>
    void Open(MainViewModel host);

    /// <summary>モック生成ウィンドウを実際に閉じる（アプリ終了時などに呼ぶ）</summary>
    void Close();
}

/// <summary><c>Views.MockGenerationDialog</c> を保持・再利用する <see cref="IMockGenerationLauncher"/> の既定実装</summary>
public sealed class MockGenerationLauncher : IMockGenerationLauncher
{
    /// <summary>シングルトンのモック生成ウィンドウ（未生成時は null）</summary>
    private Views.MockGenerationDialog? _dialog;

    /// <inheritdoc />
    public void Open(MainViewModel host)
    {
        if (_dialog is null)
        {
            var source = new MainViewModelMockDiagramSource(host);
            var viewModel = new MockGenerationDialogViewModel(source);
            _dialog = new Views.MockGenerationDialog(viewModel);
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
