using System.Text.Json;
using Microsoft.Extensions.AI;

namespace QuickER.Mcp;

/// <summary>
/// 固定の入力スキーマを持ち、実行を外部コールバックへ委譲する <see cref="AIFunction"/>。
/// MCP ツール（<c>McpServerTool.Create</c>）や関数呼び出しへ、ツール定義 1 件を橋渡しする。
/// </summary>
public sealed class DelegatingToolFunction : AIFunction
{
    private readonly Func<string, string, (string Result, bool Success)> _execute;

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override string Description { get; }

    /// <inheritdoc />
    public override JsonElement JsonSchema { get; }

    /// <summary>ツール名・説明・固定入力スキーマ・実行コールバックを指定して生成する</summary>
    /// <param name="name">ツール名</param>
    /// <param name="description">ツールの説明</param>
    /// <param name="jsonSchema">固定の入力 JSON Schema</param>
    /// <param name="execute">実行コールバック（ツール名・引数 JSON → 結果テキストと成否）</param>
    public DelegatingToolFunction(
        string name,
        string description,
        JsonElement jsonSchema,
        Func<string, string, (string Result, bool Success)> execute
    )
    {
        Name = name;
        Description = description;
        JsonSchema = jsonSchema;
        _execute = execute;
    }

    /// <inheritdoc />
    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken
    )
    {
        var argumentsJson = JsonSerializer.Serialize(arguments);
        var (result, _) = _execute(Name, argumentsJson);
        return ValueTask.FromResult<object?>(result);
    }
}
