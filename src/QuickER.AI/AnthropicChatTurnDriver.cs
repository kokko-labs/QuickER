using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Messages;

namespace QuickER.AI;

/// <summary>Anthropic (Claude) チャット接続設定（API キー・モデル）</summary>
/// <param name="ApiKey">Anthropic API キー</param>
/// <param name="Model">モデル名（例: <c>claude-opus-5-5</c>）</param>
public sealed record AnthropicChatConnection(string ApiKey, string Model);

/// <summary>
/// Anthropic 公式 C# SDK の Messages API（ストリーミング + Tool Use）を呼び出す本番ドライバ。
/// 中立な会話履歴を Anthropic の <see cref="MessageParam"/> へ変換し、ツール定義を付けてストリーム実行する。
/// OpenAI ドライバと同じ <see cref="IChatTurnDriver"/> seam を実装し、エンジンからは差し替え可能とする。
/// </summary>
public sealed class AnthropicChatTurnDriver : IChatTurnDriver
{
    /// <summary>
    /// 1 応答あたりに求める出力トークン数の上限（モデルの上限がこれより小さければモデルの上限を送る）
    /// </summary>
    /// <remarks>
    /// 考えるモデル（Claude 5 系は thinking を省くと adaptive で考える）では、思考のトークンも max_tokens に数える。
    /// 小さいと、ファイル全体を提出するような応答が思考だけで上限に届いて何も返さない。
    /// ストリーミングで受けるので、大きな値でも HTTP のタイムアウトの問題は無い。
    /// </remarks>
    internal const long DefaultMaxOutputTokens = 64_000;

    /// <summary>モデルの出力の上限を Models API から取れなかったときに送る max_tokens（古いモデルでも 400 にならない値）</summary>
    internal const long FallbackMaxOutputTokens = 8_192;

    /// <summary>Models API の 1 回の呼び出しの待ち時間の上限（失敗している間に送信を長く待たせない）</summary>
    private static readonly TimeSpan ModelLookupTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<AnthropicChatConnection> _connectionProvider;
    private readonly IReadOnlyList<ToolUnion> _tools;
    private readonly Func<AnthropicChatConnection, AnthropicClient> _clientFactory;

    /// <summary>
    /// 接続（API キー）とモデルごとに求めた、送る max_tokens（Models API から取れた値だけを覚える）。
    /// API キーは照合のキーにだけ使い、ログ・例外・診断には出さない
    /// </summary>
    private readonly ConcurrentDictionary<(string ApiKey, string Model), long> _maxOutputTokens =
        new();

    /// <summary>接続設定プロバイダからドライバを生成する（設定変更を毎ターン反映するため遅延取得する）</summary>
    /// <param name="connectionProvider">接続設定を返す関数</param>
    /// <param name="profile">用途プロファイル（ツール定義セット。合成ルートが明示的に指定する）</param>
    public AnthropicChatTurnDriver(
        Func<AnthropicChatConnection> connectionProvider,
        ErChatProfile profile
    )
        : this(
            connectionProvider,
            profile,
            connection => new AnthropicClient { ApiKey = connection.ApiKey }
        ) { }

