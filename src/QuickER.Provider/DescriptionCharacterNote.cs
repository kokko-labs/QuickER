using System.Text;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 説明の BMP の外の文字（絵文字など）を保存できない方言で、DDL の説明の前に添える注記の単一正本
/// </summary>
/// <remarks>
/// <para>
/// 判定は方言の識別子の上限の宣言（<see cref="IdentifierLengthLimit.RepresentsSupplementaryCharacters"/>＝MySQL だけ
/// <c>false</c>）と <see cref="IdentifierLengthLimit.CanRepresent"/> だけで行い、方言名で分岐しない。MySQL はそうした文字を
/// 断らずに 1 文字ずつ <c>?</c> へ置き換えて保存するので、DDL を読む時点で告げる（差分は置き換えた形で比べるので、
/// 適用した後に差分は出ない＝<see cref="IdentifierLengthLimit.StoredText"/>）。
/// </para>
/// <para>
/// 表・列の名前は任意の文字列なので <see cref="SqlComment.Sanitize"/> を通す。文面は英語が正本。
/// </para>
/// </remarks>
public static class DescriptionCharacterNote
{
    /// <summary>表の説明が置き換わる文字を含むなら、注記 1 行を書き出す（そうでなければ何もしない）</summary>
    /// <param name="sb">出力先</param>
    /// <param name="entity">表</param>
    /// <param name="limit">DDL の方言の識別子の上限</param>
    public static void AppendForTable(StringBuilder sb, Entity entity, IdentifierLengthLimit limit)
    {
        if (IsReplaced(entity.Description, limit))
        {
            sb.AppendLine($"{Prefix}table {SqlComment.Sanitize(entity.TableName)}{Suffix}");
        }
    }

    /// <summary>列の説明が置き換わる文字を含むなら、注記 1 行をインデント付きで書き出す（そうでなければ何もしない）</summary>
    /// <param name="sb">出力先</param>
    /// <param name="tableName">表の名前</param>
    /// <param name="column">列</param>
    /// <param name="indent">行頭のインデント</param>
    /// <param name="limit">DDL の方言の識別子の上限</param>
    public static void AppendForColumn(
        StringBuilder sb,
        string tableName,
        Column column,
        string indent,
        IdentifierLengthLimit limit
    )
    {
        if (IsReplaced(column.Description, limit))
        {
            sb.Append(indent)
                .AppendLine(
                    $"{Prefix}column {SqlComment.Sanitize(tableName)}.{SqlComment.Sanitize(column.Name)}{Suffix}"
                );
        }
    }

    private static bool IsReplaced(string? description, IdentifierLengthLimit limit) =>
        DescriptionText.IsPresent(description) && !limit.CanRepresent(description);

    private const string Prefix =
        "-- Note: this database stores each character outside the Basic Multilingual Plane in the description of ";

    private const string Suffix = " as '?'.";
}
