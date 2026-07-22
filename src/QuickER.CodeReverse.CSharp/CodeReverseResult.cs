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

    /// <summary>解析中に生じた非致命の警告（型トークン展開不能・型メタ欠落など。ローカライズ済み）</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
