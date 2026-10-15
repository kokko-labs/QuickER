using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// DB が採番して指定した値を拒む列の印（<see cref="Column.IsIdentity"/>）を、その種類の列を持てない方言で扱うときの判定
/// </summary>
/// <remarks>
/// <para>
/// 持てるかは方言の宣言（<see cref="DialectDeclarations"/> の <see cref="UnsupportedDefinitionKind.IdentityRejectingValues"/>）が
/// 決める。呼び出し側は方言名で分岐しない。
/// </para>
/// <para>
/// 持てない方言では、DDL は印の列を普通の列として作り、生成コードはその列を INSERT / UPDATE から外すので、
/// 値がどこからも書かれない。GUI の対象 DBMS の切替は印を外し、図と違う方言で生成する CLI・MCP は警告で名指しする。
/// </para>
/// </remarks>
public static class IdentityColumnMarks
{
    /// <summary>その方言がこの種類の列を持てるか</summary>
    /// <param name="dialect">方言の名前（<see cref="DialectNames"/>）</param>
    public static bool CanBeHeldBy(string dialect) =>
        DialectDeclarations.CanOccur(dialect, UnsupportedDefinitionKind.IdentityRejectingValues);

    /// <summary>
    /// <paramref name="dialect"/> が持てないとき、印の付いた列を「テーブル.列」の形で返す（持てるとき・印が無いときは空）
    /// </summary>
    /// <remarks>名前はファイル由来の任意文字列なので、1 行に収まるよう改行・制御文字を畳む。</remarks>
    public static IReadOnlyList<string> NamesUnsupportedBy(ErDiagram diagram, string dialect)
    {
        ArgumentNullException.ThrowIfNull(diagram);

        if (CanBeHeldBy(dialect))
        {
            return [];
        }

        return diagram
            .Entities.SelectMany(entity =>
                entity
                    .Columns.Where(column => column.IsIdentity)
                    .Select(column =>
                        $"{SqlComment.Sanitize(entity.TableName)}.{SqlComment.Sanitize(column.Name)}"
                    )
            )
            .ToList();
    }
}
