using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>
/// ChatGPT/Ollama によるスキーマ生成ダイアログ用 ViewModel。
/// </summary>
public partial class AiGenerateDialogViewModel : ObservableObject
{
    private const string CreateNewPromptSample = "ECサイトの顧客・注文・商品・カテゴリを管理するスキーマを設計してください。";
    private const string UpdateExistingPromptSample = "会員ランク管理と注文ステータス履歴を追加してください。";

    /// <summary>生成方法の選択肢です。</summary>
    public sealed record GenerationModeOption(AiGenerationMode Value, string DisplayName);

    /// <summary>命名規則の選択肢です。</summary>
    public sealed record IdentifierNamingStyleOption(AiIdentifierNamingStyle Value, string DisplayName);

    /// <summary>テーブル名の単複数の選択肢です。</summary>
    public sealed record TableNameNumberStyleOption(AiTableNameNumberStyle Value, string DisplayName);

    private readonly IAiSchemaClient _client;
    private readonly Models.ErDiagram? _existingDiagram;
    private bool _isInitializing;

    /// <summary>API キーストアのキー名。</summary>
    private const string OpenAiKeyName = "OpenAiApiKey";

    /// <summary>選択中のプロバイダ。</summary>
    [ObservableProperty]
    private AiProvider _provider = AiProvider.OpenAI;

    /// <summary>API キー (OpenAI のみ使用)。</summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    /// <summary>モデル名 (ComboBox)。</summary>
    [ObservableProperty]
    private string _model = AiModelCatalog.DefaultOpenAiModel;

    private IdentifierNamingStyleOption? _selectedIdentifierNamingStyle;
    private TableNameNumberStyleOption? _selectedTableNameNumberStyle;
    private GenerationModeOption? _selectedGenerationMode;

    /// <summary>Ollama 等のカスタムエンドポイント URL。</summary>
    [ObservableProperty]
    private string _endpointOverride = string.Empty;

    /// <summary>API キーを暗号化保存するか。</summary>
    [ObservableProperty]
    private bool _saveApiKey = true;

    /// <summary>自然言語の要件入力。</summary>
    [ObservableProperty]
    private string _prompt = CreateNewPromptSample;

    /// <summary>処理中フラグ。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>状態/エラー表示。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>生成結果 (キャンセル時は null)。</summary>
    public AiSchemaJson? Result { get; private set; }

    /// <summary>ダイアログを閉じるためのアクション (View が注入)。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>OpenAI 既定モデル一覧です。</summary>
    public IReadOnlyList<string> OpenAiModels { get; } = AiModelCatalog.OpenAiModels;

    /// <summary>Ollama でよく使われるモデル例です。</summary>
    public IReadOnlyList<string> OllamaModels { get; } = AiModelCatalog.OllamaModels;

    /// <summary>テーブル名・カラム名の命名規則候補。</summary>
    public IReadOnlyList<GenerationModeOption> GenerationModeOptions { get; }

    /// <summary>テーブル名・カラム名の命名規則候補。</summary>
    public IReadOnlyList<IdentifierNamingStyleOption> IdentifierNamingStyleOptions { get; } =
    [
        new(AiIdentifierNamingStyle.PascalCase, "パスカルケース (CustomerOrder / CustomerId)"),
        new(AiIdentifierNamingStyle.SnakeCase, "スネークケース (customer_order / customer_id)"),
    ];

    /// <summary>テーブル名の単複数候補。</summary>
    public IReadOnlyList<TableNameNumberStyleOption> TableNameNumberStyleOptions { get; } =
    [new(AiTableNameNumberStyle.Singular, "単数形 (Customer / Order)"), new(AiTableNameNumberStyle.Plural, "複数形 (Customers / Orders)")];

    /// <summary>現在のプロバイダに応じた候補モデル。</summary>
    public IReadOnlyList<string> ModelCandidates => Provider == AiProvider.OpenAI ? OpenAiModels : OllamaModels;

    /// <summary>現在選択中の命名規則。</summary>
    public AiIdentifierNamingStyle IdentifierNamingStyle => SelectedIdentifierNamingStyle?.Value ?? AiIdentifierNamingStyle.PascalCase;

    /// <summary>現在選択中のテーブル名の単複数。</summary>
    public AiTableNameNumberStyle TableNameNumberStyle => SelectedTableNameNumberStyle?.Value ?? AiTableNameNumberStyle.Singular;

    /// <summary>現在選択中の生成方法。</summary>
    public AiGenerationMode GenerationMode => SelectedGenerationMode?.Value ?? AiGenerationMode.CreateNew;

    /// <summary>命名規則とテーブル名を編集できるかどうかです。</summary>
    public bool CanCustomizeNamingOptions => GenerationMode != AiGenerationMode.UpdateExisting;

