using System.Text.Json;
using QuickER.AI;
using QuickER.AI.Chat.Resources;
using QuickER.Extensibility;
using QuickER.Mcp;
using QuickER.Mcp.Tools;

namespace QuickER.AI.Chat;

/// <summary>
/// 契約 <see cref="IErDiagramHost"/> を、AI チャット ViewModel が要求する <see cref="IErDiagramChatHost"/> へ適合させるアダプタ。
/// </summary>
/// <remarks>
/// フィーチャーモジュール（QuickER.AI.Chat）側に置く「契約 → チャット固有インターフェイス」の橋渡し。
/// <see cref="IsEmpty"/> / <see cref="AutoArrangeNewDiagram"/> はホストへ委譲し、
/// AI ツール実行は内包する <see cref="IErDiagramToolHost"/> 実装（<see cref="ToolHostAdapter"/>）が担う。
/// 名前付きクエリツール（set_query / list_queries / remove_query）だけはアダプタ層で捕捉し、面非依存の
/// 共有コア <see cref="QueryToolCore"/> で処理して結果を <see cref="QueryToolLocalizedFormatter"/> で整形する。
/// それ以外のツールは <see cref="IErDiagramHost.ExecuteTool"/> へ単純委譲する。
/// </remarks>
public sealed class ErDiagramHostChatAdapter : IErDiagramChatHost
{
    private readonly IErDiagramHost _host;
    private readonly ToolHostAdapter _toolHost;

    /// <summary>橋渡し対象の <see cref="IErDiagramHost"/> を指定して生成する</summary>
    public ErDiagramHostChatAdapter(IErDiagramHost host)
    {
        _host = host;
        _toolHost = new ToolHostAdapter(host);
    }

    /// <inheritdoc />
    public IErDiagramToolHost ToolHost => _toolHost;

    /// <inheritdoc />
    public bool IsEmpty => _host.IsEmpty;

    /// <inheritdoc />
    public void AutoArrangeNewDiagram() => _host.AutoArrangeNewDiagram();

    /// <summary>
    /// AI エンジン（QuickER.AI）の <see cref="IErDiagramToolHost"/> を、契約 <see cref="IErDiagramHost"/> へ橋渡しする実装。
    /// </summary>
    /// <remarks>
    /// 全エンジン（API キー接続の <c>ChatTurnEngine</c>・Claude Code のプロセス内 MCP サーバ・Codex の dynamicTools）は
    /// ツール実行を <see cref="IErDiagramToolHost.Execute"/> に集約するため、ここでクエリツールを捕捉すれば全経路で有効になる。
    /// </remarks>
    private sealed class ToolHostAdapter : IErDiagramToolHost
    {
        private readonly IErDiagramHost _host;

        public ToolHostAdapter(IErDiagramHost host)
        {
            _host = host;
        }

        /// <summary>カタログのツール名 → 入力スキーマ（引数の形の突き合わせに使う）</summary>
        private static readonly Dictionary<string, object> CatalogSchemas = ErDiagramToolCatalog
            .GetDefinitions()
            .ToDictionary(tool => tool.Name, tool => tool.InputSchema, StringComparer.Ordinal);

        /// <inheritdoc />
        /// <remarks>
        /// ツールを実行する前に、引数の形を入力スキーマと突き合わせる（<see cref="ToolArgumentShape"/>・MCP の
        /// <c>McpToolSet.Execute</c> と同じ規則）。必須の欠落・型違いはここで断り、ホストへ届けない。
        /// </remarks>
        public (string Result, bool Success) Execute(string toolName, string argumentsJson)
        {
            if (DescribeShapeProblem(toolName, argumentsJson) is { } problem)
            {
                return (problem, false);
            }

            // 名前付きクエリツールは共有コアで処理し、成功時のみホストの Queries へ書き戻す（＝全か無か）。
            // それ以外はホストの ExecuteTool（VM 操作）へ委譲する。
            return toolName switch
            {
                QueryToolCore.SetQueryToolName => ExecuteSetQuery(argumentsJson),
                QueryToolCore.ListQueriesToolName => ExecuteListQueries(),
                QueryToolCore.RemoveQueryToolName => ExecuteRemoveQuery(argumentsJson),
                _ => _host.ExecuteTool(toolName, argumentsJson),
            };
        }

