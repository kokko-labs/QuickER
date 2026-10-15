using System.Globalization;
using System.Text;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// エクスポート（Mermaid・DBML・HTML と Excel の定義書・SVG）と、Mermaid・DBML・Excel の取込の結果が、書式のカルチャ
/// （<see cref="CultureScope.Cultures"/>）に依らずインバリアントで作ったものと一致することを確かめる
/// </summary>
/// <remarks>
/// 定義書の固定文言の言語は引数のカルチャ（英語）で決め、ここで変えるのは数値・日時・大文字小文字の書式だけ。取込の結果は
/// 実行ごとに Id が変わるので、名前と中身だけを並べた文で比べる（<see cref="DescribeDiagram"/>）。
/// </remarks>
public sealed class CultureInvariantExportTests
{
    private static readonly CultureInfo English = new("en");

    /// <summary>流す図（SQL Server の型の全カバレッジと、名前に i / I を含む図）</summary>
    private static IEnumerable<(string Name, ErDiagram Diagram)> Diagrams()
    {
        yield return ("GeneratedFixture", GeneratedFixtureDefinition.Build());
        yield return ("TurkishI", TurkishIDiagram());
    }

    [Fact(DisplayName = "Mermaid・DBML の出力は、書式のカルチャに依らずインバリアントと同じ")]
    public void MermaidAndDbml_AreCultureInvariant()
    {
        var differences = new List<string>();

        foreach (var (name, diagram) in Diagrams())
        {
            differences.AddRange(
                CultureScope
                    .FindDifferences(() => MermaidExporter.Build(diagram))
                    .Select(d => $"Mermaid {name}: {d}")
            );
            differences.AddRange(
                CultureScope
                    .FindDifferences(() => DbmlExporter.Build(diagram))
                    .Select(d => $"DBML {name}: {d}")
            );
        }

        differences.Should().BeEmpty();
    }

    [Fact(DisplayName = "HTML と Excel の定義書は、書式のカルチャに依らずインバリアントと同じ")]
    public void TableDefinitionDocuments_AreCultureInvariant()
    {
        var differences = new List<string>();

        foreach (var (name, diagram) in Diagrams())
        {
            differences.AddRange(
                CultureScope
                    .FindDifferences(() => TableDefinitionHtmlExporter.Build(diagram, English))
                    .Select(d => $"HTML {name}: {d}")
            );
            differences.AddRange(
                CultureScope
                    .FindDifferences(() => DescribeWorkbook(diagram))
                    .Select(d => $"Excel {name}: {d}")
            );
        }

        differences.Should().BeEmpty();
    }

