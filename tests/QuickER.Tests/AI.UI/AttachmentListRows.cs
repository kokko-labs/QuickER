using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using AiUiStrings = QuickER.AI.UI.Resources.Strings;

namespace QuickER.Tests.AI.UI;

/// <summary>添付リストの表の 1 ケースの組（VM・状態の文の記録・一時フォルダ）</summary>
internal sealed class AttachmentListHarness : IDisposable
{
    private readonly TestTempFolder _folder = new();

    /// <summary>VM を作る</summary>
    /// <param name="support">受け付ける範囲</param>
    public AttachmentListHarness(AttachmentSupport support)
    {
        Vm = new AttachmentListViewModel(Statuses.Add) { Support = support };
    }

    /// <summary>VM</summary>
    public AttachmentListViewModel Vm { get; }

    /// <summary>状態の文の記録</summary>
    public List<string> Statuses { get; } = [];

    /// <summary>一時フォルダ</summary>
    public string Folder => _folder.Path;

    /// <summary>一時フォルダへファイルを書いてパスを返す</summary>
    public string WriteFile(string name, byte[] data)
    {
        var path = Path.Combine(_folder.Path, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>クリップボード画像相当（PNG のバイト列）を追加する</summary>
    public void AddClipboardImage() =>
        Vm.AddClipboardImage(AttachmentListRows.PngBytes(), new DateTime(2026, 1, 2, 3, 4, 5));

    public void Dispose() => _folder.Dispose();
}

/// <summary>添付リストの表の行の入力（受け付ける範囲と本体）</summary>
/// <param name="Support">受け付ける範囲（水準の名前）</param>
/// <param name="Kind">ファイルの種類（受け付けの表の行だけ。操作の列の行は null）</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record AttachmentListCase(
    string Support,
    ChatAttachmentKind? Kind,
    Action<AttachmentListHarness> Body
);

/// <summary>
/// 添付リスト（<see cref="AttachmentListViewModel"/>）の表と、API キー接続の添付の範囲の規則
/// （<see cref="AttachmentSupportResolver.ForApiKeyProvider"/>）の行
/// </summary>
/// <remarks>
/// 受け付けの表は、受け付ける範囲 5 通り × ファイルの種類 4 通りの全部。受け付けるかの期待は範囲ごとに手で書き
/// （<see cref="AttachmentSupportExtensions.Allows"/> を期待に使わない）、断るときの状態の文は resx から流す時点のカルチャで組む。
/// 操作の列の行は元のテストの入力と期待をそのまま持つ。
/// </remarks>
internal static class AttachmentListRows
{
    /// <summary>受け付ける範囲の水準（名前 → 範囲・受け付ける種類の期待）</summary>
    public static IReadOnlyList<(
        string Name,
        AttachmentSupport Support,
        ChatAttachmentKind[] Accepts
    )> Supports =>
        [
            ("None", AttachmentSupport.None, []),
            ("Images", AttachmentSupport.Images, [ChatAttachmentKind.Image]),
            (
                "OpenAi",
                AttachmentSupport.Images | AttachmentSupport.Text,
                [ChatAttachmentKind.Image, ChatAttachmentKind.Text]
            ),
            (
                "ClaudeApi",
                AttachmentSupport.Images | AttachmentSupport.Pdf | AttachmentSupport.Text,
                [ChatAttachmentKind.Image, ChatAttachmentKind.Pdf, ChatAttachmentKind.Text]
            ),
            (
                "ClaudeCode",
                AttachmentSupport.Images
                    | AttachmentSupport.Pdf
                    | AttachmentSupport.Text
                    | AttachmentSupport.Binary,
                [
                    ChatAttachmentKind.Image,
                    ChatAttachmentKind.Pdf,
                    ChatAttachmentKind.Text,
                    ChatAttachmentKind.Binary,
                ]
            ),
        ];

