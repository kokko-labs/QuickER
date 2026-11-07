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
    /// <summary>live（Customer(Id)）と、列を 1 本足した目標</summary>
    private static (SchemaImportResult Live, Entity[] Target) AddColumnScenario() =>
        (
            new SchemaImportResult
            {
                Entities = [TableOf("Customer", Col("Id", "INTEGER", pk: true))],
            },
            [TableOf("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT"))]
        );

    /// <summary>同期のダイアログの実行（確認・結果・失敗・実行の最中）</summary>
    private static IEnumerable<ScenarioRow<ImportSyncCase>> SyncExecuteRows() =>
        [
            Row(
                "sync-dialog-execute-confirm-declined-does-not-run",
                "実行の確認を断ると、スクリプトを流さず状態も変えない",
                [(SyncFresh, Confirm)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = false;
                    var provider = ImportSyncHarness.SyncProvider(new SqlServerProvider());
                    var vm = h.CreateSync(provider);
                    vm.ScriptPreview = "DROP TABLE [X];";

                    await h.ExecuteAsync(vm);

                    h.Dialogs.WarningConfirmMessages.Should().ContainSingle();
                    vm.StatusMessage.Should().BeEmpty();
                    vm.IsBusy.Should().BeFalse();
                    provider.Executor.Calls.Should().Be(0);
                }
            ),
            Row(
                "sync-dialog-execute-failure-before-refresh-shows-error-and-refreshes",
                "再計算の前に手で置いたスクリプトの実行が失敗すると、警告つきの確認 1 回の後にエラー 1 件で知らせ、差分を取り直して閉じない",
                [(SyncFresh, Fail)],
                async h =>
                {
                    var provider = ImportSyncHarness.SyncProvider(new SqlServerProvider());
                    provider.Executor.Fail("boom");
                    var vm = h.CreateSync(provider);
                    vm.ScriptPreview = "DROP TABLE [X];";

                    await h.ExecuteAsync(vm);

                    h.Dialogs.WarningConfirmMessages.Should().ContainSingle();
                    h.Dialogs.ConfirmMessages.Should().BeEmpty();
                    h.Dialogs.ErrorMessages.Should()
                        .Equal(DbStrings.SchemaSync_ExecuteFailedMessage + "\n" + "boom");
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                    h.Dialogs.InformationDetailsMessages.Should().BeEmpty();
                    provider.Executor.Calls.Should().Be(1);
                    provider.Importer.Calls.Should().Be(1, "失敗の後に差分を取り直す");
                    h.Closes(vm).Should().BeEmpty("失敗時にダイアログを自動で閉じない");
                    vm.IsBusy.Should().BeFalse();
                    vm.IsExecuting.Should().BeFalse();

                    // 特性の固定（文書に定めが無い今の振る舞い）：取り直しは成功して差分 0 件になり、
                    // 取り直しの末尾の UpdatePreview が空の計画からスクリプトを作り直すので、手で置いたスクリプトは残らない
                    // （src/QuickER.Db.UI/SchemaSyncDialogViewModel.cs:243-251 の TryUpdatePreview → :298-308 の ScriptPreview 代入）
                    vm.ScriptPreview.Should().BeEmpty();
                }
            ),
            Row(
                "sync-dialog-execute-disabled-while-running",
                "実行の最中は実行のコマンドを実行できず（二重に実行しない）、終われば実行できる",
                [(SyncFresh, Apply), (SyncBusy, Apply)],
                async h =>
                {
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider());
                    var gate = provider.Executor.Hold();
                    var vm = h.CreateSync(provider);
                    vm.ScriptPreview = "DROP TABLE [X];";
                    vm.ExecuteCommand.CanExecute(null).Should().BeTrue();

                    var run = h.ExecuteAsync(vm);
                    await gate.Started.Task.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    // 実行が始まってから（＝ボタンを押せる状態に戻っているはずの時点で）見る
                    vm.ExecuteCommand.CanExecute(null)
                        .Should()
                        .BeFalse("実行中はボタンから再実行できないこと");

                    gate.Release.SetResult();
                    await run.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    vm.ExecuteCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "sync-dialog-execute-rebuild-dialect-alter-column-confirms-rebuild",
                "再構築の方言で型の変更を選んだ実行の確認は再構築の文言で、再構築する表を詳細へ 1 行 1 表で並べる",
                [(SyncDiff, Confirm)],
                async h =>
                {
                    var live = new SchemaImportResult
                    {
                        Entities =
                        [
                            TableOf(
                                "Customer",
                                Col("Id", "INTEGER", pk: true),
                                Col("Age", "INTEGER")
                            ),
                        ],
                    };
                    var settings = new DbConnectionSettings { FilePath = "shop.db" };
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqliteProvider(), live),
                        settings,
                        [TableOf("Customer", Col("Id", "INTEGER", pk: true), Col("Age", "TEXT"))]
                    );

                    await h.RefreshAsync(vm);
                    // 型の変更は既定で未選択なので、明示的に選んで再構築の計画へ載せる
                    vm.DiffItems.Single(i => i.Kind == SchemaDiffKind.AlterColumn).IsSelected =
                        true;

                    await h.ExecuteAsync(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    var (message, details, _) = h
                        .Dialogs.WarningConfirmDetailsMessages.Should()
                        .ContainSingle()
                        .Subject;
                    message
                        .Should()
                        .Be(
                            string.Format(
                                DbStrings.SchemaSync_ExecuteConfirmRebuild,
                                settings.FilePath,
                                1
                            )
                        );
                    details
                        .Should()
                        .Be(
                            DbStrings.SchemaSync_ExecuteConfirmRebuildTablesHeader
                                + Environment.NewLine
                                + "  • Customer"
                        );
                }
            ),
            Row(
                "sync-dialog-execute-rebuild-dialect-add-column-only-confirms-normally",
                "再構築の方言でも、再構築の要らない差分（列の追加だけ）の実行の確認は通常の文言",
                [(SyncDiff, Confirm)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var settings = new DbConnectionSettings { FilePath = "shop.db" };
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqliteProvider(), live),
                        settings,
                        target
                    );

                    await h.RefreshAsync(vm);
                    vm.UpdatePreview();

                    await h.ExecuteAsync(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(DbStrings.SchemaSync_ExecuteConfirm, settings.FilePath));
                }
            ),
            Row(
                "sync-dialog-execute-rebuild-dialect-confirm-shows-file-path",
                "SQLite の実行の確認は、接続設定に残った別方言のデータベース名でなくファイルパスを出す",
                [(SyncDiff, Confirm)],
                async h =>
                {
                    var live = new SchemaImportResult
                    {
                        Entities = [TableOf("Customer", Col("Id", "INTEGER", pk: true))],
                    };
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqliteProvider(), live),
                        new DbConnectionSettings
                        {
                            Database = "OldSqlServerDb",
                            FilePath = @"C:\data\shop.db",
                        },
                        [TableOf("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT"))]
                    );

                    await h.RefreshAsync(vm);
                    vm.UpdatePreview();

                    await h.ExecuteAsync(vm);

                    var message = h.Dialogs.WarningConfirmMessages.Should().ContainSingle().Subject;
                    message
                        .Should()
                        .Be(string.Format(DbStrings.SchemaSync_ExecuteConfirm, @"C:\data\shop.db"));
                    message.Should().NotContain("OldSqlServerDb");
                }
            ),
            Row(
                "sync-dialog-execute-server-dialect-confirm-shows-database",
                "サーバー系の方言の実行の確認は、接続設定に残った SQLite のファイルパスでなくデータベース名を出す",
                [(SyncFresh, Confirm)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqlServerProvider()),
                        new DbConnectionSettings
                        {
                            Database = "Shop",
                            FilePath = @"C:\data\stale.db",
                        }
                    );
                    vm.ScriptPreview = "ALTER TABLE [X] ADD [Y] int NULL;";

                    await h.ExecuteAsync(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(DbStrings.SchemaSync_ExecuteConfirm, "Shop"));
                }
            ),
            Row(
                "sync-dialog-execute-success-without-remaining-diff-closes",
                "実行の成功の後に取り直した差分が 0 件なら、ダイアログを自動で閉じる",
                [(SyncDiff, Apply)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { Database = "shop.db" },
                        target
                    );

                    await h.RefreshAsync(vm);
                    vm.DiffItems.Should().NotBeEmpty();
                    // 実行の後の取込は「適用済み＝目標と同じ」の live を返す
                    provider.Importer.Result = new SchemaImportResult
                    {
                        Entities =
                        [
                            TableOf(
                                "Customer",
                                Col("Id", "INTEGER", pk: true),
                                Col("Name", "TEXT")
                            ),
                        ],
                    };

                    await h.ExecuteAsync(vm);

                    vm.DiffItems.Should().BeEmpty();
                    h.Closes(vm).Should().Equal(true);
                }
            ),
            Row(
                "sync-dialog-execute-success-with-remaining-diff-stays-open",
                "実行の成功の後も差分が残れば、ダイアログを閉じない",
                [(SyncDiff, Apply)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqliteProvider(), live),
                        new DbConnectionSettings { Database = "shop.db" },
                        target
                    );

                    await h.RefreshAsync(vm);
                    await h.ExecuteAsync(vm);

                    vm.DiffItems.Should().NotBeEmpty();
                    h.Closes(vm).Should().BeEmpty();
                }
            ),
            Row(
                "sync-dialog-execute-success-then-refresh-failure-stays-open-and-shows-error",
                "実行の成功の後の取り直しが失敗したら、差分 0 件と読んで閉じずに、失敗をモーダルで知らせ、古いスクリプトを残さない",
                [(SyncDiff, Apply), (SyncDiff, Fail)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );
                    await h.RefreshAsync(vm);
                    vm.DiffItems.Should().NotBeEmpty();
                    // 実行はコミットし、その後の取り直しだけが失敗する
                    provider.Importer.Fail("network down");

                    await h.ExecuteAsync(vm);

                    h.Closes(vm)
                        .Should()
                        .BeEmpty("取り直せなかったのを「図と DB が一致した」と読まない");
                    h.Dialogs.InformationMessages.Should().ContainSingle();
                    h.Dialogs.ErrorMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(
                            DbStrings.SchemaSync_RefreshAfterCommitFailedMessage + "\nnetwork down"
                        );
                    vm.StatusMessage.Should()
                        .Be(string.Format(DbStrings.SchemaSync_DiffFailed, "network down"));
                    vm.ScriptPreview.Should().BeEmpty();

                    await h.ExecuteAsync(vm);

                    provider
                        .Executor.Calls.Should()
                        .Be(1, "取り直せなかった古いスクリプトは流さない");
                }
            ),
            Row(
                "sync-dialog-execute-foreign-key-rebuild-risk-in-confirm-details",
                "主キーの変更で被参照列が候補キーでなくなるとき、実行の確認の詳細に外部キーの作り直しの注意を載せる",
                [(SyncDiff, Confirm)],
                async h =>
                {
                    var parent = TableOf("parent", Col("id", "int", pk: true), Col("code", "int"));
                    var child = TableOf(
                        "child",
                        Col("id", "int", pk: true),
                        Col("parent_id", "int")
                    );
                    var live = new SchemaImportResult
                    {
                        Entities = [parent, child],
                        Relationships =
                        [
                            new Relationship
                            {
                                SourceEntityId = parent.Id,
                                TargetEntityId = child.Id,
                                Type = RelationshipType.OneToMany,
                                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                                ConstraintName = "FK_child_parent",
                            },
                        ],
                    };
                    // 目標：親の主キーを id から code へ移す（子の外部キーは id を参照したまま）
                    var parentTarget = parent.Clone(preserveId: true);
                    parentTarget.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
                    parentTarget.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
                    var childTarget = child.Clone(preserveId: true);
                    var keep = new Relationship
                    {
                        SourceEntityId = parentTarget.Id,
                        TargetEntityId = childTarget.Id,
                        Type = RelationshipType.OneToMany,
                        ColumnPairs =
                        [
                            new(
                                parentTarget.Columns.Single(c => c.Name == "id").Id,
                                childTarget.Columns.Single(c => c.Name == "parent_id").Id
                            ),
                        ],
                        ConstraintName = "FK_child_parent",
                    };
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqlServerProvider(), live),
                        new DbConnectionSettings { Database = "shop" },
                        [parentTarget, childTarget],
                        [keep]
                    );

                    await h.RefreshAsync(vm);
                    vm.DiffItems.Single(i => i.Kind == SchemaDiffKind.AlterPrimaryKey).IsSelected =
                        true;

                    await h.ExecuteAsync(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    var (message, details, _) = h
                        .Dialogs.WarningConfirmDetailsMessages.Should()
                        .ContainSingle()
                        .Subject;
                    message
                        .Should()
                        .Be(string.Format(DbStrings.SchemaSync_ExecuteConfirmDestructive, "shop"));
                    details
                        .Should()
                        .Be(
                            string.Format(
                                DbStrings.SchemaSync_ExecuteConfirmForeignKeyRebuildRisk,
                                1
                            )
                        );
                }
            ),
            Row(
                "sync-dialog-type-kind-change-with-default-warns-from-imported-defaults",
                "取込で分かった列の既定値が計画へ渡り、既定値のある列の型の種類の変更を実行の確認の詳細に出す",
                [(SyncDiff, Confirm)],
                async h =>
                {
                    var live = new SchemaImportResult
                    {
                        Entities =
                        [
                            TableOf("item", Col("id", "int", pk: true), Col("v", "varchar(20)")),
                        ],
                        ColumnDefaults = [new SchemaColumnDefault("item", "v", "('abc')")],
                    };
                    var target = live.Entities[0].Clone(preserveId: true);
                    target.Columns.Single(c => c.Name == "v").DataType = "int";
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqlServerProvider(), live),
                        new DbConnectionSettings { Database = "shop" },
                        [target]
                    );

                    await h.RefreshAsync(vm);
                    vm.DiffItems.Single(i => i.Kind == SchemaDiffKind.AlterColumn).IsSelected =
                        true;
                    await h.ExecuteAsync(vm);

                    h.Dialogs.WarningConfirmDetailsMessages.Should()
                        .ContainSingle()
                        .Which.Details.Should()
                        .Contain("item / v (varchar(20) → int)");
                }
            ),
            Row(
                "sync-dialog-execute-rebuild-dialect-composite-fk-table-is-rebuilt",
                "再構築の方言（SQLite）で複合外部キーの子の表も再構築し、作り直した外部キーは全構成列を保つ",
                [(SyncDiff, Confirm)],
                async h =>
                {
                    var orders = TableOf(
                        "orders",
                        Col("Id", "INTEGER", pk: true),
                        Col("LineNo", "INTEGER")
                    );
                    var orderLine = TableOf(
                        "order_line",
                        Col("Id", "INTEGER", pk: true),
                        Col("OrderId", "INTEGER"),
                        Col("LineNo", "INTEGER"),
                        Col("Note", "TEXT")
                    );
                    var live = new SchemaImportResult
                    {
                        Entities = [orders, orderLine],
                        Relationships =
                        [
                            new Relationship
                            {
                                SourceEntityId = orders.Id,
                                TargetEntityId = orderLine.Id,
                                Type = RelationshipType.OneToMany,
                                ColumnPairs =
                                [
                                    new(orders.Columns[0].Id, orderLine.Columns[1].Id),
                                    new(orders.Columns[1].Id, orderLine.Columns[2].Id),
                                ],
                                ConstraintName = "FK_order_line_orders",
                            },
                        ],
                    };
                    // 目標：子の表の Note の型を変える（＝子の表の再構築が要る）
                    var ordersTarget = orders.Clone(preserveId: true);
                    var orderLineTarget = orderLine.Clone(preserveId: true);
                    orderLineTarget.Columns.Single(c => c.Name == "Note").DataType = "INTEGER";
                    var targetRel = new Relationship
                    {
                        SourceEntityId = ordersTarget.Id,
                        TargetEntityId = orderLineTarget.Id,
                        Type = RelationshipType.OneToMany,
                        ColumnPairs =
                        [
                            new(ordersTarget.Columns[0].Id, orderLineTarget.Columns[1].Id),
                            new(ordersTarget.Columns[1].Id, orderLineTarget.Columns[2].Id),
                        ],
                        ConstraintName = "FK_order_line_orders",
                    };
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqliteProvider(), live),
                        new DbConnectionSettings { FilePath = "shop.db" },
                        [ordersTarget, orderLineTarget],
                        [targetRel]
                    );

                    await h.RefreshAsync(vm);
                    vm.DiffItems.Single(i => i.Kind == SchemaDiffKind.AlterColumn).IsSelected =
                        true;

                    await h.ExecuteAsync(vm);

                    var (message, details, _) = h
                        .Dialogs.WarningConfirmDetailsMessages.Should()
                        .ContainSingle()
                        .Subject;
                    message
                        .Should()
                        .Be(
                            string.Format(DbStrings.SchemaSync_ExecuteConfirmRebuild, "shop.db", 1)
                        );
                    details.Should().Contain("  • order_line");
                    vm.ScriptPreview.Should()
                        .Contain(
                            "FOREIGN KEY (\"OrderId\", \"LineNo\") REFERENCES \"orders\" (\"Id\", \"LineNo\")"
                        );
                    vm.ScriptPreview.Should().NotContain("Skipped");
                }
            ),
            Row(
                "sync-dialog-execute-failure-refreshes-diff-after-error",
                "実行の失敗でもエラーを出した後に差分を取り直し（部分適用への追従）、ダイアログを閉じない",
                [(SyncDiff, Fail)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    // 実行は失敗するが、部分適用で Name 列だけは適用済みになった状態を再現する
                    provider.Executor.Fail(
                        "statement 1 failed",
                        () =>
                            provider.Importer.Result = new SchemaImportResult
                            {
                                Entities =
                                [
                                    TableOf(
                                        "Customer",
                                        Col("Id", "INTEGER", pk: true),
                                        Col("Name", "TEXT")
                                    ),
                                ],
                            }
                    );
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );

                    await h.RefreshAsync(vm);
                    vm.DiffItems.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddColumn);

                    await h.ExecuteAsync(vm);

                    h.Dialogs.ErrorMessages.Should().ContainSingle();
                    vm.DiffItems.Should()
                        .BeEmpty("失敗後も取り直すため、部分適用済みの列は差分から消える");
                    h.Closes(vm).Should().BeEmpty("失敗時にダイアログを自動で閉じない");
                }
            ),
            Row(
                "sync-dialog-refresh-failure-clears-stale-script",
                "再計算が失敗したら前回の差分とスクリプトを捨て、実行しても古いスクリプトを流さない",
                [(SyncDiff, Fail), (SyncFresh, Apply)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );
                    await h.RefreshAsync(vm);
                    vm.ScriptPreview.Should().NotBeEmpty();

                    provider.Importer.Fail("network down");
                    await h.RefreshAsync(vm);

                    vm.DiffItems.Should().BeEmpty();
                    vm.HasDiff.Should().BeFalse();
                    vm.ScriptPreview.Should().BeEmpty();
                    vm.StatusMessage.Should()
                        .Be(string.Format(DbStrings.SchemaSync_DiffFailed, "network down"));

                    await h.ExecuteAsync(vm);

                    provider
                        .Executor.Calls.Should()
                        .Be(0, "取り直せなかった古いスクリプトは流さない");
                    vm.StatusMessage.Should().Be(DbStrings.SchemaSync_NoScript);
                }
            ),
            Row(
                "sync-dialog-execute-failure-then-refresh-failure-cannot-rerun",
                "実行の失敗の後の取り直しも失敗したら、古いスクリプトを再実行できない",
                [(SyncDiff, Fail)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    provider.Executor.Fail(
                        "statement 1 failed",
                        () => provider.Importer.Fail("network down")
                    );
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );
                    await h.RefreshAsync(vm);

                    await h.ExecuteAsync(vm);
                    await h.ExecuteAsync(vm);

                    provider.Executor.Calls.Should().Be(1);
                    vm.ScriptPreview.Should().BeEmpty();
                    vm.IsBusy.Should().BeFalse();
                }
            ),
            Row(
                "sync-dialog-execute-executor-throws-shows-error-and-refreshes",
                "実行器の例外もモーダルで知らせて取り直し、取り直しの失敗の後は再実行できない",
                [(SyncDiff, Fail)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    provider.Executor.Throw(
                        "connection lost",
                        () => provider.Importer.Fail("network down")
                    );
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );
                    await h.RefreshAsync(vm);

                    await h.ExecuteAsync(vm);

                    h.Dialogs.ErrorMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain("connection lost");
                    provider.Importer.Calls.Should().Be(2, "失敗の後に差分を取り直す");
                    vm.ScriptPreview.Should()
                        .BeEmpty("取り直しも失敗したので古いスクリプトを残さない");
                    vm.IsBusy.Should().BeFalse();
                    vm.IsExecuting.Should().BeFalse();

                    await h.ExecuteAsync(vm);

                    provider.Executor.Calls.Should().Be(1, "古いスクリプトは再実行できない");
                }
            ),
            Row(
                "sync-dialog-refresh-in-progress-disables-operations-but-not-close",
                "再計算の最中は実行・全選択・全解除を受け付けず（直接の実行も古いスクリプトを流さず、最中の再計算は DB を読み直さない）、閉じることはできる",
                [
                    (SyncBusy, Select),
                    (SyncBusy, Apply),
                    (SyncBusy, Close),
                    (SyncBusy, Refresh),
                    (SyncDiff, Refresh),
                ],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );
                    await h.RefreshAsync(vm);

                    var gate = provider.Importer.Hold();
                    var refreshing = h.RefreshAsync(vm);

                    vm.IsBusy.Should().BeTrue();
                    vm.ExecuteCommand.CanExecute(null).Should().BeFalse();
                    vm.SelectAllCommand.CanExecute(null).Should().BeFalse();
                    vm.DeselectAllCommand.CanExecute(null).Should().BeFalse();
                    vm.CloseCommand.CanExecute(null).Should().BeTrue("読み直しの最中は閉じてよい");

                    // ボタンを通らない直接の呼び出しも、古いスクリプトを流さない
                    await h.ExecuteAsync(vm);
                    provider.Executor.Calls.Should().Be(0);

                    // 最中の再計算は DB を読み直さない（SchemaSyncDialogViewModel.cs の RefreshAsync の IsBusy の門）
                    // 門の前で取込が止まっているので、門を開ける前に 2 本目を待つと門の退行がハングになる。
                    // 2 本目は待たずに始め、取込の呼び出し回数を同期的に見てから、門を開けて 2 本とも終える
                    var importCalls = provider.Importer.Calls;
                    var secondRefresh = h.RefreshAsync(vm);
                    provider
                        .Importer.Calls.Should()
                        .Be(importCalls, "最中の再計算は取込を呼ばない");

                    gate.SetResult(live);
                    await Task.WhenAll(refreshing, secondRefresh)
                        .WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

                    vm.ExecuteCommand.CanExecute(null).Should().BeTrue();
                    vm.SelectAllCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "sync-dialog-execute-in-progress-cannot-close",
                "同期の実行中は閉じられず、実行が終われば閉じられる",
                [(SyncBusy, Close)],
                async h =>
                {
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider());
                    var gate = provider.Executor.Hold();
                    var vm = h.CreateSync(provider);
                    vm.ScriptPreview = "DROP TABLE [X];";

                    var run = h.ExecuteAsync(vm);
                    await gate.Started.Task.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    vm.IsExecuting.Should().BeTrue();
                    vm.CloseCommand.CanExecute(null).Should().BeFalse();
                    h.Close(vm);
                    h.Closes(vm).Should().BeEmpty("DDL の実行中にダイアログを閉じない");

                    gate.Release.SetResult();
                    await run.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    vm.IsExecuting.Should().BeFalse();
                    vm.CloseCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "sync-dialog-refresh-failure-while-in-progress-clears-diff-and-script",
                "再計算の最中に取込が例外で失敗したら、最中は操作できず、失敗の後は前回の差分とスクリプトを捨て、実行しても古いスクリプトを流さない",
                [(SyncBusy, Fail), (SyncBusy, Apply)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );
                    await h.RefreshAsync(vm);
                    vm.DiffItems.Should().NotBeEmpty();
                    vm.ScriptPreview.Should().NotBeEmpty();

                    var gate = provider.Importer.Hold();
                    var refreshing = h.RefreshAsync(vm);

                    vm.IsBusy.Should().BeTrue();
                    vm.ExecuteCommand.CanExecute(null).Should().BeFalse();

                    gate.SetException(new InvalidOperationException("network down"));
                    await refreshing.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    vm.IsBusy.Should().BeFalse();
                    vm.DiffItems.Should().BeEmpty();
                    vm.HasDiff.Should().BeFalse();
                    vm.ScriptPreview.Should().BeEmpty();
                    vm.StatusMessage.Should()
                        .Be(string.Format(DbStrings.SchemaSync_DiffFailed, "network down"));
                    h.Dialogs.ErrorMessages.Should()
                        .BeEmpty("取り直しの失敗はステータス行だけで知らせる");
                    h.Dialogs.InformationMessages.Should().BeEmpty();

                    await h.ExecuteAsync(vm);

                    provider
                        .Executor.Calls.Should()
                        .Be(0, "取り直せなかった古いスクリプトは流さない");
                }
            ),
            Row(
                "sync-dialog-refresh-during-execute-does-not-reread-database",
                "実行の最中は再計算を受け付けず、直接呼んでも DB を読み直さない（実行中の取り直しで実行の結果を乱さない）",
                [(SyncBusy, Refresh), (SyncBusy, Apply)],
                async h =>
                {
                    var (live, target) = AddColumnScenario();
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );
                    await h.RefreshAsync(vm);
                    vm.ScriptPreview.Should().NotBeEmpty();

                    var gate = provider.Executor.Hold();
                    var executing = h.ExecuteAsync(vm);
                    await gate.Started.Task.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    vm.IsBusy.Should().BeTrue();
                    vm.RefreshCommand.CanExecute(null).Should().BeFalse();

                    // ボタンを通らない直接の呼び出しも DB を読み直さない
                    // （SchemaSyncDialogViewModel.cs の RefreshAsync の IsBusy の門。IsBusy の XmlDoc の 2 つの危険のうち実行中の再計算）
                    var importCalls = provider.Importer.Calls;
                    await h.RefreshAsync(vm);
                    provider
                        .Importer.Calls.Should()
                        .Be(importCalls, "実行の最中の再計算は取込を呼ばない");

                    gate.Release.SetResult();
                    await executing.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    provider.Executor.Calls.Should().Be(1);
                }
            ),
        ];
}
