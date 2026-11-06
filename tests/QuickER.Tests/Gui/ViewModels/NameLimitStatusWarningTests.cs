using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;
using Strings = QuickER.Resources.Strings;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 利用者が名前（テーブル名・カラム名・外部キーの制約名・一意制約の名前）を確定したとき、対象 DBMS の識別子の長さの上限を超えるか
/// 表せない文字を含めば、ステータスバーの一時通知で警告し、値は受け付けることと、対象 DBMS の切替の確認に切替先が受け付けない名前を
/// 並べることを検証するテストクラス
/// </summary>
/// <remarks>
/// 判定は DDL・同期の入口と同じ <see cref="IdentifierLengthLimit.DescribeViolation"/>。Undo / Redo では出さない
/// （<see cref="QuickER.UndoRedo.DiagramChangeTracker.NameEdited"/> は履歴へ積んだ編集でだけ発火する）。
/// </remarks>
public class NameLimitStatusWarningTests
{
    private static readonly IdentifierLengthLimit PostgreSql = IdentifierLengthLimits.PostgreSql;

    /// <summary>BMP の外の文字（絵文字）を含む名前</summary>
    private static readonly string EmojiName = "x" + char.ConvertFromUtf32(0x1F600);

    private static string Over(IdentifierLengthLimit limit) => new('a', limit.Max + 1);

    /// <summary>4 方言（SQL Server・PostgreSQL・MySQL・SQLite）を登録した VM（既定の VM は SQL Server だけ）</summary>
    private static MainViewModel CreateEmptyVm(StubDialogService? dialogs = null) =>
        new(
            dialogs ?? new StubDialogService(),
            new NoopAppDialogService(),
            new NullFileDialogService(),
            providers: new DatabaseProviderRegistry(
                new IDatabaseProvider[]
                {
                    new SqlServerProvider(),
                    new PostgreSqlProvider(),
                    new MySqlProvider(),
                    new SqliteProvider(),
                }
            )
        );

    private sealed class NoopAppDialogService : IAppDialogService
    {
        public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle) => null;

