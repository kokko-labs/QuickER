using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Integration;

/// <summary>
/// 「実 DB へ適用 → 取込 → 取込図から DDL を再生成 → 再適用 → 再取込」の往復で、
/// 2 回の取込結果が一致することを表明する共有ヘルパー（5 方言の再適用テストが使う）。
/// </summary>
/// <remarks>
/// <para>
/// 見ているのは 2 つのこと。1 つは<b>再適用が成功すること</b>＝取込が持ち帰った型表記が、その方言の DDL
/// として実際に通ること（従来 PostgreSQL の <c>numeric(10,2046)</c> のように、取り込めても DDL として
/// 再適用できない表記が実在した）。もう 1 つは<b>不動点であること</b>＝1 回目と 2 回目の取込結果が
/// 一致すること（食い違うと、取り込んだ図と DB の間に「同期しても消えない差分」が残る）。
/// </para>
/// <para>
/// 比較は意味モデルの観測可能な面だけを見る（Guid は取込ごとに変わるため名前で突き合わせる）。
/// </para>
/// </remarks>
internal static class SchemaReapplyAssertions
{
    /// <summary>2 回の取込結果（エンティティ・リレーション）が一致することを表明する</summary>
    public static void ShouldRoundTrip(
        IReadOnlyList<Entity> firstEntities,
        IReadOnlyList<Relationship> firstRelationships,
        IReadOnlyList<Entity> secondEntities,
        IReadOnlyList<Relationship> secondRelationships
    )
    {
        Project(secondEntities)
            .Should()
            .BeEquivalentTo(
                Project(firstEntities),
                "再適用してから取り込み直した図は、1 回目の取込と一致すること（不動点）"
            );

        Project(secondRelationships, secondEntities)
            .Should()
            .BeEquivalentTo(
                Project(firstRelationships, firstEntities),
                "リレーションも再適用で失われたり変質したりしないこと"
            );
    }

    /// <summary>エンティティを名前ベースの比較可能な形へ射影する</summary>
    private static object Project(IReadOnlyList<Entity> entities) =>
        entities
            .OrderBy(e => e.TableName, StringComparer.Ordinal)
            .Select(e => new
            {
                e.TableName,
                e.Description,
                Columns = e
                    .Columns.Select(c => new
                    {
                        c.Name,
                        c.DataType,
                        c.IsNullable,
                        c.IsPrimaryKey,
                        c.Description,
                    })
                    .ToList(),
                // 制約名は合成名との揺れがあるため構成列だけを見る（列集合照合は差分同期と同じ流儀）
                UniqueConstraints = e
                    .UniqueConstraints.Select(u =>
                        string.Join(
                            ",",
                            u.ColumnIds.Select(id =>
                                e.Columns.FirstOrDefault(c => c.Id == id)?.Name ?? "?"
                            )
                        )
                    )
                    .OrderBy(text => text, StringComparer.Ordinal)
                    .ToList(),
            })
            .ToList();

    /// <summary>リレーションを名前ベースの比較可能な形へ射影する</summary>
    private static object Project(
        IReadOnlyList<Relationship> relationships,
        IReadOnlyList<Entity> entities
    )
    {
        string TableName(Guid id) =>
            entities.FirstOrDefault(e => e.Id == id)?.TableName ?? id.ToString();

        string ColumnName(Guid id) =>
            entities.SelectMany(e => e.Columns).FirstOrDefault(c => c.Id == id)?.Name
            ?? id.ToString();

        return relationships
            .Select(r => new
            {
                Source = TableName(r.SourceEntityId),
                Target = TableName(r.TargetEntityId),
                r.Type,
                r.OnDelete,
                r.OnUpdate,
                Pairs = r
                    .ColumnPairs.Select(p =>
                        $"{ColumnName(p.SourceColumnId)}->{ColumnName(p.TargetColumnId)}"
                    )
                    .ToList(),
            })
            .OrderBy(
                r => $"{r.Source}|{r.Target}|{string.Join(",", r.Pairs)}",
                StringComparer.Ordinal
            )
            .ToList();
    }
}
