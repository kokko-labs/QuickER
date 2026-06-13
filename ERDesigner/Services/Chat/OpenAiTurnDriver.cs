using System.ClientModel;
using System.Text;
using OpenAI;
using OpenAI.Chat;

namespace ERDesigner.Services.Chat;

/// <summary>OpenAI チャット接続設定（プロバイダ・キー・モデル・エンドポイント）</summary>
/// <param name="Provider">プロバイダ（OpenAI / Ollama）</param>
/// <param name="ApiKey">API キー（Ollama では未使用）</param>
/// <param name="Model">モデル名</param>
/// <param name="EndpointOverride">エンドポイント上書き（未指定はプロバイダ既定）</param>
public sealed record OpenAiChatConnection(AiProvider Provider, string ApiKey, string Model, string? EndpointOverride)
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
public sealed class OpenAiTurnDriver : IOpenAiTurnDriver
{
    private readonly Func<OpenAiChatConnection> _connectionProvider;
    private readonly IReadOnlyList<ChatTool> _tools;

    /// <summary>接続設定プロバイダからドライバを生成する（設定変更を毎ターン反映するため遅延取得する）</summary>
    public OpenAiTurnDriver(Func<OpenAiChatConnection> connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _tools = ErDiagramDynamicTools.ToOpenAiTools();
    }

    /// <inheritdoc />
    public async Task<OpenAiAssistantTurn> RunAsync(IReadOnlyList<OpenAiChatHistoryItem> history, Action<string> onTextDelta, CancellationToken cancellationToken)
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

        await foreach (var update in client.CompleteChatStreamingAsync(messages, options, cancellationToken).ConfigureAwait(false))
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
            .Select(acc => new OpenAiToolCallRequest(acc.Id!, acc.Name!, acc.Arguments.ToString()))
            .ToList();

        return new OpenAiAssistantTurn(textBuilder.ToString(), calls);
    }

    /// <summary>接続設定から ChatClient を生成する（Ollama は API キー不要のためダミーを渡す）</summary>
    private ChatClient CreateClient()
    {
        var connection = _connectionProvider();
        var endpoint = new Uri(connection.ResolveEndpoint());
        var key = string.IsNullOrEmpty(connection.ApiKey) ? "ollama" : connection.ApiKey;
        return new ChatClient(model: connection.Model, credential: new ApiKeyCredential(key), options: new OpenAIClientOptions { Endpoint = endpoint });
    }

    /// <summary>中立な履歴項目を OpenAI SDK の ChatMessage へ変換する</summary>
    private static ChatMessage ToChatMessage(OpenAiChatHistoryItem item) =>
        item.Role switch
        {
            OpenAiChatRole.System => new SystemChatMessage(item.Text),
            OpenAiChatRole.User => new UserChatMessage(item.Text),
            OpenAiChatRole.Tool => new ToolChatMessage(item.ToolCallId ?? string.Empty, item.Text),
            _ => ToAssistantMessage(item),
        };

    /// <summary>アシスタント履歴項目を、ツール呼び出し・テキストを保持した AssistantChatMessage へ変換する</summary>
    private static AssistantChatMessage ToAssistantMessage(OpenAiChatHistoryItem item)
    {
        if (item.ToolCalls is not { Count: > 0 })
        {
            return new AssistantChatMessage(item.Text);
        }

        var toolCalls = item.ToolCalls.Select(tc =>
            ChatToolCall.CreateFunctionToolCall(tc.Id, tc.Name, BinaryData.FromString(string.IsNullOrWhiteSpace(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson))
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
