using QuickER.AI;
using QuickER.ViewModels;

namespace QuickER.Services.Chat;

/// <summary>
/// AI チャット ViewModel が操作対象のダイアグラムに対して必要とする最小限の能力を抽象化するインターフェース。
/// </summary>
/// <remarks>
/// <see cref="AiChatDialogViewModel"/> を巨大な <see cref="MainViewModel"/> 具象から切り離し、
/// スタブ注入による単体テストを可能にする。AI のツール実行は <see cref="IErDiagramToolHost"/> が担う。
/// </remarks>
public interface IErDiagramChatHost
{
    /// <summary>AI のツール呼び出しをダイアグラム操作へ橋渡しするホスト</summary>
    IErDiagramToolHost ToolHost { get; }

    /// <summary>ダイアグラムにエンティティが 1 つも無いかどうか（ターン開始時の空判定に使用）</summary>
    bool IsEmpty { get; }

    /// <summary>新規生成されたダイアグラムを自動整列する</summary>
    void AutoArrangeNewDiagram();
}

/// <summary><see cref="MainViewModel"/> をラップして <see cref="IErDiagramChatHost"/> を提供するアダプタ</summary>
public sealed class MainViewModelChatHost : IErDiagramChatHost
{
    private readonly MainViewModel _viewModel;
    private readonly ErDiagramToolHost _toolHost;

    /// <summary>操作対象の MainViewModel を指定して生成する</summary>
    public MainViewModelChatHost(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _toolHost = new ErDiagramToolHost(viewModel);
    }

    /// <inheritdoc />
    public IErDiagramToolHost ToolHost => _toolHost;

    /// <inheritdoc />
    public bool IsEmpty => _viewModel.Entities.Count == 0;

    /// <inheritdoc />
    public void AutoArrangeNewDiagram() => _viewModel.AutoArrangeNewDiagram();
}
