using System.Text;
using QuickER.Dialects;

namespace QuickER.Provider;

/// <summary>
/// ドットを含む表名（スキーマ付きの名前）を表せない方言の DDL で、その表の <c>CREATE TABLE</c> の前に注記する
/// （<see cref="UnsupportedDefinitionKind.SchemaQualifiedTableName"/>）。
/// </summary>
/// <remarks>
/// SQLite は <c>"a"."b"</c> を「添付したデータベース a の表 b」と読むので、a を添付していない限り文が失敗する
/// （unknown database）。書くかは扱いの表のこの方言の DDL のマスが「注記」かで決め、方言名で分岐しない。
/// 同期では、同じ方言で計画が警告する（<see cref="SyncPlanWarningKind.SchemaQualifiedTableName"/>）。
/// </remarks>
public static class SchemaQualifiedTableNameNote
{
    /// <summary>表名がドットを含み、この方言の DDL のマスが注記なら、注記の行を書く</summary>
    /// <param name="sb">書き込み先</param>
    /// <param name="tableName">表名（図のまま）</param>
    /// <param name="dialect">DDL の方言（<see cref="DialectNames"/>）</param>
    public static void Append(StringBuilder sb, string tableName, string dialect)
    {
        if (
            !tableName.Contains('.')
            || UnsupportedTreatments.For(
                UnsupportedDefinitionKind.SchemaQualifiedTableName,
                UnsupportedFeature.Ddl,
                dialect
            ) != UnsupportedTreatment.Annotation
        )
        {
            return;
        }

        var parts = tableName.Split('.', 2);
        sb.AppendLine(
            $"-- Note: this database reads {SqlComment.Sanitize(tableName)} as table "
                + $"{SqlComment.Sanitize(parts[1])} in the attached database {SqlComment.Sanitize(parts[0])}; "
                + "the statement fails unless that database is attached"
        );
    }
}
