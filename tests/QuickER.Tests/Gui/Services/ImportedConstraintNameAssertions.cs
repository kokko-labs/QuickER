using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 取り込んだ図の外部キー名を確かめる、Mermaid・DBML・Excel 定義書の取込テストが共有する補助
/// </summary>
internal static class ImportedConstraintNameAssertions
{
    /// <summary>customers（主キー id）と orders（bill_customer_id / ship_customer_id）の 2 テーブルの図</summary>
    public static ErDiagram BuildCustomersAndOrders()
    {
        var customers = new Entity
        {
            TableName = "customers",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var orders = new Entity
        {
            TableName = "orders",
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
                    Name = "bill_customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                },
                new Column
                {
                    Name = "ship_customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                },
            },
        };

        return new ErDiagram { Entities = { customers, orders } };
    }

    /// <summary>customers.id → orders の指定列の 1 対多リレーションを図へ足す</summary>
    public static void AddLink(ErDiagram diagram, string childColumn, string? name = null)
    {
        var customers = diagram.Entities.Single(entity => entity.TableName == "customers");
        var orders = diagram.Entities.Single(entity => entity.TableName == "orders");

        diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = customers.Id,
                TargetEntityId = orders.Id,
                Type = RelationshipType.OneToMany,
                ConstraintName = name,
                ColumnPairs =
                [
                    new RelationshipColumnPair(
                        customers.Columns[0].Id,
                        orders.Columns.Single(column => column.Name == childColumn).Id
                    ),
                ],
            }
        );
    }

    /// <summary>リレーションを子の列名と制約名の組で返す（図の順）</summary>
    public static (string ChildColumn, string? Name)[] Describe(ErDiagram diagram) =>
        diagram
            .Relationships.Select(relationship =>
                (
                    diagram
                        .Entities.Single(entity => entity.Id == relationship.TargetEntityId)
                        .Columns.Single(column =>
                            column.Id == relationship.ColumnPairs.Single().TargetColumnId
                        )
                        .Name,
                    relationship.ConstraintName
                )
            )
            .ToArray();

    /// <summary>SQL Server の DDL が付ける制約名がすべて異なる（大文字小文字を無視）ことを表明する</summary>
    public static void ShouldHaveDistinctDdlConstraintNames(ErDiagram diagram)
    {
        var ddl = new SqlServerDdlGenerator().Build(diagram);
        var names = Regex
            .Matches(ddl, @"CONSTRAINT \[([^\]]+)\]")
            .Select(match => match.Groups[1].Value)
            .ToList();

        names.Should().NotBeEmpty();
        names
            .Should()
            .OnlyHaveUniqueItems(
                name => name.ToUpperInvariant(),
                "DDL の制約名は重複してはいけない"
            );
    }
}
