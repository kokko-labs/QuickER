using QuickER.Settings;

namespace QuickER.AI;

/// <summary>Codex App Server の起動設定</summary>
public class CodexAppServerSettings
{
    /// <summary>使用するモデルプロバイダー（例: ollama-launch, openai）空の場合は codex の既定を使う</summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>使用するモデル名（例: gemma4:31b-cloud）空の場合は codex の既定を使う</summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>Codex App Server の設定を JSON ファイルへ保存・読込するストア</summary>
public class CodexAppServerSettingsStore : JsonSettingsStore<CodexAppServerSettings>
{
    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    public CodexAppServerSettingsStore()
        : base("codex-app-server.json") { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public CodexAppServerSettingsStore(string folder)
        : base("codex-app-server.json", folder) { }
}
