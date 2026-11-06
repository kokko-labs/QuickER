using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ImportSync.ImportSyncOp;
using static QuickER.Tests.Db.UI.ImportSync.ImportSyncState;
using DbStrings = QuickER.Db.UI.Resources.Strings;

namespace QuickER.Tests.Db.UI.ImportSync;

internal static partial class ImportSyncRows
{
    /// <summary>DB 同期のコマンドサービス</summary>
    /// <remarks>
    /// 「確認の間に変わった図」（世代の比較）は取込だけが持ち、同期は持たない：同期は接続を確定した時点の図を目標にする設計で
    /// （src/QuickER.Db.UI/DbSyncCommandService.cs:65「接続確定時点の図をスナップショットして同期の目標スキーマとする」）、
    /// 確認を挟まずに図を 1 回読むだけ。接続ダイアログの最中に図が変わったら、確定の後に読んだ図が目標になる
    /// （行 <c>sync-command-uses-diagram-read-after-connection-confirmed</c>）。
    /// </remarks>
    private static IEnumerable<ScenarioRow<ImportSyncCase>> SyncCommandRows() =>
        [
            CanRunRow(
                "sync-command-sqlite-can-run-with-write-back-tooltip",
                SqliteProvider.ProviderName
            ),
            CanRunRow(
                "sync-command-sqlserver-can-run-with-write-back-tooltip",
                SqlServerProvider.ProviderName
            ),
            Row(
                "sync-command-picker-cancelled-does-not-show-sync-dialog",
                "接続ダイアログを取り消すと同期のダイアログを開かない",
                [(SyncCommand, Pick)],
                h =>
                {
                    var host = ImportSyncHarness.Host(target: SqlServerProvider.ProviderName);
                    var sync = ImportSyncHarness.SyncPresenter();

                    h.RunSyncCommand(host, ImportSyncHarness.Presenter(host), sync);

                    sync.Calls.Should().BeEmpty();
                }
            ),
            Row(
                "sync-command-opens-connection-dialog-in-sync-mode-with-file-creation",
                "同期は接続ダイアログを同期モード・SQLite の新規作成ありで開く",
                [(SyncCommand, Pick)],
                h =>
                {
                    var host = ImportSyncHarness.Host(target: SqliteProvider.ProviderName);
                    var presenter = ImportSyncHarness.Presenter(host);

                    h.RunSyncCommand(host, presenter, ImportSyncHarness.SyncPresenter());

                    presenter.Calls.Single().Mode.Should().Be(DbConnectionDialogMode.Sync);
                    presenter.Calls.Single().AllowSqliteFileCreation.Should().BeTrue();
                }
            ),
            Row(
                "sync-command-resolves-target-dbms-as-fixed-dialect",
                "対象の DBMS をホストで解決できるとき、その方言を同期の接続ダイアログに固定させる",
                [(SyncCommand, Pick)],
                h =>
                {
                    var sqlite = new SqliteProvider();
                    var host = ImportSyncHarness.Host(target: SqliteProvider.ProviderName);
                    host.Registry = new DatabaseProviderRegistry([new SqlServerProvider(), sqlite]);
                    var presenter = ImportSyncHarness.Presenter(host);

                    h.RunSyncCommand(host, presenter, ImportSyncHarness.SyncPresenter());

                    presenter.Calls.Single().FixedProvider.Should().BeSameAs(sqlite);
                    presenter.Calls.Single().Title.Should().Be(DbStrings.Db_SyncTitle);
                }
            ),
            Row(
                "sync-command-confirmed-shows-sync-dialog-with-snapshot",
                "接続を確定すると、確定した方言・接続設定と図のスナップショットで同期のダイアログを開く",
                [(SyncCommand, Apply)],
                h =>
                {
                    var host = ImportSyncHarness.Host(
                        new ErDiagram
                        {
                            Entities = { new Entity { TableName = "Customer" } },
                            TargetDbms = SqlServerProvider.ProviderName,
                        },
                        target: SqlServerProvider.ProviderName
                    );
                    var provider = new SqlServerProvider();
                    var settings = new DbConnectionSettings();
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = new DbConnectionDialogResult(settings, provider);
                    var sync = ImportSyncHarness.SyncPresenter();

                    h.RunSyncCommand(host, presenter, sync);

                    var shown = sync.Calls.Should().ContainSingle().Subject;
                    shown.Provider.Should().BeSameAs(provider);
                    shown.Settings.Should().BeSameAs(settings);
                    shown.Entities.Should().ContainSingle().Which.TableName.Should().Be("Customer");
                }
            ),
            Row(
                "sync-command-uses-diagram-read-after-connection-confirmed",
                "接続ダイアログを開いている間に図が変わったら、確定の後の図を同期の目標にする（世代の比較はしない）",
                [(SyncCommand, Apply)],
                h =>
                {
                    var host = ImportSyncHarness.Host(
                        DiagramWith("Before"),
                        target: SqlServerProvider.ProviderName
                    );
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = new DbConnectionDialogResult(
                        new DbConnectionSettings(),
                        new SqlServerProvider()
                    );
                    presenter.OnShow = () =>
                    {
                        host.Diagram = DiagramWith("After");
                        host.Revision++;
                    };
                    var sync = ImportSyncHarness.SyncPresenter();

                    h.RunSyncCommand(host, presenter, sync);

                    sync.Calls.Should()
                        .ContainSingle()
                        .Which.Entities.Should()
                        .ContainSingle()
                        .Which.TableName.Should()
                        .Be("After");
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                }
            ),
        ];

    /// <summary>対象の DBMS に依らず同期でき、ツールチップが共通の説明である行</summary>
    private static ScenarioRow<ImportSyncCase> CanRunRow(string id, string target) =>
        Row(
            id,
            $"対象の DBMS が {target} でも同期でき、ツールチップは共通の説明",
            [(SyncCommand, Pick)],
            _ =>
            {
                var host = ImportSyncHarness.Host(target: target);
                var service = ImportSyncHarness.SyncCommand(
                    host,
                    ImportSyncHarness.Presenter(host),
                    ImportSyncHarness.SyncPresenter()
                );

                service.CanRun.Should().BeTrue();
                service.CurrentTooltip.Should().Be(DbStrings.Db_SyncWriteBack);
            }
        );
}
