using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Generator;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>C# コード生成ダイアログの ViewModel</summary>
/// <remarks>
/// 設定は <see cref="CSharpGenerationSettingsStore"/> で永続化し、生成確定時に保存・次回構築時に復元する。
/// <see cref="GeneratedRegex"/> を利用するため partial クラスとして定義する
/// </remarks>
public partial class CSharpGenerationDialogViewModel : ObservableObject
{
    /// <summary>設定の永続化ストア</summary>
    private readonly CSharpGenerationSettingsStore _store;

    /// <summary>ベース名前空間変更時の子名前空間追従更新を一時的に抑止するフラグ（設定適用中に使う）</summary>
    private bool _suppressNamespaceFollow;

    /// <summary>確定結果（OK 確定まで null）</summary>
    public CSharpGenerationDialogResult? Result { get; private set; }

    /// <summary>ダイアログを閉じる際に呼ぶアクション（引数は確定可否）</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>出力ファイル選択ダイアログを開くためのコールバック（現在のパスを受け取り、選択結果を返す）</summary>
    public Func<string, string?>? BrowseOutputFileAction { get; set; }

    /// <summary>出力フォルダ選択ダイアログを開くためのコールバック（現在のパスを受け取り、選択結果を返す）</summary>
    public Func<string, string?>? BrowseOutputFolderAction { get; set; }

    /// <summary>設定ストアを指定して ViewModel を生成し、保存済み設定を復元する</summary>
    public CSharpGenerationDialogViewModel(CSharpGenerationSettingsStore? store = null)
    {
        _store = store ?? new CSharpGenerationSettingsStore();
        ApplySettings(_store.Load());
    }

    // ===== 出力モード =====

    /// <summary>出力をカテゴリごとに分割するか（false=1ファイルにまとめる）</summary>
    [ObservableProperty]
    private bool _splitFilesByCategory;

    /// <summary>1 ファイルにまとめるか（<see cref="SplitFilesByCategory"/> の反転。モード①ラジオ用）</summary>
    public bool MergeIntoSingleFile
    {
        get => !SplitFilesByCategory;
        set => SplitFilesByCategory = !value;
    }

    // ===== 名前空間 =====

    /// <summary>ベース（ルート）名前空間</summary>
    [ObservableProperty]
    private string _baseNamespace = CSharpGenerationSettings.DefaultBaseNamespace;

    /// <summary>共有基盤（Runtime）名前空間</summary>
    [ObservableProperty]
    private string _runtimeNamespace = string.Empty;

    /// <summary>Entity 名前空間</summary>
    [ObservableProperty]
    private string _entityNamespace = string.Empty;

    /// <summary>EditModel 名前空間</summary>
    [ObservableProperty]
    private string _editModelNamespace = string.Empty;

    /// <summary>Mapper 名前空間</summary>
    [ObservableProperty]
    private string _mapperNamespace = string.Empty;

    /// <summary>Repository 名前空間</summary>
    [ObservableProperty]
    private string _repositoryNamespace = string.Empty;

    /// <summary>ValueObject 名前空間</summary>
    [ObservableProperty]
    private string _valueObjectNamespace = string.Empty;

    // ===== 出力先 =====

    /// <summary>非分割（モード①）時の出力ファイルパス</summary>
    [ObservableProperty]
    private string _outputFilePath = CSharpGenerationSettings.DefaultOutputFilePath;

    /// <summary>分割（モード②）時の出力フォルダパス</summary>
    [ObservableProperty]
    private string _outputFolderPath = string.Empty;

    // ===== 生成対象 =====

    /// <summary>Entity クラスを生成するかどうか</summary>
    [ObservableProperty]
    private bool _generateEntityClasses = true;

    /// <summary>EditModel クラスを生成するかどうか</summary>
    [ObservableProperty]
    private bool _generateEditModels = true;

    /// <summary>Mapper クラスを生成するかどうか</summary>
    [ObservableProperty]
    private bool _generateMappers = true;

    /// <summary>Repository クラスを生成するかどうか</summary>
    [ObservableProperty]
    private bool _generateRepositories = true;

    /// <summary>全カラムを値オブジェクト（Value Object）として生成するかどうか</summary>
    [ObservableProperty]
    private bool _generateValueObjects;

    /// <summary>string 型の主キーを GuidKey 値オブジェクト（GUID 自動採番）にするかどうか</summary>
    [ObservableProperty]
    private bool _useGuidKeyForStringPrimaryKey;

    /// <summary>入力エラーや補助メッセージ</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>生成されるファイルのプレビュー（「ファイル名 → namespace」の一覧。設定に追従して更新）</summary>
    public ObservableCollection<string> PreviewFiles { get; } = new();

    // ===== 表示制御（派生） =====

    /// <summary>非分割時の出力ファイル欄を表示するか</summary>
    public bool ShowSingleFileOutput => !SplitFilesByCategory;

