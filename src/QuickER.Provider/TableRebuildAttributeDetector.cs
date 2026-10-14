using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickER.Provider;

/// <summary>
/// テーブル再構築（rebuild 方言＝SQLite）で失われる属性を、live の <c>CREATE TABLE</c> 文から検出する。
/// </summary>
/// <remarks>
/// <para>
/// 再構築の <c>CREATE TABLE</c> は意味モデル（<see cref="QuickER.Model.Column"/> の 7 プロパティ）から
/// 組み立て直すため、モデルが持たない次の属性は再現されずに黙って消える。
/// インデックス・トリガーは <see cref="SchemaAuxiliaryObject"/> が CREATE 文全文で温存しているのと対照的。
/// </para>
/// <list type="bullet">
///   <item>列の属性: <c>AUTOINCREMENT</c> / <c>DEFAULT</c> / <c>CHECK</c> / <c>COLLATE</c> /
///   生成列（<c>GENERATED ALWAYS AS</c>・省略形の <c>AS (...)</c>）</item>
///   <item>制約の句: 競合解決句（<c>ON CONFLICT</c>）と、外部キーの遅延（<c>DEFERRABLE INITIALLY DEFERRED</c>）</item>
///   <item>テーブルオプション（最外の括弧の後ろ）: <c>WITHOUT ROWID</c> / <c>STRICT</c></item>
/// </list>
/// <para>
/// 外部キーの遅延は <c>DEFERRABLE INITIALLY DEFERRED</c> の形だけを数える。SQLite で検査が遅延するのは
/// この形だけで、<c>NOT DEFERRABLE</c>（<c>NOT DEFERRABLE INITIALLY DEFERRED</c> を含む）や
/// <c>DEFERRABLE INITIALLY IMMEDIATE</c> は既定と同じ即時検査（消えても意味が変わらない）のため、数えると本当の喪失が埋もれる。
/// 同じ理由で、外部キーの参照アクション <c>SET DEFAULT</c> は列の <c>DEFAULT</c> として数えない（参照アクションは
/// 意味モデルが運ぶので再構築で失われない）。
/// </para>
/// <para>
/// なお <b>rowid 別名（暗黙の自動採番）は失われない</b>。SQLite では表制約形式の <c>PRIMARY KEY(x)</c> でも
/// <c>x INTEGER</c> なら rowid 別名になるため、明示 <c>AUTOINCREMENT</c> の有無だけが差になる。
/// そのため rowid 別名は検出対象に含めない（含めると常時警告になり、本当の喪失が埋もれる）。
/// </para>
/// <para>
/// <b>検出精度の割り切り</b>: 完全な SQL パーサーは持たず、<c>CREATE TABLE</c> の最外側の括弧の中と、
/// その後ろ（テーブルオプション）を分けて取り出し、文字列リテラル（<c>'...'</c>）・引用識別子
/// （<c>"..."</c> / <c>[...]</c> / <c>`...`</c>）・コメント（<c>--</c> / <c>/* */</c>）を取り除いてから
/// キーワードを語境界付き・大文字小文字非依存で探す。取り除いた跡には空白を 1 つ置く（隣り合う語がつながって
/// <c>DEFAULT'a'COLLATE</c> が 1 語に見え、検出を漏らさないため）。
/// 取り除いた後でも「テーブル名が DEFAULT を含む列の CHECK 式」のような入れ子の誤検出は理屈上あり得るが、
/// これは実行を止めない警告であり、過剰に出ても害が小さい側（見落としのほうが有害）に倒している。
/// </para>
/// </remarks>
public static class TableRebuildAttributeDetector
{
    /// <summary>括弧の中で探す列の属性（表示にそのまま使うため SQL の綴りで持つ）</summary>
    private static readonly (string Token, Regex Pattern)[] Keywords =
    [
        ("AUTOINCREMENT", Word("AUTOINCREMENT")),
        // 外部キーの参照アクション SET DEFAULT は列の DEFAULT ではない
        (
            "DEFAULT",
            new Regex(@"(?<!\bSET\s+)\bDEFAULT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        ),
        ("CHECK", Word("CHECK")),
        ("COLLATE", Word("COLLATE")),
        ("GENERATED", Word("GENERATED")),
    ];

    /// <summary>生成列の省略形（<c>col AS (expr)</c>）。<c>GENERATED</c> と同じ喪失なので同じトークンで報告する</summary>
    private static readonly Regex ShorthandGeneratedColumn = new(
        @"\bAS\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>括弧の中で探す制約の句（列の属性の後ろに並べて報告する）</summary>
    /// <remarks>
    /// 外部キーの遅延は検査が実際に遅延する <c>DEFERRABLE INITIALLY DEFERRED</c> だけを数える
    /// （クラスの remarks を参照）。報告は短い <c>DEFERRABLE</c> にする。
    /// </remarks>
    private static readonly (string Token, Regex Pattern)[] ClauseKeywords =
    [
        ("ON CONFLICT", Words("ON", "CONFLICT")),
        // NOT DEFERRABLE INITIALLY DEFERRED は遅延しない（即時検査）ので数えない
        (
            "DEFERRABLE",
            new Regex(
                @"(?<!\bNOT\s+)\bDEFERRABLE\s+INITIALLY\s+DEFERRED\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )
        ),
    ];

    /// <summary>最外の括弧の後ろで探すテーブルオプション</summary>
    private static readonly (string Token, Regex Pattern)[] TableOptionKeywords =
    [
        ("WITHOUT ROWID", Words("WITHOUT", "ROWID")),
        ("STRICT", Word("STRICT")),
    ];

    /// <summary>
    /// <c>CREATE TABLE</c> 文から、再構築で失われる属性のトークンを検出する。
    /// </summary>
    /// <param name="createTableSql">live の <c>CREATE TABLE</c> 文全文（<c>null</c> / 空なら検出なし）</param>
    /// <returns>
    /// 検出したトークン（<c>AUTOINCREMENT</c> 等）を重複なしで返す。並びは列の属性 → 制約の句 →
    /// テーブルオプションで、それぞれの中は定義順。無ければ空
    /// </returns>
    public static IReadOnlyList<string> Detect(string? createTableSql)
    {
        var (body, tail) = ExtractTableParts(createTableSql);

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

        foreach (var (token, pattern) in ClauseKeywords)
        {
            if (pattern.IsMatch(body))
            {
                found.Add(token);
            }
        }

        foreach (var (token, pattern) in TableOptionKeywords)
        {
            if (pattern.IsMatch(tail))
            {
                found.Add(token);
            }
        }

        return found;
    }

    /// <summary>語境界付き・大文字小文字非依存のキーワード検索パターンを作る</summary>
    private static Regex Word(string keyword) =>
        new($@"\b{keyword}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>空白で区切られた語の並びを、語境界付き・大文字小文字非依存で探すパターンを作る</summary>
    private static Regex Words(params string[] keywords) =>
        new(
            $@"\b{string.Join(@"\s+", keywords)}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

    /// <summary>
    /// <c>CREATE TABLE ... ( ... ) ...</c> を、最外側の括弧の中身（本体）とその後ろ（テーブルオプション）に分け、
    /// それぞれから文字列リテラル・引用識別子・コメントを取り除いた文字列を返す。
    /// </summary>
    /// <remarks>
    /// テーブル名側（<c>CREATE TABLE "default_values"</c> 等）を検査対象から外すのが目的。
    /// 括弧が見つからない（<c>CREATE TABLE x AS SELECT ...</c> 等）場合は両方とも空を返す＝検出なし。
    /// </remarks>
    private static (string Body, string Tail) ExtractTableParts(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (string.Empty, string.Empty);
        }

        var builder = new StringBuilder(sql.Length);
        var tail = new StringBuilder();
        var depth = 0;
        var bodyClosed = false;

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

                AppendSeparator();
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

                AppendSeparator();
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

                AppendSeparator();
                continue;
            }

            // 最外側の閉じ括弧より後ろはテーブルオプション（WITHOUT ROWID / STRICT）として別に集める
            if (bodyClosed)
            {
                tail.Append(c);
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

                // 最外側の閉じ括弧で本体は終わり（以降はテーブルオプション）
                if (depth == 0)
                {
                    bodyClosed = true;
                    continue;
                }
            }

            if (depth >= 1)
            {
                builder.Append(c);
            }
        }

        return (builder.ToString(), tail.ToString());

        // 取り除いた跡に空白を置く（いまの位置が本体かテーブルオプションかに合わせる）
        void AppendSeparator()
        {
            if (bodyClosed)
            {
                tail.Append(' ');
            }
            else if (depth >= 1)
            {
                builder.Append(' ');
            }
        }
    }
}
