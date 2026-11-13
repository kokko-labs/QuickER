using System.Text;
using QuickER.Dialects;

namespace QuickER.Provider;

/// <summary>
/// 子と親が違うスキーマの外部キーを張れない方言の DDL・同期で、作らずに省いた外部キーをその表の <c>CREATE TABLE</c> の前に注記する
/// （<see cref="UnsupportedDefinitionKind.CrossSchemaForeignKey"/>）。
/// </summary>
/// <remarks>
/// SQLite の外部キーの句は表名にスキーマを書けず、親は子と同じ DB の中で解決するので、添付した別の DB の表は参照できない。
/// 省くかどうか（子と親が同じ DB か）は方言の識別子の規則が決め、注記を書くかは扱いの表のこの方言のマスが
/// 「省いて注記」かで決める（方言名で分岐しない）。同期の計画は、その表をドットを含む表名の警告
/// （<see cref="SyncPlanWarningKind.SchemaQualifiedTableName"/>）で名指しする。
/// </remarks>
public static class CrossSchemaForeignKeyNote
{
    /// <summary>扱いの表のこの方言・機能のマスが「省いて注記」なら、省いた外部キーの注記の行を書く</summary>
    /// <param name="sb">書き込み先</param>
    /// <param name="constraintName">省いた外部キーの制約名</param>
    /// <param name="childTable">子の表名（図のまま）</param>
    /// <param name="parentTable">親の表名（図のまま）</param>
    /// <param name="dialect">方言（<see cref="DialectNames"/>）</param>
    /// <param name="feature">DDL か同期か</param>
    public static void Append(
        StringBuilder sb,
        string constraintName,
        string childTable,
        string parentTable,
        string dialect,
        UnsupportedFeature feature
    )
    {
        if (
            UnsupportedTreatments.For(
                UnsupportedDefinitionKind.CrossSchemaForeignKey,
                feature,
                dialect
            ) != UnsupportedTreatment.OmitWithAnnotation
        )
        {
            return;
        }

        // 名前は自由入力なので改行・制御文字を畳んでから載せる（固定文は英語が正本）
        sb.AppendLine(
            $"-- Note: this database cannot reference {SqlComment.Sanitize(parentTable)} from {SqlComment.Sanitize(childTable)} "
                + $"across attached databases; the foreign key {SqlComment.Sanitize(constraintName)} is not created"
        );
    }
}
