using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Excel 定義書の往復（自分が書き出した定義書を読み戻せること）と、マージ取込で定義書が運べない情報を現在図から引き継ぐことを検証するテストクラス。
/// </summary>
/// <remarks>
/// 定義書は計算列の指定と、一意制約の構成列の順序・名前を書かない。マージ取込はそれらを現在図から引き継ぐ
/// （<see cref="TableDefinitionMergePostProcessor"/>）。引き継ぎが後処理の働きであることは、素の取込結果と比べて確かめる。
/// </remarks>
public class TableDefinitionRoundTripTests
{
    /// <summary>親 customers(id) と子 orders(id, customer_id, a, b) の図を組み立てる（リレーションは呼び出し側が足す）</summary>
    private static ErDiagram BuildDiagram()
    {
        var customers = new Entity
        {
            TableName = "customers",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column { Name = "customer_id", DataType = "int" },
                new Column { Name = "a", DataType = "int" },
                new Column { Name = "b", DataType = "int" },
            ],
        };

        return new ErDiagram { Entities = [customers, orders], TargetDbms = "sqlserver" };
    }

    /// <summary>図を定義書へ書き出して読み戻す</summary>
    private static ErDiagram RoundTrip(ErDiagram diagram)
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(diagram);

        return TableDefinitionDocumentImporter.Load(workbook);
    }

    [Fact(
        DisplayName = "Excel 往復: 列の対応を持たないリレーションは列ペアなしのまま読み戻せる（同じ組に 2 本でも）"
    )]
    public void RoundTrip_PairlessRelationships_AreReadBack()
    {
        var diagram = BuildDiagram();

        foreach (var type in new[] { RelationshipType.OneToMany, RelationshipType.OneToOne })
        {
            diagram.Relationships.Add(
                new Relationship
                {
                    SourceEntityId = diagram.Entities[0].Id,
                    TargetEntityId = diagram.Entities[1].Id,
                    Type = type,
                }
            );
        }

        var restored = RoundTrip(diagram);

        restored
            .Relationships.Select(relationship => relationship.Type)
            .Should()
            .Equal(RelationshipType.OneToMany, RelationshipType.OneToOne);
        restored
            .Relationships.Should()
            .OnlyContain(relationship => relationship.ColumnPairs.Count == 0);
        restored
            .Entities.SelectMany(entity => entity.Columns)
            .Should()
            .OnlyContain(column => !column.IsForeignKey);
    }

    [Fact(DisplayName = "Excel 往復: 列の無いテーブルを読み戻せる")]
    public void RoundTrip_TableWithoutColumns_IsReadBack()
    {
        var diagram = BuildDiagram();
        diagram.Entities.Add(new Entity { TableName = "empty_table" });

        var restored = RoundTrip(diagram);

        restored
            .Entities.Single(entity => entity.TableName == "empty_table")
            .Columns.Should()
            .BeEmpty();
    }

    [Fact(DisplayName = "Excel 取込: 参照元列・参照先列の片方だけが空の行は従来どおり断る")]
    public void Load_RelationshipWithOneColumnCellEmpty_IsRejected()
    {
        var diagram = BuildDiagram();
        diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = diagram.Entities[0].Id,
                TargetEntityId = diagram.Entities[1].Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs =
                [
                    new RelationshipColumnPair(
                        diagram.Entities[0].Columns[0].Id,
                        diagram.Entities[1].Columns[1].Id
                    ),
                ],
            }
        );
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(diagram);
        workbook.DefinedNames.TryGetValue(
            TableDefinitionDocumentLayout.RelationshipsDefinedName,
            out var defined
        );
        defined!
            .Ranges.First()
            .Worksheet.Cell(TableDefinitionDocumentLayout.RelationshipDataStartRow, 4)
            .Value = string.Empty;

        var act = () => TableDefinitionDocumentImporter.Load(workbook);

        act.Should().Throw<InvalidDataException>();
    }

    /// <summary>計算列と、構成列の順序が列の並びと違う名前付き一意制約 (b, a) を持つ現在図を組み立てる</summary>
    private static ErDiagram BuildCurrentWithComputedAndUnique()
    {
        var diagram = BuildDiagram();
        var orders = diagram.Entities[1];
        orders.Columns[2].IsComputed = true;
        orders.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_orders_b_a",
                ColumnIds = [orders.Columns[3].Id, orders.Columns[2].Id],
            }
        );
        orders.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [orders.Columns[1].Id] });

        return diagram;
    }

    [Fact(
        DisplayName = "Excel のマージ取込: 計算列の指定と一意制約の順序・名前を現在図から引き継ぐ"
    )]
    public void MergeImport_InheritsComputedAndUniqueOrderAndName()
    {
        var current = BuildCurrentWithComputedAndUnique();
        var dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "quicker-tabledoc-" + Guid.NewGuid().ToString("N"))
        );

        try
        {
            var path = Path.Combine(dir.FullName, "doc.xlsx");
            TableDefinitionDocumentExporter.SaveTo(current, path);

            // 素の取込では落ちている（後処理が引き継いでいることの対照）
            var plain = TableDefinitionDocumentImporter
                .Load(path)
                .Entities.Single(entity => entity.TableName == "orders");
            plain.Columns.Single(column => column.Name == "a").IsComputed.Should().BeFalse();
            plain.UniqueConstraints[0].Name.Should().BeNull();
            plain
                .UniqueConstraints[0]
                .ColumnIds.Select(id => plain.Columns.Single(c => c.Id == id).Name)
                .Should()
                .Equal("a", "b");

            var host = new StubDiagramTransferHost { Model = current };
            var service = new DiagramImportService(
                host,
                new StubDialogService(),
                new StubFileDialogService()
            );

            service.ImportDiagramFile(DiagramImportFormat.Excel, path);

            var merged = host
                .MergedReplacements.Should()
                .ContainSingle()
                .Subject.Entities.Single(entity => entity.TableName == "orders");
            merged.Columns.Single(column => column.Name == "a").IsComputed.Should().BeTrue();
            merged.Columns.Single(column => column.Name == "b").IsComputed.Should().BeFalse();
            merged
                .UniqueConstraints.Select(constraint =>
                    $"{constraint.Name}:"
                    + string.Join(
                        ",",
                        constraint.ColumnIds.Select(id =>
                            merged.Columns.Single(c => c.Id == id).Name
                        )
                    )
                )
                .Should()
                .Equal("UQ_orders_b_a:b,a", ":customer_id");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact(DisplayName = "Excel のマージ取込: 構成列の集合が現在図と違う一意制約は取込値のまま")]
    public void MergePostProcessor_DifferentColumnSet_KeepsImported()
    {
        var current = BuildCurrentWithComputedAndUnique();
        var currentOrders = current.Entities[1];
        var imported = new Entity
        {
            Id = currentOrders.Id,
            TableName = "orders",
            Columns = currentOrders
                .Columns.Select(column => new Column
                {
                    Id = column.Id,
                    Name = column.Name,
                    DataType = column.DataType,
                })
                .ToList(),
        };
        imported.UniqueConstraints.Add(
            new UniqueConstraint
            {
                ColumnIds = [currentOrders.Columns[1].Id, currentOrders.Columns[2].Id],
            }
        );

        TableDefinitionMergePostProcessor.Apply(current, [imported]);

        var constraint = imported.UniqueConstraints.Should().ContainSingle().Subject;
        constraint.Name.Should().BeNull();
        constraint
            .ColumnIds.Should()
            .Equal(currentOrders.Columns[1].Id, currentOrders.Columns[2].Id);
        imported.Columns[2].IsComputed.Should().BeTrue();
    }
}
