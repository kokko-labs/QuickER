using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Services;

/// <summary><see cref="TableDefinitionDocumentImporter" /> の定義書取込と整合性検証を検証するテストクラス</summary>
public class TableDefinitionDocumentImporterTests
{
    /// <summary>本アプリが出力した定義書を再取込し、エンティティ・列・リレーションが往復保持されることを検証する</summary>
    [Fact(DisplayName = "このアプリが出力した定義書をそのまま再取込できる")]
    public void Load_RoundTripsExportedWorkbook()
    {
        var vm = new MainViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "Category",
                Description = "カテゴリ",
                Memo = "分類用",
                Columns =
                {
                    new Column
                    {
                        Name = "CategoryId",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                        Description = "主キー",
                    },
                    new Column
                    {
                        Name = "CategoryName",
                        DataType = "nvarchar(50)",
                        IsNullable = false,
                        Description = "名称",
                    },
                },
            }
        );
        var child = new EntityViewModel(
            new Entity
            {
                TableName = "Product",
                Description = "商品",
                Columns =
                {
                    new Column
                    {
                        Name = "ProductId",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "CategoryId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
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
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[1].Id,
                    ConstraintName = "FK_Product_Category",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
                parent,
                child
            )
        );

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(vm.ToDiagramModel());
        var diagram = TableDefinitionDocumentImporter.Load(workbook);

        diagram.Entities.Should().HaveCount(2);
        diagram.Relationships.Should().ContainSingle();
        diagram
            .Entities.Should()
            .ContainSingle(entity =>
                entity.TableName == "Category"
                && entity.Description == "カテゴリ"
                && entity.Memo == "分類用"
            );
        diagram
            .Entities.Should()
            .ContainSingle(entity =>
                entity.TableName == "Product"
                && entity.Columns.Any(column => column.Name == "CategoryId" && column.IsForeignKey)
            );
        diagram.Relationships[0].ConstraintName.Should().Be("FK_Product_Category");
        diagram.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
        diagram.Relationships[0].OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        diagram.Relationships[0].OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);
    }

    /// <summary>一覧に対応する詳細シートが欠落している場合に取込が例外となることを検証する</summary>
    [Fact(DisplayName = "詳細シートが不足していると取り込みをエラーにする")]
    public void Load_ThrowsWhenDetailSheetIsMissing()
    {
        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("テーブル一覧");
        var relationship = workbook.Worksheets.Add("リレーション一覧");
        summary.Cell(1, 1).Value = "No.";
        summary.Cell(1, 2).Value = "詳細";
        summary.Cell(1, 3).Value = "テーブル名";
        summary.Cell(2, 1).Value = 1;
        summary.Cell(2, 2).Value = "詳細";
        summary.Cell(2, 3).Value = "Users";
        relationship.Cell(1, 1).Value = "No.";

        var act = () => TableDefinitionDocumentImporter.Load(workbook);

        // 詳細シート自体が1枚も無いため、実際に投げられるのは件数不一致（テーブル一覧1件・詳細シート0件）。
        // 製品コードと同じ resx キーから期待値を導出し、カルチャに依らず完全一致で検証する
        act.Should().Throw<InvalidDataException>().WithMessage(GuiStrings.TableDoc_CountMismatch);
    }

    /// <summary>リレーション一覧の参照カラムが実在しない場合に取込が例外となることを検証する</summary>
    [Fact(DisplayName = "リレーション一覧の参照カラムが存在しないと取り込みをエラーにする")]
    public void Load_ThrowsWhenRelationshipColumnDoesNotExist()
    {
        var vm = new MainViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "Parent",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                },
            }
        );
        var child = new EntityViewModel(
            new Entity
            {
                TableName = "Child",
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
                        Name = "ParentId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
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
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[1].Id,
                },
                parent,
                child
            )
        );

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(vm.ToDiagramModel());
        workbook.Worksheet("リレーション一覧").Cell(2, 4).Value = "MissingColumn";

        var act = () => TableDefinitionDocumentImporter.Load(workbook);

        // 製品コードと同じ resx キーからフォーマット済みメッセージを導出し、カルチャに依らず完全一致で検証する
        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(GuiStrings.TableDoc_RelChildColumnNotFound, "Child", "MissingColumn")
            );
    }
}
