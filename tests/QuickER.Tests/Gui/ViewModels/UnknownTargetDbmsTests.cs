using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 図の対象 DBMS が知らない方言名（手で編集した図の "foo" など）でも、名前の上限を引く経路が例外にならないことを検証する
/// </summary>
/// <remarks>
/// 方言名から上限を引く <c>IdentifierLengthLimits.ForDbms</c> は知らない名前で例外にする（方言の宣言は既定値を持たない）。
/// 図から方言名を取る経路は <c>ForDiagramTarget</c> を通し、知らない名前なら上限を検査しない。GUI は読込で知らない方言を
/// SQL Server へ寄せるので、上限はプロバイダの名前から引く。
/// </remarks>
public sealed class UnknownTargetDbmsTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-unknown-dbms-" + Guid.NewGuid().ToString("N")
    );

    public UnknownTargetDbmsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    /// <summary>対象 DBMS を "foo" と書いた図のファイル（親 p・子 c・外部キー 1 本）を作る</summary>
    private string WriteUnknownDbmsDiagram()
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
        var path = Path.Combine(_folder, "unknown.json");
        JsonStorageService.SaveAtomic(
            path,
            new DiagramDocument
            {
                Schema = new ErDiagram
                {
                    TargetDbms = "foo",
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
                },
            }
        );

        return path;
    }

    [Fact(
        DisplayName = "知らない方言名の図を GUI で開き、名前を確定し、対象 DBMS を切り替えても例外にならない"
    )]
    public void Gui_OpenEditAndSwitch_DoesNotThrow()
    {
        var path = WriteUnknownDbmsDiagram();
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(
            dialogs,
            files: new RecordingFileDialogService { OpenResult = new(path, 1) },
            providers: new DatabaseProviderRegistry(
                new IDatabaseProvider[] { new SqlServerProvider(), new PostgreSqlProvider() }
            )
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );

        vm.OpenCommand.Execute(null);

        vm.Entities.Should().HaveCount(2, "図は開ける");
        vm.CurrentProvider.Name.Should()
            .Be(SqlServerProvider.ProviderName, "知らない方言は SQL Server へ寄せる");

        // 名前の確定時の警告（上限はプロバイダの名前から引く）
        var longName = new string('t', 129);
        vm.Entities[1].TableName = longName;
        vm.Entities[1].TableName.Should().Be(longName);
        vm.StatusMessage.Should().Contain(longName).And.Contain("128");

        // 対象 DBMS の切替の確認（切替先の上限で名前を判定する）
        vm.SelectedProvider = vm.AvailableProviders.Single(p =>
            p.Name == PostgreSqlProvider.ProviderName
        );
        vm.CurrentProvider.Name.Should().Be(PostgreSqlProvider.ProviderName);
        dialogs
            .WarningConfirmDetailsMessages.Should()
            .ContainSingle()
            .Which.Details.Should()
            .Contain(longName);
    }
}
