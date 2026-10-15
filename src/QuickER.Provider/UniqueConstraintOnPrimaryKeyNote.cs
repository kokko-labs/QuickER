using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 主キーと同じ列集合の一意制約を、持てない方言の <c>CREATE TABLE</c> から省き、省いたことを注記する
/// （<see cref="UnsupportedDefinitionKind.UniqueConstraintOnPrimaryKey"/>）。
/// </summary>
/// <remarks>
/// <para>
/// Oracle は主キーと同じ列集合の一意制約を作れず（ORA-02261）、SQLite は作っても索引を作らず取り込みに現れない。
/// どちらも主キーが一意性を保証するので、省いても意味は変わらない。省くかは扱いの表のこの方言・機能のマスが
/// 「省いて注記」かで決め、方言名で分岐しない。
/// </para>
/// <para>
/// 注記は <c>CREATE TABLE</c> の中、制約行の直前に置く（列定義の末尾カンマの判定に影響しないよう、制約行とは別に書く）。
/// </para>
/// </remarks>
public static class UniqueConstraintOnPrimaryKeyNote
{
    /// <summary>解決済みの一意制約を、出すものと省くもの（の注記の行）に分ける</summary>
    /// <param name="entity">対象の表（主キーの正本）</param>
    /// <param name="uniques">解決済みの一意制約</param>
    /// <param name="dialect">出力の方言（<see cref="DialectNames"/>）</param>
    /// <param name="feature">DDL か同期か</param>
    /// <returns>出す一意制約と、省いた一意制約ごとの注記の行（行頭インデント付き）</returns>
    public static (List<ResolvedUniqueConstraint> Kept, List<string> Notes) Split(
        Entity entity,
        IReadOnlyList<ResolvedUniqueConstraint> uniques,
        string dialect,
        UnsupportedFeature feature
    )
    {
        var kept = new List<ResolvedUniqueConstraint>();
        var notes = new List<string>();
        var keyColumns = entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();
        var omits =
            keyColumns.Count > 0
            && UnsupportedTreatments.For(
                UnsupportedDefinitionKind.UniqueConstraintOnPrimaryKey,
                feature,
                dialect
            ) == UnsupportedTreatment.OmitWithAnnotation;
        var keySignature = omits ? UniqueConstraintNaming.ColumnSetSignature(keyColumns) : null;

        foreach (var unique in uniques)
        {
            if (
                omits
                && string.Equals(
                    UniqueConstraintNaming.ColumnSetSignature(unique.ColumnNames),
                    keySignature,
                    StringComparison.Ordinal
                )
            )
            {
                notes.Add(
                    $"    -- Note: this database cannot have a unique constraint on the primary key columns, "
                        + $"so {SqlComment.Sanitize(unique.Name)} "
                        + $"({SqlComment.Sanitize(string.Join(", ", unique.ColumnNames))}) is omitted; "
                        + "the primary key already guarantees uniqueness"
                );
                continue;
            }

            kept.Add(unique);
        }

        return (kept, notes);
    }
}
