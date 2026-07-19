using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ProviderRoutingTurnDriver"/> のプロバイダ種別 → 委譲先ドライバ選択（ルーティング）を
/// 全分岐で検証するテストクラス。Claude のみ Anthropic ドライバ・それ以外（OpenAI/Ollama/未知）は
/// OpenAI ドライバへ振り分けられること、選択関数が毎ターン評価されることを確認する。
/// </summary>
public class ProviderRoutingTurnDriverTests
{
    /// <summary>どのドライバへ振り分けられたか記録し、識別用の応答を返すフェイクドライバ</summary>
    private sealed class RecordingDriver : IChatTurnDriver
    {
        private readonly string _label;

        public RecordingDriver(string label) => _label = label;

        /// <summary>RunAsync が呼ばれた回数</summary>
        public int CallCount { get; private set; }

        /// <summary>最後に渡された履歴</summary>
        public IReadOnlyList<ChatHistoryItem>? LastHistory { get; private set; }

        /// <summary>最後に渡されたキャンセルトークン</summary>
        public CancellationToken LastToken { get; private set; }

        public Task<ChatAssistantTurn> RunAsync(
            IReadOnlyList<ChatHistoryItem> history,
            Action<string> onTextDelta,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            LastHistory = history;
            LastToken = cancellationToken;

            return Task.FromResult(
                new ChatAssistantTurn(_label, Array.Empty<ChatToolCallRequest>())
            );
        }
    }

    private static readonly IReadOnlyList<ChatHistoryItem> History =
    [
        new(ChatHistoryRole.User, "やあ"),
    ];

    /// <summary>指定プロバイダを固定で返すルーターと各フェイクドライバを組み立てる</summary>
    private static (
        ProviderRoutingTurnDriver Router,
        RecordingDriver OpenAi,
        RecordingDriver Anthropic
    ) BuildRouter(AiProvider provider)
    {
        var openAi = new RecordingDriver("openai");
        var anthropic = new RecordingDriver("anthropic");
        var router = new ProviderRoutingTurnDriver(() => provider, openAi, anthropic);

        return (router, openAi, anthropic);
    }

    /// <summary>Claude プロバイダでは Anthropic ドライバへ振り分けられることを検証する</summary>
    [Fact(DisplayName = "Claude は Anthropic ドライバへ振り分ける")]
    public async Task Claude_RoutesToAnthropicDriver()
    {
        var (router, openAi, anthropic) = BuildRouter(AiProvider.Claude);

        var turn = await router.RunAsync(History, _ => { }, CancellationToken.None);

        turn.Text.Should().Be("anthropic");
        anthropic.CallCount.Should().Be(1);
        openAi.CallCount.Should().Be(0);
    }

    /// <summary>OpenAI プロバイダでは OpenAI ドライバへ振り分けられることを検証する</summary>
    [Fact(DisplayName = "OpenAI は OpenAI ドライバへ振り分ける")]
    public async Task OpenAi_RoutesToOpenAiDriver()
    {
        var (router, openAi, anthropic) = BuildRouter(AiProvider.OpenAI);

        var turn = await router.RunAsync(History, _ => { }, CancellationToken.None);

        turn.Text.Should().Be("openai");
        openAi.CallCount.Should().Be(1);
        anthropic.CallCount.Should().Be(0);
    }

    /// <summary>Ollama プロバイダは（非 Claude なので）OpenAI ドライバへ振り分けられることを検証する</summary>
    [Fact(DisplayName = "Ollama は OpenAI ドライバへ振り分ける")]
    public async Task Ollama_RoutesToOpenAiDriver()
    {
        var (router, openAi, anthropic) = BuildRouter(AiProvider.Ollama);

        var turn = await router.RunAsync(History, _ => { }, CancellationToken.None);

        turn.Text.Should().Be("openai");
        openAi.CallCount.Should().Be(1);
        anthropic.CallCount.Should().Be(0);
    }

    /// <summary>未定義のプロバイダ値でも（Claude 以外の既定分岐で）OpenAI ドライバへ振り分けられることを検証する</summary>
    [Fact(DisplayName = "未知のプロバイダ値は OpenAI ドライバへ振り分ける")]
    public async Task UnknownProvider_RoutesToOpenAiDriver()
    {
        var (router, openAi, anthropic) = BuildRouter((AiProvider)999);

        var turn = await router.RunAsync(History, _ => { }, CancellationToken.None);

        turn.Text.Should().Be("openai");
        openAi.CallCount.Should().Be(1);
        anthropic.CallCount.Should().Be(0);
    }

    /// <summary>選択関数が毎ターン評価され、ターンごとに振り分け先が切り替わることを検証する</summary>
    [Fact(DisplayName = "選択関数は毎ターン評価される")]
    public async Task ProviderSelector_IsEvaluatedEveryTurn()
    {
        var provider = AiProvider.OpenAI;
        var openAi = new RecordingDriver("openai");
        var anthropic = new RecordingDriver("anthropic");
        var router = new ProviderRoutingTurnDriver(() => provider, openAi, anthropic);

        // 1 ターン目は OpenAI
        var first = await router.RunAsync(History, _ => { }, CancellationToken.None);
        first.Text.Should().Be("openai");

        // プロバイダを切り替えると 2 ターン目は Anthropic（選択関数が再評価される証拠）
        provider = AiProvider.Claude;
        var second = await router.RunAsync(History, _ => { }, CancellationToken.None);
        second.Text.Should().Be("anthropic");

        openAi.CallCount.Should().Be(1);
        anthropic.CallCount.Should().Be(1);
    }

    /// <summary>履歴とキャンセルトークンが選択された委譲先へそのまま渡されることを検証する</summary>
    [Fact(DisplayName = "履歴とキャンセルトークンは委譲先へそのまま渡る")]
    public async Task Arguments_ArePassedThroughToDelegate()
    {
        var (router, _, anthropic) = BuildRouter(AiProvider.Claude);
        using var cts = new CancellationTokenSource();

        await router.RunAsync(History, _ => { }, cts.Token);

        anthropic.LastHistory.Should().BeSameAs(History);
        anthropic.LastToken.Should().Be(cts.Token);
    }
}
