using AwesomeAssertions;
using QuickER.Mcp.Tools;
using QuickER.Services;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 成功のまま返すが利用者が気付くべきことを応答に添える 4 つの規則（要約の対象 DBMS・主キー列の NULL 許容の無視・
/// 列ペアの推論が親の主キーに届かなかったこと・明示した親側の列が候補キーでないこと）を、GUI 側 <see cref="ErDiagramDynamicTools"/> と MCP の
/// <see cref="DocumentErDiagramToolHost"/> の両方で検証する
/// </summary>
/// <remarks>
/// GUI 側の文言は表示言語に追従するので、表明は言語に依らない部分（引数名・DBMS 名・列名）で行う
/// </remarks>
public sealed class ErDiagramToolResponseNoteTests : TwoHostToolTestBase
{
    // ---------------- get_diagram_summary の対象 DBMS ----------------

    [Theory(DisplayName = "get_diagram_summary は先頭行で対象 DBMS を示す")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void GetDiagramSummary_FirstLineShowsTargetDbms(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec("get_diagram_summary", new { });

        success.Should().BeTrue();
        result.Split('\n')[0].Should().Contain(Schema.TargetDbms);
    }

    [Fact(DisplayName = "get_diagram_summary（MCP）の先頭行は 'Target DBMS: <名前>'")]
    public void GetDiagramSummary_File_FirstLineIsTargetDbms()
    {
        Setup(ToolHostKind.File);

        Exec("get_diagram_summary", new { })
            .Result.Split('\n')[0]
            .TrimEnd('\r')
            .Should()
            .Be("Target DBMS: sqlserver");
    }

    // ---------------- 主キー列の is_nullable=true ----------------

    [Theory(
        DisplayName = "set_column_property は主キー列への is_nullable=true を無視したと応答で告げる"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void SetColumnProperty_NullableOnPrimaryKey_IsReported(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "set_column_property",
            new
            {
                table_name = "Customer",
                column_name = "CustomerId",
                is_nullable = true,
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("'CustomerId'").And.Contain("is_nullable=true");
        EntityNamed("Customer")
            .Columns.Single(c => c.Name == "CustomerId")
            .IsNullable.Should()
            .BeFalse();
    }

    [Theory(
        DisplayName = "set_column_property は他の変更を反映したうえで、無視した is_nullable=true も告げる"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void SetColumnProperty_NullableOnPrimaryKeyWithOtherChange_AppliesAndReports(
        ToolHostKind host
    )
    {
        Setup(host);

        var (result, success) = Exec(
            "set_column_property",
            new
            {
                table_name = "Customer",
                column_name = "CustomerId",
                description = "Identifier",
                is_nullable = true,
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("is_nullable=true");
        var column = EntityNamed("Customer").Columns.Single(c => c.Name == "CustomerId");
        column.Description.Should().Be("Identifier");
        column.IsNullable.Should().BeFalse();
    }

    [Theory(
        DisplayName = "set_column_property は主キーでない列の is_nullable=true では注意を添えない"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void SetColumnProperty_NullableOnOrdinaryColumn_HasNoNote(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "set_column_property",
            new
            {
                table_name = "Customer",
                column_name = "Name",
                is_nullable = true,
            }
        );

        success.Should().BeTrue();
        result.Should().NotContain("is_nullable=true");
    }

    // ---------------- 列ペアの推論が親の主キーに届かない ----------------

    [Theory(
        DisplayName = "add_relationship は列ペアを親の主キー全列へ推論できたときは注意を添えない"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_FullyInferredPairs_HasNoNote(ToolHostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        result.Should().NotContain("source_columns");
        Schema.Relationships.Single().ColumnPairs.Should().ContainSingle();
    }

    [Theory(
        DisplayName = "add_relationship は識別関係（子の主キー (OrderId, LineNo) の OrderId）を推論し、注意を添えない"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_IdentifyingRelationship_PairsChildPrimaryKey(ToolHostKind host)
    {
        Setup(host);
        Arrange("add_entity", new { table_name = "OrderLine" });
        Arrange(
            "add_column",
            new
            {
                table_name = "OrderLine",
                column_name = "OrderId",
                data_type = "int",
                is_primary_key = true,
            }
        );
        Arrange(
            "add_column",
            new
            {
                table_name = "OrderLine",
                column_name = "LineNo",
                data_type = "int",
                is_primary_key = true,
            }
        );

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "Order",
                target_table = "OrderLine",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        result.Should().NotContain("source_columns");
        var schema = Schema;
        var child = schema.Entities.Single(entity => entity.TableName == "OrderLine");
        var pair = schema.Relationships.Single().ColumnPairs.Should().ContainSingle().Subject;
        var childColumn = child.Columns.Single(column => column.Id == pair.TargetColumnId);
        childColumn.Name.Should().Be("OrderId");
        childColumn.IsPrimaryKey.Should().BeTrue();
        childColumn.IsForeignKey.Should().BeTrue();
    }

    [Theory(DisplayName = "add_relationship は親に主キーが無く列ペアを推論できないと注意を添える")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_ParentWithoutKey_IsReported(ToolHostKind host)
    {
        Setup(host);
        Arrange("add_entity", new { table_name = "Tag" });
        Arrange(
            "add_column",
            new
            {
                table_name = "Tag",
                column_name = "Label",
                data_type = "nvarchar(20)",
            }
        );

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "Tag",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("'Tag'").And.Contain("source_columns");
        Schema.Relationships.Single().ColumnPairs.Should().BeEmpty();
    }

    [Theory(
        DisplayName = "add_relationship は複合主キーの一部しか対応付けられないと本数を添えて注意する"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_PartiallyInferredPairs_IsReported(ToolHostKind host)
    {
        Setup(host);
        Arrange("add_entity", new { table_name = "Region" });
        Arrange(
            "add_column",
            new
            {
                table_name = "Region",
                column_name = "RegionId",
                data_type = "int",
                is_primary_key = true,
            }
        );
        Arrange(
            "add_column",
            new
            {
                table_name = "Region",
                column_name = "Code",
                data_type = "nvarchar(10)",
                is_primary_key = true,
            }
        );
        Arrange(
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "RegionId",
                data_type = "int",
            }
        );

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "Region",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("1").And.Contain("2").And.Contain("source_columns");
        Schema.Relationships.Single().ColumnPairs.Should().ContainSingle();
    }

    [Theory(DisplayName = "add_relationship は列を明示したときと多対多では推論の注意を添えない")]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_ExplicitColumnsOrManyToMany_HasNoNote(ToolHostKind host)
    {
        Setup(host);
        Arrange("add_entity", new { table_name = "Tag" });
        Arrange(
            "add_column",
            new
            {
                table_name = "Tag",
                column_name = "Label",
                data_type = "nvarchar(20)",
            }
        );

        // 明示する親側の列は候補キー（一意制約）にしておく＝候補キーでない列の注意とは切り分ける
        Arrange("set_unique_constraint", new { table_name = "Tag", columns = new[] { "Label" } });

        Exec(
            "add_relationship",
            new
            {
                source_table = "Tag",
                target_table = "Order",
                relationship_type = "ManyToMany",
            }
        )
            .Result.Should()
            .NotContain("source_columns");
        Exec(
            "add_relationship",
            new
            {
                source_table = "Tag",
                target_table = "Order",
                source_columns = new[] { "Label" },
                target_columns = new[] { "ShipToCustomerId" },
                relationship_type = "OneToMany",
            }
        )
            .Result.Should()
            .NotContain("source_columns");
    }

    [Theory(
        DisplayName = "add_relationship は親に主キーがあっても子に対応する列が 1 本も無いと注意を添える"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_NoChildColumnMatchesParentKey_IsReported(ToolHostKind host)
    {
        Setup(host);
        Arrange("add_entity", new { table_name = "Region" });
        Arrange(
            "add_column",
            new
            {
                table_name = "Region",
                column_name = "RegionId",
                data_type = "int",
                is_primary_key = true,
            }
        );

        // Order に RegionId に当たる列は無い＝推論できた列ペアは 0 本
        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "Region",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("0").And.Contain("1").And.Contain("source_columns");
        Schema.Relationships.Single().ColumnPairs.Should().BeEmpty();
    }

    // ---------------- 明示した親側の列が候補キーでない ----------------

    [Theory(
        DisplayName = "add_relationship は明示した親側の列が主キーでも一意制約でもなければ、作成したうえで注意を添える"
    )]
    [InlineData(ToolHostKind.Gui, "Name")]
    [InlineData(ToolHostKind.File, "Name")]
    [InlineData(ToolHostKind.Gui, "CustomerId,Name")]
    [InlineData(ToolHostKind.File, "CustomerId,Name")]
    public void AddRelationship_ExplicitNonCandidateKey_IsReported(
        ToolHostKind host,
        string parentColumns
    )
    {
        Setup(host);
        var sourceColumns = parentColumns.Split(',');
        var targetColumns = new[] { "CustomerId", "ShipToCustomerId" }[..sourceColumns.Length];

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = sourceColumns,
                target_columns = targetColumns,
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue("拒否はせず、作成したうえで注意する");
        result.Should().Contain("'Customer'").And.Contain("Name").And.Contain("source_columns");
        Schema.Relationships.Single().ColumnPairs.Should().HaveCount(sourceColumns.Length);
    }

    [Theory(
        DisplayName = "add_relationship は明示した親側の列が主キー全列か一意制約と一致すれば、並びを問わず注意を添えない"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void AddRelationship_ExplicitCandidateKey_HasNoNote(ToolHostKind host)
    {
        Setup(host);
        Arrange("add_entity", new { table_name = "Region" });

        foreach (var column in new[] { "RegionId", "Code" })
        {
            Arrange(
                "add_column",
                new
                {
                    table_name = "Region",
                    column_name = column,
                    data_type = "int",
                    is_primary_key = true,
                }
            );
        }

        Arrange(
            "add_column",
            new
            {
                table_name = "Region",
                column_name = "Slug",
                data_type = "nvarchar(20)",
            }
        );
        Arrange("set_unique_constraint", new { table_name = "Region", columns = new[] { "Slug" } });

        foreach (var column in new[] { "RegionId", "RegionCode", "RegionSlug" })
        {
            Arrange(
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = column,
                    data_type = "int",
                }
            );
        }

        // 主キー全列（宣言と逆の並び）
        Exec(
            "add_relationship",
            new
            {
                source_table = "Region",
                target_table = "Order",
                source_columns = new[] { "Code", "RegionId" },
                target_columns = new[] { "RegionCode", "RegionId" },
                relationship_type = "OneToMany",
            }
        )
            .Result.Should()
            .NotContain("source_columns");

        // 一意制約の列
        Exec(
            "add_relationship",
            new
            {
                source_table = "Region",
                target_table = "Order",
                source_columns = new[] { "Slug" },
                target_columns = new[] { "RegionSlug" },
                relationship_type = "OneToMany",
            }
        )
            .Result.Should()
            .NotContain("source_columns");
    }
}
