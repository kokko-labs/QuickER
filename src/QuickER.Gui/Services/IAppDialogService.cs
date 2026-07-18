using System.Windows;
using QuickER.CodeGen.UI;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;
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

/// <summary>印刷ダイアログの確定結果（サイズモード・ヘッダのタイトル・印刷日時の印字有無）</summary>
/// <param name="SizeMode">縮小フィット／原寸大の選択</param>
/// <param name="Title">ヘッダに表示する図のタイトル（空欄ならヘッダへ印字しない）</param>
/// <param name="IncludeTimestamp">ヘッダに印刷日時を印字するかどうか</param>
public sealed record PrintOptions(PrintSizeMode SizeMode, string Title, bool IncludeTimestamp);

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
    /// <param name="currentProvider">
    /// アプリの現在のプロバイダ。QuickER 版 Repository（SQL Server 専用）の選択可否判定と DB 表示名の提示に使う
    /// </param>
    CSharpGenerationDialogResult? ShowCSharpGenerationDialog(IDatabaseProvider currentProvider);

    /// <summary>名前付きクエリ定義エディタを表示し、確定した定義リストを返す（キャンセル時は null）</summary>
    /// <param name="diagram">エンティティと既存クエリを含む現在の ER 図（この参照は変更しない）</param>
    List<QueryDefinition>? ShowQueryDefinitionDialog(ErDiagram diagram);

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

    /// <summary>印刷オプション（サイズモード・タイトル・日時印字）の選択ダイアログを表示する（キャンセル時は null）</summary>
    /// <param name="defaultTitle">タイトル入力欄の初期値（最後に保存／読込した文書名。未保存なら null）</param>
    PrintOptions? ShowPrintOptionsDialog(string? defaultTitle);
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
    public CSharpGenerationDialogResult? ShowCSharpGenerationDialog(
        IDatabaseProvider currentProvider
    )
    {
        var viewModel = new CSharpGenerationDialogViewModel(
            files: _files,
            currentProvider: currentProvider,
            dialogs: new MessageBoxDialogService()
        );
        var dialog = new CSharpGenerationDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.ViewModel.Result : null;
    }

    /// <inheritdoc />
    public List<QueryDefinition>? ShowQueryDefinitionDialog(ErDiagram diagram)
    {
        var viewModel = new QueryDefinitionDialogViewModel(diagram);
        var dialog = new QueryDefinitionDialog(viewModel)
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
        var viewModel = new DbConnectionDialogViewModel(
            _providers,
            mode,
            fixedProvider,
            fileDialogService: _files
        );
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

    /// <inheritdoc />
    public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle)
    {
        var dialog = new Views.PrintOptionsDialog(defaultTitle)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
