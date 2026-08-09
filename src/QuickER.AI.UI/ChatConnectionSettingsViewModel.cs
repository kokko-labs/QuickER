using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI;
using QuickER.AI.UI.Resources;

namespace QuickER.AI.UI;

/// <summary>
/// AI チャット系ダイアログの「接続方式タブ（API キー接続 / Codex 接続 / Claude Code 接続 / Copilot 接続）」の
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

    /// <summary>ローカル LLM API キーの保存名（認証を課すローカルサーバー向け。空なら保存されない）</summary>
    private const string LocalLlmApiKeyStoreName = "LocalLlmApiKey";

    private const string OpenAiProviderName = "openai";

    /// <summary>このダイアログの種別（UI 状態セクション ChatUi / MockUi を選ぶキー）</summary>
    private readonly AiDialogKind _dialogKind;

    /// <summary>
    /// AI 設定（Codex / Claude Code / UI 状態 / モデル履歴）を 1 ファイルへ集約するストア。
    /// 両ダイアログが共有するため、保存は必ず「Load → 該当セクションのみ変更 → Save」で行う。
    /// </summary>
    private readonly AiSettingsStore _settingsStore;

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
    /// <param name="dialogKind">このダイアログの種別（UI 状態セクション ChatUi / MockUi を選ぶキー）</param>
    /// <param name="settingsStore">AI 設定ストア（省略時は既定の保存先＝<c>ai-settings.json</c>・両ダイアログ共有）</param>
    /// <param name="codexConfigReader">config.toml 読込 seam（省略時は既定パス読込）</param>
    /// <param name="apiKeyLoader">API キー読込 seam（省略時は <see cref="ApiKeyStore.Load(string)"/>）</param>
    /// <param name="apiKeySaver">API キー保存 seam（省略時は <see cref="ApiKeyStore.Save(string, string)"/>）</param>
    public ChatConnectionSettingsViewModel(
        AiDialogKind dialogKind,
        AiSettingsStore? settingsStore = null,
        Func<CodexConfigToml>? codexConfigReader = null,
        Func<string, string?>? apiKeyLoader = null,
        Action<string, string>? apiKeySaver = null
    )
    {
        _dialogKind = dialogKind;
        _settingsStore = settingsStore ?? new AiSettingsStore();
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

    /// <summary>GitHub Copilot 接続タブが選択されているか</summary>
    public bool IsCopilotBackend => SelectedBackend == ErChatBackendKind.Copilot;

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
    [AiProvider.OpenAI, AiProvider.Claude, AiProvider.LocalLlm];

    /// <summary>
    /// 現在の API プロバイダーに応じたモデル候補。OpenAI / Claude は静的カタログ（削除不可）を上に固定し、
    /// その下へカタログ外の手入力モデルの MRU 履歴（× で削除可能）を並べる。ローカル LLM は
    /// 手元のサーバーが持つモデル次第でカタログを持てないため履歴のみ。
    /// 履歴はチャットのターン成功時に <see cref="RecordSuccessfulModel"/> で追加される。
    /// </summary>
    public ObservableCollection<ModelCandidate> ApiModelCandidates { get; } = new();

    /// <summary>現在のプロバイダーがローカル LLM か（キー任意・エンドポイント欄の表示条件）</summary>
    public bool IsLocalLlmProvider => ApiProvider == AiProvider.LocalLlm;

    /// <summary>
    /// API キー欄を表示するか。全プロバイダーで表示する（ローカル LLM も認証を課すサーバーがあるためキーを受け付ける）。
    /// 将来プロバイダーごとに出し分ける可能性を残すため、XAML の可視性バインド先としてプロパティのまま置く。
    /// </summary>
    public bool ShowApiKey => true;

    /// <summary>エンドポイント欄を表示するか（ローカル LLM 選択時のみ）</summary>
    public bool ShowEndpoint => IsLocalLlmProvider;

    /// <summary>
    /// 実際に接続へ渡すエンドポイント上書き値。ローカル LLM 以外では常に null を返す。
    /// エンドポイント欄はローカル LLM のときだけ表示されるため、値を入れたまま OpenAI へ切り替えると
    /// 見えない欄の値が OpenAI 接続へ紛れ込む。その事故を防ぐ規則をここに一本化する
    /// （チャット／モックの両ダイアログが本プロパティを介して接続を組み立てる）。
    /// </summary>
    public string? EffectiveEndpointOverride =>
        IsLocalLlmProvider && !string.IsNullOrWhiteSpace(EndpointOverride)
            ? EndpointOverride
            : null;

    // ── Codex 接続タブ ──

    /// <summary>Codex モデルプロバイダー候補（openai + config.toml）</summary>
    public ObservableCollection<string> CodexModelProviderCandidates { get; } = new();

    /// <summary>
    /// Codex モデル候補。openai は静的カタログ（削除不可）、非 openai は MRU 履歴（× で削除可能）のみ。
    /// </summary>
    public ObservableCollection<ModelCandidate> CodexModelCandidates { get; } = new();

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

    // ── GitHub Copilot 接続タブ（状態の置き場のみ。更新経路は親の責務） ──

    /// <summary>使用するモデル ID（空なら Copilot CLI の既定モデルに任せる＝既定値）</summary>
    [ObservableProperty]
    private string _copilotModel = string.Empty;

    [ObservableProperty]
    private string _copilotStatusSummary = Strings.Connection_CopilotStatusUnverified;

    [ObservableProperty]
    private ConnectionHealth _copilotStatusLevel = ConnectionHealth.Pending;

    [ObservableProperty]
    private string _copilotGuidance = Strings.Connection_CopilotGuidanceDefault;

    private IReadOnlyList<string> _copilotAvailableModels = [];

    /// <summary>
    /// 接続確立後にエンジンが実行時列挙したモデル ID の一覧（親が
    /// <see cref="CopilotChatEngine.AvailableModels"/> から流し込む）。
    /// Copilot は静的カタログを持たないため、これが候補の上段（削除不可）になる。
    /// </summary>
    public IReadOnlyList<string> CopilotAvailableModels
    {
        get => _copilotAvailableModels;
        set
        {
            _copilotAvailableModels = value ?? [];
            OnPropertyChanged();
            RefreshCopilotModelCandidates();
        }
    }

    /// <summary>
    /// Copilot モデル候補。実行時列挙（削除不可）を上に固定し、その下へ列挙に無いモデルの
    /// MRU 履歴（× で削除可能）を並べる。未接続でも直近使ったモデルを選べるようにするための 2 層構成。
    /// </summary>
    public ObservableCollection<ModelCandidate> CopilotModelCandidates { get; } = new();

    /// <summary>現在のプロバイダーに対応する API キー保存名（プロバイダーごとに別スロットで保持する）</summary>
    private string? CurrentApiKeyStoreName =>
        ApiProvider switch
        {
            AiProvider.OpenAI => OpenAiApiKeyStoreName,
            AiProvider.Claude => ClaudeApiKeyStoreName,
            AiProvider.LocalLlm => LocalLlmApiKeyStoreName,
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
        // 共有ファイルを 1 回だけ読み、Codex / Claude Code / UI 状態を各セクションから取り出す
        var settings = _settingsStore.Load();

        LoadCodexModelCandidates();
        CodexModelProvider = ResolveCodexModelProvider(settings.CodexAppServer.ModelProvider);
        CodexModel = string.IsNullOrWhiteSpace(settings.CodexAppServer.Model)
            ? AiModelCatalog.DefaultOpenAiModel
            : settings.CodexAppServer.Model;

        ClaudeCodeModel = settings.ClaudeCode.Model;
        CopilotModel = settings.Copilot.Model;
        RefreshCopilotModelCandidates();

        var ui = settings.UiFor(_dialogKind);
        InitialBackend = ui.ParseLastBackend() ?? ErChatBackendKind.ApiKey;

        // プロバイダー → エンドポイントの順で復元する。プロバイダー変更フックは
        // 「ローカル LLM かつエンドポイント空」なら既定 URL を補完するため、保存値がある場合だけ後から上書きする
        // （保存が無いローカル LLM は既定 URL・保存がある場合はその URL、という復元になる）
        ApiProvider = ui.ParseApiProvider() ?? AiProvider.OpenAI;

        if (!string.IsNullOrWhiteSpace(ui.EndpointOverride))
        {
            EndpointOverride = ui.EndpointOverride;
        }

        RefreshApiModelCandidates();
    }

    /// <summary>現在の API プロバイダーの履歴キー（<see cref="AiProvider"/> の小文字名）</summary>
    private string ApiProviderKey => ApiProvider.ToString().ToLowerInvariant();

    /// <summary>現在の API プロバイダーの静的カタログ（ローカル LLM はカタログ無し＝空）</summary>
    private IReadOnlyList<string> CurrentApiCatalog =>
        ApiProvider switch
        {
            AiProvider.Claude => AiModelCatalog.ClaudeModels,
            AiProvider.OpenAI => AiModelCatalog.OpenAiModels,
            _ => [],
        };

    /// <summary>
    /// 現在の API プロバイダーの既定モデル（カタログを持たないローカル LLM は空）。
    /// カタログの並び順は「上位モデルから」の表示順であって既定ではないため、
    /// 既定はカタログ先頭ではなく <see cref="AiModelCatalog"/> の Default* から解決する。
    /// </summary>
    private string CurrentApiDefaultModel =>
        ApiProvider switch
        {
            AiProvider.Claude => AiModelCatalog.DefaultClaudeModel,
            AiProvider.OpenAI => AiModelCatalog.DefaultOpenAiModel,
            _ => string.Empty,
        };

    /// <summary>
    /// 現在の API プロバイダーに応じてモデル候補を再構築する。静的カタログ（削除不可）を上に固定し、
    /// その下へカタログ外の手入力モデルの MRU 履歴（× で削除可能）を追加する
    /// （カタログと同名の履歴は表示しない。両ダイアログ共有ファイルの最新を反映するため都度 Load する）。
    /// </summary>
    private void RefreshApiModelCandidates()
    {
        // 候補の Clear で編集可能 ComboBox が選択中項目を失い、Text（＝双方向バインドの ApiModel）を
        // 空へ押し戻すことがあるため、再構築の前に退避して最後に復元する（成功ターン後の MRU 記録・
        // 履歴の × 削除のどの経路でも選択値を保つ。プロバイダー切替の既定モデルは本メソッドの後で
        // 明示代入されるため復元と両立する）
        var current = ApiModel;

        ApiModelCandidates.Clear();

        foreach (var m in CurrentApiCatalog)
        {
            ApiModelCandidates.Add(new ModelCandidate(m, IsRemovable: false));
        }

        foreach (var m in _settingsStore.Load().ApiModelHistory.ModelsFor(ApiProviderKey))
        {
            // カタログと同名（大文字小文字問わず）の履歴は表示しない（カタログ側を優先。ファイル上の履歴には残る）
            if (
                ApiModelCandidates.Any(c =>
                    string.Equals(c.Name, m, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                continue;
            }

            ApiModelCandidates.Add(new ModelCandidate(m, IsRemovable: true));
        }

        ApiModel = current;
    }

    /// <summary>
    /// チャットのターンが成功したときに、使用中のモデルを MRU 履歴へ記録する。
    /// ①API キー接続 → そのプロバイダーの静的カタログに無いモデルのみ API 履歴（プロバイダ別）、
    /// ②Codex 接続かつ非 openai プロバイダー → Codex 履歴（プロバイダ別）、
    /// ③Copilot 接続 → Copilot 履歴（固定キー）に記録する。
    /// どれにも当たらなければ何もしない
    /// （Claude Code バックエンド等の成功ターンで無条件に呼ばれても安全）。
    /// </summary>
    public void RecordSuccessfulModel()
    {
        if (SelectedBackend == ErChatBackendKind.Codex)
        {
            RecordSuccessfulCodexModel();
            return;
        }

        if (SelectedBackend == ErChatBackendKind.Copilot)
        {
            RecordSuccessfulCopilotModel();
            return;
        }

        if (SelectedBackend != ErChatBackendKind.ApiKey || string.IsNullOrWhiteSpace(ApiModel))
        {
            return;
        }

        var model = ApiModel.Trim();

        // カタログ在中モデルの使用は記録しない（上限 20 件/プロバイダをカスタムモデルのために温存する）
        if (CurrentApiCatalog.Contains(model, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        // 共有ファイルは両ダイアログで書き換え合うため、記録直前に最新を読み直し、
        // 該当セクションだけ変更して全体を書き戻す（他セクションを消さない）
        var settings = _settingsStore.Load();

        if (settings.ApiModelHistory.Touch(ApiProviderKey, model))
        {
            _settingsStore.Save(settings);
        }

        RefreshApiModelCandidates();
    }

    /// <summary>指定したモデルを現在の API プロバイダーの履歴から個別削除する（ドロップダウン項目の × ボタン）</summary>
    /// <param name="model">削除するモデル名（項目の Name）</param>
    [RelayCommand]
    private void RemoveApiModelHistory(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        var settings = _settingsStore.Load();

        if (settings.ApiModelHistory.Remove(ApiProviderKey, model))
        {
            _settingsStore.Save(settings);
        }

        // 選択中の項目を消しても Text（＝双方向バインドの ApiModel）が保たれる（再構築側が退避・復元する）
        RefreshApiModelCandidates();
    }

    /// <summary>
    /// Codex 接続の成功ターンで使用モデルをプロバイダ別 MRU 履歴へ記録する
    /// （openai プロバイダーは静的カタログのため記録しない）。
    /// </summary>
    private void RecordSuccessfulCodexModel()
    {
        if (IsOpenAiCodexProvider || string.IsNullOrWhiteSpace(CodexModel))
        {
            return;
        }

        // 共有ファイルは両ダイアログで書き換え合うため、記録直前に最新を読み直し、
        // 該当セクションだけ変更して全体を書き戻す（他セクションを消さない）
        var settings = _settingsStore.Load();

        if (settings.CodexModelHistory.Touch(CodexModelProvider.Trim(), CodexModel.Trim()))
        {
            _settingsStore.Save(settings);
        }

        RefreshCodexModelCandidates();
    }

    /// <summary>指定した Codex モデルを現在プロバイダーの履歴から個別削除する（ドロップダウン項目の × ボタン）</summary>
    /// <param name="model">削除するモデル名（項目の Name）</param>
    [RelayCommand]
    private void RemoveCodexModelHistory(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        var settings = _settingsStore.Load();

        if (settings.CodexModelHistory.Remove(CodexModelProvider?.Trim() ?? string.Empty, model))
        {
            _settingsStore.Save(settings);
        }

        // 選択中の項目を消しても Text（＝双方向バインドの CodexModel）が保たれる（再構築側が退避・復元する）
        RefreshCodexModelCandidates();
    }

    /// <summary>
    /// Copilot 接続の成功ターンで使用モデルを MRU 履歴（固定キー）へ記録する。
    /// Copilot は静的カタログを持たず候補が接続後にしか判らないため、実行時列挙に載っているモデルも
    /// 区別せず記録する（未接続時に直近使ったモデルを候補として出せるようにするため）。
    /// </summary>
    private void RecordSuccessfulCopilotModel()
    {
        // 空＝CLI 既定モデルに任せる指定なので、記録すべきモデル名が無い
        if (string.IsNullOrWhiteSpace(CopilotModel))
        {
            return;
        }

        // 共有ファイルは両ダイアログで書き換え合うため、記録直前に最新を読み直し、
        // 該当セクションだけ変更して全体を書き戻す（他セクションを消さない）
        var settings = _settingsStore.Load();

        if (
            settings.CopilotModelHistory.Touch(
                CopilotSettings.HistoryProviderKey,
                CopilotModel.Trim()
            )
        )
        {
            _settingsStore.Save(settings);
        }

        RefreshCopilotModelCandidates();
    }

    /// <summary>指定した Copilot モデルを履歴から個別削除する（ドロップダウン項目の × ボタン）</summary>
    /// <param name="model">削除するモデル名（項目の Name）</param>
    [RelayCommand]
    private void RemoveCopilotModelHistory(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        var settings = _settingsStore.Load();

        if (settings.CopilotModelHistory.Remove(CopilotSettings.HistoryProviderKey, model))
        {
            _settingsStore.Save(settings);
        }

        // 選択中の項目を消しても Text（＝双方向バインドの CopilotModel）が保たれる（再構築側が退避・復元する）
        RefreshCopilotModelCandidates();
    }

    /// <summary>
    /// Copilot のモデル候補を再構築する。実行時列挙（削除不可）を上に固定し、
    /// その下へ列挙に無いモデルの MRU 履歴（× で削除可能）を追加する
    /// （両ダイアログ共有ファイルの最新を反映するため都度 Load する）。
    /// </summary>
    private void RefreshCopilotModelCandidates()
    {
        // 候補の Clear で編集可能 ComboBox が選択中項目を失い、Text（＝双方向バインドの CopilotModel）を
        // 空へ押し戻すことがあるため、再構築の前に退避して最後に復元する。Copilot は成功ターンごとに
        // MRU 記録→再構築が走る（静的カタログが無く常に記録する）ため、復元しないと
        // 「チャットするたびに選択モデルが空へ戻る」ことになる
        var current = CopilotModel;

        CopilotModelCandidates.Clear();

        foreach (var m in CopilotAvailableModels)
        {
            CopilotModelCandidates.Add(new ModelCandidate(m, IsRemovable: false));
        }

        foreach (
            var m in _settingsStore
                .Load()
                .CopilotModelHistory.ModelsFor(CopilotSettings.HistoryProviderKey)
        )
        {
            // 実行時列挙と同名（大文字小文字問わず）の履歴は表示しない（列挙側を優先。ファイル上の履歴には残る）
            if (
                CopilotModelCandidates.Any(c =>
                    string.Equals(c.Name, m, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                continue;
            }

            CopilotModelCandidates.Add(new ModelCandidate(m, IsRemovable: true));
        }

        CopilotModel = current;
    }

    /// <summary>接続タブ関連の設定を保存する（親の SaveSettings から呼ぶ）</summary>
    public void SaveSettings()
    {
        // 両ダイアログが共有する 1 ファイルを Load → 該当セクションのみ変更 → Save で書き戻す
        // （古いスナップショットの丸ごと書き戻しで他ダイアログのセクションを消さない）
        var settings = _settingsStore.Load();

        settings.CodexAppServer = new CodexAppServerSettings
        {
            ModelProvider = CodexModelProvider?.Trim() ?? string.Empty,
            Model = CodexModel?.Trim() ?? string.Empty,
        };

        settings.ClaudeCode = new ClaudeCodeSettings
        {
            Model = ClaudeCodeModel?.Trim() ?? string.Empty,
        };

        settings.Copilot = new CopilotSettings { Model = CopilotModel?.Trim() ?? string.Empty };

        var ui = settings.UiFor(_dialogKind);
        ui.LastBackend = SelectedBackend.ToString();

        // API キー接続の選択（プロバイダー・エンドポイント）も次回起動へ引き継ぐ。
        // API キー本体はここ（平文 JSON）へは入れず、従来どおり DPAPI ストアだけが持つ
        ui.ApiProvider = ApiProvider.ToString();
        ui.EndpointOverride = EndpointOverride?.Trim() ?? string.Empty;

        _settingsStore.Save(settings);
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

    /// <summary>
    /// 保存済みのプロバイダー名を候補リストへ解決する（大文字小文字を問わず一致した候補の表記を採用）。
    /// プロバイダーはリスト選択のみ（自由入力不可）のため、候補に無い値（config.toml から消えた等）は
    /// openai へフォールバックする。
    /// </summary>
    /// <param name="saved">保存済みのプロバイダー名</param>
    private string ResolveCodexModelProvider(string? saved) =>
        CodexModelProviderCandidates.FirstOrDefault(p =>
            string.Equals(p, saved?.Trim(), StringComparison.OrdinalIgnoreCase)
        ) ?? OpenAiProviderName;

    /// <summary>現在の Codex プロバイダーが openai（既定）かどうか</summary>
    private bool IsOpenAiCodexProvider =>
        string.IsNullOrWhiteSpace(CodexModelProvider)
        || CodexModelProvider.Trim().Equals(OpenAiProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 現在の Codex プロバイダーに応じてモデル候補を更新する。
    /// openai は静的カタログのみ（従来同等・削除不可）。非 openai は MRU 履歴（× で削除可能）のみで、
    /// 初期状態は空・チャットのターン成功時に使用モデルが追加される。
    /// </summary>
    private void RefreshCodexModelCandidates()
    {
        // 候補の Clear で編集可能 ComboBox が選択中項目を失い、Text（＝双方向バインドの CodexModel）を
        // 空へ押し戻すことがあるため、再構築の前に退避して最後に復元する（成功ターン後の MRU 記録・
        // 履歴の × 削除・プロバイダー切替のどの経路でも選択値を保つ。手入力の自由テキストは従来から
        // 切替後も残るため、リスト選択だけ消える非対称の解消でもある）
        var current = CodexModel;

        try
        {
            CodexModelCandidates.Clear();

            if (IsOpenAiCodexProvider)
            {
                foreach (var m in AiModelCatalog.OpenAiModels)
                {
                    CodexModelCandidates.Add(new ModelCandidate(m, IsRemovable: false));
                }

                return;
            }

            // 非 openai: MRU 履歴のみ。両ダイアログ共有ファイルの最新を反映するため都度 Load する
            foreach (var m in _settingsStore.Load().CodexModelHistory.ModelsFor(CodexModelProvider))
            {
                CodexModelCandidates.Add(new ModelCandidate(m, IsRemovable: true));
            }
        }
        finally
        {
            // openai の早期 return を含む全経路で復元する
            CodexModel = current;
        }
    }

    // ── 設定変更フック ──

    partial void OnSelectedBackendChanged(ErChatBackendKind value)
    {
        // Is* 4 通知（エンジン差し替え・readiness 再評価は親が PropertyChanged 購読で行う）
        OnPropertyChanged(nameof(IsApiKeyBackend));
        OnPropertyChanged(nameof(IsCodexBackend));
        OnPropertyChanged(nameof(IsClaudeCodeBackend));
        OnPropertyChanged(nameof(IsCopilotBackend));
    }

    partial void OnApiProviderChanged(AiProvider value)
    {
        OnPropertyChanged(nameof(IsLocalLlmProvider));
        OnPropertyChanged(nameof(ShowApiKey));
        OnPropertyChanged(nameof(ShowEndpoint));
        OnPropertyChanged(nameof(EffectiveEndpointOverride));

        RefreshApiModelCandidates();

        // OpenAI / Claude はカタログの既定モデル、ローカル LLM は MRU 先頭（履歴なしなら空）を選ぶ。
        // カタログの並び順は上位モデルからの表示順なので、先頭を既定にすると切替のたびに
        // 最上位（＝最も高コスト）のモデルが選ばれてしまう。
        ApiModel = string.IsNullOrEmpty(CurrentApiDefaultModel)
            ? ApiModelCandidates.FirstOrDefault()?.Name ?? string.Empty
            : CurrentApiDefaultModel;

        if (value == AiProvider.LocalLlm && string.IsNullOrWhiteSpace(EndpointOverride))
        {
            EndpointOverride = LocalLlmDefaults.Endpoint;
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

    partial void OnEndpointOverrideChanged(string value) =>
        OnPropertyChanged(nameof(EffectiveEndpointOverride));

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
