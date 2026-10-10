using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Mermaid 出力（<see cref="MermaidExporter"/>）が関係行のラベルへ制約名を書く形と、取込で戻らない名前の告知を検証する
/// </summary>
/// <remarks>
/// Mermaid のラベルは引用なしの 1 語か引用文字列で、空白を含む名前を素で書くと行が構文として壊れる。
/// 取込は引用されたラベルを制約名に採らないので、引用した名前は落ちる＝欠落として告知する
/// </remarks>
public class MermaidRelationshipLabelTests
{
    /// <summary>customers(id) と orders(id, customer_id) に、与えた制約名のリレーションを張った図を作る</summary>
    private static ErDiagram BuildDiagram(params string?[] constraintNames)
    {
        var customerId = new Column
        {
            Name = "id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };
        var orderCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsForeignKey = true,
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
                orderCustomerId,
            },
        };

        return new ErDiagram
        {
            Entities = [customers, orders],
            Relationships = constraintNames
                .Select(name => new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = name,
                    ColumnPairs = { new RelationshipColumnPair(customerId.Id, orderCustomerId.Id) },
                })
                .ToList(),
        };
    }

    /// <summary>出力した Mermaid の関係行（<c>--</c> を含む行）を返す</summary>
    private static string[] RelationshipLines(string mermaid) =>
        mermaid
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => line.Contains("--"))
            .Select(line => line.Trim())
            .ToArray();

    [Fact(DisplayName = "空白を含む制約名は引用して書き、出力を読み戻せる（構文が壊れない）")]
    public void NameWithSpace_IsQuoted_AndReadsBack()
    {
        var diagram = BuildDiagram("FK orders customer");

        var mermaid = MermaidExporter.Build(diagram);

        RelationshipLines(mermaid)
            .Should()
            .Equal("customers ||--o{ orders : \"FK orders customer\"");

        var reimported = MermaidImporter.Parse(mermaid, "sqlserver");
        var relationship = reimported.Relationships.Should().ContainSingle().Subject;
        relationship.Type.Should().Be(RelationshipType.OneToMany);
        reimported
            .Entities.Single(entity => entity.Id == relationship.TargetEntityId)
            .TableName.Should()
            .Be("orders");

        // 取込は引用されたラベルを制約名に採らない＝名前は落ちるので告知する
        relationship.ConstraintName.Should().NotBe("FK orders customer");
        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.RelationshipConstraintName);
    }

    [Theory(
        DisplayName = "素で書けない記号を含む制約名は引用し、引用の中に書けない \" は _ へ畳む"
    )]
    [InlineData("FK(orders)", "\"FK(orders)\"")]
    [InlineData("FK\"orders", "\"FK_orders\"")]
    [InlineData("FK:orders", "\"FK:orders\"")]
    public void NameWithSymbols_IsQuoted(string name, string expectedLabel)
    {
        var diagram = BuildDiagram(name);

        var mermaid = MermaidExporter.Build(diagram);

        RelationshipLines(mermaid).Should().Equal($"customers ||--o{{ orders : {expectedLabel}");
        MermaidImporter.Parse(mermaid, "sqlserver").Relationships.Should().ContainSingle();
        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.RelationshipConstraintName);
    }

    [Theory(DisplayName = "素で書ける一意の制約名は引用せずに書き、往復するので告知しない")]
    [InlineData("FK_orders_customers")]
    [InlineData("fk-orders.customer")]
    [InlineData("外部キー_注文")]
    public void PlainUniqueName_IsWrittenBare_AndRoundTrips(string name)
    {
        var diagram = BuildDiagram(name);

        var mermaid = MermaidExporter.Build(diagram);

        RelationshipLines(mermaid).Should().Equal($"customers ||--o{{ orders : {name}");
        MermaidImporter
            .Parse(mermaid, "sqlserver")
            .Relationships.Should()
            .ContainSingle()
            .Which.ConstraintName.Should()
            .Be(name);
        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .NotContain(ExportOmissionKind.RelationshipConstraintName);
    }

    [Fact(DisplayName = "名前の無いリレーションだけなら制約名の欠落を告知しない")]
    public void UnnamedRelationships_AreNotReported()
    {
        MermaidExporter
            .DetectOmissions(BuildDiagram(null, " "))
            .Should()
            .NotContain(ExportOmissionKind.RelationshipConstraintName);
    }

    [Theory(
        DisplayName = "素で書けても取込で戻らない制約名（重複・relates・主キーと同名）も告知する"
    )]
    [InlineData("FK_same", "FK_same")]
    [InlineData("relates", null)]
    [InlineData("PK_customers", null)]
    public void PlainNameNotReadBack_IsReported(string first, string? second)
    {
        var diagram = second is null ? BuildDiagram(first) : BuildDiagram(first, second);

        MermaidExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.RelationshipConstraintName);
    }

    [Fact(
        DisplayName = "識別関係（子の主キー列を外部キーに含む）は出力して読み戻しても列ペアを保つ"
    )]
    public void IdentifyingRelationship_KeepsColumnPairOnRoundTrip()
    {
        var orderId = new Column
        {
            Name = "OrderId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orders = new Entity { TableName = "Orders", Columns = { orderId } };
        var lineOrderId = new Column
        {
            Name = "OrderId",
            DataType = "int",
            IsPrimaryKey = true,
            IsForeignKey = true,
            IsNullable = false,
        };
        var lines = new Entity
        {
            TableName = "OrderLines",
            Columns =
            {
                lineOrderId,
                new Column
                {
                    Name = "LineNo",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var diagram = new ErDiagram
        {
            Entities = [orders, lines],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = orders.Id,
                    TargetEntityId = lines.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = { new RelationshipColumnPair(orderId.Id, lineOrderId.Id) },
                },
            ],
        };

        var reimported = MermaidImporter.Parse(MermaidExporter.Build(diagram), "sqlserver");

        var child = reimported.Entities.Single(entity => entity.TableName == "OrderLines");
        var pair = reimported
            .Relationships.Should()
            .ContainSingle()
            .Which.ColumnPairs.Should()
            .ContainSingle()
            .Subject;
        child
            .Columns.Single(column => column.Id == pair.TargetColumnId)
            .Name.Should()
            .Be("OrderId");
    }
}
