using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Mermaid が複合主キー自身の列順序を運べないことを、出力時の欠落告知で知らせることを検証するテストクラス
/// </summary>
/// <remarks>
/// Mermaid の属性行は主キー列に <c>PK</c> を付けるだけで順序の構文を持たず、取り込み直すと列の並びが主キーの順序になる。
/// 告知は順序が列の並びと食い違うときだけ立てる（判定の正本は <see cref="Entity.GetReorderedPrimaryKeyColumnNames"/>）。
/// </remarks>
public class MermaidPrimaryKeyOrderTests
{
    /// <summary>主キー 2 列（tenant_id → order_no の宣言順）のテーブルを作る</summary>
    private static ErDiagram Diagram(string[]? order)
    {
        var entity = new Entity
        {
            TableName = "order_lines",
            Columns =
            {
                new Column
                {
                    Name = "tenant_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = true,
                },
                new Column
                {
                    Name = "order_no",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = true,
                },
            },
        };

        if (order is not null)
        {
            entity.PrimaryKeyColumnIds = order
                .Select(name => entity.Columns.Single(column => column.Name == name).Id)
                .ToList();
        }

        return new ErDiagram { Entities = { entity } };
    }

    [Fact(DisplayName = "Mermaid: 列の並びと食い違う主キーの順序は欠落として告知する")]
    public void DetectOmissions_ReorderedKey_IsReported()
    {
        MermaidExporter
            .DetectOmissions(Diagram(["order_no", "tenant_id"]))
            .Should()
            .Equal(ExportOmissionKind.PrimaryKeyOrder);
    }

    [Fact(DisplayName = "Mermaid: 順序を明示していない主キーは告知しない")]
    public void DetectOmissions_UnstatedKeyOrder_IsNotReported()
    {
        MermaidExporter.DetectOmissions(Diagram(order: null)).Should().BeEmpty();
    }

    [Fact(DisplayName = "Mermaid: 列の並びどおりに明示した主キーは告知しない")]
    public void DetectOmissions_KeyPinnedInColumnOrder_IsNotReported()
    {
        MermaidExporter.DetectOmissions(Diagram(["tenant_id", "order_no"])).Should().BeEmpty();
    }
}
