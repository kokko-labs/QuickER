namespace QuickER.AI;

/// <summary>GitHub Copilot 接続の設定（<see cref="AiSettings.Copilot"/> セクションとして保持する）</summary>
public class CopilotSettings
{
    /// <summary>
    /// モデル MRU 履歴（<see cref="AiSettings.CopilotModelHistory"/>）で使うプロバイダーキー。
    /// Copilot はプロバイダーが 1 つなので固定キーで持つ（API キー接続の "openai" 等に相当）。
    /// </summary>
    public const string HistoryProviderKey = "copilot";

    /// <summary>
    /// 使用するモデル ID（例 <c>gpt-5</c>, <c>claude-sonnet-4.5</c>）。
    /// 空なら Copilot CLI の既定モデルに任せる（Claude Code 接続と同じ思想）。
    /// </summary>
    public string Model { get; set; } = string.Empty;
}
