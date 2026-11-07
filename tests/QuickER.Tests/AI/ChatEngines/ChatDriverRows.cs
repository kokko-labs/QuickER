using System.Text;
using System.Text.RegularExpressions;
using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>ドライバの表の行の入力（ドライバの送る形を射影する手順と、ドライバごとの射影の期待）</summary>
/// <param name="Project">ドライバのアダプタを受けて、送る形を射影する（利用者の発話の変換・ツールの往復の 2 回目の要求）</param>
/// <param name="Expected">ドライバ（軸の値）→ 射影の期待（要素ごとのパターン。<c>*</c> だけが任意の文字列）</param>
internal sealed record ChatDriverCase(
    Func<ChatDriverAdapter, CancellationToken, Task<IReadOnlyList<string>>> Project,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Expected
);

/// <summary>
/// API キー接続のドライバの表：同じ利用者の発話（添付の形）とツールの往復を、Anthropic と OpenAI の両方のドライバへ流し、送る形の射影を照合する
/// </summary>
/// <remarks>
/// 期待の違い（Anthropic は PDF を document で送る・OpenAI は PDF を送らない・Anthropic は添付の無い本文を文字列で送る）は
/// 製品の添付の範囲（<see cref="AttachmentSupportResolver.ForApiKeyProvider"/>）と SDK の形から来る。期待は要素の数と並びまで厳密に照合し、
/// 元のテストが見ていない部分（本文の書式）だけを <c>*</c> にする。
/// ツールの往復の行は、本物のエンジンとドライバを偽のサーバーへつないで 1 ターン流し（<see cref="ChatDriverToolRoundTrip"/>）、
/// サーバーが付けたツールの呼び出しの ID が 2 回目の要求のアシスタントのツールの呼び出しとツールの結果の両方へ戻ることを照合する。
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
                ToolRoundTripRow(
                    "tool-call-id-round-trips",
                    "サーバーが付けたツールの呼び出しの ID を、ストリームから読み取り、ツールを実行した後の要求でアシスタントのツールの呼び出しと"
                        + "ツールの結果の両方へそのまま戻す（ID を落とすと、サーバーがツールの結果を呼び出しへ突き合わせられない）",
                    new ToolRoundTrip(
                        "Book のテーブルを追加して",
                        "call_server_1",
                        "add_entity",
                        "{\"table_name\":\"Book\"}"
                    ),
                    [
                        "user",
                        "tool_call:call_server_1:add_entity",
                        "tool_result:call_server_1",
                        "executed:add_entity:*Book*",
                    ]
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
                (adapter, _) => Task.FromResult(adapter.ProjectUserMessage(item)),
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [Anthropic] = anthropic,
                    [OpenAi] = openAi,
                }
            )
        );

    /// <summary>ツールの往復の行（期待はどのドライバも同じ）</summary>
    private static ScenarioRow<ChatDriverCase> ToolRoundTripRow(
        string id,
        string description,
        ToolRoundTrip trip,
        IReadOnlyList<string> expected
    ) =>
        new(
            id,
            null,
            description,
            new ChatDriverCase(
                (adapter, cancellationToken) =>
                    ChatDriverToolRoundTrip.RunAsync(adapter, trip, cancellationToken),
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [Anthropic] = expected,
                    [OpenAi] = expected,
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