    /// <summary>分割時の詳細（カテゴリ別名前空間・出力フォルダ）を表示するか</summary>
    public bool ShowSplitOptions => SplitFilesByCategory;

    /// <summary>Runtime 名前空間欄を表示するか（分割時は常に必要）</summary>
    public bool ShowRuntimeNamespace => SplitFilesByCategory;

    /// <summary>Entity 名前空間欄を表示するか</summary>
    public bool ShowEntityNamespace => SplitFilesByCategory && GenerateEntityClasses;

    /// <summary>EditModel 名前空間欄を表示するか</summary>
    public bool ShowEditModelNamespace => SplitFilesByCategory && GenerateEditModels;

    /// <summary>Mapper 名前空間欄を表示するか</summary>
    public bool ShowMapperNamespace => SplitFilesByCategory && GenerateMappers;

    /// <summary>Repository 名前空間欄を表示するか</summary>
    public bool ShowRepositoryNamespace => SplitFilesByCategory && GenerateRepositories;

    /// <summary>ValueObject 名前空間欄を表示するか</summary>
    public bool ShowValueObjectNamespace => SplitFilesByCategory && GenerateValueObjects;

    // ===== 変更フック =====

    partial void OnSplitFilesByCategoryChanged(bool value)
    {
        OnPropertyChanged(nameof(MergeIntoSingleFile));
        RaiseDerivedChanged();
    }

    partial void OnGenerateEntityClassesChanged(bool value) => RaiseDerivedChanged();

    partial void OnGenerateEditModelsChanged(bool value) => RaiseDerivedChanged();

    partial void OnGenerateMappersChanged(bool value) => RaiseDerivedChanged();

    partial void OnGenerateRepositoriesChanged(bool value) => RaiseDerivedChanged();

    partial void OnGenerateValueObjectsChanged(bool value) => RaiseDerivedChanged();

    partial void OnRuntimeNamespaceChanged(string value) => RefreshPreview();

    partial void OnEntityNamespaceChanged(string value) => RefreshPreview();

    partial void OnEditModelNamespaceChanged(string value) => RefreshPreview();

    partial void OnMapperNamespaceChanged(string value) => RefreshPreview();

    partial void OnRepositoryNamespaceChanged(string value) => RefreshPreview();

    partial void OnValueObjectNamespaceChanged(string value) => RefreshPreview();

    partial void OnOutputFilePathChanged(string value) => RefreshPreview();

    /// <summary>ベース名前空間が変わったら、既定（{旧base}.{接尾辞}）のままの子名前空間を新ベースへ追従させる</summary>
    partial void OnBaseNamespaceChanged(string? oldValue, string newValue)
    {
        if (!_suppressNamespaceFollow && oldValue is not null)
        {
            FollowBaseNamespace(oldValue, newValue);
        }

        RefreshPreview();
    }

    /// <summary>派生プロパティ（表示制御）の変更通知を発行し、プレビューを更新する</summary>
    private void RaiseDerivedChanged()
    {
        OnPropertyChanged(nameof(ShowSingleFileOutput));
        OnPropertyChanged(nameof(ShowSplitOptions));
        OnPropertyChanged(nameof(ShowRuntimeNamespace));
        OnPropertyChanged(nameof(ShowEntityNamespace));
        OnPropertyChanged(nameof(ShowEditModelNamespace));
        OnPropertyChanged(nameof(ShowMapperNamespace));
        OnPropertyChanged(nameof(ShowRepositoryNamespace));
        OnPropertyChanged(nameof(ShowValueObjectNamespace));
        RefreshPreview();
    }

    /// <summary>各子名前空間が「{旧base}.{接尾辞}」既定のままなら新ベースへ更新する（手編集済みは保持）</summary>
    private void FollowBaseNamespace(string oldBase, string newBase)
    {
        _suppressNamespaceFollow = true;
        try
        {
            RuntimeNamespace = FollowOne(
                RuntimeNamespace,
                oldBase,
                newBase,
                GenerationBucket.Runtime
            );
            EntityNamespace = FollowOne(EntityNamespace, oldBase, newBase, GenerationBucket.Entity);
            EditModelNamespace = FollowOne(
                EditModelNamespace,
                oldBase,
                newBase,
                GenerationBucket.EditModel
            );
            MapperNamespace = FollowOne(MapperNamespace, oldBase, newBase, GenerationBucket.Mapper);
            RepositoryNamespace = FollowOne(
                RepositoryNamespace,
                oldBase,
                newBase,
                GenerationBucket.Repository
            );
            ValueObjectNamespace = FollowOne(
                ValueObjectNamespace,
                oldBase,
                newBase,
                GenerationBucket.ValueObject
            );
        }
        finally
        {
            _suppressNamespaceFollow = false;
        }
    }

