using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// DDL の外部キーの文の前に、その方言で適用に失敗し得る外部キーを名指しする注意コメントを書く（英語＝生成 SQL の固定文の正本）。
/// </summary>
/// <remarks>
/// <para>
/// 書くかは扱いの表（<see cref="UnsupportedTreatments"/>）のこの方言の DDL のマスが「注記」かで決め、方言名で分岐しない。
/// </para>
/// <list type="bullet">
/// <item>
/// 参照アクション <c>SET DEFAULT</c>（<see cref="UnsupportedDefinitionKind.SetDefaultReferentialAction"/>）：SQL Server は、
/// NOT NULL の子の列に DEFAULT 制約が無いと <c>SET DEFAULT</c> の外部キーを作れない（エラー 1762）。図は列の既定値を
/// 持たないので、NOT NULL の子の列を名指しする（DB 側の列に DEFAULT 制約があれば通る）。
/// </item>
/// <item>
/// NOT NULL の子の列への参照アクション <c>SET NULL</c>（<see cref="UnsupportedDefinitionKind.SetNullOnNotNullColumn"/>）：
/// 5 方言とも NOT NULL の子の列を名指しする。宣言が「表せない」方言（SQL Server・MySQL）は外部キーを作れないことを、
/// ほかの方言は参照される親の行の削除・更新が失敗することを書く。方言が持たない <c>ON UPDATE</c>（DDL が句を省く）の
/// <c>SET NULL</c> は数えない。
/// </item>
/// <item>
/// 両端の型の長さの違い（<see cref="UnsupportedDefinitionKind.ForeignKeyTypeMismatch"/>）：SQL Server は外部キーの両端の
/// 文字列型の長さが違うと外部キーを作れない（エラー 1753）。長さだけが違う列の組を名指しする。
/// </item>
/// </list>
/// </remarks>
public static class ForeignKeyStatementNotes
{
    /// <summary>外部キーの文の前に注意コメントを書く（該当しなければ何も書かない）</summary>
    /// <param name="sb">書き込み先</param>
    /// <param name="relationship">外部キーのリレーション</param>
    /// <param name="parent">参照先（親）の表</param>
    /// <param name="child">参照元（子）の表</param>
    /// <param name="dialect">DDL の方言（<see cref="DialectNames"/>）</param>
    public static void Append(
        StringBuilder sb,
        Relationship relationship,
        Entity parent,
        Entity child,
        string dialect
    )
    {
        foreach (var line in Lines(relationship, parent, child, dialect))
        {
            sb.AppendLine(line);
        }
    }

