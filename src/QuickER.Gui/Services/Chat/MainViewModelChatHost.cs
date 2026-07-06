using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.ViewModels;

namespace QuickER.Services.Chat;

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
