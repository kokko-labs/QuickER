using System.Windows;
using QuickER.Gui.Abstractions;
using QuickER.Provider;

namespace QuickER.CodeGen.UI;

/// <summary>
/// C# コード生成ダイアログの表示を抽象化するシーム（テスト容易性のためコマンドサービスから分離する）。
/// </summary>
/// <remarks>
/// ViewModel から <c>Views.*</c> ウィンドウへの直接依存を切り離すための切断面。
/// 単体テストでは戻り値を差し替えたフェイクへ置換する。
/// </remarks>
public interface ICSharpGenerationDialogPresenter
{
    /// <summary>C# コード生成ダイアログを表示し、生成設定を返す（キャンセル時は null）</summary>
    /// <param name="currentProvider">
    /// アプリの現在のプロバイダ。QuickER 版 Repository（SQL Server 専用）の選択可否判定と DB 表示名の提示に使う
    /// </param>
    CSharpGenerationDialogResult? Show(IDatabaseProvider currentProvider);
}

/// <summary>WPF の <see cref="CSharpGenerationDialog"/> を用いた <see cref="ICSharpGenerationDialogPresenter"/> の既定実装</summary>
public sealed class CSharpGenerationDialogPresenter : ICSharpGenerationDialogPresenter
{
    /// <summary>子ダイアログ ViewModel が利用するファイル選択サービス</summary>
    private readonly IFileDialogService _files;

    /// <summary>設定の保存／読込の結果を提示するメッセージダイアログ</summary>
    private readonly IDialogService _dialogs;

    /// <summary>依存を注入して生成する</summary>
    public CSharpGenerationDialogPresenter(IFileDialogService files, IDialogService dialogs)
    {
        _files = files;
        _dialogs = dialogs;
    }

    /// <inheritdoc />
    public CSharpGenerationDialogResult? Show(IDatabaseProvider currentProvider)
    {
        var viewModel = new CSharpGenerationDialogViewModel(
            files: _files,
            currentProvider: currentProvider,
            dialogs: _dialogs
        );
        var dialog = new CSharpGenerationDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.ViewModel.Result : null;
    }
}
