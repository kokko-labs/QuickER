using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickER.Provider;

/// <summary>
/// テーブル再構築（rebuild 方言＝SQLite）で失われる列レベル属性を、live の <c>CREATE TABLE</c> 文から検出する。
/// </summary>
/// <remarks>
/// <para>
/// 再構築の <c>CREATE TABLE</c> は意味モデル（<see cref="QuickER.Model.Column"/> の 7 プロパティ）から
/// 組み立て直すため、モデルが持たない <c>AUTOINCREMENT</c> / <c>DEFAULT</c> / <c>CHECK</c> / <c>COLLATE</c> /
/// 生成列（<c>GENERATED ALWAYS AS</c>・省略形の <c>AS (...)</c>）は再現されずに黙って消える。
/// インデックス・トリガーは <see cref="SchemaAuxiliaryObject"/> が CREATE 文全文で温存しているのと対照的。
/// </para>
/// <para>
/// なお <b>rowid 別名（暗黙の自動採番）は失われない</b>。SQLite では表制約形式の <c>PRIMARY KEY(x)</c> でも
/// <c>x INTEGER</c> なら rowid 別名になるため、明示 <c>AUTOINCREMENT</c> の有無だけが差になる。
/// そのため rowid 別名は検出対象に含めない（含めると常時警告になり、本当の喪失が埋もれる）。
/// </para>
/// <para>
/// <b>検出精度の割り切り</b>: 完全な SQL パーサーは持たず、<c>CREATE TABLE</c> の最外側の括弧の中だけを
/// 対象に、文字列リテラル（<c>'...'</c>）・引用識別子（<c>"..."</c> / <c>[...]</c> / <c>`...`</c>）・
/// コメント（<c>--</c> / <c>/* */</c>）を取り除いてからキーワードを語境界付き・大文字小文字非依存で探す。
/// 取り除いた後でも「テーブル名が DEFAULT を含む列の CHECK 式」のような入れ子の誤検出は理屈上あり得るが、
/// これは実行を止めない警告であり、過剰に出ても害が小さい側（見落としのほうが有害）に倒している。
/// </para>
/// </remarks>
public static class TableRebuildAttributeDetector
{
    /// <summary>検出対象のキーワード（表示にそのまま使うため SQL の綴りで持つ）</summary>
    private static readonly (string Token, Regex Pattern)[] Keywords =
    [
        ("AUTOINCREMENT", Word("AUTOINCREMENT")),
        ("DEFAULT", Word("DEFAULT")),
        ("CHECK", Word("CHECK")),
        ("COLLATE", Word("COLLATE")),
        ("GENERATED", Word("GENERATED")),
    ];

    /// <summary>生成列の省略形（<c>col AS (expr)</c>）。<c>GENERATED</c> と同じ喪失なので同じトークンで報告する</summary>
    private static readonly Regex ShorthandGeneratedColumn = new(
        @"\bAS\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// <c>CREATE TABLE</c> 文から、再構築で失われる列レベル属性のトークンを検出する。
    /// </summary>
    /// <param name="createTableSql">live の <c>CREATE TABLE</c> 文全文（<c>null</c> / 空なら検出なし）</param>
    /// <returns>検出したトークン（<c>AUTOINCREMENT</c> 等）を出現順・重複なしで返す。無ければ空</returns>
    public static IReadOnlyList<string> Detect(string? createTableSql)
    {
        var body = ExtractTableBody(createTableSql);

        if (body.Length == 0)
        {
            return [];
        }

        var found = new List<string>();

        foreach (var (token, pattern) in Keywords)
        {
            if (pattern.IsMatch(body))
            {
                found.Add(token);
            }
        }

        if (!found.Contains("GENERATED") && ShorthandGeneratedColumn.IsMatch(body))
        {
            found.Add("GENERATED");
        }

        return found;
    }

    /// <summary>語境界付き・大文字小文字非依存のキーワード検索パターンを作る</summary>
    private static Regex Word(string keyword) =>
        new($@"\b{keyword}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// <c>CREATE TABLE ... ( ... )</c> の最外側の括弧の中身を取り出し、
    /// 文字列リテラル・引用識別子・コメントを取り除いた文字列を返す。
    /// </summary>
    /// <remarks>
    /// テーブル名側（<c>CREATE TABLE "default_values"</c> 等）を検査対象から外すのが目的。
    /// 括弧が見つからない（<c>CREATE TABLE x AS SELECT ...</c> 等）場合は空を返す＝検出なし。
    /// </remarks>
    private static string ExtractTableBody(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(sql.Length);
        var depth = 0;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            // コメントは中身ごと捨てる（キーワードを含んでいても属性ではない）
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;

                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }

                i++;

                continue;
            }

            // 文字列リテラル・引用識別子は中身ごと捨てる（既定値の文字列に 'CHECK' 等が入っていても属性ではない）
            if (c is '\'' or '"' or '`' or '[')
            {
                var close = c switch
                {
                    '[' => ']',
                    _ => c,
                };
                i++;

                while (i < sql.Length && sql[i] != close)
                {
                    i++;
                }

                continue;
            }

            if (c == '(')
            {
                depth++;

                // 最外側の開き括弧そのものは本体に含めない
                if (depth == 1)
                {
                    continue;
                }
            }
            else if (c == ')')
            {
                depth--;

                // 最外側の閉じ括弧で本体は終わり（以降のテーブルオプションは対象外）
                if (depth == 0)
                {
                    break;
                }
            }

            if (depth >= 1)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
