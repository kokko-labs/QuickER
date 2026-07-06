using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Chat;

namespace QuickER.Tests.Services.Chat;

/// <summary>
/// <see cref="ClaudeCodeChatEngine"/> の添付処理（attachments/ へ書き出し・プロンプト付記・
/// Read の許可ツール追加・添付なし時の挙動不変・添付使用後のターンでの Read 維持）を検証するテストクラス。
/// </summary>
public class ClaudeCodeChatEngineAttachmentTests
{
    /// <summary>UI スレッドへのマーシャリングを同期実行で代替するテスト用ディスパッチャ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>プロンプト・起動オプションを記録するフェイク Claude Code クライアント</summary>
    private sealed class RecordingClaudeCodeClient : IClaudeCodeClient
    {
        public List<string> Prompts { get; } = new();
        public List<ClaudeCodeLaunchOptions> Options { get; } = new();

        public bool IsAvailable() => true;

        public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClaudeLoginProbeResult.LoggedIn);

        public Task<ClaudeCodeTurnOutcome> RunTurnAsync(
            string prompt,
            string? resumeSessionId,
            ClaudeCodeLaunchOptions options,
            Action<string> onAssistantText,
            CancellationToken cancellationToken
        )
        {
            Prompts.Add(prompt);
            Options.Add(options);
            return Task.FromResult(new ClaudeCodeTurnOutcome(true, null, "s1", false));
        }

