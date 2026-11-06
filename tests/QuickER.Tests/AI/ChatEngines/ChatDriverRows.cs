using System.Text;
using System.Text.RegularExpressions;
using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>ドライバの表の行の入力（利用者の発話の履歴項目と、ドライバごとの射影の期待）</summary>
/// <param name="Item">利用者の発話の履歴項目</param>
/// <param name="Expected">ドライバ（軸の値）→ 射影の期待（要素ごとのパターン。<c>*</c> だけが任意の文字列）</param>
internal sealed record ChatDriverCase(
    ChatHistoryItem Item,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Expected
);

/// <summary>
/// API キー接続のドライバの表：同じ利用者の発話（添付の形）を、Anthropic と OpenAI の両方のドライバへ流し、送る形の射影を照合する
/// </summary>
/// <remarks>
/// 期待の違い（Anthropic は PDF を document で送る・OpenAI は PDF を送らない・Anthropic は添付の無い本文を文字列で送る）は
/// 製品の添付の範囲（<see cref="AttachmentSupportResolver.ForApiKeyProvider"/>）と SDK の形から来る。期待は要素の数と並びまで厳密に照合し、
/// 元のテストが見ていない部分（本文の書式）だけを <c>*</c> にする。
/// </remarks>
internal static class ChatDriverRows
{
    private const string Anthropic = nameof(AnthropicChatTurnDriver);
    private const string OpenAi = nameof(OpenAiTurnDriver);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Pdf = "%PDF-1.7"u8.ToArray();

    /// <summary>行の表</summary>
    public static ScenarioTable<ChatDriverCase> Table =>
        new(
            EngineIds.Ai,
            [
                Row(
                    "user-no-attachments",
                    "添付の無い発話は本文だけで送る（Anthropic は文字列の content）",
                    User("こんにちは"),
                    anthropic: ["string:こんにちは"],
                    openAi: ["text:こんにちは"]
                ),
                Row(
                    "user-image",
                    "画像の添付は画像の部分（Base64・メディアの型）と本文で送る",
                    User("この図を参考に", Image("figure.png")),
                    anthropic: [$"image:ImagePng:{Base64(Png)}", "text:*"],
                    openAi: [$"image:image/png:{Base64(Png)}", "text:*"]
                ),
                Row(
                    "user-pdf",
                    "PDF の添付は Anthropic では document の部分で送り、OpenAI では送らない（本文だけ）",
                    User("この仕様書に沿って", PdfOf("spec.pdf")),
                    anthropic: [$"document:{Base64(Pdf)}", "text:*"],
                    openAi: ["text:*"]
                ),
                Row(
                    "user-text-attachment",
                    "テキストの添付は本文へインラインで展開し、部分を作らない",
                    User("この要件で", Text("req.md", "要件A\n要件B")),
                    anthropic: ["string:*この要件で*Attached file: req.md*要件A*"],
                    openAi: ["text:*この要件で*Attached file: req.md*要件A*"]
                ),
                Row(
                    "user-image-and-text",
                    "画像とテキストの添付は、画像の部分と、テキストをインラインで展開した本文で送る",
                    User("両方見て", Image("f.png"), Text("m.txt", "メモ本文")),
                    anthropic:
                    [
                        $"image:ImagePng:{Base64(Png)}",
                        "text:*両方見て*Attached file: m.txt*メモ本文*",
                    ],
                    openAi:
                    [
                        $"image:image/png:{Base64(Png)}",
                        "text:*両方見て*Attached file: m.txt*メモ本文*",
                    ]
                ),
                Row(
                    "user-pdf-and-text",
                    "PDF とテキストの添付は、Anthropic では document の部分とインラインの本文、OpenAI ではインラインの本文だけで送る",
                    User("両方参照", PdfOf("s.pdf"), Text("n.txt", "補足メモ")),
                    anthropic:
                    [
                        $"document:{Base64(Pdf)}",
                        "text:*両方参照*Attached file: n.txt*補足メモ*",
                    ],
                    openAi: ["text:*両方参照*Attached file: n.txt*補足メモ*"]
                ),
            ]
        );

    /// <summary>射影が期待のパターンの並びに当たるか（要素の数と並びまで）</summary>
    /// <param name="actual">射影</param>
    /// <param name="expected">要素ごとのパターン</param>
    public static bool Matches(IReadOnlyList<string> actual, IReadOnlyList<string> expected) =>
        actual.Count == expected.Count
        && actual
            .Zip(expected)
            .All(pair =>
                Regex.IsMatch(
                    pair.First,
                    "^" + Regex.Escape(pair.Second).Replace("\\*", ".*") + "$",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant
                )
            );

    private static ScenarioRow<ChatDriverCase> Row(
        string id,
        string description,
        ChatHistoryItem item,
        IReadOnlyList<string> anthropic,
        IReadOnlyList<string> openAi
    ) =>
        new(
            id,
            null,
            description,
            new ChatDriverCase(
                item,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [Anthropic] = anthropic,
                    [OpenAi] = openAi,
                }
            )
        );

    private static ChatHistoryItem User(string text, params ChatAttachment[] attachments) =>
        new(ChatHistoryRole.User, text, Attachments: attachments.Length > 0 ? attachments : null);

    private static ChatAttachment Image(string name) =>
        new(name, ChatAttachmentKind.Image, "image/png", Png);

    private static ChatAttachment PdfOf(string name) =>
        new(name, ChatAttachmentKind.Pdf, "application/pdf", Pdf);

    private static ChatAttachment Text(string name, string body) =>
        new(name, ChatAttachmentKind.Text, "text/plain", Encoding.UTF8.GetBytes(body));

    private static string Base64(byte[] data) => Convert.ToBase64String(data);
}
