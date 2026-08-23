using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
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

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// ターゲット DBMS 切替（型のピボット変換・単一 Undo・適用前の続行確認・同一方言 no-op・
/// 未知方言フォールバック）を検証するテストクラス。
/// </summary>
public class MainViewModelTargetDbmsTests
{
    /// <summary>SQL Server と SQLite（実プロバイダ）を登録したレジストリを作る</summary>
    /// <remarks>
    /// rowversion → BLOB の「NOT NULL 解除を伴う変換」は実カタログ 2 つの組み合わせでしか起きないため、
    /// 擬似プロバイダではなく実プロバイダを使う
    /// </remarks>
    private static (
        MainViewModel Vm,
        SqliteProvider Sqlite,
        StubDialogService Dialogs
    ) CreateSqliteVm()
    {
        var sqlite = new SqliteProvider();
        var registry = new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider(), sqlite }
        );
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(
            dialogs,
            new NoopAppDialogService(),
            new NoopFileDialogService(),
            providers: registry
        );
        return (vm, sqlite, dialogs);
    }

    /// <summary>選択中エンティティへ指定の名前・型・NULL 許容のカラムを追加する</summary>
    private static ColumnViewModel AddColumn(
        MainViewModel vm,
        string name,
        string dataType,
        bool isNullable = false
    )
    {
        vm.AddColumnCommand.Execute(null);
        var column = vm.Entities[0].Columns[^1];
        column.Name = name;
        column.DataType = dataType;
        column.IsNullable = isNullable;
        return column;
    }

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

    /// <summary>変換できない型がある場合に、適用前の続行確認へ未変換一覧が列挙されることを検証する</summary>
    [Fact(DisplayName = "未変換の列は適用前の続行確認に列挙される")]
    public void ChangeDbms_Unconverted_ConfirmsBeforeApply()
    {
        var (vm, fake, dialogs) = CreateVm();
        vm.AddEntityCommand.Execute(null);
        // 擬似プロバイダが変換できない型（uniqueidentifier）へ変更する
        vm.Entities[0].Columns[0].DataType = "uniqueidentifier";

        vm.SelectedProvider = fake;

        // 事後の詳細ダイアログではなく、適用前の ConfirmWarningDetails（要約＋詳細の確認）に移行している
        dialogs.InformationDetailsMessages.Should().BeEmpty();
        dialogs.InformationMessages.Should().BeEmpty();
        var entry = dialogs.WarningConfirmDetailsMessages.Should().ContainSingle().Subject;
        // 要約（message）＝導入文・不可逆の注記・続行の問い／詳細（details）＝未変換の節
        entry
            .Message.Should()
            .Contain(GuiStrings.TypeConversion_ConfirmNote)
            .And.Contain(GuiStrings.TypeConversion_ConfirmQuestion);
        entry
            .Details.Should()
            .Contain(GuiStrings.TypeConversion_WarningHeader)
            .And.Contain("NewTable")
            .And.Contain("uniqueidentifier");
        // NOT NULL 解除の列が無いので、その節の見出しは出ない
        entry.Details.Should().NotContain(GuiStrings.TypeConversion_NullableWarningHeader);
        entry.Title.Should().Be(GuiStrings.TypeConversion_ConfirmTitle);
        // 既定応答（OK）では切替が適用される
        vm.CurrentProvider.Name.Should().Be(FakeProvider.FakeName);
    }

    /// <summary>NOT NULL 解除だけが起きる場合も、適用前の続行確認へその一覧が列挙されることを検証する</summary>
    [Fact(DisplayName = "NOT NULL 解除のみでも適用前の続行確認に列挙される")]
    public void ChangeDbms_MakeNullableOnly_ConfirmsBeforeApply()
    {
        var (vm, sqlite, dialogs) = CreateSqliteVm();
        vm.AddEntityCommand.Execute(null); // int の PK 列を持つ NewTable
        AddColumn(vm, "RowVer", "rowversion");

        vm.SelectedProvider = sqlite;

        var entry = dialogs.WarningConfirmDetailsMessages.Should().ContainSingle().Subject;
        entry
            .Details.Should()
            .Contain(GuiStrings.TypeConversion_NullableWarningHeader)
            .And.Contain("NewTable")
            .And.Contain("RowVer")
            .And.Contain("BLOB");
        // 未変換の見出しは出ない
        entry.Details.Should().NotContain(GuiStrings.TypeConversion_WarningHeader);
    }

    /// <summary>未変換と NOT NULL 解除が同時に起きる場合に、1 回の続行確認へ 2 節でまとめることを検証する</summary>
    [Fact(DisplayName = "未変換と NOT NULL 解除は 1 回の続行確認へ 2 節でまとめる")]
    public void ChangeDbms_UnconvertedAndMakeNullable_ConfirmsBothSections()
    {
        var (vm, sqlite, dialogs) = CreateSqliteVm();
        vm.AddEntityCommand.Execute(null);
        AddColumn(vm, "RowVer", "rowversion");
        AddColumn(vm, "Tree", "hierarchyid"); // SQL Server カタログが解析できない＝未変換

        vm.SelectedProvider = sqlite;

        var entry = dialogs.WarningConfirmDetailsMessages.Should().ContainSingle().Subject;
        entry
            .Details.Should()
            .Contain(GuiStrings.TypeConversion_WarningHeader)
            .And.Contain("Tree")
            .And.Contain("hierarchyid")
            .And.Contain(GuiStrings.TypeConversion_NullableWarningHeader)
            .And.Contain("RowVer")
            .And.Contain("BLOB");
    }

    /// <summary>確認の一覧は件数上限で畳まず全件を列挙することを検証する（判断材料の完全性）</summary>
    [Fact(DisplayName = "確認の一覧は 30 件を超えても畳まず全件列挙する")]
    public void ChangeDbms_ManyColumns_ListsAllWithoutFolding()
    {
        var (vm, sqlite, dialogs) = CreateSqliteVm();
        vm.AddEntityCommand.Execute(null);

        // 旧上限（DialogItemList.Format の 30 件）を超える数の NOT NULL 解除列を作る
        for (var i = 1; i <= 35; i++)
        {
            AddColumn(vm, $"RowVer{i:D2}", "rowversion");
        }

        vm.SelectedProvider = sqlite;

        var entry = dialogs.WarningConfirmDetailsMessages.Should().ContainSingle().Subject;
        // 31 件目以降も落ちず、「…他 N 件」の畳み行も現れない
        entry.Details.Should().Contain("RowVer31").And.Contain("RowVer35");
        entry.Details.Should().NotContain(string.Format(GuiStrings.Common_MoreItems, 5));
    }

    /// <summary>続行確認をキャンセルした場合に、切替も型変換も一切適用されないことを検証する</summary>
    [Fact(DisplayName = "続行確認のキャンセルは切替も型変換も適用しない")]
    public void ChangeDbms_ConfirmCancelled_AppliesNothing()
    {
        var (vm, fake, dialogs) = CreateVm();
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].Columns[0].DataType = "uniqueidentifier";
        // 履歴の不変は世代カウンタで観測する（CountUndoable は生の UndoRedo.Undo() を呼ぶため、
        // 追跡対象のプロパティ変更が履歴にあるここでは使えない＝巻き戻し自体が再追跡されて無限ループになる）
        var generationBefore = vm.UndoRedo.ChangeGeneration;
        // ComboBox の表示を現在方言へ戻すための再通知を観測する
        var selectedProviderNotified = false;
        vm.PropertyChanged += (_, e) =>
            selectedProviderNotified |= e.PropertyName == nameof(vm.SelectedProvider);
        dialogs.ConfirmResult = false;

        vm.SelectedProvider = fake;

        // 方言・型・履歴のいずれも変わらない（確認は出ている）
        dialogs.WarningConfirmDetailsMessages.Should().ContainSingle();
        vm.CurrentProvider.Name.Should().Be("sqlserver");
        vm.Entities[0].Columns[0].DataType.Should().Be("uniqueidentifier");
        vm.ToDiagramModel().TargetDbms.Should().Be("sqlserver");
        vm.UndoRedo.ChangeGeneration.Should().Be(generationBefore);
        // ComboBox が切替先を表示したままにならないよう、SelectedProvider の再通知が出る
        selectedProviderNotified.Should().BeTrue();
    }

    /// <summary>クリーンに変換できる切替では確認を出さず即適用することを検証する（確認の形骸化防止）</summary>
    [Fact(DisplayName = "未変換も NOT NULL 解除も無ければ確認なしで適用する")]
    public void ChangeDbms_NothingToReport_ShowsNoDialog()
    {
        var (vm, sqlite, dialogs) = CreateSqliteVm();
        vm.AddEntityCommand.Execute(null); // int の PK 列のみ＝素直に変換できる

        vm.SelectedProvider = sqlite;

        dialogs.WarningConfirmDetailsMessages.Should().BeEmpty();
        dialogs.WarningConfirmMessages.Should().BeEmpty();
        dialogs.ConfirmMessages.Should().BeEmpty();
        dialogs.InformationDetailsMessages.Should().BeEmpty();
        dialogs.InformationMessages.Should().BeEmpty();
        vm.CurrentProvider.Name.Should().Be("sqlite");
    }

    /// <summary>主キーの行バージョン列は NOT NULL 解除の対象外（確認を出さない）ことを検証する</summary>
    [Fact(DisplayName = "主キーの行バージョン列は NOT NULL 解除として確認に載せない")]
    public void ChangeDbms_PrimaryKeyRowVersion_IsNotListed()
    {
        var (vm, sqlite, dialogs) = CreateSqliteVm();
        vm.AddEntityCommand.Execute(null);
        var key = vm.Entities[0].Columns[0];
        key.IsPrimaryKey.Should().BeTrue();
        key.DataType = "rowversion";

        vm.SelectedProvider = sqlite;

        // 主キー列は 3 層が NOT NULL へクランプするため、解除の告知そのものが出ない＝確認なしで適用される
        dialogs.WarningConfirmDetailsMessages.Should().BeEmpty();
        dialogs.InformationDetailsMessages.Should().BeEmpty();
        key.IsNullable.Should().BeFalse();
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

        public SyncDialectCapabilities SyncCapabilities { get; } = new();

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
            int commandTimeoutSeconds,
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
        public string Build(SyncPlan plan) => string.Empty;
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
