using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using QuickER.AI;
using QuickER.AI.Chat;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>ツールの往復の入力（利用者の発話と、偽のサーバーが 1 回目の応答で返すツールの呼び出し）</summary>
/// <param name="Prompt">利用者の発話</param>
/// <param name="CallId">サーバーが付けるツールの呼び出しの ID</param>
/// <param name="ToolName">ツール名</param>
/// <param name="ArgumentsJson">引数 JSON</param>
internal sealed record ToolRoundTrip(
    string Prompt,
    string CallId,
    string ToolName,
    string ArgumentsJson
);

/// <summary>偽のサーバー：そこへつないだドライバと、受け取った要求の本文（順に）</summary>
/// <param name="driver">偽のサーバーへつないだ本物のドライバ</param>
/// <param name="requests">受け取った要求の本文</param>
/// <param name="stop">サーバーを止める処理（無ければ何もしない）</param>
internal sealed class FakeChatServer(
    IChatTurnDriver driver,
    IReadOnlyList<string> requests,
    Func<ValueTask>? stop = null
) : IAsyncDisposable
{
    /// <summary>偽のサーバーへつないだ本物のドライバ</summary>
    public IChatTurnDriver Driver { get; } = driver;

    /// <summary>受け取った要求の本文（受け取った順）</summary>
    public IReadOnlyList<string> Requests { get; } = requests;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => stop?.Invoke() ?? ValueTask.CompletedTask;
}

/// <summary>
/// ツールの往復：本物の <see cref="ChatTurnEngine"/> に本物のドライバを載せて偽のサーバーへ 1 ターン流し、
/// ツールを実行した後の 2 回目の要求を射影する
/// </summary>
/// <remarks>
/// 1 回目の応答はツールの呼び出し（サーバーが付けた ID）、2 回目の応答はテキスト。ID はドライバの層で
/// ストリームから読み取り（OpenAI の <c>ToolCallId</c>・Anthropic の <c>tool_use.id</c>）、エンジンが履歴へ積み、
/// 2 回目の要求でアシスタントのツールの呼び出しとツールの結果の両方へ書き戻される。どこで ID を落としても 2 回目の要求の射影が変わる。
/// </remarks>
internal static class ChatDriverToolRoundTrip
{
    /// <summary>1 ターン流して、2 回目の要求の射影と実行したツールの呼び出しを返す</summary>
    /// <param name="adapter">ドライバのアダプタ</param>
    /// <param name="trip">往復の入力</param>
    /// <param name="cancellationToken">取り消し</param>
    public static async Task<IReadOnlyList<string>> RunAsync(
        ChatDriverAdapter adapter,
        ToolRoundTrip trip,
        CancellationToken cancellationToken
    )
    {
        await using var server = await adapter.StartServerAsync(trip, cancellationToken);
        var host = new RecordingToolHost();
        ErChatTurnResult? result = null;
        var engine = new ChatTurnEngine(
            server.Driver,
            host,
            new ChatSyncDispatcher(),
            () => true,
            ErDesignProfile.ErDesign
        );
        engine.TurnCompleted += (_, completed) => result = completed;

        try
        {
            await engine.SendAsync(trip.Prompt, cancellationToken);
        }
        finally
        {
            await engine.DisposeAsync();
        }

        // 要求が 2 回でなければ、照合に失敗の理由が出るよう射影の代わりに状況を返す
        if (server.Requests.Count != 2)
        {
            return
            [
                $"requests:{server.Requests.Count} success:{result?.Success} error:{result?.Error}",
            ];
        }

        return
        [
            .. adapter.ProjectRequest(server.Requests[1]),
            .. host.Calls.Select(call => $"executed:{call.Tool}:{call.Args}"),
        ];
    }

