using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace QuickER.Tests.Cli;

/// <summary>
/// <c>quicker mcp</c>（stdio MCP サーバ）を実プロセスとして起動し、ModelContextProtocol のクライアントで
/// stdio 越しに往復する end-to-end テスト。プロトコル純度（標準出力に JSON-RPC 以外が混入しないこと）の実証を兼ねる。
/// </summary>
/// <remarks>
/// モック不可（実プロセス起動が本テストの主眼）。1 プロセスで ListTools → create_diagram → add_entity →
/// add_column → get_diagram_summary → generate_ddl の全シナリオを流す。
/// </remarks>
public class McpServerE2ETests
{
    /// <summary>
    /// QuickER.Cli 自身のビルド出力（<c>src/QuickER.Cli/bin/&lt;Config&gt;/net10.0/QuickER.Cli.dll</c>）の絶対パス。
    /// </summary>
    /// <remarks>
    /// テスト出力にコピーされる QuickER.Cli.dll は使わない。WPF テストホストが AspNetCore 共有フレームワークを
    /// 取り込む結果 <c>Microsoft.Extensions.Hosting.dll</c> 等がテスト出力から重複排除され、単体プロセスとして
    /// 起動する CLI（NETCore.App のみ参照）が依存を解決できないため。CLI 自身の bin には全依存が配置される。
    /// ProjectReference により <c>dotnet test</c> 実行時に CLI も最新へビルドされる。
    /// </remarks>
    private static string CliDllPath
    {
        get
        {
            var netDir = new DirectoryInfo(AppContext.BaseDirectory); // .../bin/<Config>/net10.0-windows
            var config = netDir.Parent?.Name ?? "Debug";

            var dir = netDir;

            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                dir = dir.Parent;
            }

            if (dir is null)
            {
                throw new InvalidOperationException(
                    "リポジトリルート（QuickER.slnx を含む）が見つかりませんでした。"
                );
            }

            return Path.Combine(
                dir.FullName,
                "src",
                "QuickER.Cli",
                "bin",
                config,
                "net10.0",
                "QuickER.Cli.dll"
            );
        }
    }

    [Fact(DisplayName = "quicker mcp は stdio で往復し 12 ツールを公開する")]
    public async Task McpServer_RoundTripsOverStdio()
    {
        File.Exists(CliDllPath)
            .Should()
            .BeTrue($"QuickER.Cli.dll がテスト出力に存在するはず: {CliDllPath}");

        var workDir = Path.Combine(
            Path.GetTempPath(),
            "QuickERMcpE2E",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(workDir);
        var diagramFile = Path.Combine(workDir, "diagram.json");
        var sqlFile = Path.Combine(workDir, "schema.sql");

        // サーバ側 stderr を捕捉（失敗時の診断用）
        var stderrLines = new ConcurrentQueue<string>();

        // プロセス起動と各操作にゆとりを持たせたタイムアウト（dotnet の起動を含む）
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "quicker-mcp",
                Command = "dotnet",
                Arguments = [CliDllPath, "mcp"],
                WorkingDirectory = workDir,
                StandardErrorLines = line => stderrLines.Enqueue(line),
            }
        );

        McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

        try
        {
            // --- ListTools: 12 ツール（ER 9 ＋ create_diagram ＋ generate_csharp ＋ generate_ddl）、全ツールに file パラメータ ---
            var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
            var toolNames = tools.Select(t => t.Name).ToHashSet();

            toolNames
                .Should()
                .BeEquivalentTo(
                    new[]
                    {
                        "get_diagram_summary",
                        "add_entity",
                        "remove_entity",
                        "add_column",
                        "remove_column",
                        "set_entity_property",
                        "set_column_property",
                        "add_relationship",
                        "remove_relationship",
                        "create_diagram",
                        "generate_csharp",
                        "generate_ddl",
                    },
                    Diagnostics(stderrLines)
                );

            foreach (var tool in tools)
            {
                tool.JsonSchema.TryGetProperty("properties", out var props)
                    .Should()
                    .BeTrue($"tool '{tool.Name}' has a properties object");
                props
                    .TryGetProperty("file", out _)
                    .Should()
                    .BeTrue($"tool '{tool.Name}' exposes a 'file' parameter");
            }

            // --- create_diagram ---
            var create = await CallAsync(
                client,
                "create_diagram",
                new() { ["file"] = diagramFile, ["target_dbms"] = "sqlite" },
                cts.Token
            );
            create
                .isError.Should()
                .BeFalse(Diagnostics(stderrLines) + " create_diagram: " + create.text);
            File.Exists(diagramFile).Should().BeTrue();

            // --- add_entity ---
            var addEntity = await CallAsync(
                client,
                "add_entity",
                new() { ["file"] = diagramFile, ["table_name"] = "Customer" },
                cts.Token
            );
            addEntity.isError.Should().BeFalse(addEntity.text);

            // --- add_column (PK) ---
            var addColumn = await CallAsync(
                client,
                "add_column",
                new()
                {
                    ["file"] = diagramFile,
                    ["table_name"] = "Customer",
                    ["column_name"] = "Id",
                    ["data_type"] = "int",
                    ["is_primary_key"] = true,
                    ["is_nullable"] = false,
                },
                cts.Token
            );
            addColumn.isError.Should().BeFalse(addColumn.text);

            // --- get_diagram_summary: 内容確認 ---
            var summary = await CallAsync(
                client,
                "get_diagram_summary",
                new() { ["file"] = diagramFile },
                cts.Token
            );
            summary.isError.Should().BeFalse(summary.text);
            summary.text.Should().Contain("Customer");
            summary.text.Should().Contain("Id");
            summary.text.Should().Contain("PK");

            // --- generate_ddl: SQL ファイル生成確認 ---
            var ddl = await CallAsync(
                client,
                "generate_ddl",
                new() { ["file"] = diagramFile, ["out_file"] = sqlFile },
                cts.Token
            );
            ddl.isError.Should().BeFalse(ddl.text);
            File.Exists(sqlFile).Should().BeTrue();
            File.ReadAllText(sqlFile).Should().Contain("-- DDL auto-generated by QuickER");
        }
        finally
        {
            await client.DisposeAsync();

            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    /// <summary>ツールを呼び出し、結果テキストとエラーフラグを取り出す</summary>
    private static async Task<(string text, bool isError)> CallAsync(
        McpClient client,
        string name,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken
    )
    {
        var result = await client.CallToolAsync(
            name,
            arguments,
            cancellationToken: cancellationToken
        );
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

        return (text, result.IsError ?? false);
    }

    /// <summary>失敗時のアサーションメッセージにサーバ側 stderr を添える</summary>
    private static string Diagnostics(ConcurrentQueue<string> stderrLines) =>
        stderrLines.IsEmpty
            ? "(no server stderr)"
            : "server stderr:\n" + string.Join("\n", stderrLines);
}
