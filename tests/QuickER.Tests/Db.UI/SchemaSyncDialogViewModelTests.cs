using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using DbStrings = QuickER.Db.UI.Resources.Strings;

namespace QuickER.Tests.Db.UI;

/// <summary><see cref="SchemaSyncDialogViewModel"/> の差分選択・プレビュー生成・実行確認を検証するテストクラス</summary>
public class SchemaSyncDialogViewModelTests
{
    /// <summary>SQL Server プロバイダ（同期スクリプト生成に用いる）</summary>
    private static readonly IDatabaseProvider Provider = new SqlServerProvider();

    /// <summary>プロバイダと空スキーマで ViewModel を生成する</summary>
    private static SchemaSyncDialogViewModel CreateVm(IDialogService? dialogs = null) =>
        new(Provider, new DbConnectionSettings(), [], [], dialogs);

    /// <summary>全選択が選択可能な差分のみを対象とし、案内項目を選択しないことを検証する</summary>
    [Fact(DisplayName = "全選択は選択可能な差分のみを対象にする")]
    public void SelectAll_SelectsOnlySelectableItems()
    {
        var vm = CreateVm();
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "Customer",
                Entity = new QuickER.Model.Entity
                {
                    TableName = "Customer",
                    Columns =
                    {
                        new QuickER.Model.Column
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                },

                IsSelected = false,
                IsSelectable = true,
            }
        );
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "Order",
                Description = "列順変更は DB 同期しません: [Order]",
                IsSelected = false,
                IsSelectable = false,
            }
        );

        vm.SelectAllCommand.Execute(null);

        vm.DiffItems[0].IsSelected.Should().BeTrue();
        vm.DiffItems[1].IsSelected.Should().BeFalse();
    }

    /// <summary>全解除が選択不可の案内項目の状態を変更しないことを検証する</summary>
    [Fact(DisplayName = "全解除は選択不可の案内項目の状態を変更しない")]
    public void DeselectAll_DoesNotChangeNonSelectableItems()
    {
        var vm = CreateVm();
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "Customer",
                ColumnName = "Name",
                Column = new QuickER.Model.Column { Name = "Name", DataType = "nvarchar(50)" },
                IsSelected = true,
                IsSelectable = true,
            }
        );
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "Order",
                Description = "列順変更は DB 同期しません: [Order]",
                IsSelected = false,
                IsSelectable = false,
            }
        );

        vm.DeselectAllCommand.Execute(null);

        vm.DiffItems[0].IsSelected.Should().BeFalse();
        vm.DiffItems[1].IsSelected.Should().BeFalse();
    }

    /// <summary>プレビュー生成が選択済みの通常差分のみを対象とし、案内項目を出力しないことを検証する</summary>
    [Fact(DisplayName = "スクリプト生成時は案内項目を選択していなくても通常差分のみが対象になる")]
    public void UpdatePreview_IgnoresNonSelectedInformationalItems()
    {
        var vm = CreateVm();
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "Customer",
                ColumnName = "Name",
                Column = new QuickER.Model.Column { Name = "Name", DataType = "nvarchar(50)" },
                IsSelected = true,
                IsSelectable = true,
            }
        );
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "Order",
                Description = "列順変更は DB 同期しません: [Order]",
                IsSelected = false,
                IsSelectable = false,
            }
        );

        vm.UpdatePreview();

        vm.ScriptPreview.Should().Contain("ALTER TABLE [Customer] ADD [Name] nvarchar(50) NULL;");
        vm.ScriptPreview.Should().NotContain("RebuildTable");
        vm.ScriptPreview.Should().NotContain("列順変更は DB 同期しません");
    }

    /// <summary>実行時の警告確認でキャンセルすると、スクリプトを実行しないことを検証する</summary>
    [Fact(DisplayName = "Execute: 警告確認でキャンセルするとスクリプトは実行されない")]
    public async Task Execute_ConfirmDeclined_DoesNotRunScript()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateVm(dialogs);
        vm.ScriptPreview = "DROP TABLE [X];";

        await vm.ExecuteCommand.ExecuteAsync(null);

        dialogs.WarningConfirmMessages.Should().ContainSingle();
        vm.StatusMessage.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    // ---------------- SQLite（テーブル再構築方言）向けの配線 ----------------

    /// <summary>Id/型を指定してエンティティを組み立てる（テストフィクスチャ用）</summary>
    private static Entity Table(string name, params Column[] columns)
    {
        var entity = new Entity { TableName = name };

        foreach (var column in columns)
        {
            entity.Columns.Add(column);
        }

        return entity;
    }

    /// <summary>列定義を組み立てる（テストフィクスチャ用）</summary>
    private static Column Col(string name, string type, bool pk = false) =>
        new()
        {
            Name = name,
            DataType = type,
            IsPrimaryKey = pk,
        };

    /// <summary>Refresh 前に UpdatePreview を呼んでも、rebuild 方言で例外にならず空プレビューになることを検証する</summary>
    [Fact(DisplayName = "rebuild 方言でも Refresh 前の UpdatePreview は例外にならず空プレビュー")]
    public void UpdatePreview_BeforeRefresh_RebuildDialect_DoesNotThrow()
    {
        // live 未取得（Refresh 前）でも SyncPlanContext を欠いて BuildPlan が throw しないこと
        var provider = new FakeSqliteProvider(new FakeSchemaImporter(new SchemaImportResult()));
        var vm = new SchemaSyncDialogViewModel(provider, new DbConnectionSettings(), [], []);

        var act = vm.UpdatePreview;

        act.Should().NotThrow();
        vm.ScriptPreview.Should().BeEmpty();
    }

    /// <summary>rebuild 方言で AlterColumn を選択して実行すると、確認文言が再構築（テーブル名列挙）に切り替わることを検証する</summary>
    [Fact(DisplayName = "rebuild 方言: AlterColumn 選択時の実行確認は再構築文言（テーブル名列挙）")]
    public async Task Execute_RebuildDialect_AlterColumn_ShowsRebuildConfirm()
    {
        // live: Customer(Id INTEGER PK, Age INTEGER) / target: Age を TEXT へ型変更
        var live = new SchemaImportResult
        {
            Entities = [Table("Customer", Col("Id", "INTEGER", pk: true), Col("Age", "INTEGER"))],
        };
        var provider = new FakeSqliteProvider(new FakeSchemaImporter(live));

        // SQLite の接続設定は FilePath を使う（Database はサーバー系方言用）
        var settings = new DbConnectionSettings { FilePath = "shop.db" };
        var target = new[]
        {
            Table("Customer", Col("Id", "INTEGER", pk: true), Col("Age", "TEXT")),
        };
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new SchemaSyncDialogViewModel(provider, settings, target, [], dialogs);

        await vm.RefreshCommand.ExecuteAsync(null);

        // 型変更 AlterColumn は既定で未選択のため、明示的に選択して再構築計画へ載せる
        var alter = vm.DiffItems.Single(i => i.Kind == SchemaDiffKind.AlterColumn);
        alter.IsSelected = true;

        await vm.ExecuteCommand.ExecuteAsync(null);

        var message = dialogs.WarningConfirmMessages.Should().ContainSingle().Subject;
        // 対象テーブルは 1 行 1 テーブルの箇条書きで列挙される
        message
            .Should()
            .Be(
                string.Format(
                    DbStrings.SchemaSync_ExecuteConfirmRebuild,
                    settings.FilePath,
                    "  • Customer"
                )
            );
    }

    /// <summary>rebuild 方言でも再構築を伴わない差分（列追加のみ）の実行確認は通常文言のままであることを検証する</summary>
    [Fact(DisplayName = "rebuild 方言: 列追加のみの実行確認は通常文言のまま")]
    public async Task Execute_RebuildDialect_AddColumnOnly_ShowsNormalConfirm()
    {
        // live: Customer(Id) / target: Name を追加（SQLite でも逐次 ADD COLUMN で足りる＝再構築不要）
        var live = new SchemaImportResult
        {
            Entities = [Table("Customer", Col("Id", "INTEGER", pk: true))],
        };
        var provider = new FakeSqliteProvider(new FakeSchemaImporter(live));

        // SQLite の接続設定は FilePath を使う（Database はサーバー系方言用）
        var settings = new DbConnectionSettings { FilePath = "shop.db" };
        var target = new[]
        {
            Table("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT")),
        };
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new SchemaSyncDialogViewModel(provider, settings, target, [], dialogs);

        await vm.RefreshCommand.ExecuteAsync(null);
        // 列追加は既定で選択済み。念のためプレビューを更新しておく
        vm.UpdatePreview();

        await vm.ExecuteCommand.ExecuteAsync(null);

        var message = dialogs.WarningConfirmMessages.Should().ContainSingle().Subject;
        message.Should().Be(string.Format(DbStrings.SchemaSync_ExecuteConfirm, settings.FilePath));
    }

    /// <summary>SQLite の実行確認が、共用設定に残った別方言のデータベース名でなくファイルパスを表示することを検証する</summary>
    /// <remarks>
    /// 対象 DB を SQL Server から SQLite へ切り替えた直後の再現。共用の <see cref="DbConnectionSettings"/> には
    /// SQL Server 時代の Database が残るため、表示フィールドを方言で選ばないと前の名前が出てしまう。
    /// </remarks>
    [Fact(
        DisplayName = "rebuild 方言: 実行確認は残存する別方言の Database でなく FilePath を表示する"
    )]
    public async Task Execute_RebuildDialect_ShowsFilePath_NotStaleDatabase()
    {
        var live = new SchemaImportResult
        {
            Entities = [Table("Customer", Col("Id", "INTEGER", pk: true))],
        };
        var provider = new FakeSqliteProvider(new FakeSchemaImporter(live));
        var settings = new DbConnectionSettings
        {
            Database = "OldSqlServerDb",
            FilePath = @"C:\data\shop.db",
        };
        var target = new[]
        {
            Table("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT")),
        };
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new SchemaSyncDialogViewModel(provider, settings, target, [], dialogs);

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.UpdatePreview();

        await vm.ExecuteCommand.ExecuteAsync(null);

        var message = dialogs.WarningConfirmMessages.Should().ContainSingle().Subject;
        message.Should().Be(string.Format(DbStrings.SchemaSync_ExecuteConfirm, @"C:\data\shop.db"));
        message.Should().NotContain("OldSqlServerDb");
    }

    /// <summary>サーバー系方言の実行確認が、共用設定に残った SQLite のファイルパスでなくデータベース名を表示することを検証する</summary>
    [Fact(DisplayName = "サーバー系方言: 実行確認は残存する FilePath でなく Database を表示する")]
    public async Task Execute_ServerDialect_ShowsDatabase_NotStaleFilePath()
    {
        // 逆方向の切替（SQLite → SQL Server）でも表示が方言のフィールドに追従することを固定する
        var settings = new DbConnectionSettings
        {
            Database = "Shop",
            FilePath = @"C:\data\stale.db",
        };
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new SchemaSyncDialogViewModel(Provider, settings, [], [], dialogs);
        vm.ScriptPreview = "ALTER TABLE [X] ADD [Y] int NULL;";

        await vm.ExecuteCommand.ExecuteAsync(null);

        var message = dialogs.WarningConfirmMessages.Should().ContainSingle().Subject;
        message.Should().Be(string.Format(DbStrings.SchemaSync_ExecuteConfirm, "Shop"));
    }

    /// <summary>実行成功後の再計算で差分が 0 になったとき、ダイアログが自動で閉じることを検証する</summary>
    [Fact(DisplayName = "Execute: 成功後に差分 0 ならダイアログを自動で閉じる")]
    public async Task Execute_Success_NoRemainingDiff_ClosesDialog()
    {
        // live: Customer(Id) / target: Name を追加。実行成功後の再取込では target と同一の live を返し、
        // 差分 0（自動クローズ条件）を再現する
        var importer = new FakeSchemaImporter(
            new SchemaImportResult
            {
                Entities = [Table("Customer", Col("Id", "INTEGER", pk: true))],
            }
        );
        var provider = new FakeSqliteProvider(importer, new FakeSuccessExecutor());
        var target = new[]
        {
            Table("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT")),
        };
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings { Database = "shop.db" },
            target,
            [],
            dialogs
        );
        var closed = false;
        vm.CloseAction = _ => closed = true;

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.DiffItems.Should().NotBeEmpty();

        // 実行後の再取込は「適用済み＝target と同一」の live を返す
        importer.Result = new SchemaImportResult
        {
            Entities = [Table("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT"))],
        };

        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.DiffItems.Should().BeEmpty();
        closed.Should().BeTrue();
    }

    /// <summary>実行成功後も差分が残る場合は、ダイアログを閉じないことを検証する</summary>
    [Fact(DisplayName = "Execute: 成功後も差分が残ればダイアログは開いたまま")]
    public async Task Execute_Success_RemainingDiff_KeepsDialogOpen()
    {
        // 再取込でも Name が未反映の live を返し続ける＝差分が残るケース
        var importer = new FakeSchemaImporter(
            new SchemaImportResult
            {
                Entities = [Table("Customer", Col("Id", "INTEGER", pk: true))],
            }
        );
        var provider = new FakeSqliteProvider(importer, new FakeSuccessExecutor());
        var target = new[]
        {
            Table("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT")),
        };
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings { Database = "shop.db" },
            target,
            [],
            dialogs
        );
        var closed = false;
        vm.CloseAction = _ => closed = true;

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.DiffItems.Should().NotBeEmpty();
        closed.Should().BeFalse();
    }

    /// <summary>Compute へ SQLite ケーパビリティが渡り、説明のみの差分が抑止されることを検証する</summary>
    [Fact(DisplayName = "rebuild 方言: 説明のみの差分は抑止される（capabilities 伝播の外形検証）")]
    public async Task Refresh_RebuildDialect_SuppressesDescriptionOnlyDiff()
    {
        // live と target は列構成が同一で、target のみテーブル説明を持つ。
        // SQLite は説明機構が無い（SupportsDescriptions=false）ため、capabilities が伝われば差分は 0 件になる
        var live = new SchemaImportResult
        {
            Entities = [Table("Customer", Col("Id", "INTEGER", pk: true))],
        };
        var provider = new FakeSqliteProvider(new FakeSchemaImporter(live));
        var targetTable = Table("Customer", Col("Id", "INTEGER", pk: true));
        targetTable.Description = "顧客マスタ";
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings(),
            [targetTable],
            []
        );

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.DiffItems.Should().BeEmpty();
        vm.HasDiff.Should().BeFalse();
    }

    // ---------------- 列順変更（ReorderColumns）の配線 ----------------

    /// <summary>列順のみ入れ替えた live / target（Id は共通・Name↔Email を入れ替え）</summary>
    private static (SchemaImportResult Live, Entity[] Target) ReorderScenario()
    {
        var live = new SchemaImportResult
        {
            Entities =
            [
                Table(
                    "Customer",
                    Col("Id", "INTEGER", pk: true),
                    Col("Name", "TEXT"),
                    Col("Email", "TEXT")
                ),
            ],
        };
        var target = new[]
        {
            Table(
                "Customer",
                Col("Id", "INTEGER", pk: true),
                Col("Email", "TEXT"),
                Col("Name", "TEXT")
            ),
        };
        return (live, target);
    }

    /// <summary>Rebuild 方言（SQLite）では列順変更が選択可能な ReorderColumns 項目として並ぶことを検証する</summary>
    [Fact(DisplayName = "Rebuild 方言: 列順変更は選択可能な ReorderColumns 項目になる")]
    public async Task Refresh_RebuildDialect_ColumnReorder_YieldsSelectableItem()
    {
        var (live, target) = ReorderScenario();
        var provider = new FakeProvider(new SqliteProvider(), new FakeSchemaImporter(live));
        var vm = new SchemaSyncDialogViewModel(provider, new DbConnectionSettings(), target, []);

        await vm.RefreshCommand.ExecuteAsync(null);

        var reorder = vm
            .DiffItems.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.ReorderColumns)
            .Which;
        reorder.IsSelectable.Should().BeTrue();
        reorder.IsSelected.Should().BeFalse();
        // 選択不可の案内項目（RebuildTable）は重複して出さない
        vm.DiffItems.Should().NotContain(i => i.Kind == SchemaDiffKind.RebuildTable);
    }

    /// <summary>Native 方言（MySQL）でも列順変更が選択可能な ReorderColumns 項目として並ぶことを検証する</summary>
    [Fact(DisplayName = "Native 方言: 列順変更は選択可能な ReorderColumns 項目になる")]
    public async Task Refresh_NativeDialect_ColumnReorder_YieldsSelectableItem()
    {
        var (live, target) = ReorderScenario();
        var provider = new FakeProvider(new MySqlProvider(), new FakeSchemaImporter(live));
        var vm = new SchemaSyncDialogViewModel(provider, new DbConnectionSettings(), target, []);

        await vm.RefreshCommand.ExecuteAsync(null);

        var reorder = vm
            .DiffItems.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.ReorderColumns)
            .Which;
        reorder.IsSelectable.Should().BeTrue();
        reorder.IsSelected.Should().BeFalse();
        vm.DiffItems.Should().NotContain(i => i.Kind == SchemaDiffKind.RebuildTable);
    }

    /// <summary>None 方言（SQL Server）では従来どおり選択不可の案内項目（RebuildTable）になることを検証する</summary>
    [Fact(DisplayName = "None 方言: 列順変更は選択不可の案内項目のまま")]
    public async Task Refresh_NoneDialect_ColumnReorder_YieldsInformationalItem()
    {
        var (live, target) = ReorderScenario();
        var provider = new FakeProvider(new SqlServerProvider(), new FakeSchemaImporter(live));
        var vm = new SchemaSyncDialogViewModel(provider, new DbConnectionSettings(), target, []);

        await vm.RefreshCommand.ExecuteAsync(null);

        var info = vm
            .DiffItems.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.RebuildTable)
            .Which;
        info.IsSelectable.Should().BeFalse();
        // 対応方言でないため選択可能な ReorderColumns 項目は生成されない
        vm.DiffItems.Should().NotContain(i => i.Kind == SchemaDiffKind.ReorderColumns);
    }

    // ---------------- 複合外部キー（取込で列対応が失われた FK）の扱い ----------------

    /// <summary>
    /// 複合外部キーを取り込んだ live（図側では FK が消えている＝DropForeignKey 差分が出る）を組み立てる。
    /// </summary>
    private static (SchemaImportResult Live, Entity[] Target) CompositeForeignKeyScenario()
    {
        var parent = Table("parent", Col("id", "int", pk: true));
        var child = Table(
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
            SourceColumnId = parent.Columns[0].Id,
            // 複合外部キーは意味モデルが列対応を表現できず、子列の指定を失う
            TargetColumnId = null,
            ConstraintName = "FK_child_parent",
        };
        var live = new SchemaImportResult
        {
            Entities = [parent, child],
            Relationships = [rel],
            Warnings =
            [
                new CompositeForeignKeyImportWarning(
                    "FK_child_parent",
                    "child",
                    ["parent_id", "order_no"],
                    "parent",
                    ["id", "order_no"]
                ),
            ],
        };

        // 目標はテーブル構成が同じでリレーション無し＝ DropForeignKey 差分だけが出る
        var target = new[] { parent.Clone(preserveId: true), child.Clone(preserveId: true) };
        return (live, target);
    }

    /// <summary>
    /// 複合外部キーを含む取込では、一覧の先頭に案内項目が出て、関与する FK 差分が選択不可へ格下げされることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "複合外部キー: 案内項目が先頭に出て、関与する FK 差分は選択不可へ格下げされる"
    )]
    public async Task Refresh_CompositeForeignKey_AddsNoticeAndDemotesForeignKeyDiff()
    {
        var (live, target) = CompositeForeignKeyScenario();
        var provider = new FakeProvider(new SqlServerProvider(), new FakeSchemaImporter(live));
        var vm = new SchemaSyncDialogViewModel(provider, new DbConnectionSettings(), target, []);

        await vm.RefreshCommand.ExecuteAsync(null);

        var notice = vm.DiffItems[0];
        notice.Kind.Should().Be(SchemaDiffKind.Advisory);
        notice.IsSelectable.Should().BeFalse();
        notice
            .Description.Should()
            .Be(string.Format(DbStrings.SchemaSync_CompositeForeignKeyNotice, 1));

        var dropFk = vm
            .DiffItems.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropForeignKey)
            .Which;
        dropFk.IsSelectable.Should().BeFalse();
        dropFk.IsSelected.Should().BeFalse();
        dropFk
            .Description.Should()
            .Match(string.Format(DbStrings.SchemaSync_CompositeForeignKeyDiffBlocked, "*"));

        // 全選択でも実行対象にならない（誤った単列 FK への置換を構造的に封じる）
        vm.SelectAllCommand.Execute(null);
        dropFk.IsSelected.Should().BeFalse();
        vm.ScriptPreview.Should().NotContain("FK_child_parent");
    }

    /// <summary>
    /// 複合外部キーの参照先（親）テーブルの主キー変更と、複合外部キーが関与する列の型変更を含む
    /// live / target を組み立てる（無関係な列追加も 1 本混ぜて、通常の差分が巻き添えにならないことを見る）。
    /// </summary>
    private static (
        SchemaImportResult Live,
        Entity[] Target,
        Relationship[] TargetRelationships
    ) CompositeForeignKeyChangeScenario()
    {
        var parent = Table("parent", Col("id", "int", pk: true), Col("code", "int"));
        var child = Table(
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
            SourceColumnId = parent.Columns[0].Id,
            // 複合外部キーは意味モデルが列対応を表現できず、子列の指定を失う
            TargetColumnId = null,
            ConstraintName = "FK_child_parent",
        };
        var live = new SchemaImportResult
        {
            Entities = [parent, child],
            Relationships = [rel],
            Warnings =
            [
                new CompositeForeignKeyImportWarning(
                    "FK_child_parent",
                    "child",
                    ["parent_id", "order_no"],
                    "parent",
                    ["id", "code"]
                ),
            ],
        };

        // 目標: 親の主キーを id → code へ移し、子の parent_id を型変更し、無関係な列を 1 本足す
        var parentTarget = parent.Clone(preserveId: true);
        parentTarget.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        parentTarget.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
        var childTarget = child.Clone(preserveId: true);
        childTarget.Columns.Single(c => c.Name == "parent_id").DataType = "bigint";
        childTarget.Columns.Add(new Column { Name = "memo", DataType = "nvarchar(50)" });

        // 外部キー自体は図にも残す（FK 差分を出さず、主キー・列の変更だけを見るため）
        var targetRel = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parentTarget.Columns.Single(c => c.Name == "id").Id,
            TargetColumnId = childTarget.Columns.Single(c => c.Name == "parent_id").Id,
            ConstraintName = "FK_child_parent",
        };

        return (live, [parentTarget, childTarget], [targetRel]);
    }

    /// <summary>
    /// 複合外部キーの作り直しを招く変更（参照先テーブルの主キー変更・関与列の型変更）が選択不可へ
    /// 格下げされ、無関係な差分は従来どおり選択できることを検証する。
    /// </summary>
    [Fact(DisplayName = "複合外部キー: 参照先の主キー変更と関与列の型変更は選択不可へ格下げされる")]
    public async Task Refresh_CompositeForeignKey_DemotesPrimaryKeyAndColumnChanges()
    {
        var (live, target, targetRelationships) = CompositeForeignKeyChangeScenario();
        var provider = new FakeProvider(new SqlServerProvider(), new FakeSchemaImporter(live));
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings(),
            target,
            targetRelationships
        );

        await vm.RefreshCommand.ExecuteAsync(null);

        var alterPk = vm
            .DiffItems.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterPrimaryKey)
            .Which;
        alterPk.IsSelectable.Should().BeFalse();
        alterPk
            .Description.Should()
            .Match(string.Format(DbStrings.SchemaSync_CompositeForeignKeyChangeBlocked, "*"));

        var alterColumn = vm
            .DiffItems.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn)
            .Which;
        alterColumn.ColumnName.Should().Be("parent_id");
        alterColumn.IsSelectable.Should().BeFalse();

        // 複合外部キーと無関係な差分（列追加）は従来どおり選択できる
        vm.DiffItems.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddColumn)
            .Which.IsSelectable.Should()
            .BeTrue();

        // 全選択でも実行対象にならない（複合外部キーの単列化を構造的に封じる）
        vm.SelectAllCommand.Execute(null);
        alterPk.IsSelected.Should().BeFalse();
        alterColumn.IsSelected.Should().BeFalse();
        vm.ScriptPreview.Should().NotContain("PRIMARY KEY");
    }

    /// <summary>
    /// 複合外部キーの<b>副構成列</b>（子側の 2 列目 order_no）の型変更も選択不可へ格下げされることを検証する。
    /// </summary>
    /// <remarks>
    /// 副構成列は意味モデルへ劣化した live リレーションからは復元できない（子側の列指定を失っている）ため、
    /// 照合範囲を live の外部キー列挙で組み立てると素通りする。SQL Server では素通りした変更が実行時に
    /// Msg 5074 で失敗し続けるため、取込警告の全構成列を照合範囲にして実行前に格下げする。
    /// </remarks>
    [Fact(DisplayName = "複合外部キー: 副構成列（2 列目）の型変更も選択不可へ格下げされる")]
    public async Task Refresh_CompositeForeignKey_DemotesSecondaryConstituentColumnChange()
    {
        var (live, target, targetRelationships) = CompositeForeignKeyChangeScenario();
        // 第 1 構成列（parent_id）に加えて第 2 構成列（order_no）も型変更する
        var childTarget = target.Single(e => e.TableName == "child");
        childTarget.Columns.Single(c => c.Name == "order_no").DataType = "bigint";

        var provider = new FakeProvider(new SqlServerProvider(), new FakeSchemaImporter(live));
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings(),
            target,
            targetRelationships
        );

        await vm.RefreshCommand.ExecuteAsync(null);

        var alterColumns = vm.DiffItems.Where(i => i.Kind == SchemaDiffKind.AlterColumn).ToList();
        alterColumns.Select(i => i.ColumnName).Should().BeEquivalentTo("parent_id", "order_no");
        alterColumns.Should().OnlyContain(i => !i.IsSelectable);

        foreach (var alter in alterColumns)
        {
            alter
                .Description.Should()
                .Match(string.Format(DbStrings.SchemaSync_CompositeForeignKeyChangeBlocked, "*"));
        }

        // 複合外部キーと無関係な差分（列追加）は従来どおり選択できる
        vm.DiffItems.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddColumn)
            .Which.IsSelectable.Should()
            .BeTrue();

        // 全選択でも実行対象にならない
        vm.SelectAllCommand.Execute(null);
        alterColumns.Should().OnlyContain(i => !i.IsSelected);
    }

    /// <summary>
    /// 格下げをすり抜けて選択された場合でも、計画側が変更を落として実行確認へ列挙することを検証する
    /// （UI の格下げと計画側の除外の二重防御）。
    /// </summary>
    [Fact(DisplayName = "実行確認: 複合外部キーのため計画から落ちた変更が列挙される")]
    public async Task Execute_CompositeForeignKeyBlockedChange_AppendsWarningToConfirm()
    {
        var (live, target, targetRelationships) = CompositeForeignKeyChangeScenario();
        var provider = new FakeProvider(new SqlServerProvider(), new FakeSchemaImporter(live));
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings { Database = "shop" },
            target,
            targetRelationships,
            dialogs
        );

        await vm.RefreshCommand.ExecuteAsync(null);
        // UI では選べない項目を直接選択して、計画側の最終防御（除外＋警告）が働くことを見る
        vm.DiffItems.Single(i => i.Kind == SchemaDiffKind.AlterPrimaryKey).IsSelected = true;

        await vm.ExecuteCommand.ExecuteAsync(null);

        var message = dialogs.WarningConfirmMessages.Should().ContainSingle().Subject;
        message
            .Should()
            .Contain(
                string.Format(
                    DbStrings.SchemaSync_ExecuteConfirmCompositeForeignKeyBlocked,
                    "  • parent"
                )
            );

        // 主キー変更は計画に入らないため、プレビューにも現れない
        vm.ScriptPreview.Should().NotContain("PRIMARY KEY");
    }

    /// <summary>
    /// 主キー変更で被参照列が候補キーでなくなる場合、実行確認へ FK 自動再作成の注意が追記されることを検証する。
    /// </summary>
    [Fact(DisplayName = "実行確認: 候補キーを失う FK 自動再作成の注意が追記される")]
    public async Task Execute_ForeignKeyRebuildRisk_AppendsWarningToConfirm()
    {
        var parent = Table("parent", Col("id", "int", pk: true), Col("code", "int"));
        var child = Table("child", Col("id", "int", pk: true), Col("parent_id", "int"));
        var rel = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parent.Columns[0].Id,
            TargetColumnId = child.Columns[1].Id,
            ConstraintName = "FK_child_parent",
        };
        var live = new SchemaImportResult { Entities = [parent, child], Relationships = [rel] };
        var provider = new FakeProvider(new SqlServerProvider(), new FakeSchemaImporter(live));

        // 目標: 親の主キーを id から code へ移す（子の FK は id を参照したまま維持する）
        var parentTarget = parent.Clone(preserveId: true);
        parentTarget.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        parentTarget.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
        var childTarget = child.Clone(preserveId: true);
        var relKeep = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parentTarget.Columns.Single(c => c.Name == "id").Id,
            TargetColumnId = childTarget.Columns.Single(c => c.Name == "parent_id").Id,
            ConstraintName = "FK_child_parent",
        };

        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings { Database = "shop" },
            [parentTarget, childTarget],
            [relKeep],
            dialogs
        );

        await vm.RefreshCommand.ExecuteAsync(null);
        // 主キー変更は既定で未選択のため明示的に選択する（選択変更でプレビューと計画が再構築される）
        vm.DiffItems.Single(i => i.Kind == SchemaDiffKind.AlterPrimaryKey).IsSelected = true;

        await vm.ExecuteCommand.ExecuteAsync(null);

        var message = dialogs.WarningConfirmMessages.Should().ContainSingle().Subject;
        message
            .Should()
            .StartWith(string.Format(DbStrings.SchemaSync_ExecuteConfirmDestructive, "shop"));
        message
            .Should()
            .Contain(string.Format(DbStrings.SchemaSync_ExecuteConfirmForeignKeyRebuildRisk, 1));
    }

    /// <summary>
    /// rebuild 方言（SQLite）で複合外部キーの子テーブルは再構築されず、実行確認に対象が列挙されることを検証する。
    /// </summary>
    [Fact(DisplayName = "rebuild 方言: 複合外部キーの子テーブルは再構築されず実行確認へ列挙される")]
    public async Task Execute_RebuildDialect_CompositeForeignKeyTable_IsBlockedAndListed()
    {
        var orderLine = Table(
            "order_line",
            Col("Id", "INTEGER", pk: true),
            Col("OrderId", "INTEGER"),
            Col("LineNo", "INTEGER"),
            Col("Note", "TEXT")
        );
        var live = new SchemaImportResult
        {
            Entities = [orderLine],
            Warnings =
            [
                new CompositeForeignKeyImportWarning(
                    "FK_order_line_orders",
                    "order_line",
                    ["OrderId", "LineNo"],
                    "orders",
                    ["Id", "LineNo"]
                ),
            ],
        };
        var provider = new FakeSqliteProvider(new FakeSchemaImporter(live));
        var target = new[]
        {
            Table(
                "order_line",
                Col("Id", "INTEGER", pk: true),
                Col("OrderId", "INTEGER"),
                Col("LineNo", "INTEGER"),
                Col("Note", "INTEGER")
            ),
        };
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings { FilePath = "shop.db" },
            target,
            [],
            dialogs
        );

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.DiffItems.Single(i => i.Kind == SchemaDiffKind.AlterColumn).IsSelected = true;

        await vm.ExecuteCommand.ExecuteAsync(null);

        var message = dialogs.WarningConfirmMessages.Should().ContainSingle().Subject;
        // 再構築されないため確認文言は再構築版にならず、破壊的変更版＋ブロック注記になる
        message
            .Should()
            .StartWith(string.Format(DbStrings.SchemaSync_ExecuteConfirmDestructive, "shop.db"));
        message
            .Should()
            .Contain(
                string.Format(DbStrings.SchemaSync_ExecuteConfirmRebuildBlocked, "  • order_line")
            );

        // プレビューには畳み込まれなかった項目のスキップコメントが出る（スクリプト内は英語固定）
        vm.ScriptPreview.Should().Contain("Skipped 'AlterColumn' on order_line");
    }

    /// <summary>
    /// 実行失敗時も、エラー表示のあとに差分を取り直すこと（MySQL / Oracle の部分適用への追従）を検証する。
    /// </summary>
    [Fact(DisplayName = "Execute: 失敗時もエラー表示後に差分を取り直す")]
    public async Task Execute_Failure_RefreshesDiffAfterError()
    {
        var importer = new FakeSchemaImporter(
            new SchemaImportResult
            {
                Entities = [Table("Customer", Col("Id", "INTEGER", pk: true))],
            }
        );
        // 実行は失敗するが、部分適用で Name 列だけは適用済みになった状態を再現する
        var executor = new FakeFailingExecutor(() =>
            importer.Result = new SchemaImportResult
            {
                Entities = [Table("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT"))],
            }
        );
        var provider = new FakeSqliteProvider(importer, executor);
        var target = new[]
        {
            Table("Customer", Col("Id", "INTEGER", pk: true), Col("Name", "TEXT")),
        };
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = new SchemaSyncDialogViewModel(
            provider,
            new DbConnectionSettings { FilePath = "shop.db" },
            target,
            [],
            dialogs
        );
        var closed = false;
        vm.CloseAction = _ => closed = true;

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.DiffItems.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddColumn);

        await vm.ExecuteCommand.ExecuteAsync(null);

        dialogs.ErrorMessages.Should().ContainSingle();
        vm.DiffItems.Should().BeEmpty("失敗後も取り直すため、部分適用済みの列は差分から消える");
        closed.Should().BeFalse("失敗時にダイアログを自動で閉じない");
    }

    /// <summary>内部プロバイダへ委譲しつつ SchemaImporter だけを差し替える汎用フェイクプロバイダ（実接続しない）</summary>
    private sealed class FakeProvider : IDatabaseProvider
    {
        private readonly IDatabaseProvider _inner;
        private readonly ISchemaImporter _importer;

        public FakeProvider(IDatabaseProvider inner, ISchemaImporter importer)
        {
            _inner = inner;
            _importer = importer;
        }

        public string Name => _inner.Name;

        public string DisplayName => _inner.DisplayName;

        public int? DefaultPort => _inner.DefaultPort;

        public ISchemaImporter SchemaImporter => _importer;

        public IColumnTypeMapper TypeMapper => _inner.TypeMapper;

        public ITypeCatalog TypeCatalog => _inner.TypeCatalog;

        public ISyncScriptBuilder SyncScriptBuilder => _inner.SyncScriptBuilder;

        public SyncDialectCapabilities SyncCapabilities => _inner.SyncCapabilities;

        public ISchemaSyncExecutor SyncExecutor => _inner.SyncExecutor;

        public IDdlGenerator DdlGenerator => _inner.DdlGenerator;

        // 実接続はしない（フェイクインポーターが接続文字列を無視する）ため固定値を返す
        public string BuildConnectionString(DbConnectionSettings settings) => "Fake";
    }

    /// <summary>SchemaImporter を差し替え可能にした SQLite 相当のフェイクプロバイダ（rebuild ケーパビリティ・実 SQLite レンダラー）</summary>
    private sealed class FakeSqliteProvider : IDatabaseProvider
    {
        /// <summary>ケーパビリティ・レンダラー・型系の実装は本物の SQLite プロバイダへ委譲する</summary>
        private readonly SqliteProvider _inner = new();

        private readonly ISchemaImporter _importer;

        private readonly ISchemaSyncExecutor? _executor;

        public FakeSqliteProvider(ISchemaImporter importer, ISchemaSyncExecutor? executor = null)
        {
            _importer = importer;
            _executor = executor;
        }

        public string Name => _inner.Name;

        public string DisplayName => _inner.DisplayName;

        public int? DefaultPort => _inner.DefaultPort;

        public ISchemaImporter SchemaImporter => _importer;

        public IColumnTypeMapper TypeMapper => _inner.TypeMapper;

        public ITypeCatalog TypeCatalog => _inner.TypeCatalog;

        public ISyncScriptBuilder SyncScriptBuilder => _inner.SyncScriptBuilder;

        public SyncDialectCapabilities SyncCapabilities => _inner.SyncCapabilities;

        public ISchemaSyncExecutor SyncExecutor => _executor ?? _inner.SyncExecutor;

        public IDdlGenerator DdlGenerator => _inner.DdlGenerator;

        // 実接続はしない（フェイクインポーターが接続文字列を無視する）ため固定値を返す
        public string BuildConnectionString(DbConnectionSettings settings) =>
            "Data Source=:memory:";
    }

    /// <summary>固定の取込結果を返すフェイクインポーター（接続文字列は無視する・結果は差し替え可能）</summary>
    private sealed class FakeSchemaImporter : ISchemaImporter
    {
        public FakeSchemaImporter(SchemaImportResult result) => Result = result;

        /// <summary>次回の取込で返す結果（同期実行後の「適用済み live」を再現するために差し替える）</summary>
        public SchemaImportResult Result { get; set; }

        public Task<SchemaImportResult> ImportAsync(
            string connectionString,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result);
    }

    /// <summary>常に成功（COMMIT）を返すフェイク実行器（実 DB へ接続しない）</summary>
    private sealed class FakeSuccessExecutor : ISchemaSyncExecutor
    {
        public Task<SchemaSyncResult> ExecuteAsync(
            DbConnectionSettings settings,
            string script,
            CancellationToken ct = default
        ) => Task.FromResult(new SchemaSyncResult { Committed = true });
    }

    /// <summary>常に失敗を返すフェイク実行器（実行時の副作用＝部分適用をコールバックで再現できる）</summary>
    private sealed class FakeFailingExecutor : ISchemaSyncExecutor
    {
        private readonly Action? _onExecute;

        public FakeFailingExecutor(Action? onExecute = null) => _onExecute = onExecute;

        public Task<SchemaSyncResult> ExecuteAsync(
            DbConnectionSettings settings,
            string script,
            CancellationToken ct = default
        )
        {
            _onExecute?.Invoke();
            return Task.FromResult(
                new SchemaSyncResult { Committed = false, Error = "statement 1 failed" }
            );
        }
    }
}
