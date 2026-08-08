using QuickER.Model;

namespace QuickER.CodeReverse.CSharp;

/// <summary>
/// C# コードからのリバース解析結果（意味モデルのエンティティ・リレーションと、非致命の警告）。
/// </summary>
/// <remarks>
/// エンティティ・列・リレーションの Id は解析時に新規採番される（DB 取込の <c>SchemaImportResult</c> と同様）。
/// リレーションの参照 Id（両端エンティティ・両端列）は本結果内のエンティティ・列 Id を指す。
/// </remarks>
public sealed class CodeReverseResult
{
    /// <summary>復元したエンティティ一覧（新規 Id・ソース上の宣言順を保持する）</summary>
    public required IReadOnlyList<Entity> Entities { get; init; }

    /// <summary>復元したリレーション一覧（<c>[NavigationReference]</c> の端点 4 つ組で一意化済み）</summary>
    public required IReadOnlyList<Relationship> Relationships { get; init; }

    /// <summary>
    /// リレーション <see cref="Relationship.Id"/> → コードで明示されていた外部キーメタデータ
    /// （制約名・参照アクション。未指定フィールドは <c>null</c>）
    /// </summary>
    /// <remarks>
    /// 値そのものは <see cref="Relationships"/> の各リレーションへ反映済みで、この索引は「コードが指定していたか」
    /// だけを伝える（既定値と未指定を区別する用途＝GUI マージの温存判断。詳細は
    /// <see cref="ReverseRelationshipMetadata"/>）。指定が 1 つも無いリレーションは登録されない。
    /// </remarks>
    public IReadOnlyDictionary<
        Guid,
        ReverseRelationshipMetadata
    > RelationshipMetadata { get; init; } = new Dictionary<Guid, ReverseRelationshipMetadata>();

    /// <summary>解析中に生じた非致命の警告（型トークン展開不能・型メタ欠落など。ローカライズ済み）</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