        /// <summary>引数の形の問題を表示言語の 1 文にする（カタログに無いツール・読めない引数は null＝各処理に任せる）</summary>
        private static string? DescribeShapeProblem(string toolName, string argumentsJson)
        {
            if (!CatalogSchemas.TryGetValue(toolName, out var schema))
            {
                return null;
            }

            JsonElement arguments;

            try
            {
                using var document = JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson
                );

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                arguments = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }

            return ToolArgumentShape.Check(schema, arguments) switch
            {
                null => null,
                { Kind: ToolArgumentProblemKind.Missing } missing => string.Format(
                    Strings.Tool_ArgumentMissing,
                    missing.Path
                ),
                var wrong => string.Format(
                    Strings.Tool_ArgumentWrongType,
                    wrong.Path,
                    wrong.ExpectedType
                ),
            };
        }

        /// <summary>set_query: 独立コピーの図で検証・upsert し、成功時のみ ReplaceQueries で書き戻す</summary>
        private (string Result, bool Success) ExecuteSetQuery(string argumentsJson)
        {
            if (
                !TryParseArguments(
                    QueryToolCore.SetQueryToolName,
                    argumentsJson,
                    out var args,
                    out var error
                )
            )
            {
                return error;
            }

            var diagram = _host.GetDiagram();
            var outcome = QueryToolCore.SetQuery(diagram, args);

            // 検証失敗時は ReplaceQueries を呼ばない（MCP 面と同一意味論の「全か無か」）
            if (outcome.Success)
            {
                _host.ReplaceQueries(diagram.Queries);
            }

            return (QueryToolLocalizedFormatter.FormatSetQuery(outcome), outcome.Success);
        }

        /// <summary>list_queries: 現在の図の名前付きクエリをローカライズ整形して返す（読み取り専用）</summary>
        private (string Result, bool Success) ExecuteListQueries()
        {
            var diagram = _host.GetDiagram();
            var outcome = QueryToolCore.ListQueries(diagram);

            return (QueryToolLocalizedFormatter.FormatListing(outcome.Listing!), true);
        }

        /// <summary>remove_query: 独立コピーの図で 1 件削除し、成功時のみ ReplaceQueries で書き戻す</summary>
        private (string Result, bool Success) ExecuteRemoveQuery(string argumentsJson)
        {
            if (
                !TryParseArguments(
                    QueryToolCore.RemoveQueryToolName,
                    argumentsJson,
                    out var args,
                    out var error
                )
            )
            {
                return error;
            }

            var diagram = _host.GetDiagram();
            var outcome = QueryToolCore.RemoveQuery(diagram, args);

            if (outcome.Success)
            {
                _host.ReplaceQueries(diagram.Queries);
            }

            return (QueryToolLocalizedFormatter.FormatRemoveQuery(outcome), outcome.Success);
        }

        /// <summary>
        /// 引数 JSON を <see cref="JsonElement"/> へ解釈する（空は空オブジェクト扱い・解釈不能と
        /// オブジェクト以外の根〔<c>[]</c>・<c>null</c>・数値など〕はローカライズ済みエラー）
        /// </summary>
        /// <remarks>
        /// 根がオブジェクトでないまま渡すと、<see cref="QueryToolCore"/> の <c>TryGetProperty</c> が
        /// <see cref="InvalidOperationException"/>（英語の生メッセージ）で落ちる。MCP 側の
        /// <c>DocumentErDiagramToolSet</c> と同じく入口で断る。
        /// </remarks>
        private static bool TryParseArguments(
            string toolName,
            string argumentsJson,
            out JsonElement arguments,
            out (string Result, bool Success) error
        )
        {
            try
            {
                using var document = JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson
                );

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    arguments = default;
                    error = (string.Format(Strings.QueryTool_ArgumentsNotObject, toolName), false);
                    return false;
                }

                arguments = document.RootElement.Clone();
                error = default;
                return true;
            }
            catch (JsonException)
            {
                arguments = default;
                error = (string.Format(Strings.QueryTool_InvalidArgumentsJson, toolName), false);
                return false;
            }
        }
    }
}
