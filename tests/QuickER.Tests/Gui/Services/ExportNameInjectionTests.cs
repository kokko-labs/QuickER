using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 行指向のテキスト形式（Mermaid / DBML）への出力が、図の名前・説明で行構造を壊されないことを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// Mermaid の <c>erDiagram</c> も DBML も「1 行 1 要素」の記法で、テーブル名・列名・制約名に改行が混じると
/// 行が途中で終わり、残りが別の宣言として解釈される（図の名前で出力の構造を壊せてしまう）。
/// </para>
/// <para>
/// あわせて DBML の note リテラルのエスケープが往復することも固定する。バックスラッシュを二重化しないと、
/// 元から <c>\</c> を含む説明とエスケープ由来の <c>\</c> が区別できなくなり、取込側で説明が壊れる。
/// </para>
/// </remarks>
public class ExportNameInjectionTests
{
    /// <summary>行構造を壊そうとする名前（改行の後に別の宣言に見えるテキストを置く）</summary>
    private const string Injected = "a\nTable injected {";

    /// <summary>サニタイズ後の期待形（改行が空白 1 つへ畳まれる）</summary>
    private const string Sanitized = "a Table injected {";

    // ---------------- Mermaid ----------------

    [Fact(DisplayName = "Mermaid 出力は改行入りのテーブル名・列名・制約名で行を壊さない")]
    public void Mermaid_NamesWithNewLine_DoNotBreakLines()
    {
        var mermaid = MermaidExporter.Build(BuildInjectedDiagram());

        mermaid.Should().NotContain("\n" + "Table injected {");
        mermaid.Should().Contain(Sanitized);
        AssertEveryLineIsWellFormed(mermaid, "    ");
    }

    [Fact(DisplayName = "Mermaid の型トークンは改行入りの型文字列を 1 トークンへ畳む")]
    public void Mermaid_DataTypeWithNewLine_IsFoldedIntoOneToken()
    {
        var diagram = BuildInjectedDiagram();
        diagram.Entities[0].Columns[0].DataType = "int\nDROP";

        var mermaid = MermaidExporter.Build(diagram);

        mermaid.Should().Contain("int_DROP");
        mermaid.Should().NotContain("\nDROP");
    }

    // ---------------- DBML ----------------

    [Fact(DisplayName = "DBML 出力は改行入りのテーブル名・列名で行を壊さない")]
    public void Dbml_NamesWithNewLine_DoNotBreakLines()
    {
        var dbml = DbmlExporter.Build(BuildInjectedDiagram());

        dbml.Should().NotContain("\n" + "Table injected {");
        dbml.Should().Contain(Sanitized);
    }

    [Fact(DisplayName = "DBML の Note は改行入りの説明を 1 行へ畳む")]
    public void Dbml_DescriptionWithNewLine_StaysOnOneLine()
    {
        var diagram = BuildInjectedDiagram();
        diagram.Entities[0].Description = "line1\nline2";

        var dbml = DbmlExporter.Build(diagram);

        dbml.Should().Contain("Note: 'line1 line2'");
    }

    [Theory(DisplayName = "DBML の説明はバックスラッシュとシングルクォートを往復できる")]
    // バックスラッシュの直後にクォートが来る形（エスケープ由来の \ と元からの \ が並ぶ）
    [InlineData(@"a\'b")]
    // 末尾がバックスラッシュの形。二重化しないと出力が 'C:\temp\' となり、取込側の正規表現が
    // 閉じクォートを \' のエスケープとして食べてしまって Note 行として認識されず、説明が丸ごと落ちる
    [InlineData(@"C:\temp\")]
    [InlineData(@"a\\b")]
    [InlineData("plain 'quoted' text")]
    public void Dbml_BackslashAndQuoteInDescription_RoundTrips(string tricky)
    {
        var diagram = BuildDescriptionDiagram(tricky);

        var restored = DbmlImporter.Parse(DbmlExporter.Build(diagram));

        restored.Entities.Should().ContainSingle();
        restored.Entities[0].Description.Should().Be(tricky);
        restored.Entities[0].Columns[0].Description.Should().Be(tricky);
    }

    /// <summary>テーブルと列の説明に同じ文字列を置いた最小の図を組み立てる</summary>
    private static ErDiagram BuildDescriptionDiagram(string description)
    {
        return new ErDiagram
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
    }

    [Fact(DisplayName = "DBML の一意制約名・外部キー制約名もバックスラッシュを往復できる")]
    public void Dbml_BackslashInConstraintNames_RoundTrips()
    {
        const string Tricky = @"UQ_weird\";
        const string ForeignKeyName = @"FK_weird\";

        var customer = new Entity
        {
            TableName = "customers",
            Columns =
            [
                new Column
                {
                    Name = "customer_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
            ],
        };
        var order = new Entity
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
                    Name = "customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };
        customer.UniqueConstraints.Add(
            new UniqueConstraint { Name = Tricky, ColumnIds = [customer.Columns[1].Id] }
        );

        var diagram = new ErDiagram
        {
            Entities = [customer, order],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = ForeignKeyName,
                    ColumnPairs = [new(customer.Columns[0].Id, order.Columns[1].Id)],
                },
            ],
        };

        var restored = DbmlImporter.Parse(DbmlExporter.Build(diagram));

        restored
            .Entities.Single(entity => entity.TableName == "customers")
            .UniqueConstraints.Single()
            .Name.Should()
            .Be(Tricky);
        restored.Relationships.Single().ConstraintName.Should().Be(ForeignKeyName);
    }

    /// <summary>テーブル名・列名・制約名のすべてに改行入りの名前を置いた図を組み立てる</summary>
    private static ErDiagram BuildInjectedDiagram()
    {
        var parent = new Entity
        {
            TableName = Injected,
            Columns =
            [
                new Column
                {
                    Name = Injected,
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
                    ConstraintName = Injected,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                },
            ],
        };
    }

    /// <summary>注入されたテキストが独立した行として現れていないことを検証する</summary>
    /// <param name="indent">この形式で宣言行が持つはずのインデント（行頭から始まる注入行と区別する）</param>
    private static void AssertEveryLineIsWellFormed(string output, string indent)
    {
        var injectedLines = output
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => line.Contains("Table injected {", StringComparison.Ordinal))
            .ToList();

        injectedLines.Should().NotBeEmpty("注入テキストは出力のどこかに現れること");
        injectedLines
            .Should()
            .OnlyContain(
                line => line.StartsWith(indent, StringComparison.Ordinal),
                "名前は必ず宣言行の内側に留まり、行頭から始まる新しい宣言にはならないこと"
            );
    }
}
