using System.IO;
using System.Text;
using QuickER.Model;

namespace QuickER.Services;

/// <summary>
/// ER 図を DBML (Database Markup Language) テキストへ変換するエクスポーター
/// </summary>
/// <remarks>
/// 出力は <see cref="DbmlImporter"/> が解釈できる記法の範囲に限定する
/// <list type="bullet">
///   <item><c>Table</c> ブロック: カラム設定は <c>pk</c> / <c>ref</c> / <c>null</c> / <c>not null</c> / <c>note</c> のみ出力（Indexes・Enum 等は対象外）</item>
///   <item><c>Ref:</c> 行: 多重度を <c>-</c>（1対1）/ <c>&lt;</c>（1対多）/ <c>&lt;&gt;</c>（多対多）の記号で表現</item>
///   <item>note 文字列中のシングルクォートは <c>\'</c> にエスケープ</item>
/// </list>
/// </remarks>
public static class DbmlExporter
{
    /// <summary>
    /// ER 図定義から DBML 文字列を生成する
    /// </summary>
    /// <returns>全 Table ブロックの後に Ref 行をまとめた DBML テキスト（末尾は改行 1 つ）</returns>
    public static string Build(ErDiagram diagram)
    {
        var builder = new StringBuilder();

        foreach (var entity in diagram.Entities)
        {
            builder.AppendLine($"Table {entity.TableName} {{");

            foreach (var column in entity.Columns)
            {
                builder.AppendLine($"  {BuildColumnLine(column)}");
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
        foreach (var relationship in diagram.Relationships)
        {
            var line = BuildRelationshipLine(relationship, entitiesById);
            if (line is not null)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// DBML 文字列を UTF-8 でファイルへ保存する
    /// </summary>
    public static void SaveTo(ErDiagram diagram, string path)
    {
        File.WriteAllText(path, Build(diagram), Encoding.UTF8);
    }

    /// <summary>
    /// DBML のカラム定義行（<c>名前 型 [設定, ...]</c>）を構築する
    /// </summary>
    /// <remarks>
    /// PK 列には <c>pk</c> のみを出力し <c>ref</c> は併記しない。NULL 許可は常に
    /// <c>null</c> / <c>not null</c> のどちらかを明示し、インポート時の既定値依存を避ける
    /// </remarks>
    private static string BuildColumnLine(Column column)
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
    /// DBML のリレーション行（<c>Ref:</c> 行）を構築する
    /// </summary>
    /// <remarks>
    /// 制約名は note 設定として出力する。標準 DBML では設定をエンドポイントの後ろへ置くが、
    /// ここでは <see cref="DbmlImporter"/> との往復を前提に <c>Ref:</c> 直後へ配置する独自形式を採る。
    /// 参照カラム未指定のリレーションは各エンティティの先頭カラムで代用する
    /// </remarks>
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

        var sourceColumn =
            source.Columns.FirstOrDefault(column => column.Id == relationship.SourceColumnId)
            ?? source.Columns.First();
        var targetColumn =
            target.Columns.FirstOrDefault(column => column.Id == relationship.TargetColumnId)
            ?? target.Columns.First();
        var symbol = relationship.Type switch
        {
            RelationshipType.OneToOne => "-",
            RelationshipType.OneToMany => "<",
            RelationshipType.ManyToMany => "<>",
            _ => "<",
        };
        var note = string.IsNullOrWhiteSpace(relationship.ConstraintName)
            ? string.Empty
            : $" [note: '{EscapeNote(relationship.ConstraintName!)}']";

        return $"Ref:{note} {source.TableName}.{sourceColumn.Name} {symbol} {target.TableName}.{targetColumn.Name}";
    }

    /// <summary>
    /// DBML の note リテラル内で使えないシングルクォートを <c>\'</c> へエスケープする
    /// </summary>
    private static string EscapeNote(string text)
    {
        return text.Replace("'", "\\'");
    }
}
