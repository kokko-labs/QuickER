using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace QuickER.AI;

/// <summary>
/// ER 図操作ツールを Claude Code へ公開する、プロセス内 HTTP/SSE MCP サーバー。
/// 127.0.0.1 のエフェメラルポートで Kestrel を起動し、bearer トークンで保護する。
/// ツール定義は生成時に注入されたセット（機能側 QuickER.AI.Chat の ErDiagramToolDefinitions が単一ソース）を用い、
/// 呼び出しは注入された実行コールバック（UI スレッドへのマーシャリングは呼び出し側の責務）へ委譲する。
/// </summary>
public sealed class ErDiagramMcpServer : IAsyncDisposable
{
    /// <summary>mcp-config に書く既定サーバー名（ツール名は <c>mcp__erdesigner__&lt;tool&gt;</c> になる）</summary>
    public const string ServerName = "erdesigner";

    private readonly Func<string, string, (string Result, bool Success)> _execute;
    private readonly IReadOnlyList<CodexDynamicToolDefinition> _tools;
    private WebApplication? _app;

    /// <summary>解決済みのサーバー URL（例: <c>http://127.0.0.1:54321</c>）。未起動なら null</summary>
    public string? Url { get; private set; }

    /// <summary>接続時に Authorization: Bearer で要求するトークン。未起動なら null</summary>
    public string? AuthToken { get; private set; }

    /// <summary>ツール実行コールバック（ツール名・引数 JSON → 結果テキストと成否）を指定して生成する</summary>
    /// <param name="execute">ツール実行コールバック</param>
    /// <param name="tools">公開するツール定義セット（合成ルート／エンジンが明示的に指定する）</param>
    public ErDiagramMcpServer(
        Func<string, string, (string Result, bool Success)> execute,
        IReadOnlyList<CodexDynamicToolDefinition> tools
    )
    {
        _execute = execute;
        _tools = tools;
    }

    /// <summary>サーバーを起動し、<see cref="Url"/> / <see cref="AuthToken"/> を確定する（起動済みなら何もしない）</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null)
        {
            return;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddMcpServer().WithHttpTransport().WithTools(BuildTools());

        var app = builder.Build();

        // localhost とはいえ他プロセスからの接続もあり得るため bearer トークンで保護する
        app.Use(
            async (context, next) =>
            {
                if (!IsAuthorized(context.Request.Headers.Authorization, token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await next(context).ConfigureAwait(false);
            }
        );

        app.MapMcp();

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        _app = app;
        AuthToken = token;
        Url = app.Urls.FirstOrDefault();
    }

    /// <summary>Authorization ヘッダー値が期待する Bearer トークンと一致するかを判定する</summary>
    /// <remarks>
    /// 通常の文字列比較は不一致位置で早期終了するため、応答時間の差からトークンを推測される余地を残す。
    /// ループバック限定でも他プロセスからの試行はあり得るので固定時間比較を用いる
    /// </remarks>
    internal static bool IsAuthorized(string? authorizationHeader, string expectedToken)
    {
        var expected = Encoding.UTF8.GetBytes($"Bearer {expectedToken}");
        var actual = Encoding.UTF8.GetBytes(authorizationHeader ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>全ツール定義を MCP ツールへ変換する</summary>
    private IReadOnlyList<McpServerTool> BuildTools() => _tools.Select(CreateTool).ToList();

    /// <summary>1 つの dynamicTool 定義を、固定スキーマと実行委譲を持つ MCP ツールへ変換する</summary>
    private McpServerTool CreateTool(CodexDynamicToolDefinition definition)
    {
        var schema = JsonSerializer.SerializeToElement(definition.InputSchema);
        var function = new ErToolFunction(
            definition.Name,
            definition.Description,
            schema,
            _execute
        );
        return McpServerTool.Create(function);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
    }

    /// <summary>固定の入力スキーマを持ち、実行を外部コールバックへ委譲する <see cref="AIFunction"/></summary>
    private sealed class ErToolFunction : AIFunction
    {
        private readonly Func<string, string, (string Result, bool Success)> _execute;

        public override string Name { get; }
        public override string Description { get; }
        public override JsonElement JsonSchema { get; }

        public ErToolFunction(
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
}
