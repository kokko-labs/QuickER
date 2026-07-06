using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.ViewModels;

namespace QuickER.Services.Chat;

/// <summary><see cref="MainViewModel"/> をラップして <see cref="IMockDiagramSource"/> を提供するアダプタ</summary>
public sealed class MainViewModelMockDiagramSource : IMockDiagramSource
{
    private readonly MainViewModel _viewModel;

    /// <summary>供給元の MainViewModel を指定して生成する</summary>
    public MainViewModelMockDiagramSource(MainViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    /// <inheritdoc />
    public bool IsEmpty => _viewModel.Entities.Count == 0;

    /// <inheritdoc />
    public ErDiagram GetDiagram() => _viewModel.ToDiagramModel();

    /// <inheritdoc />
    public DatabaseProviderRegistry Providers => _viewModel.Providers;
}
