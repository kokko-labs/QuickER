using System.IO;
using System.Text;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>
/// ER 図を Mermaid の <c>erDiagram</c> 記法へ変換するサービスです。
/// </summary>
public static class MermaidExporter
{
    /// <summary>
    /// 現在の <see cref="MainViewModel" /> から Mermaid 文字列を生成します。
    /// </summary>
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

    /// <summary>
    /// Mermaid 文字列をファイルへ保存します。
    /// </summary>
    public static void SaveTo(MainViewModel viewModel, string path)
    {
        File.WriteAllText(path, Build(viewModel), Encoding.UTF8);
    }

    /// <summary>
    /// Mermaid の属性型トークン用に DataType を正規化します。
    /// カッコ・カンマ・スペースをアンダースコアに置換し、英数字とアンダースコアのみの文字列にします。
    /// 例: decimal(10,2) → decimal_10_2、nvarchar(100) → nvarchar_100
    /// </summary>
    private static string NormalizeDataType(string dataType)
    {
        // カッコ・カンマ・スペース等の記号をアンダースコアに変換し、連続分は1つにまとめる
        var result = System.Text.RegularExpressions.Regex.Replace(dataType, @"[\s(),]+", "_");

        // 末尾のアンダースコアを除去
        return result.TrimEnd('_');
    }

    /// <summary>
    /// Mermaid の属性行を構築します。
    /// PK と FK が両方設定されている場合は PK を優先し、FK は出力しません
    /// （Mermaid は同一カラムへの PK と FK の同時指定を構文エラーとして扱うため）。
    /// </summary>
    private static string BuildColumnLine(ColumnViewModel column)
    {
        var builder = new StringBuilder();
        builder.Append(NormalizeDataType(column.DataType));
        builder.Append(' ');
        builder.Append(column.Name);

        if (column.IsPrimaryKey)
        {
            // PK が設定されている場合は PK のみ出力
            builder.Append(" PK");
        }
        else if (column.IsForeignKey)
        {
            builder.Append(" FK");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Mermaid のリレーション行を構築します。
    /// </summary>
    private static string BuildRelationshipLine(RelationshipViewModel relationship)
    {
        var symbol = relationship.Type switch
        {
            RelationshipType.OneToOne => "||--||",
            RelationshipType.OneToMany => "||--o{",
            RelationshipType.ManyToMany => "}o--o{",
            _ => "||--o{",
        };
        var label = string.IsNullOrWhiteSpace(relationship.ConstraintName) ? "relates" : relationship.ConstraintName;

        return $"{relationship.Source.TableName} {symbol} {relationship.Target.TableName} : {label}";
    }
}
