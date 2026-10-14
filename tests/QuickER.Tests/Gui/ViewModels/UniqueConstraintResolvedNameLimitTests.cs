using System.ComponentModel;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// プロパティパネルの一意制約カードが見せる合成名（<see cref="UniqueConstraintViewModel.ResolvedName"/>）が、
/// 図の対象 DBMS の識別子の長さの上限で縮めた名前（その DBMS の DDL と同じ名前）になり、方言を切り替えると表示し直すことを検証する
/// </summary>
/// <remarks>
/// 上限は <see cref="MainViewModel"/> がエンティティを図へ載せるときに渡す（<c>EntityViewModel.IdentifierLimitSource</c>）。
/// 経路の正本は <see cref="ConstraintNames"/> の XmlDoc。
/// </remarks>
public class UniqueConstraintResolvedNameLimitTests
{
    private static readonly string TableName = new('t', 60);

    /// <summary>SQL Server と PostgreSQL を登録した VM</summary>
    private static MainViewModel CreateVm() =>
        new(
            new StubDialogService(),
            new NoopAppDialogService(),
            new NullFileDialogService(),
            providers: new DatabaseProviderRegistry(
                new IDatabaseProvider[] { new SqlServerProvider(), new PostgreSqlProvider() }
            )
        );

    private static ErDiagram Diagram()
    {
        var entity = new Entity
        {
            TableName = TableName,
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
                    Name = "code",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [entity.Columns[1].Id] });

        return new ErDiagram { TargetDbms = "sqlserver", Entities = { entity } };
    }

    [Fact(
        DisplayName = "一意制約の合成名のプレビューは対象 DBMS の上限で縮め、方言を切り替えると表示し直す"
    )]
    public void ResolvedName_FollowsTargetDbmsLimit()
    {
        var vm = CreateVm();
        vm.ReplaceDiagramFromModule(Diagram());
        var constraint = vm.Entities[0].UniqueConstraints[0];
        var changed = new List<string?>();
        ((INotifyPropertyChanged)constraint).PropertyChanged += (_, e) =>
            changed.Add(e.PropertyName);

        constraint
            .ResolvedName.Should()
            .Be($"UQ_{TableName}_code", "SQL Server（128）の上限内なので縮めない");

        vm.SelectedProvider = vm.AvailableProviders.Single(p => p.Name == "postgresql");

        changed.Should().Contain(nameof(UniqueConstraintViewModel.ResolvedName));
        constraint
            .ResolvedName.Should()
            .Be(
                UniqueConstraint.SynthesizeName(
                    TableName,
                    ["code"],
                    IdentifierLengthLimits.PostgreSql
                )
            )
            .And.NotBe($"UQ_{TableName}_code", "PostgreSQL（63 バイト）の上限を超えるので縮む");
    }

    private sealed class NoopAppDialogService : IAppDialogService
    {
        public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle) => null;

        public void ShowAboutDialog(AboutInfo info) { }
    }
}
