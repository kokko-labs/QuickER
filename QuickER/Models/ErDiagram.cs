namespace QuickER.Models;

/// <summary>
/// ER 図全体を表すルートモデル
/// ファイル保存・自動保存における JSON シリアライズの単位
/// </summary>
public class ErDiagram
{
    /// <summary>ER 図に含まれる全エンティティ</summary>
    public List<Entity> Entities { get; set; } = new();

    /// <summary>ER 図に含まれる全リレーション</summary>
    public List<Relationship> Relationships { get; set; } = new();
}
