using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary><see cref="AiUpdateDiffService" /> の差分抽出とリレーション対応付けを検証するテストクラス</summary>
public class AiUpdateDiffServiceTests
{
    /// <summary>テーブル・カラム・リレーションの差分がカテゴリ別グループへ抽出されることを検証する</summary>
    [Fact(DisplayName = "テーブル・カラム・リレーションの差分をカテゴリ別に抽出できる")]
    public void Compute_CreatesGroupedDiffItems()
    {
        var currentCustomer = new Entity
        {
            TableName = "Customer",
            Description = "顧客",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "Name",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                },
            ],
        };
        var currentOrder = new Entity
        {
            TableName = "Order",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };
        var currentRelationship = new Relationship
        {
            SourceEntityId = currentCustomer.Id,
            TargetEntityId = currentOrder.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = currentCustomer.Columns[0].Id,
            TargetColumnId = currentOrder.Columns[1].Id,
            ConstraintName = "FK_Order_Customer",
            OnDelete = ForeignKeyReferentialAction.NoAction,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };
        var updatedCustomer = new Entity
        {
            TableName = "Customer",
            Description = "顧客マスタ",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "Name",
                    DataType = "nvarchar(200)",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "Email",
                    DataType = "nvarchar(256)",
                    IsNullable = false,
                },
            ],
        };
        var updatedOrder = new Entity
        {
            TableName = "Order",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };
        var updatedSubscription = new Entity
        {
            TableName = "Subscription",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var updatedRelationship = new Relationship
        {
            SourceEntityId = updatedCustomer.Id,
            TargetEntityId = updatedOrder.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = updatedCustomer.Columns[0].Id,
            TargetColumnId = updatedOrder.Columns[1].Id,
            ConstraintName = "FK_Order_Customer",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };

        var diff = new AiUpdateDiffService().Compute(
            new ErDiagram { Entities = [currentCustomer, currentOrder], Relationships = [currentRelationship] },
            new ErDiagram { Entities = [updatedCustomer, updatedOrder, updatedSubscription], Relationships = [updatedRelationship] }
        );

        diff.TotalChanges.Should().BeGreaterThan(0);
        diff.Groups.Should().Contain(group => group.Title == "テーブル");
        diff.Groups.Should().Contain(group => group.Title == "カラム");
        diff.Groups.Should().Contain(group => group.Title == "リレーション");
        diff.Groups.SelectMany(group => group.Items).Should().Contain(item => item.Summary.Contains("[追加] Subscription"));
        diff.Groups.SelectMany(group => group.Items).Should().Contain(item => item.Summary.Contains("[変更] Customer.Name"));
        diff.Groups.SelectMany(group => group.Items).Should().Contain(item => item.Summary.Contains("[変更] Customer → Order"));
    }

    /// <summary>制約名のみ変わったリレーションが削除+追加ではなく変更として対応付けられることを検証する</summary>
    [Fact(DisplayName = "リレーションの制約名だけが変わっても削除と追加ではなく変更として扱う")]
    public void Compute_RelationshipConstraintChange_IsHandledAsModify()
    {
        var currentCustomer = new Entity
        {
            TableName = "Customer",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var currentOrder = new Entity
        {
            TableName = "Order",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };
        var updatedCustomer = new Entity
        {
            TableName = "Customer",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var updatedOrder = new Entity
        {
            TableName = "Order",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };
        var currentRelationship = new Relationship
        {
            SourceEntityId = currentCustomer.Id,
            TargetEntityId = currentOrder.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = currentCustomer.Columns[0].Id,
            TargetColumnId = currentOrder.Columns[1].Id,
            ConstraintName = "FK_Order_Customer_Old",
            OnDelete = ForeignKeyReferentialAction.NoAction,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };
        var updatedRelationship = new Relationship
        {
            SourceEntityId = updatedCustomer.Id,
            TargetEntityId = updatedOrder.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = updatedCustomer.Columns[0].Id,
            TargetColumnId = updatedOrder.Columns[1].Id,
            ConstraintName = "FK_Order_Customer_New",
            OnDelete = ForeignKeyReferentialAction.NoAction,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };

        var diff = new AiUpdateDiffService().Compute(
            new ErDiagram { Entities = [currentCustomer, currentOrder], Relationships = [currentRelationship] },
            new ErDiagram { Entities = [updatedCustomer, updatedOrder], Relationships = [updatedRelationship] }
        );
        var relationshipItems = diff.Groups.Where(group => group.Title == "リレーション").SelectMany(group => group.Items).ToList();

        relationshipItems.Should().ContainSingle();
        relationshipItems[0].ChangeType.Should().Be(AiUpdateDiffChangeType.Modify);
        relationshipItems[0].Summary.Should().Contain("[変更]");
    }
}
