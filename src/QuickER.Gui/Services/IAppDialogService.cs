using System.Windows;
using QuickER.Model;
using QuickER.Provider;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>接続ダイアログの確定結果（接続設定と選択された方言）</summary>
/// <param name="Settings">確定した接続設定</param>
/// <param name="Provider">確定時に選択されていたプロバイダ</param>
public sealed record DbConnectionDialogResult(
    DbConnectionSettings Settings,
    IDatabaseProvider Provider
);

/// <summary>
/// アプリ固有のモーダルダイアログ（C# 生成・DB 接続・スキーマ同期）の表示を抽象化するインターフェース
/// </summary>
/// <remarks>
/// メッセージボックスは <see cref="IDialogService"/>、ファイル選択は <see cref="IFileDialogService"/> が担う。
/// ViewModel から <c>Views.*</c> への直接依存を除去し、単体テストではスタブへ差し替える。
/// </remarks>
public interface IAppDialogService
{
    /// <summary>C# コード生成ダイアログを表示し、生成設定を返す（キャンセル時は null）</summary>
    CSharpGenerationDialogResult? ShowCSharpGenerationDialog();

    /// <summary>DB 接続ダイアログを表示し、接続設定と方言を返す（キャンセル時は null）</summary>
    /// <param name="mode">用途（取込は DBMS 選択可・同期は方言固定）</param>
    /// <param name="fixedProvider">同期時に固定する方言（取込では初期選択に用いる）</param>
    /// <param name="title">ウィンドウタイトル（省略時は既定）</param>
    DbConnectionDialogResult? ShowDbConnectionDialog(
        DbConnectionDialogMode mode,
        IDatabaseProvider? fixedProvider = null,
        string? title = null
    );

    /// <summary>スキーマ同期ダイアログを表示する（方言・接続設定・目標スキーマを渡す）</summary>
    void ShowSchemaSyncDialog(
        IDatabaseProvider provider,
        DbConnectionSettings settings,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    );
}

/// <summary>WPF の <c>Views.*</c> ウィンドウを用いた <see cref="IAppDialogService"/> の既定実装</summary>
public sealed class WpfAppDialogService : IAppDialogService
{
    /// <summary>子ダイアログ ViewModel が利用するファイル選択サービス</summary>
    private readonly IFileDialogService _files;

    /// <summary>DB 接続ダイアログが用いるプロバイダレジストリ</summary>
    private readonly DatabaseProviderRegistry _providers;

    /// <summary>依存を注入して生成する</summary>
    public WpfAppDialogService(IFileDialogService files, DatabaseProviderRegistry providers)
    {
        _files = files;
        _providers = providers;
    }

    /// <inheritdoc />
    public CSharpGenerationDialogResult? ShowCSharpGenerationDialog()
    {
        var viewModel = new CSharpGenerationDialogViewModel(files: _files);
        var dialog = new Views.CSharpGenerationDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.ViewModel.Result : null;
    }

    /// <inheritdoc />
    public DbConnectionDialogResult? ShowDbConnectionDialog(
        DbConnectionDialogMode mode,
        IDatabaseProvider? fixedProvider = null,
        string? title = null
    )
    {
        var viewModel = new DbConnectionDialogViewModel(_providers, mode, fixedProvider);
        var dialog = new Views.DbConnectionDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

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

    /// <inheritdoc />
    public void ShowSchemaSyncDialog(
        IDatabaseProvider provider,
        DbConnectionSettings settings,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    )
    {
        var viewModel = new SchemaSyncDialogViewModel(provider, settings, entities, relationships);
        var dialog = new Views.SchemaSyncDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        dialog.ShowDialog();
    }
}
