using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>ツールの引数で名指しされたテーブル・列を図から引く共通処理（図ツールとクエリツールが共有する）</summary>
/// <remarks>名前の照合はどのツールも <see cref="SchemaNames.AreSame"/>（前後の空白を除き大文字小文字を区別しない）で行い、最初に一致したものを返す</remarks>
internal static class SchemaLookup
{
    /// <summary>テーブル名でエンティティを検索する（前後の空白を除き大文字小文字を区別しない・最初の一致）</summary>
    /// <param name="schema">検索対象の図</param>
    /// <param name="tableName">テーブル名</param>
    /// <returns>見つかったエンティティ。無ければ null</returns>
    public static Entity? FindEntity(ErDiagram schema, string tableName) =>
        schema.Entities.FirstOrDefault(entity => SchemaNames.AreSame(entity.TableName, tableName));

    /// <summary>ID でエンティティを検索する</summary>
    /// <param name="schema">検索対象の図</param>
    /// <param name="id">エンティティの ID</param>
    /// <returns>見つかったエンティティ。無ければ null</returns>
    public static Entity? FindEntityById(ErDiagram schema, Guid id) =>
        schema.Entities.FirstOrDefault(entity => entity.Id == id);

    /// <summary>列名でエンティティ内の列を検索する（前後の空白を除き大文字小文字を区別しない・最初の一致）</summary>
    /// <param name="entity">検索対象のエンティティ</param>
    /// <param name="columnName">列名</param>
    /// <returns>見つかった列。無ければ null</returns>
    public static Column? FindColumn(Entity entity, string columnName) =>
        entity.Columns.FirstOrDefault(column => SchemaNames.AreSame(column.Name, columnName));
}
