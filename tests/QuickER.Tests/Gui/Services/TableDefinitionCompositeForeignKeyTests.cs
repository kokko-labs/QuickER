using System.Globalization;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// テーブル定義書（Excel / HTML）における複合外部キーの表記（リレーション一覧のカンマ区切り複数列・
/// 詳細シートの <c>FK{n}</c> 同番）と、Excel 取込での復元を検証するテストクラス
/// </summary>
/// <remarks>
/// リレーション一覧は 1 行 1 リレーションを保ち、参照元列・参照先列セルへ構成列を宣言順で並べる。
/// 連番は 1 リレーションにつき 1 つで、<b>同じ番号＝同じ外部キー</b>（一意制約の <c>UQ{n}</c> と同じ流儀）。
/// </remarks>
public class TableDefinitionCompositeForeignKeyTests
{
    /// <summary>複合 PK の親 TenantRegion と、複合 FK＋単一 FK を持つ子 TenantUser の図を作る</summary>
    private static ErDiagram BuildDiagram()
    {
        var tenantId = new Column
        {
            Name = "TenantId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var regionCode = new Column
        {
            Name = "RegionCode",
            DataType = "nvarchar(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parent = new Entity { TableName = "TenantRegion", Columns = { tenantId, regionCode } };

        var planId = new Column
        {
            Name = "PlanId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var plan = new Entity { TableName = "Plan", Columns = { planId } };

        var userId = new Column
        {
            Name = "TenantUserId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var tenantRef = new Column
        {
            Name = "TenantRef",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var regionRef = new Column
        {
            Name = "RegionRef",
            DataType = "nvarchar(10)",
            IsForeignKey = true,
            IsNullable = false,
        };
        var planRef = new Column
        {
            Name = "PlanId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var child = new Entity
        {
            TableName = "TenantUser",
            Columns = { userId, tenantRef, regionRef, planRef },
        };

        return new ErDiagram
        {
            Entities = { parent, plan, child },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new(tenantId.Id, tenantRef.Id),
                        new(regionCode.Id, regionRef.Id),
                    ],
                    ConstraintName = "FK_TenantUser_TenantRegion",
                },
                new Relationship
                {
                    SourceEntityId = plan.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(planId.Id, planRef.Id)],
                    ConstraintName = "FK_TenantUser_Plan",
                },
            },
        };
    }

    [Fact(DisplayName = "HTML 定義書は複合外部キーをカンマ区切りの複数列で 1 行に出力する")]
    public void HtmlExport_WritesCompositeColumnsInOneRow()
    {
        var html = TableDefinitionHtmlExporter.Build(
            BuildDiagram(),
            culture: new CultureInfo("en")
        );

        // 参照元列（子）・参照先列（親）とも構成列を宣言順で並べる
        html.Should().Contain("<td>TenantRef, RegionRef</td>");
        html.Should().Contain("<td>TenantId, RegionCode</td>");

        // 詳細シートのキー欄は複合の構成列すべてに同じ番号が並ぶ（単一 FK は別番号）
        html.Should().Contain("<td>FK1</td>");
        html.Should().Contain("<td>FK2</td>");
    }

    [Fact(DisplayName = "Excel 定義書は複合外部キーを往復で復元する")]
    public void ExcelRoundTrip_RestoresCompositeForeignKey()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildDiagram(),
            culture: new CultureInfo("en")
        );

        var diagram = TableDefinitionDocumentImporter.Load(workbook);
        var parent = diagram.Entities.Single(entity => entity.TableName == "TenantRegion");
        var child = diagram.Entities.Single(entity => entity.TableName == "TenantUser");
        var composite = diagram.Relationships.Single(relationship =>
            relationship.ConstraintName == "FK_TenantUser_TenantRegion"
        );

        composite
            .ColumnPairs.Select(pair =>
                (
                    parent.Columns.Single(column => column.Id == pair.SourceColumnId).Name,
                    child.Columns.Single(column => column.Id == pair.TargetColumnId).Name
                )
            )
            .Should()
            .Equal(("TenantId", "TenantRef"), ("RegionCode", "RegionRef"));

        // 単一列の外部キーは 1 組のまま
        diagram
            .Relationships.Single(relationship =>
                relationship.ConstraintName == "FK_TenantUser_Plan"
            )
            .ColumnPairs.Should()
            .ContainSingle();

        // 構成列はすべて FK 化される
        child.Columns.Single(column => column.Name == "TenantRef").IsForeignKey.Should().BeTrue();
        child.Columns.Single(column => column.Name == "RegionRef").IsForeignKey.Should().BeTrue();
    }