    /// <summary>子名前空間が空または旧既定なら新既定へ、手編集済みならそのままにする</summary>
    private static string FollowOne(
        string current,
        string oldBase,
        string newBase,
        GenerationBucket bucket
    )
    {
        var suffix = GeneratedFilePlanner.DefaultSuffix(bucket);
        var oldDefault = $"{oldBase}.{suffix}";
        return string.IsNullOrWhiteSpace(current) || current == oldDefault
            ? $"{newBase}.{suffix}"
            : current;
    }

    /// <summary>設定値を各プロパティへ適用する（空の子名前空間は {base}.{接尾辞} でプリフィルする）</summary>
    private void ApplySettings(CSharpGenerationSettings settings)
    {
        _suppressNamespaceFollow = true;
        try
        {
            SplitFilesByCategory = settings.SplitFilesByCategory;
            BaseNamespace = settings.BaseNamespace;
            RuntimeNamespace = Prefill(settings.RuntimeNamespace, GenerationBucket.Runtime);
            EntityNamespace = Prefill(settings.EntityNamespace, GenerationBucket.Entity);
            EditModelNamespace = Prefill(settings.EditModelNamespace, GenerationBucket.EditModel);
            MapperNamespace = Prefill(settings.MapperNamespace, GenerationBucket.Mapper);
            RepositoryNamespace = Prefill(
                settings.RepositoryNamespace,
                GenerationBucket.Repository
            );
            ValueObjectNamespace = Prefill(
                settings.ValueObjectNamespace,
                GenerationBucket.ValueObject
            );
            GenerateEntityClasses = settings.GenerateEntityClasses;
            GenerateEditModels = settings.GenerateEditModels;
            GenerateMappers = settings.GenerateMappers;
            GenerateRepositories = settings.GenerateRepositories;
            GenerateValueObjects = settings.GenerateValueObjects;
            UseGuidKeyForStringPrimaryKey = settings.UseGuidKeyForStringPrimaryKey;
            OutputFilePath = settings.OutputFilePath;
            OutputFolderPath = settings.OutputFolderPath;
        }
        finally
        {
            _suppressNamespaceFollow = false;
        }

        StatusMessage = string.Empty;
        RaiseDerivedChanged();
    }

    /// <summary>子名前空間が空なら {base}.{接尾辞} を返す（プリフィル）</summary>
    private string Prefill(string value, GenerationBucket bucket) =>
        string.IsNullOrWhiteSpace(value)
            ? $"{BaseNamespace}.{GeneratedFilePlanner.DefaultSuffix(bucket)}"
            : value;

    /// <summary>現在の設定値から設定オブジェクトを組み立てる（永続化用）</summary>
    private CSharpGenerationSettings ToSettings() =>
        new()
        {
            SplitFilesByCategory = SplitFilesByCategory,
            BaseNamespace = BaseNamespace.Trim(),
            RuntimeNamespace = RuntimeNamespace.Trim(),
            EntityNamespace = EntityNamespace.Trim(),
            EditModelNamespace = EditModelNamespace.Trim(),
            MapperNamespace = MapperNamespace.Trim(),
            RepositoryNamespace = RepositoryNamespace.Trim(),
            ValueObjectNamespace = ValueObjectNamespace.Trim(),
            GenerateEntityClasses = GenerateEntityClasses,
            GenerateEditModels = GenerateEditModels,
            GenerateMappers = GenerateMappers,
            GenerateRepositories = GenerateRepositories,
            GenerateValueObjects = GenerateValueObjects,
            UseGuidKeyForStringPrimaryKey = UseGuidKeyForStringPrimaryKey,
            OutputFilePath = OutputFilePath.Trim(),
            OutputFolderPath = OutputFolderPath.Trim(),
        };

    /// <summary>現在の設定値からコード生成オプションを組み立てる</summary>
    public CodeGenerationOptions ToOptions() =>
        new()
        {
            NamespaceName = BaseNamespace.Trim(),
            OutputFileName = Path.GetFileName(OutputFilePath.Trim()),
            SplitFilesByCategory = SplitFilesByCategory,
            RuntimeNamespace = NullIfEmpty(RuntimeNamespace),
            EntityNamespace = NullIfEmpty(EntityNamespace),
            EditModelNamespace = NullIfEmpty(EditModelNamespace),
            MapperNamespace = NullIfEmpty(MapperNamespace),
            RepositoryNamespace = NullIfEmpty(RepositoryNamespace),
            ValueObjectNamespace = NullIfEmpty(ValueObjectNamespace),
            GenerateEntityClasses = GenerateEntityClasses,
            GenerateEditModels = GenerateEditModels,
            GenerateMappers = GenerateMappers,
            GenerateRepositories = GenerateRepositories,
            GenerateValueObjects = GenerateValueObjects,
            UseGuidKeyForStringPrimaryKey = UseGuidKeyForStringPrimaryKey,
        };

