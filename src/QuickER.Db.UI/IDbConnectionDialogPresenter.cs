using System.Windows;
using QuickER.Gui.Abstractions;
using QuickER.Provider;

namespace QuickER.Db.UI;

/// <summary>
/// DB 接続ダイアログの表示を抽象化するシーム（テスト容易性のためコマンドサービスから分離する）。
/// </summary>
/// <remarks>
/// コマンドサービスから <c>DbConnectionDialog</c> ウィンドウへの直接依存を切り離すための切断面。
/// 単体テストでは戻り値を差し替えたフェイクへ置換する。
/// </remarks>
public interface IDbConnectionDialogPresenter
{
    /// <summary>DB 接続ダイアログを表示し、接続設定と方言を返す（キャンセル時は null）</summary>
    /// <param name="mode">用途（取込は DBMS 選択可・同期は方言固定）</param>
    /// <param name="fixedProvider">同期時に固定する方言（取込では初期選択に用いる）</param>
    /// <param name="title">ウィンドウタイトル（省略時は既定）</param>
    /// <param name="allowSqliteFileCreation">新規 SQLite ファイル作成を許可するか（DB 同期のみ true。取込では既定 false）</param>
    DbConnectionDialogResult? Show(
        DbConnectionDialogMode mode,
        IDatabaseProvider? fixedProvider = null,
        string? title = null,
        bool allowSqliteFileCreation = false
    );
}

/// <summary>WPF の <see cref="DbConnectionDialog"/> を用いた <see cref="IDbConnectionDialogPresenter"/> の既定実装</summary>
public sealed class DbConnectionDialogPresenter : IDbConnectionDialogPresenter
{
    /// <summary>子ダイアログ ViewModel が利用するファイル選択サービス</summary>
    private readonly IFileDialogService _files;

    /// <summary>DB 接続ダイアログが用いるプロバイダレジストリ</summary>
    private readonly DatabaseProviderRegistry _providers;

    /// <summary>依存を注入して生成する</summary>
    public DbConnectionDialogPresenter(IFileDialogService files, DatabaseProviderRegistry providers)
    {
        _files = files;
        _providers = providers;
    }

    /// <inheritdoc />
    public DbConnectionDialogResult? Show(
        DbConnectionDialogMode mode,
        IDatabaseProvider? fixedProvider = null,
        string? title = null,
        bool allowSqliteFileCreation = false
    )
    {
        var viewModel = new DbConnectionDialogViewModel(
            _providers,
            mode,
            fixedProvider,
            fileDialogService: _files,
            allowSqliteFileCreation: allowSqliteFileCreation
        );
        var dialog = new DbConnectionDialog(viewModel) { Owner = Application.Current?.MainWindow };

        if (title is not null)
        {
            dialog.Title = title;
        }

        if (dialog.ShowDialog() == true && dialog.ViewModel.Result is { } settings)
        {
            return new DbConnectionDialogResult(
                settings,
                dialog.ViewModel.ResultProvider ?? _providers.Get("sqlserver")
            );
        }

        return null;
    }
}
