using System.Text;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 計算列・生成列（<see cref="Column.IsComputed"/>）の列定義行へ添える注意コメントの単一正本。
/// </summary>
/// <remarks>
/// <para>
/// 意味モデルは式を持たないため、取り込んだ計算列から生成する DDL は<b>普通の列</b>を作る。
/// 黙って普通の列を出すと、その DDL を当てた DB では計算列が消えて値が入らないため、
/// 列定義行の直前に「式は図に無い・手で足すこと」を書き添える。
/// </para>
/// <para>
/// 列定義行を組み立てる箇所は DDL 生成（5 方言）と同期スクリプト生成（AddTable / AddColumn / SQLite の
/// テーブル再構築）に 12 箇所散っている。文面が散らないようここへ 1 本化する
/// （<see cref="TableConstraintLineBuilder"/> が制約行に対して担うのと同じ役割）。
/// </para>
/// <para>
/// NOT NULL の計算列だけは 1 文を足す。生成コードは計算列を INSERT の対象から外す（式が入るはずの列なので
/// 正しい）ため、式を足すまでは<b>その列の NOT NULL 制約でそのテーブルへの追加が必ず失敗する</b>。
/// 式のない普通の列として作った直後がいちばん踏みやすいので、DDL を読む時点で告げる。
/// NULL 許容の計算列の出力は従来どおり（バイト不変）。
/// </para>
/// <para>
/// 列名は DB 由来の任意文字列なので <see cref="SqlComment.Sanitize"/> を通す（コメント行の突き破り対策）。
/// 文面は英語が正本（生成 SQL の固定文と同じ規則）。
/// </para>
/// </remarks>
public static class ComputedColumnComment
{
    /// <summary>
    /// 計算列なら注意コメント 1 行（インデント・改行なし）を、そうでなければ <c>null</c> を返す。
    /// </summary>
    public static string? Build(Column column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!column.IsComputed)
        {
            return null;
        }

        var note =
            $"-- Computed column '{SqlComment.Sanitize(column.Name)}': "
            + "the expression is not part of the diagram; add it to the schema by hand.";

        // NOT NULL のまま式を足さないと、生成コードの INSERT がこの列を送らないため追加が必ず失敗する
        return column.IsNullable
            ? note
            : note
                + " Until the expression is added, generated INSERT statements leave this column out"
                + " and fail on its NOT NULL constraint.";
    }

    /// <summary>計算列なら注意コメント行を指定インデント付きで書き出す（そうでなければ何もしない）</summary>
    /// <param name="sb">書き出し先</param>
    /// <param name="column">対象の列</param>
    /// <param name="indent">行頭に付けるインデント（列定義行と揃える）</param>
    public static void Append(StringBuilder sb, Column column, string indent)
    {
        ArgumentNullException.ThrowIfNull(sb);

        if (Build(column) is { } line)
        {
            sb.AppendLine(indent + line);
        }
    }
}
