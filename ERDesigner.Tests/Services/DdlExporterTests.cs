using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="DdlExporter"/> のテスト。
/// </summary>
public class DdlExporterTests
{
    [Fact(DisplayName = "Build: CREATE TABLE と PRIMARY KEY が出力される")]
    public void Build_EmitsCreateTableAndPk()
    {
        var vm = new MainViewModel();
        var e = new EntityViewModel(
            new Entity
            {
                TableName = "User",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column { Name = "Name", DataType = "nvarchar(50)" },
                },
            }
        );
        vm.Entities.Add(e);

        var sql = DdlExporter.Build(vm);

        sql.Should().Contain("CREATE TABLE [User]");
        sql.Should().Contain("[Id] int");
        sql.Should().Contain("PRIMARY KEY ([Id])");
        sql.Should().Contain("[Name] nvarchar(50)");
    }

    [Fact(DisplayName = "Build: 1対多リレーションが FOREIGN KEY を生成する")]
    public void Build_OneToMany_EmitsForeignKey()
    {
        var vm = new MainViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "P",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        var child = new EntityViewModel(
            new Entity
            {
                TableName = "C",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        vm.Entities.Add(parent);
        vm.Entities.Add(child);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                },
                parent,
                child
            )
        );

        var sql = DdlExporter.Build(vm);

        sql.Should().Contain("ALTER TABLE [C]");
        sql.Should().Contain("FOREIGN KEY ([P_Id])");
        sql.Should().Contain("REFERENCES [P] ([Id])");
    }
}
