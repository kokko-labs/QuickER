namespace ERDesigner.Services;

/// <summary>AI 機能で共通利用するモデル名候補のカタログ</summary>
public static class AiModelCatalog
{
    /// <summary>OpenAI の既定モデル名</summary>
    public const string DefaultOpenAiModel = "gpt-5.4-mini";

    /// <summary>OpenAI の候補モデル一覧</summary>
    public static readonly IReadOnlyList<string> OpenAiModels = [DefaultOpenAiModel, "gpt-5.4", "gpt-5-nano", "gpt-5-mini", "gpt-5.5"];

    /// <summary>Ollama でよく使われる候補モデル一覧</summary>
    public static readonly IReadOnlyList<string> OllamaModels = ["gpt-oss:20b", "gemma4:12b", "qwen3.6:35b", "gemma4:31b-cloud", "minimax-m3:cloud", "nemotron-3-ultra:cloud"];
}
