using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>エンティティ・リレーションの構造を表す署名文字列を計算する（DB 方言に依存しない）</summary>
/// <remarks>取込前後で構造が同一かどうかの判定に用いる 署名が一致すれば構造的に同一とみなす</remarks>
public static class SchemaSignature
{
    /// <summary>スキーマ内容の一致比較に使う署名文字列を生成する（取込前後の置換要否判定に用いる）</summary>
    public static string Compute(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var e = string.Join(
            "|",
            entities
                .OrderBy(x => x.TableName)
                .Select(x =>
                    x.TableName
                    + ":"
                    + string.Join(
                        ",",
                        x.Columns.Select(c =>
                            c.Name
                            + "("
                            + c.DataType
                            + (c.IsPrimaryKey ? "*PK" : "")
                            + (c.IsNullable ? "*NULL" : "*NOTNULL")
                            + ")"
                        )
                    )
                    + "#"
                    + UniqueConstraintPart(x)
                )
        );
        var r = string.Join(
            "|",
            relationships
                .Select(x =>
                    x.SourceEntityId
                    + ">"
                    + x.TargetEntityId
                    + ":"
                    + x.Type
                    + ":"
                    + x.SourceColumnId
                    + ":"
                    + x.TargetColumnId
                    + ":"
                    + x.ConstraintName
                    + ":"
                    + x.OnDelete
                    + ":"
                    + x.OnUpdate
                )
                .OrderBy(s => s)
        );
        return e + "##" + r;
    }

    /// <summary>エンティティの一意制約を署名の部分文字列へ変換する</summary>
    /// <remarks>
    /// 制約ごとに「制約名（未設定は空）＋宣言順の構成列名」を組み立て、制約リストの並び順に
    /// 依存しないようソートして連結する（取込側の列挙順が変わっても署名がぶれないようにする）。
    /// 解決できないカラム ID は ID そのものを使う（差異は差異として署名に残す）。
    /// </remarks>
    private static string UniqueConstraintPart(Entity entity)
    {
        if (entity.UniqueConstraints.Count == 0)
        {
            return string.Empty;
        }

        var columnNamesById = entity.Columns.ToDictionary(c => c.Id, c => c.Name);

        return string.Join(
            ",",
            entity
                .UniqueConstraints.Select(u =>
                    (u.Name ?? "")
                    + "("
                    + string.Join(
                        "+",
                        u.ColumnIds.Select(id =>
                            columnNamesById.TryGetValue(id, out var name) ? name : id.ToString()
                        )
                    )
                    + ")"
                )
                .OrderBy(s => s, StringComparer.Ordinal)
        );
    }
}
