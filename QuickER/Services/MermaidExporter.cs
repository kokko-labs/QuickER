using System.IO;
using System.Text;
using QuickER.Model;

namespace QuickER.Services;

/// <summary>ER 図を Mermaid の <c>erDiagram</c> 記法へ変換するサービス</summary>
public static class MermaidExporter
{
    /// <summary>ER 図定義から Mermaid 文字列を生成する</summary>
    public static string Build(ErDiagram diagram)
    {
        var builder = new StringBuilder();
        builder.AppendLine("erDiagram");

        foreach (var entity in diagram.Entities)
        {
            builder.AppendLine($"    {entity.TableName} {{");

            foreach (var column in entity.Columns)
            {
                builder.AppendLine($"        {BuildColumnLine(column)}");
            }

            builder.AppendLine("    }");
        }

        if (diagram.Entities.Count > 0 && diagram.Relationships.Count > 0)
        {
            builder.AppendLine();
        }

        var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
        foreach (var relationship in diagram.Relationships)
        {
            var line = BuildRelationshipLine(relationship, entitiesById);
            if (line is not null)
            {
                builder.AppendLine($"    {line}");
            }
        }

        return builder.ToString();
    }

    /// <summary>Mermaid 文字列をファイルへ保存する</summary>
    public static void SaveTo(ErDiagram diagram, string path)
    {
        File.WriteAllText(path, Build(diagram), Encoding.UTF8);
    }

    /// <summary>Mermaid の属性型トークン用に DataType を正規化する</summary>
    /// <remarks>
    /// Mermaid の型トークンは英数字とアンダースコアのみ許容するため、カッコ・カンマ・空白を
    /// アンダースコアへ置換する 例: <c>decimal(10,2)</c> → <c>decimal_10_2</c>
    /// </remarks>
    private static string NormalizeDataType(string dataType)
    {
        // 記号・空白の連続をまとめて 1 つのアンダースコアへ置換する
        var result = System.Text.RegularExpressions.Regex.Replace(dataType, @"[\s(),]+", "_");

        return result.TrimEnd('_');
    }

    /// <summary>Mermaid の属性行を構築する</summary>
    /// <remarks>
    /// Mermaid は同一カラムへの PK と FK の同時指定を構文エラーとして扱うため、
    /// 両方該当する場合は PK を優先し FK は出力しない
    /// </remarks>
    private static string BuildColumnLine(Column column)
    {
        var builder = new StringBuilder();
        builder.Append(NormalizeDataType(column.DataType));
        builder.Append(' ');
        builder.Append(column.Name);

        if (column.IsPrimaryKey)
        {
            builder.Append(" PK");
        }
        else if (column.IsForeignKey)
        {
            builder.Append(" FK");
        }

        return builder.ToString();
    }

    /// <summary>Mermaid のリレーション行を構築する。参照先エンティティが解決できない場合は null を返す</summary>
    private static string? BuildRelationshipLine(
        Relationship relationship,
        IReadOnlyDictionary<Guid, Entity> entitiesById
    )
    {
        if (
            !entitiesById.TryGetValue(relationship.SourceEntityId, out var source)
            || !entitiesById.TryGetValue(relationship.TargetEntityId, out var target)
        )
        {
            return null;
        }

        var symbol = relationship.Type switch
        {
            RelationshipType.OneToOne => "||--||",
            RelationshipType.OneToMany => "||--o{",
            RelationshipType.ManyToMany => "}o--o{",
            _ => "||--o{",
        };
        var label = string.IsNullOrWhiteSpace(relationship.ConstraintName)
            ? "relates"
            : relationship.ConstraintName;

        return $"{source.TableName} {symbol} {target.TableName} : {label}";
    }
}
