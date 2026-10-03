using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ツールの入力検証（空・重複する名前、空のデータ型、未知の relationship_type、同じ列ペアの外部キーの重複）を、
/// GUI 側 <see cref="ErDiagramDynamicTools"/> と MCP の <see cref="DocumentErDiagramToolHost"/> の両方で検証する。
/// </summary>
/// <remarks>
/// 拒否した呼び出しが図を一部だけ変えていないことも見る（GUI 側は変更をその場で反映するため、検証が変更より後ろに
/// あると一部だけ反映される）。
/// </remarks>
public sealed class ErDiagramToolInputValidationTests : TwoHostToolTestBase
{
    // ---------------- テーブル名 ----------------

    [Theory(DisplayName = "add_entity は空・空白だけのテーブル名を拒否する")]
    [InlineData(HostKind.Gui, "")]
    [InlineData(HostKind.Gui, "   ")]
    [InlineData(HostKind.File, "")]
    [InlineData(HostKind.File, "   ")]
    public void AddEntity_BlankName_IsRejected(HostKind host, string tableName)
    {
        Setup(host);

        Exec("add_entity", new { table_name = tableName }).Success.Should().BeFalse();

        Schema.Entities.Select(entity => entity.TableName).Should().Equal("Customer", "Order");
    }

    [Theory(DisplayName = "add_entity は既存テーブルと大文字小文字だけ違う名前を拒否する")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddEntity_NameDifferingOnlyInCase_IsRejected(HostKind host)
    {
        Setup(host);

        var (result, success) = Exec("add_entity", new { table_name = "customer" });

        success.Should().BeFalse();
        result.Should().Contain("'Customer'");
        Schema.Entities.Select(entity => entity.TableName).Should().Equal("Customer", "Order");
    }

    [Theory(
        DisplayName = "set_entity_property は他のテーブルと同じ名前への改名を拒否し、同時に指定した変更も反映しない"
    )]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void SetEntityProperty_RenameToOtherTable_IsRejectedWithoutPartialChange(HostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "set_entity_property",
            new
            {
                table_name = "Order",
                new_table_name = "CUSTOMER",
                memo = "must not be applied",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("'Customer'");
        EntityNamed("Order").Memo.Should().BeEmpty();
    }

    [Theory(DisplayName = "set_entity_property は空白だけの new_table_name を拒否する")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void SetEntityProperty_BlankNewName_IsRejected(HostKind host)
    {
        Setup(host);

        Exec("set_entity_property", new { table_name = "Order", new_table_name = "  " })
            .Success.Should()
            .BeFalse();

        Schema.Entities.Select(entity => entity.TableName).Should().Equal("Customer", "Order");
    }

    [Theory(DisplayName = "set_entity_property は自分自身の大文字小文字だけを変える改名を許す")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void SetEntityProperty_CaseOnlyRenameOfItself_IsAllowed(HostKind host)
    {
        Setup(host);

        Exec("set_entity_property", new { table_name = "Order", new_table_name = "ORDER" })
            .Success.Should()
            .BeTrue();

        Schema.Entities.Select(entity => entity.TableName).Should().Equal("Customer", "ORDER");
    }

    // ---------------- 列名・データ型 ----------------

    [Theory(DisplayName = "add_column は同じテーブルの列と大文字小文字だけ違う名前を拒否する")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddColumn_NameDifferingOnlyInCase_IsRejected(HostKind host)
    {
        Setup(host);

        var (result, success) = Exec(
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "customerid",
                data_type = "int",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("'CustomerId'");
        EntityNamed("Order")
            .Columns.Select(column => column.Name)
            .Should()
            .Equal("OrderId", "CustomerId", "ShipToCustomerId");
    }

    [Theory(DisplayName = "add_column は別のテーブルにある同名の列を妨げない")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddColumn_SameNameInAnotherTable_IsAllowed(HostKind host)
    {
        Setup(host);

        Exec(
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "Name",
                data_type = "nvarchar(50)",
            }
        )
            .Success.Should()
            .BeTrue();
    }

    [Theory(
        DisplayName = "add_column は空・空白だけの data_type を拒否し、省略時の既定型は従来どおり"
    )]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddColumn_BlankDataType_IsRejected(HostKind host)
    {
        Setup(host);

        Exec(
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "Note",
                data_type = " ",
            }
        )
            .Success.Should()
            .BeFalse();
        Exec("add_column", new { table_name = "Order", column_name = "Memo" })
            .Success.Should()
            .BeTrue();

        EntityNamed("Order").Columns.Should().NotContain(column => column.Name == "Note");
        EntityNamed("Order")
            .Columns.Single(column => column.Name == "Memo")
            .DataType.Should()
            .Be("nvarchar(100)");
    }

    [Theory(
        DisplayName = "set_column_property は空の data_type を拒否し、同時に指定した説明も反映しない"
    )]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void SetColumnProperty_BlankDataType_IsRejectedBeforeAnyChange(HostKind host)
    {
        Setup(host);

        Exec(
            "set_column_property",
            new
            {
                table_name = "Order",
                column_name = "CustomerId",
                description = "must not be applied",
                data_type = "",
            }
        )
            .Success.Should()
            .BeFalse();

        var column = EntityNamed("Order").Columns.Single(c => c.Name == "CustomerId");
        column.DataType.Should().Be("int");
        column.Description.Should().BeEmpty();
    }

    // ---------------- relationship_type ----------------

    [Theory(
        DisplayName = "add_relationship の relationship_type は大文字小文字を無視して受け、省略時は一対多"
    )]
    [InlineData(HostKind.Gui, "onetoone", RelationshipType.OneToOne)]
    [InlineData(HostKind.Gui, "ONETOMANY", RelationshipType.OneToMany)]
    [InlineData(HostKind.Gui, null, RelationshipType.OneToMany)]
    [InlineData(HostKind.File, "onetoone", RelationshipType.OneToOne)]
    [InlineData(HostKind.File, "ONETOMANY", RelationshipType.OneToMany)]
    [InlineData(HostKind.File, null, RelationshipType.OneToMany)]
    public void AddRelationship_RelationshipType_IsParsedIgnoringCase(
        HostKind host,
        string? relationshipType,
        RelationshipType expected
    )
    {
        Setup(host);

        Exec(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = new[] { "CustomerId" },
                target_columns = new[] { "CustomerId" },
                relationship_type = relationshipType,
            }
        )
            .Success.Should()
            .BeTrue();

        Schema.Relationships.Should().ContainSingle().Which.Type.Should().Be(expected);
    }

    [Theory(DisplayName = "add_relationship は未知の relationship_type を一対多へ落とさず拒否する")]
    [InlineData(HostKind.Gui, "one_to_many")]
    [InlineData(HostKind.Gui, "")]
    [InlineData(HostKind.Gui, "1")]
    [InlineData(HostKind.File, "one_to_many")]
    [InlineData(HostKind.File, "")]
    [InlineData(HostKind.File, "1")]
    public void AddRelationship_UnknownRelationshipType_IsRejected(
        HostKind host,
        string relationshipType
    )
    {
        Setup(host);

        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = new[] { "CustomerId" },
                target_columns = new[] { "CustomerId" },
                relationship_type = relationshipType,
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("OneToOne, OneToMany, ManyToMany");
        Schema.Relationships.Should().BeEmpty();
    }

    // ---------------- 外部キーの重複 ----------------

    [Theory(DisplayName = "add_relationship は同じ親子・同じ列ペアの外部キーの重複を拒否する")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_SameColumnPairsTwice_IsRejected(HostKind host)
    {
        Setup(host);
        Arrange(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = new[] { "CustomerId" },
                target_columns = new[] { "CustomerId" },
                relationship_type = "OneToMany",
            }
        );

        // 種別・制約名が違っても、同じ列ペアなら同じ外部キー
        var (result, success) = Exec(
            "add_relationship",
            new
            {
                source_table = "customer",
                target_table = "order",
                source_columns = new[] { "customerid" },
                target_columns = new[] { "customerid" },
                relationship_type = "OneToOne",
                constraint_name = "FK_Another",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("FK_Order_Customer");
        Schema.Relationships.Should().ContainSingle();
    }

    [Theory(
        DisplayName = "add_relationship は列ペアの並びだけが違う複合外部キーも重複として拒否する"
    )]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_SameColumnPairsInOtherOrder_IsRejected(HostKind host)
    {
        Setup(host);
        Arrange(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = new[] { "CustomerId", "Name" },
                target_columns = new[] { "CustomerId", "ShipToCustomerId" },
                relationship_type = "OneToMany",
            }
        );

        Exec(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = new[] { "Name", "CustomerId" },
                target_columns = new[] { "ShipToCustomerId", "CustomerId" },
                relationship_type = "OneToMany",
            }
        )
            .Success.Should()
            .BeFalse();

        Schema.Relationships.Should().ContainSingle();
    }

    [Theory(DisplayName = "add_relationship は同じ親子でも列ペアが違えば 2 本目を許す")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_DifferentColumnPairs_IsAllowed(HostKind host)
    {
        Setup(host);
        Arrange(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = new[] { "CustomerId" },
                target_columns = new[] { "CustomerId" },
                relationship_type = "OneToMany",
            }
        );

        Exec(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = new[] { "CustomerId" },
                target_columns = new[] { "ShipToCustomerId" },
                relationship_type = "OneToMany",
            }
        )
            .Success.Should()
            .BeTrue();

        Schema.Relationships.Should().HaveCount(2);
    }

    [Theory(DisplayName = "add_relationship は列ペアを持たない多対多の 2 本目を重複として扱わない")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void AddRelationship_ManyToManyTwice_IsAllowed(HostKind host)
    {
        Setup(host);
        var args = new
        {
            source_table = "Customer",
            target_table = "Order",
            source_columns = new[] { "CustomerId" },
            target_columns = new[] { "CustomerId" },
            relationship_type = "ManyToMany",
        };

        Arrange("add_relationship", args);
        Exec("add_relationship", args).Success.Should().BeTrue();

        Schema.Relationships.Should().HaveCount(2);
    }
}
