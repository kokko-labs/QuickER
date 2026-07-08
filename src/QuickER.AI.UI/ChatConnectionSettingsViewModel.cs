using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.AI;
using QuickER.AI.UI.Resources;

namespace QuickER.AI.UI;

/// <summary>
/// AI チャット系ダイアログの「接続方式タブ（API キー接続 / Codex 接続 / Claude Code 接続）」の
/// 状態と永続化を束ねる共通 VM 部品。AiChatDialog / MockGenerationDialog の両方がコンポジションで保持する。
/// </summary>
/// <remarks>
/// <para>
/// 本 VM は純粋な「状態＋永続化」部品であり、親（AiChatDialogViewModel / MockGenerationDialogViewModel）が
/// 保持するエンジンやコマンドの可否（readiness）には一切依存しない。親 VM は本 VM の
/// <see cref="ObservableObject.PropertyChanged"/> を購読し、必要な readiness 再評価（NotifyReadinessChanged 等）
/// やエンジンのモデル同期を親側で行う規約とする。
/// </para>
/// <para>
/// コンストラクタでは設定を自動ロードしない（<see cref="LoadSettings"/> は親が PropertyChanged 購読を
/// 確立した後に呼ぶ）。購読前にロードするとエンジンのモデル同期が漏れるため、この順序は親が固定する。
/// </para>
/// <para>
/// テスト隔離のため、config.toml 読込・API キーの読み書きは delegate として注入できる。
/// 既定はいずれも現行の static 直呼びと同一挙動（本番挙動不変）。
/// </para>
/// </remarks>
public partial class ChatConnectionSettingsViewModel : ObservableObject
{
    /// <summary>OpenAI API キーの保存名</summary>
    private const string OpenAiApiKeyStoreName = "OpenAiApiKey";

    /// <summary>Anthropic (Claude) API キーの保存名</summary>
    private const string ClaudeApiKeyStoreName = "ClaudeApiKey";

    private const string OpenAiProviderName = "openai";

    private readonly CodexAppServerSettingsStore _codexSettingsStore;
    private readonly ClaudeCodeSettingsStore _claudeCodeSettingsStore;

    /// <summary>UI 状態（最後に使った接続タブ）の保存先</summary>
    private readonly ChatUiSettingsStore _uiSettingsStore;

    /// <summary>config.toml 読込 seam（テスト隔離用。既定は <see cref="CodexConfigTomlReader.Read()"/>）</summary>
    private readonly Func<CodexConfigToml> _codexConfigReader;

    /// <summary>API キー読込 seam（テスト隔離用。既定は <see cref="ApiKeyStore.Load(string)"/>）</summary>
    private readonly Func<string, string?> _apiKeyLoader;

    /// <summary>API キー保存 seam（テスト隔離用。既定は <see cref="ApiKeyStore.Save(string, string)"/>）</summary>
    private readonly Action<string, string> _apiKeySaver;

    /// <summary>初期化中は API キーの上書き保存を抑止するフラグ</summary>
    private bool _isInitializing;

    private CodexConfigToml _configToml = new();

    /// <summary>起動時に選択すべき接続方式（前回使ったタブ。保存が無ければ API キー）</summary>
    public ErChatBackendKind InitialBackend { get; private set; } = ErChatBackendKind.ApiKey;

    /// <summary>依存を注入して生成する</summary>
    /// <param name="uiSettingsFileName">UI 状態の保存ファイル名（例: <c>ai-chat-ui.json</c> / <c>mock-generation-ui.json</c>）</param>
    /// <param name="codexSettingsStore">Codex 設定ストア（省略時は既定の保存先）</param>
    /// <param name="uiSettingsStore">UI 状態ストア（省略時は <paramref name="uiSettingsFileName"/> で新規生成）</param>
    /// <param name="claudeCodeSettingsStore">Claude Code 設定ストア（省略時は既定の保存先）</param>
    /// <param name="codexConfigReader">config.toml 読込 seam（省略時は既定パス読込）</param>
    /// <param name="apiKeyLoader">API キー読込 seam（省略時は <see cref="ApiKeyStore.Load(string)"/>）</param>
    /// <param name="apiKeySaver">API キー保存 seam（省略時は <see cref="ApiKeyStore.Save(string, string)"/>）</param>
    public ChatConnectionSettingsViewModel(
        string uiSettingsFileName,
        CodexAppServerSettingsStore? codexSettingsStore = null,
        ChatUiSettingsStore? uiSettingsStore = null,
        ClaudeCodeSettingsStore? claudeCodeSettingsStore = null,
        Func<CodexConfigToml>? codexConfigReader = null,
        Func<string, string?>? apiKeyLoader = null,
        Action<string, string>? apiKeySaver = null
    )
    {
        _codexSettingsStore = codexSettingsStore ?? new CodexAppServerSettingsStore();
        _uiSettingsStore = uiSettingsStore ?? new ChatUiSettingsStore(uiSettingsFileName);
        _claudeCodeSettingsStore = claudeCodeSettingsStore ?? new ClaudeCodeSettingsStore();
        _codexConfigReader = codexConfigReader ?? CodexConfigTomlReader.Read;
        _apiKeyLoader = apiKeyLoader ?? ApiKeyStore.Load;
        _apiKeySaver = apiKeySaver ?? ApiKeyStore.Save;
    }

