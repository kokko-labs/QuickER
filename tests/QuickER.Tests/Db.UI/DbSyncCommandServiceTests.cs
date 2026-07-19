using FluentAssertions;
using QuickER.Db.UI;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using DbStrings = QuickER.Db.UI.Resources.Strings;

namespace QuickER.Tests.Db.UI;

/// <summary>
/// <see cref="DbSyncCommandService"/> の実行可否・ツールチップ切替・同期フロー（キャンセル・成功スナップショット）を検証するテストクラス。
/// </summary>
/// <remarks>
/// <c>MainViewModel</c> 由来の DB 同期の可否・ツールチップ・実行ロジックを、フィーチャーモジュール側サービスへ移植したもの。
/// resx 期待値は Db.UI の厳密型アクセサ経由（グローバルカルチャは変更しない）。
/// </remarks>
public class DbSyncCommandServiceTests
{
    /// <summary>SQLite では同期不可・ツールチップに未対応理由が出ることを検証する</summary>
    [Fact(DisplayName = "SQLite では CanRun=false・ツールチップに未対応理由")]
    public void Sqlite_CannotRun_ShowsUnsupportedTooltip()
    {
        var host = new StubErDiagramHost { TargetDbmsToReturn = SqliteProvider.ProviderName };
        var service = new DbSyncCommandService(
            host,
            new FakeConnectionPresenter(null),
            new RecordingSyncPresenter()
        );

        service.CanRun.Should().BeFalse();
        service.CurrentTooltip.Should().Be(DbStrings.Db_SyncSqliteUnsupported);
    }

    /// <summary>SQL Server では同期可・ツールチップに通常の説明が出ることを検証する</summary>
    [Fact(DisplayName = "SQL Server では CanRun=true・ツールチップに通常説明")]
    public void SqlServer_CanRun_ShowsWriteBackTooltip()
    {
        var host = new StubErDiagramHost { TargetDbmsToReturn = SqlServerProvider.ProviderName };
        var service = new DbSyncCommandService(
            host,
            new FakeConnectionPresenter(null),
            new RecordingSyncPresenter()
        );

        service.CanRun.Should().BeTrue();
        service.CurrentTooltip.Should().Be(DbStrings.Db_SyncWriteBack);
    }

    /// <summary>接続ダイアログのキャンセル時は同期ダイアログを開かないことを検証する</summary>
    [Fact(DisplayName = "接続ダイアログのキャンセルでは同期ダイアログを開かない")]
    public void Run_Cancelled_DoesNotShowSyncDialog()
    {
        var host = new StubErDiagramHost { TargetDbmsToReturn = SqlServerProvider.ProviderName };
        var sync = new RecordingSyncPresenter();
        var service = new DbSyncCommandService(host, new FakeConnectionPresenter(null), sync);

        service.Run();

        sync.ShownCount.Should().Be(0);
    }

    /// <summary>接続確定時は、確定時点の図をスナップショットして同期ダイアログへ渡すことを検証する</summary>
    [Fact(DisplayName = "接続確定時は図のスナップショットで同期ダイアログを開く")]
    public void Run_Confirmed_ShowsSyncDialogWithSnapshot()
    {
        var diagram = new ErDiagram
        {
            Entities = { new Entity { TableName = "Customer" } },
            TargetDbms = SqlServerProvider.ProviderName,
        };
        var host = new StubErDiagramHost
        {
            TargetDbmsToReturn = SqlServerProvider.ProviderName,
            DiagramToReturn = diagram,
        };
        var provider = new SqlServerProvider();
        var settings = new DbConnectionSettings();
        var sync = new RecordingSyncPresenter();
        var service = new DbSyncCommandService(
            host,
            new FakeConnectionPresenter(new DbConnectionDialogResult(settings, provider)),
            sync
        );

        service.Run();

        sync.ShownCount.Should().Be(1);
        sync.LastProvider.Should().BeSameAs(provider);
        sync.LastSettings.Should().BeSameAs(settings);
        sync.LastEntities.Should().ContainSingle().Which.TableName.Should().Be("Customer");
    }

    // FakeConnectionPresenter は共有版（QuickER.Tests.Db.UI.FakeConnectionPresenter）を使用する

    /// <summary>同期ダイアログ提示の呼び出し内容を記録するフェイク</summary>
    private sealed class RecordingSyncPresenter : ISchemaSyncDialogPresenter
    {
        public int ShownCount { get; private set; }

        public IDatabaseProvider? LastProvider { get; private set; }

        public DbConnectionSettings? LastSettings { get; private set; }

        public IReadOnlyList<Entity>? LastEntities { get; private set; }

        public IReadOnlyList<Relationship>? LastRelationships { get; private set; }

        public void Show(
            IDatabaseProvider provider,
            DbConnectionSettings settings,
            IReadOnlyList<Entity> entities,
            IReadOnlyList<Relationship> relationships
        )
        {
            ShownCount++;
            LastProvider = provider;
            LastSettings = settings;
            LastEntities = entities;
            LastRelationships = relationships;
        }
    }
}
