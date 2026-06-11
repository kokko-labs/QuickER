using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>
/// AI (OpenAI / Ollama) による ER 図スキーマ生成ダイアログの ViewModel
/// </summary>
public partial class AiGenerateDialogViewModel : ObservableObject
{
    /// <summary>新規生成モードで初期表示するサンプル要件文</summary>
    private const string CreateNewPromptSample = "ECサイトの顧客・注文・商品・カテゴリを管理するスキーマを設計してください。";
    /// <summary>既存更新モードで初期表示するサンプル要件文</summary>
    private const string UpdateExistingPromptSample = "会員ランク管理と注文ステータス履歴を追加してください。";

    /// <summary>生成方法 (新規生成 / 既存更新) の選択肢</summary>
    /// <param name="Value">生成モードの値</param>
    /// <param name="DisplayName">画面に表示する文言</param>
    public sealed record GenerationModeOption(AiGenerationMode Value, string DisplayName);

    /// <summary>テーブル名・カラム名の命名規則の選択肢</summary>
    /// <param name="Value">命名規則の値</param>
    /// <param name="DisplayName">画面に表示する文言</param>
    public sealed record IdentifierNamingStyleOption(AiIdentifierNamingStyle Value, string DisplayName);

    /// <summary>テーブル名の単複数の選択肢</summary>
    /// <param name="Value">単複数スタイルの値</param>
    /// <param name="DisplayName">画面に表示する文言</param>
    public sealed record TableNameNumberStyleOption(AiTableNameNumberStyle Value, string DisplayName);

    /// <summary>スキーマ生成を実行する AI クライアント</summary>
    private readonly IAiSchemaClient _client;
    /// <summary>更新対象となる既存 ER 図 (エンティティが 1 件もない場合は null)</summary>
    private readonly Models.ErDiagram? _existingDiagram;
    /// <summary>コンストラクタでの初期値設定中に API キーの永続化処理が走るのを抑止するフラグ</summary>
    private bool _isInitializing;

    /// <summary>API キーストアへ保存する際のキー名</summary>
    private const string OpenAiKeyName = "OpenAiApiKey";

    /// <summary>選択中の AI プロバイダ</summary>
    [ObservableProperty]
    private AiProvider _provider = AiProvider.OpenAI;

    /// <summary>API キー (OpenAI 選択時のみ使用)</summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    /// <summary>使用するモデル名 (ComboBox で選択)</summary>
    [ObservableProperty]
    private string _model = AiModelCatalog.DefaultOpenAiModel;

    /// <summary><see cref="SelectedIdentifierNamingStyle"/> のバッキングフィールド</summary>
    private IdentifierNamingStyleOption? _selectedIdentifierNamingStyle;
    /// <summary><see cref="SelectedTableNameNumberStyle"/> のバッキングフィールド</summary>
    private TableNameNumberStyleOption? _selectedTableNameNumberStyle;
    /// <summary><see cref="SelectedGenerationMode"/> のバッキングフィールド</summary>
    private GenerationModeOption? _selectedGenerationMode;

    /// <summary>Ollama 等で使用するカスタムエンドポイント URL</summary>
    [ObservableProperty]
    private string _endpointOverride = string.Empty;

    /// <summary>API キーを暗号化保存するかどうか</summary>
    [ObservableProperty]
    private bool _saveApiKey = true;

    /// <summary>自然言語で記述するスキーマ要件</summary>
    [ObservableProperty]
    private string _prompt = CreateNewPromptSample;

    /// <summary>生成処理の実行中かどうか</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>状態・エラーの表示文言</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>生成結果 (キャンセル時は null)</summary>
    public AiSchemaJson? Result { get; private set; }

    /// <summary>ダイアログを閉じるためのアクション (View 側が注入する)</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>OpenAI の既定モデル候補一覧</summary>
    public IReadOnlyList<string> OpenAiModels { get; } = AiModelCatalog.OpenAiModels;

    /// <summary>Ollama でよく使われるモデル候補一覧</summary>
    public IReadOnlyList<string> OllamaModels { get; } = AiModelCatalog.OllamaModels;

    /// <summary>生成方法の選択肢一覧 (既存 ER 図がない場合は新規生成のみ)</summary>
    public IReadOnlyList<GenerationModeOption> GenerationModeOptions { get; }

    /// <summary>テーブル名・カラム名の命名規則の選択肢一覧</summary>
    public IReadOnlyList<IdentifierNamingStyleOption> IdentifierNamingStyleOptions { get; } =
    [
        new(AiIdentifierNamingStyle.PascalCase, "パスカルケース (CustomerOrder / CustomerId)"),
        new(AiIdentifierNamingStyle.SnakeCase, "スネークケース (customer_order / customer_id)"),
    ];

