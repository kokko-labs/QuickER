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

    [Fact(DisplayName = "テーブル名が対象 DBMS の上限を超えると警告し、値は受け付ける")]
    public void TableName_OverLimit_WarnsAndKeepsValue()
    {
        var vm = CreateVm("postgresql");
        var name = Over(PostgreSql);

        vm.Entities[1].TableName = name;

        vm.Entities[1].TableName.Should().Be(name);
        vm.StatusMessage.Should().Contain(name).And.Contain("63").And.Contain("postgresql");
    }

    [Fact(DisplayName = "カラム名が対象 DBMS の上限を超えると警告する")]
    public void ColumnName_OverLimit_Warns()
    {
        var vm = CreateVm("postgresql");

        vm.Entities[1].Columns[1].Name = Over(PostgreSql);

        vm.StatusMessage.Should().Contain("63");
    }

    [Fact(DisplayName = "外部キーの制約名が対象 DBMS の上限を超えると警告する")]
    public void ForeignKeyName_OverLimit_Warns()
    {
        var vm = CreateVm("postgresql");

        vm.Relationships[0].ConstraintName = Over(PostgreSql);

        vm.StatusMessage.Should().Contain("63");
    }

    [Fact(DisplayName = "一意制約の名前が対象 DBMS の上限を超えると警告する")]
    public void UniqueConstraintName_OverLimit_Warns()
    {
        var vm = CreateVm("postgresql");

        vm.Entities[1].UniqueConstraints[0].Name = Over(PostgreSql);

        vm.StatusMessage.Should().Contain("63");
    }

    [Fact(DisplayName = "上限ちょうどの名前・上限なしの対象 DBMS では警告しない")]
    public void WithinLimit_DoesNotWarn()
    {
        var vm = CreateVm("postgresql");
        var ready = vm.StatusMessage;
        vm.Entities[1].TableName = new string('a', PostgreSql.Max);

        vm.StatusMessage.Should().Be(ready);

        var sqlite = CreateVm("sqlite");
        sqlite.Entities[1].TableName = new string('a', 500);

        sqlite.StatusMessage.Should().Be(ready);
    }

    [Fact(DisplayName = "MySQL では BMP の外の文字を含む名前を、表せない文字として警告する")]
    public void MySql_SupplementaryCharacter_WarnsAsUnrepresentable()
    {
        var vm = CreateVm("mysql");

        vm.Entities[1].Columns[1].Name = EmojiName;

        vm.StatusMessage.Should()
            .Be(string.Format(Strings.Status_NameNotRepresentable, EmojiName, "mysql"));
    }

    [Fact(
        DisplayName = "MySQL のテーブル名は、ファイル名での長さを超えても警告する（51 文字の漢字）"
    )]
    public void MySql_TableFileName_Warns()
    {
        var vm = CreateVm("mysql");

        vm.Entities[1].TableName = new string(char.ConvertFromUtf32(0x8868)[0], 51);

        vm.StatusMessage.Should().Contain("251");
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
