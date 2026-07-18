using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Db.UI.Resources;
using QuickER.Extensibility;

namespace QuickER.Db.UI;

/// <summary>
/// DB 取込・DB 同期機能をホスト（QuickER.Gui）へ着脱可能な形で提供するフィーチャーモジュール。
/// </summary>
/// <remarks>
/// DI へダイアログ提示シーム 2 種とコマンドサービス 2 種を登録し、
/// ツールバーへ「DB 取込」「DB 同期」の 2 ボタンを寄与する。
/// DB 同期ボタンの活性・ツールチップは対象 DBMS に依存するため、
/// <see cref="CreateToolbarItems"/> 内で <see cref="IErDiagramHost.TargetDbmsChanged"/> を購読し、
/// 方言切替のたびに実行可否とツールチップを再評価する。
/// ダイアログはすべてモーダルで残存するモードレスウィンドウが無いため、終了時の後始末は不要（空実装）。
/// </remarks>
public sealed class DbToolsFeatureModule : IFeatureModule
{
    /// <inheritdoc />
    public string Id => "db-tools";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        // ダイアログ提示シーム（テスト容易性のためインターフェイス越しに提供）
        services.AddSingleton<IDbConnectionDialogPresenter, DbConnectionDialogPresenter>();
        services.AddSingleton<ISchemaSyncDialogPresenter, SchemaSyncDialogPresenter>();

        // コマンドサービス（DB 取込・DB 同期）
        services.AddSingleton<DbImportCommandService>();
        services.AddSingleton<DbSyncCommandService>();
    }

    /// <inheritdoc />
    public IReadOnlyList<FeatureToolbarItem> CreateToolbarItems(IServiceProvider services)
    {
        var host = services.GetRequiredService<IErDiagramHost>();
        var import = services.GetRequiredService<DbImportCommandService>();
        var sync = services.GetRequiredService<DbSyncCommandService>();

        // ①DB 取込: 前のグループ（対象 DB 選択など）との区切りとして BeginsGroup=true
        var importItem = new FeatureToolbarItem(
            icon: "🛢",
            label: Strings.Toolbar_ImportFromDb,
            tooltip: Strings.Toolbar_ImportFromDbTooltip,
            command: new AsyncRelayCommand(import.RunAsync),
            beginsGroup: true
        );

        // ②DB 同期: 実行可否とツールチップは対象 DBMS に依存する（SQLite は未対応）
        var syncCommand = new RelayCommand(sync.Run, () => sync.CanRun);
        var syncItem = new FeatureToolbarItem(
            icon: "⇪",
            label: Strings.Toolbar_SyncToDb,
            tooltip: sync.CurrentTooltip,
            command: syncCommand
        );

        // 方言切替のたびに DB 同期ボタンの活性・ツールチップを再評価する。
        // ツールバー UI の生成後にホストへ購読するため、クロージャで対象アイテムとコマンドを捕捉する。
        host.TargetDbmsChanged += (_, _) =>
        {
            syncCommand.NotifyCanExecuteChanged();
            syncItem.Tooltip = sync.CurrentTooltip;
        };

        return new[] { importItem, syncItem };
    }

    /// <inheritdoc />
    public void OnMainWindowClosing(IServiceProvider services)
    {
        // モーダルダイアログのみで残存するモードレスウィンドウが無いため、後始末は不要（空実装）。
    }
}