    /// <summary>空白を null へ畳む（オプションのフォールバックを効かせるため）</summary>
    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>現在の設定で生成されるファイル一覧（「ファイル名 → namespace」）を再計算する</summary>
    private void RefreshPreview()
    {
        PreviewFiles.Clear();

        if (string.IsNullOrWhiteSpace(BaseNamespace))
        {
            return;
        }

        foreach (var spec in GeneratedFilePlanner.Plan(ToOptions()))
        {
            PreviewFiles.Add($"{spec.FileName}  →  namespace {spec.NamespaceName}");
        }
    }

    /// <summary>出力先ファイルを選択し、結果をパスへ反映する</summary>
    [RelayCommand]
    private void BrowseOutputFile()
    {
        if (BrowseOutputFileAction is null)
        {
            return;
        }

        var selectedPath = BrowseOutputFileAction(OutputFilePath);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        OutputFilePath = selectedPath;
        StatusMessage = string.Empty;
    }

    /// <summary>出力先フォルダを選択し、結果をパスへ反映する</summary>
    [RelayCommand]
    private void BrowseOutputFolder()
    {
        if (BrowseOutputFolderAction is null)
        {
            return;
        }

        var selectedPath = BrowseOutputFolderAction(OutputFolderPath);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        OutputFolderPath = selectedPath;
        StatusMessage = string.Empty;
    }

    /// <summary>全設定を工場出荷既定へ戻す（ディスクへの反映は次の生成確定時）</summary>
    [RelayCommand]
    private void Clear() => ApplySettings(CSharpGenerationSettings.CreateDefault());

    /// <summary>入力内容を検証して確定し、設定を保存する（不正時はステータスにエラーを表示する）</summary>
    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(BaseNamespace))
        {
            StatusMessage = "namespace を入力してください。";
            return;
        }

        if (!IsValidNamespace(BaseNamespace))
        {
            StatusMessage = "namespace の形式が正しくありません。";
            return;
        }

        if (SplitFilesByCategory)
        {
            if (!ValidateSplitNamespaces(out var invalidName))
            {
                StatusMessage = $"{invalidName} の形式が正しくありません。";
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputFolderPath))
            {
                StatusMessage = "出力先フォルダを指定してください。";
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(OutputFilePath))
        {
            StatusMessage = "出力先ファイルを指定してください。";
            return;
        }

        _store.Save(ToSettings());

        var outputDirectory = SplitFilesByCategory
            ? OutputFolderPath.Trim()
            : Path.GetDirectoryName(OutputFilePath.Trim()) ?? string.Empty;

        Result = new CSharpGenerationDialogResult(ToOptions(), outputDirectory);
        CloseAction?.Invoke(true);
    }

    /// <summary>確定せずダイアログを閉じる</summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    /// <summary>表示中のカテゴリ別名前空間（非空のもの）の形式を検証する</summary>
    /// <param name="invalidName">最初に不正だった項目名（呼び出し側のメッセージ用）</param>
    private bool ValidateSplitNamespaces(out string invalidName)
    {
        var targets = new (bool Show, string Value, string Name)[]
        {
            (ShowRuntimeNamespace, RuntimeNamespace, "Runtime の namespace"),
            (ShowEntityNamespace, EntityNamespace, "Entity の namespace"),
            (ShowEditModelNamespace, EditModelNamespace, "EditModel の namespace"),
            (ShowMapperNamespace, MapperNamespace, "Mapper の namespace"),
            (ShowRepositoryNamespace, RepositoryNamespace, "Repository の namespace"),
            (ShowValueObjectNamespace, ValueObjectNamespace, "ValueObject の namespace"),
        };

        foreach (var target in targets)
        {
            if (
                target.Show
                && !string.IsNullOrWhiteSpace(target.Value)
                && !IsValidNamespace(target.Value)
            )
            {
                invalidName = target.Name;
                return false;
            }
        }

        invalidName = string.Empty;
        return true;
    }

    /// <summary>namespace として妥当な形式かを各セグメントの識別子検証で簡易判定する</summary>
    private static bool IsValidNamespace(string value)
    {
        var segments = value.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return segments.Length > 0 && segments.All(segment => IdentifierRegex().IsMatch(segment));
    }

    /// <summary>C# 識別子として有効なセグメントにマッチする正規表現</summary>
    [GeneratedRegex(@"^[_\p{L}][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}

/// <summary>C# コード生成ダイアログの確定結果</summary>
/// <param name="Options">生成に使うオプション</param>
/// <param name="OutputDirectory">生成ファイルの書き出し先ディレクトリ（モード①はファイルの親、モード②は出力フォルダ）</param>
public sealed record CSharpGenerationDialogResult(
    CodeGenerationOptions Options,
    string OutputDirectory
);
