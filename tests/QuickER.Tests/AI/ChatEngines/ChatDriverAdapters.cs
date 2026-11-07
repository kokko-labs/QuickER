using System.Net.Http;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using OpenAI.Chat;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>API キー接続のドライバの軸の値ごとのアダプタ（利用者の発話の履歴項目を、ドライバが送る形へ変えて射影する）</summary>
/// <remarks>
/// 射影の書式：1 要素＝送る内容の 1 部分。<c>string:{本文}</c>（Anthropic の文字列の content）・<c>text:{本文}</c>（テキストの部分）・
/// <c>image:{メディアの型}:{Base64}</c>（画像の部分）・<c>document:{Base64}</c>（PDF の部分）。ツールの往復は 2 回目の要求のメッセージ列を射影する（<see cref="ProjectRequest"/>）。SDK の型の違いはこの射影に閉じ込め、
/// アダプタの中で軸の値を名指しして分岐しない。
/// </remarks>
internal abstract class ChatDriverAdapter : IMatrixAdapter
{
    /// <summary>軸の値（ドライバの型の名前）</summary>
    public abstract string AxisValue { get; }

    /// <summary>利用者の発話の履歴項目を、ドライバが送る形へ変えて射影する</summary>
    /// <param name="item">利用者の発話の履歴項目</param>
    public abstract IReadOnlyList<string> ProjectUserMessage(ChatHistoryItem item);

    /// <summary>偽のサーバーを起こし、そこへつないだ本物のドライバを返す（1 回目の応答はツールの呼び出し・2 回目はテキスト）</summary>
    /// <param name="trip">往復の入力</param>
    /// <param name="cancellationToken">取り消し</param>
    public abstract Task<FakeChatServer> StartServerAsync(
        ToolRoundTrip trip,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// ドライバが送った要求の本文（JSON）のメッセージ列を射影する。1 要素＝送る内容の 1 部分：<c>user</c>（利用者の本文）・
    /// <c>assistant</c>（アシスタントの本文）・<c>tool_call:{ID}:{ツール名}</c>・<c>tool_result:{ID}</c>。システムの指示は射影しない
    /// </summary>
    /// <param name="json">要求の本文</param>
    public abstract IReadOnlyList<string> ProjectRequest(string json);
}

/// <summary>Anthropic のドライバ（<see cref="AnthropicChatTurnDriver.ToUserParam"/>）</summary>
internal sealed class AnthropicDriverAdapter : ChatDriverAdapter
{
    /// <inheritdoc/>
    public override string AxisValue => nameof(AnthropicChatTurnDriver);

    /// <inheritdoc/>
    public override IReadOnlyList<string> ProjectUserMessage(ChatHistoryItem item)
    {
        var content = AnthropicChatTurnDriver.ToUserParam(item).Content;

        if (content.Value is string text)
        {
            return [$"string:{text}"];
        }

        return ((IReadOnlyList<ContentBlockParam>)content.Value!)
            .Select(block =>
                block.Value switch
                {
                    ImageBlockParam { Source.Value: Base64ImageSource source } =>
                        $"image:{source.MediaType.Value()}:{source.Data}",
                    DocumentBlockParam { Source.Value: Base64PdfSource source } =>
                        $"document:{source.Data}",
                    TextBlockParam textBlock => $"text:{textBlock.Text}",
                    var other => $"other:{other?.GetType().Name}",
                }
            )
            .ToList();
    }

    /// <inheritdoc/>
    /// <remarks>偽のサーバーは HttpClient のハンドラ（製品の internal のクライアントの差し込み口へ渡す）</remarks>
    public override Task<FakeChatServer> StartServerAsync(
        ToolRoundTrip trip,
        CancellationToken cancellationToken
    )
    {
        var handler = new ChatDriverToolRoundTrip.ScriptedSseHandler([
            AnthropicSse.ToolUse(trip),
            AnthropicSse.Text("done"),
        ]);
        var driver = new AnthropicChatTurnDriver(
            () => new AnthropicChatConnection("test-key", "claude-test"),
            ErDesignProfile.ErDesign,
            connection => new AnthropicClient
            {
                ApiKey = connection.ApiKey,
                // ドライバは呼び出しごとにクライアントを破棄するので、ハンドラ（記録）は破棄させない
                HttpClient = new HttpClient(handler, disposeHandler: false),
                MaxRetries = 0,
            }
        );

        return Task.FromResult(
            new FakeChatServer(
                driver,
                handler.Requests,
                () =>
                {
                    handler.Dispose();
                    return ValueTask.CompletedTask;
                }
            )
        );
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> ProjectRequest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var parts = new List<string>();

        foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
        {
            var role = message.GetProperty("role").GetString();
            var content = message.GetProperty("content");

            if (content.ValueKind == JsonValueKind.String)
            {
                parts.Add($"{role}");
                continue;
            }

            foreach (var block in content.EnumerateArray())
            {
                parts.Add(
                    block.GetProperty("type").GetString() switch
                    {
                        "tool_use" =>
                            $"tool_call:{block.GetProperty("id").GetString()}:{block.GetProperty("name").GetString()}",
                        "tool_result" =>
                            $"tool_result:{block.GetProperty("tool_use_id").GetString()}",
                        _ => $"{role}",
                    }
                );
            }
        }

        return parts;
    }
}

/// <summary>OpenAI（ローカル LLM も同じ）のドライバ（<see cref="OpenAiTurnDriver.ToUserMessage"/>）</summary>
internal sealed class OpenAiDriverAdapter : ChatDriverAdapter
{
    /// <inheritdoc/>
    public override string AxisValue => nameof(OpenAiTurnDriver);