        public void Interrupt() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>PNG シグネチャのバイト列を作る</summary>
    private static ChatAttachment PngAttachment(string fileName = "figure.png")
    {
        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];
        return new ChatAttachment(fileName, ChatAttachmentKind.Image, "image/png", pngData);
    }

    /// <summary>テキスト添付を作る</summary>
    private static ChatAttachment TextAttachment(string fileName = "spec.txt")
    {
        var data = System.Text.Encoding.UTF8.GetBytes("要件メモ");
        return new ChatAttachment(fileName, ChatAttachmentKind.Text, "text/plain", data);
    }

    /// <summary>バイナリ添付を作る</summary>
    private static ChatAttachment BinaryAttachment(string fileName = "data.bin")
    {
        byte[] data = [0x00, 0x01, 0x02, 0xFF];
        return new ChatAttachment(
            fileName,
            ChatAttachmentKind.Binary,
            "application/octet-stream",
            data
        );
    }

    /// <summary>Claude Code の添付対応は全種別（画像・PDF・テキスト・バイナリ）であることを検証する</summary>
    [Fact(DisplayName = "AttachmentSupport は全種別")]
    public void AttachmentSupport_IsAllKinds()
    {
        var engine = new ClaudeCodeChatEngine(
            new RecordingClaudeCodeClient(),
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        engine
            .AttachmentSupport.Should()
            .Be(
                AttachmentSupport.Images
                    | AttachmentSupport.Pdf
                    | AttachmentSupport.Text
                    | AttachmentSupport.Binary
            );
    }

    /// <summary>
    /// 添付あり送信で、ファイルが attachments/ へ書き出され、プロンプトに絶対パスが付記され、
    /// 許可ツールに Read が追加されることを検証する。
    /// </summary>
    [Fact(DisplayName = "添付ありは attachments/ へ書き出し・パス付記・Read 追加")]
    public async Task SendAsync_WithAttachment_WritesFileAppendsPathAndAllowsRead()
    {
        var client = new RecordingClaudeCodeClient();
        var engine = new ClaudeCodeChatEngine(
            client,
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync(
            "この図を参考に",
            new[] { PngAttachment() },
            TestContext.Current.CancellationToken
        );

        // プロンプトへ添付パスが付記される
        client.Prompts.Should().ContainSingle();
        client.Prompts[0].Should().Contain("添付ファイル（Read ツールで読むこと）");
        client.Prompts[0].Should().Contain("attachments");

        // 付記された絶対パスにファイルが実在し、attachments/ 配下である
        var line = client.Prompts[0].Split('\n').First(l => l.Contains("figure.png"));
        var path = line.TrimStart('-', ' ');
        File.Exists(path).Should().BeTrue();
        Path.GetDirectoryName(path)!.Should().EndWith("attachments");

        // 許可ツールに Read が追加される
        client.Options[0].AdditionalAllowedTools.Should().Contain("Read");

        await engine.DisposeAsync();
    }

    /// <summary>添付なし送信では Read が追加されず、パス付記も無い（従来と不変）ことを検証する</summary>
    [Fact(DisplayName = "添付なしは Read を追加せずパス付記も無い")]
    public async Task SendAsync_NoAttachment_DoesNotAllowRead()
    {
        var client = new RecordingClaudeCodeClient();
        var engine = new ClaudeCodeChatEngine(
            client,
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        client.Options[0].AdditionalAllowedTools.Should().BeEmpty();
        client.Prompts[0].Should().NotContain("添付ファイル");

        await engine.DisposeAsync();
    }

    /// <summary>
    /// 一度添付を使った会話では、以降の添付なしターンでも Read 許可が維持されることを検証する
    /// （継続セッションが添付を読み返せるようにするため）。
    /// </summary>
    [Fact(DisplayName = "添付使用後のターンも Read 許可を維持する")]
    public async Task SendAsync_AfterAttachment_KeepsReadAllowed()
    {
        var client = new RecordingClaudeCodeClient();
        var engine = new ClaudeCodeChatEngine(
            client,
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        // 1 ターン目: 添付あり
        await engine.SendAsync(
            "図を見て",
            new[] { PngAttachment() },
            TestContext.Current.CancellationToken
        );
        // 2 ターン目: 添付なし
        await engine.SendAsync("続けて", TestContext.Current.CancellationToken);

        client.Options[1].AdditionalAllowedTools.Should().Contain("Read");

        await engine.DisposeAsync();
    }

    /// <summary>
    /// テキスト・バイナリ添付も画像/PDF と同様に attachments/ へ書き出され、パスが付記され、
    /// Read 許可が付くことを検証する（Read 経路は種別に依らず共通）。
    /// </summary>
    [Fact(DisplayName = "テキスト・バイナリも書き出し・パス付記・Read 追加")]
    public async Task SendAsync_TextAndBinary_WritesAndAppendsPath()
    {
        var client = new RecordingClaudeCodeClient();
        var engine = new ClaudeCodeChatEngine(
            client,
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync(
            "参考資料",
            new[] { TextAttachment(), BinaryAttachment() },
            TestContext.Current.CancellationToken
        );

        client.Prompts[0].Should().Contain("spec.txt");
        client.Prompts[0].Should().Contain("data.bin");
        client.Options[0].AdditionalAllowedTools.Should().Contain("Read");

        // 実ファイルが書き出されている
        foreach (var name in new[] { "spec.txt", "data.bin" })
        {
            var line = client.Prompts[0].Split('\n').First(l => l.Contains(name));
            File.Exists(line.TrimStart('-', ' ')).Should().BeTrue();
        }

        await engine.DisposeAsync();
    }

    /// <summary>バイナリを含む添付では、代替案をユーザーへ伝える一文がプロンプトに付記されることを検証する</summary>
    [Fact(DisplayName = "バイナリ含む送信は代替案の付記が入る")]
    public async Task SendAsync_WithBinary_AppendsFallbackNote()
    {
        var client = new RecordingClaudeCodeClient();
        var engine = new ClaudeCodeChatEngine(
            client,
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync(
            "これ何？",
            new[] { BinaryAttachment() },
            TestContext.Current.CancellationToken
        );

        client.Prompts[0].Should().Contain("Read で読めない形式");
        client.Prompts[0].Should().Contain("代替案");

        await engine.DisposeAsync();
    }

    /// <summary>バイナリを含まない（画像のみ）送信では、代替案の付記が入らないことを検証する</summary>
    [Fact(DisplayName = "バイナリ無しは代替案の付記が入らない")]
    public async Task SendAsync_WithoutBinary_NoFallbackNote()
    {
        var client = new RecordingClaudeCodeClient();
        var engine = new ClaudeCodeChatEngine(
            client,
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync(
            "図を見て",
            new[] { PngAttachment() },
            TestContext.Current.CancellationToken
        );

        client.Prompts[0].Should().NotContain("Read で読めない形式");

        await engine.DisposeAsync();
    }

    /// <summary>ファイル名が衝突する添付は連番付きで別ファイルへ書き出されることを検証する</summary>
    [Fact(DisplayName = "同名添付は連番で別ファイルになる")]
    public async Task SendAsync_DuplicateNames_WritesSeparateFiles()
    {
        var client = new RecordingClaudeCodeClient();
        var engine = new ClaudeCodeChatEngine(
            client,
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync(
            "2 枚",
            new[] { PngAttachment("a.png"), PngAttachment("a.png") },
            TestContext.Current.CancellationToken
        );

        var pathLines = client
            .Prompts[0]
            .Split('\n')
            .Where(l => l.TrimStart().StartsWith("- "))
            .Select(l => l.TrimStart('-', ' '))
            .ToList();

        pathLines.Should().HaveCount(2);
        pathLines[0].Should().NotBe(pathLines[1]);
        pathLines.All(File.Exists).Should().BeTrue();

        await engine.DisposeAsync();
    }
}
