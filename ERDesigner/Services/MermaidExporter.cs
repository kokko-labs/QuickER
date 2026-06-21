using System.IO;
using System.Text;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>ER 図を Mermaid の <c>erDiagram</c> 記法へ変換するサービス</summary>
public static class MermaidExporter
{
    /// <summary>現在の <see cref="MainViewModel" /> から Mermaid 文字列を生成する</summary>
    public static string Build(MainViewModel viewModel)
    {
        var builder = new StringBuilder();
        builder.AppendLine("erDiagram");

        foreach (var entity in viewModel.Entities)
        {
            builder.AppendLine($"    {entity.TableName} {{");

            foreach (var column in entity.Columns)
            {
                builder.AppendLine($"        {BuildColumnLine(column)}");
            }

            builder.AppendLine("    }");
        }

        if (viewModel.Entities.Count > 0 && viewModel.Relationships.Count > 0)
        {
            builder.AppendLine();
        }

        foreach (var relationship in viewModel.Relationships)
        {
            builder.AppendLine($"    {BuildRelationshipLine(relationship)}");
        }

        return builder.ToString();
    }

    /// <summary>Mermaid 文字列をファイルへ保存する</summary>
    public static void SaveTo(MainViewModel viewModel, string path)
    {
        File.WriteAllText(path, Build(viewModel), Encoding.UTF8);
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
    private static string BuildColumnLine(ColumnViewModel column)
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

    /// <summary>Mermaid のリレーション行を構築する</summary>
    private static string BuildRelationshipLine(RelationshipViewModel relationship)
    {
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

        return $"{relationship.Source.TableName} {symbol} {relationship.Target.TableName} : {label}";
    }
}
