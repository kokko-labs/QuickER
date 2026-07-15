namespace QuickER.AI;

/// <summary>Codex App Server の起動設定（<see cref="AiSettings.CodexAppServer"/> セクションとして保持する）</summary>
public class CodexAppServerSettings
{
    /// <summary>使用するモデルプロバイダー（例: ollama-launch, openai）空の場合は codex の既定を使う</summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>使用するモデル名（例: gemma4:31b-cloud）空の場合は codex の既定を使う</summary>
    public string Model { get; set; } = string.Empty;
}
