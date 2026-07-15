namespace QuickER.AI;

/// <summary>Claude Code 接続の設定（<see cref="AiSettings.ClaudeCode"/> セクションとして保持する）</summary>
public class ClaudeCodeSettings
{
    /// <summary>使用するモデルエイリアス（例: sonnet, opus）。空なら Claude Code の既定を使う</summary>
    public string Model { get; set; } = string.Empty;
}
