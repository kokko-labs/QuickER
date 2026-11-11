using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 図が同じ名前の表・表の中で同じ名前の列を持てば、名指しで図全体を断る（DDL 生成と差分同期の入口）。
/// </summary>
/// <remarks>
/// 判定は Model の <see cref="DiagramNameDuplicates"/>（名前の同一性は <see cref="SchemaNames"/>）で、ここは例外文を組むだけ。
/// DDL は同じ表・列を 2 回作ろうとして DB に断られ、差分は名前で表を組めずに同じ表の追加を 2 つ出すので、どちらも入口で止める。
/// 5 方言とも同じ扱い（方言の宣言に差を持たせない）。
/// </remarks>
public static class SqlNameDuplicates
{
    /// <summary>同じ名前の表・列があれば名指しで例外を投げる</summary>
    /// <param name="entities">図の表</param>
    /// <exception cref="InvalidOperationException">同じ名前の表・列がある場合</exception>
    public static void Validate(IReadOnlyList<Entity> entities)
    {
        var groups = DiagramNameDuplicates
            .Tables(entities)
            .Select(names => string.Join(" and ", names.Select(name => $"TABLE '{name}'")))
            .Concat(
                DiagramNameDuplicates
                    .Columns(entities)
                    .Select(group =>
                        string.Join(
                            " and ",
                            group.ColumnNames.Select(name => $"COLUMN '{group.TableName}'.'{name}'")
                        )
                    )
            )
            .ToList();

        if (groups.Count > 0)
        {
            throw new InvalidOperationException(
                "The following tables or columns have the same name: "
                    + string.Join(", ", groups)
                    + ". Names that differ only in letter case or in leading and trailing spaces are the same name."
                    + " Give each of them a distinct name in the diagram."
            );
        }
    }
}
