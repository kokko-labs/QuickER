using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.Services.Chat;

/// <summary>
/// 各エンジンが公開する <see cref="AttachmentSupport"/> の判定マトリクスを検証するテストクラス。
/// Anthropic=画像＋PDF・OpenAI=画像・Ollama=なし・Codex=なし・Claude Code=画像＋PDF。
/// </summary>
public class AttachmentSupportMatrixTests
{
    /// <summary>UI スレッドへのマーシャリングを同期実行で代替するテスト用ディスパッチャ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>常に空応答を返すダミードライバ（AttachmentSupport の評価だけを見る）</summary>
    private sealed class NoopTurnDriver : IChatTurnDriver
    {
        public Task<ChatAssistantTurn> RunAsync(
            IReadOnlyList<ChatHistoryItem> history,
            Action<string> onTextDelta,
            CancellationToken cancellationToken
        ) => Task.FromResult(new ChatAssistantTurn(string.Empty, []));
    }

    /// <summary>何もしないツールホスト</summary>
    private sealed class NoopToolHost : IErDiagramToolHost
    {
        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            (string.Empty, true);
    }

    /// <summary>実際のプロバイダー選択に応じた添付範囲を返す判定関数（合成ルートと同じ規則）</summary>
    private static AttachmentSupport ResolveApiKeySupport(AiProvider provider) =>
        provider switch
        {
            AiProvider.Claude => AttachmentSupport.ImagesAndPdf,
            AiProvider.OpenAI => AttachmentSupport.Images,
            _ => AttachmentSupport.None,
        };

    /// <summary>API キー接続: Anthropic は画像＋PDF・OpenAI は画像・Ollama はなし、と判定されることを検証する</summary>
    [Theory(DisplayName = "API キー接続の添付範囲はプロバイダー依存")]
    [InlineData(AiProvider.Claude, AttachmentSupport.ImagesAndPdf)]
    [InlineData(AiProvider.OpenAI, AttachmentSupport.Images)]
    [InlineData(AiProvider.Ollama, AttachmentSupport.None)]
    public void ChatTurnEngine_AttachmentSupport_DependsOnProvider(
        AiProvider provider,
        AttachmentSupport expected
    )
    {
        var engine = new ChatTurnEngine(
            new NoopTurnDriver(),
            new NoopToolHost(),
            new SyncUiDispatcher(),
            () => true,
            attachmentSupport: () => ResolveApiKeySupport(provider)
        );

        engine.AttachmentSupport.Should().Be(expected);
    }

    /// <summary>Claude Code は画像＋PDF に対応することを検証する</summary>
    [Fact(DisplayName = "Claude Code は ImagesAndPdf")]
    public void ClaudeCode_AttachmentSupport_IsImagesAndPdf()
    {
        var engine = new ClaudeCodeChatEngine(
            new FakeAvailableClaudeCodeClient(),
            toolHost: null,
            new SyncUiDispatcher()
        );

        engine.AttachmentSupport.Should().Be(AttachmentSupport.ImagesAndPdf);
    }

    /// <summary>常に利用可能・成功を返す最小フェイク（AttachmentSupport 評価用）</summary>
    private sealed class FakeAvailableClaudeCodeClient : IClaudeCodeClient
    {
        public bool IsAvailable() => true;

        public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClaudeLoginProbeResult.LoggedIn);

        public Task<ClaudeCodeTurnOutcome> RunTurnAsync(
            string prompt,
            string? resumeSessionId,
            ClaudeCodeLaunchOptions options,
            Action<string> onAssistantText,
            CancellationToken cancellationToken
        ) => Task.FromResult(new ClaudeCodeTurnOutcome(true, null, null, false));

        public void Interrupt() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
