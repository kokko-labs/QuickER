using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ツール（GUI 側 <see cref="QuickER.Services.ErDiagramDynamicTools"/> と MCP の <see cref="QuickER.Mcp.Tools.DocumentErDiagramToolHost"/>）が
/// 図へ保存する既定の外部キー名と、要約に見せる一意制約の合成名を、図の対象 DBMS の識別子の長さの上限に収めることを検証する
/// </summary>
/// <remarks>
/// 図へ保存する既定名は、図の対象 DBMS の上限で縮める（経路の正本は <see cref="ConstraintNames"/> の XmlDoc）。
/// 縮めずに保存すると、利用者が付けた名前と区別できないまま DDL の入口で長さの超過として断られる。
/// 図の対象 DBMS は SQL Server（128 の UTF-16 コード単位）。
/// </remarks>
public sealed class ErDiagramToolLongDefaultNameTests : TwoHostToolTestBase
{
    private static readonly string Parent = new('p', 128);
    private static readonly string Child = new('c', 128);

    private void ArrangeLongTables()
    {
        Arrange("add_entity", new { table_name = Parent });
        Arrange(
            "add_column",
            new
            {
                table_name = Parent,
                column_name = "id",
                data_type = "int",
                is_primary_key = true,
            }
        );
        Arrange("add_entity", new { table_name = Child });
        Arrange(
            "add_column",
            new
            {
                table_name = Child,
                column_name = "id",
                data_type = "int",
                is_primary_key = true,
            }
        );
        Arrange(
            "add_column",
            new
            {
                table_name = Child,
                column_name = "parent_id",
                data_type = "int",
            }
        );
    }

    [Theory(
        DisplayName = "add_relationship: 名前を省くと、図の対象 DBMS の上限に収めた既定名を保存する"
    )]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_StoresShortenedDefaultName(HostKind host)
    {
        Setup(host);
        ArrangeLongTables();

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = Parent,
                target_table = Child,
                relationship_type = "OneToMany",
                source_columns = new[] { "id" },
                target_columns = new[] { "parent_id" },
            }
        );

        success.Should().BeTrue(result);
        var name = Schema
            .Relationships.Single(r => r.TargetEntityId == EntityNamed(Child).Id)
            .ConstraintName!;
        name.Should()
            .Be(ConstraintNames.ForeignKey(Child, Parent, IdentifierLengthLimits.SqlServer));
        IdentifierLengthLimits.SqlServer.Fits(name).Should().BeTrue(name);
    }

    [Theory(
        DisplayName = "get_diagram_summary: 一意制約の合成名は、図の対象 DBMS の上限に収めた名前（DDL と同じ）を見せる"
    )]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void Summary_ShowsShortenedSynthesizedUniqueName(HostKind host)
    {
        Setup(host);
        ArrangeLongTables();
        Arrange(
            "set_unique_constraint",
            new { table_name = Child, columns = new[] { "parent_id" } }
        );

        var (summary, success) = Exec("get_diagram_summary", new { });

        success.Should().BeTrue(summary);
        summary
            .Should()
            .Contain(
                UniqueConstraint.SynthesizeName(
                    Child,
                    ["parent_id"],
                    IdentifierLengthLimits.SqlServer
                )
            );
        summary
            .Should()
            .NotContain(
                UniqueConstraint.SynthesizeName(
                    Child,
                    ["parent_id"],
                    IdentifierLengthLimit.Unlimited
                )
            );
    }
}
