using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace QuickER.Mcp;

/// <summary>
/// 複数の <see cref="McpToolSet"/> を stdio トランスポートで公開する MCP サーバのホスト。
/// 外部 AI エージェント（Claude Code / Codex 等）が子プロセスとして起動して接続する用途を想定する。
/// </summary>
public static class StdioMcpServerHost
{
    /// <summary>
    /// 与えられたツールセット群を stdio トランスポートの MCP サーバとして起動し、終了まで待機する。
    /// </summary>
    /// <param name="toolSets">公開するツールセット群</param>
    /// <param name="cancellationToken">停止トークン</param>
    public static async Task RunAsync(
        IReadOnlyList<McpToolSet> toolSets,
        CancellationToken cancellationToken = default
    )
    {
        var builder = Host.CreateApplicationBuilder();

        // stdout は MCP プロトコル専用チャネルのため、コンソールへのログ出力を必ず無効化する
        // （ログが JSON-RPC ストリームへ混入するとプロトコルが壊れる）
        builder.Logging.ClearProviders();

        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools(BuildTools(toolSets));

        var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>全ツールセットの定義を、実行を委譲する MCP ツールへ平坦化して変換する</summary>
    private static IReadOnlyList<McpServerTool> BuildTools(IReadOnlyList<McpToolSet> toolSets)
    {
        var tools = new List<McpServerTool>();

        foreach (var toolSet in toolSets)
        {
            foreach (var definition in toolSet.Tools)
            {
                var schema = JsonSerializer.SerializeToElement(definition.InputSchema);
                var function = new DelegatingToolFunction(
                    definition.Name,
                    definition.Description,
                    schema,
                    toolSet.Execute
                );
                tools.Add(McpServerTool.Create(function));
            }
        }

        return tools;
    }
}
