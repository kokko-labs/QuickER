using AwesomeAssertions;
using QuickER.Mcp.Tools;
using QuickER.Services;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 成功のまま返すが利用者が気付くべきことを応答に添える 3 つの規則（要約の対象 DBMS・主キー列の NULL 許容の無視・
/// 列ペアの推論が親の主キーに届かなかったこと）を、GUI 側 <see cref="ErDiagramDynamicTools"/> と MCP の
/// <see cref="DocumentErDiagramToolHost"/> の両方で検証する
/// </summary>
/// <remarks>
/// GUI 側の文言は表示言語に追従するので、表明は言語に依らない部分（引数名・DBMS 名・列名）で行う
/// </remarks>
public sealed class ErDiagramToolResponseNoteTests : TwoHostToolTestBase
{
    // ---------------- get_diagram_summary の対象 DBMS ----------------

    [Theory(DisplayName = "get_diagram_summary は先頭行で対象 DBMS を示す")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void GetDiagramSummary_FirstLineShowsTargetDbms(HostKind host)
    {
        Setup(host);

        var (result, success) = Exec("get_diagram_summary", new { });

        success.Should().BeTrue();
        result.Split('\n')[0].Should().Contain(Schema.TargetDbms);
    }

    [Fact(DisplayName = "get_diagram_summary（MCP）の先頭行は 'Target DBMS: <名前>'")]
    public void GetDiagramSummary_File_FirstLineIsTargetDbms()
    {
        Setup(HostKind.File);

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
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void SetColumnProperty_NullableOnPrimaryKey_IsReported(HostKind host)
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
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void SetColumnProperty_NullableOnPrimaryKeyWithOtherChange_AppliesAndReports(
        HostKind host
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
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void SetColumnProperty_NullableOnOrdinaryColumn_HasNoNote(HostKind host)
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
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_FullyInferredPairs_HasNoNote(HostKind host)
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

    [Theory(DisplayName = "add_relationship は親に主キーが無く列ペアを推論できないと注意を添える")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_ParentWithoutKey_IsReported(HostKind host)
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
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_PartiallyInferredPairs_IsReported(HostKind host)
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
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_ExplicitColumnsOrManyToMany_HasNoNote(HostKind host)
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
}
