using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ツールの行（<see cref="ToolScenarioRows"/>）が図を照合するための写し（1 要素 1 行の文字列）
/// </summary>
/// <remarks>
/// <para>
/// 2 つのホストは Guid を別々に作るので、名前で書く。並びは図の中の並び（テーブル・列・一意制約・リレーション）のまま。
/// 行の書式（値のある項目は必ず書く・順は固定）：
/// </para>
/// <list type="bullet">
/// <item><c>table {表} desc="…" memo="…"</c></item>
/// <item><c>col {表}.{列} {型} pk|nopk null|notnull fk|nofk[ computed][ identity] desc="…"</c></item>
/// <item><c>pkorder {表} (列,…)</c>（主キーの実効順）・<c>pkids {表} (列,…)</c>（保存された順序の並び・引けない Id は <c>?</c>）</item>
/// <item><c>pkreorder {表} (列,…)</c>（主キーの順序を出力へ載せる判定 <c>GetReorderedPrimaryKeyColumnNames</c> が並びを返すときだけ）</item>
/// <item><c>uq {表}.{名前|-} (列,…)</c>（引けない Id は <c>?</c>）</item>
/// <item><c>rel {親}->{子} {種別} name={制約名|-} del={削除時} upd={更新時} pairs=(親の列>子の列,…)</c></item>
/// <item><c>query {表}.{名前} {戻りの形}</c>（クエリの対象の表が引けなければ <c>?</c>）</item>
/// <item><c>qdef {表}.{名前} {戻りの形} id={Id} impl=… scalar=… result=… cond={"…"|-} params=(名前:{型|-|""}[]@列,…) order=(@列 desc,…)
/// paging=… fields=(名前:型@列,…) sql=(方言="…",…) desc="…"</c>（クエリの定義。列の参照は <c>@列名</c>・
/// 引けない列は <c>@?</c>・改行は <c>\n</c>。<c>query</c> の行のあとに同じ並びで並ぶ）</item>
/// </list>
/// <para>名前が空・null のものは <c>-</c>（GUI は一意制約の未設定名を空文字で、MCP は null で持つので同じ写しにそろえる）。</para>
/// </remarks>
internal static class ToolSchemaProjection
{
    /// <summary>図の写し</summary>
    /// <param name="schema">図</param>
    public static List<string> Lines(ErDiagram schema)
    {
        var lines = new List<string>();
        var entities = schema.Entities.ToDictionary(entity => entity.Id);

        foreach (var entity in schema.Entities)
        {
            var columns = entity.Columns.ToDictionary(column => column.Id);
            string ColumnName(Guid id) =>
                columns.TryGetValue(id, out var column) ? column.Name : "?";

            lines.Add(
                $"table {entity.TableName} desc=\"{entity.Description}\" memo=\"{entity.Memo}\""
            );

            foreach (var column in entity.Columns)
            {
                lines.Add(
                    $"col {entity.TableName}.{column.Name} {column.DataType}"
                        + (column.IsPrimaryKey ? " pk" : " nopk")
                        + (column.IsNullable ? " null" : " notnull")
                        + (column.IsForeignKey ? " fk" : " nofk")
                        + (column.IsComputed ? " computed" : string.Empty)
                        + (column.IsIdentity ? " identity" : string.Empty)
                        + $" desc=\"{column.Description}\""
                );
            }

            lines.Add(
                $"pkorder {entity.TableName} ({string.Join(",", entity.GetPrimaryKeyColumnsInOrder().Select(column => column.Name))})"
            );
            lines.Add(
                $"pkids {entity.TableName} ({string.Join(",", entity.PrimaryKeyColumnIds.Select(ColumnName))})"
            );

            if (entity.GetReorderedPrimaryKeyColumnNames() is { } reordered)
            {
                lines.Add($"pkreorder {entity.TableName} ({string.Join(",", reordered)})");
            }

            foreach (var unique in entity.UniqueConstraints)
            {
                lines.Add(
                    $"uq {entity.TableName}.{OrDash(unique.Name)} ({string.Join(",", unique.ColumnIds.Select(ColumnName))})"
                );
            }
        }

        foreach (var relationship in schema.Relationships)
        {
            var source = entities.GetValueOrDefault(relationship.SourceEntityId);
            var target = entities.GetValueOrDefault(relationship.TargetEntityId);
            var pairs = relationship.ColumnPairs.Select(pair =>
                $"{ColumnOf(source, pair.SourceColumnId)}>{ColumnOf(target, pair.TargetColumnId)}"
            );

            lines.Add(
                $"rel {source?.TableName ?? "?"}->{target?.TableName ?? "?"} {relationship.Type}"
                    + $" name={OrDash(relationship.ConstraintName)}"
                    + $" del={relationship.OnDelete} upd={relationship.OnUpdate}"
                    + $" pairs=({string.Join(",", pairs)})"
            );
        }

        var columnNames = schema
            .Entities.SelectMany(entity => entity.Columns)
            .GroupBy(column => column.Id)
            .ToDictionary(group => group.Key, group => group.First().Name);
        string Ref(Guid? id) =>
            id is { } value ? "@" + columnNames.GetValueOrDefault(value, "?") : string.Empty;

        foreach (var query in schema.Queries)
        {
            var table = entities.GetValueOrDefault(query.EntityId)?.TableName ?? "?";
            lines.Add($"query {table}.{query.Name} {query.Returns}");
        }

        foreach (var query in schema.Queries)
        {
            var table = entities.GetValueOrDefault(query.EntityId)?.TableName ?? "?";
            var parameters = query.Parameters.Select(parameter =>
                $"{parameter.Name}:{NullOrText(parameter.Type)}{(parameter.IsList ? "[]" : string.Empty)}{Ref(parameter.SourceColumnId)}"
            );
            var orderBy = query.OrderBy.Select(ordering =>
                Ref(ordering.ColumnId) + (ordering.Descending ? " desc" : string.Empty)
            );
            var fields = query.Fields.Select(field =>
                $"{field.Name}:{NullOrText(field.Type)}{Ref(field.SourceColumnId)}"
            );
            var sql = query
                .Sql.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}=\"{Escape(pair.Value)}\"");
            lines.Add(
                $"qdef {table}.{query.Name} {query.Returns} id={query.Id:N} impl={query.Implementation}"
                    + $" scalar={OrDash(query.ScalarType)} result={OrDash(query.ResultTypeName)}"
                    + $" cond={NullOrQuoted(query.Condition)} params=({string.Join(",", parameters)})"
                    + $" order=({string.Join(",", orderBy)}) paging={query.HasPaging}"
                    + $" fields=({string.Join(",", fields)}) sql=({string.Join(",", sql)})"
                    + $" desc=\"{Escape(query.Description)}\""
            );
        }

        return lines;
    }

    /// <summary>引用の中に置く文字列（改行を見える形にする・null は空）</summary>
    private static string Escape(string? text) =>
        (text ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");

    private static string ColumnOf(Entity? entity, Guid columnId) =>
        entity?.Columns.FirstOrDefault(column => column.Id == columnId)?.Name ?? "?";

    private static string OrDash(string? name) => string.IsNullOrEmpty(name) ? "-" : name;

    /// <summary>null は <c>-</c>・空文字は <c>""</c>・ほかはそのまま（null と空を写しで見分ける）</summary>
    private static string NullOrText(string? text) =>
        text is null ? "-"
        : text.Length == 0 ? "\"\""
        : text;

    /// <summary>null は <c>-</c>・ほかは引用（空文字は <c>""</c>）</summary>
    private static string NullOrQuoted(string? text) => text is null ? "-" : $"\"{Escape(text)}\"";
}
