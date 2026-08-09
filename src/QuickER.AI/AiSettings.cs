using QuickER.Settings;

namespace QuickER.AI;

/// <summary>
/// UI 状態をダイアログ別に持つ AI 系ダイアログの種別
/// （同一ファイルの別セクション <see cref="AiSettings.ChatUi"/> / <see cref="AiSettings.MockUi"/> を選ぶキー）。
/// </summary>
public enum AiDialogKind
{
    /// <summary>AI チャットダイアログ</summary>
    AiChat,

    /// <summary>モック生成ダイアログ</summary>
    MockGeneration,
}

/// <summary>AI 機能（チャット / モック生成）の設定・UI 状態・モデル履歴を 1 ファイルへ集約する設定ルート</summary>
public class AiSettings
{
    /// <summary>AI チャットダイアログの UI 状態（最後に使った接続タブなど）</summary>
    public ChatUiSettings ChatUi { get; set; } = new();

    /// <summary>モック生成ダイアログの UI 状態（最後に使った接続タブなど）</summary>
    public ChatUiSettings MockUi { get; set; } = new();

    /// <summary>Claude Code 接続の設定</summary>
    public ClaudeCodeSettings ClaudeCode { get; set; } = new();

    /// <summary>Codex App Server の起動設定</summary>
    public CodexAppServerSettings CodexAppServer { get; set; } = new();

    /// <summary>GitHub Copilot 接続の設定</summary>
    public CopilotSettings Copilot { get; set; } = new();

    /// <summary>API キー接続のモデル MRU 履歴（両ダイアログ共有）</summary>
    public ProviderModelHistory ApiModelHistory { get; set; } = new();

    /// <summary>Codex 接続のモデル MRU 履歴（両ダイアログ共有）</summary>
    public ProviderModelHistory CodexModelHistory { get; set; } = new();

    /// <summary>
    /// GitHub Copilot 接続のモデル MRU 履歴（両ダイアログ共有。キーは
    /// <see cref="CopilotSettings.HistoryProviderKey"/> 固定）。
    /// </summary>
    public ProviderModelHistory CopilotModelHistory { get; set; } = new();

    /// <summary>指定ダイアログの UI 状態セクションを取得する</summary>
    /// <param name="kind">対象ダイアログの種別</param>
    public ChatUiSettings UiFor(AiDialogKind kind) =>
        kind == AiDialogKind.MockGeneration ? MockUi : ChatUi;
}

/// <summary>
/// <see cref="AiSettings"/> を JSON ファイル（%APPDATA%\QuickER\ai-settings.json）へ保存・読込するストア。
/// AI チャット／AI モックの両ダイアログが同一ファイルを共有し、各自のセクションだけを読み書きする。
/// </summary>
public class AiSettingsStore : JsonSettingsStore<AiSettings>
{
    /// <summary>既定の保存ファイル名（両ダイアログ共有）</summary>
    public const string DefaultFileName = "ai-settings.json";

    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    public AiSettingsStore()
        : base(DefaultFileName) { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    /// <param name="folder">保存先フォルダ</param>
    public AiSettingsStore(string folder)
        : base(DefaultFileName, folder) { }
}
