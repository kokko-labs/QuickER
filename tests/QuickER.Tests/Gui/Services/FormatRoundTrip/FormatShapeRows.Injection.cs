using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>名前・説明による注入と、DBML のリテラルのエスケープの行（ExportNameInjectionTests から移した）</summary>
internal static partial class FormatShapeRows
{
    /// <summary>行構造を壊そうとする名前（改行の後に別の宣言に見えるテキストを置く）</summary>
    private static string InjectedName => "a\nTable injected {";

    /// <summary>サニタイズ後の期待形（改行が空白 1 つへ畳まれる）</summary>
    private static string SanitizedName => "a Table injected {";

    private static IEnumerable<ScenarioRow<FormatShape>> InjectionRows() =>
        [
            // ExportNameInjectionTests.Mermaid_NamesWithNewLine_DoNotBreakLines・Dbml_NamesWithNewLine_DoNotBreakLines（同じ図）
            Shape(
                    "injection-names-with-new-line",
                    "Mermaid 出力は改行入りのテーブル名・列名・制約名で行を壊さない／DBML 出力は改行入りのテーブル名・列名で行を壊さない"
                )
                .Build(InjectedDiagram)
                .Expect(
                    e =>
                        e.Output.Absent("\n" + "Table injected {")
                            .Contains(SanitizedName)
                            .That(
                                "注入テキストを含む行が 1 つ以上あり、どれも宣言行のインデント（4 つの空白）で始まる",
                                InjectedLinesStayIndented("    ")
                            ),
                    nameof(DiagramExportFormat.Mermaid)
                )
                .Expect(
                    e => e.Output.Absent("\n" + "Table injected {").Contains(SanitizedName),
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape(
                    "injection-data-type-with-new-line",
                    "Mermaid の型トークンは改行入りの型文字列を 1 トークンへ畳む"
                )
                .Build(() =>
                {
                    var diagram = InjectedDiagram();
                    diagram.Entities[0].Columns[0].DataType = "int\nDROP";
                    return diagram;
                })
                .Expect(
                    e => e.Output.Contains("int_DROP").Absent("\nDROP"),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            Shape(
                    "injection-description-with-new-line",
                    "DBML の Note は改行入りの説明を 1 行へ畳む"
                )
                .Build(() =>
                {
                    var diagram = InjectedDiagram();
                    diagram.Entities[0].Description = "line1\nline2";
                    return diagram;
                })
                .Expect(
                    e => e.Output.Contains("Note: 'line1 line2'"),
                    nameof(DiagramExportFormat.Dbml)
                ),
            .. new[]
            {
                // バックスラッシュの直後にクォートが来る形（エスケープ由来の \ と元からの \ が並ぶ）
                ("backslash-before-quote", @"a\'b"),
                // 末尾がバックスラッシュの形。二重化しないと出力が 'C:\temp\' となり、取込側の正規表現が
                // 閉じクォートを \' のエスケープとして食べてしまって Note 行として認識されず、説明が丸ごと落ちる
                ("trailing-backslash", @"C:\temp\"),
                ("double-backslash", @"a\\b"),
                ("single-quotes", "plain 'quoted' text"),
            }.Select(item =>
                Shape(
                        $"injection-dbml-description-escape-{item.Item1}",
                        $"DBML の説明はバックスラッシュとシングルクォートを往復できる（{item.Item2}）"
                    )
                    .Build(() => InjectionDescriptionDiagram(item.Item2))
                    .Expect(
                        e =>
                            e.Imported.Count("Structure table *", 1)
                                .Single($"TableDescription customers = \"{item.Item2}\"")
                                .Single(
                                    $"ColumnDescription customers.customer_id = \"{item.Item2}\""
                                ),
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
        ];

    /// <summary>テーブル名・列名・制約名のすべてに改行入りの名前を置いた図（ExportNameInjectionTests.BuildInjectedDiagram）</summary>
    private static ErDiagram InjectedDiagram()
    {
        var parent = new Entity
        {
            TableName = InjectedName,
            Columns =
            [
                new Column
                {
                    Name = InjectedName,
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var child = new Entity
        {
            TableName = "orders",
            Columns =
            [
                new Column
                {
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "parent_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = InjectedName,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                },
            ],
        };
    }

    /// <summary>テーブルと列の説明に同じ文字列を置いた最小の図（ExportNameInjectionTests.BuildDescriptionDiagram）</summary>
    private static ErDiagram InjectionDescriptionDiagram(string description) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "customers",
                    Description = description,
                    Columns =
                    [
                        new Column
                        {
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                            Description = description,
                        },
                    ],
                },
            ],
        };

    /// <summary>
    /// 注入されたテキストを含む行が 1 つ以上あり、どれも宣言行のインデントで始まる
    /// （名前は宣言行の内側に留まり、行頭から始まる新しい宣言にならない。ExportNameInjectionTests.AssertEveryLineIsWellFormed）
    /// </summary>
    private static Func<IReadOnlyList<string>, bool> InjectedLinesStayIndented(string indent) =>
        lines =>
        {
            var injected = string.Join("\n", lines)
                .Replace("\r\n", "\n")
                .Split('\n')
                .Where(line => line.Contains("Table injected {", StringComparison.Ordinal))
                .ToList();

            return injected.Count > 0
                && injected.All(line => line.StartsWith(indent, StringComparison.Ordinal));
        };
}
