using Anthropic.Models.Messages;
using OpenAI.Chat;
using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>API キー接続のドライバの軸の値ごとのアダプタ（利用者の発話の履歴項目を、ドライバが送る形へ変えて射影する）</summary>
/// <remarks>
/// 射影の書式：1 要素＝送る内容の 1 部分。<c>string:{本文}</c>（Anthropic の文字列の content）・<c>text:{本文}</c>（テキストの部分）・
/// <c>image:{メディアの型}:{Base64}</c>（画像の部分）・<c>document:{Base64}</c>（PDF の部分）。SDK の型の違いはこの射影に閉じ込め、
/// アダプタの中で軸の値を名指しして分岐しない。
/// </remarks>
internal abstract class ChatDriverAdapter : IMatrixAdapter
{
    /// <summary>軸の値（ドライバの型の名前）</summary>
    public abstract string AxisValue { get; }

    /// <summary>利用者の発話の履歴項目を、ドライバが送る形へ変えて射影する</summary>
    /// <param name="item">利用者の発話の履歴項目</param>
    public abstract IReadOnlyList<string> ProjectUserMessage(ChatHistoryItem item);
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
