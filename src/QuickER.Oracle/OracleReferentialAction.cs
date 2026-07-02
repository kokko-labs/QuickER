using QuickER.Model;

namespace QuickER.Oracle;

/// <summary>
/// Oracle の外部キー参照アクション句を組み立てるヘルパー。
/// </summary>
/// <remarks>
/// Oracle は <c>ON UPDATE</c> をサポートせず、<c>ON DELETE</c> も <c>CASCADE</c> / <c>SET NULL</c> の
/// 2 種のみを句として出力できる（<c>NO ACTION</c> は既定のため省略）。
/// <c>SET DEFAULT</c> も Oracle には無いため句を出力しない（既定＝NO ACTION 扱い）。
/// </remarks>
public static class OracleReferentialAction
{
    /// <summary><c>ON DELETE</c> 句を組み立てる（対象外のアクションは空文字）</summary>
    /// <param name="onDelete">削除時アクション</param>
    /// <returns>
    /// <see cref="ForeignKeyReferentialAction.Cascade"/> なら <c>" ON DELETE CASCADE"</c>、
    /// <see cref="ForeignKeyReferentialAction.SetNull"/> なら <c>" ON DELETE SET NULL"</c>、
    /// それ以外（NoAction / SetDefault）は空文字。
    /// </returns>
    public static string BuildOnDeleteClause(ForeignKeyReferentialAction onDelete) =>
        onDelete switch
        {
            ForeignKeyReferentialAction.Cascade => " ON DELETE CASCADE",
            ForeignKeyReferentialAction.SetNull => " ON DELETE SET NULL",
            _ => string.Empty,
        };
}
