namespace ERDesigner.Models;

/// <summary>
/// 2 つのエンティティを繋ぐリレーション（関連）を表すモデルです。
/// JSON 保存対象になります。
/// </summary>
public class Relationship
{
    /// <summary>リレーションを一意に識別する ID です。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>関連の起点となる <see cref="Entity"/> の ID です。</summary>
    public Guid SourceEntityId { get; set; }

    /// <summary>関連の終点となる <see cref="Entity"/> の ID です。</summary>
    public Guid TargetEntityId { get; set; }

    /// <summary>関連の種類（1対1 / 1対多 / 多対多）。</summary>
    public RelationshipType Type { get; set; } = RelationshipType.OneToMany;

    /// <summary>起点エンティティ側の参照先カラム ID です。</summary>
    public Guid? SourceColumnId { get; set; }

    /// <summary>終点エンティティ側の外部キーカラム ID です。</summary>
    public Guid? TargetColumnId { get; set; }

    /// <summary>DB から取り込んだ外部キー制約名です。</summary>
    public string? ConstraintName { get; set; }

    /// <summary>親行削除時の参照アクションです。</summary>
    public ForeignKeyReferentialAction OnDelete { get; set; } = ForeignKeyReferentialAction.NoAction;

    /// <summary>親キー更新時の参照アクションです。</summary>
    public ForeignKeyReferentialAction OnUpdate { get; set; } = ForeignKeyReferentialAction.NoAction;
}