    /// <inheritdoc/>
    public override IReadOnlyList<string> ProjectUserMessage(ChatHistoryItem item) =>
        OpenAiTurnDriver
            .ToUserMessage(item)
            .Content.Select(part =>
                part.Kind switch
                {
                    ChatMessageContentPartKind.Image =>
                        $"image:{part.ImageBytesMediaType}:{Convert.ToBase64String(part.ImageBytes.ToArray())}",
                    ChatMessageContentPartKind.Text => $"text:{part.Text}",
                    var other => $"other:{other}",
                }
            )
            .ToList();

    /// <inheritdoc/>
    /// <remarks>
    /// ドライバは差し込み口を持たないので、偽のサーバーはループバックの HTTP サーバーにし、接続のエンドポイントの上書き
    /// （ローカル LLM の正規の設定）でそこへ向ける
    /// </remarks>
    public override async Task<FakeChatServer> StartServerAsync(
        ToolRoundTrip trip,
        CancellationToken cancellationToken
    )
    {
        var (baseUrl, requests, stop) = await ChatDriverToolRoundTrip.StartLoopbackSseServerAsync(
            [OpenAiSse.ToolCall(trip), OpenAiSse.Text("done")],
            cancellationToken
        );
        var driver = new OpenAiTurnDriver(
            () =>
                new OpenAiChatConnection(
                    AiProvider.LocalLlm,
                    string.Empty,
                    "test-model",
                    $"{baseUrl.TrimEnd('/')}/v1"
                ),
            ErDesignProfile.ErDesign
        );

        return new FakeChatServer(driver, requests, stop);
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> ProjectRequest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var parts = new List<string>();

        foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
        {
            var role = message.GetProperty("role").GetString();

            switch (role)
            {
                case "system":
                    break;
                case "tool":
                    parts.Add($"tool_result:{message.GetProperty("tool_call_id").GetString()}");
                    break;
                case "assistant":
                    if (
                        message.TryGetProperty("content", out var content)
                        && content.ValueKind is not JsonValueKind.Null
                        && !(
                            content.ValueKind == JsonValueKind.String
                            && content.GetString() == string.Empty
                        )
                    )
                    {
                        parts.Add("assistant");
                    }

                    if (message.TryGetProperty("tool_calls", out var toolCalls))
                    {
                        parts.AddRange(
                            toolCalls
                                .EnumerateArray()
                                .Select(call =>
                                    $"tool_call:{call.GetProperty("id").GetString()}:{call.GetProperty("function").GetProperty("name").GetString()}"
                                )
                        );
                    }

                    break;
                default:
                    parts.Add($"{role}");
                    break;
            }
        }

        return parts;
    }
}

/// <summary>API キー接続のドライバの軸（正本＝QuickER.AI の <see cref="IChatTurnDriver"/> の具象の型の名前）</summary>
internal static class ChatDriverAxis
{
    /// <summary>軸の名前</summary>
    public const string Name = "ChatDriver";

    /// <summary>正本（QuickER.AI の <see cref="IChatTurnDriver"/> を実装する具象の型の名前・序数順）</summary>
    public static IReadOnlyList<string> Names { get; } =
        typeof(IChatTurnDriver)
            .Assembly.GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && typeof(IChatTurnDriver).IsAssignableFrom(type)
            )
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>軸</summary>
    public static MatrixAxis<ChatDriverAdapter> Axis { get; } =
        new(
            Name,
            Names,
            [new AnthropicDriverAdapter(), new OpenAiDriverAdapter()],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [nameof(ProviderRoutingTurnDriver)] =
                    "プロバイダーごとに Anthropic / OpenAI のドライバへ振り分けるだけで、送る形へ変えない（振り分けは ProviderRoutingTurnDriverTests が持つ）",
            }
        );
}
