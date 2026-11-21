using System.Text;
using QuickER.Dialects;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// PostgreSQL が上限を超える識別子を黙って切り詰めるときの切り方の写し（UTF-8 で 63 バイト以内の最長の先頭部分・文字の途中では切らない）
/// </summary>
/// <remarks>
/// 以前の版の QuickER が PostgreSQL に作った外部キーの名前（縮める前の長い既定名を PostgreSQL が 63 バイトで黙って切った形）を、
/// 差分と同期のテストの前準備で組むための写し。製品は使わない（PostgreSQL が黙って切ることは製品の宣言
/// <c>SyncDialectCapabilities.TruncatesLongIdentifiers</c> が持つ）。切り方の正しさは、同じ名前を実 PostgreSQL で作って取り込む
/// 同期計画の期待の行 <c>realdb-long-default-pg-truncated-fk-name-is-recreated-once</c> が確かめる。
/// Integration は Compile のリンクで使う。
/// </remarks>
public static class PostgreSqlIdentifierTruncation
{
    /// <summary>名前を PostgreSQL の識別子の上限で切り詰める（上限以内ならそのまま）</summary>
    public static string Truncate(string name)
    {
        var limit = IdentifierLengthLimits.PostgreSql;

        if (limit.Fits(name))
        {
            return name;
        }

        var sb = new StringBuilder();
        var used = 0;

        foreach (var rune in name.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > limit.Max)
            {
                break;
            }

            used += rune.Utf8SequenceLength;
            sb.Append(rune.ToString());
        }

        return sb.ToString();
    }
}