    // ── 接続方式の選択 ──

    [ObservableProperty]
    private ErChatBackendKind _selectedBackend = ErChatBackendKind.ApiKey;

    /// <summary>API キー接続タブが選択されているか</summary>
    public bool IsApiKeyBackend => SelectedBackend == ErChatBackendKind.ApiKey;

    /// <summary>Codex 接続タブが選択されているか</summary>
    public bool IsCodexBackend => SelectedBackend == ErChatBackendKind.Codex;

    /// <summary>Claude Code 接続タブが選択されているか</summary>
    public bool IsClaudeCodeBackend => SelectedBackend == ErChatBackendKind.ClaudeCode;

    // ── API キー接続タブ ──

    [ObservableProperty]
    private AiProvider _apiProvider = AiProvider.OpenAI;

    [ObservableProperty]
    private string _apiModel = AiModelCatalog.DefaultOpenAiModel;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _saveApiKey = true;

    [ObservableProperty]
    private string _endpointOverride = string.Empty;

    /// <summary>API キー接続で利用可能なプロバイダー一覧</summary>
    public IReadOnlyList<AiProvider> ApiProviders { get; } =
    [AiProvider.OpenAI, AiProvider.Claude, AiProvider.Ollama];

    /// <summary>現在の API プロバイダーに応じたモデル候補</summary>
    public IReadOnlyList<string> ApiModelCandidates =>
        ApiProvider switch
        {
            AiProvider.Ollama => AiModelCatalog.OllamaModels,
            AiProvider.Claude => AiModelCatalog.ClaudeModels,
            _ => AiModelCatalog.OpenAiModels,
        };

    /// <summary>API キー欄を表示するか（API キーが必要な OpenAI / Claude 選択時のみ）</summary>
    public bool ShowApiKey => ApiProvider is AiProvider.OpenAI or AiProvider.Claude;

    /// <summary>エンドポイント欄を表示するか（Ollama 選択時のみ）</summary>
    public bool ShowEndpoint => ApiProvider == AiProvider.Ollama;

    // ── Codex 接続タブ ──

    /// <summary>Codex モデルプロバイダー候補（openai + config.toml）</summary>
    public ObservableCollection<string> CodexModelProviderCandidates { get; } = new();

    /// <summary>Codex モデル候補</summary>
    public ObservableCollection<string> CodexModelCandidates { get; } = new();

    [ObservableProperty]
    private string _codexModelProvider = OpenAiProviderName;

    [ObservableProperty]
    private string _codexModel = AiModelCatalog.DefaultOpenAiModel;

    // ── Claude Code 接続タブ（状態の置き場のみ。更新経路は親の責務） ──

    [ObservableProperty]
    private string _claudeCodeModel = AiModelCatalog.DefaultClaudeCodeModel;

    [ObservableProperty]
    private string _claudeCodeStatusSummary = Strings.Connection_ClaudeCodeStatusUnverified;

    [ObservableProperty]
    private ConnectionHealth _claudeCodeStatusLevel = ConnectionHealth.Pending;

    [ObservableProperty]
    private string _claudeCodeGuidance = Strings.Connection_ClaudeCodeGuidanceDefault;

    /// <summary>Claude Code のモデル候補（エイリアス）</summary>
    public IReadOnlyList<string> ClaudeCodeModelCandidates { get; } =
        AiModelCatalog.ClaudeCodeModels;

    /// <summary>現在のプロバイダーに対応する API キー保存名（API キー不要のプロバイダーは null）</summary>
    private string? CurrentApiKeyStoreName =>
        ApiProvider switch
        {
            AiProvider.OpenAI => OpenAiApiKeyStoreName,
            AiProvider.Claude => ClaudeApiKeyStoreName,
            _ => null,
        };

    /// <summary>
    /// ダイアログ表示時に呼ぶ初期化。現在のプロバイダーの API キーを読み直す
    /// （読み込みで <see cref="OnApiKeyChanged"/> が走っても上書き保存しないよう抑止する）。
    /// </summary>
    /// <remarks>親は「Connection 生成 → PropertyChanged 購読 → LoadSettings → Initialize」の順で呼ぶ。</remarks>
    public void Initialize()
    {
        _isInitializing = true;
        ApiKey = CurrentApiKeyStoreName is { } slot
            ? _apiKeyLoader(slot) ?? string.Empty
            : string.Empty;
        _isInitializing = false;
    }

