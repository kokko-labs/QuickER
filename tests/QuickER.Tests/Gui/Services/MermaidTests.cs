using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>MermaidExporter / MermaidImporter による Mermaid 形式の入出力を検証するテストクラス</summary>
public class MermaidTests
{
    /// <summary>PK・FK 列とリレーションを持つ図から erDiagram 記法が生成されることを検証する</summary>
    [Fact(DisplayName = "Mermaid 出力で erDiagram 記法を生成できる")]
    public void Export_BuildsErDiagramText()
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

        var mermaid = MermaidExporter.Build(vm.ToDiagramModel());

        mermaid.Should().Contain("erDiagram");
        mermaid.Should().Contain("Customer {");
        mermaid.Should().Contain("int CustomerId PK");
        mermaid.Should().Contain("int CustomerId FK");
        mermaid.Should().Contain("Customer ||--o{ Orders : FK_Orders_Customer");
    }

    /// <summary>Mermaid テキストの解析でエンティティ・列・リレーションの端点が復元されることを検証する</summary>
    [Fact(DisplayName = "Mermaid 読込でエンティティとリレーションを復元できる")]
    public void Import_ParsesEntitiesAndRelationships()
    {
        var text = string.Join(
            Environment.NewLine,
            [
                "erDiagram",
                "    Customer {",
                "        int CustomerId PK",
                "        nvarchar(100) CustomerName",
                "    }",
                "    Orders {",
                "        int OrderId PK",
                "        int CustomerId FK",
                "    }",
                "    Customer ||--o{ Orders : FK_Orders_Customer",
            ]
        );

        var diagram = MermaidImporter.Parse(text);

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

        var source = diagram.Entities.Single(entity =>
            entity.Id == diagram.Relationships[0].SourceEntityId
        );
        var target = diagram.Entities.Single(entity =>
            entity.Id == diagram.Relationships[0].TargetEntityId
        );
        source.TableName.Should().Be("Customer");
        target.TableName.Should().Be("Orders");
        source
            .Columns.Should()
            .ContainSingle(column => column.Id == diagram.Relationships[0].SourceColumnId);
        target
            .Columns.Should()
            .ContainSingle(column => column.Id == diagram.Relationships[0].TargetColumnId);
    }

    /// <summary>SaveTo で書き出した Mermaid ファイルを Load で読み戻し、内容が往復保持されることを検証する</summary>
    [Fact(DisplayName = "Mermaid ファイルの SaveTo と Load を往復できる")]
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

        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.mmd");

        try
        {
            MermaidExporter.SaveTo(vm.ToDiagramModel(), path);
            var diagram = MermaidImporter.Load(path);

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

    /// <summary>括弧付き型が出力時に正規化され、読込時に元の SQL 型名へ復元されることを検証する</summary>
    [Fact(
        DisplayName = "decimal(10,2) や nvarchar(100) は出力→読込のラウンドトリップで元の型名に復元できる"
    )]
    public void Export_Import_NormalizesAndDenormalizesDataTypes()
    {
        var vm = new MainViewModel();
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "Product",
                Columns =
                {
                    new Column
                    {
                        Name = "Price",
                        DataType = "decimal(10,2)",
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "ProductName",
                        DataType = "nvarchar(100)",
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(entity);

        // Act: 出力して Mermaid テキストを確認し、再度読み込む
        var mermaidText = MermaidExporter.Build(vm.ToDiagramModel());
        var diagram = MermaidImporter.Parse(mermaidText);

        // Assert: Mermaid テキスト上では正規化済み形式になっている
        mermaidText.Should().Contain("decimal_10_2");
        mermaidText.Should().Contain("nvarchar_100");

        // Assert: 読み込み後は元の SQL 型名に復元されている
        var product = diagram.Entities.First(e => e.TableName == "Product");
        product.Columns.First(c => c.Name == "Price").DataType.Should().Be("decimal(10,2)");
        product.Columns.First(c => c.Name == "ProductName").DataType.Should().Be("nvarchar(100)");
    }
}