    /// <summary>範囲の水準の名前から範囲を引く</summary>
    public static AttachmentSupport SupportOf(string name) =>
        Supports.Single(entry => entry.Name == name).Support;

    /// <summary>行の表</summary>
    public static ScenarioTable<AttachmentListCase> Table =>
        new(EngineIds.Ai, [.. Acceptance(), .. Operations(), .. Resolver()]);

    /// <summary>PNG シグネチャ＋任意末尾のバイト列</summary>
    public static byte[] PngBytes(int totalLength = 16)
    {
        var data = new byte[Math.Max(totalLength, 8)];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>PDF シグネチャ＋任意末尾のバイト列</summary>
    private static byte[] PdfBytes(int totalLength = 16)
    {
        var data = new byte[Math.Max(totalLength, 5)];
        var signature = "%PDF-"u8.ToArray();
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>種類ごとのファイル（名前と中身。テキストは内容で、バイナリは NUL を含むことで分類される）</summary>
    private static (string Name, byte[] Data) FileOf(ChatAttachmentKind kind) =>
        kind switch
        {
            ChatAttachmentKind.Image => ("figure.png", PngBytes()),
            ChatAttachmentKind.Pdf => ("spec.pdf", PdfBytes()),
            ChatAttachmentKind.Text => (
                "note.txt",
                System.Text.Encoding.UTF8.GetBytes("これはテキスト添付のテストです。\nline2")
            ),
            _ => ("data.bin", [0x00, 0x01, 0x02, 0xFF, 0x00, 0x10]),
        };

    /// <summary>断ったときの状態の文の期待（範囲が無ければ添付できない旨・あれば種類ごとの断りの文）</summary>
    private static string RejectionOf(string support, ChatAttachmentKind kind, string fileName) =>
        support == "None"
            ? AiUiStrings.Attachment_DisabledUnsupported
            : kind switch
            {
                ChatAttachmentKind.Pdf => string.Format(AiUiStrings.Attachment_RejectPdf, fileName),
                ChatAttachmentKind.Text => string.Format(
                    AiUiStrings.Attachment_RejectText,
                    fileName
                ),
                ChatAttachmentKind.Binary => string.Format(
                    AiUiStrings.Attachment_RejectBinary,
                    fileName
                ),
                _ => string.Format(AiUiStrings.Attachment_RejectImage, fileName),
            };

    /// <summary>受け付けの表（範囲 × 種類。ファイルを渡して受け付けるか・断りの文）</summary>
    private static IEnumerable<ScenarioRow<AttachmentListCase>> Acceptance() =>
        from entry in Supports
        from kind in Enum.GetValues<ChatAttachmentKind>()
        let accepts = entry.Accepts.Contains(kind)
        select new ScenarioRow<AttachmentListCase>(
            $"accept-{entry.Name.ToLowerInvariant()}-{kind.ToString().ToLowerInvariant()}",
            null,
            accepts
                ? $"範囲 {entry.Name} は {kind} のファイルを受け付ける"
                : $"範囲 {entry.Name} は {kind} のファイルを断り、理由を状態の文で知らせる",
            new AttachmentListCase(
                entry.Name,
                kind,
                h =>
                {
                    var (name, data) = FileOf(kind);
                    h.Vm.AddFiles([h.WriteFile(name, data)]);

                    if (accepts)
                    {
                        h.Vm.Items.Should().ContainSingle().Which.Attachment.Kind.Should().Be(kind);
                        h.Statuses.Should().BeEmpty();
                    }
                    else
                    {
                        h.Vm.Items.Should().BeEmpty();
                        h.Statuses.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(RejectionOf(entry.Name, kind, name));
                    }
                }
            )
        );

    /// <summary>操作の列（元のテストの入力と期待のまま）</summary>
    private static IEnumerable<ScenarioRow<AttachmentListCase>> Operations() =>
        [
            Op(
                "op-add-clipboard-image-when-supported",
                "対応時に画像を追加でき、送信用の一覧と要約に出る",
                "ClaudeCode",
                h =>
                {
                    h.AddClipboardImage();

                    h.Vm.Items.Should().HaveCount(1);
                    h.Vm.HasAttachments.Should().BeTrue();
                    h.Vm.BuildAttachments().Should().HaveCount(1);
                    h.Vm.BuildSummary().Should().Contain("📎").And.Contain(".png");
                }
            ),
            Op(
                "op-remove-and-clear-empties-items",
                "削除・クリアで空になる",
                "ClaudeCode",
                h =>
                {
                    h.AddClipboardImage();
                    h.Vm.Remove(h.Vm.Items[0]);
                    h.Vm.Items.Should().BeEmpty();

                    h.AddClipboardImage();
                    h.Vm.Clear();
                    h.Vm.Items.Should().BeEmpty();
                    h.Vm.HasAttachments.Should().BeFalse();
                }
            ),
            Op(
                "op-support-none-disables-and-rejects-clipboard-image",
                "範囲が無ければ添付できず理由を示し、クリップボードの画像も断る",
                "None",
                h =>
                {
                    h.Vm.IsEnabled.Should().BeFalse();
                    h.Vm.DisabledReason.Should().NotBeEmpty();

                    h.AddClipboardImage();

                    h.Vm.Items.Should().BeEmpty();
                    h.Statuses.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain(AiUiStrings.Attachment_DisabledUnsupported);
                }
            ),
            Op(
                "op-unreadable-paths-report-status-without-throwing",
                "フォルダや存在しないパスは例外を出さず 1 件ずつ状態の文へ出し、読めるファイルだけを取り込む",
                "ClaudeCode",
                h =>
                {
                    var directory = Directory.CreateDirectory(Path.Combine(h.Folder, "dir"));
                    var missing = Path.Combine(h.Folder, "missing.png");
                    var png = h.WriteFile("ok.png", PngBytes());

                    var act = () => h.Vm.AddFiles([directory.FullName, missing, png]);

                    act.Should().NotThrow();
                    h.Vm.Items.Should().ContainSingle().Which.FileName.Should().Be("ok.png");
                    h.Statuses.Should().HaveCount(2);
                    h.Statuses[0].Should().Contain(directory.Name);
                    h.Statuses[1].Should().Contain("missing.png");
                }
            ),
            Op(
                "op-image-count-over-limit-is-rejected",
                "画像は上限の枚数を超えると断り、上限を状態の文で知らせる",
                "ClaudeCode",
                h =>
                {
                    for (var i = 0; i < ChatAttachmentLimits.MaxImagesPerMessage; i++)
                    {
                        h.AddClipboardImage();
                    }

                    h.Vm.Items.Should().HaveCount(ChatAttachmentLimits.MaxImagesPerMessage);

                    h.AddClipboardImage();

                    h.Vm.Items.Should().HaveCount(ChatAttachmentLimits.MaxImagesPerMessage);
                    h.Statuses.Should()
                        .Contain(s =>
                            s.Contains(
                                ChatAttachmentLimits.MaxImagesPerMessage.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture
                                )
                            )
                        );
                }
            ),
            Op(
                "op-turn-in-progress-blocks-editing",
                "ターンの実行中は添付を追加も削除もできない",
                "ClaudeCode",
                h =>
                {
                    h.AddClipboardImage();
                    var item = h.Vm.Items[0];

                    h.Vm.IsTurnInProgress = true;
                    h.Vm.IsEnabled.Should().BeFalse();

                    h.AddClipboardImage();
                    h.Vm.Items.Should().HaveCount(1);
                    h.Vm.Remove(item);
                    h.Vm.Items.Should().HaveCount(1);
                }
            ),
            Op(
                "op-switch-to-none-clears-pending",
                "範囲が無い接続へ切り替えると送信待ちを消して知らせる",
                "ClaudeCode",
                h =>
                {
                    h.AddClipboardImage();
                    h.Vm.Items.Should().HaveCount(1);

                    h.Vm.Support = AttachmentSupport.None;

                    h.Vm.Items.Should().BeEmpty();
                    h.Statuses.Should().Contain(AiUiStrings.Attachment_ClearedUnsupported);
                }
            ),
            Op(
                "op-shrink-support-removes-unsupported-only",
                "範囲を縮めると受け付けなくなった種類（PDF）だけを除いて知らせる",
                "ClaudeCode",
                h =>
                {
                    h.AddClipboardImage();
                    var pdf = ChatAttachmentFactory.CreateFromBytes("spec.pdf", PdfBytes());
                    h.Vm.Items.Add(new PendingAttachmentItem(pdf.Attachment!));

                    h.Vm.Support = SupportOf("OpenAi");

                    h.Vm.Items.Should()
                        .ContainSingle()
                        .Which.Attachment.Kind.Should()
                        .Be(ChatAttachmentKind.Image);
                    h.Statuses.Should().Contain(AiUiStrings.Attachment_RemovedUnsupported);
                }
            ),
            Op(
                "op-file-dialog-filter-opens-all-formats",
                "ファイルの選択のフィルタは全形式の開放と補助のフィルタ（画像・PDF）を持つ",
                "OpenAi",
                h =>
                {
                    h.Vm.FileDialogFilter.Should().Contain(AiUiStrings.Attachment_FilterAllFiles);
                    h.Vm.FileDialogFilter.Should().Contain("*.*");
                    h.Vm.FileDialogFilter.Should().Contain("*.png");
                    h.Vm.FileDialogFilter.Should().Contain("*.pdf");
                }
            ),
        ];

    /// <summary>API キー接続の添付の範囲の規則（プロバイダー → 範囲。ChatTurnEngine は注入された範囲をそのまま公開する）</summary>
    private static IEnumerable<ScenarioRow<AttachmentListCase>> Resolver() =>
        from provider in new[] { AiProvider.Claude, AiProvider.OpenAI, AiProvider.LocalLlm }
        let expected = provider == AiProvider.Claude ? "ClaudeApi" : "OpenAi"
        select Op(
            $"resolver-api-key-{provider.ToString().ToLowerInvariant()}",
            $"API キー接続の {provider} の添付の範囲は {expected}（ローカル LLM は OpenAI 互換で同じ）",
            expected,
            _ =>
            {
                var engine = new ChatTurnEngine(
                    new NeverCalledDriver(),
                    new RecordingToolHost(),
                    new ChatEngines.ChatSyncDispatcher(),
                    () => true,
                    ErDesignProfile.ErDesign,
                    attachmentSupport: () => AttachmentSupportResolver.ForApiKeyProvider(provider)
                );

                AttachmentSupportResolver
                    .ForApiKeyProvider(provider)
                    .Should()
                    .Be(SupportOf(expected));
                engine.AttachmentSupport.Should().Be(SupportOf(expected));

                // 接続方式から範囲を引く規則（エンジンを作る前に使う）も同じ範囲を返す
                AttachmentSupportResolver
                    .ForBackend(ErChatBackendKind.ApiKey, provider)
                    .Should()
                    .Be(SupportOf(expected));
            }
        );

    private static ScenarioRow<AttachmentListCase> Op(
        string id,
        string description,
        string support,
        Action<AttachmentListHarness> body
    ) => new(id, null, description, new AttachmentListCase(support, null, body));

    /// <summary>呼ばれないドライバ（範囲の公開だけを見る）</summary>
    private sealed class NeverCalledDriver : IChatTurnDriver
    {
        public Task<ChatAssistantTurn> RunAsync(
            IReadOnlyList<ChatHistoryItem> history,
            Action<string> onTextDelta,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("範囲の行はドライバを呼ばない");
    }
}
