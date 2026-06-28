using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Services;

/// <summary>DbmlExporter / DbmlImporter による DBML 形式の入出力をテストするクラス</summary>
public class DbmlTests
{
    /// <summary>PK・FK 列とリレーションを持つ図から Table ブロックと制約名付き Ref 行が生成されることを検証する</summary>
    [Fact(DisplayName = "DBML 出力で Table と Ref を生成できる")]
    public void Export_BuildsDbmlText()
    {
        var vm = new MainViewModel();
        var customer = new EntityViewModel(
            new Entity
            {
                TableName = "Customer",
                Columns =
                {
                    new Column
                    {
                        Name = "CustomerId",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "CustomerName",
                        DataType = "nvarchar(100)",
                        IsNullable = false,
                    },
                },
            }
        );
        var order = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
                Columns =
                {
                    new Column
                    {
                        Name = "OrderId",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "CustomerId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(customer);
        vm.Entities.Add(order);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = customer.Columns[0].Id,
                    TargetColumnId = order.Columns[1].Id,
                    ConstraintName = "FK_Orders_Customer",
                },
                customer,
                order
            )
        );

        var dbml = DbmlExporter.Build(vm.ToDiagramModel());

        dbml.Should().Contain("Table Customer {");
        dbml.Should().Contain("CustomerId int [pk, not null]");
        dbml.Should().Contain("CustomerId int [ref, not null]");
        dbml.Should()
            .Contain("Ref: [note: 'FK_Orders_Customer'] Customer.CustomerId < Orders.CustomerId");
    }

    /// <summary>DBML テキストのパースで pk/ref 属性と Ref 行の制約名・1対多種別が復元されることを検証する</summary>
    [Fact(DisplayName = "DBML 読込でエンティティとリレーションを復元できる")]
    public void Import_ParsesEntitiesAndRelationships()
    {
        var text = string.Join(
            Environment.NewLine,
            [
                "Table Customer {",
                "  CustomerId int [pk, not null]",
                "  CustomerName nvarchar(100) [not null]",
                "}",
                string.Empty,
                "Table Orders {",
                "  OrderId int [pk, not null]",
                "  CustomerId int [ref, not null]",
                "}",
                string.Empty,
                "Ref: [note: 'FK_Orders_Customer'] Customer.CustomerId < Orders.CustomerId",
            ]
        );

        var diagram = DbmlImporter.Parse(text);

        diagram.Entities.Should().HaveCount(2);
        diagram.Relationships.Should().ContainSingle();
        diagram
            .Entities.Should()
            .ContainSingle(entity =>
                entity.TableName == "Customer"
                && entity.Columns.Any(column => column.Name == "CustomerId" && column.IsPrimaryKey)
            );
        diagram
            .Entities.Should()
            .ContainSingle(entity =>
                entity.TableName == "Orders"
                && entity.Columns.Any(column => column.Name == "CustomerId" && column.IsForeignKey)
            );
        diagram.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
        diagram.Relationships[0].ConstraintName.Should().Be("FK_Orders_Customer");
    }

    /// <summary>SaveTo で書き出した DBML ファイルを Load で読み戻し、エンティティとリレーションが往復で保持されることを検証する</summary>
    [Fact(DisplayName = "DBML ファイルの SaveTo と Load を往復できる")]
    public void SaveAndLoad_RoundTrip()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].TableName = "Parent";
        vm.Entities[0].Columns[0].Name = "ParentId";
        vm.Entities[1].TableName = "Child";
        vm.Entities[1].Columns[0].Name = "ChildId";
        vm.Entities[1]
            .Columns.Add(
                new ColumnViewModel(
                    new Column
                    {
                        Name = "ParentId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
                    }
                )
            );
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        vm.Relationships[0].TargetColumnId = vm.Entities[1].Columns[1].Id;
        vm.Relationships[0].ConstraintName = "FK_Child_Parent";

        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.dbml");

        try
        {
            DbmlExporter.SaveTo(vm.ToDiagramModel(), path);
            var diagram = DbmlImporter.Load(path);

            diagram.Entities.Should().HaveCount(2);
            diagram.Relationships.Should().ContainSingle();
            diagram.Entities.Should().Contain(entity => entity.TableName == "Parent");
            diagram.Entities.Should().Contain(entity => entity.TableName == "Child");
            diagram.Relationships[0].ConstraintName.Should().Be("FK_Child_Parent");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
