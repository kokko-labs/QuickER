using QuickER.Dialects;

namespace QuickER.Model;

/// <summary>
/// 図の名前のうち、対象 DBMS が受け付けないもの（識別子の長さの上限を超える・表せない文字を含む）を見つけ、
/// ツールの応答に添える英語の注意文にする
/// </summary>
/// <remarks>
/// <para>
/// 判定は DDL・同期の入口と同じ <see cref="IdentifierLengthLimit.DescribeViolation"/>（写しを作らない）。
/// 対象は図に保存された名前＝テーブル名・カラム名・外部キーと一意制約の保存された名前（QuickER が付ける既定名は生成のときに
/// 上限へ収めるので対象外）。上限は図の対象 DBMS のもので、呼び出し側が <see cref="IdentifierLengthLimits.ForDiagramTarget"/> で引いて渡す
/// （Model は方言名から上限を引かない＝<c>ModelDialectLookupGuardTests</c>）。
/// </para>
/// <para>
/// ツールは名前を受け付けて図へ保存し（DDL・同期の入口が断るのは生成のとき）、変更の前後で見つけた違反を比べて、
/// <b>その呼び出しで新しく生じた</b>ものだけを注意文にする（MCP のツールホストと内蔵チャットのツールの両方が同じ関数を通す）。
/// </para>
/// </remarks>
public static class NameLimitNotes
{
    /// <summary>図の中の、対象 DBMS が受け付けない名前を、場所と理由つきの行にして返す</summary>
    /// <param name="diagram">図</param>
    /// <param name="limit">判定に使う識別子の長さの上限（図の対象 DBMS の上限）</param>
    public static IReadOnlyList<string> Find(ErDiagram diagram, IdentifierLengthLimit limit)
    {
        var violations = new List<string>();

        if (limit.IsUnlimited && limit.RepresentsSupplementaryCharacters)
        {
            return violations;
        }

        void Check(string? name, bool isTable, string location)
        {
            if (!string.IsNullOrEmpty(name) && limit.DescribeViolation(name, isTable) is { } reason)
            {
                violations.Add($"{location} '{name}' {reason}");
            }
        }

        foreach (var entity in diagram.Entities)
        {
            Check(entity.TableName, true, "table");

            foreach (var column in entity.Columns)
            {
                Check(column.Name, false, $"column of '{entity.TableName}':");
            }

            foreach (var constraint in entity.UniqueConstraints)
            {
                Check(constraint.Name, false, $"unique constraint of '{entity.TableName}':");
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            Check(relationship.ConstraintName, false, "foreign key");
        }

        return violations;
    }

    /// <summary>変更の前後で見つけた違反を比べ、その変更で新しく生じたものの注意文を返す（無ければ <c>null</c>）</summary>
    /// <param name="before">変更前の <see cref="Find"/></param>
    /// <param name="after">変更後の <see cref="Find"/></param>
    /// <param name="targetDbms">図の対象 DBMS（文面に載せる）</param>
    public static string? Describe(
        IReadOnlyList<string> before,
        IReadOnlyList<string> after,
        string targetDbms
    )
    {
        var added = after.Where(line => !before.Contains(line, StringComparer.Ordinal)).ToList();

        return added.Count == 0
            ? null
            : $"Note: {targetDbms} would refuse these names, so DDL generation and sync stop until they are changed: "
                + string.Join("; ", added)
                + ".";
    }
}
