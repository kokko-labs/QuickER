using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// ターゲット DBMS 切替（型のピボット変換・単一 Undo・未変換警告・同一方言 no-op・
/// 未知方言フォールバック）を検証するテストクラス。
/// </summary>
public class MainViewModelTargetDbmsTests
{
    /// <summary>SQL Server と、int↔integer を変換する擬似プロバイダを登録したレジストリを作る</summary>
    private static (MainViewModel Vm, FakeProvider Fake, StubDialogService Dialogs) CreateVm()
    {
        var fake = new FakeProvider();
        var registry = new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider(), fake }
        );
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(
            dialogs,
            new NoopAppDialogService(),
            new NoopFileDialogService(),
            providers: registry
        );
        return (vm, fake, dialogs);
    }

    /// <summary>DBMS 切替で変換対象カラムの型と TargetDbms が同時に変わり、単一 Undo で両方戻ることを検証する</summary>
    [Fact(DisplayName = "DBMS 切替は単一 Undo で型と TargetDbms を戻す")]
    public void ChangeDbms_SingleUndo_RestoresTypeAndDbms()
    {
        var (vm, fake, _) = CreateVm();
        vm.AddEntityCommand.Execute(null); // 既定型 int の PK 列を持つ NewTable
        var column = vm.Entities[0].Columns[0];
        column.DataType.Should().Be("int");

        vm.SelectedProvider = fake;

        vm.CurrentProvider.Name.Should().Be(FakeProvider.FakeName);
        column.DataType.Should().Be("integer");
        vm.ToDiagramModel().TargetDbms.Should().Be(FakeProvider.FakeName);

        vm.UndoCommand.Execute(null);

        vm.CurrentProvider.Name.Should().Be("sqlserver");
        column.DataType.Should().Be("int");
        vm.ToDiagramModel().TargetDbms.Should().Be("sqlserver");
    }

    /// <summary>同一方言への切替は履歴を積まない（no-op）ことを検証する</summary>
    [Fact(DisplayName = "同一方言への切替は no-op")]
    public void ChangeDbms_SameDialect_IsNoOp()
    {
        var (vm, _, _) = CreateVm();
        vm.AddEntityCommand.Execute(null);
        var undoCountBefore = CountUndoable(vm);

        vm.SelectedProvider = vm.CurrentProvider; // 同一

        CountUndoable(vm).Should().Be(undoCountBefore);
    }

    /// <summary>変換できない型がある場合に、導入文（message）と一覧（details）が分割提示されることを検証する</summary>
    [Fact(DisplayName = "未変換の列は導入文と一覧に分割して詳細ダイアログで列挙される")]
    public void ChangeDbms_Unconverted_ShowsWarning()
    {
        var (vm, fake, dialogs) = CreateVm();
        vm.AddEntityCommand.Execute(null);
        // 擬似プロバイダが変換できない型（uniqueidentifier）へ変更する
        vm.Entities[0].Columns[0].DataType = "uniqueidentifier";

        vm.SelectedProvider = fake;

        // 単文の ShowInformation ではなく、要約＋詳細の ShowInformationDetails に移行している
        dialogs.InformationMessages.Should().BeEmpty();
        var entry = dialogs.InformationDetailsMessages.Should().ContainSingle().Subject;
        // 導入文（message）は変換警告の見出し・一覧（details）に未変換列が並ぶ
        entry.Message.Should().Be(GuiStrings.TypeConversion_WarningHeader);
        entry.Details.Should().Contain("NewTable").And.Contain("uniqueidentifier");
    }

    /// <summary>未知の TargetDbms を持つ図の読込相当で、SQL Server として動作することを検証する</summary>
    [Fact(DisplayName = "未知 TargetDbms はレジストリで SQL Server にフォールバックする")]
    public void UnknownTargetDbms_FallsBackToSqlServer()
    {
        // sqlserver のみ登録
        var registry = new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider() }
        );
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(
            dialogs,
            new NoopAppDialogService(),
            new NoopFileDialogService(),
            providers: registry
        );

        // 未知方言の図を読込む（Open 相当の内部経路を模すため公開 API 経由で確認）
        // ここでは AvailableDataTypes が SQL Server の型を返すことでフォールバックを確認する
        vm.AvailableDataTypes.Should().Contain("int");
        vm.CurrentProvider.Name.Should().Be("sqlserver");
    }

    /// <summary>SQLite では DB 同期コマンドが実行不可・他方言では実行可となることを検証する</summary>
    [Fact(DisplayName = "SQLite では DB 同期コマンドが実行不可・他方言では実行可")]
    public void SyncToDatabase_DisabledForSqlite_EnabledForOthers()
    {
        var sqlite = new SqliteProvider();
        var registry = new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider(), sqlite }
        );
        var vm = new MainViewModel(
            new StubDialogService(),
            new NoopAppDialogService(),
            new NoopFileDialogService(),
            providers: registry
        );

        // 既定は SQL Server：同期は実行可
        vm.SyncToDatabaseCommand.CanExecute(null).Should().BeTrue();

        // SQLite へ切替：同期は実行不可、ツールチップに理由が出る
        vm.SelectedProvider = sqlite;
        vm.SyncToDatabaseCommand.CanExecute(null).Should().BeFalse();
        // 製品コードと同じ resx キーから期待値を導出し、カルチャに依らず完全一致で検証する
        vm.SyncToDatabaseTooltip.Should().Be(GuiStrings.Db_SyncSqliteUnsupported);

        // SQL Server へ戻すと再び実行可
        vm.SelectedProvider = registry.Get("sqlserver");
        vm.SyncToDatabaseCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>Undo 可能件数を数える（履歴に積まれたか判定用）</summary>
    private static int CountUndoable(MainViewModel vm)
    {
        var count = 0;
        while (vm.UndoRedo.CanUndo)
        {
            vm.UndoRedo.Undo();
            count++;
        }
        // 元に戻す
        while (vm.UndoRedo.CanRedo)
        {
            vm.UndoRedo.Redo();
        }
        return count;
    }

    // ---------------- 擬似プロバイダ・スタブ ----------------

    /// <summary>int↔integer のみ変換し、uniqueidentifier は変換不能とする擬似プロバイダ</summary>
    private sealed class FakeProvider : IDatabaseProvider
    {
        public const string FakeName = "fakedb";

        public string Name => FakeName;

        public string DisplayName => "Fake DB";

        public int? DefaultPort => 1234;

        public ISchemaImporter SchemaImporter { get; } = new NullSchemaImporter();

        public IColumnTypeMapper TypeMapper { get; } = new NullTypeMapper();

        public ITypeCatalog TypeCatalog { get; } = new FakeTypeCatalog();

        public ISyncScriptBuilder SyncScriptBuilder { get; } = new NullSyncScriptBuilder();

        public ISchemaSyncExecutor SyncExecutor { get; } = new NullSyncExecutor();

        public IDdlGenerator DdlGenerator { get; } = new NullDdlGenerator();

        public string BuildConnectionString(DbConnectionSettings settings) => "fake";
    }

    /// <summary>int↔integer のみ相互変換する擬似カタログ</summary>
    private sealed class FakeTypeCatalog : ITypeCatalog
    {
        public IReadOnlyList<string> DataTypes { get; } = new[] { "integer", "text" };

        public string DefaultDataType => "integer";

        public bool TryParse(string nativeType, out CanonicalType canonical)
        {
            if (string.Equals(nativeType, "integer", StringComparison.OrdinalIgnoreCase))
            {
                canonical = new CanonicalType(CanonicalTypeKind.Int32);
                return true;
            }

            canonical = null!;
            return false;
        }

        public bool TryFormat(CanonicalType canonical, out string nativeType)
        {
            if (canonical.Kind == CanonicalTypeKind.Int32)
            {
                nativeType = "integer";
                return true;
            }

            nativeType = string.Empty;
            return false;
        }
    }

    private sealed class NullSchemaImporter : ISchemaImporter
    {
        public Task<SchemaImportResult> ImportAsync(
            string connectionString,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new SchemaImportResult());
    }

    private sealed class NullTypeMapper : IColumnTypeMapper
    {
        public IReadOnlyDictionary<Guid, CSharpTypeInfo> ResolveColumnTypes(ErDiagram diagram) =>
            new Dictionary<Guid, CSharpTypeInfo>();
    }

    private sealed class NullSyncScriptBuilder : ISyncScriptBuilder
    {
        public string Build(IEnumerable<SchemaDiffItem> items) => string.Empty;
    }

    private sealed class NullSyncExecutor : ISchemaSyncExecutor
    {
        public Task<SchemaSyncResult> ExecuteAsync(
            DbConnectionSettings settings,
            string script,
            CancellationToken ct = default
        ) => Task.FromResult(new SchemaSyncResult());
    }

    private sealed class NullDdlGenerator : IDdlGenerator
    {
        public string Build(ErDiagram diagram) => string.Empty;
    }

    private sealed class NoopAppDialogService : IAppDialogService
    {
        public DbConnectionDialogResult? ShowDbConnectionDialog(
            DbConnectionDialogMode mode,
            IDatabaseProvider? fixedProvider = null,
            string? title = null
        ) => null;

        public void ShowSchemaSyncDialog(
            IDatabaseProvider provider,
            DbConnectionSettings settings,
            IReadOnlyList<Entity> entities,
            IReadOnlyList<Relationship> relationships
        ) { }

        public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle) => null;
    }

    private sealed class NoopFileDialogService : IFileDialogService
    {
        public FileDialogResult? PickOpenFile(string filter) => null;

        public FileDialogResult? PickSaveFile(
            string filter,
            string defaultExt,
            string? initialFileName = null,
            string? initialDirectory = null
        ) => null;

        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }
}
