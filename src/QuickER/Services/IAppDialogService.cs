using System.Windows;
using QuickER.Model;
using QuickER.SqlServer;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>
/// アプリ固有のモーダルダイアログ（C# 生成・SQL 接続・スキーマ同期）の表示を抽象化するインターフェース
/// </summary>
/// <remarks>
/// メッセージボックスは <see cref="IDialogService"/>、ファイル選択は <see cref="IFileDialogService"/> が担う。
/// ViewModel から <c>Views.*</c> への直接依存を除去し、単体テストではスタブへ差し替える。
/// </remarks>
public interface IAppDialogService
{
    /// <summary>C# コード生成ダイアログを表示し、生成設定を返す（キャンセル時は null）</summary>
    CSharpGenerationDialogResult? ShowCSharpGenerationDialog();

    /// <summary>SQL Server 接続ダイアログを表示し、接続設定を返す（キャンセル時は null）</summary>
    /// <param name="title">ウィンドウタイトル（省略時は既定）</param>
    SqlConnectionSettings? ShowSqlConnectionDialog(string? title = null);

    /// <summary>スキーマ同期ダイアログを表示する（接続設定と目標スキーマを渡す）</summary>
    void ShowSchemaSyncDialog(
        SqlConnectionSettings settings,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    );
}

/// <summary>WPF の <c>Views.*</c> ウィンドウを用いた <see cref="IAppDialogService"/> の既定実装</summary>
public sealed class WpfAppDialogService : IAppDialogService
{
    /// <inheritdoc />
    public CSharpGenerationDialogResult? ShowCSharpGenerationDialog()
    {
        var dialog = new Views.CSharpGenerationDialog { Owner = Application.Current?.MainWindow };

        return dialog.ShowDialog() == true ? dialog.ViewModel.Result : null;
    }

    /// <inheritdoc />
    public SqlConnectionSettings? ShowSqlConnectionDialog(string? title = null)
    {
        var dialog = new Views.SqlConnectionDialog { Owner = Application.Current?.MainWindow };

        if (title is not null)
        {
            dialog.Title = title;
        }

        return dialog.ShowDialog() == true ? dialog.ViewModel.Result : null;
    }

    /// <inheritdoc />
    public void ShowSchemaSyncDialog(
        SqlConnectionSettings settings,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    )
    {
        var viewModel = new SchemaSyncDialogViewModel(settings, entities, relationships);
        var dialog = new Views.SchemaSyncDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        dialog.ShowDialog();
    }
}