    /// <summary>保存済み設定と config.toml の候補を読み込む（親が PropertyChanged 購読確立後に呼ぶ）</summary>
    public void LoadSettings()
    {
        var settings = _codexSettingsStore.Load();
        LoadCodexModelCandidates();
        CodexModelProvider = string.IsNullOrWhiteSpace(settings.ModelProvider)
            ? OpenAiProviderName
            : settings.ModelProvider;
        CodexModel = string.IsNullOrWhiteSpace(settings.Model)
            ? AiModelCatalog.DefaultOpenAiModel
            : settings.Model;

        ClaudeCodeModel = _claudeCodeSettingsStore.Load().Model;
        InitialBackend = _uiSettingsStore.Load().ParseLastBackend() ?? ErChatBackendKind.ApiKey;
    }

    /// <summary>接続タブ関連の設定を保存する（親の SaveSettings から呼ぶ）</summary>
    public void SaveSettings()
    {
        _codexSettingsStore.Save(
            new CodexAppServerSettings
            {
                ModelProvider = CodexModelProvider?.Trim() ?? string.Empty,
                Model = CodexModel?.Trim() ?? string.Empty,
            }
        );

        _claudeCodeSettingsStore.Save(
            new ClaudeCodeSettings { Model = ClaudeCodeModel?.Trim() ?? string.Empty }
        );

        _uiSettingsStore.Save(new ChatUiSettings { LastBackend = SelectedBackend.ToString() });
    }

    /// <summary>config.toml から Codex のプロバイダー・モデル候補を読み込む</summary>
    private void LoadCodexModelCandidates()
    {
        _configToml = _codexConfigReader();
        CodexModelProviderCandidates.Clear();
        CodexModelProviderCandidates.Add(OpenAiProviderName);

        foreach (var name in _configToml.ProviderNames)
        {
            if (!CodexModelProviderCandidates.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                CodexModelProviderCandidates.Add(name);
            }
        }

        RefreshCodexModelCandidates();
    }

    /// <summary>現在の Codex プロバイダーに応じてモデル候補を更新する</summary>
    private void RefreshCodexModelCandidates()
    {
        CodexModelCandidates.Clear();
        var isOpenAi =
            string.IsNullOrWhiteSpace(CodexModelProvider)
            || CodexModelProvider
                .Trim()
                .Equals(OpenAiProviderName, StringComparison.OrdinalIgnoreCase);

        if (isOpenAi)
        {
            foreach (var m in AiModelCatalog.OpenAiModels)
            {
                CodexModelCandidates.Add(m);
            }
        }
        else if (_configToml.ProviderModels.TryGetValue(CodexModelProvider.Trim(), out var models))
        {
            foreach (var m in models)
            {
                CodexModelCandidates.Add(m);
            }
        }
        else if (!string.IsNullOrWhiteSpace(_configToml.Model))
        {
            CodexModelCandidates.Add(_configToml.Model);
        }
    }

    // ── 設定変更フック ──

    partial void OnSelectedBackendChanged(ErChatBackendKind value)
    {
        // Is* 3 通知（エンジン差し替え・readiness 再評価は親が PropertyChanged 購読で行う）
        OnPropertyChanged(nameof(IsApiKeyBackend));
        OnPropertyChanged(nameof(IsCodexBackend));
        OnPropertyChanged(nameof(IsClaudeCodeBackend));
    }

    partial void OnApiProviderChanged(AiProvider value)
    {
        OnPropertyChanged(nameof(ApiModelCandidates));
        OnPropertyChanged(nameof(ShowApiKey));
        OnPropertyChanged(nameof(ShowEndpoint));
        ApiModel = ApiModelCandidates[0];

        if (value == AiProvider.Ollama && string.IsNullOrWhiteSpace(EndpointOverride))
        {
            EndpointOverride = "http://localhost:11434/v1";
        }

        // プロバイダーごとに別の API キーを保持するため、切替時に保存済みキーを読み直す
        // （読み込みで OnApiKeyChanged が走っても上書き保存しないよう _isInitializing で抑止する）
        var wasInitializing = _isInitializing;
        _isInitializing = true;
        ApiKey = CurrentApiKeyStoreName is { } slot
            ? _apiKeyLoader(slot) ?? string.Empty
            : string.Empty;
        _isInitializing = wasInitializing;
    }

    partial void OnApiKeyChanged(string value) => PersistApiKey();

    partial void OnSaveApiKeyChanged(bool value) => PersistApiKey();

    partial void OnCodexModelProviderChanged(string value)
    {
        // Codex エンジンへのモデル同期は親の責務。子は候補更新のみ行う。
        RefreshCodexModelCandidates();
    }

    /// <summary>保存設定に従い、現在のプロバイダーの API キーを永続化する（キー不要のプロバイダーは何もしない）</summary>
    private void PersistApiKey()
    {
        if (_isInitializing)
        {
            return;
        }

        if (CurrentApiKeyStoreName is { } slot)
        {
            _apiKeySaver(slot, SaveApiKey ? ApiKey : string.Empty);
        }
    }
}
