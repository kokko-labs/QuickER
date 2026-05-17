using System.IO;
using System.Text;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>
/// ER 図を DBML 形式へ変換するサービスです。
/// </summary>
public static class DbmlExporter
{
    /// <summary>
    /// 現在の <see cref="MainViewModel" /> から DBML 文字列を生成します。
    /// </summary>
    public static string Build(MainViewModel viewModel)
    {
        var builder = new StringBuilder();

        foreach (var entity in viewModel.Entities)
        {
            builder.AppendLine($"Table {entity.TableName} {{");

            foreach (var column in entity.Columns)
            {
                builder.AppendLine($"  {BuildColumnLine(column)}");
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        foreach (var relationship in viewModel.Relationships)
        {
            builder.AppendLine(BuildRelationshipLine(relationship));
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// DBML 文字列をファイルへ保存します。
    /// </summary>
    public static void SaveTo(MainViewModel viewModel, string path)
    {
        File.WriteAllText(path, Build(viewModel), Encoding.UTF8);
    }

    /// <summary>
    /// DBML のカラム定義行を構築します。
    /// </summary>
    private static string BuildColumnLine(ColumnViewModel column)
    {
        var settings = new List<string>();

        if (column.IsPrimaryKey)
        {
            settings.Add("pk");
        }

        if (column.IsForeignKey && !column.IsPrimaryKey)
        {
            settings.Add("ref");
        }

        settings.Add(column.IsNullable ? "null" : "not null");

        if (!string.IsNullOrWhiteSpace(column.Description))
        {
            settings.Add($"note: '{EscapeNote(column.Description)}'");
        }

        return $"{column.Name} {column.DataType} [{string.Join(", ", settings)}]";
    }

    /// <summary>
    /// DBML のリレーション行を構築します。
    /// </summary>
    private static string BuildRelationshipLine(RelationshipViewModel relationship)
    {
        var sourceColumn = relationship.Source.Columns.FirstOrDefault(column => column.Id == relationship.SourceColumnId) ?? relationship.Source.Columns.First();
        var targetColumn = relationship.Target.Columns.FirstOrDefault(column => column.Id == relationship.TargetColumnId) ?? relationship.Target.Columns.First();
        var symbol = relationship.Type switch
        {
            RelationshipType.OneToOne => "-",
            RelationshipType.OneToMany => "<",
            RelationshipType.ManyToMany => "<>",
            _ => "<",
        };
        var note = string.IsNullOrWhiteSpace(relationship.ConstraintName) ? string.Empty : $" [note: '{EscapeNote(relationship.ConstraintName!)}']";

        return $"Ref:{note} {relationship.Source.TableName}.{sourceColumn.Name} {symbol} {relationship.Target.TableName}.{targetColumn.Name}";
    }

    /// <summary>
    /// DBML の note 文字列に含められないクォートをエスケープします。
    /// </summary>
    private static string EscapeNote(string text)
    {
        return text.Replace("'", "\\'");
    }
}
