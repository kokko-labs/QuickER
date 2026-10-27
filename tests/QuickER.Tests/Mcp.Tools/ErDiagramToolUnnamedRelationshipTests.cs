using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 名前の無いリレーション（取込・古いファイル・手編集で生じる）を、DDL が付ける名前で要約に見せ、その名前で消せることを
/// 両ホストで確かめる
/// </summary>
/// <remarks>
/// 名前の無い外部キーの DDL 上の名前は <see cref="ConstraintNames.ResolveForeignKeyNames"/> が決める（DDL・同期と同じ正本）。
/// 期待値もこの関数から求める＝要約やツールが別の規則で名前を作ると食い違う。
/// </remarks>
public sealed class ErDiagramToolUnnamedRelationshipTests : TwoHostToolTestBase
{
    /// <summary>既知の失敗（32-4-P1 の現状）。直したら外し、区切りの最後に空にする</summary>
    private static readonly KnownCaseLedger Ledger = new(
        "ErDiagramToolUnnamedRelationshipTests",
        []
    );

    [Theory(
        DisplayName = "同じ向きのテーブル対に名前の無いリレーションが 2 本あっても、DDL が付ける名前で 1 本を選んで消せる"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void RemoveRelationship_SelectsUnnamedByItsDdlName(ToolHostKind host) =>
        Ledger.Run(
            $"RemoveRelationship_SelectsUnnamedByItsDdlName.{host}",
            () => RemoveRelationship_SelectsUnnamedByItsDdlNameCore(host)
        );

    private void RemoveRelationship_SelectsUnnamedByItsDdlNameCore(ToolHostKind host)
    {
        ArrangeTwoUnnamedRelationships(host);
        var names = DdlNames();
        var byCustomerId = Schema.Relationships.Single(relationship =>
            TargetColumnName(relationship) == "CustomerId"
        );
        var name = names[byCustomerId.Id];

        var (result, success) = Exec(
            "remove_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                constraint_name = name,
            }
        );

        success.Should().BeTrue(result);
        Schema
            .Relationships.Select(TargetColumnName)
            .Should()
            .Equal(["ShipToCustomerId"], "指定した名前の 1 本だけが消える");
    }

    [Theory(
        DisplayName = "get_diagram_summary は、名前の無いリレーションにも DDL が付ける名前を示す"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void Summary_ShowsDdlNameOfUnnamedRelationships(ToolHostKind host) =>
        Ledger.Run(
            $"Summary_ShowsDdlNameOfUnnamedRelationships.{host}",
            () => Summary_ShowsDdlNameOfUnnamedRelationshipsCore(host)
        );

    private void Summary_ShowsDdlNameOfUnnamedRelationshipsCore(ToolHostKind host)
    {
        ArrangeTwoUnnamedRelationships(host);
        var names = DdlNames();

        var (result, success) = Exec("get_diagram_summary", new { });

        success.Should().BeTrue();

        foreach (var name in names.Values)
        {
            result
                .Should()
                .Contain(name, "名前を見ずに消すツールへ渡す名前を知る手段は要約しかない");
        }
    }

    /// <summary>Customer → Order に、CustomerId と ShipToCustomerId の 2 本の名前の無いリレーションを用意する</summary>
    private void ArrangeTwoUnnamedRelationships(ToolHostKind host)
    {
        Setup(host);

        foreach (var column in new[] { "CustomerId", "ShipToCustomerId" })
        {
            Arrange(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "CustomerId" },
                    target_columns = new[] { column },
                    relationship_type = "OneToMany",
                }
            );
        }

        ClearConstraintNames();
        Schema
            .Relationships.Should()
            .OnlyContain(relationship => relationship.ConstraintName == null);
    }

    /// <summary>今の図のリレーションの Id → DDL が付ける名前</summary>
    private Dictionary<Guid, string> DdlNames()
    {
        var schema = Schema;

        return ConstraintNames
            .ResolveForeignKeyNames(
                schema.Entities,
                schema.Relationships,
                IdentifierLengthLimits.ForDbms(schema.TargetDbms)
            )
            .ToDictionary(pair => pair.Key.Id, pair => pair.Value);
    }

    /// <summary>リレーションの子側の列名（列ペアは 1 組だけの前提）</summary>
    private string TargetColumnName(Relationship relationship)
    {
        var columnId = relationship.ColumnPairs.Single().TargetColumnId;

        return Schema
            .Entities.SelectMany(entity => entity.Columns)
            .Single(column => column.Id == columnId)
            .Name;
    }
}
