using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ImportSync.ImportSyncOp;
using static QuickER.Tests.Db.UI.ImportSync.ImportSyncState;
using DbStrings = QuickER.Db.UI.Resources.Strings;

namespace QuickER.Tests.Db.UI.ImportSync;

internal static partial class ImportSyncRows
{
    /// <summary>同期のダイアログの差分の選択・プレビュー・再計算</summary>
    private static IEnumerable<ScenarioRow<ImportSyncCase>> SyncPreviewRows() =>
        [
            Row(
                "sync-dialog-select-all-selects-only-selectable-items",
                "全選択は選択できる差分だけを選び、案内の項目を選ばない",
                [(SyncFresh, Select)],
                h =>
                {
                    var vm = h.CreateSync(ImportSyncHarness.SyncProvider(new SqlServerProvider()));
                    vm.DiffItems.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddTable,
                            TableName = "Customer",
                            Entity = TableOf("Customer", Col("Id", "int", pk: true)),
                            IsSelected = false,
                            IsSelectable = true,
                        }
                    );
                    vm.DiffItems.Add(InformationalReorderItem());

                    h.SelectAll(vm);

                    vm.DiffItems[0].IsSelected.Should().BeTrue();
                    vm.DiffItems[1].IsSelected.Should().BeFalse();
                }
            ),
            Row(
                "sync-dialog-deselect-all-keeps-informational-items",
                "全解除は選択できない案内の項目の状態を変えない",
                [(SyncFresh, Select)],
                h =>
                {
                    var vm = h.CreateSync(ImportSyncHarness.SyncProvider(new SqlServerProvider()));
                    vm.DiffItems.Add(AddNameColumnItem());
                    vm.DiffItems.Add(InformationalReorderItem());

                    h.DeselectAll(vm);

                    vm.DiffItems[0].IsSelected.Should().BeFalse();
                    vm.DiffItems[1].IsSelected.Should().BeFalse();
                }
            ),
            Row(
                "sync-dialog-preview-ignores-informational-items",
                "スクリプトのプレビューは選択した通常の差分だけを出し、案内の項目を出さない",
                [(SyncFresh, Select)],
                h =>
                {
                    var vm = h.CreateSync(ImportSyncHarness.SyncProvider(new SqlServerProvider()));
                    vm.DiffItems.Add(AddNameColumnItem());
                    vm.DiffItems.Add(InformationalReorderItem());

                    vm.UpdatePreview();

                    vm.ScriptPreview.Should()
                        .Contain("ALTER TABLE [Customer] ADD [Name] nvarchar(50) NULL;");
                    vm.ScriptPreview.Should().NotContain("RebuildTable");
                    vm.ScriptPreview.Should().NotContain("列順変更は DB 同期しません");
                }
            ),
            Row(
                "sync-dialog-preview-before-refresh-on-rebuild-dialect-is-empty",
                "再構築の方言でも、再計算の前のプレビューは例外にならず空",
                [(SyncFresh, Select)],
                h =>
                {
                    var vm = h.CreateSync(ImportSyncHarness.SyncProvider(new SqliteProvider()));

                    var act = vm.UpdatePreview;

                    act.Should().NotThrow();
                    vm.ScriptPreview.Should().BeEmpty();
                }
            ),
            Row(
                "sync-dialog-preview-generation-failure-reports-status",
                "スクリプトの組み立てが失敗する差分（改行入りのテーブル名）でも、プレビューは例外を出さずに状態の文へ出す",
                [(SyncFresh, Select)],
                h =>
                {
                    var vm = h.CreateSync(ImportSyncHarness.SyncProvider(new SqlServerProvider()));
                    vm.DiffItems.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddTable,
                            TableName = "a\nDROP TABLE users; --",
                            Entity = TableOf("a\nDROP TABLE users; --", Col("Id", "int", pk: true)),
                            IsSelected = true,
                            IsSelectable = true,
                        }
                    );

                    var act = () => vm.UpdatePreview();

                    act.Should().NotThrow();
                    vm.ScriptPreview.Should().BeEmpty();
                    vm.StatusMessage.Should().Contain("DROP TABLE users");
                }
            ),
            Row(
                "sync-dialog-refresh-passes-command-timeout-from-settings",
                "接続設定のコマンドタイムアウトが差分の取込まで届く",
                [(SyncFresh, Refresh)],
                async h =>
                {
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider());
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings
                        {
                            FilePath = "shop.db",
                            CommandTimeoutSeconds = 180,
                        }
                    );

                    await h.RefreshAsync(vm);

                    provider.Importer.LastCommandTimeoutSeconds.Should().Be(180);
                }
            ),
            Row(
                "sync-dialog-refresh-rebuild-dialect-suppresses-description-only-diff",
                "再構築の方言（SQLite）は説明だけの差分を出さない（方言の能力が差分へ渡る）",
                [(SyncFresh, Refresh)],
                async h =>
                {
                    var live = new SchemaImportResult
                    {
                        Entities = [TableOf("Customer", Col("Id", "INTEGER", pk: true))],
                    };
                    var targetTable = TableOf("Customer", Col("Id", "INTEGER", pk: true));
                    targetTable.Description = "顧客マスタ";
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqliteProvider(), live),
                        target: [targetTable]
                    );

                    await h.RefreshAsync(vm);

                    vm.DiffItems.Should().BeEmpty();
                    vm.HasDiff.Should().BeFalse();
                }
            ),
            ReorderRow(
                "sync-dialog-refresh-rebuild-dialect-column-reorder-is-selectable",
                new SqliteProvider(),
                selectable: true
            ),
            ReorderRow(
                "sync-dialog-refresh-native-dialect-column-reorder-is-selectable",
                new MySqlProvider(),
                selectable: true
            ),
            ReorderRow(
                "sync-dialog-refresh-none-dialect-column-reorder-is-informational",
                new SqlServerProvider(),
                selectable: false
            ),
            Row(
                "sync-dialog-refresh-composite-fk-diff-stays-selectable",
                "複合外部キーの差分は案内へ格下げされず、選べばスクリプトへ載る",
                [(SyncFresh, Refresh), (SyncDiff, Select)],
                async h =>
                {
                    var (live, target) = CompositeForeignKeyScenario();
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqlServerProvider(), live),
                        target: target
                    );

                    await h.RefreshAsync(vm);

                    vm.DiffItems.Should().OnlyContain(i => i.Kind == SchemaDiffKind.DropForeignKey);
                    var dropFk = vm.DiffItems.Should().ContainSingle().Which;
                    dropFk.IsSelectable.Should().BeTrue();

                    // DropForeignKey は破壊的のため既定では未選択。選択すればスクリプトへ載る
                    h.SelectAll(vm);
                    dropFk.IsSelected.Should().BeTrue();
                    vm.ScriptPreview.Should().Contain("FK_child_parent");
                }
            ),
            Row(
                "sync-dialog-refresh-composite-fk-key-and-column-changes-stay-selectable",
                "複合外部キーの参照先の主キーの変更と構成列の型の変更も選べ、主キーを選ぶと外部キーを全構成列のまま作り直す",
                [(SyncFresh, Refresh), (SyncDiff, Select)],
                async h =>
                {
                    var (live, target, targetRelationships) = CompositeForeignKeyChangeScenario();
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqlServerProvider(), live),
                        target: target,
                        relationships: targetRelationships
                    );

                    await h.RefreshAsync(vm);

                    vm.DiffItems.Should().OnlyContain(i => i.IsSelectable);
                    var alterPk = vm
                        .DiffItems.Should()
                        .ContainSingle(i => i.Kind == SchemaDiffKind.AlterPrimaryKey)
                        .Which;
                    // 列の変更は parent_id の型と、新しく主キーに加わる code の NOT NULL 化（NULL 許容は実効の値で比べる）
                    vm.DiffItems.Where(i => i.Kind == SchemaDiffKind.AlterColumn)
                        .Select(i => i.ColumnName)
                        .Should()
                        .BeEquivalentTo(["parent_id", "code"]);
                    vm.DiffItems.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddColumn);

                    alterPk.IsSelected = true;
                    vm.ScriptPreview.Should().Contain("PRIMARY KEY");
                    vm.ScriptPreview.Should()
                        .Contain(
                            "FOREIGN KEY ([parent_id], [order_no]) REFERENCES [parent] ([id], [code])"
                        );
                }
            ),
            Row(
                "sync-dialog-refresh-composite-fk-secondary-column-stays-selectable",
                "複合外部キーの 2 列目の構成列の型の変更も選べ、選ぶと外部キーを全構成列のまま作り直す",
                [(SyncFresh, Refresh), (SyncDiff, Select)],
                async h =>
                {
                    var (live, target, targetRelationships) = CompositeForeignKeyChangeScenario();
                    // 第 2 構成列も型を変え、参照先の 2 列も同じ型へ揃える（両端の型が食い違う外部キーは外し直さないため）
                    var childTarget = target.Single(e => e.TableName == "child");
                    childTarget.Columns.Single(c => c.Name == "order_no").DataType = "bigint";
                    var parentTarget = target.Single(e => e.TableName == "parent");
                    parentTarget.Columns.Single(c => c.Name == "id").DataType = "bigint";
                    parentTarget.Columns.Single(c => c.Name == "code").DataType = "bigint";
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqlServerProvider(), live),
                        target: target,
                        relationships: targetRelationships
                    );

                    await h.RefreshAsync(vm);

                    var alterColumns = vm
                        .DiffItems.Where(i => i.Kind == SchemaDiffKind.AlterColumn)
                        .ToList();
                    alterColumns
                        .Select(i => $"{i.TableName}.{i.ColumnName}")
                        .Should()
                        .BeEquivalentTo(
                            "child.parent_id",
                            "child.order_no",
                            "parent.id",
                            "parent.code"
                        );
                    alterColumns.Should().OnlyContain(i => i.IsSelectable);

                    h.SelectAll(vm);
                    alterColumns.Should().OnlyContain(i => i.IsSelected);
                    vm.ScriptPreview.Should()
                        .Contain(
                            "FOREIGN KEY ([parent_id], [order_no]) REFERENCES [parent] ([id], [code])"
                        );
                }
            ),
            Row(
                "sync-dialog-refresh-preview-failure-keeps-reason",
                "再計算でプレビューの組み立てが失敗したら、その理由を件数の表示で上書きしない",
                [(SyncFresh, Refresh)],
                async h =>
                {
                    // 改行入りのテーブル名の追加は入口の名前の検証で断られる（既定で選ばれる差分）
                    var bad = TableOf("a\nDROP TABLE users; --", Col("Id", "INTEGER", pk: true));
                    var vm = h.CreateSync(
                        ImportSyncHarness.SyncProvider(new SqliteProvider()),
                        new DbConnectionSettings { FilePath = "shop.db" },
                        [bad]
                    );

                    await h.RefreshAsync(vm);

                    vm.DiffItems.Should().NotBeEmpty();
                    vm.ScriptPreview.Should().BeEmpty();
                    vm.StatusMessage.Should()
                        .StartWith(DbStrings.SchemaSync_PreviewFailed.Split("{0}")[0])
                        .And.Contain("DROP TABLE users");
                }
            ),
            Row(
                "sync-dialog-select-all-and-deselect-all-build-script-once",
                "全選択・全解除はスクリプトを 1 回だけ組み直す（項目ごとに組み直さない）",
                [(SyncDiff, Select)],
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
                    var target = new[]
                    {
                        TableOf(
                            "Customer",
                            Col("Id", "INTEGER", pk: true),
                            Col("Age", "TEXT"),
                            Col("A", "TEXT"),
                            Col("B", "TEXT"),
                            Col("C", "TEXT")
                        ),
                    };
                    var provider = ImportSyncHarness.SyncProvider(new SqliteProvider(), live);
                    var vm = h.CreateSync(
                        provider,
                        new DbConnectionSettings { FilePath = "shop.db" },
                        target
                    );
                    await h.RefreshAsync(vm);
                    vm.DiffItems.Count(i => i.IsSelectable).Should().BeGreaterThanOrEqualTo(4);

                    var before = provider.BuildCount;
                    h.DeselectAll(vm);
                    provider.BuildCount.Should().Be(before + 1);

                    h.SelectAll(vm);
                    provider.BuildCount.Should().Be(before + 2);
                    vm.DiffItems.Where(i => i.IsSelectable).Should().OnlyContain(i => i.IsSelected);
                }
            ),
            Row(
                "sync-dialog-close-when-idle-closes",
                "実行中でなければ閉じるで閉じる",
                [(SyncFresh, Close)],
                h =>
                {
                    var vm = h.CreateSync(ImportSyncHarness.SyncProvider(new SqlServerProvider()));

                    h.Close(vm);

                    h.Closes(vm).Should().Equal(true);
                }
            ),
            Row(
                "sync-dialog-close-after-refresh-closes-without-confirm",
                "特性の固定（文書に定めが無い今の振る舞い）：差分を再計算した後（未実行のスクリプトがあるとき）も、確認を出さずに閉じる",
                [(SyncDiff, Close)],
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

                    h.Close(vm);

                    // 未実行のスクリプトを捨てることを確認しない（src/QuickER.Db.UI/SchemaSyncDialogViewModel.cs:672-680 の Close は
                    // IsExecuting の門と CloseAction(true) だけ）。文書に定めができたら書き直す
                    h.Closes(vm).Should().Equal(true);
                    h.Dialogs.ConfirmMessages.Should().BeEmpty();
                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    h.Dialogs.WarningConfirmDetailsMessages.Should().BeEmpty();
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                    h.Dialogs.InformationDetailsMessages.Should().BeEmpty();
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                    provider.Executor.Calls.Should().Be(0);
                }
            ),
        ];

    /// <summary>列順だけを入れ替えた差分の扱いが方言の能力で決まる行</summary>
    private static ScenarioRow<ImportSyncCase> ReorderRow(
        string id,
        IDatabaseProvider dialect,
        bool selectable
    ) =>
        Row(
            id,
            selectable
                ? $"{dialect.Name} では列順の変更が選択できる ReorderColumns の項目になり、案内の項目を重ねない"
                : $"{dialect.Name} では列順の変更が選択できない案内の項目（RebuildTable）になる",
            [(SyncFresh, Refresh)],
            async h =>
            {
                var live = new SchemaImportResult
                {
                    Entities =
                    [
                        TableOf(
                            "Customer",
                            Col("Id", "INTEGER", pk: true),
                            Col("Name", "TEXT"),
                            Col("Email", "TEXT")
                        ),
                    ],
                };
                var target = new[]
                {
                    TableOf(
                        "Customer",
                        Col("Id", "INTEGER", pk: true),
                        Col("Email", "TEXT"),
                        Col("Name", "TEXT")
                    ),
                };
                var vm = h.CreateSync(
                    ImportSyncHarness.SyncProvider(dialect, live),
                    target: target
                );

                await h.RefreshAsync(vm);

                if (selectable)
                {
                    var reorder = vm
                        .DiffItems.Should()
                        .ContainSingle(i => i.Kind == SchemaDiffKind.ReorderColumns)
                        .Which;
                    reorder.IsSelectable.Should().BeTrue();
                    reorder.IsSelected.Should().BeFalse();
                    vm.DiffItems.Should().NotContain(i => i.Kind == SchemaDiffKind.RebuildTable);
                }
                else
                {
                    var info = vm
                        .DiffItems.Should()
                        .ContainSingle(i => i.Kind == SchemaDiffKind.RebuildTable)
                        .Which;
                    info.IsSelectable.Should().BeFalse();
                    vm.DiffItems.Should().NotContain(i => i.Kind == SchemaDiffKind.ReorderColumns);
                }
            }
        );

    /// <summary>Customer へ Name 列を足す選択済みの差分</summary>
    private static SchemaDiffItem AddNameColumnItem() =>
        new()
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "Customer",
            ColumnName = "Name",
            Column = new Column { Name = "Name", DataType = "nvarchar(50)" },
            IsSelected = true,
            IsSelectable = true,
        };

    /// <summary>列順の変更を同期しない旨の案内の項目（選択できない）</summary>
    private static SchemaDiffItem InformationalReorderItem() =>
        new()
        {
            Kind = SchemaDiffKind.RebuildTable,
            TableName = "Order",
            Description = "列順変更は DB 同期しません: [Order]",
            IsSelected = false,
            IsSelectable = false,
        };

    /// <summary>複合外部キーを持つ live と、外部キーの無い目標（DropForeignKey の差分だけが出る）</summary>
    private static (SchemaImportResult Live, Entity[] Target) CompositeForeignKeyScenario()
    {
        var parent = TableOf("parent", Col("id", "int", pk: true), Col("order_no", "int"));
        var child = TableOf(
            "child",
            Col("id", "int", pk: true),
            Col("parent_id", "int"),
            Col("order_no", "int")
        );
        var rel = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                new(parent.Columns[0].Id, child.Columns[1].Id),
                new(parent.Columns[1].Id, child.Columns[2].Id),
            ],
            ConstraintName = "FK_child_parent",
        };
        var live = new SchemaImportResult { Entities = [parent, child], Relationships = [rel] };
        var target = new[] { parent.Clone(preserveId: true), child.Clone(preserveId: true) };
        return (live, target);
    }

    /// <summary>
    /// 複合外部キーの参照先の主キーの変更と構成列の型の変更を含む live と目標（無関係な列の追加も 1 本混ぜる）
    /// </summary>
    private static (
        SchemaImportResult Live,
        Entity[] Target,
        Relationship[] TargetRelationships
    ) CompositeForeignKeyChangeScenario()
    {
        var parent = TableOf("parent", Col("id", "int", pk: true), Col("code", "int"));
        var child = TableOf(
            "child",
            Col("id", "int", pk: true),
            Col("parent_id", "int"),
            Col("order_no", "int")
        );
        var rel = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                new(parent.Columns[0].Id, child.Columns[1].Id),
                new(parent.Columns[1].Id, child.Columns[2].Id),
            ],
            ConstraintName = "FK_child_parent",
        };
        var live = new SchemaImportResult { Entities = [parent, child], Relationships = [rel] };

        // 目標：親の主キーを id → (id, code) へ広げ、子の parent_id の型を変え、無関係な列を 1 本足す
        var parentTarget = parent.Clone(preserveId: true);
        parentTarget.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
        var childTarget = child.Clone(preserveId: true);
        childTarget.Columns.Single(c => c.Name == "parent_id").DataType = "bigint";
        childTarget.Columns.Add(new Column { Name = "memo", DataType = "nvarchar(50)" });

        // 外部キーは図にも live と同じ複合の構成で残す（FK の差分を出さず、主キー・列の変更だけを見る）
        var targetRel = new Relationship
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
                new(
                    parentTarget.Columns.Single(c => c.Name == "code").Id,
                    childTarget.Columns.Single(c => c.Name == "order_no").Id
                ),
            ],
            ConstraintName = "FK_child_parent",
        };

        return (live, [parentTarget, childTarget], [targetRel]);
    }
}
