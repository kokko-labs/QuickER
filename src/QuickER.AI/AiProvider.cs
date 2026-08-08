namespace QuickER.AI;

/// <summary>AI プロバイダの種別</summary>
public enum AiProvider
{
    /// <summary>OpenAI 公式 API (api.openai.com)</summary>
    OpenAI,

    /// <summary>Anthropic Claude 公式 API (api.anthropic.com)</summary>
    Claude,

    /// <summary>OpenAI 互換 API のローカル LLM (Ollama / LM Studio / llama.cpp / vLLM 等)</summary>
    LocalLlm,
}

/// <summary>
/// ローカル LLM プロバイダ (<see cref="AiProvider.LocalLlm"/>) の既定値。
/// UI・ドライバ・設定の 3 箇所で同じ値を使うため、ここを唯一の正本とする。
/// </summary>
public static class LocalLlmDefaults
{
    /// <summary>既定のエンドポイント URL (Ollama の既定ポート。他の実装は UI のエンドポイント欄で上書きする)</summary>
    public const string Endpoint = "http://localhost:11434/v1";

    /// <summary>
    /// API キー未入力時に送る代替キー。認証を要求しないローカルサーバーでも
    /// OpenAI SDK が空キーを拒否するため、無害なダミー文字列を送る
    /// (値は Ollama 時代からの互換のため変更しない。受け側は認証不要なので内容に意味はない)。
    /// </summary>
    public const string PlaceholderApiKey = "ollama";
}
