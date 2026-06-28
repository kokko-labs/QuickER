using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Messages;

namespace QuickER.Services.Chat;

/// <summary>Anthropic (Claude) チャット接続設定（API キー・モデル）</summary>
/// <param name="ApiKey">Anthropic API キー</param>
/// <param name="Model">モデル名（例: <c>claude-opus-4-8</c>）</param>
public sealed record AnthropicChatConnection(string ApiKey, string Model);

/// <summary>
/// Anthropic 公式 C# SDK の Messages API（ストリーミング + Tool Use）を呼び出す本番ドライバ。
/// 中立な会話履歴を Anthropic の <see cref="MessageParam"/> へ変換し、ツール定義を付けてストリーム実行する。
/// OpenAI ドライバと同じ <see cref="IChatTurnDriver"/> seam を実装し、エンジンからは差し替え可能とする。
/// </summary>
public sealed class AnthropicChatTurnDriver : IChatTurnDriver
{
    /// <summary>1 応答あたりの最大出力トークン数</summary>
    private const int MaxOutputTokens = 8192;

    private readonly Func<AnthropicChatConnection> _connectionProvider;
    private readonly IReadOnlyList<ToolUnion> _tools;

    /// <summary>接続設定プロバイダからドライバを生成する（設定変更を毎ターン反映するため遅延取得する）</summary>
    public AnthropicChatTurnDriver(Func<AnthropicChatConnection> connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _tools = ErDiagramDynamicTools.ToAnthropicTools().Select(tool => (ToolUnion)tool).ToList();
    }

    /// <inheritdoc />
    public async Task<ChatAssistantTurn> RunAsync(
        IReadOnlyList<ChatHistoryItem> history,
        Action<string> onTextDelta,
        CancellationToken cancellationToken
    )
    {
        var connection = _connectionProvider();
        var client = new AnthropicClient { ApiKey = connection.ApiKey };

        var systemText = ExtractSystemPrompt(history);
        var parameters = new MessageCreateParams
        {
            MaxTokens = MaxOutputTokens,
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

        return ToAssistantTurn(aggregator.Message());
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
                    messages.Add(new MessageParam { Role = Role.User, Content = item.Text });
                    break;
                case ChatHistoryRole.Assistant:
                    messages.Add(ToAssistantParam(item));
                    break;
                case ChatHistoryRole.Tool:
                    messages.Add(
                        new MessageParam
                        {
                            Role = Role.User,
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

    /// <summary>アシスタント履歴項目を、テキスト・ツール呼び出しを保持した MessageParam へ変換する</summary>
    private static MessageParam ToAssistantParam(ChatHistoryItem item)
    {
        if (item.ToolCalls is not { Count: > 0 })
        {
            return new MessageParam { Role = Role.Assistant, Content = item.Text };
        }

        var blocks = new List<ContentBlockParam>();

        if (!string.IsNullOrEmpty(item.Text))
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

        return new MessageParam { Role = Role.Assistant, Content = blocks };
    }

    /// <summary>応答メッセージからテキストとツール呼び出しを取り出し、中立な 1 ターンへ変換する</summary>
    internal static ChatAssistantTurn ToAssistantTurn(Message message)
    {
        var textBuilder = new StringBuilder();
        var toolCalls = new List<ChatToolCallRequest>();

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
        }

        return new ChatAssistantTurn(textBuilder.ToString(), toolCalls);
    }

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
