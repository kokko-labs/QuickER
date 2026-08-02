using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="OpenAiChatConnection"/> の解決規則（エンドポイント既定・API キーのダミー代替）を
/// 検証するテストクラス。ローカル LLM はキーが任意なので、未入力／入力ありの両方を固定する。
/// </summary>
public class OpenAiChatConnectionTests
{
    private static OpenAiChatConnection Connection(
        AiProvider provider,
        string apiKey = "",
        string? endpointOverride = null
    ) => new(provider, apiKey, "some-model", endpointOverride);

    /// <summary>ローカル LLM の既定エンドポイントを検証する</summary>
    [Fact(DisplayName = "ローカル LLM の既定エンドポイントはローカルサーバー")]
    public void ResolveEndpoint_LocalLlm_UsesLocalDefault() =>
        Connection(AiProvider.LocalLlm).ResolveEndpoint().Should().Be(LocalLlmDefaults.Endpoint);

    /// <summary>OpenAI の既定エンドポイントを検証する</summary>
    [Fact(DisplayName = "OpenAI の既定エンドポイントは公式 API")]
    public void ResolveEndpoint_OpenAi_UsesOfficialApi() =>
        Connection(AiProvider.OpenAI).ResolveEndpoint().Should().Be("https://api.openai.com/v1");

    /// <summary>上書きが指定されていれば、プロバイダーに依らずその値を使うことを検証する</summary>
    [Fact(DisplayName = "上書き指定はプロバイダーに依らず優先される")]
    public void ResolveEndpoint_Override_TakesPrecedence() =>
        Connection(AiProvider.LocalLlm, endpointOverride: "http://127.0.0.1:1234/v1")
            .ResolveEndpoint()
            .Should()
            .Be("http://127.0.0.1:1234/v1");

    /// <summary>キー未入力なら、認証不要サーバー向けのダミーへ置き換わることを検証する（挙動不変の固定）</summary>
    [Fact(DisplayName = "キー未入力ならダミーキーを送る")]
    public void ResolveApiKey_Empty_FallsBackToPlaceholder() =>
        Connection(AiProvider.LocalLlm)
            .ResolveApiKey()
            .Should()
            .Be(LocalLlmDefaults.PlaceholderApiKey);

    /// <summary>キー入力があれば、そのキーがそのまま送られることを検証する（認証を課すローカルサーバー向け）</summary>
    [Fact(DisplayName = "キー入力ありならその値をそのまま送る")]
    public void ResolveApiKey_Provided_IsSentAsIs() =>
        Connection(AiProvider.LocalLlm, apiKey: "local-secret")
            .ResolveApiKey()
            .Should()
            .Be("local-secret");
}
