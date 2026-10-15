using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// MCP の ER 図操作ツールの応答（図の要約・名前付きクエリの一覧）と、変更系ツールが保存する図ファイルが、
/// 書式のカルチャ（<see cref="CultureScope.Cultures"/>）に依らずインバリアントで作ったものと一致することを確かめる
/// </summary>
/// <remarks>
/// 応答は英語固定で外部のエージェントが読むので、数値・大文字小文字の書式が実行環境のカルチャで変わってはいけない。
/// </remarks>
public sealed class CultureInvariantToolOutputTests : IDisposable
{
    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-culture-tools-" + Guid.NewGuid().ToString("N")
    );

    public CultureInvariantToolOutputTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 一時ディレクトリの後始末の失敗はテストの結果に影響しない
        }
    }

    [Fact(DisplayName = "get_diagram_summary の応答は、書式のカルチャに依らずインバリアントと同じ")]
    public void DiagramSummary_IsCultureInvariant()
    {
        var differences = new List<string>();

        foreach (var (name, diagram) in Diagrams())
        {
            var file = Write(name, diagram);
            differences.AddRange(
                CultureScope
                    .FindDifferences(() => Run(ErDiagramToolNames.GetDiagramSummary, file, new { }))
                    .Select(d => $"{name}: {d}")
            );
        }

        differences.Should().BeEmpty();
    }

    [Fact(DisplayName = "名前付きクエリの一覧の応答は、書式のカルチャに依らずインバリアントと同じ")]
    public void QueryListing_IsCultureInvariant()
    {
        CultureScope
            .FindDifferences(() =>
            {
                var outcome = QueryToolCore.ListQueries(QueryFixtureDefinition.Build());
                outcome.Listing.Should().NotBeNull();

                return QueryToolEnglishFormatter.FormatListing(outcome.Listing!);
            })
            .Should()
            .BeEmpty();
    }

    [Fact(
        DisplayName = "変更系ツールが保存する図ファイルは、書式のカルチャに依らずインバリアントと同じ"
    )]
    public void SavedDiagram_IsCultureInvariant()
    {
        var seed = Write("Seed", GeneratedFixtureDefinition.Build());
        var index = 0;

        CultureScope
            .FindDifferences(() =>
            {
                var file = Path.Combine(_dir, $"run-{index++}.json");
                File.Copy(seed, file);

                var (result, success) = DocumentErDiagramToolHost.Execute(
                    ErDiagramToolNames.AddColumn,
                    file,
                    JsonSerializer.SerializeToElement(
                        new
                        {
                            table_name = "customers",
                            column_name = "Discount",
                            data_type = "decimal(10,-2)",
                            is_nullable = true,
                        }
                    )
                );
                success.Should().BeTrue(result);

                // 列の Id は呼び出しごとに変わるので、Id の値を伏せて比べる
                return MaskIds(File.ReadAllText(file)) + "\n" + MaskIds(result);
            })
            .Should()
            .BeEmpty();
    }

    /// <summary>流す図（SQL Server の型の全カバレッジ・名前付きクエリ入りの図・名前と方言名に i / I を含む図）</summary>
    private static IEnumerable<(string Name, ErDiagram Diagram)> Diagrams()
    {
        yield return ("GeneratedFixture", GeneratedFixtureDefinition.Build());
        yield return ("QueryFixture", QueryFixtureDefinition.Build());
        yield return ("TurkishI", TurkishIDiagram());
    }

    /// <summary>方言名（sqlite）・名前・説明に i / I / İ / ı を含み、一意制約と外部キーを持つ図</summary>
    private static ErDiagram TurkishIDiagram()
    {
        var items = new Entity { TableName = "Items", Description = "İtem ıtem ITEM item" };
        var id = new Column
        {
            Name = "ID",
            DataType = "INTEGER",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "InvoiceCode",
            DataType = "TEXT",
            IsNullable = false,
        };
        items.Columns.AddRange([id, code]);
        items.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [code.Id] });
        var lines = new Entity { TableName = "ItemLines" };
        var lineId = new Column
        {
            Name = "LineID",
            DataType = "INTEGER",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var itemId = new Column
        {
            Name = "ItemID",
            DataType = "INTEGER",
            IsNullable = false,
        };
        lines.Columns.AddRange([lineId, itemId]);

        return new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities = [items, lines],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = items.Id,
                    TargetEntityId = lines.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(id.Id, itemId.Id)],
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            ],
        };
    }

    /// <summary>図を一時ディレクトリへ保存してパスを返す</summary>
    private string Write(string name, ErDiagram diagram)
    {
        var file = Path.Combine(_dir, name + ".json");
        JsonStorageService.SaveAtomic(
            file,
            new DiagramDocument { Version = DiagramDocument.CurrentVersion, Schema = diagram }
        );

        return file;
    }

    /// <summary>ツールを実行して応答を返す（失敗はテストの失敗）</summary>
    private static string Run(string tool, string file, object arguments)
    {
        var (result, success) = DocumentErDiagramToolHost.Execute(
            tool,
            file,
            JsonSerializer.SerializeToElement(arguments)
        );
        success.Should().BeTrue(result);

        return result;
    }

    /// <summary>GUID の形の文字列を伏せる</summary>
    private static string MaskIds(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            "00000000-0000-0000-0000-000000000000",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant
        );
}
