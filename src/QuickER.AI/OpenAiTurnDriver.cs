using System.ClientModel;
using System.Text;
using OpenAI;
using OpenAI.Chat;

namespace QuickER.AI;

/// <summary>OpenAI チャット接続設定（プロバイダ・キー・モデル・エンドポイント）</summary>
/// <param name="Provider">プロバイダ（OpenAI / Ollama）</param>
/// <param name="ApiKey">API キー（Ollama では未使用）</param>
/// <param name="Model">モデル名</param>
/// <param name="EndpointOverride">エンドポイント上書き（未指定はプロバイダ既定）</param>
public sealed record OpenAiChatConnection(
    AiProvider Provider,
    string ApiKey,
    string Model,
    string? EndpointOverride
)
{
    /// <summary>使用するエンドポイント URL を解決する</summary>
    public string ResolveEndpoint() =>
        !string.IsNullOrWhiteSpace(EndpointOverride)
            ? EndpointOverride!
            : Provider switch
            {
                AiProvider.Ollama => "http://localhost:11434/v1",
                _ => "https://api.openai.com/v1",
            };
}

/// <summary>
/// OpenAI SDK のストリーミング Function Calling を呼び出す本番ドライバ。
/// 中立な会話履歴を SDK の <see cref="ChatMessage"/> へ変換し、ツール定義を付けてストリーム実行する。
/// </summary>
public sealed class OpenAiTurnDriver : IChatTurnDriver
{
    private readonly Func<OpenAiChatConnection> _connectionProvider;
    private readonly IReadOnlyList<ChatTool> _tools;

    /// <summary>接続設定プロバイダからドライバを生成する（設定変更を毎ターン反映するため遅延取得する）</summary>
    /// <param name="connectionProvider">接続設定を返す関数</param>
    /// <param name="profile">用途プロファイル（ツール定義セット。合成ルートが明示的に指定する）</param>
    public OpenAiTurnDriver(Func<OpenAiChatConnection> connectionProvider, ErChatProfile profile)
    {
        _connectionProvider = connectionProvider;
        _tools = ChatToolConverter.ToOpenAiTools(profile.Tools);
    }

    /// <inheritdoc />
    public async Task<ChatAssistantTurn> RunAsync(
        IReadOnlyList<ChatHistoryItem> history,
        Action<string> onTextDelta,
        CancellationToken cancellationToken
    )
    {
        var client = CreateClient();
        var messages = history.Select(ToChatMessage).ToList();

        var options = new ChatCompletionOptions();

        foreach (var tool in _tools)
        {
            options.Tools.Add(tool);
        }

        var textBuilder = new StringBuilder();
        var toolCalls = new SortedDictionary<int, ToolCallAccumulator>();

        await foreach (
            var update in client
                .CompleteChatStreamingAsync(messages, options, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    textBuilder.Append(part.Text);
                    onTextDelta(part.Text);
                }
            }

            foreach (var toolUpdate in update.ToolCallUpdates)
            {
                if (!toolCalls.TryGetValue(toolUpdate.Index, out var acc))
                {
                    acc = new ToolCallAccumulator();
                    toolCalls[toolUpdate.Index] = acc;
                }

                if (!string.IsNullOrEmpty(toolUpdate.ToolCallId))
                {
                    acc.Id = toolUpdate.ToolCallId;
                }

                if (!string.IsNullOrEmpty(toolUpdate.FunctionName))
                {
                    acc.Name = toolUpdate.FunctionName;
                }

                if (toolUpdate.FunctionArgumentsUpdate is { } argsUpdate)
                {
                    acc.Arguments.Append(argsUpdate.ToString());
                }
            }
        }

        var calls = toolCalls
            .Values.Where(acc => !string.IsNullOrEmpty(acc.Id) && !string.IsNullOrEmpty(acc.Name))
            .Select(acc => new ChatToolCallRequest(acc.Id!, acc.Name!, acc.Arguments.ToString()))
            .ToList();

