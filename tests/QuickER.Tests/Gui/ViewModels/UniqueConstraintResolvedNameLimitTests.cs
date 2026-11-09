using System.ComponentModel;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Dialects;
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
/// 図の対象 DBMS の識別子の長さの上限で縮めた名前（その DBMS の DDL と同じ名前）になり、方言を切り替えると表示し直すこと、
/// ほかの表の制約名とぶつかると図全体で決めた番号つきの名前になり、図が変わると表示し直すことを検証する
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

    /// <summary>
    /// 表の名前を変えて、合成名がほかの表の一意制約の名前と同じになると番号つきの名前になり、元に戻すと戻ることを検証する
    /// （名前は図全体で決める＝<see cref="ConstraintNames.ResolveUniqueConstraintNames"/>。表示し直すきっかけは図の世代）。
    /// 明示の名前をほかの制約名へ重ねる書き換えは入口（ツール・名前の欄）が断るので、表の名前を変えて合成名の側を重ねて確かめる
    /// </summary>
    [Fact(
        DisplayName = "一意制約の合成名のプレビューは、ほかの表の制約名とぶつかると番号つきになり、元に戻すと戻る"
    )]
    public void ResolvedName_FollowsCollisionsAcrossTables()
    {
        var vm = CreateVm();
        var diagram = Diagram();
        diagram.Entities.Add(
            new Entity
            {
                TableName = "other",
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
                        Name = "y",
                        DataType = "int",
                        IsNullable = false,
                    },
                },
            }
        );
        vm.ReplaceDiagramFromModule(diagram);
        var constraint = vm.Entities[0].UniqueConstraints[0];
        var synthesized = $"UQ_{TableName}_code";
        constraint.ResolvedName.Should().Be(synthesized);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)constraint).PropertyChanged += (_, e) =>
            changed.Add(e.PropertyName);

        // ツールとプロパティパネルの名前の欄は既存の制約名（合成名を含む）と同じ名前を断るので、ほかの表の一意制約に
        // まだぶつからない名前を付けてから、表の名前を変えて合成名をその名前へ重ねる（取込・手で編集した図でも起きる形）
        const string renamed = "renamed";
        var renamedSynthesized = $"UQ_{renamed}_code";
        ErDiagramDynamicTools
            .Execute(
                "set_unique_constraint",
                JsonSerializer.SerializeToElement(
                    new
                    {
                        table_name = "other",
                        columns = new[] { "y" },
                        name = renamedSynthesized,
                    }
                ),
                vm
            )
            .Success.Should()
            .BeTrue();
        changed.Clear();
        vm.Entities[0].TableName = renamed;

        changed.Should().Contain(nameof(UniqueConstraintViewModel.ResolvedName));
        constraint.ResolvedName.Should().Be(renamedSynthesized + "_2", "ほかの表の制約名を避ける");

        vm.UndoCommand.Execute(null);

        constraint.ResolvedName.Should().Be(synthesized, "ぶつかる名前が消えたので番号は要らない");
    }

    private sealed class NoopAppDialogService : IAppDialogService
    {
        public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle) => null;

        public void ShowAboutDialog(AboutInfo info) { }
    }
}
