using System.Text.Json;

namespace QuickER.Mcp;

/// <summary>
/// MCP サーバへ公開する 1 まとまりのツール群。公開するツール定義と、
/// ツール名・引数 JSON を受け取り結果テキストと成否を返す実行デリゲートを対で保持する。
/// </summary>
/// <remarks>
/// <see cref="Execute"/> は、実行デリゲートへ渡す前に引数の形を入力スキーマと突き合わせる
/// （<see cref="ToolArgumentShape"/>）。必須の欠落・型違いはここで英語の 1 文で断り、ツールの処理へは届けない。
/// 引数が JSON として読めない・オブジェクトでないときは、その判定と文言を実行デリゲートに任せる。
/// </remarks>
public sealed class McpToolSet
{
    /// <summary>実行デリゲート（ツール名・引数 JSON → 結果テキストと成否）</summary>
    private readonly Func<string, string, (string Result, bool Success)> _dispatch;

    /// <summary>ツール名 → 入力スキーマ</summary>
    private readonly Dictionary<string, object> _schemas;

    /// <summary>公開するツール定義と実行デリゲートを指定して生成する</summary>
    /// <param name="tools">公開するツール定義一覧</param>
    /// <param name="dispatch">ツール実行デリゲート（ツール名・引数 JSON → 結果テキストと成否）</param>
    public McpToolSet(
        IReadOnlyList<ToolDefinition> tools,
        Func<string, string, (string Result, bool Success)> dispatch
    )
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(dispatch);

        Tools = tools;
        _dispatch = dispatch;
        _schemas = tools.ToDictionary(
            tool => tool.Name,
            tool => tool.InputSchema,
            StringComparer.Ordinal
        );
    }

    /// <summary>公開するツール定義一覧</summary>
    public IReadOnlyList<ToolDefinition> Tools { get; }

    /// <summary>引数の形を確かめてからツールを実行する</summary>
    /// <param name="toolName">ツール名</param>
    /// <param name="argumentsJson">引数 JSON</param>
    /// <returns>結果テキストと成否</returns>
    public (string Result, bool Success) Execute(string toolName, string argumentsJson)
    {
        if (
            _schemas.TryGetValue(toolName, out var schema)
            && TryParseObject(argumentsJson, out var arguments)
            && ToolArgumentShape.Check(schema, arguments) is { } problem
        )
        {
            return (ToolArgumentShape.DescribeEnglish(problem), false);
        }

        return _dispatch(toolName, argumentsJson);
    }

    /// <summary>引数 JSON をオブジェクトとして読む（読めない・オブジェクトでないときは false）</summary>
    private static bool TryParseObject(string argumentsJson, out JsonElement arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                arguments = document.RootElement.Clone();
                return true;
            }
        }
        catch (JsonException)
        {
            // 読めない引数の文言は実行デリゲートに任せる
        }

        arguments = default;
        return false;
    }
}