        return new ChatAssistantTurn(textBuilder.ToString(), calls);
    }

    /// <summary>接続設定から ChatClient を生成する（Ollama は API キー不要のためダミーを渡す）</summary>
    private ChatClient CreateClient()
    {
        var connection = _connectionProvider();
        var endpoint = new Uri(connection.ResolveEndpoint());
        var key = string.IsNullOrEmpty(connection.ApiKey) ? "ollama" : connection.ApiKey;
        return new ChatClient(
            model: connection.Model,
            credential: new ApiKeyCredential(key),
            options: new OpenAIClientOptions { Endpoint = endpoint }
        );
    }

    /// <summary>中立な履歴項目を OpenAI SDK の ChatMessage へ変換する</summary>
    private static ChatMessage ToChatMessage(ChatHistoryItem item) =>
        item.Role switch
        {
            ChatHistoryRole.System => new SystemChatMessage(item.Text),
            ChatHistoryRole.User => ToUserMessage(item),
            ChatHistoryRole.Tool => new ToolChatMessage(item.ToolCallId ?? string.Empty, item.Text),
            _ => ToAssistantMessage(item),
        };

    /// <summary>
    /// ユーザー履歴項目を UserChatMessage へ変換する。画像添付は image コンテンツパート
    /// （バイト列 + MIME）として積み、テキスト添付は本文末尾へインライン展開する。
    /// </summary>
    /// <remarks>
    /// PDF は添付対象外（<see cref="OpenAiTurnDriver"/> の AttachmentSupport に Pdf を含めない）。
    /// 根拠: OpenAI .NET SDK 2.10.0 の <c>ChatMessageContentPart.CreateFilePart</c> は実験的属性
    /// （OPENAI001＝「評価目的のみ・将来変更/削除の可能性」）でコンパイルエラーになる不安定 API のため、
    /// PDF ファイル入力は本フェーズでは組み立てない。画像パート（CreateImagePart(bytes, mediaType)）は安定 API。
    /// </remarks>
    internal static UserChatMessage ToUserMessage(ChatHistoryItem item)
    {
        // テキスト添付は本文へインライン展開する（履歴再送でも同じ本文に再構築される）
        var effectiveText = TextAttachmentInliner.BuildEffectiveText(item.Text, item.Attachments);

        var images =
            item.Attachments?.Where(a => a.Kind == ChatAttachmentKind.Image).ToList()
            ?? new List<ChatAttachment>();

        if (images.Count == 0)
        {
            return new UserChatMessage(effectiveText);
        }

        var parts = new List<ChatMessageContentPart>();

        foreach (var image in images)
        {
            parts.Add(
                ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(image.Data),
                    image.MediaType
                )
            );
        }

        if (!string.IsNullOrEmpty(effectiveText))
        {
            parts.Add(ChatMessageContentPart.CreateTextPart(effectiveText));
        }

        return new UserChatMessage(parts);
    }

    /// <summary>アシスタント履歴項目を、ツール呼び出し・テキストを保持した AssistantChatMessage へ変換する</summary>
    private static AssistantChatMessage ToAssistantMessage(ChatHistoryItem item)
    {
        if (item.ToolCalls is not { Count: > 0 })
        {
            return new AssistantChatMessage(item.Text);
        }

        var toolCalls = item.ToolCalls.Select(tc =>
            ChatToolCall.CreateFunctionToolCall(
                tc.Id,
                tc.Name,
                BinaryData.FromString(
                    string.IsNullOrWhiteSpace(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson
                )
            )
        );

        var message = new AssistantChatMessage(toolCalls);

        if (!string.IsNullOrEmpty(item.Text))
        {
            message.Content.Add(ChatMessageContentPart.CreateTextPart(item.Text));
        }

        return message;
    }

    /// <summary>ストリーム断片からツール呼び出しを index ごとに組み立てる作業用</summary>
    private sealed class ToolCallAccumulator
    {
        /// <summary>ツール呼び出し ID</summary>
        public string? Id { get; set; }

        /// <summary>ツール名</summary>
        public string? Name { get; set; }

        /// <summary>引数 JSON（断片を連結する）</summary>
        public StringBuilder Arguments { get; } = new();
    }
}
