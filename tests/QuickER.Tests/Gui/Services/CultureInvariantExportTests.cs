using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// SVG の出力が、書式のカルチャ（<see cref="CultureScope.Cultures"/>）に依らずインバリアントで作ったものと一致することを確かめる
/// </summary>
/// <remarks>
/// Mermaid・DBML・HTML と Excel の定義書の出力と、Mermaid・DBML・Excel の取込の結果のカルチャ不変は、形式の往復のエンジン
/// （<c>FormatRoundTripTests</c> のカルチャの代表の形 <c>fixture-generated</c>・<c>culture-turkish-i</c>）が持つ。
/// SVG は入力が画面の表示と座標で、意味モデルの往復の外なのでここに残す。
/// </remarks>
public sealed class CultureInvariantExportTests
{
    /// <summary>流す図（SQL Server の型の全カバレッジと、名前に i / I を含む図）</summary>
    private static IEnumerable<(string Name, ErDiagram Diagram)> Diagrams()
    {
        yield return ("GeneratedFixture", GeneratedFixtureDefinition.Build());
        yield return ("TurkishI", TurkishIDiagram());
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
