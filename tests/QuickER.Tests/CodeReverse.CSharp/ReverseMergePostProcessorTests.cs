using AwesomeAssertions;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// <see cref="ReverseMergePostProcessor"/> の温存挙動（未指定の参照アクション・制約名だけを補完する
/// fallback 専用の引継ぎ・多対多の温存・コードで消えた通常リレーションの非追加）を検証する。
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

    /// <summary>現在図（温存の供給元）を組み立てる</summary>
    private static ErDiagram CurrentDiagram() =>
        new()
        {
            Entities = { Customer(), Order() },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = CustomerId,
                    TargetEntityId = OrderId,
                    ColumnPairs = [new(CustomerPk, OrderFk)],
                    Type = RelationshipType.OneToMany,
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.SetNull,
                    ConstraintName = "FK_orders_customers",
                },
            },
        };

    /// <summary>コード由来（Guid 引継後）のリレーションを組み立てる</summary>
    private static Relationship CodeRelationship(
        string? constraintName = null,
        ForeignKeyReferentialAction onDelete = ForeignKeyReferentialAction.NoAction,
        ForeignKeyReferentialAction onUpdate = ForeignKeyReferentialAction.NoAction
    ) =>
        new()
        {
            SourceEntityId = CustomerId,
            TargetEntityId = OrderId,
            ColumnPairs = [new(CustomerPk, OrderFk)],
            Type = RelationshipType.OneToMany,
            ConstraintName = constraintName,
            OnDelete = onDelete,
            OnUpdate = onUpdate,
        };

    /// <summary>
    /// メタデータ索引が無い（＝コードが外部キーメタデータを一切指定していない・旧形式コード）場合は、
    /// 端点一致する現在図の参照アクション・制約名を全項目温存する
    /// </summary>
    [Fact(DisplayName = "コード未指定なら OnDelete/OnUpdate/ConstraintName を温存する")]
    public void Apply_CarriesOverActionsAndConstraintName_WhenCodeSpecifiesNothing()
    {
        var current = CurrentDiagram();
        var mergedEntities = new[] { Customer(), Order() };
        var mergedRelationships = new[] { CodeRelationship() };

        var result = ReverseMergePostProcessor.Apply(current, mergedEntities, mergedRelationships);

        var relationship = result.Should().ContainSingle().Subject;
        relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.SetNull);
        relationship.ConstraintName.Should().Be("FK_orders_customers");
    }

    /// <summary>
    /// コードが指定していたフィールドはコードが勝ち、指定が無いフィールドだけ現在図から補完する
    /// （fallback 専用＝コード上で NO ACTION へ戻した変更が現在図の値で握り潰されない）
    /// </summary>
    [Fact(DisplayName = "コード指定フィールドはコードが勝ち、未指定フィールドのみ温存する")]
    public void Apply_PrefersCodeSpecifiedFields_AndFillsOnlyUnspecified()
    {
        var current = CurrentDiagram();
        var mergedEntities = new[] { Customer(), Order() };

        // コードは OnDelete のみ明示（NoAction＝図の Cascade を意図的に外した状態）。
        // 制約名・OnUpdate は未指定なので現在図から補完される。
        var codeRelationship = CodeRelationship();
        var mergedRelationships = new[] { codeRelationship };
        var metadata = new Dictionary<Guid, ReverseRelationshipMetadata>
        {
            [codeRelationship.Id] = new(
                ConstraintName: null,
                OnDelete: ForeignKeyReferentialAction.NoAction,
                OnUpdate: null
            ),
        };

        var result = ReverseMergePostProcessor.Apply(
            current,
            mergedEntities,
            mergedRelationships,
            metadata
        );

        var relationship = result.Should().ContainSingle().Subject;
        relationship
            .OnDelete.Should()
            .Be(ForeignKeyReferentialAction.NoAction, "コードの指定が勝つ");
        relationship
            .OnUpdate.Should()
            .Be(ForeignKeyReferentialAction.SetNull, "未指定は温存される");
        relationship.ConstraintName.Should().Be("FK_orders_customers", "未指定は温存される");
    }

    /// <summary>コードが全フィールドを指定していれば、現在図の値は一切引き継がれない</summary>
    [Fact(DisplayName = "コードが全フィールド指定なら現在図の値で上書きしない")]
    public void Apply_KeepsCodeValues_WhenAllFieldsSpecified()
    {
        var current = CurrentDiagram();
        var mergedEntities = new[] { Customer(), Order() };

        var codeRelationship = CodeRelationship(
            constraintName: "FK_orders_customers_v2",
            onDelete: ForeignKeyReferentialAction.SetDefault,
            onUpdate: ForeignKeyReferentialAction.NoAction
        );
        var metadata = new Dictionary<Guid, ReverseRelationshipMetadata>
        {
            [codeRelationship.Id] = new(
                ConstraintName: "FK_orders_customers_v2",
                OnDelete: ForeignKeyReferentialAction.SetDefault,
                OnUpdate: ForeignKeyReferentialAction.NoAction
            ),
        };

        var result = ReverseMergePostProcessor.Apply(
            current,
            mergedEntities,
            [codeRelationship],
            metadata
        );

        var relationship = result.Should().ContainSingle().Subject;
        relationship.ConstraintName.Should().Be("FK_orders_customers_v2");
        relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.SetDefault);
        relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);
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
                    ColumnPairs = [new(CustomerPk, OrderFk)],
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