    [Fact(DisplayName = "SVG の出力は、書式のカルチャに依らずインバリアントと同じ")]
    public void Svg_IsCultureInvariant()
    {
        var differences = new List<string>();

        foreach (var (name, diagram) in Diagrams())
        {
            differences.AddRange(
                CultureScope
                    .FindDifferences(() => ImageExportService.BuildSvg(ViewModelOf(diagram)))
                    .Select(d => $"SVG {name}: {d}")
            );
        }

        differences.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "Mermaid・DBML・Excel の取込の結果は、書式のカルチャに依らずインバリアントと同じ"
    )]
    public void Imports_AreCultureInvariant()
    {
        var differences = new List<string>();

        foreach (var (name, diagram) in Diagrams())
        {
            // 出力はインバリアントで作り、取込だけを各カルチャで流す
            string mermaid;
            string dbml;

            using (new CultureScope(CultureInfo.InvariantCulture.Name))
            {
                mermaid = MermaidExporter.Build(diagram);
                dbml = DbmlExporter.Build(diagram);
            }

            differences.AddRange(
                CultureScope
                    .FindDifferences(() =>
                        DescribeDiagram(MermaidImporter.Parse(mermaid, diagram.TargetDbms))
                    )
                    .Select(d => $"Mermaid {name}: {d}")
            );
            differences.AddRange(
                CultureScope
                    .FindDifferences(() =>
                        DescribeDiagram(DbmlImporter.Parse(dbml, diagram.TargetDbms))
                    )
                    .Select(d => $"DBML {name}: {d}")
            );
            differences.AddRange(
                CultureScope
                    .FindDifferences(() =>
                    {
                        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
                            diagram,
                            English
                        );

                        return DescribeDiagram(
                            TableDefinitionDocumentImporter.Load(workbook, diagram.TargetDbms)
                        );
                    })
                    .Select(d => $"Excel {name}: {d}")
            );
        }

        differences.Should().BeEmpty();
    }

    /// <summary>名前・型・説明に i / I / İ / ı を含み、外部キー・一意制約・参照アクションを持つ図</summary>
    private static ErDiagram TurkishIDiagram()
    {
        var items = new Entity { TableName = "Items", Description = "İtem ıtem ITEM item" };
        var id = new Column
        {
            Name = "ID",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "InvoiceCode",
            DataType = "nvarchar(40)",
            IsNullable = false,
        };
        var price = new Column
        {
            Name = "UnitPrice",
            DataType = "decimal(18,4)",
            IsNullable = true,
            Description = "Price in INR",
        };
        items.Columns.AddRange([id, code, price]);
        items.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Items_InvoiceCode", ColumnIds = [code.Id] }
        );
        var lines = new Entity { TableName = "ItemLines" };
        var lineId = new Column
        {
            Name = "LineID",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var itemId = new Column
        {
            Name = "ItemID",
            DataType = "int",
            IsNullable = false,
        };
        lines.Columns.AddRange([lineId, itemId]);

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = [items, lines],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = items.Id,
                    TargetEntityId = lines.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(id.Id, itemId.Id)],
                    ConstraintName = "FK_ItemLines_Items",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            ],
        };
    }

    /// <summary>Excel の定義書の全シートの使われているセルを並べる</summary>
    private static string DescribeWorkbook(ErDiagram diagram)
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(diagram, English);
        var builder = new StringBuilder();

        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var cell in sheet.CellsUsed())
            {
                builder
                    .Append(sheet.Name)
                    .Append('!')
                    .Append(cell.Address.ToString())
                    .Append('=')
                    .Append(cell.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>図を、Id を使わずに名前と中身だけで並べる</summary>
    private static string DescribeDiagram(ErDiagram diagram)
    {
        var builder = new StringBuilder();
        var tableNames = diagram.Entities.ToDictionary(e => e.Id, e => e.TableName);
        var columnNames = diagram
            .Entities.SelectMany(e => e.Columns)
            .ToDictionary(c => c.Id, c => c.Name);

        builder.Append("target=").Append(diagram.TargetDbms).Append('\n');

        foreach (var entity in diagram.Entities)
        {
            builder
                .Append("table ")
                .Append(entity.TableName)
                .Append(" | ")
                .Append(entity.Description)
                .Append('\n');

            foreach (var column in entity.Columns)
            {
                builder
                    .Append("  ")
                    .Append(column.Name)
                    .Append(' ')
                    .Append(column.DataType)
                    .Append(column.IsPrimaryKey ? " pk" : "")
                    .Append(column.IsNullable ? " null" : " notnull")
                    .Append(" | ")
                    .Append(column.Description)
                    .Append('\n');
            }

            foreach (var unique in entity.UniqueConstraints)
            {
                builder
                    .Append("  unique ")
                    .Append(unique.Name)
                    .Append(' ')
                    .Append(string.Join(",", unique.ColumnIds.Select(id => columnNames[id])))
                    .Append('\n');
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            builder
                .Append("rel ")
                .Append(tableNames[relationship.SourceEntityId])
                .Append("->")
                .Append(tableNames[relationship.TargetEntityId])
                .Append(' ')
                .Append(relationship.Type)
                .Append(' ')
                .Append(relationship.ConstraintName)
                .Append(' ')
                .Append(relationship.OnDelete)
                .Append(' ')
                .Append(relationship.OnUpdate)
                .Append(' ')
                .Append(
                    string.Join(
                        ",",
                        relationship.ColumnPairs.Select(pair =>
                            columnNames.GetValueOrDefault(pair.SourceColumnId)
                            + "="
                            + columnNames.GetValueOrDefault(pair.TargetColumnId)
                        )
                    )
                )
                .Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>図のテーブルを、端数のある位置と幅で並べた画面の状態</summary>
    private static MainViewModel ViewModelOf(ErDiagram diagram)
    {
        var vm = new MainViewModel();
        var index = 0;

        foreach (var entity in diagram.Entities)
        {
            vm.Entities.Add(
                new EntityViewModel(
                    entity,
                    new EntityLayout
                    {
                        X = 12.5 + index * 310.25,
                        Y = 40.75 + index * 15.5,
                        Width = 280.125,
                    }
                )
            );
            index++;
        }

        return vm;
    }
}
