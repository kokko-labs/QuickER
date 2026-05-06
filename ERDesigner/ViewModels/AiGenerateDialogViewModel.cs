using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>
/// ChatGPT/Ollama によるスキーマ生成ダイアログ用 ViewModel。
/// </summary>
public partial class AiGenerateDialogViewModel : ObservableObject
{
    /// <summary>命名規則の選択肢です。</summary>
    public sealed record IdentifierNamingStyleOption(AiIdentifierNamingStyle Value, string DisplayName);

    private readonly IAiSchemaClient _client;
    private bool _isInitializing;

    /// <summary>API キーストアのキー名。</summary>
    private const string OpenAiKeyName = "OpenAiApiKey";

    /// <summary>選択中のプロバイダ。</summary>
    [ObservableProperty]
    private AiProvider _provider = AiProvider.OpenAi;

    /// <summary>API キー (OpenAI のみ使用)。</summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    /// <summary>モデル名 (ComboBox)。</summary>
    [ObservableProperty]
    private string _model = "gpt-5.4-mini";

    private IdentifierNamingStyleOption? _selectedIdentifierNamingStyle;

    /// <summary>Ollama 等のカスタムエンドポイント URL。</summary>
    [ObservableProperty]
    private string _endpointOverride = string.Empty;

    /// <summary>API キーを暗号化保存するか。</summary>
    [ObservableProperty]
    private bool _saveApiKey = true;

    /// <summary>自然言語の要件入力。</summary>
    [ObservableProperty]
    private string _prompt = "ECサイトの顧客・注文・商品・カテゴリを管理するスキーマを設計してください。";

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

    /// <summary>OpenAI 既定モデル一覧 (ユーザー希望の "gpt-5.4-mini" を既定)。</summary>
    public IReadOnlyList<string> OpenAiModels { get; } = new[] { "gpt-5.4-mini", "gpt-5.4", "gpt-4o-mini", "gpt-4o", "gpt-4.1", "gpt-4.1-mini" };

    /// <summary>Ollama でよく使われるモデル例。</summary>
    public IReadOnlyList<string> OllamaModels { get; } = new[] { "gpt-oss:20b", "qwen3.6", "gemma4:e4b", "gemma4:31b-cloud" };

    /// <summary>テーブル名・カラム名の命名規則候補。</summary>
    public IReadOnlyList<IdentifierNamingStyleOption> IdentifierNamingStyleOptions { get; } =
    [
        new(AiIdentifierNamingStyle.PascalCase, "パスカルケース (CustomerOrder / CustomerId)"),
        new(AiIdentifierNamingStyle.SnakeCase, "スネークケース (customer_order / customer_id)"),
    ];

    /// <summary>現在のプロバイダに応じた候補モデル。</summary>
    public IReadOnlyList<string> ModelCandidates => Provider == AiProvider.OpenAi ? OpenAiModels : OllamaModels;

    /// <summary>現在選択中の命名規則。</summary>
    public AiIdentifierNamingStyle IdentifierNamingStyle => SelectedIdentifierNamingStyle?.Value ?? AiIdentifierNamingStyle.PascalCase;

    /// <summary>生成するテーブル名・カラム名の命名規則。</summary>
    public IdentifierNamingStyleOption? SelectedIdentifierNamingStyle
    {
        get => _selectedIdentifierNamingStyle;
        set => SetProperty(ref _selectedIdentifierNamingStyle, value);
    }

    /// <summary>OpenAI 選択時のみ APIキー欄を表示するためのフラグ。</summary>
    public bool ShowApiKey => Provider == AiProvider.OpenAi;

    /// <summary>Ollama 選択時のみエンドポイント欄を表示するためのフラグ。</summary>
    public bool ShowEndpoint => Provider == AiProvider.Ollama;

    /// <summary>新しいダイアログ ViewModel を生成します。</summary>
    public AiGenerateDialogViewModel(IAiSchemaClient? client = null)
    {
        _client = client ?? new OpenAiSchemaClient();
        _isInitializing = true;
        // 保存済み API キーがあれば自動入力
        ApiKey = ApiKeyStore.Load(OpenAiKeyName);
        SelectedIdentifierNamingStyle = IdentifierNamingStyleOptions[0];
        _isInitializing = false;
    }

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

        if (Provider != AiProvider.OpenAi)
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

        if (Provider == AiProvider.OpenAi && string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "OpenAI API キーを入力してください。";
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
                EndpointOverride = string.IsNullOrWhiteSpace(EndpointOverride) ? null : EndpointOverride,
                Prompt = Prompt,
            };

            var result = await _client.GenerateAsync(settings).ConfigureAwait(true);
            Result = result;

            if (Provider == AiProvider.OpenAi)
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
