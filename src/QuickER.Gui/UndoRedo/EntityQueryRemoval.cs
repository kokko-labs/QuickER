using System.Collections.Generic;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>エンティティの削除に伴う名前付きクエリの退避と復元（単一削除・一括削除のコマンドが共有する）</summary>
/// <remarks>
/// クエリはエンティティ単位で定義されるので、エンティティを消したあとに残しても、クエリ定義ダイアログにも
/// <c>remove_query</c> ツールにも現れず、生成のたびに警告を出すだけになる。そのためエンティティと一緒に取り除き、
/// Undo で元の位置へ戻す。MCP のツール（<c>remove_entity</c>）も同じ規則でクエリを消す。
/// </remarks>
internal static class EntityQueryRemoval
{
    /// <summary>指定エンティティのクエリを取り除き、元の位置とともに返す</summary>
    /// <param name="main">クエリを持つメイン ViewModel</param>
    /// <param name="entityIds">削除するエンティティの Id</param>
    /// <returns>取り除いたクエリと、取り除く前の位置（昇順）</returns>
    public static List<(int Index, QueryDefinition Query)> Remove(
        MainViewModel main,
        IReadOnlySet<Guid> entityIds
    )
    {
        var removed = new List<(int Index, QueryDefinition Query)>();

        for (var i = 0; i < main.Queries.Count; i++)
        {
            if (entityIds.Contains(main.Queries[i].EntityId))
            {
                removed.Add((i, main.Queries[i]));
            }
        }

        // 後ろから取り除けば、記録した位置がずれない
        for (var i = removed.Count - 1; i >= 0; i--)
        {
            main.Queries.RemoveAt(removed[i].Index);
        }

        return removed;
    }

    /// <summary>取り除いたクエリを元の位置へ戻す</summary>
    /// <param name="main">クエリを持つメイン ViewModel</param>
    /// <param name="removed"><see cref="Remove"/> が返した一覧</param>
    /// <remarks>
    /// 昇順に挿し戻すと、各クエリは取り除く前の位置へ戻る。クエリの一覧は Undo の対象外の操作
    /// （クエリ定義ダイアログの確定など）で差し替わることがあるので、位置は一覧の長さで頭打ちにする
    /// </remarks>
    public static void Restore(
        MainViewModel main,
        IReadOnlyList<(int Index, QueryDefinition Query)> removed
    )
    {
        foreach (var (index, query) in removed)
        {
            main.Queries.Insert(Math.Min(index, main.Queries.Count), query);
        }
    }
}