    /// <summary>
    /// 決まった応答を順に返す HTTP ハンドラ（Anthropic のクライアントへ差し込む）。Messages の要求だけを記録し、ほか（Models API）は 404
    /// </summary>
    /// <param name="responses">Messages の要求へ順に返す SSE の本文</param>
    internal sealed class ScriptedSseHandler(IReadOnlyList<string> responses) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        /// <summary>受け取った Messages の要求の本文</summary>
        public IReadOnlyList<string> Requests => _requests;

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (
                request.Method != HttpMethod.Post
                || request.RequestUri?.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal)
                    != true
            )
            {
                // Models API（出力の上限の照会）は取れない扱い（ドライバは既定の上限で送る）
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        """{"type":"error","error":{"type":"not_found_error","message":"not found"}}"""
                    ),
                };
            }

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var index = _requests.Count;
            _requests.Add(body);

            return index < responses.Count
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        responses[index],
                        Encoding.UTF8,
                        "text/event-stream"
                    ),
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{}"),
                };
        }
    }

    /// <summary>
    /// ループバックの HTTP サーバー（Kestrel・ポート 0）で、POST の要求へ決まった SSE の本文を順に返す。
    /// OpenAI のドライバは差し込み口を持たないので、接続のエンドポイントの上書き（製品の正規の設定）でここへ向ける
    /// </summary>
    /// <param name="responses">POST の要求へ順に返す SSE の本文</param>
    /// <param name="cancellationToken">取り消し</param>
    /// <returns>サーバーの基底 URL・受け取った要求の本文・止める処理</returns>
    public static async Task<(
        string BaseUrl,
        IReadOnlyList<string> Requests,
        Func<ValueTask> Stop
    )> StartLoopbackSseServerAsync(
        IReadOnlyList<string> responses,
        CancellationToken cancellationToken
    )
    {
        var requests = new List<string>();
        var gate = new object();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.MapPost(
            "/{**path}",
            async context =>
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(context.RequestAborted);
                int index;

                lock (gate)
                {
                    index = requests.Count;
                    requests.Add(body);
                }

                if (index >= responses.Count)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    return;
                }

                context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync(responses[index], context.RequestAborted);
            }
        );

        await app.StartAsync(cancellationToken);

        return (
            app.Urls.Single(),
            requests,
            async () =>
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        );
    }
}

/// <summary>Anthropic の Messages API のストリーミングの応答（SSE）を組む</summary>
internal static class AnthropicSse
{
    /// <summary>空のオブジェクト（補間した生文字列の中で閉じ波かっこが 3 つ並ばないよう分けて書く）</summary>
    private const string EmptyObject = "{}";

    /// <summary>ツールの呼び出し 1 つで終わる応答（終了理由 tool_use）</summary>
    public static string ToolUse(ToolRoundTrip trip) =>
        Start()
        + Event(
            "content_block_start",
            $$$"""{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":{{{Json(trip.CallId)}}},"name":{{{Json(trip.ToolName)}}},"input":{{{EmptyObject}}}}}"""
        )
        + Event(
            "content_block_delta",
            $$$"""{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":{{{Json(trip.ArgumentsJson)}}}}}"""
        )
        + Event("content_block_stop", """{"type":"content_block_stop","index":0}""")
        + Stop("tool_use");

    /// <summary>テキストだけで終わる応答（終了理由 end_turn）</summary>
    public static string Text(string text) =>
        Start()
        + Event(
            "content_block_start",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""
        )
        + Event(
            "content_block_delta",
            $$$"""{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":{{{Json(text)}}}}}"""
        )
        + Event("content_block_stop", """{"type":"content_block_stop","index":0}""")
        + Stop("end_turn");

    private static string Start() =>
        Event(
            "message_start",
            """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}"""
        );

    private static string Stop(string stopReason) =>
        Event(
            "message_delta",
            $$$"""{"type":"message_delta","delta":{"stop_reason":{{{Json(stopReason)}}},"stop_sequence":null},"usage":{"output_tokens":5}}"""
        ) + Event("message_stop", """{"type":"message_stop"}""");

    private static string Event(string name, string json) => $"event: {name}\ndata: {json}\n\n";

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}

/// <summary>OpenAI の Chat Completions のストリーミングの応答（SSE）を組む</summary>
internal static class OpenAiSse
{
    /// <summary>ツールの呼び出し 1 つで終わる応答（ID と名前の断片 → 引数の断片 → 終了理由 tool_calls）</summary>
    public static string ToolCall(ToolRoundTrip trip) =>
        Chunk(
            $$$"""{"role":"assistant","tool_calls":[{"index":0,"id":{{{Json(trip.CallId)}}},"type":"function","function":{"name":{{{Json(trip.ToolName)}}},"arguments":""}}]}""",
            "null"
        )
        + Chunk(
            $$$"""{"tool_calls":[{"index":0,"function":{"arguments":{{{Json(trip.ArgumentsJson)}}}}}]}""",
            "null"
        )
        + Chunk("{}", "\"tool_calls\"")
        + "data: [DONE]\n\n";

    /// <summary>テキストだけで終わる応答（終了理由 stop）</summary>
    public static string Text(string text) =>
        Chunk($$$"""{"role":"assistant","content":{{{Json(text)}}}}""", "null")
        + Chunk("{}", "\"stop\"")
        + "data: [DONE]\n\n";

    private static string Chunk(string delta, string finishReason) =>
        $$$"""data: {"id":"chatcmpl-1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{{{delta}}},"finish_reason":{{{finishReason}}}}]}"""
        + "\n\n";

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
