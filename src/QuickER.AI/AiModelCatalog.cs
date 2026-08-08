namespace QuickER.AI;

/// <summary>AI 機能で共通利用するモデル名候補のカタログ</summary>
public static class AiModelCatalog
{
    /// <summary>OpenAI の既定モデル名（GPT-5.6 ファミリーの中位＝知能とコストのバランス型）</summary>
    public const string DefaultOpenAiModel = "gpt-5.6-terra";

    /// <summary>
    /// OpenAI の候補モデル一覧（2026-08 時点の GPT-5.6 ファミリー。sol=最上位・terra=バランス・
    /// luna=低コスト大量処理向けで、上位から順に並べる）。
    /// **並び順は表示順であって既定ではない**＝既定は <see cref="DefaultOpenAiModel"/> が正本で、
    /// プロバイダー切替時もそちらが選ばれる。旧世代は API で引き続き利用可能なので、
    /// 必要なら手入力すれば MRU 履歴として候補に残る。
    /// </summary>
    public static readonly IReadOnlyList<string> OpenAiModels =
    [
        "gpt-5.6-sol",
        "gpt-5.6-terra", // = DefaultOpenAiModel
        "gpt-5.6-luna",
    ];

    /// <summary>Anthropic (Claude) の既定モデル名（速度と知能のバランス型）</summary>
    public const string DefaultClaudeModel = "claude-sonnet-5";

    /// <summary>
    /// Anthropic (Claude) の候補モデル一覧（2026-08 時点。fable-5=最上位・opus-5=エージェンティック
    /// コーディング特化・sonnet-5=バランス・haiku-4-5=最速で、上位から順に並べる）。
    /// 並び順と既定の関係・旧世代の扱いは <see cref="OpenAiModels"/> と同じ。
    /// </summary>
    public static readonly IReadOnlyList<string> ClaudeModels =
    [
        "claude-fable-5",
        "claude-opus-5",
        "claude-sonnet-5", // = DefaultClaudeModel
        "claude-haiku-4-5",
    ];

    /// <summary>Claude Code（CLI）の既定モデル（空＝Claude Code の既定に従う）</summary>
    public const string DefaultClaudeCodeModel = "";

    /// <summary>
    /// Claude Code（CLI）の候補モデル。ここだけは API のモデル ID ではなく CLI の
    /// <c>--model</c> エイリアスで、上位から順に並べる（<see cref="ClaudeModels"/> と同じ流儀）。
    /// 先頭の空文字だけは表示順ではなく既定＝<see cref="DefaultClaudeCodeModel"/> の意味で、
    /// 選ぶと <c>--model</c> を渡さず Claude Code 側の既定に従う。
    /// エイリアスは常に「その系列の最新」を指すため、世代が上がっても書き換え不要
    /// （特定世代へ固定したいときは <c>claude-fable-5</c> のようなフルネームを手入力する）。
    /// </summary>
    public static readonly IReadOnlyList<string> ClaudeCodeModels =
    [
        DefaultClaudeCodeModel,
        "fable",
        "opus",
        "sonnet",
        "haiku",
    ];
}