    /// <summary>テーブル名の単複数の選択肢一覧</summary>
    public IReadOnlyList<TableNameNumberStyleOption> TableNameNumberStyleOptions { get; } =
    [new(AiTableNameNumberStyle.Singular, "単数形 (Customer / Order)"), new(AiTableNameNumberStyle.Plural, "複数形 (Customers / Orders)")];

    /// <summary>現在のプロバイダに応じたモデル候補</summary>
    public IReadOnlyList<string> ModelCandidates => Provider == AiProvider.OpenAI ? OpenAiModels : OllamaModels;

    /// <summary>現在選択中の命名規則 (未選択時はパスカルケース)</summary>
    public AiIdentifierNamingStyle IdentifierNamingStyle => SelectedIdentifierNamingStyle?.Value ?? AiIdentifierNamingStyle.PascalCase;

    /// <summary>現在選択中のテーブル名の単複数 (未選択時は単数形)</summary>
    public AiTableNameNumberStyle TableNameNumberStyle => SelectedTableNameNumberStyle?.Value ?? AiTableNameNumberStyle.Singular;

    /// <summary>現在選択中の生成方法 (未選択時は新規生成)</summary>
    public AiGenerationMode GenerationMode => SelectedGenerationMode?.Value ?? AiGenerationMode.CreateNew;

    /// <summary>命名規則・単複数を編集できるかどうか (既存更新モードでは既存の命名に合わせるため編集不可)</summary>
    public bool CanCustomizeNamingOptions => GenerationMode != AiGenerationMode.UpdateExisting;

    /// <summary>選択中の命名規則オプション</summary>
    public IdentifierNamingStyleOption? SelectedIdentifierNamingStyle
    {
        get => _selectedIdentifierNamingStyle;
        set => SetProperty(ref _selectedIdentifierNamingStyle, value);
    }

    /// <summary>選択中のテーブル名単複数オプション</summary>
    public TableNameNumberStyleOption? SelectedTableNameNumberStyle
    {
        get => _selectedTableNameNumberStyle;
        set => SetProperty(ref _selectedTableNameNumberStyle, value);
    }

