using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Db.UI;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using DbStrings = QuickER.Db.UI.Resources.Strings;

namespace QuickER.Tests.FeatureModules;

/// <summary>
/// <see cref="DbToolsFeatureModule"/> の DI 登録・ツールバー寄与・方言切替追従を検証するテストクラス。
/// </summary>
/// <remarks>
/// resx の期待値は厳密型アクセサ（<see cref="DbStrings"/>）経由で取得して比較する
/// （グローバルカルチャは変更しない）。
/// </remarks>
public class DbToolsFeatureModuleTests
{
    /// <summary>モジュールの依存（ホスト・ダイアログ・ファイル選択・プロバイダレジストリ）を登録する</summary>
    private static ServiceCollection BuildServices(StubErDiagramHost host)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(host);
        services.AddSingleton<IDialogService>(new StubDialogService());
        services.AddSingleton<IFileDialogService>(new NullFileDialogService());
        services.AddSingleton(new DatabaseProviderRegistry(Array.Empty<IDatabaseProvider>()));
        return services;
    }

    /// <summary>Id が "db-tools" であることを検証する</summary>
    [Fact(DisplayName = "Id は db-tools")]
    public void Id_IsDbTools()
    {
        new DbToolsFeatureModule().Id.Should().Be("db-tools");
    }

    /// <summary>ConfigureServices 後に提示シーム 2 種とコマンドサービス 2 種が解決できることを検証する</summary>
    [Fact(DisplayName = "ConfigureServices 後にサービス群が解決できる")]
    public void ConfigureServices_RegistersResolvableServices()
    {
        var module = new DbToolsFeatureModule();
        var services = BuildServices(new StubErDiagramHost());

        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        provider.GetService<IDbConnectionDialogPresenter>().Should().NotBeNull();
        provider.GetService<ISchemaSyncDialogPresenter>().Should().NotBeNull();
        provider.GetService<DbImportCommandService>().Should().NotBeNull();
        provider.GetService<DbSyncCommandService>().Should().NotBeNull();
    }

    /// <summary>CreateToolbarItems が resx 一致の 2 件（DB 取込・DB 同期）を返すことを検証する</summary>
    [Fact(DisplayName = "CreateToolbarItems は resx 一致の 2 件を返す")]
    public void CreateToolbarItems_ReturnsTwoLocalizedItems()
    {
        var module = new DbToolsFeatureModule();
        var services = BuildServices(new StubErDiagramHost());
        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var items = module.CreateToolbarItems(provider);

        items.Should().HaveCount(2);

        // ①DB 取込: 前のグループとの区切りとして BeginsGroup=true
        var import = items[0];
        import.Icon.Should().Be("🛢");
        import.Label.Should().Be(DbStrings.Toolbar_ImportFromDb);
        import.Tooltip.Should().Be(DbStrings.Toolbar_ImportFromDbTooltip);
        import.Command.Should().NotBeNull();
        import.BeginsGroup.Should().BeTrue();

        // ②DB 同期: 区切りなし・ツールチップは対象 DBMS（既定 sqlserver）に応じた通常説明
        var sync = items[1];
        sync.Icon.Should().Be("⇪");
        sync.Label.Should().Be(DbStrings.Toolbar_SyncToDb);
        sync.Tooltip.Should().Be(DbStrings.Db_SyncWriteBack);
        sync.Command.Should().NotBeNull();
        sync.BeginsGroup.Should().BeFalse();
    }

    /// <summary>対象 DBMS 切替の通知で、DB 同期ボタンのツールチップと実行可否が更新されることを検証する</summary>
    [Fact(DisplayName = "TargetDbmsChanged で DB 同期の Tooltip と CanExecute が更新される")]
    public void TargetDbmsChanged_UpdatesSyncTooltipAndCanExecute()
    {
        var host = new StubErDiagramHost { TargetDbmsToReturn = SqlServerProvider.ProviderName };
        var module = new DbToolsFeatureModule();
        var services = BuildServices(host);
        module.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var sync = module.CreateToolbarItems(provider)[1];

        // 既定（SQL Server）は実行可・通常ツールチップ
        sync.Command.CanExecute(null).Should().BeTrue();
        sync.Tooltip.Should().Be(DbStrings.Db_SyncWriteBack);

        // SQLite へ切替して通知すると、実行不可・未対応ツールチップへ更新される
        host.TargetDbmsToReturn = SqliteProvider.ProviderName;
        host.RaiseTargetDbmsChanged();

        sync.Command.CanExecute(null).Should().BeFalse();
        sync.Tooltip.Should().Be(DbStrings.Db_SyncSqliteUnsupported);
    }

    /// <summary>OnMainWindowClosing が例外なく完了することを検証する（後始末不要の空実装）</summary>
    [Fact(DisplayName = "OnMainWindowClosing は例外なく完了する")]
    public void OnMainWindowClosing_DoesNotThrow()
    {
        var module = new DbToolsFeatureModule();
        var services = BuildServices(new StubErDiagramHost());
        module.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var act = () => module.OnMainWindowClosing(provider);
        act.Should().NotThrow();
    }
}
