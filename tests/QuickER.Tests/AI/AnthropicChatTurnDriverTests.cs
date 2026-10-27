using System.Net.Http;
using Anthropic;
using Anthropic.Models.Messages;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;

namespace QuickER.Tests.AI;

/// <summary><see cref="AnthropicChatTurnDriver"/> の中立履歴 → Anthropic メッセージ変換を検証するテストクラス</summary>
public class AnthropicChatTurnDriverTests
{
    /// <summary>System 役割の履歴項目が結合され system プロンプト文字列になることを検証する</summary>
    [Fact(DisplayName = "ExtractSystemPrompt は System 役割の項目を結合する")]
    public void ExtractSystemPrompt_JoinsSystemItems()
    {
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.System, "ルール1"),
            new(ChatHistoryRole.User, "やあ"),
            new(ChatHistoryRole.System, "ルール2"),
        };

        var system = AnthropicChatTurnDriver.ExtractSystemPrompt(history);

        system.Should().Be("ルール1\n\nルール2");
    }

    /// <summary>System 項目はメッセージ列から除外され、それ以外の役割が順に変換されることを検証する</summary>
    [Fact(DisplayName = "ToMessageParams は System を除外し残りをメッセージへ変換する")]
    public void ToMessageParams_ExcludesSystemAndMapsRest()
    {
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.System, "システム指示"),
            new(ChatHistoryRole.User, "本のテーブルを作って"),
            new(
                ChatHistoryRole.Assistant,
                string.Empty,
                new[]
                {
                    new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}"),
                }
            ),
            new(ChatHistoryRole.Tool, "テーブル 'Book' を追加しました。", ToolCallId: "call_1"),
        };

        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        // System を除いた User / Assistant(tool) / Tool の 3 件が積まれる
        messages.Should().HaveCount(3);
    }

    /// <summary>ツール呼び出しを伴わないアシスタント発話が、単純なテキストメッセージへ変換されることを検証する</summary>
    [Fact(DisplayName = "ToMessageParams はツール無しアシスタント発話をテキストメッセージにする")]
    public void ToMessageParams_PlainAssistant_ProducesSingleMessage()
    {
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.User, "こんにちは"),
            new(ChatHistoryRole.Assistant, "こんにちは、何をしますか？"),
        };

        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        messages.Should().HaveCount(2);
    }

    /// <summary>
    /// テキストが空・空白だけで、ツール呼び出しも無いアシスタント項目（空の応答）は再送しないことを検証する
    /// （空の content は API が 400 で拒否し、履歴に残る限り以後の全ターンが失敗する）。
    /// </summary>
    [Theory(DisplayName = "ToMessageParams は空の応答のアシスタント項目を積まない")]
    [InlineData("")]
    [InlineData("   ")]
    public void ToMessageParams_EmptyAssistant_IsSkipped(string text)
    {
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.User, "こんにちは"),
            new(ChatHistoryRole.Assistant, text),
            new(ChatHistoryRole.User, "もう一度"),
        };

        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        messages.Should().HaveCount(2);
        messages.Should().OnlyContain(m => m.Role.Value() == MessageParamRole.User);
    }

    /// <summary>
    /// ツール呼び出しを伴うアシスタント項目で、テキストが空白だけなら、テキストブロックを作らず
    /// ツール呼び出しのブロックだけにすることを検証する（空白だけのテキストブロックも API が拒否する）。
    /// </summary>
    [Fact(DisplayName = "ToMessageParams はツール呼び出し前の空白テキストをブロックにしない")]
    public void ToMessageParams_WhitespaceTextWithToolCalls_HasOnlyToolUseBlocks()
    {
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.User, "本のテーブルを作って"),
            new(
                ChatHistoryRole.Assistant,
                Environment.NewLine + Environment.NewLine,
                new[]
                {
                    new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}"),
                }
            ),
            new(ChatHistoryRole.Tool, "テーブル 'Book' を追加しました。", ToolCallId: "call_1"),
        };

        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        messages.Should().HaveCount(3);
        var blocks = messages[1].Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        blocks.Should().ContainSingle();
        blocks[0].Value.Should().BeOfType<ToolUseBlockParam>();
    }

    /// <summary>画像添付付き User 項目が image(base64) ブロック＋テキストブロックの content 配列になることを検証する</summary>
    [Fact(DisplayName = "画像添付は Base64ImageSource の image ブロックになる")]
    public void ToUserParam_ImageAttachment_ProducesImageBlock()
    {
        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var item = new ChatHistoryItem(
            ChatHistoryRole.User,
            "この図を参考に",
            Attachments: new[]
            {
                new ChatAttachment("figure.png", ChatAttachmentKind.Image, "image/png", pngData),
            }
        );

        var param = AnthropicChatTurnDriver.ToUserParam(item);

        // content はブロック配列（画像＋テキスト）になる
        var blocks = param.Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        blocks.Should().HaveCount(2);
        blocks[0].Value.Should().BeOfType<ImageBlockParam>();

        var image = (ImageBlockParam)blocks[0].Value!;
        var source = image.Source.Value.As<Base64ImageSource>();
        source.Data.Should().Be(Convert.ToBase64String(pngData));
        source.MediaType.Value().Should().Be(MediaType.ImagePng);

        blocks[1].Value.Should().BeOfType<TextBlockParam>();
    }

    /// <summary>PDF 添付付き User 項目が Base64PdfSource の document ブロックになることを検証する</summary>
    [Fact(DisplayName = "PDF 添付は Base64PdfSource の document ブロックになる")]
    public void ToUserParam_PdfAttachment_ProducesDocumentBlock()
    {
        var pdfData = "%PDF-1.7"u8.ToArray();
        var item = new ChatHistoryItem(
            ChatHistoryRole.User,
            "この仕様書に沿って",
            Attachments: new[]
            {
                new ChatAttachment("spec.pdf", ChatAttachmentKind.Pdf, "application/pdf", pdfData),
            }
        );

        var param = AnthropicChatTurnDriver.ToUserParam(item);

        var blocks = param.Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        blocks[0].Value.Should().BeOfType<DocumentBlockParam>();

        var document = (DocumentBlockParam)blocks[0].Value!;
        var source = document.Source.Value.As<Base64PdfSource>();
        source.Data.Should().Be(Convert.ToBase64String(pdfData));
    }

    /// <summary>添付が無い User 項目は単純なテキストメッセージになることを検証する（挙動不変）</summary>
    [Fact(DisplayName = "添付なし User はテキストメッセージのまま")]
    public void ToUserParam_NoAttachments_ProducesPlainText()
    {
        var item = new ChatHistoryItem(ChatHistoryRole.User, "こんにちは");

        var param = AnthropicChatTurnDriver.ToUserParam(item);

        // content は文字列（ブロック配列ではない）
        param.Content.Value.Should().BeOfType<string>().Which.Should().Be("こんにちは");
    }

    /// <summary>
    /// テキスト添付のみの User 項目は、テキストが本文へインライン展開された単純なテキストメッセージに
    /// なることを検証する（コンテンツブロックは作らない）。
    /// </summary>
    [Fact(DisplayName = "テキスト添付は本文へインライン展開される")]
    public void ToUserParam_TextAttachment_InlinedIntoBody()
    {
        var body = System.Text.Encoding.UTF8.GetBytes("要件A\n要件B");
        var item = new ChatHistoryItem(
            ChatHistoryRole.User,
            "この要件で",
            Attachments: new[]
            {
                new ChatAttachment("req.md", ChatAttachmentKind.Text, "text/plain", body),
            }
        );

        var param = AnthropicChatTurnDriver.ToUserParam(item);

        var text = param.Content.Value.Should().BeOfType<string>().Subject;
        text.Should().Contain("この要件で");
        text.Should().Contain("Attached file: req.md");
        text.Should().Contain("要件A");
    }

    /// <summary>PDF＋テキスト混在では PDF は document ブロック・テキストは本文へインラインされることを検証する</summary>
    [Fact(DisplayName = "PDF＋テキスト混在は document ブロック＋インライン本文")]
    public void ToUserParam_PdfAndText_MixesCorrectly()
    {
        var pdfData = "%PDF-1.7"u8.ToArray();
        var textData = System.Text.Encoding.UTF8.GetBytes("補足メモ");
        var item = new ChatHistoryItem(
            ChatHistoryRole.User,
            "両方参照",
            Attachments: new[]
            {
                new ChatAttachment("s.pdf", ChatAttachmentKind.Pdf, "application/pdf", pdfData),
                new ChatAttachment("n.txt", ChatAttachmentKind.Text, "text/plain", textData),
            }
        );

        var param = AnthropicChatTurnDriver.ToUserParam(item);

        var blocks = param.Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        // document ブロック 1 ＋ テキストブロック 1（テキスト添付はインライン済みで本文へ）
        blocks.Should().HaveCount(2);
        blocks[0].Value.Should().BeOfType<DocumentBlockParam>();

        var textBlock = (TextBlockParam)blocks[1].Value!;
        textBlock.Text.Should().Contain("両方参照");
        textBlock.Text.Should().Contain("Attached file: n.txt");
        textBlock.Text.Should().Contain("補足メモ");
    }

    /// <summary>添付付き履歴が再構築（ToMessageParams 経由）でも image ブロックを保持することを検証する（毎ターン再送）</summary>
    [Fact(DisplayName = "履歴再構築でも添付ブロックが保持される")]
    public void ToMessageParams_WithAttachment_RebuildsImageBlock()
    {
        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.System, "指示"),
            new(
                ChatHistoryRole.User,
                "図を見て",
                Attachments: new[]
                {
                    new ChatAttachment("a.png", ChatAttachmentKind.Image, "image/png", pngData),
                }
            ),
        };

        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        messages.Should().ContainSingle();
        var blocks = messages[0].Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        blocks[0].Value.Should().BeOfType<ImageBlockParam>();
    }

    /// <summary>応答を 500 で返し、破棄されたかどうかを記録する HTTP ハンドラー</summary>
    private sealed class DisposeRecordingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{}"),
                }
            );

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 呼び出しごとに作った SDK のクライアントを、失敗した呼び出しでも破棄することを検証する
    /// （クライアントはインスタンスごとに自分の HttpClient を持ち、破棄しないとツール往復のたびに
    /// 接続が積み上がる）。破棄はクライアントへ渡した HttpClient のハンドラーが破棄されたことで観測する
    /// （SDK はクライアントの破棄で、渡された HttpClient も破棄する）。
    /// </summary>
    [Fact(DisplayName = "RunAsync は呼び出しごとに作ったクライアントを破棄する")]
    public async Task RunAsync_DisposesClientPerCall()
    {
        var handler = new DisposeRecordingHandler();
        var driver = new AnthropicChatTurnDriver(
            () => new AnthropicChatConnection("test-key", "claude-test"),
            ErDesignProfile.ErDesign,
            connection => new AnthropicClient
            {
                ApiKey = connection.ApiKey,
                HttpClient = new HttpClient(handler),
                MaxRetries = 0,
            }
        );
        var history = new List<ChatHistoryItem> { new(ChatHistoryRole.User, "やあ") };

        var run = () => driver.RunAsync(history, _ => { }, TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<Exception>();
        handler.Disposed.Should().BeTrue();
    }

    /// <summary>決まった SSE の本文を 200 で返す HTTP ハンドラー（ストリーミングの応答の代わり）</summary>
    private sealed class SseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        body,
                        System.Text.Encoding.UTF8,
                        "text/event-stream"
                    ),
                }
            );
    }

    /// <summary>SSE のイベント 1 つ</summary>
    private static string SseEvent(string name, string json) => $"event: {name}\ndata: {json}\n\n";

    /// <summary>応答の始まり（ブロックはまだ無い）</summary>
    private static string SseStart() =>
        SseEvent(
            "message_start",
            """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}"""
        );

    /// <summary>応答の終わり（終了理由と出力のトークン数）</summary>
    private static string SseStop(string stopReason, int outputTokens) =>
        SseEvent(
            "message_delta",
            $$$"""{"type":"message_delta","delta":{"stop_reason":"{{{stopReason}}}","stop_sequence":null},"usage":{"output_tokens":{{{outputTokens}}}}}"""
        ) + SseEvent("message_stop", """{"type":"message_stop"}""");

    /// <summary>決まった SSE を返すクライアントでドライバを 1 回走らせる</summary>
    private static Task<ChatAssistantTurn> RunWithSseAsync(string body) =>
        new AnthropicChatTurnDriver(
            () => new AnthropicChatConnection("test-key", "claude-test"),
            ErDesignProfile.ErDesign,
            connection => new AnthropicClient
            {
                ApiKey = connection.ApiKey,
                HttpClient = new HttpClient(new SseHandler(body)),
                MaxRetries = 0,
            }
        ).RunAsync(
            new List<ChatHistoryItem> { new(ChatHistoryRole.User, "やあ") },
            _ => { },
            TestContext.Current.CancellationToken
        );

    /// <summary>
    /// 出力の上限で切れたツール呼び出しは、終了理由 max_tokens・出力のトークン数とともに返り、
    /// エンジンが「上限で切れた」と判断できることを検証する
    /// </summary>
    [Fact(DisplayName = "上限で切れたツール呼び出しは終了理由と出力のトークン数を添えて返す")]
    public async Task RunAsync_TruncatedToolUse_ReportsOutputLimit()
    {
        var turn = await RunWithSseAsync(
            SseStart()
                + SseEvent(
                    "content_block_start",
                    """{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"tu_1","name":"emit_file","input":{}}}"""
                )
                + SseEvent(
                    "content_block_delta",
                    """{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"a.razor\",\"content\":\"@page"}}"""
                )
                + SseStop("max_tokens", 8192)
        );

        turn.ToolCalls.Should().ContainSingle();
        turn.End.Should().NotBeNull();
        turn.End!.StopReason.Should().Be("max_tokens");
        turn.End.Kind.Should().Be(ChatStopKind.OutputLimit);
        turn.End.OutputTokens.Should().Be(8192);
        turn.End.OmittedBlockKinds.Should().BeEmpty();
    }

    /// <summary>思考のブロックだけで終わった応答は、テキストもツール呼び出しも無く、捨てたブロックの種類を名指しすることを検証する</summary>
    [Fact(DisplayName = "思考のブロックだけの応答は捨てたブロックの種類を名指しする")]
    public async Task RunAsync_ThinkingOnly_ReportsOmittedThinking()
    {
        var turn = await RunWithSseAsync(
            SseStart()
                + SseEvent(
                    "content_block_start",
                    """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}"""
                )
                + SseEvent(
                    "content_block_delta",
                    """{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"hmm"}}"""
                )
                + SseStop("max_tokens", 8192)
        );

        turn.Text.Should().BeEmpty();
        turn.ToolCalls.Should().BeEmpty();
        turn.End!.Kind.Should().Be(ChatStopKind.OutputLimit);
        turn.End.OmittedBlockKinds.Should().Equal("thinking");
    }

    /// <summary>テキストを書き終えた応答は終了理由 end_turn・正常として返ることを検証する</summary>
    [Fact(DisplayName = "書き終えた応答は正常の終わり方として返す")]
    public async Task RunAsync_EndTurn_IsNormal()
    {
        var turn = await RunWithSseAsync(
            SseStart()
                + SseEvent(
                    "content_block_start",
                    """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""
                )
                + SseEvent(
                    "content_block_delta",
                    """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"done"}}"""
                )
                + SseStop("end_turn", 3)
        );

        turn.Text.Should().Be("done");
        turn.End!.StopReason.Should().Be("end_turn");
        turn.End.Kind.Should().Be(ChatStopKind.Normal);
        turn.End.OutputTokens.Should().Be(3);
    }

    /// <summary>Anthropic の終了理由の読み替えを表で検証する（値が無いときは正常、知らない値は Other）</summary>
    [Theory(DisplayName = "終了理由は中立な分類へ読み替える")]
    [InlineData("end_turn", ChatStopKind.Normal)]
    [InlineData("tool_use", ChatStopKind.Normal)]
    [InlineData("stop_sequence", ChatStopKind.Normal)]
    [InlineData(null, ChatStopKind.Normal)]
    [InlineData("max_tokens", ChatStopKind.OutputLimit)]
    [InlineData("refusal", ChatStopKind.Refused)]
    [InlineData("model_context_window_exceeded", ChatStopKind.ContextWindowExceeded)]
    [InlineData("pause_turn", ChatStopKind.Other)]
    [InlineData("something_new", ChatStopKind.Other)]
    public void ToStopKind_MapsStopReasons(string? stopReason, ChatStopKind expected) =>
        AnthropicChatTurnDriver.ToStopKind(stopReason).Should().Be(expected);

    /// <summary>
    /// Models API（<c>GET /v1/models/{id}</c>）と Messages API（<c>POST /v1/messages</c>）を経路で振り分ける HTTP ハンドラー。
    /// Messages へ送った本文の max_tokens と、Models API を呼んだ回数を記録する。
    /// </summary>
    private sealed class ModelsRoutingHandler : HttpMessageHandler
    {
        /// <summary>モデル名 → Models API の応答（null なら例外を投げる＝通信の失敗）</summary>
        public Func<string, HttpResponseMessage?> ModelResponse { get; set; } =
            _ => ModelInfoResponse(64_000);

        /// <summary>Models API を呼んだときの副作用（取り消しの再現など）</summary>
        public Action? OnModelRequest { get; set; }

        /// <summary>Models API を呼んだ回数</summary>
        public int ModelRequests { get; private set; }

        /// <summary>Messages へ送った max_tokens（送った順）</summary>
        public List<long> SentMaxTokens { get; } = new();

        /// <summary>Messages へ送った本文（送った順）</summary>
        public List<string> SentBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;

            if (
                request.Method == HttpMethod.Get
                && path.StartsWith("/v1/models/", StringComparison.Ordinal)
            )
            {
                ModelRequests++;
                OnModelRequest?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();

                return ModelResponse(path["/v1/models/".Length..])
                    ?? throw new HttpRequestException("connection refused");
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            SentBodies.Add(body);
            SentMaxTokens.Add(
                System
                    .Text.Json.JsonDocument.Parse(body)
                    .RootElement.GetProperty("max_tokens")
                    .GetInt64()
            );

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    SseStart()
                        + SseEvent(
                            "content_block_start",
                            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""
                        )
                        + SseEvent(
                            "content_block_delta",
                            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ok"}}"""
                        )
                        + SseStop("end_turn", 2),
                    System.Text.Encoding.UTF8,
                    "text/event-stream"
                ),
            };
        }
    }

    /// <summary>Models API の成功の応答（max_tokens が null なら欄を出さない）</summary>
    private static HttpResponseMessage ModelInfoResponse(long? maxTokens) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"type\":\"model\",\"id\":\"claude-test\",\"display_name\":\"Test\",\"created_at\":\"2026-01-01T00:00:00Z\",\"max_input_tokens\":200000"
                    + (maxTokens is { } value ? $",\"max_tokens\":{value}" : string.Empty)
                    + "}",
                System.Text.Encoding.UTF8,
                "application/json"
            ),
        };

    /// <summary>接続を差し替えられるドライバを、経路で振り分けるハンドラーで作る</summary>
    private static AnthropicChatTurnDriver DriverFor(
        ModelsRoutingHandler handler,
        Func<AnthropicChatConnection> connection
    ) =>
        new(
            connection,
            ErDesignProfile.ErDesign,
            c => new AnthropicClient
            {
                ApiKey = c.ApiKey,
                HttpClient = new HttpClient(handler, disposeHandler: false),
                MaxRetries = 0,
            }
        );

    private static Task<ChatAssistantTurn> RunOnceAsync(
        AnthropicChatTurnDriver driver,
        CancellationToken cancellationToken
    ) =>
        driver.RunAsync(
            new List<ChatHistoryItem> { new(ChatHistoryRole.User, "やあ") },
            _ => { },
            cancellationToken
        );

    /// <summary>
    /// 送る max_tokens は、モデルの出力の上限と 64,000 の小さいほうで、送信の本文と応答の終わり方の両方に載ることを検証する
    /// </summary>
    [Theory(DisplayName = "送る max_tokens はモデルの上限と 64,000 の小さいほう")]
    [InlineData(32_000, 32_000)]
    [InlineData(64_000, 64_000)]
    [InlineData(128_000, 64_000)]
    public async Task RunAsync_SendsSmallerOfModelLimitAndDefault(long modelMax, long expected)
    {
        var handler = new ModelsRoutingHandler { ModelResponse = _ => ModelInfoResponse(modelMax) };
        var driver = DriverFor(handler, () => new AnthropicChatConnection("key-1", "claude-test"));

        var turn = await RunOnceAsync(driver, TestContext.Current.CancellationToken);

        handler.SentMaxTokens.Should().Equal(expected);
        turn.End!.MaxOutputTokens.Should().Be(expected);
    }

    /// <summary>
    /// 同じ接続とモデルでは Models API を 1 回しか呼ばず、モデルか API キーを変えたときだけ取り直すことを検証する
    /// </summary>
    [Fact(DisplayName = "モデルの上限は接続とモデルごとに 1 回だけ取る")]
    public async Task RunAsync_LooksUpModelLimitOncePerConnectionAndModel()
    {
        var handler = new ModelsRoutingHandler
        {
            ModelResponse = model => ModelInfoResponse(model == "claude-small" ? 16_000 : 128_000),
        };
        var connection = new AnthropicChatConnection("key-1", "claude-test");
        var driver = DriverFor(handler, () => connection);
        var token = TestContext.Current.CancellationToken;

        await RunOnceAsync(driver, token);
        await RunOnceAsync(driver, token);
        handler.ModelRequests.Should().Be(1);

        connection = new AnthropicChatConnection("key-1", "claude-small");
        await RunOnceAsync(driver, token);
        await RunOnceAsync(driver, token);
        handler.ModelRequests.Should().Be(2);

        connection = new AnthropicChatConnection("key-2", "claude-small");
        await RunOnceAsync(driver, token);
        handler.ModelRequests.Should().Be(3);

        handler.SentMaxTokens.Should().Equal(64_000, 64_000, 16_000, 16_000, 16_000);
    }

    /// <summary>
    /// Models API が失敗したとき（404・500・通信の失敗・上限の欄なし）は、会話を止めずに 8,192 で送り、
    /// 失敗は覚えずに次の呼び出しで取り直すこと、API キーを応答の終わり方に載せないことを検証する
    /// </summary>
    [Theory(DisplayName = "モデルの上限を取れなければ 8,192 で送り、次の呼び出しで取り直す")]
    [InlineData("404")]
    [InlineData("500")]
    [InlineData("network")]
    [InlineData("missing")]
    public async Task RunAsync_ModelLookupFails_FallsBackAndRetries(string failure)
    {
        var handler = new ModelsRoutingHandler
        {
            ModelResponse = _ =>
                failure switch
                {
                    "404" => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                    {
                        Content = new StringContent(
                            """{"type":"error","error":{"type":"not_found_error","message":"model: claude-test"}}"""
                        ),
                    },
                    "500" => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("{}"),
                    },
                    "network" => null,
                    _ => ModelInfoResponse(null),
                },
        };
        var driver = DriverFor(
            handler,
            () => new AnthropicChatConnection("secret-key-xyz", "claude-test")
        );
        var token = TestContext.Current.CancellationToken;

        var first = await RunOnceAsync(driver, token);
        await RunOnceAsync(driver, token);

        first.Text.Should().Be("ok");
        handler.SentMaxTokens.Should().Equal(8_192, 8_192);
        first.End!.MaxOutputTokens.Should().Be(8_192);
        handler.ModelRequests.Should().Be(2);
        first.End.ToString().Should().NotContain("secret-key-xyz");
    }

    /// <summary>Models API の呼び出し中に取り消されたら、取り消しのまま投げて Messages を送らないことを検証する</summary>
    [Fact(DisplayName = "モデルの上限を取る途中の取り消しは取り消しのまま投げる")]
    public async Task RunAsync_CanceledDuringModelLookup_Throws()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var handler = new ModelsRoutingHandler { OnModelRequest = cts.Cancel };
        var driver = DriverFor(handler, () => new AnthropicChatConnection("key-1", "claude-test"));

        var run = () => RunOnceAsync(driver, cts.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        handler.SentBodies.Should().BeEmpty();
    }
}
