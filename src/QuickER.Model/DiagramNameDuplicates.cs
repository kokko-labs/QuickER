using System.Collections.Generic;
using System.Linq;

namespace QuickER.Model;

/// <summary>
/// 図の中で同じ名前（<see cref="SchemaNames"/> の規則）を持つ表と、表の中で同じ名前を持つ列を見つける（判定の正本）。
/// </summary>
/// <remarks>
/// <para>
/// 名前の重複を断る入口（GUI・ツール・形式の取込）を通らずに作られた図（手で編集した図のファイル・同名の表を警告つきで
/// 受け入れる C# リバース）は、同じ名前を 2 つ以上持ち得る。図ファイルの読込検査は名前の重複を断らない（Id の重複だけ）。
/// こうした図の DDL は同じ表・列を 2 回作ろうとして DB に断られ、差分は名前で表を組めずに同じ表の追加を 2 つ出すので、
/// DDL 生成と差分同期の入口がこの判定で図全体を断る。
/// </para>
/// <para>結果は図の並び順に従う（同じ名前の組の中も図の並び順）。</para>
/// </remarks>
public static class DiagramNameDuplicates
{
    /// <summary>同じ名前を持つ表の組（2 つ以上の表名の並び）</summary>
    /// <param name="entities">図の表</param>
    public static IReadOnlyList<IReadOnlyList<string>> Tables(IEnumerable<Entity> entities) =>
        Groups(entities.Select(entity => entity.TableName));

    /// <summary>表の中で同じ名前を持つ列の組（表名と、2 つ以上の列名の並び）</summary>
    /// <param name="entities">図の表</param>
    public static IReadOnlyList<(string TableName, IReadOnlyList<string> ColumnNames)> Columns(
        IEnumerable<Entity> entities
    ) =>
        entities
            .SelectMany(entity =>
                Groups(entity.Columns.Select(column => column.Name))
                    .Select(names => (entity.TableName, names))
            )
            .ToList();

    private static IReadOnlyList<IReadOnlyList<string>> Groups(IEnumerable<string> names) =>
        names
            .GroupBy(name => name, SchemaNames.Comparer)
            .Where(group => group.Count() > 1)
            .Select(group => (IReadOnlyList<string>)group.ToList())
            .ToList();
}
