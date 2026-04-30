namespace ERDesigner.Models;

/// <summary>
/// リレーション（関連）の種類を表す列挙体です。
/// </summary>
public enum RelationshipType
{
    /// <summary>1 対 1 の関連。</summary>
    OneToOne,
    /// <summary>1 対 多 の関連。</summary>
    OneToMany,
    /// <summary>多 対 多 の関連。</summary>
    ManyToMany
}
