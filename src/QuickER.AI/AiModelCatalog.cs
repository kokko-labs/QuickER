namespace QuickER.AI;

/// <summary>AI 機能で共通利用するモデル名候補のカタログ</summary>
public static class AiModelCatalog
{
    /// <summary>OpenAI の既定モデル名（GPT-5.6 ファミリーの中位＝知能とコストのバランス型）</summary>
    public const string DefaultOpenAiModel = "gpt-5.6-terra";

    /// <summary>
    /// OpenAI の候補モデル一覧（2026-07 時点の GPT-5.6 ファミリー。sol=最上位・terra=バランス・
    /// luna=低コスト大量処理向け。旧世代 2 つは API で引き続き利用可能なため互換用に残す）
    /// </summary>
    public static readonly IReadOnlyList<string> OpenAiModels =
    [
        DefaultOpenAiModel,
        "gpt-5.6-sol",
        "gpt-5.6-luna",
        "gpt-5.5",
        "gpt-5.4-mini",
    ];

    /// <summary>Anthropic (Claude) の既定モデル名（速度と知能のバランス型）</summary>
    public const string DefaultClaudeModel = "claude-sonnet-5";

    /// <summary>
    /// Anthropic (Claude) の候補モデル一覧（2026-07 時点。fable-5=最上位・opus-4-8=エージェンティック
    /// コーディング特化・sonnet-5=バランス・haiku-4-5=最速。旧世代 2 つは API で引き続き利用可能なため互換用に残す）
    /// </summary>
    public static readonly IReadOnlyList<string> ClaudeModels =
    [
        DefaultClaudeModel,
        "claude-fable-5",
        "claude-opus-4-8",
        "claude-haiku-4-5",
        "claude-sonnet-4-6",
        "claude-opus-4-7",
    ];

    /// <summary>Claude Code（CLI）の既定モデル（空＝Claude Code の既定に従う）</summary>
    public const string DefaultClaudeCodeModel = "";

    /// <summary>Claude Code（CLI）の候補モデル（エイリアス。空は「既定」）</summary>
    public static readonly IReadOnlyList<string> ClaudeCodeModels =
    [
        DefaultClaudeCodeModel,
        "sonnet",
        "opus",
        "haiku",
    ];
}
