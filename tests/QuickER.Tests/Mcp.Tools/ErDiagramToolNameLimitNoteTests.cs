using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 名前を受けるツール（GUI 側 <see cref="QuickER.Services.ErDiagramDynamicTools"/> と MCP の
/// <see cref="QuickER.Mcp.Tools.DocumentErDiagramToolHost"/>）が、図の対象 DBMS が受け付けない名前（識別子の長さの上限を超える）を
/// 受け付けて保存したとき、その呼び出しの応答に英語の注意文を添えることを、両ホストで同じ表明で検証する
/// </summary>
/// <remarks>
/// 注意文は変更の前後の違反を比べ、<b>その呼び出しで新しく生じた</b>ものだけを載せる（<see cref="NameLimitNotes"/> が 2 つのホストで共有）。
/// 判定は DDL・同期の入口と同じ <see cref="IdentifierLengthLimit.DescribeViolation"/>。図の対象 DBMS は SQL Server
/// （128 の UTF-16 コード単位）。名前そのものは拒否しない（保存したうえで知らせる）。
/// </remarks>
public sealed class ErDiagramToolNameLimitNoteTests : TwoHostToolTestBase
{
    private static readonly string Long = new('a', 129);

    [Theory(DisplayName = "add_entity: 上限を超えるテーブル名は保存したうえで注意文を添える")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddEntity_OverLimit_AppendsNote(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec("add_entity", new { table_name = Long });

        success.Should().BeTrue(result);
        result.Should().Contain("Note:").And.Contain(Long).And.Contain("128 UTF-16 code units");
        Schema.Entities.Should().Contain(entity => entity.TableName == Long);
    }

    [Theory(DisplayName = "add_column: 上限を超えるカラム名に注意文を添える")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddColumn_OverLimit_AppendsNote(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "add_column",
            new
            {
                table_name = "Customer",
                column_name = Long,
                data_type = "int",
            }
        );

        success.Should().BeTrue(result);
        result.Should().Contain("Note:").And.Contain("column of 'Customer'").And.Contain(Long);
    }

    [Theory(DisplayName = "set_entity_property: 上限を超えるテーブル名への変更に注意文を添える")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void RenameTable_OverLimit_AppendsNote(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "set_entity_property",
            new { table_name = "Customer", new_table_name = Long }
        );

        success.Should().BeTrue(result);
        result.Should().Contain("Note:").And.Contain(Long);
    }

    [Theory(DisplayName = "add_relationship: 上限を超える constraint_name に注意文を添える")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_OverLimitConstraintName_AppendsNote(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                relationship_type = "OneToMany",
                source_columns = new[] { "CustomerId" },
                target_columns = new[] { "CustomerId" },
                constraint_name = Long,
            }
        );

        success.Should().BeTrue(result);
        result.Should().Contain("Note:").And.Contain("foreign key").And.Contain(Long);
    }

    [Theory(DisplayName = "set_unique_constraint: 上限を超える name に注意文を添える")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void SetUniqueConstraint_OverLimitName_AppendsNote(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "set_unique_constraint",
            new
            {
                table_name = "Customer",
                columns = new[] { "Name" },
                name = Long,
            }
        );

        success.Should().BeTrue(result);
        result
            .Should()
            .Contain("Note:")
            .And.Contain("unique constraint of 'Customer'")
            .And.Contain(Long);
    }

    [Theory(
        DisplayName = "上限内の名前には注意文を添えず、すでにある違反を次の呼び出しで繰り返さない"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void WithinLimit_NoNote_AndExistingViolationIsNotRepeated(ToolHostKind host)
    {
        Setup(host);

        var (ok, _) = Exec("add_entity", new { table_name = "Product" });
        ok.Should().NotContain("Note:");

        Exec("add_entity", new { table_name = Long }).Result.Should().Contain("Note:");

        var (next, success) = Exec("add_entity", new { table_name = "Category" });

        success.Should().BeTrue(next);
        next.Should().NotContain("Note:", "先の呼び出しで知らせた違反は繰り返さない");
    }
}