    /// <summary>外部キーの文の前に書く注意コメントの行（該当しなければ空。行頭の字下げは付けない）</summary>
    /// <param name="relationship">外部キーのリレーション</param>
    /// <param name="parent">参照先（親）の表</param>
    /// <param name="child">参照元（子）の表</param>
    /// <param name="dialect">DDL の方言（<see cref="DialectNames"/>）</param>
    public static IReadOnlyList<string> Lines(
        Relationship relationship,
        Entity parent,
        Entity child,
        string dialect
    )
    {
        var lines = new List<string>();
        var pairs = ResolveColumns(relationship, parent, child);

        if (
            UnsupportedTreatments.For(
                UnsupportedDefinitionKind.SetDefaultReferentialAction,
                UnsupportedFeature.Ddl,
                dialect
            ) == UnsupportedTreatment.Annotation
            && UsesSetDefault(relationship)
        )
        {
            var notNull = pairs.Where(p => !p.Child.IsNullable).Select(p => p.Child.Name).ToList();

            if (notNull.Count > 0)
            {
                lines.Add(
                    $"-- Note: SET DEFAULT needs a DEFAULT constraint on the NOT NULL referencing column(s) "
                        + $"{SqlComment.Sanitize(string.Join(", ", notNull))}; the diagram does not carry defaults, so this foreign key "
                        + "fails unless the database column has one"
                );
            }
        }

        if (
            UnsupportedTreatments.For(
                UnsupportedDefinitionKind.SetNullOnNotNullColumn,
                UnsupportedFeature.Ddl,
                dialect
            ) == UnsupportedTreatment.Annotation
            && UsesSetNull(relationship, dialect)
        )
        {
            var notNull = pairs.Where(p => !p.Child.IsNullable).Select(p => p.Child.Name).ToList();

            if (notNull.Count > 0)
            {
                // 宣言が「表せない」方言は作れないことを、ほかの方言は親の行の削除・更新が失敗することを書く
                lines.Add(
                    DialectDeclarations.CanOccur(
                        dialect,
                        UnsupportedDefinitionKind.SetNullOnNotNullColumn
                    )
                        ? $"-- Note: SET NULL on the NOT NULL referencing column(s) "
                            + $"{SqlComment.Sanitize(string.Join(", ", notNull))}; deleting or updating a referenced "
                            + "parent row fails because the column cannot be set to NULL"
                        : $"-- Note: SET NULL on the NOT NULL referencing column(s) "
                            + $"{SqlComment.Sanitize(string.Join(", ", notNull))}; this database cannot create "
                            + "this foreign key"
                );
            }
        }

        if (
            UnsupportedTreatments.For(
                UnsupportedDefinitionKind.ForeignKeyTypeMismatch,
                UnsupportedFeature.Ddl,
                dialect
            ) == UnsupportedTreatment.Annotation
        )
        {
            foreach (var (parentColumn, childColumn) in pairs)
            {
                if (
                    !DbTypeText.AreEquivalent(parentColumn.DataType, childColumn.DataType)
                    && SyncDialectCapabilities.DiffersOnlyInStringLength(
                        parentColumn.DataType,
                        childColumn.DataType
                    )
                )
                {
                    lines.Add(
                        $"-- Note: {SqlComment.Sanitize(child.TableName)}.{SqlComment.Sanitize(childColumn.Name)} "
                            + $"({SqlComment.Sanitize(childColumn.DataType)}) and "
                            + $"{SqlComment.Sanitize(parent.TableName)}.{SqlComment.Sanitize(parentColumn.Name)} "
                            + $"({SqlComment.Sanitize(parentColumn.DataType)}) differ in length; this database needs "
                            + "the same length on both ends of a foreign key, so this foreign key fails"
                    );
                }
            }
        }

        return lines;
    }

    /// <summary>ON DELETE / ON UPDATE のどちらかが SET DEFAULT か</summary>
    public static bool UsesSetDefault(Relationship relationship) =>
        relationship.OnDelete == ForeignKeyReferentialAction.SetDefault
        || relationship.OnUpdate == ForeignKeyReferentialAction.SetDefault;

    /// <summary>
    /// ON DELETE か、DDL がこの方言で書く ON UPDATE が SET NULL か（ON UPDATE を持たない方言＝扱いの表の DDL のマスが
    /// 「句を省いて注記」の方言では、ON UPDATE の SET NULL は数えない）
    /// </summary>
    private static bool UsesSetNull(Relationship relationship, string dialect) =>
        relationship.OnDelete == ForeignKeyReferentialAction.SetNull
        || relationship.OnUpdate == ForeignKeyReferentialAction.SetNull
            && UnsupportedTreatments.For(
                UnsupportedDefinitionKind.OnUpdateReferentialAction,
                UnsupportedFeature.Ddl,
                dialect
            ) != UnsupportedTreatment.OmitWithAnnotation;

    /// <summary>リレーションの列ペアを列そのものへ解決する（解決できない組は飛ばす）</summary>
    private static List<(Column Parent, Column Child)> ResolveColumns(
        Relationship relationship,
        Entity parent,
        Entity child
    )
    {
        var resolved = new List<(Column, Column)>();

        foreach (var pair in relationship.ColumnPairs)
        {
            var parentColumn = parent.Columns.FirstOrDefault(c => c.Id == pair.SourceColumnId);
            var childColumn = child.Columns.FirstOrDefault(c => c.Id == pair.TargetColumnId);

            if (parentColumn is not null && childColumn is not null)
            {
                resolved.Add((parentColumn, childColumn));
            }
        }

        return resolved;
    }
}
