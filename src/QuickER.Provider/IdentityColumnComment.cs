using System.Text;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// DB が値を採番して指定した値を拒む列（<see cref="Column.IsIdentity"/>）の列定義行へ添える注記の単一正本。
/// </summary>
/// <remarks>
/// <para>
/// 意味モデルは採番の開始値・増分を持たないため、DDL 生成・同期スクリプト（AddTable / AddColumn / SQLite の
/// テーブル再構築）が作るのは<b>普通の列</b>になる。注記を出すかは扱いの表
/// （<see cref="UnsupportedTreatments"/> の <see cref="UnsupportedDefinitionKind.IdentityRejectingValues"/>）の
/// その機能・その方言のマスが <see cref="UnsupportedTreatment.Annotation"/> かどうかで決める（呼び出し側は方言名で分岐しない）。
/// </para>
/// <para>
/// 主キーでない NOT NULL の採番列だけは 1 文を足す。生成コードはその列を INSERT の対象から外すため、
/// 採番の指定を足すまでは<b>その列の NOT NULL 制約でそのテーブルへの追加が必ず失敗する</b>。
/// 主キーの採番列は生成コードが書き込みから外さない（値を送る）ので足さない。
/// </para>
/// <para>
/// 列名は DB 由来の任意文字列なので <see cref="SqlComment.Sanitize"/> を通す。文面は英語が正本。
/// 計算列でもある列は計算列の注記（<see cref="ComputedColumnComment"/>）と並べて出す。
/// </para>
/// </remarks>
public static class IdentityColumnComment
{
    /// <summary>
    /// 注記を出す列なら注記 1 行（インデント・改行なし）を、そうでなければ <c>null</c> を返す。
    /// </summary>
    /// <param name="column">対象の列</param>
    /// <param name="dialect">出力する方言の名前（<see cref="DialectNames"/>）</param>
    /// <param name="feature">出力する機能（<see cref="UnsupportedFeature.Ddl"/> か <see cref="UnsupportedFeature.Sync"/>）</param>
    public static string? Build(Column column, string dialect, UnsupportedFeature feature)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (
            !column.IsIdentity
            || UnsupportedTreatments.For(
                UnsupportedDefinitionKind.IdentityRejectingValues,
                feature,
                dialect
            ) != UnsupportedTreatment.Annotation
        )
        {
            return null;
        }

        var note =
            $"-- Identity column '{SqlComment.Sanitize(column.Name)}': "
            + "the database assigns its values, but the diagram does not record the identity definition;"
            + " this creates a plain column. Add the identity property by hand.";

        // 主キーでない採番列は生成コードの INSERT が送らないため、NOT NULL のままだと追加が必ず失敗する
        var excludedAndNotNull = !column.IsPrimaryKey && !column.IsNullable;

        return !excludedAndNotNull
            ? note
            : note
                + " Until it is added, generated INSERT statements leave this column out"
                + " and fail on its NOT NULL constraint.";
    }

    /// <summary>注記を出す列なら注記行を指定インデント付きで書き出す（そうでなければ何もしない）</summary>
    /// <param name="sb">書き出し先</param>
    /// <param name="column">対象の列</param>
    /// <param name="indent">行頭に付けるインデント（列定義行と揃える）</param>
    /// <param name="dialect">出力する方言の名前</param>
    /// <param name="feature">出力する機能</param>
    public static void Append(
        StringBuilder sb,
        Column column,
        string indent,
        string dialect,
        UnsupportedFeature feature
    )
    {
        ArgumentNullException.ThrowIfNull(sb);

        if (Build(column, dialect, feature) is { } line)
        {
            sb.AppendLine(indent + line);
        }
    }
}
