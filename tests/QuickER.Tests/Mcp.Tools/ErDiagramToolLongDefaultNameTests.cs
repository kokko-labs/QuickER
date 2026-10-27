using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport;

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
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_StoresShortenedDefaultName(ToolHostKind host)
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
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void Summary_ShowsShortenedSynthesizedUniqueName(ToolHostKind host)
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

    /// <summary>
    /// 名前の無いリレーション（取込・古いファイル・手編集で生じる）を 1 本持つ図を作る。
    /// その外部キーの DDL 上の名前は、上限で縮めた既定名 <c>FK_{子}_{親}</c> になる
    /// </summary>
    private void ArrangeUnnamedRelationship()
    {
        ArrangeLongTables();
        Arrange(
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
        ClearConstraintNames();
    }

    [Theory(
        DisplayName = "add_relationship: 名前の無い外部キーが持つ、上限で縮めた既定名と同じ constraint_name は断る"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_NameEqualToShortenedDefaultOfUnnamed_IsRejected(ToolHostKind host)
    {
        Setup(host);
        ArrangeUnnamedRelationship();
        Arrange(
            "add_column",
            new
            {
                table_name = Child,
                column_name = "parent_id2",
                data_type = "int",
            }
        );
        var shortened = ConstraintNames.ForeignKey(Child, Parent, IdentifierLengthLimits.SqlServer);

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = Parent,
                target_table = Child,
                relationship_type = "OneToMany",
                source_columns = new[] { "id" },
                target_columns = new[] { "parent_id2" },
                constraint_name = shortened,
            }
        );

        success
            .Should()
            .BeFalse("DDL では名前の無い外部キーがこの名前になり、同じ名前の制約が 2 つできる");
        result.Should().Contain(shortened, "衝突した名前を名指しする");
        Schema.Relationships.Should().ContainSingle();
    }

    [Theory(DisplayName = "set_unique_constraint: 主キーの、上限で縮めた既定名と同じ name は断る")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void SetUniqueConstraint_NameEqualToShortenedPrimaryKeyName_IsRejected(ToolHostKind host)
    {
        Setup(host);
        ArrangeLongTables();
        var shortened = ConstraintNames.PrimaryKey(Child, IdentifierLengthLimits.SqlServer);

        var (result, success) = Exec(
            "set_unique_constraint",
            new
            {
                table_name = Child,
                columns = new[] { "parent_id" },
                name = shortened,
            }
        );

        success.Should().BeFalse("DDL では主キーがこの名前になり、同じ名前の制約が 2 つできる");
        result.Should().Contain(shortened, "衝突した名前を名指しする");
        EntityNamed(Child).UniqueConstraints.Should().BeEmpty();
    }

    [Theory(
        DisplayName = "名前の無い外部キーは、上限で縮めた既定名（DDL と同じ）で要約に出て、その名前で消せる"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void UnnamedRelationship_IsShownAndRemovedByShortenedDefaultName(ToolHostKind host)
    {
        Setup(host);
        ArrangeUnnamedRelationship();
        var shortened = ConstraintNames.ForeignKey(Child, Parent, IdentifierLengthLimits.SqlServer);
        var unshortened = ConstraintNames.ForeignKey(
            Child,
            Parent,
            IdentifierLengthLimit.Unlimited
        );

        var (summary, summarized) = Exec("get_diagram_summary", new { });

        summarized.Should().BeTrue(summary);
        summary.Should().Contain(shortened);
        summary.Should().NotContain(unshortened);

        var (result, success) = Exec(
            "remove_relationship",
            new
            {
                source_table = Parent,
                target_table = Child,
                constraint_name = shortened,
            }
        );

        success.Should().BeTrue(result);
        Schema.Relationships.Should().BeEmpty();
    }

    [Theory(
        DisplayName = "add_relationship: 名前の無い外部キーと同じ列ペアの重複は、上限で縮めた既定名でその外部キーを名指しして断る"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_DuplicateOfUnnamed_NamesItByShortenedDefaultName(ToolHostKind host)
    {
        Setup(host);
        ArrangeUnnamedRelationship();

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

        success.Should().BeFalse(result);
        result
            .Should()
            .Contain(ConstraintNames.ForeignKey(Child, Parent, IdentifierLengthLimits.SqlServer))
            .And.NotContain(
                ConstraintNames.ForeignKey(Child, Parent, IdentifierLengthLimit.Unlimited)
            );
    }
}