    /// <summary>クライアントの生成方法を差し替えてドライバを生成する（テスト用）</summary>
    /// <param name="connectionProvider">接続設定を返す関数</param>
    /// <param name="profile">用途プロファイル</param>
    /// <param name="clientFactory">
    /// 接続設定から SDK のクライアントを作る関数。作ったクライアントは呼び出しごとにドライバが破棄する
    /// </param>
    internal AnthropicChatTurnDriver(
        Func<AnthropicChatConnection> connectionProvider,
        ErChatProfile profile,
        Func<AnthropicChatConnection, AnthropicClient> clientFactory
    )
    {
        _connectionProvider = connectionProvider;
        _clientFactory = clientFactory;
        _tools = ChatToolConverter
            .ToAnthropicTools(profile.Tools)
            .Select(tool => (ToolUnion)tool)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ChatAssistantTurn> RunAsync(
        IReadOnlyList<ChatHistoryItem> history,
        Action<string> onTextDelta,
        CancellationToken cancellationToken
    )
    {
        var connection = _connectionProvider();
        // クライアントはインスタンスごとに自分の HttpClient（接続プール）を持つため、呼び出しごとに破棄する。
        // 破棄しないと、ツール往復のたびに作るクライアントの接続が積み上がる
        using var client = _clientFactory(connection);

        var maxOutputTokens = await ResolveMaxOutputTokensAsync(
                client,
                connection,
                cancellationToken
            )
            .ConfigureAwait(false);
        var systemText = ExtractSystemPrompt(history);
        var parameters = new MessageCreateParams
        {
            MaxTokens = maxOutputTokens,
            Model = connection.Model,
            System = string.IsNullOrEmpty(systemText)
                ? null
                : (MessageCreateParamsSystem)systemText,
            Messages = ToMessageParams(history),
            Tools = _tools,
        };

        var aggregator = new MessageContentAggregator();

        await foreach (
            var streamEvent in client
                .Messages.CreateStreaming(parameters)
                .CollectAsync(aggregator)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            if (
                streamEvent.TryPickContentBlockDelta(out var blockDelta)
                && blockDelta.Delta.TryPickText(out var textDelta)
                && !string.IsNullOrEmpty(textDelta.Text)
            )
            {
                onTextDelta(textDelta.Text);
            }
        }

        return ToAssistantTurn(aggregator.Message(), maxOutputTokens);
    }

    /// <summary>
    /// 送る max_tokens を求める（<see cref="DefaultMaxOutputTokens"/> とモデルの上限の小さいほう）。
    /// </summary>
    /// <remarks>
    /// モデルの上限は Models API（<c>GET /v1/models/{id}</c> の <c>max_tokens</c>）から取り、接続とモデルごとに覚えて
    /// ターンごとに取り直さない。取れなかったとき（権限・通信・タイムアウト・知らないモデル・値なし）は、
    /// そのターンを <see cref="FallbackMaxOutputTokens"/> で送って会話を続け、失敗は覚えずに次の呼び出しで取り直す
    /// （一時的な失敗でその会話の間ずっと小さい上限に固定しない）。呼び出し側の取り消しはそのまま投げる。
    /// </remarks>
    private async Task<long> ResolveMaxOutputTokensAsync(
        AnthropicClient client,
        AnthropicChatConnection connection,
        CancellationToken cancellationToken
    )
    {
        var key = (connection.ApiKey, connection.Model);

        if (_maxOutputTokens.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var model = await client
                .Models.WithOptions(options =>
                    options with
                    {
                        MaxRetries = 0,
                        Timeout = ModelLookupTimeout,
                    }
                )
                .Retrieve(connection.Model, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (model.MaxTokens is not > 0)
            {
                return FallbackMaxOutputTokens;
            }

            var resolved = Math.Min(DefaultMaxOutputTokens, model.MaxTokens.Value);
            _maxOutputTokens[key] = resolved;
            return resolved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // 上限が分からないだけで会話は続けられるので、例外の文言は出さずに既定の小さい上限で送る
            return FallbackMaxOutputTokens;
        }
    }

    /// <summary>履歴中の System 役割項目を結合し、Anthropic の system パラメータ文字列を組み立てる</summary>
    internal static string ExtractSystemPrompt(IReadOnlyList<ChatHistoryItem> history)
    {
        var systemTexts = history
            .Where(item => item.Role == ChatHistoryRole.System && !string.IsNullOrEmpty(item.Text))
            .Select(item => item.Text);
        return string.Join("\n\n", systemTexts);
    }

    /// <summary>中立な会話履歴を Anthropic の MessageParam 一覧へ変換する（System は除外し別パラメータへ回す）</summary>
    internal static List<MessageParam> ToMessageParams(IReadOnlyList<ChatHistoryItem> history)
    {
        var messages = new List<MessageParam>();

        foreach (var item in history)
        {
            switch (item.Role)
            {
                case ChatHistoryRole.System:
                    // System は MessageCreateParams.System へ回すためメッセージ列には積まない
                    break;
                case ChatHistoryRole.User:
                    messages.Add(ToUserParam(item));
                    break;
                case ChatHistoryRole.Assistant:
                    // テキストもツール呼び出しも無い応答（空の応答）は再送しない。空の content は API が
                    // 400 で拒否し、履歴に残る限り以後の全ターンが失敗する。前後の user ターンが
                    // 連続することになるが、連続する同じ役割のターンは API 側が 1 つに結合して受け付ける
                    if (IsEmptyAssistantItem(item))
                    {
                        break;
                    }

                    messages.Add(ToAssistantParam(item));
                    break;
                case ChatHistoryRole.Tool:
                    messages.Add(
                        new UserMessageParam
                        {
                            Content = new List<ContentBlockParam>
                            {
                                new ToolResultBlockParam
                                {
                                    ToolUseID = item.ToolCallId ?? string.Empty,
                                    Content = item.Text,
                                },
                            },
                        }
                    );
                    break;
            }
        }

        return messages;
    }

    /// <summary>
    /// ユーザー履歴項目を MessageParam へ変換する。画像・PDF 添付は image / document(PDF) ブロックへ、
    /// テキスト添付は本文末尾へインライン展開する（添付なしなら単純なテキストメッセージ）。
    /// </summary>
    internal static MessageParam ToUserParam(ChatHistoryItem item)
    {
        // テキスト添付は本文へインライン展開する（履歴再送でも同じ本文に再構築される）
        var effectiveText = TextAttachmentInliner.BuildEffectiveText(item.Text, item.Attachments);

        // 画像・PDF はコンテンツブロックとして積む対象（テキストはインライン済みなので除外）
        var mediaAttachments =
            item.Attachments?.Where(a =>
                    a.Kind is ChatAttachmentKind.Image or ChatAttachmentKind.Pdf
                )
                .ToList()
            ?? new List<ChatAttachment>();

        if (mediaAttachments.Count == 0)
        {
            return new UserMessageParam { Content = effectiveText };
        }

        var blocks = new List<ContentBlockParam>();

        // 画像・PDF を先に置き、その後にユーザーのテキスト（インライン展開済み）を添える構成にする
        foreach (var attachment in mediaAttachments)
        {
            blocks.Add(ToContentBlock(attachment));
        }

        if (!string.IsNullOrEmpty(effectiveText))
        {
            blocks.Add(new TextBlockParam { Text = effectiveText });
        }

        return new UserMessageParam { Content = blocks };
    }

    /// <summary>添付 1 件を Anthropic のコンテンツブロック（image base64 / document PDF base64）へ変換する</summary>
    internal static ContentBlockParam ToContentBlock(ChatAttachment attachment) =>
        attachment.Kind == ChatAttachmentKind.Image
            ? new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data = attachment.ToBase64(),
                    MediaType = ToImageMediaType(attachment.MediaType),
                },
            }
            : new DocumentBlockParam
            {
                Source = new Base64PdfSource { Data = attachment.ToBase64() },
            };

