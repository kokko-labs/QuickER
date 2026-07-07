using QuickER.Settings;

namespace QuickER.AI;

/// <summary>Claude Code 接続の設定</summary>
public class ClaudeCodeSettings
{
    /// <summary>使用するモデルエイリアス（例: sonnet, opus）。空なら Claude Code の既定を使う</summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>Claude Code 接続の設定を JSON ファイルへ保存・読込するストア</summary>
public class ClaudeCodeSettingsStore : JsonSettingsStore<ClaudeCodeSettings>
{
    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    public ClaudeCodeSettingsStore()
        : base("claude-code.json") { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public ClaudeCodeSettingsStore(string folder)
        : base("claude-code.json", folder) { }
}
