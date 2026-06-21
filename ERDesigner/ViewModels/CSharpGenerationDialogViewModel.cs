using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ERDesigner.ViewModels;

/// <summary>C# コード生成ダイアログの ViewModel</summary>
/// <remarks><see cref="GeneratedRegex"/> を利用するため partial クラスとして定義する</remarks>
public partial class CSharpGenerationDialogViewModel : ObservableObject
{
    /// <summary>確定結果（OK 確定まで null）</summary>
    public CSharpGenerationDialogResult? Result { get; private set; }

    /// <summary>ダイアログを閉じる際に呼ぶアクション（引数は確定可否）</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>出力ファイル選択ダイアログを開くためのコールバック</summary>
    public Func<string, string?>? BrowseOutputFileAction { get; set; }

    /// <summary>生成先の namespace</summary>
    [ObservableProperty]
    private string _namespaceName;

    /// <summary>生成ファイルの出力先パス</summary>
    [ObservableProperty]
    private string _outputFilePath;

    /// <summary>入力エラーや補助メッセージ</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>名前空間と出力先の初期値を指定して ViewModel を生成する</summary>
    public CSharpGenerationDialogViewModel(
        string namespaceName,
        string outputFilePath = "ErDesignerEntities.g.cs"
    )
    {
        _namespaceName = namespaceName;
        _outputFilePath = outputFilePath;
    }

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

    /// <summary>入力内容を検証して確定する（不正時はステータスにエラーを表示する）</summary>
    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(NamespaceName))
        {
            StatusMessage = "namespace を入力してください。";
            return;
        }

        if (!IsValidNamespace(NamespaceName))
        {
            StatusMessage = "namespace の形式が正しくありません。";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFilePath))
        {
            StatusMessage = "出力先ファイルを指定してください。";
            return;
        }

        Result = new CSharpGenerationDialogResult(
            NamespaceName.Trim(),
            OutputFilePath.Trim(),
            GenerateEntityClasses,
            GenerateEditModels,
            GenerateMappers,
            GenerateRepositories,
            GenerateValueObjects,
            UseGuidKeyForStringPrimaryKey
        );
        CloseAction?.Invoke(true);
    }

    /// <summary>確定せずダイアログを閉じる</summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
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
public sealed record CSharpGenerationDialogResult(
    string NamespaceName,
    string OutputFilePath,
    bool GenerateEntityClasses,
    bool GenerateEditModels,
    bool GenerateMappers,
    bool GenerateRepositories,
    bool GenerateValueObjects,
    bool UseGuidKeyForStringPrimaryKey
);
