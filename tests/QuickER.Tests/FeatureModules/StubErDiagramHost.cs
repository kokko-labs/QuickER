using QuickER.Extensibility;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.FeatureModules;

/// <summary>
/// フィーチャーモジュールのテスト用に、契約 <see cref="IErDiagramHost"/> をスタブ化した検証用ダブル。
/// </summary>
/// <remarks>
/// 委譲の観測用に、返す値と呼び出し記録（<see cref="AutoArrangeCallCount"/> /
/// <see cref="LastToolName"/> など）を公開する。実 UI・実 ViewModel には一切依存しない。
/// </remarks>
internal sealed class StubErDiagramHost : IErDiagramHost
{
    /// <summary>プロパティ・メソッドが返す固定のダイアグラム</summary>
    public ErDiagram DiagramToReturn { get; init; } = new();

    /// <summary><see cref="IsEmpty"/> が返す値</summary>
    public bool IsEmptyToReturn { get; init; }

    /// <summary><see cref="ExecuteTool"/> が返す結果テキストと成否</summary>
    public (string Result, bool Success) ToolResultToReturn { get; init; } = ("ok", true);

    /// <summary><see cref="Providers"/> が返すレジストリ（既定は空）</summary>
    public DatabaseProviderRegistry ProvidersToReturn { get; init; } =
        new(Array.Empty<IDatabaseProvider>());

    /// <summary><see cref="AutoArrangeNewDiagram"/> が呼ばれた回数</summary>
    public int AutoArrangeCallCount { get; private set; }

    /// <summary>直近の <see cref="ExecuteTool"/> に渡されたツール名</summary>
    public string? LastToolName { get; private set; }

    /// <summary>直近の <see cref="ExecuteTool"/> に渡された引数 JSON</summary>
    public string? LastArgumentsJson { get; private set; }

    /// <summary>直近の <see cref="ReplaceQueries"/> に渡されたクエリ一覧（未呼び出しなら null）</summary>
    public IReadOnlyList<QueryDefinition>? LastReplacedQueries { get; private set; }

    /// <inheritdoc />
    public event EventHandler<ColumnRenamedEventArgs>? ColumnRenamed;

    /// <inheritdoc />
    public bool IsEmpty => IsEmptyToReturn;

    /// <inheritdoc />
    public DatabaseProviderRegistry Providers => ProvidersToReturn;

    /// <inheritdoc />
    public ErDiagram GetDiagram() => DiagramToReturn;

    /// <inheritdoc />
    public void AutoArrangeNewDiagram() => AutoArrangeCallCount++;

    /// <inheritdoc />
    public (string Result, bool Success) ExecuteTool(string toolName, string argumentsJson)
    {
        LastToolName = toolName;
        LastArgumentsJson = argumentsJson;

        return ToolResultToReturn;
    }

    /// <inheritdoc />
    public void ReplaceQueries(IReadOnlyList<QueryDefinition> queries) =>
        LastReplacedQueries = queries;

    /// <summary>テストから <see cref="ColumnRenamed"/> を発火させるためのヘルパー</summary>
    public void RaiseColumnRenamed(Guid entityId, string oldName, string newName) =>
        ColumnRenamed?.Invoke(this, new ColumnRenamedEventArgs(entityId, oldName, newName));
}
