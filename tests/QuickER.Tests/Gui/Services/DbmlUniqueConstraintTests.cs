using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// DBML 入出力（<see cref="DbmlExporter"/> / <see cref="DbmlImporter"/>）の一意制約対応を検証するテストクラス
/// </summary>
/// <remarks>
/// 出力方針は「名前なし単一列＝カラム設定 <c>unique</c>／複合・名前付き＝<c>Indexes</c> ブロック」で、
/// どちらの記法も取込で復元できる（往復で消えない）ことを固定する。
/// </remarks>
public class DbmlUniqueConstraintTests
{
    /// <summary>Customer(CustomerId PK / Email / TenantId / Code) を持つ図を作る</summary>
    private static (ErDiagram Diagram, Entity Customer) BuildDiagram()
    {
        var customer = new Entity
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
                    Name = "Email",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "TenantId",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "Code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
            },
        };

        return (new ErDiagram { Entities = { customer } }, customer);
    }

    /// <summary>エンティティの一意制約を「制約名 → 構成列名（宣言順）」へ展開する</summary>
    private static (string? Name, string[] Columns)[] Describe(Entity entity) =>
        entity
            .UniqueConstraints.Select(constraint =>
                (
                    constraint.Name,
                    constraint
                        .ColumnIds.Select(id =>
                            entity.Columns.Single(column => column.Id == id).Name
                        )
                        .ToArray()
                )
            )
            .ToArray();

    [Fact(DisplayName = "DBML 出力は名前なし単一列制約をカラム設定 unique として書く")]
    public void Export_SingleColumnUnnamedConstraint_WritesColumnSetting()
    {
        var (diagram, customer) = BuildDiagram();
        customer.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [customer.Columns[1].Id] }
        );

        var text = DbmlExporter.Build(diagram);

        text.Should().Contain("Email nvarchar(100) [unique, not null]");
        text.Should().NotContain("Indexes {");
    }

    [Fact(DisplayName = "DBML 出力は複合制約・名前付き制約を Indexes ブロックへ書く")]
    public void Export_CompositeAndNamedConstraints_WriteIndexesBlock()
    {
        var (diagram, customer) = BuildDiagram();
        customer.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [customer.Columns[2].Id, customer.Columns[3].Id] }
        );
        customer.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_Customer_Email",
                ColumnIds = [customer.Columns[1].Id],
            }
        );

        var text = DbmlExporter.Build(diagram);

        text.Should().Contain("Indexes {");
        text.Should().Contain("(TenantId, Code) [unique]");
        text.Should().Contain("(Email) [unique, name: 'UQ_Customer_Email']");
        // 名前付き単一列はカラム設定ではなく索引として出す（設定側は名前を持てないため）
        text.Should().NotContain("Email nvarchar(100) [unique");
    }

    [Fact(DisplayName = "DBML は一意制約（単一列・複合・名前付き）を往復で保持する")]
    public void RoundTrip_PreservesUniqueConstraints()
    {
        var (diagram, customer) = BuildDiagram();
        customer.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [customer.Columns[1].Id] }
        );
        customer.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_Customer_Tenant",
                ColumnIds = [customer.Columns[2].Id, customer.Columns[3].Id],
            }
        );

        var restored = DbmlImporter.Parse(DbmlExporter.Build(diagram));

        var restoredCustomer = restored.Entities.Single();
        Describe(restoredCustomer)
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    ((string?)null, new[] { "Email" }),
                    ("UQ_Customer_Tenant", new[] { "TenantId", "Code" }),
                }
            );
    }

    [Fact(DisplayName = "DBML 取込は Indexes ブロックの unique 索引だけを一意制約として取り込む")]
    public void Import_IndexesBlock_ParsesUniqueIndexesOnly()
    {
        var text = string.Join(
            Environment.NewLine,
            [
                "Table Customer {",
                "  CustomerId int [pk, not null]",
                "  Email nvarchar(100) [not null]",
                "  TenantId int [not null]",
                "  Code nvarchar(20) [not null]",
                string.Empty,
                "  Indexes {",
                "    (TenantId, Code) [unique, name: 'UQ_Customer_Tenant']",
                "    Email [unique]",
                // unique でない索引は一意制約ではないため無視される
                "    (CustomerId) [name: 'IX_Customer_Id']",
                "  }",
                "}",
            ]
        );

        var diagram = DbmlImporter.Parse(text);

        var customer = diagram.Entities.Single();
        Describe(customer)
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    ("UQ_Customer_Tenant", new[] { "TenantId", "Code" }),
                    ((string?)null, new[] { "Email" }),
                }
            );
    }

    [Fact(DisplayName = "DBML 取込は索引が参照する未定義カラムをエラーにする")]
    public void Import_IndexReferencingUnknownColumn_Throws()
    {
        var text = string.Join(
            Environment.NewLine,
            [
                "Table Customer {",
                "  CustomerId int [pk, not null]",
                "  Indexes {",
                "    (NoSuchColumn) [unique]",
                "  }",
                "}",
            ]
        );

        var act = () => DbmlImporter.Parse(text);

        act.Should().Throw<System.IO.InvalidDataException>().WithMessage("*NoSuchColumn*");
    }
}
