using System.Text.Json;
using QuickER.Extensibility;
using QuickER.Model;
using QuickER.Provider;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>
/// アプリ本体の <see cref="MainViewModel"/> を包み、着脱可能な機能モジュールへ
/// <see cref="IErDiagramHost"/> 契約を提供するホスト実装。
/// </summary>
/// <remarks>
/// 依存方向を「プラグイン → 契約 ← ホスト」に逆転させる切断面のホスト側実装。
/// フィーチャーモジュール（QuickER.AI.Chat / QuickER.AI.Mock）は本クラスを直接知らず、
/// <see cref="IErDiagramHost"/> 契約越しに ER 図を操作する。
/// <see cref="ExecuteTool"/> は <see cref="Model.Entity"/> 等の可変状態を書き換えるため、
/// 必ず UI スレッド上で呼び出すこと。
/// </remarks>
public sealed class MainViewModelErDiagramHost : IErDiagramHost
{
    private readonly MainViewModel _viewModel;

    /// <summary>操作対象の MainViewModel を指定して生成する</summary>
    public MainViewModelErDiagramHost(MainViewModel viewModel)
    {
        _viewModel = viewModel;

        // VM の列リネーム通知を、契約イベントとしてそのまま中継する（引数の変換はしない単純リレー）
        _viewModel.ColumnRenamed += (_, e) => ColumnRenamed?.Invoke(this, e);

        // VM の方言切替通知も、契約イベントとしてそのまま中継する（ColumnRenamed と同じリレー方式）
        _viewModel.TargetDbmsChanged += (_, e) => TargetDbmsChanged?.Invoke(this, e);
    }

    /// <inheritdoc />
    public event EventHandler<ColumnRenamedEventArgs>? ColumnRenamed;

    /// <inheritdoc />
    public event EventHandler? TargetDbmsChanged;

    /// <inheritdoc />
    public bool IsEmpty => _viewModel.Entities.Count == 0;

    /// <inheritdoc />
    public ErDiagram GetDiagram() => _viewModel.ToDiagramModel();

    /// <inheritdoc />
    public DatabaseProviderRegistry Providers => _viewModel.Providers;

    /// <inheritdoc />
    public void AutoArrangeNewDiagram() => _viewModel.AutoArrangeNewDiagram();

    /// <inheritdoc />
    public void ReplaceQueries(IReadOnlyList<QueryDefinition> queries) =>
        _viewModel.ReplaceQueries(queries);

    /// <inheritdoc />
    public void ReplaceDiagram(ErDiagram diagram) => _viewModel.ReplaceDiagramFromModule(diagram);

    /// <inheritdoc />
    public string TargetDbms => _viewModel.CurrentProvider.Name;

    /// <inheritdoc />
    /// <remarks>必ず UI スレッドで呼び出すこと（ObservableCollection を変更するため）</remarks>
    public (string Result, bool Success) ExecuteTool(string toolName, string argumentsJson)
    {
        JsonElement arguments;

        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson
            );
            arguments = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return (
                string.Format(QuickER.Resources.Strings.Tool_InvalidArgumentsJson, toolName),
                false
            );
        }

        var (result, success) = ErDiagramDynamicTools.Execute(toolName, arguments, _viewModel);
        _viewModel.RefreshCanvasSize();
        return (result, success);
    }
}
