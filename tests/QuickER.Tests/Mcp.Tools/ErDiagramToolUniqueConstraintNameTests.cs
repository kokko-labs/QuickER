using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// <c>set_unique_constraint</c> の制約名の衝突検査（<see cref="ConstraintNames.IsTaken"/>）を、GUI 側
/// <see cref="QuickER.Services.ErDiagramDynamicTools"/> と MCP の <see cref="QuickER.Mcp.Tools.DocumentErDiagramToolHost"/>
/// の両方で検証する。
/// </summary>
/// <remarks>
/// 検査は「自分自身の今の名前とは衝突させない」ことが要る。除外が壊れると、同じ呼び出しを繰り返しただけで
/// 自分の名前と衝突したとして断られる（AI が同じ呼び出しを繰り返すのはよくある）。
/// </remarks>
public sealed class ErDiagramToolUniqueConstraintNameTests : TwoHostToolTestBase
{
    [Theory(
        DisplayName = "set_unique_constraint: 同じ列集合・同じ名前（大文字小文字違いも）で呼び直しても成功し、制約は 1 つのまま"
    )]
    [InlineData(ToolHostKind.Gui, "UQ_Customer_Name", "UQ_Customer_Name")]
    [InlineData(ToolHostKind.Gui, "UQ_Customer_Name", "uq_customer_name")]
    [InlineData(ToolHostKind.Gui, "UQ_Customer_Name", "  UQ_Customer_Name  ")]
    [InlineData(ToolHostKind.File, "UQ_Customer_Name", "UQ_Customer_Name")]
    [InlineData(ToolHostKind.File, "UQ_Customer_Name", "uq_customer_name")]
    [InlineData(ToolHostKind.File, "UQ_Customer_Name", "  UQ_Customer_Name  ")]
    public void SetUniqueConstraint_RepeatedWithOwnName_Succeeds(
        ToolHostKind host,
        string firstName,
        string secondName
    )
    {
        Setup(host);
        Arrange(
            "set_unique_constraint",
            new
            {
                table_name = "Customer",
                columns = new[] { "Name" },
                name = firstName,
            }
        );

        var (result, success) = Exec(
            "set_unique_constraint",
            new
            {
                table_name = "Customer",
                columns = new[] { "Name" },
                name = secondName,
            }
        );

        success.Should().BeTrue(result);
        EntityNamed("Customer")
            .UniqueConstraints.Should()
            .ContainSingle()
            .Which.Name.Should()
            .Be(secondName.Trim());
    }

    [Theory(DisplayName = "set_unique_constraint: 別の制約が使っている名前は拒否し、図を変えない")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void SetUniqueConstraint_NameOfAnotherConstraint_IsRejected(ToolHostKind host)
    {
        Setup(host);
        Arrange(
            "set_unique_constraint",
            new
            {
                table_name = "Customer",
                columns = new[] { "Name" },
                name = "UQ_Customer_Name",
            }
        );

        var (result, success) = Exec(
            "set_unique_constraint",
            new
            {
                table_name = "Order",
                columns = new[] { "CustomerId" },
                name = "uq_customer_name",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("uq_customer_name");
        EntityNamed("Order").UniqueConstraints.Should().BeEmpty();
        EntityNamed("Customer").UniqueConstraints.Should().ContainSingle();
    }
}
