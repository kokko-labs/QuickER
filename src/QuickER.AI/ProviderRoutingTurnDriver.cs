namespace QuickER.AI;

/// <summary>
/// 「API キー接続」バックエンド内のプロバイダ選択に応じて、毎ターン
/// OpenAI／ローカル LLM ドライバと Anthropic (Claude) ドライバへ振り分ける <see cref="IChatTurnDriver"/>。
/// これにより <see cref="ChatTurnEngine"/> を変更せずに Claude を同一バックエンドへ追加できる。
/// </summary>
public sealed class ProviderRoutingTurnDriver : IChatTurnDriver
{
    private readonly Func<AiProvider> _providerSelector;
    private readonly IChatTurnDriver _openAiDriver;
    private readonly IChatTurnDriver _anthropicDriver;

    /// <summary>プロバイダ選択関数と各プロバイダ向けドライバからルーターを生成する</summary>
    /// <param name="providerSelector">現在の API プロバイダを返す関数（毎ターン評価する）</param>
    /// <param name="openAiDriver">OpenAI / ローカル LLM 向けドライバ</param>
    /// <param name="anthropicDriver">Anthropic (Claude) 向けドライバ</param>
    public ProviderRoutingTurnDriver(
        Func<AiProvider> providerSelector,
        IChatTurnDriver openAiDriver,
        IChatTurnDriver anthropicDriver
    )
    {
        _providerSelector = providerSelector;
        _openAiDriver = openAiDriver;
        _anthropicDriver = anthropicDriver;
    }

    /// <inheritdoc />
    public Task<ChatAssistantTurn> RunAsync(
        IReadOnlyList<ChatHistoryItem> history,
        Action<string> onTextDelta,
        CancellationToken cancellationToken
    )
    {
        var driver = _providerSelector() == AiProvider.Claude ? _anthropicDriver : _openAiDriver;
        return driver.RunAsync(history, onTextDelta, cancellationToken);
    }
}
