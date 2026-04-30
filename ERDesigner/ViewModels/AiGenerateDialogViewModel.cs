using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>
/// ChatGPT/Ollama によるスキーマ生成ダイアログ用 ViewModel。
/// </summary>
public partial class AiGenerateDialogViewModel : ObservableObject
{
    private readonly IAiSchemaClient _client;

    /// <summary>API キーストアのキー名。</summary>
    private const string OpenAiKeyName = "OpenAiApiKey";

    /// <summary>選択中のプロバイダ。</summary>
    [ObservableProperty] private AiProvider _provider = AiProvider.OpenAi;
    /// <summary>API キー (OpenAI のみ使用)。</summary>
    [ObservableProperty] private string _apiKey = string.Empty;
    /// <summary>モデル名 (ComboBox)。</summary>
    [ObservableProperty] private string _model = "gpt-5.4-mini";
    /// <summary>Ollama 等のカスタムエンドポイント URL。</summary>
    [ObservableProperty] private string _endpointOverride = string.Empty;
    /// <summary>API キーを暗号化保存するか。</summary>
    [ObservableProperty] private bool _saveApiKey = true;

    /// <summary>自然言語の要件入力。</summary>
    [ObservableProperty] private string _prompt =
        "ECサイトの顧客・注文・商品・カテゴリを管理するスキーマを設計してください。";

    /// <summary>処理中フラグ。</summary>
    [ObservableProperty] private bool _isBusy;
    /// <summary>状態/エラー表示。</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>生成結果 (キャンセル時は null)。</summary>
    public AiSchemaJson? Result { get; private set; }

    /// <summary>ダイアログを閉じるためのアクション (View が注入)。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>OpenAI 既定モデル一覧 (ユーザー希望の "gpt-5.4-mini" を既定)。</summary>
    public IReadOnlyList<string> OpenAiModels { get; } = new[]
    {
        "gpt-5.4-mini", "gpt-5.4", "gpt-4o-mini", "gpt-4o", "gpt-4.1", "gpt-4.1-mini"
    };

    /// <summary>Ollama でよく使われるモデル例。</summary>
    public IReadOnlyList<string> OllamaModels { get; } = new[]
    {
        "llama3.1", "llama3.2", "qwen2.5-coder", "mistral", "phi3"
    };

    /// <summary>現在のプロバイダに応じた候補モデル。</summary>
    public IReadOnlyList<string> ModelCandidates => Provider == AiProvider.OpenAi ? OpenAiModels : OllamaModels;

    /// <summary>OpenAI 選択時のみ APIキー欄を表示するためのフラグ。</summary>
    public bool ShowApiKey => Provider == AiProvider.OpenAi;

    /// <summary>Ollama 選択時のみエンドポイント欄を表示するためのフラグ。</summary>
    public bool ShowEndpoint => Provider == AiProvider.Ollama;

    /// <summary>新しいダイアログ ViewModel を生成します。</summary>
    public AiGenerateDialogViewModel(IAiSchemaClient? client = null)
    {
        _client = client ?? new OpenAiSchemaClient();
        // 保存済み API キーがあれば自動入力
        ApiKey = ApiKeyStore.Load(OpenAiKeyName);
    }

    partial void OnProviderChanged(AiProvider value)
    {
        OnPropertyChanged(nameof(ModelCandidates));
        OnPropertyChanged(nameof(ShowApiKey));
        OnPropertyChanged(nameof(ShowEndpoint));
        // プロバイダ変更時に既定モデルへ
        Model = ModelCandidates[0];
        if (value == AiProvider.Ollama && string.IsNullOrWhiteSpace(EndpointOverride))
            EndpointOverride = "http://localhost:11434/v1";
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
                EndpointOverride = string.IsNullOrWhiteSpace(EndpointOverride) ? null : EndpointOverride,
                Prompt = Prompt
            };
            var result = await _client.GenerateAsync(settings).ConfigureAwait(true);
            Result = result;

            if (Provider == AiProvider.OpenAi)
            {
                if (SaveApiKey) ApiKeyStore.Save(OpenAiKeyName, ApiKey);
                else ApiKeyStore.Save(OpenAiKeyName, string.Empty);
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
