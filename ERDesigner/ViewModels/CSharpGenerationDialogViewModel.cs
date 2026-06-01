using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ERDesigner.ViewModels;

/// <summary>
/// C# コード生成ダイアログ用の ViewModel です。
/// GeneratedRegex を利用するため partial クラスとして定義します。
/// </summary>
public partial class CSharpGenerationDialogViewModel : ObservableObject
{
    /// <summary>出力結果です。OK 確定までは null です。</summary>
    public CSharpGenerationDialogResult? Result { get; private set; }

    /// <summary>ダイアログを閉じるためのアクションです。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>出力ファイル選択を開くためのコールバックです。</summary>
    public Func<string, string?>? BrowseOutputFileAction { get; set; }

    /// <summary>生成先 namespace です。</summary>
    [ObservableProperty]
    private string _namespaceName;

    /// <summary>生成ファイルの出力先パスです。</summary>
    [ObservableProperty]
    private string _outputFilePath;

    /// <summary>入力エラーや補助メッセージです。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>初期値を指定して ViewModel を生成します。</summary>
    public CSharpGenerationDialogViewModel(string namespaceName, string outputFilePath = "ErDesignerEntities.g.cs")
    {
        _namespaceName = namespaceName;
        _outputFilePath = outputFilePath;
    }

    /// <summary>Entity クラスを生成するかどうかです。</summary>
    [ObservableProperty]
    private bool _generateEntityClasses = true;

    /// <summary>EditModel クラスを生成するかどうかです。</summary>
    [ObservableProperty]
    private bool _generateEditModels = true;

    /// <summary>Mapper クラスを生成するかどうかです。</summary>
    [ObservableProperty]
    private bool _generateMappers = true;

    /// <summary>Repository クラスを生成するかどうかです。</summary>
    [ObservableProperty]
    private bool _generateRepositories = true;

    /// <summary>出力先ファイルを選択します。</summary>
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

    /// <summary>入力内容を確定します。</summary>
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

        Result = new CSharpGenerationDialogResult(NamespaceName.Trim(), OutputFilePath.Trim(), GenerateEntityClasses, GenerateEditModels, GenerateMappers, GenerateRepositories);
        CloseAction?.Invoke(true);
    }

    /// <summary>キャンセルしてダイアログを閉じます。</summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    /// <summary>namespace として妥当な形式か簡易判定します。</summary>
    private static bool IsValidNamespace(string value)
    {
        var segments = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 && segments.All(segment => IdentifierRegex().IsMatch(segment));
    }

    [GeneratedRegex(@"^[_\p{L}][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}

/// <summary>
/// C# コード生成ダイアログの確定結果です。
/// </summary>
public sealed record CSharpGenerationDialogResult(
    string NamespaceName,
    string OutputFilePath,
    bool GenerateEntityClasses,
    bool GenerateEditModels,
    bool GenerateMappers,
    bool GenerateRepositories
);
