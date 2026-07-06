using QuickER.Model;
using QuickER.Provider;
using QuickER.ViewModels;

namespace QuickER.Services.Chat;

/// <summary>
/// モック生成 ViewModel が必要とする「現在の ER 図」の供給元を抽象化するインターフェース。
/// </summary>
/// <remarks>
/// <see cref="MockGenerationDialogViewModel"/> を巨大な <see cref="MainViewModel"/> 具象から切り離し、
/// スタブ注入による単体テストを可能にする（AI チャットの <see cref="IErDiagramChatHost"/> と同じ発想）。
/// </remarks>
public interface IMockDiagramSource
{
    /// <summary>ダイアグラムにエンティティが 1 つも無いか（生成開始の空判定に使用）</summary>
    bool IsEmpty { get; }

    /// <summary>現在の ER 図を意味モデル（<see cref="ErDiagram"/>・視覚情報なし）として取得する</summary>
    ErDiagram GetDiagram();

    /// <summary>WPF モック生成の型解決に使う DB プロバイダレジストリ</summary>
    DatabaseProviderRegistry Providers { get; }
}

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
