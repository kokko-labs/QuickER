using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Mermaid の型トークン・実体名の書き方と、それに伴う欠落告知（型の書き換え・テーブル名の畳み・外部キー列の UNIQUE・計算列）を検証するテストクラス。
/// </summary>
/// <remarks>
/// Mermaid の文法（erDiagram.jison）は、素の実体名に非 ASCII 文字・英数字・<c>_ - * .</c> だけを許し、
/// それ以外は引用（<c>"…"</c>。中に <c>" % \</c> は書けない）を要る。型は英字か <c>_</c> で始まる語でなければならない。
/// </remarks>
public partial class MermaidFormatFidelityTests
{
    /// <summary>Mermaid の型トークン（ATTRIBUTE_WORD）の形に一致する正規表現</summary>
    [GeneratedRegex(@"^[*A-Za-z_\u00C0-\uFFFF][A-Za-z0-9\-_\[\]\(\)\.,\u00C0-\uFFFF*]*$")]
    private static partial Regex MermaidTypeToken();

    /// <summary>1 テーブル（主キー id と、指定した型の列 value）の図を組み立てる</summary>
    private static ErDiagram BuildSingleTable(string tableName, string dataType) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = tableName,
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column { Name = "value", DataType = dataType },
                    ],
                },
            ],
        };

    /// <summary>テーブル名を指定して、親子 2 テーブルとリレーション 1 本の図を組み立てる</summary>
    private static ErDiagram BuildParentChild(string parentName, string childName)
    {
        var parent = BuildSingleTable(parentName, "int").Entities[0];
        var child = BuildSingleTable(childName, "int").Entities[0];

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
                },
            ],
        };
    }

    [Fact(DisplayName = "Mermaid 往復: 型が空の列は占位トークンで書き、名前と主キー印を保つ")]
    public void RoundTrip_EmptyDataType_KeepsNameAndPrimaryKey()
    {
        var diagram = BuildSingleTable("items", "int");
        diagram.Entities[0].Columns[0].DataType = string.Empty;

        var mermaid = MermaidExporter.Build(diagram);
        var restored = MermaidImporter.Parse(mermaid).Entities[0].Columns[0];

        mermaid.Should().Contain("_ id PK");
        restored.Name.Should().Be("id");
        restored.DataType.Should().BeEmpty();
        restored.IsPrimaryKey.Should().BeTrue();
        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .NotContain(ExportOmissionKind.ColumnTypeNormalized);
    }

    [Theory(
        DisplayName = "Mermaid 出力: 型は Mermaid の型トークンとして書け、元へ戻らない書き換えだけを告知する"
    )]
    [InlineData("int", false)]
    [InlineData("varchar(50)", false)]
    [InlineData("decimal(10,2)", false)]
    [InlineData("integer[]", false)]
    [InlineData("sys.geography", false)]
    [InlineData("nvarchar(max)", true)]
    [InlineData("double precision", true)]
    [InlineData("decimal(10, 2)", true)]
    [InlineData("\"MyType\"", true)]
    [InlineData("enum('a','b')", true)]
    [InlineData("varchar(20)[]", true)]
    public void Export_DataType_IsValidTokenAndReportsLossyRewrite(string dataType, bool lossy)
    {
        var diagram = BuildSingleTable("items", dataType);

        var mermaid = MermaidExporter.Build(diagram);
        var restored = MermaidImporter.Parse(mermaid).Entities[0].Columns[1];

        var token = mermaid
            .Split('\n')
            .Select(line => line.Trim())
            .Single(line => line.EndsWith(" value", System.StringComparison.Ordinal))
            .Split(' ')[0];
        MermaidTypeToken().IsMatch(token).Should().BeTrue(token);
        restored.Name.Should().Be("value");
        MermaidExporter
            .DetectOmissions(diagram)
            .Contains(ExportOmissionKind.ColumnTypeNormalized)
            .Should()
            .Be(lossy);

        if (!lossy)
        {
            restored.DataType.Should().Be(dataType);
        }
    }

    [Theory(DisplayName = "Mermaid 出力: 素で書けるテーブル名は従来どおり素で書く")]
    [InlineData("orders")]
    [InlineData("受注")]
    [InlineData("dbo.Orders")]
    [InlineData("order-items")]
    public void Export_PlainEntityName_IsWrittenBare(string tableName)
    {
        var mermaid = MermaidExporter.Build(BuildSingleTable(tableName, "int"));

        mermaid.Should().Contain($"    {tableName} {{");
    }

    [Theory(
        DisplayName = "Mermaid 往復: 空白や括弧を含むテーブル名は引用して書き、リレーションごと戻る"
    )]
    [InlineData("Order Details", "Order Lines")]
    [InlineData("orders (2024)", "lines")]
    public void RoundTrip_EntityNameNeedingQuotes_WithRelationship(
        string parentName,
        string childName
    )
    {
        var diagram = BuildParentChild(parentName, childName);

        var mermaid = MermaidExporter.Build(diagram);
        var restored = MermaidImporter.Parse(mermaid);

        mermaid.Should().Contain($"\"{parentName}\" {{");
        restored.Entities.Select(entity => entity.TableName).Should().Equal(parentName, childName);
        var relationship = restored.Relationships.Should().ContainSingle().Subject;
        restored
            .Entities.Single(entity => entity.Id == relationship.SourceEntityId)
            .TableName.Should()
            .Be(parentName);
        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .NotContain(ExportOmissionKind.TableNameNormalized);
    }

    [Fact(
        DisplayName = "Mermaid 出力: 引用にも書けない文字は畳んで告知し、衝突したら番号を付けて読み戻せる"
    )]
    public void Export_UnquotableEntityName_IsFoldedUniquelyAndReported()
    {
        var diagram = BuildParentChild("a\"b c", "a_b c");
        diagram.Entities.Add(BuildSingleTable(@"100% \ done", "int").Entities[0]);

        var restored = MermaidImporter.Parse(MermaidExporter.Build(diagram));

        restored
            .Entities.Select(entity => entity.TableName)
            .Should()
            .Equal("a_b c_2", "a_b c", "100_ _ done");
        restored.Relationships.Should().ContainSingle();
        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.TableNameNormalized);
    }

    [Fact(
        DisplayName = "Mermaid の欠落: 外部キー列の単一列 UNIQUE は告知し、主キー列・通常列の UNIQUE は告知しない"
    )]
    public void DetectOmissions_UniqueOnForeignKeyColumn()
    {
        var diagram = BuildSingleTable("items", "int");
        var entity = diagram.Entities[0];
        var key = entity.Columns[0];
        var value = entity.Columns[1];
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [key.Id] });
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [value.Id] });

        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .NotContain(ExportOmissionKind.UniqueConstraintOnForeignKeyColumn);
        MermaidExporter.Build(diagram).Should().Contain("int value UK");

        value.IsForeignKey = true;

        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.UniqueConstraintOnForeignKeyColumn);
        MermaidExporter.Build(diagram).Should().Contain("int value FK");
    }

    [Fact(DisplayName = "Mermaid の欠落: 計算列があるときだけ計算列の指定を告知する")]
    public void DetectOmissions_ComputedColumn_IsReportedOnlyWhenPresent()
    {
        var diagram = BuildSingleTable("items", "int");

        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .NotContain(ExportOmissionKind.ComputedColumn);

        diagram.Entities[0].Columns[1].IsComputed = true;

        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.ComputedColumn);
    }
}