    /// <summary>MIME 文字列を Anthropic の画像 MediaType 列挙へ変換する（未知は PNG 扱い）</summary>
    private static MediaType ToImageMediaType(string mediaType) =>
        mediaType switch
        {
            "image/jpeg" => MediaType.ImageJpeg,
            "image/gif" => MediaType.ImageGif,
            "image/webp" => MediaType.ImageWebP,
            _ => MediaType.ImagePng,
        };

    /// <summary>テキストが空白だけで、ツール呼び出しも無いアシスタント履歴項目か</summary>
    private static bool IsEmptyAssistantItem(ChatHistoryItem item) =>
        string.IsNullOrWhiteSpace(item.Text) && item.ToolCalls is not { Count: > 0 };

    /// <summary>アシスタント履歴項目を、テキスト・ツール呼び出しを保持した MessageParam へ変換する</summary>
    private static MessageParam ToAssistantParam(ChatHistoryItem item)
    {
        if (item.ToolCalls is not { Count: > 0 })
        {
            return new AssistantMessageParam { Content = item.Text };
        }

        var blocks = new List<ContentBlockParam>();

        // 空白だけのテキストブロックも API が拒否するため、ツール呼び出しの前に置かない
        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            blocks.Add(new TextBlockParam { Text = item.Text });
        }

        foreach (var call in item.ToolCalls)
        {
            blocks.Add(
                new ToolUseBlockParam
                {
                    ID = call.Id,
                    Name = call.Name,
                    Input = ParseArguments(call.ArgumentsJson),
                }
            );
        }

        return new AssistantMessageParam { Content = blocks };
    }

    /// <summary>
    /// 応答メッセージからテキストとツール呼び出しを取り出し、中立な 1 ターンへ変換する
    /// （終了理由・出力のトークン数・捨てたブロックの種類も添える）
    /// </summary>
    /// <param name="message">集約した応答メッセージ</param>
    /// <param name="maxOutputTokens">この応答を求めたときに送った max_tokens（分からなければ null）</param>
    internal static ChatAssistantTurn ToAssistantTurn(Message message, long? maxOutputTokens = null)
    {
        var textBuilder = new StringBuilder();
        var toolCalls = new List<ChatToolCallRequest>();
        var omitted = new List<string>();

        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                textBuilder.Append(textBlock.Text);
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                toolCalls.Add(
                    new ChatToolCallRequest(
                        toolUse.ID,
                        toolUse.Name,
                        JsonSerializer.Serialize(toolUse.Input)
                    )
                );
            }
            else
            {
                // テキストとツール呼び出し以外（思考のブロックなど）は会話へ渡さないが、診断のために種類だけ残す
                var kind =
                    block.TryPickThinking(out _) ? "thinking"
                    : block.TryPickRedactedThinking(out _) ? "redacted_thinking"
                    : "other";

                if (!omitted.Contains(kind))
                {
                    omitted.Add(kind);
                }
            }
        }

        var stopReason = message.StopReason?.Raw();

        return new ChatAssistantTurn(
            textBuilder.ToString(),
            toolCalls,
            new ChatResponseEnd(
                stopReason,
                ToStopKind(stopReason),
                message.Usage?.OutputTokens,
                omitted,
                maxOutputTokens
            )
        );
    }

    /// <summary>
    /// Anthropic の stop_reason を中立な分類へ読み替える（知らない値は <see cref="ChatStopKind.Other"/>。
    /// 値が無いときは正常とみなし、応答を止める理由にしない）
    /// </summary>
    internal static ChatStopKind ToStopKind(string? stopReason) =>
        stopReason switch
        {
            null or "end_turn" or "tool_use" or "stop_sequence" => ChatStopKind.Normal,
            "max_tokens" => ChatStopKind.OutputLimit,
            "refusal" => ChatStopKind.Refused,
            "model_context_window_exceeded" => ChatStopKind.ContextWindowExceeded,
            _ => ChatStopKind.Other,
        };

    /// <summary>ツール引数 JSON を Anthropic の input 用辞書へ変換する（空・不正は空辞書）</summary>
    private static IReadOnlyDictionary<string, JsonElement> ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new Dictionary<string, JsonElement>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson)
                ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }
}
