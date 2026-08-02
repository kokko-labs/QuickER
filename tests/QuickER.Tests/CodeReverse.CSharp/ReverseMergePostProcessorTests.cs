using AwesomeAssertions;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// <see cref="ReverseMergePostProcessor"/> の温存挙動（参照アクション・制約名の引継ぎ・多対多の温存・
/// コードで消えた通常リレーションの非追加）を検証する。
/// </summary>
public class ReverseMergePostProcessorTests
{
    private static readonly Guid CustomerId = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CustomerPk = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OrderId = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid OrderFk = new("bbbbbbbb-0000-0000-0000-000000000002");

    private static Entity Customer() =>
        new()
        {
            Id = CustomerId,
            TableName = "customers",
            Columns =
            {
                new Column { Id = CustomerPk, Name = "customer_id" },
            },
        };

    private static Entity Order() =>
        new()
        {
            Id = OrderId,
            TableName = "orders",
            Columns =
            {
                new Column { Id = OrderFk, Name = "customer_id" },
            },
        };

    /// <summary>端点 4 つ組が一致するリレーションへ、現在図の参照アクション・制約名が引き継がれる</summary>
    [Fact(DisplayName = "一致リレーションの OnDelete/OnUpdate/ConstraintName を温存する")]
    public void Apply_CarriesOverActionsAndConstraintName()
    {
        var current = new ErDiagram
        {
            Entities = { Customer(), Order() },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = CustomerId,
                    TargetEntityId = OrderId,
                    SourceColumnId = CustomerPk,
                    TargetColumnId = OrderFk,
                    Type = RelationshipType.OneToMany,
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.SetNull,
                    ConstraintName = "FK_orders_customers",
                },
            },
        };

        // マージ結果（Guid 引継後）: 同一端点・同一 Id だが参照アクション・制約名は既定
        var mergedEntities = new[] { Customer(), Order() };
        var mergedRelationships = new[]
        {
            new Relationship
            {
                SourceEntityId = CustomerId,
                TargetEntityId = OrderId,
                SourceColumnId = CustomerPk,
                TargetColumnId = OrderFk,
                Type = RelationshipType.OneToMany,
            },
        };

        var result = ReverseMergePostProcessor.Apply(current, mergedEntities, mergedRelationships);

        var relationship = result.Should().ContainSingle().Subject;
        relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.SetNull);
        relationship.ConstraintName.Should().Be("FK_orders_customers");
    }

    /// <summary>現在図の多対多は、両端エンティティが生存していれば結果へ温存される</summary>
    [Fact(DisplayName = "両端生存の多対多は温存される")]
    public void Apply_PreservesManyToMany_WhenBothEntitiesSurvive()
    {
        var manyToMany = new Relationship
        {
            SourceEntityId = CustomerId,
            TargetEntityId = OrderId,
            Type = RelationshipType.ManyToMany,
        };
        var current = new ErDiagram
        {
            Entities = { Customer(), Order() },
            Relationships = { manyToMany },
        };

        var result = ReverseMergePostProcessor.Apply(
            current,
            new[] { Customer(), Order() },
            Array.Empty<Relationship>()
        );

        result.Should().ContainSingle().Which.Type.Should().Be(RelationshipType.ManyToMany);
    }

    /// <summary>片端エンティティが消えた多対多は温存されない</summary>
    [Fact(DisplayName = "片端が消えた多対多は温存されない")]
    public void Apply_DropsManyToMany_WhenOneEntityMissing()
    {
        var manyToMany = new Relationship
        {
            SourceEntityId = CustomerId,
            TargetEntityId = OrderId,
            Type = RelationshipType.ManyToMany,
        };
        var current = new ErDiagram
        {
            Entities = { Customer(), Order() },
            Relationships = { manyToMany },
        };

        // マージ結果には customers しか残っていない（orders はコードから消えた）
        var result = ReverseMergePostProcessor.Apply(
            current,
            new[] { Customer() },
            Array.Empty<Relationship>()
        );

        result.Should().BeEmpty();
    }

    /// <summary>コードで消えた通常（多対多以外）のリレーションは結果へ追加されない</summary>
    [Fact(DisplayName = "コードで消えた 1 対多は結果に含まれない")]
    public void Apply_DoesNotReAddRemovedNormalRelationship()
    {
        var current = new ErDiagram
        {
            Entities = { Customer(), Order() },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = CustomerId,
                    TargetEntityId = OrderId,
                    SourceColumnId = CustomerPk,
                    TargetColumnId = OrderFk,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = "FK_orders_customers",
                },
            },
        };

        // コード側（マージ結果）にはリレーションが無い＝この 1 対多は消えた
        var result = ReverseMergePostProcessor.Apply(
            current,
            new[] { Customer(), Order() },
            Array.Empty<Relationship>()
        );

        result.Should().BeEmpty();
    }
}
