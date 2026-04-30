using System.Collections.Generic;

namespace ERDesigner.Models;

/// <summary>
/// ER 図全体を表すルートモデルです。JSON のシリアライズ単位になります。
/// </summary>
public class ErDiagram
{
    /// <summary>ER 図に含まれるすべてのエンティティ。</summary>
    public List<Entity> Entities { get; set; } = new();

    /// <summary>ER 図に含まれるすべてのリレーション。</summary>
    public List<Relationship> Relationships { get; set; } = new();
}
