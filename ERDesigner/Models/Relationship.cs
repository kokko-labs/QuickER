namespace ERDesigner.Models;

/// <summary>
/// 2 つのエンティティを結ぶリレーション（関連）を表すモデル
/// JSON シリアライズの対象
/// </summary>
public class Relationship
{
    /// <summary>リレーションの一意識別子</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>関連の起点となる <see cref="Entity"/> の ID</summary>
    public Guid SourceEntityId { get; set; }

    /// <summary>関連の終点となる <see cref="Entity"/> の ID</summary>
    public Guid TargetEntityId { get; set; }

    /// <summary>関連の種類（1対1 / 1対多 / 多対多）</summary>
    public RelationshipType Type { get; set; } = RelationshipType.OneToMany;

    /// <summary>起点エンティティ側の参照先カラム ID（未設定の場合は <c>null</c>）</summary>
    public Guid? SourceColumnId { get; set; }

    /// <summary>終点エンティティ側の外部キーカラム ID（未設定の場合は <c>null</c>）</summary>
    public Guid? TargetColumnId { get; set; }

    /// <summary>DB から取り込んだ外部キー制約名（手動作成のリレーションでは <c>null</c>）</summary>
    public string? ConstraintName { get; set; }

    /// <summary>親行削除時の参照アクション</summary>
    public ForeignKeyReferentialAction OnDelete { get; set; } =
        ForeignKeyReferentialAction.NoAction;

    /// <summary>親キー更新時の参照アクション</summary>
    public ForeignKeyReferentialAction OnUpdate { get; set; } =
        ForeignKeyReferentialAction.NoAction;
}