    /// <summary>生成するテーブル名・カラム名の命名規則。</summary>
    public IdentifierNamingStyleOption? SelectedIdentifierNamingStyle
    {
        get => _selectedIdentifierNamingStyle;
        set => SetProperty(ref _selectedIdentifierNamingStyle, value);
    }

    /// <summary>生成するテーブル名の単複数。</summary>
    public TableNameNumberStyleOption? SelectedTableNameNumberStyle
    {
        get => _selectedTableNameNumberStyle;
        set => SetProperty(ref _selectedTableNameNumberStyle, value);
    }

    /// <summary>生成方法です。</summary>
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

    /// <summary>OpenAI 選択時のみ APIキー欄を表示するためのフラグ。</summary>
    public bool ShowApiKey => Provider == AiProvider.OpenAI;

    /// <summary>Ollama 選択時のみエンドポイント欄を表示するためのフラグ。</summary>
    public bool ShowEndpoint => Provider == AiProvider.Ollama;

    /// <summary>新しいダイアログ ViewModel を生成します。</summary>
    public AiGenerateDialogViewModel(IAiSchemaClient? client = null, Models.ErDiagram? existingDiagram = null)
    {
        _client = client ?? new OpenAiSchemaClient();
        _existingDiagram = existingDiagram?.Entities.Count > 0 ? existingDiagram : null;
        GenerationModeOptions = _existingDiagram is null
            ? [new(AiGenerationMode.CreateNew, "新規 ER 図を生成")]
            : [new(AiGenerationMode.CreateNew, "新規 ER 図を生成"), new(AiGenerationMode.UpdateExisting, "既存 ER 図に追加・変更")];
        _isInitializing = true;
        // 保存済み API キーがあれば自動入力
        ApiKey = ApiKeyStore.Load(OpenAiKeyName);
        SelectedGenerationMode = GenerationModeOptions[0];
        SelectedIdentifierNamingStyle = IdentifierNamingStyleOptions[0];
        SelectedTableNameNumberStyle = TableNameNumberStyleOptions[0];
        Prompt = GetPromptSample(GenerationMode);
        _isInitializing = false;
    }

    /// <summary>生成モード切り替え時に、未編集のサンプル文のみ新しいモード向けに差し替えます。</summary>
    private void UpdatePromptSample(AiGenerationMode previousMode, AiGenerationMode currentMode)
    {
        var previousSample = GetPromptSample(previousMode);

        if (string.IsNullOrWhiteSpace(Prompt) || string.Equals(Prompt, previousSample, StringComparison.Ordinal))
        {
            Prompt = GetPromptSample(currentMode);
        }
    }

    /// <summary>生成モードに応じたサンプル文を返します。</summary>
    private static string GetPromptSample(AiGenerationMode generationMode) =>
        generationMode == AiGenerationMode.UpdateExisting ? UpdateExistingPromptSample : CreateNewPromptSample;

    partial void OnApiKeyChanged(string value)
    {
        PersistApiKeyPreference();
    }

    partial void OnSaveApiKeyChanged(bool value)
    {
        PersistApiKeyPreference();
    }

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

    partial void OnProviderChanged(AiProvider value)
    {
        OnPropertyChanged(nameof(ModelCandidates));
        OnPropertyChanged(nameof(ShowApiKey));
        OnPropertyChanged(nameof(ShowEndpoint));
        // プロバイダ変更時に既定モデルへ
        Model = ModelCandidates[0];

        if (value == AiProvider.Ollama && string.IsNullOrWhiteSpace(EndpointOverride))
        {
            EndpointOverride = "http://localhost:11434/v1";
        }
    }

    /// <summary>生成処理を実行します。</summary>
    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            StatusMessage = "要件を入力してください。";
            return;
        }

        if (Provider == AiProvider.OpenAI && string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "OpenAI API キーを入力してください。";
            return;
        }

        if (GenerationMode == AiGenerationMode.UpdateExisting && _existingDiagram is null)
        {
            StatusMessage = "更新対象の ER 図がありません。";
            return;
        }

        IsBusy = true;
        StatusMessage = "生成中...";

        try
        {
            var settings = new AiGenerationSettings
            {
                Provider = Provider,
                ApiKey = ApiKey,
                Model = Model,
                IdentifierNamingStyle = IdentifierNamingStyle,
                TableNameNumberStyle = TableNameNumberStyle,
                GenerationMode = GenerationMode,
                ExistingDiagram = GenerationMode == AiGenerationMode.UpdateExisting ? _existingDiagram : null,
                EndpointOverride = string.IsNullOrWhiteSpace(EndpointOverride) ? null : EndpointOverride,
                Prompt = Prompt,
            };

            var result = await _client.GenerateAsync(settings).ConfigureAwait(true);
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

    /// <summary>キャンセルボタン。</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseAction?.Invoke(false);
    }
}
