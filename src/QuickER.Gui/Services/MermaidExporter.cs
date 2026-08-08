using System.IO;
using System.Text;
using QuickER.Model;

namespace QuickER.Services;

/// <summary>ER 図を Mermaid の <c>erDiagram</c> 記法へ変換するサービス</summary>
/// <remarks>
/// キー標識は Mermaid の単一キー方式（1 カラム 1 標識）に合わせ <c>PK &gt; FK &gt; UK</c> の優先度で 1 つに畳む。
/// 一意制約は単一列のものだけを <c>UK</c> として出力し、複合制約は出力しない
/// （<see cref="CollectSingleColumnUniqueMembers"/> 参照）。
/// </remarks>
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

            var uniqueColumnIds = CollectSingleColumnUniqueMembers(entity);

            foreach (var column in entity.Columns)
            {
                builder.AppendLine(
                    $"        {BuildColumnLine(column, uniqueColumnIds.Contains(column.Id))}"
                );
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

    /// <summary>Mermaid 文字列をファイルへ保存し、この形式では表現できず落ちた情報の種類を返す</summary>
    public static IReadOnlyList<ExportOmissionKind> SaveTo(ErDiagram diagram, string path)
    {
        File.WriteAllText(path, Build(diagram), Encoding.UTF8);

        return DetectOmissions(diagram);
    }

    /// <summary>この形式では表現できないために落ちる情報の種類を、図の実際の中身から判定する</summary>
    /// <remarks>
    /// Mermaid の <c>erDiagram</c> は「型・名前・単一キー標識」しか属性行に書けず、説明・NULL 許可・
    /// 複合制約・外部キーの列対応・参照アクションを表現する構文を持たない（ビュー用の表現形式であり、
    /// 往復での完全性は DBML・定義書・保存 JSON が担う）。ここではその欠落のうち、
    /// <b>図に実際に中身が入っているものだけ</b>を返す（説明が空の図で「説明が落ちる」とは言わない）
    /// </remarks>
    public static IReadOnlyList<ExportOmissionKind> DetectOmissions(ErDiagram diagram)
    {
        var checks = new (bool Detected, ExportOmissionKind Kind)[]
        {
            (
                diagram.Entities.Any(entity => !string.IsNullOrWhiteSpace(entity.Description)),
                ExportOmissionKind.TableDescription
            ),
            (
                diagram.Entities.Any(entity => !string.IsNullOrWhiteSpace(entity.Memo)),
                ExportOmissionKind.TableMemo
            ),
            (
                diagram.Entities.Any(entity =>
                    entity.Columns.Any(column => !string.IsNullOrWhiteSpace(column.Description))
                ),
                ExportOmissionKind.ColumnDescription
            ),
            (
                // Mermaid の属性行は NULL 許可を書けず、取込時は全列が NULL 許可になる
                diagram.Entities.Any(entity => entity.Columns.Any(column => !column.IsNullable)),
                ExportOmissionKind.ColumnNullability
            ),
            (
                diagram.Entities.Any(entity =>
                    entity.UniqueConstraints.Any(constraint => constraint.ColumnIds.Count > 1)
                ),
                ExportOmissionKind.CompositeUniqueConstraint
            ),
            (
                diagram.Entities.Any(entity =>
                    entity.UniqueConstraints.Any(constraint =>
                        !string.IsNullOrWhiteSpace(constraint.Name)
                    )
                ),
                ExportOmissionKind.UniqueConstraintName
            ),
            (
                diagram.Relationships.Any(relationship => relationship.ColumnPairs.Count > 0),
                ExportOmissionKind.ForeignKeyColumnPairs
            ),
            (
                diagram.Relationships.Any(relationship =>
                    relationship.OnDelete != ForeignKeyReferentialAction.NoAction
                    || relationship.OnUpdate != ForeignKeyReferentialAction.NoAction
                ),
                ExportOmissionKind.ReferentialAction
            ),
            (diagram.Queries.Count > 0, ExportOmissionKind.NamedQuery),
        };

        return checks.Where(check => check.Detected).Select(check => check.Kind).ToList();
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
    /// Mermaid は同一カラムへの PK と FK の同時指定を構文エラーとして扱うため、キー標識は
    /// <c>PK &gt; FK &gt; UK</c> の優先度で 1 つだけ出力する（GUI のキー標識
    /// <see cref="ColumnKeyMarkPalette"/> と同じ序列）
    /// </remarks>
    private static string BuildColumnLine(Column column, bool isUnique)
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
        else if (isUnique)
        {
            builder.Append(" UK");
        }

        return builder.ToString();
    }

    /// <summary>単一列の一意制約に参加しているカラム ID を集める</summary>
    /// <remarks>
    /// Mermaid の ER 記法には複数列をひとまとめにする構文が無く、複合制約を列ごとの <c>UK</c> へ分解すると
    /// 取込時に「単一列の一意制約 × N」という別の意味になってしまう。そのため
    /// <b>出力するのは単一列制約の構成列だけ</b>とし、複合制約は出力しない（Mermaid はビュー用の
    /// 表現形式であり、往復での完全性は DBML・定義書・保存 JSON が担う）
    /// </remarks>
    private static HashSet<Guid> CollectSingleColumnUniqueMembers(Entity entity)
    {
        var members = new HashSet<Guid>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            if (constraint.ColumnIds.Count != 1)
            {
                continue;
            }

            var columnId = constraint.ColumnIds[0];

            if (entity.Columns.Any(column => column.Id == columnId))
            {
                members.Add(columnId);
            }
        }

        return members;
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
