using System.Globalization;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// テーブル定義書（Excel / HTML）のキー表記における一意制約（<c>UQ{n}</c>）の出力と、
/// Excel 取込での復元を検証するテストクラス
/// </summary>
/// <remarks>
/// 連番は制約の登場順で、<b>同じ番号＝同じ制約</b>（複合制約は構成列すべてに同じ番号が並ぶ）。
/// 1 列が複数の制約に参加する場合は外部キー連番と同じくカンマ連結する。
/// </remarks>
public class TableDefinitionUniqueConstraintTests
{
    /// <summary>Category（親）と Product（子・FK＋一意制約 2 件）を持つサンプル図を作る</summary>
    /// <remarks>
    /// Product の制約は UQ1＝(Sku)＝PK でない単独列、UQ2＝(CategoryId, Code)＝FK 列を含む複合。
    /// これで <c>UQ1</c> / <c>FK1/UQ2</c> / <c>UQ2</c> の 3 パターンが同時に検証できる。
    /// </remarks>
    private static ErDiagram BuildDiagram()
    {
        var categoryId = new Column
        {
            Name = "CategoryId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var category = new Entity { TableName = "Category", Columns = { categoryId } };

        var productId = new Column
        {
            Name = "ProductId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var sku = new Column
        {
            Name = "Sku",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        var productCategoryId = new Column
        {
            Name = "CategoryId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "Code",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        var product = new Entity
        {
            TableName = "Product",
            Columns = { productId, sku, productCategoryId, code },
            UniqueConstraints =
            {
                new UniqueConstraint { ColumnIds = [sku.Id] },
                new UniqueConstraint
                {
                    Name = "UQ_Product_Category_Code",
                    ColumnIds = [productCategoryId.Id, code.Id],
                },
            },
        };

        return new ErDiagram
        {
            Entities = { category, product },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = category.Id,
                    TargetEntityId = product.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(categoryId.Id, productCategoryId.Id)],
                    ConstraintName = "FK_Product_Category",
                },
            },
        };
    }

    [Fact(DisplayName = "HTML 定義書のキー欄へ UQ{n}（同番＝同一制約）を出力する")]
    public void HtmlExport_WritesUniqueConstraintLabels()
    {
        var html = TableDefinitionHtmlExporter.Build(
            BuildDiagram(),
            culture: new CultureInfo("en")
        );

        // 単独列の制約は UQ1、FK 列を含む複合制約は FK1/UQ2 と UQ2（同番＝同じ制約）
        html.Should().Contain("<td>UQ1</td>");
        html.Should().Contain("<td>FK1/UQ2</td>");
        html.Should().Contain("<td>UQ2</td>");
    }

    [Fact(DisplayName = "Excel 定義書は一意制約（複合・複数件）を往復で復元する")]
    public void ExcelRoundTrip_RestoresUniqueConstraints()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildDiagram(),
            culture: new CultureInfo("en")
        );

        var diagram = TableDefinitionDocumentImporter.Load(workbook);

        var product = diagram.Entities.Single(entity => entity.TableName == "Product");
        var restored = product
            .UniqueConstraints.Select(constraint =>
                constraint
                    .ColumnIds.Select(id => product.Columns.Single(column => column.Id == id).Name)
                    .ToArray()
            )
            .ToArray();

        // 番号順に復元され、複合制約は 1 件へまとまる（制約名は定義書に載らないため未設定＝合成名になる）
        restored
            .Should()
            .BeEquivalentTo(new[] { new[] { "Sku" }, new[] { "CategoryId", "Code" } });
        product.UniqueConstraints.Should().OnlyContain(constraint => constraint.Name == null);

        // PK / FK も併せて復元される
        product
            .Columns.Single(column => column.Name == "ProductId")
            .IsPrimaryKey.Should()
            .BeTrue();
        product
            .Columns.Single(column => column.Name == "CategoryId")
            .IsForeignKey.Should()
            .BeTrue();
    }

    [Fact(DisplayName = "一意制約が無い図のキー表記は PK / FK1 のみ")]
    public void Export_WithoutUniqueConstraints_KeepsExistingLabels()
    {
        var diagram = BuildDiagram();
        diagram.Entities.Single(entity => entity.TableName == "Product").UniqueConstraints.Clear();

        var html = TableDefinitionHtmlExporter.Build(diagram, culture: new CultureInfo("en"));

        html.Should().Contain("<td>PK</td>");
        html.Should().Contain("<td>FK1</td>");
        html.Should().NotContain("UQ");
    }
}
