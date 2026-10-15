using System.Text;
using QuickER.Model;

namespace QuickER.Provider.Oracle;

/// <summary>
/// Oracle の外部キー参照アクション句と、省いた句の注意コメントを組み立てるヘルパー（DDL 生成と同期スクリプトが共有する）。
/// </summary>
/// <remarks>
/// Oracle は <c>ON UPDATE</c> をサポートせず、<c>ON DELETE</c> も <c>CASCADE</c> / <c>SET NULL</c> の
/// 2 種のみを句として出力できる（<c>NO ACTION</c> は既定のため省略）。
/// <c>SET DEFAULT</c> も Oracle には無いため句を出力しない（既定＝NO ACTION 扱い）。
/// 省いた句は <see cref="AppendNotes"/> の注意コメントで残す（宣言どおりに動くと誤解させないため）。
/// </remarks>
public static class OracleReferentialAction
{
    /// <summary>ON UPDATE を省いたことを告げる注意コメント（英語＝生成 SQL の固定文の正本）</summary>
    public const string OnUpdateNote = "-- Note: Oracle does not support ON UPDATE; ignoring it";

    /// <summary>ON DELETE SET DEFAULT を省いたことを告げる注意コメント（英語＝生成 SQL の固定文の正本）</summary>
    public const string SetDefaultNote =
        "-- Note: Oracle does not support ON DELETE SET DEFAULT; using NO ACTION";

    /// <summary>外部キーの文の前に、Oracle が持たない参照アクションを省いた旨の注意コメントを書く</summary>
    /// <param name="sb">書き込み先</param>
    /// <param name="relationship">外部キーのリレーション（null なら何も書かない）</param>
    public static void AppendNotes(StringBuilder sb, Relationship? relationship)
    {
        if (relationship is null)
        {
            return;
        }

        if (relationship.OnUpdate != ForeignKeyReferentialAction.NoAction)
        {
            sb.AppendLine(OnUpdateNote);
        }

        if (relationship.OnDelete == ForeignKeyReferentialAction.SetDefault)
        {
            sb.AppendLine(SetDefaultNote);
        }
    }

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
