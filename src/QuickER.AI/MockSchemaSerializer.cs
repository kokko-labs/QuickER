using System.Text;
using QuickER.Model;

namespace QuickER.AI;

/// <summary>
/// <see cref="ErDiagram"/> を AI プロンプト用のスキーマ記述テキスト（Markdown 風）へ直列化する。
/// モック生成の初回プロンプトで、テーブル・列・リレーションを人間にも読める形で渡すために使う。
/// </summary>
public static class MockSchemaSerializer
{
    /// <summary>ER 図をスキーマ記述テキストへ直列化する</summary>
    /// <param name="diagram">対象の ER 図（意味モデル）</param>
    /// <returns>テーブル・列・リレーションを列挙した Markdown 風テキスト</returns>
    public static string Serialize(ErDiagram diagram)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# データベーススキーマ");
        builder.AppendLine();
        builder.AppendLine($"対象 DBMS: {diagram.TargetDbms}");
        builder.AppendLine();

        if (diagram.Entities.Count == 0)
        {
            builder.AppendLine("（テーブルは定義されていません）");
            return builder.ToString().TrimEnd();
        }

        AppendEntities(builder, diagram);
        AppendRelationships(builder, diagram);

        return builder.ToString().TrimEnd();
    }

    /// <summary>各エンティティ（テーブル）と列を箇条書きで書き出す</summary>
    private static void AppendEntities(StringBuilder builder, ErDiagram diagram)
    {
        builder.AppendLine("## テーブル");
        builder.AppendLine();

        foreach (var entity in diagram.Entities)
        {
            // 見出しはテーブル名。表示名（Description 由来）が異なる場合のみ併記する
            var displayName = ResolveDisplayName(entity.TableName, entity.Description);
            var heading =
                displayName == entity.TableName
                    ? $"### {entity.TableName}"
                    : $"### {entity.TableName}（{displayName}）";
            builder.AppendLine(heading);

            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                builder.AppendLine($"説明: {entity.Description}");
            }

            builder.AppendLine();

            if (entity.Columns.Count == 0)
            {
                builder.AppendLine("- （列は定義されていません）");
                builder.AppendLine();
                continue;
            }

            foreach (var column in entity.Columns)
            {
                builder.AppendLine(FormatColumn(column));
            }

            builder.AppendLine();
        }
    }

    /// <summary>1 列を箇条書き 1 行へ整形する（名前・表示名・型・制約・説明）</summary>
    private static string FormatColumn(Column column)
    {
        var attributes = new List<string> { column.DataType };

        if (column.IsPrimaryKey)
        {
            attributes.Add("主キー");
        }

        if (column.IsForeignKey)
        {
            attributes.Add("外部キー");
        }

        // NULL 許容/必須を明示する（主キーは常に NOT NULL 前提だが列単位の設定をそのまま反映する）
        attributes.Add(column.IsNullable ? "NULL可" : "必須");

        var displayName = ResolveDisplayName(column.Name, column.Description);
        var namePart = displayName == column.Name ? column.Name : $"{column.Name}（{displayName}）";

        var line = $"- {namePart}: {string.Join(", ", attributes)}";

        if (!string.IsNullOrWhiteSpace(column.Description))
        {
            line += $" — {column.Description}";
        }

        return line;
    }

    /// <summary>リレーション（親→子）を箇条書きで書き出す</summary>
    private static void AppendRelationships(StringBuilder builder, ErDiagram diagram)
    {
        if (diagram.Relationships.Count == 0)
        {
            return;
        }

        builder.AppendLine("## リレーション");
        builder.AppendLine();

        // ID から名前へ引くための索引を組む
        var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);

        foreach (var relationship in diagram.Relationships)
        {
            builder.AppendLine(FormatRelationship(relationship, entitiesById));
        }

        builder.AppendLine();
    }

    /// <summary>1 リレーションを「親 → 子（参照列・多重度）」の 1 行へ整形する</summary>
    private static string FormatRelationship(
        Relationship relationship,
        IReadOnlyDictionary<Guid, Entity> entitiesById
    )
    {
        var source = ResolveEntity(entitiesById, relationship.SourceEntityId);
        var target = ResolveEntity(entitiesById, relationship.TargetEntityId);

        var sourceColumn = ResolveColumnName(source, relationship.SourceColumnId);
        var targetColumn = ResolveColumnName(target, relationship.TargetColumnId);

        var sourceName = source?.TableName ?? "(不明)";
        var targetName = target?.TableName ?? "(不明)";

        var reference =
            sourceColumn is not null && targetColumn is not null
                ? $"（{sourceName}.{sourceColumn} → {targetName}.{targetColumn}）"
                : string.Empty;

        var multiplicity = FormatMultiplicity(relationship.Type);

        return $"- {sourceName} → {targetName}{reference}: {multiplicity}";
    }

    /// <summary>多重度を日本語表記へ変換する</summary>
    private static string FormatMultiplicity(RelationshipType type) =>
        type switch
        {
            RelationshipType.OneToOne => "1 対 1",
            RelationshipType.OneToMany => "1 対 多",
            RelationshipType.ManyToMany => "多 対 多",
            _ => type.ToString(),
        };

    /// <summary>表示名を解決する（説明があればそれ、無ければ識別子そのもの）</summary>
    private static string ResolveDisplayName(string name, string? description) =>
        string.IsNullOrWhiteSpace(description) ? name : description!.Trim();

    /// <summary>ID からエンティティを引く（見つからなければ null）</summary>
    private static Entity? ResolveEntity(IReadOnlyDictionary<Guid, Entity> entitiesById, Guid id) =>
        entitiesById.TryGetValue(id, out var entity) ? entity : null;

    /// <summary>エンティティ内の列名を ID から引く（未設定・不明なら null）</summary>
    private static string? ResolveColumnName(Entity? entity, Guid? columnId)
    {
        if (entity is null || columnId is null)
        {
            return null;
        }

        return entity.Columns.FirstOrDefault(column => column.Id == columnId.Value)?.Name;
    }
}