    [Fact(DisplayName = "Excel 取込は参照元列と参照先列の数が違う行をエラーにする")]
    public void ExcelImport_ColumnCountMismatch_Throws()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildDiagram(),
            culture: new CultureInfo("en")
        );

        // 参照先列（親側）だけを 1 列に減らし、数の不一致を作る
        var sheet = workbook
            .DefinedNames.Single(defined =>
                defined.Name == TableDefinitionDocumentLayout.RelationshipsDefinedName
            )
            .Ranges.First()
            .Worksheet;
        var row = TableDefinitionDocumentLayout.RelationshipDataStartRow;

        while (sheet.Cell(row, 2).GetString() != "FK_TenantUser_TenantRegion")
        {
            row++;
        }

        sheet.Cell(row, 6).Value = "TenantId";

        var act = () => TableDefinitionDocumentImporter.Load(workbook);

        act.Should().Throw<System.IO.InvalidDataException>();
    }

    /// <summary>
    /// 同じ親子の間に 2 本の外部キー（配送先・請求先）を持つ図を作る。
    /// </summary>
    /// <remarks>
    /// 現実の注文と住所の関係そのもので、定義書は 1 行 1 リレーションで両方を書き出せる。
    /// 重複判定がテーブルの組だけを見ていると、この定義書は読み戻せない（IF4）。
    /// </remarks>
    private static ErDiagram BuildTwoForeignKeysBetweenSameTablesDiagram()
    {
        var addressId = new Column
        {
            Name = "AddressId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var address = new Entity { TableName = "Address", Columns = { addressId } };

        var orderId = new Column
        {
            Name = "OrderId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var shipTo = new Column
        {
            Name = "ShipToAddressId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var billTo = new Column
        {
            Name = "BillToAddressId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var order = new Entity { TableName = "Orders", Columns = { orderId, shipTo, billTo } };

        return new ErDiagram
        {
            Entities = { address, order },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = address.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(addressId.Id, shipTo.Id)],
                    ConstraintName = "FK_Orders_ShipToAddress",
                },
                new Relationship
                {
                    SourceEntityId = address.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(addressId.Id, billTo.Id)],
                    ConstraintName = "FK_Orders_BillToAddress",
                },
            },
        };
    }

    /// <summary>
    /// 同じ親子の間の複数の外部キーが、Excel 定義書の往復で 2 本とも構成列ごと復元される（IF4）。
    /// </summary>
    [Fact(DisplayName = "Excel 定義書は同じ親子間の複数の外部キーを往復で復元する")]
    public void ExcelRoundTrip_RestoresMultipleForeignKeysBetweenSameTables()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildTwoForeignKeysBetweenSameTablesDiagram(),
            culture: new CultureInfo("en")
        );

        var diagram = TableDefinitionDocumentImporter.Load(workbook);
        var address = diagram.Entities.Single(entity => entity.TableName == "Address");
        var order = diagram.Entities.Single(entity => entity.TableName == "Orders");

        diagram.Relationships.Should().HaveCount(2);

        diagram
            .Relationships.Select(relationship =>
                (
                    relationship.ConstraintName,
                    order
                        .Columns.Single(column =>
                            column.Id == relationship.ColumnPairs.Single().TargetColumnId
                        )
                        .Name
                )
            )
            .Should()
            .BeEquivalentTo([
                ("FK_Orders_ShipToAddress", "ShipToAddressId"),
                ("FK_Orders_BillToAddress", "BillToAddressId"),
            ]);

        // 参照先はどちらも親の主キー
        diagram
            .Relationships.Should()
            .OnlyContain(relationship =>
                relationship.ColumnPairs.Single().SourceColumnId
                == address.Columns.Single(column => column.Name == "AddressId").Id
            );

        // 構成列はどちらも FK 化される
        order
            .Columns.Single(column => column.Name == "ShipToAddressId")
            .IsForeignKey.Should()
            .BeTrue();
        order
            .Columns.Single(column => column.Name == "BillToAddressId")
            .IsForeignKey.Should()
            .BeTrue();
    }

    /// <summary>
    /// テーブルの組・構成列がまったく同じ 2 行は、従来どおり重複として弾かれる（IF4 で緩めすぎていないこと）。
    /// </summary>
    [Fact(DisplayName = "Excel 取込は構成列まで同一のリレーション行を重複として弾く")]
    public void ExcelImport_IdenticalRelationshipRow_Throws()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildTwoForeignKeysBetweenSameTablesDiagram(),
            culture: new CultureInfo("en")
        );

        // 請求先の行の参照元列を配送先と同じにし、構成列まで同一の 2 行を作る
        var sheet = workbook
            .DefinedNames.Single(defined =>
                defined.Name == TableDefinitionDocumentLayout.RelationshipsDefinedName
            )
            .Ranges.First()
            .Worksheet;
        var row = TableDefinitionDocumentLayout.RelationshipDataStartRow;

        while (sheet.Cell(row, 2).GetString() != "FK_Orders_BillToAddress")
        {
            row++;
        }

        sheet.Cell(row, 4).Value = "ShipToAddressId";

        var act = () => TableDefinitionDocumentImporter.Load(workbook);

        act.Should()
            .Throw<System.IO.InvalidDataException>()
            .WithMessage(string.Format(GuiStrings.TableDoc_RelDuplicate, "Address", "Orders"));
    }
}
