using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;

namespace QuickER.Tests.AI;

/// <summary>
/// 各エンジンが公開する <see cref="AttachmentSupport"/>（[Flags]）の判定マトリクスを検証するテストクラス。
/// Anthropic=画像＋PDF＋テキスト・OpenAI／ローカル LLM=画像＋テキスト・Codex=なし・Claude Code=全種別。
/// </summary>
public class AttachmentSupportMatrixTests
{
    /// <summary>Anthropic（Claude）が受け付ける種別集合</summary>
    private const AttachmentSupport ClaudeApiSupport =
        AttachmentSupport.Images | AttachmentSupport.Pdf | AttachmentSupport.Text;

    /// <summary>OpenAI・ローカル LLM が受け付ける種別集合（ローカル LLM は OpenAI 互換 API のため同一）</summary>
    private const AttachmentSupport OpenAiSupport =
        AttachmentSupport.Images | AttachmentSupport.Text;

    /// <summary>Claude Code が受け付ける全種別集合</summary>
    private const AttachmentSupport ClaudeCodeSupport =
        AttachmentSupport.Images
        | AttachmentSupport.Pdf
        | AttachmentSupport.Text
        | AttachmentSupport.Binary;

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

    /// <summary>合成ルートの共有規則（<see cref="AttachmentSupportResolver"/>）が期待どおりの集合を返すことを検証する</summary>
    [Theory(DisplayName = "API キー接続の添付範囲はプロバイダー依存")]
    [InlineData(AiProvider.Claude)]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.LocalLlm)]
    public void Resolver_ForApiKeyProvider_ReturnsExpectedFlags(AiProvider provider)
    {
        var expected = provider switch
        {
            AiProvider.Claude => ClaudeApiSupport,
            AiProvider.OpenAI or AiProvider.LocalLlm => OpenAiSupport,
            _ => AttachmentSupport.None,
        };

        var engine = new ChatTurnEngine(
            new NoopTurnDriver(),
            new NoopToolHost(),
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign,
            attachmentSupport: () => AttachmentSupportResolver.ForApiKeyProvider(provider)
        );

        engine.AttachmentSupport.Should().Be(expected);
        AttachmentSupportResolver.ForApiKeyProvider(provider).Should().Be(expected);
    }

    /// <summary>OpenAI は画像・テキストを許可し PDF・バイナリは許可しないことを検証する（[Flags] のビット単位）</summary>
    [Fact(DisplayName = "OpenAI は画像・テキストのみ許可")]
    public void OpenAi_Allows_ImageAndTextOnly()
    {
        var support = AttachmentSupportResolver.ForApiKeyProvider(AiProvider.OpenAI);

        support.Allows(ChatAttachmentKind.Image).Should().BeTrue();
        support.Allows(ChatAttachmentKind.Text).Should().BeTrue();
        support.Allows(ChatAttachmentKind.Pdf).Should().BeFalse();
        support.Allows(ChatAttachmentKind.Binary).Should().BeFalse();
    }

    /// <summary>Claude Code は全種別（画像・PDF・テキスト・バイナリ）に対応することを検証する</summary>
    [Fact(DisplayName = "Claude Code は全種別に対応")]
    public void ClaudeCode_AttachmentSupport_IsAllKinds()
    {
        var engine = new ClaudeCodeChatEngine(
            new FakeAvailableClaudeCodeClient(),
            toolHost: null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        engine.AttachmentSupport.Should().Be(ClaudeCodeSupport);
        engine.AttachmentSupport.Allows(ChatAttachmentKind.Binary).Should().BeTrue();
    }

    /// <summary>
    /// 接続方式から添付範囲を引く規則（エンジンを作る前に使う）が、各エンジンが実際に公開する範囲と一致することを検証する。
    /// </summary>
    /// <remarks>
    /// モック生成は会話を始める前に規則で、AI チャットはエンジンから添付範囲を読む。両者が食い違うと、
    /// 片方のダイアログだけが添付を受け付けて送信時に落とす。
    /// </remarks>
    [Theory(DisplayName = "接続方式の添付範囲の規則はエンジンが公開する範囲と一致する")]
    [InlineData(ErChatBackendKind.Codex, AiProvider.OpenAI)]
    [InlineData(ErChatBackendKind.ClaudeCode, AiProvider.OpenAI)]
    [InlineData(ErChatBackendKind.Copilot, AiProvider.OpenAI)]
    [InlineData(ErChatBackendKind.ApiKey, AiProvider.OpenAI)]
    [InlineData(ErChatBackendKind.ApiKey, AiProvider.Claude)]
    [InlineData(ErChatBackendKind.ApiKey, AiProvider.LocalLlm)]
    public void Resolver_ForBackend_MatchesEngine(ErChatBackendKind backend, AiProvider provider)
    {
        var dispatcher = new SyncUiDispatcher();
        IErChatEngine engine = backend switch
        {
            ErChatBackendKind.Codex => new CodexChatEngine(
                new FakeCodexAppServerClient(),
                toolHost: null,
                dispatcher,
                ErDesignProfile.ErDesign
            ),
            ErChatBackendKind.ClaudeCode => new ClaudeCodeChatEngine(
                new FakeAvailableClaudeCodeClient(),
                toolHost: null,
                dispatcher,
                ErDesignProfile.ErDesign
            ),
            ErChatBackendKind.Copilot => new CopilotChatEngine(
                new FakeCopilotRuntimeClient(),
                toolHost: null,
                dispatcher,
                ErDesignProfile.ErDesign
            ),
            _ => new ChatTurnEngine(
                new NoopTurnDriver(),
                new NoopToolHost(),
                dispatcher,
                () => true,
                ErDesignProfile.ErDesign,
                attachmentSupport: () => AttachmentSupportResolver.ForApiKeyProvider(provider)
            ),
        };

        AttachmentSupportResolver
            .ForBackend(backend, provider)
            .Should()
            .Be(engine.AttachmentSupport);
    }

    /// <summary>CLI 3 方式の添付範囲の値（Codex=なし・Claude Code=全種別・Copilot=画像のみ）を検証する</summary>
    [Fact(DisplayName = "CLI 3 方式の添付範囲は Codex なし・Claude Code 全種別・Copilot 画像のみ")]
    public void Resolver_ForBackend_CliValues()
    {
        AttachmentSupportResolver
            .ForBackend(ErChatBackendKind.Codex, AiProvider.OpenAI)
            .Should()
            .Be(AttachmentSupport.None);
        AttachmentSupportResolver
            .ForBackend(ErChatBackendKind.ClaudeCode, AiProvider.OpenAI)
            .Should()
            .Be(ClaudeCodeSupport);
        AttachmentSupportResolver
            .ForBackend(ErChatBackendKind.Copilot, AiProvider.OpenAI)
            .Should()
            .Be(AttachmentSupport.Images);
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
