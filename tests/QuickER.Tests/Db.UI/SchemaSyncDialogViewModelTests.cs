using FluentAssertions;
using QuickER.Db.UI;
using QuickER.Gui.Abstractions;
using QuickER.Model;
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
        var settings = new DbConnectionSettings { Database = "shop.db" };
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
        message
            .Should()
            .Be(
                string.Format(
                    DbStrings.SchemaSync_ExecuteConfirmRebuild,
                    settings.Database,
                    "Customer"
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
        var settings = new DbConnectionSettings { Database = "shop.db" };
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
        message.Should().Be(string.Format(DbStrings.SchemaSync_ExecuteConfirm, settings.Database));
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

    /// <summary>SchemaImporter を差し替え可能にした SQLite 相当のフェイクプロバイダ（rebuild ケーパビリティ・実 SQLite レンダラー）</summary>
    private sealed class FakeSqliteProvider : IDatabaseProvider
    {
        /// <summary>ケーパビリティ・レンダラー・型系の実装は本物の SQLite プロバイダへ委譲する</summary>
        private readonly SqliteProvider _inner = new();

        private readonly ISchemaImporter _importer;

        public FakeSqliteProvider(ISchemaImporter importer) => _importer = importer;

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
        public string BuildConnectionString(DbConnectionSettings settings) =>
            "Data Source=:memory:";
    }

    /// <summary>固定の取込結果を返すフェイクインポーター（接続文字列は無視する）</summary>
    private sealed class FakeSchemaImporter : ISchemaImporter
    {
        private readonly SchemaImportResult _result;

        public FakeSchemaImporter(SchemaImportResult result) => _result = result;

        public Task<SchemaImportResult> ImportAsync(
            string connectionString,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_result);
    }
}
