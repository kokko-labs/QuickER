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
}