        public void ShowAboutDialog(AboutInfo info) { }
    }

    /// <summary>親 p・子 c（外部キー 1 本・一意制約 1 つ）を対象 DBMS の図として載せた VM</summary>
    private static MainViewModel CreateVm(string dbms)
    {
        var parent = new Entity
        {
            TableName = "p",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = "c",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "pid",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };
        child.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { child.Columns[1].Id } });
        var diagram = new ErDiagram
        {
            TargetDbms = dbms,
            Entities = { parent, child },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                },
            },
        };
        var vm = CreateEmptyVm();
        vm.ReplaceDiagramFromModule(diagram);

        return vm;
    }

    /// <summary>確定した名前に対するステータスバーの期待の形</summary>
    public enum StatusExpectation
    {
        /// <summary>通知が期待の文と完全に一致する</summary>
        Exact,

        /// <summary>通知が期待の断片をすべて含む</summary>
        Contains,

        /// <summary>通知が変わらない（警告しない）</summary>
        Unchanged,
    }

    /// <summary>
    /// 名前の確定の 1 行（対象 DBMS・確定する場所・名前・期待の形と断片）
    /// </summary>
    /// <param name="Id">行の名前</param>
    /// <param name="Dbms">図の対象 DBMS</param>
    /// <param name="Edit">VM の名前を確定する（<see cref="CreateVm"/> の図の場所）</param>
    /// <param name="Read">確定した場所の名前を読み戻す</param>
    /// <param name="Name">確定する名前</param>
    /// <param name="Expectation">期待の形</param>
    /// <param name="Fragments">完全一致の文（Exact は 1 つ）か、含む断片（Contains）</param>
    public sealed record NameEditRow(
        string Id,
        string Dbms,
        Action<MainViewModel, string> Edit,
        Func<MainViewModel, string?> Read,
        string Name,
        StatusExpectation Expectation,
        params string[] Fragments
    )
    {
        public override string ToString() => Id;
    }

    private static void SetTableName(MainViewModel vm, string name) =>
        vm.Entities[1].TableName = name;

    private static string? GetTableName(MainViewModel vm) => vm.Entities[1].TableName;

    private static void SetColumnName(MainViewModel vm, string name) =>
        vm.Entities[1].Columns[1].Name = name;

    private static string? GetColumnName(MainViewModel vm) => vm.Entities[1].Columns[1].Name;

    /// <summary>名前の確定の行（4 種類の名前の上限超え・上限ちょうど・上限なし・MySQL の表せない文字とファイル名）</summary>
    public static TheoryData<NameEditRow> NameEditRows =>
        new()
        {
            new NameEditRow(
                "table-name-over-limit",
                "postgresql",
                SetTableName,
                GetTableName,
                Over(PostgreSql),
                StatusExpectation.Contains,
                Over(PostgreSql),
                "63",
                "postgresql"
            ),
            new NameEditRow(
                "column-name-over-limit",
                "postgresql",
                SetColumnName,
                GetColumnName,
                Over(PostgreSql),
                StatusExpectation.Contains,
                "63"
            ),
            new NameEditRow(
                "foreign-key-name-over-limit",
                "postgresql",
                (vm, name) => vm.Relationships[0].ConstraintName = name,
                vm => vm.Relationships[0].ConstraintName,
                Over(PostgreSql),
                StatusExpectation.Contains,
                "63"
            ),
            new NameEditRow(
                "unique-constraint-name-over-limit",
                "postgresql",
                (vm, name) => vm.Entities[1].UniqueConstraints[0].Name = name,
                vm => vm.Entities[1].UniqueConstraints[0].Name,
                Over(PostgreSql),
                StatusExpectation.Contains,
                "63"
            ),
            // 上限ちょうどの名前は警告しない
            new NameEditRow(
                "within-limit-postgresql",
                "postgresql",
                SetTableName,
                GetTableName,
                new string('a', PostgreSql.Max),
                StatusExpectation.Unchanged
            ),
            // 上限なしの対象 DBMS では警告しない
            new NameEditRow(
                "unlimited-sqlite",
                "sqlite",
                SetTableName,
                GetTableName,
                new string('a', 500),
                StatusExpectation.Unchanged
            ),
            // MySQL では BMP の外の文字を含む名前を、表せない文字として警告する
            new NameEditRow(
                "mysql-supplementary-character",
                "mysql",
                SetColumnName,
                GetColumnName,
                EmojiName,
                StatusExpectation.Exact,
                string.Format(Strings.Status_NameNotRepresentable, EmojiName, "mysql")
            ),
            // MySQL のテーブル名は、ファイル名での長さを超えても警告する（51 文字の漢字）
            new NameEditRow(
                "mysql-table-file-name",
                "mysql",
                SetTableName,
                GetTableName,
                new string(char.ConvertFromUtf32(0x8868)[0], 51),
                StatusExpectation.Contains,
                "251"
            ),
        };

    [Theory(
        DisplayName = "名前を確定すると、対象 DBMS が受け付けない名前なら警告し（受け付ける名前なら通知を変えず）、値は受け付ける"
    )]
    [MemberData(nameof(NameEditRows))]
    public void NameEdit_WarnsWhenTheTargetRefuses(NameEditRow row)
    {
        var vm = CreateVm(row.Dbms);
        var ready = vm.StatusMessage;

        row.Edit(vm, row.Name);

        row.Read(vm).Should().Be(row.Name, "値は受け付ける");

        switch (row.Expectation)
        {
            case StatusExpectation.Exact:
                vm.StatusMessage.Should().Be(row.Fragments.Single());
                break;

            case StatusExpectation.Contains:
                foreach (var fragment in row.Fragments)
                {
                    vm.StatusMessage.Should().Contain(fragment);
                }

                break;

            case StatusExpectation.Unchanged:
                vm.StatusMessage.Should().Be(ready);
                break;
        }
    }

    [Fact(DisplayName = "元に戻す・やり直すでは警告しない")]
    public void UndoRedo_DoesNotWarn()
    {
        var vm = CreateVm("postgresql");
        var notifications = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusMessage))
            {
                notifications++;
            }
        };
        vm.Entities[1].TableName = Over(PostgreSql);
        notifications.Should().BeGreaterThan(0, "名前を確定したときは警告する（対照）");
        notifications = 0;

        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);

        notifications.Should().Be(0);
    }

    [Fact(
        DisplayName = "図を丸ごと置き換えても警告しない（読み込んだ名前を利用者が確定したわけではない）"
    )]
    public void ReplaceDiagram_DoesNotWarn()
    {
        var vm = CreateVm("postgresql");
        var diagram = new ErDiagram
        {
            TargetDbms = "postgresql",
            Entities = { new Entity { TableName = Over(PostgreSql) } },
        };

        vm.ReplaceDiagramFromModule(diagram);

        vm.StatusMessage.Should().NotContain("63");
    }

    [Fact(
        DisplayName = "対象 DBMS の切替の確認に、切替先が受け付けない名前を場所と理由つきで並べる"
    )]
    public void SwitchTargetDbms_ListsNamesTheTargetRefuses()
    {
        var dialogs = new StubDialogService();
        var vm = CreateEmptyVm(dialogs);
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities =
                {
                    new Entity
                    {
                        TableName = new string('t', 64),
                        Columns =
                        {
                            new Column
                            {
                                Name = EmojiName,
                                DataType = "int",
                                IsPrimaryKey = true,
                                IsNullable = false,
                            },
                        },
                    },
                },
            }
        );

        vm.SelectedProvider = vm.AvailableProviders.Single(p => p.Name == "postgresql");
        vm.CurrentProvider.Name.Should().Be("postgresql", "OK なら切り替わる（名前は残る）");
        var postgres = dialogs.WarningConfirmDetailsMessages.Should().ContainSingle().Subject;
        postgres.Details.Should().Contain(new string('t', 64)).And.Contain("63");
        postgres.Details.Should().NotContain(EmojiName, "PostgreSQL は BMP の外の文字を保存できる");

        dialogs.WarningConfirmDetailsMessages.Clear();
        vm.SelectedProvider = vm.AvailableProviders.Single(p => p.Name == "mysql");
        var mysql = dialogs.WarningConfirmDetailsMessages.Should().ContainSingle().Subject;
        mysql
            .Details.Should()
            .Contain(EmojiName)
            .And.Contain(Strings.TypeConversion_NameLimitNotRepresentable);
    }

    [Fact(DisplayName = "対象 DBMS の切替の確認をキャンセルすると、切り替わらない")]
    public void SwitchTargetDbms_Cancel_KeepsProvider()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateEmptyVm(dialogs);
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = { new Entity { TableName = new string('t', 64) } },
            }
        );

        vm.SelectedProvider = vm.AvailableProviders.Single(p => p.Name == "postgresql");

        vm.CurrentProvider.Name.Should().Be("sqlserver");
    }

    [Fact(DisplayName = "切替先の上限に収まる名前だけなら、名前のための確認は出ない")]
    public void SwitchTargetDbms_NamesWithinLimit_NoConfirmation()
    {
        var dialogs = new StubDialogService();
        var vm = CreateEmptyVm(dialogs);
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = { new Entity { TableName = "orders" } },
            }
        );

        vm.SelectedProvider = vm.AvailableProviders.Single(p => p.Name == "postgresql");

        dialogs.WarningConfirmDetailsMessages.Should().BeEmpty();
        vm.CurrentProvider.Name.Should().Be("postgresql");
    }
}