    /// <summary>選択中の生成方法オプション</summary>
    /// <remarks>変更時に派生プロパティの変更を通知し、未編集のサンプル文を新モード向けに差し替える</remarks>
    public GenerationModeOption? SelectedGenerationMode
    {
        get => _selectedGenerationMode;
        set
        {
            var previousMode = GenerationMode;

            if (!SetProperty(ref _selectedGenerationMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(GenerationMode));
            OnPropertyChanged(nameof(CanCustomizeNamingOptions));
            UpdatePromptSample(previousMode, GenerationMode);
        }
    }

    /// <summary>API キー欄を表示するかどうか (OpenAI 選択時のみ)</summary>
    public bool ShowApiKey => Provider == AiProvider.OpenAI;

    /// <summary>エンドポイント欄を表示するかどうか (Ollama 選択時のみ)</summary>
    public bool ShowEndpoint => Provider == AiProvider.Ollama;

    /// <summary>ViewModel を生成する</summary>
    /// <param name="client">スキーマ生成クライアント (null の場合は <see cref="OpenAiSchemaClient"/>)</param>
    /// <param name="existingDiagram">更新対象候補の既存 ER 図 (エンティティを持たない場合は無視)</param>
    public AiGenerateDialogViewModel(IAiSchemaClient? client = null, Models.ErDiagram? existingDiagram = null)
    {
        _client = client ?? new OpenAiSchemaClient();
        _existingDiagram = existingDiagram?.Entities.Count > 0 ? existingDiagram : null;
        GenerationModeOptions = _existingDiagram is null
            ? [new(AiGenerationMode.CreateNew, "新規 ER 図を生成")]
            : [new(AiGenerationMode.CreateNew, "新規 ER 図を生成"), new(AiGenerationMode.UpdateExisting, "既存 ER 図に追加・変更")];
        // 初期値設定中は OnApiKeyChanged 等による API キー永続化を抑止する
        _isInitializing = true;
        // 保存済みの API キーがあれば自動入力する
        ApiKey = ApiKeyStore.Load(OpenAiKeyName);
        SelectedGenerationMode = GenerationModeOptions[0];
        SelectedIdentifierNamingStyle = IdentifierNamingStyleOptions[0];
        SelectedTableNameNumberStyle = TableNameNumberStyleOptions[0];
        Prompt = GetPromptSample(GenerationMode);
        _isInitializing = false;
    }

    /// <summary>生成モード切り替え時、プロンプトが未編集のサンプル文のままであれば新モード向けのサンプル文に差し替える</summary>
    /// <remarks>ユーザーが入力済みの要件文を上書きしないよう、旧モードのサンプル文と一致する場合のみ差し替える</remarks>
    private void UpdatePromptSample(AiGenerationMode previousMode, AiGenerationMode currentMode)
    {
        var previousSample = GetPromptSample(previousMode);

        if (string.IsNullOrWhiteSpace(Prompt) || string.Equals(Prompt, previousSample, StringComparison.Ordinal))
        {
            Prompt = GetPromptSample(currentMode);
        }
    }

    /// <summary>生成モードに応じたサンプル要件文を返す</summary>
    private static string GetPromptSample(AiGenerationMode generationMode) =>
        generationMode == AiGenerationMode.UpdateExisting ? UpdateExistingPromptSample : CreateNewPromptSample;

    /// <summary>API キー変更時に保存設定へ反映する</summary>
    partial void OnApiKeyChanged(string value)
    {
        PersistApiKeyPreference();
    }

    /// <summary>保存チェックの切り替え時に保存状態へ反映する</summary>
    partial void OnSaveApiKeyChanged(bool value)
    {
        PersistApiKeyPreference();
    }

    /// <summary>保存設定に応じて API キーを暗号化保存する (保存オフ時は空文字で上書きして削除する)</summary>
    /// <remarks>初期化中および OpenAI 以外のプロバイダ選択時は何もしない</remarks>
    private void PersistApiKeyPreference()
    {
        if (_isInitializing)
        {
            return;
        }

        if (Provider != AiProvider.OpenAI)
        {
            return;
        }

        if (SaveApiKey)
        {
            ApiKeyStore.Save(OpenAiKeyName, ApiKey ?? string.Empty);
        }
        else
        {
            ApiKeyStore.Save(OpenAiKeyName, string.Empty);
        }
    }

    /// <summary>プロバイダ切り替え時に関連 UI の表示を更新し、モデルを切り替え先の既定値に戻す</summary>
    partial void OnProviderChanged(AiProvider value)
    {
        OnPropertyChanged(nameof(ModelCandidates));
        OnPropertyChanged(nameof(ShowApiKey));
        OnPropertyChanged(nameof(ShowEndpoint));
        // 旧プロバイダのモデル名が残らないよう既定モデルへ戻す
        Model = ModelCandidates[0];

        if (value == AiProvider.Ollama && string.IsNullOrWhiteSpace(EndpointOverride))
        {
            EndpointOverride = "http://localhost:11434/v1";
        }
    }

    /// <summary>スキーマ生成を実行する</summary>
    /// <remarks>成功時は <see cref="Result"/> を設定してダイアログを閉じ、失敗時は日本語化したエラーをステータスに表示する</remarks>
    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!ValidateInput())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "生成中...";

        try
        {
            var result = await _client.GenerateAsync(BuildGenerationSettings()).ConfigureAwait(true);
            Result = result;

            if (Provider == AiProvider.OpenAI)
            {
                PersistApiKeyPreference();
            }

            CloseAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusMessage = AiErrorMessageLocalizer.ToJapanese(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>入力内容を検証し、問題があればステータスにメッセージを表示する</summary>
    /// <returns>入力が有効な場合は <c>true</c></returns>
    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            StatusMessage = "要件を入力してください。";
            return false;
        }

        if (Provider == AiProvider.OpenAI && string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "OpenAI API キーを入力してください。";
            return false;
        }

        if (GenerationMode == AiGenerationMode.UpdateExisting && _existingDiagram is null)
        {
            StatusMessage = "更新対象の ER 図がありません。";
            return false;
        }

        return true;
    }

    /// <summary>現在の入力値から生成リクエスト設定を組み立てる</summary>
    private AiGenerationSettings BuildGenerationSettings() =>
        new()
        {
            Provider = Provider,
            ApiKey = ApiKey,
            Model = Model,
            IdentifierNamingStyle = IdentifierNamingStyle,
            TableNameNumberStyle = TableNameNumberStyle,
            GenerationMode = GenerationMode,
            ExistingDiagram = IsUpdateExistingMode() ? _existingDiagram : null,
            EndpointOverride = string.IsNullOrWhiteSpace(EndpointOverride) ? null : EndpointOverride,
            Prompt = Prompt,
        };

    /// <summary>既存更新モードが選択されているかどうかを返す</summary>
    private bool IsUpdateExistingMode() => GenerationMode == AiGenerationMode.UpdateExisting;

    /// <summary>生成をキャンセルしてダイアログを閉じる</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseAction?.Invoke(false);
    }
}
