using System;

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
}
