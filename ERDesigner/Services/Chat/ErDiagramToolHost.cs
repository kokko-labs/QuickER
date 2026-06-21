using System.Text.Json;
using ERDesigner.ViewModels;

namespace ERDesigner.Services.Chat;

/// <summary>AI のツール呼び出しを MainViewModel 上の ER 図操作へ橋渡しする本番ホスト</summary>
/// <remarks>必ず UI スレッドで呼び出すこと（ObservableCollection を変更するため）</remarks>
public sealed class ErDiagramToolHost : IErDiagramToolHost
{
    private readonly MainViewModel _mainViewModel;

    /// <summary>操作対象の MainViewModel を指定して生成する</summary>
    public ErDiagramToolHost(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    /// <inheritdoc />
    public (string Result, bool Success) Execute(string toolName, string argumentsJson)
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
            return ($"ツール '{toolName}' の引数 JSON を解釈できませんでした。", false);
        }

        var (result, success) = ErDiagramDynamicTools.Execute(toolName, arguments, _mainViewModel);
        _mainViewModel.RefreshCanvasSize();
        return (result, success);
    }
}
