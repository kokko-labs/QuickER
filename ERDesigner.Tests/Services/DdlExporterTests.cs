using ClosedXML.Excel;
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
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "Name",
                        DataType = "nvarchar(50)",
                        IsNullable = true,
                    },
                },
            }
        );
        vm.Entities.Add(e);

        var sql = DdlExporter.Build(vm);

        sql.Should().Contain("CREATE TABLE [User]");
        sql.Should().Contain("[Id] int NOT NULL");
        sql.Should().Contain("PRIMARY KEY ([Id])");
        sql.Should().Contain("[Name] nvarchar(50) NULL");
    }

    [Fact(DisplayName = "Build: NULL 許容 OFF の列は NOT NULL が出力される")]
    public void Build_NotNullableColumn_EmitsNotNull()
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
                        Name = "Code",
                        DataType = "nvarchar(20)",
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(e);

        var sql = DdlExporter.Build(vm);

        sql.Should().Contain("[Code] nvarchar(20) NOT NULL");
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
                    new Column { Name = "ParentId", DataType = "int" },
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
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[1].Id,
                },
                parent,
                child
            )
        );

        var sql = DdlExporter.Build(vm);

        sql.Should().Contain("ALTER TABLE [C]");
        sql.Should().Contain("FOREIGN KEY ([ParentId])");
        sql.Should().Contain("REFERENCES [P] ([Id])");
    }

    [Fact(DisplayName = "BuildWorkbook: テーブル定義書のサマリーとテーブル詳細が生成される")]
    public void BuildWorkbook_CreatesSummaryAndEntityWorksheets()
    {
        var vm = new MainViewModel();
        var user = new EntityViewModel(
            new Entity
            {
                TableName = "User",
                Description = "利用者",
                Memo = "業務利用",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                        Description = "主キー",
                    },
                    new Column
                    {
                        Name = "Name",
                        DataType = "nvarchar(50)",
                        IsNullable = false,
                        Description = "氏名",
                    },
                },
            }
        );
        var order = new EntityViewModel(
            new Entity
            {
                TableName = "Order",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "UserId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(user);
        vm.Entities.Add(order);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = user.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = user.Columns[0].Id,
                    TargetColumnId = order.Columns[1].Id,
                    ConstraintName = "FK_Order_User",
                },
                user,
                order
            )
        );
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = user.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = user.Columns[0].Id,
                    TargetColumnId = order.Columns[1].Id,
                    ConstraintName = "FK_Order_User_Audit",
                },
                user,
                order
            )
        );

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(vm);

        workbook.Worksheets.Should().Contain(sheet => sheet.Name == "テーブル一覧");
        workbook.Worksheets.Should().Contain(sheet => sheet.Name == "リレーション一覧");

        var summarySheet = workbook.Worksheet("テーブル一覧");
        summarySheet.Cell(1, 1).GetString().Should().Be("No.");
        summarySheet.Cell(2, 2).GetString().Should().Be("詳細");
        summarySheet.Cell(2, 3).GetString().Should().Be("Order");
        summarySheet.Cell(3, 3).GetString().Should().Be("User");
        summarySheet.Column(3).Width.Should().BeApproximately(35.7109375d, 0.001d);
        summarySheet.Cell(2, 2).Style.Font.FontName.Should().Be("游ゴシック");
        summarySheet.Cell(2, 2).GetHyperlink().InternalAddress.ToString().Should().Be("'Order'!A1");

        var userSheet = workbook.Worksheet("User");
        userSheet.Cell(2, 2).GetString().Should().Be("User");
        userSheet.Cell(2, 3).GetString().Should().Be("利用者");
        userSheet.Cell(5, 2).GetString().Should().Be("Id");
        userSheet.Cell(5, 3).GetString().Should().Be("主キー");
        userSheet.Cell(5, 5).GetString().Should().Be("〇");
        userSheet.Cell(5, 6).GetString().Should().Be("PK");
        userSheet.Cell(6, 2).GetString().Should().Be("Name");
        userSheet.Cell(8, 1).GetString().Should().Be("テーブル一覧に戻る");
        userSheet.Cell(8, 1).GetHyperlink().InternalAddress.ToString().Should().Be("'テーブル一覧'!A1");
        userSheet.PageSetup.PrintAreas.Single().RangeAddress.ToString().Should().Be("A1:G8");

        var orderSheet = workbook.Worksheet("Order");
        orderSheet.Cell(5, 2).GetString().Should().Be("Id");
        orderSheet.Cell(6, 2).GetString().Should().Be("UserId");
        orderSheet.Cell(6, 5).GetString().Should().Be("〇");
        orderSheet.Cell(6, 6).GetString().Should().Be("FK1,FK2");
        orderSheet.Cell(6, 7).GetString().Should().Be("User.Id");
        orderSheet.Cell(8, 1).GetString().Should().Be("テーブル一覧に戻る");
        orderSheet.Cell(8, 1).GetHyperlink().InternalAddress.ToString().Should().Be("'テーブル一覧'!A1");

        var relationshipSheet = workbook.Worksheet("リレーション一覧");
        relationshipSheet.Cell(2, 2).GetString().Should().Be("FK_Order_User");
        relationshipSheet.Cell(2, 3).GetString().Should().Be("User");
        relationshipSheet.Cell(2, 4).GetString().Should().Be("Id");
        relationshipSheet.Cell(2, 5).GetString().Should().Be("Order");
        relationshipSheet.Cell(2, 6).GetString().Should().Be("UserId");
        relationshipSheet.Cell(2, 7).GetString().Should().Be("N:1");
        relationshipSheet.Cell(3, 2).GetString().Should().Be("FK_Order_User_Audit");
    }
}
