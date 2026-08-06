using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Mermaid 入出力（<see cref="MermaidExporter"/> / <see cref="MermaidImporter"/>）の一意制約対応を検証する
/// </summary>
/// <remarks>
/// Mermaid のキー欄は 1 カラム 1 標識のため <c>PK &gt; FK &gt; UK</c> の優先度で畳み、
/// 複数列をまとめる構文が無いため複合制約は出力しない（分解すると別の意味になる）ことを固定する。
/// </remarks>
public class MermaidUniqueConstraintTests
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

    [Fact(DisplayName = "Mermaid 出力は単一列の一意制約の構成列へ UK を付ける")]
    public void Export_SingleColumnConstraint_WritesUkMarker()
    {
        var (diagram, customer) = BuildDiagram();
        customer.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [customer.Columns[1].Id] }
        );

        var text = MermaidExporter.Build(diagram);

        text.Should().Contain("nvarchar_100 Email UK");
    }

    [Fact(DisplayName = "Mermaid 出力は複合制約を出さず、PK / FK を UK より優先する")]
    public void Export_CompositeConstraintAndKeyPriority()
    {
        var (diagram, customer) = BuildDiagram();
        // 複合制約（出力対象外）
        customer.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [customer.Columns[2].Id, customer.Columns[3].Id] }
        );
        // PK 列・FK 列も一意制約の構成列にする（標識は PK / FK が優先される）
        customer.Columns[1].IsForeignKey = true;
        customer.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [customer.Columns[0].Id] }
        );
        customer.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [customer.Columns[1].Id] }
        );

        var text = MermaidExporter.Build(diagram);

        text.Should().Contain("int CustomerId PK");
        text.Should().Contain("nvarchar_100 Email FK");
        text.Should().NotContain("UK");
    }

    [Fact(DisplayName = "Mermaid 取込は UK 標識をその 1 列の一意制約として復元する")]
    public void Import_UkMarker_RestoresSingleColumnConstraint()
    {
        var text = string.Join(
            Environment.NewLine,
            [
                "erDiagram",
                "    Customer {",
                "        int CustomerId PK",
                "        nvarchar_100 Email UK",
                "        int TenantId",
                "    }",
            ]
        );

        var diagram = MermaidImporter.Parse(text);

        var customer = diagram.Entities.Single();
        var constraint = customer.UniqueConstraints.Should().ContainSingle().Subject;
        constraint.Name.Should().BeNull();
        constraint
            .ColumnIds.Should()
            .Equal(customer.Columns.Single(column => column.Name == "Email").Id);
    }

    [Fact(DisplayName = "Mermaid は単一列の一意制約を往復で保持する")]
    public void RoundTrip_PreservesSingleColumnConstraint()
    {
        var (diagram, customer) = BuildDiagram();
        customer.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [customer.Columns[1].Id] }
        );

        var restored = MermaidImporter.Parse(MermaidExporter.Build(diagram));

        var restoredCustomer = restored.Entities.Single();
        restoredCustomer
            .UniqueConstraints.Should()
            .ContainSingle()
            .Which.ColumnIds.Should()
            .Equal(restoredCustomer.Columns.Single(column => column.Name == "Email").Id);
    }
}
