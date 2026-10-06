using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using QuickER.Provider;

namespace QuickER.Provider.Oracle;

/// <summary>生成済みの DDL スクリプトを Oracle に対し実行する</summary>
/// <remarks>
/// <para>
/// ODP.NET（Oracle.ManagedDataAccess.Core）は 1 コマンドで複数文を実行できないため、
/// スクリプトを <see cref="OracleSyncScriptBuilder"/> の規約（SQL*Plus と同じ＝通常文は行末の <c>;</c>、PL/SQL ブロックは
/// 「/」のみの行で終わる）に従って分割し、1 文ずつ順次実行する。
/// </para>
/// <para>
/// 通常文は末尾の <c>;</c> を除去してから実行する（ODP.NET は素の SQL の末尾セミコロンを拒否する）。
/// 一方、<c>DECLARE</c> / <c>BEGIN</c> で始まる PL/SQL 無名ブロックは末尾の <c>;</c>（<c>END;</c>）を保持したまま実行する。
/// </para>
/// <para>
/// Oracle も DDL は暗黙コミットされるため、途中で失敗しても以前に成功した文はロールバックされない。
/// このためエラー時は <see cref="SchemaSyncResult.Committed"/> を <c>false</c> にしつつ、
/// 部分適用が発生している可能性をメッセージで正直に報告する（MySQL 版と同方針）。原子性は保証しない。
/// </para>
/// </remarks>
public sealed class OracleSchemaSyncExecutor : ISchemaSyncExecutor
{
    /// <summary>スクリプトを文に分割し（<see cref="SplitStatements"/>）、1 文ずつ順次実行する</summary>
    public async Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    )
    {
        var result = new SchemaSyncResult();

        if (string.IsNullOrWhiteSpace(script))
        {
            result.Committed = true;
            return result;
        }

        var statements = SplitStatements(script);

        if (statements.Count == 0)
        {
            result.Committed = true;
            return result;
        }

        await using var conn = new OracleConnection(OracleConnectionStringFactory.Build(settings));
        await conn.OpenAsync(ct).ConfigureAwait(false);

        for (var i = 0; i < statements.Count; i++)
        {
            var sql = statements[i];

            try
            {
                await using var cmd = DbCommands.Create(conn, sql, settings.CommandTimeoutSeconds);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                result.Batches.Add(new SchemaSyncBatchResult(i + 1, sql, true, null));
            }
            catch (Exception ex)
            {
                result.Batches.Add(new SchemaSyncBatchResult(i + 1, sql, false, ex.Message));
                // DDL は暗黙コミットのため、直前までに成功した文は取り消せない。
                // 部分適用の可能性を明示し、以降の文は実行を打ち切る。
                result.Error = string.Format(
                    QuickER.Provider.Resources.Strings.Sync_Error_OracleStatementFailed,
                    ex.Message,
                    i
                );
                return result;
            }
        }

        result.Committed = true;
        return result;
    }

    /// <summary>スクリプトを SQL*Plus と同じ規則で文に分割し、各文を実行可能な形へ整える</summary>
    /// <remarks>
    /// <para>
    /// 区切りは SQL*Plus と同じ 2 つ。通常の文は<b>行末の <c>;</c></b>で終わり、<c>DECLARE</c> / <c>BEGIN</c> で始まる
    /// PL/SQL 無名ブロックは<b>「/」のみの行</b>で終わる（ブロックの中の行末の <c>;</c> では区切らない）。
    /// <see cref="OracleSyncScriptBuilder"/> はこの規約で書く（「/」行はブロックの後ろだけ）。
    /// </para>
    /// <para>
    /// 溜めている文が無いときの「/」行は何もしない。SQL*Plus はそこで直前の文をもう一度実行するが、ここでは
    /// 通常の文の後ろにも「/」行を置いたスクリプトを流しても、各文は 1 回だけ実行される。
    /// </para>
    /// <para>
    /// 通常文は末尾の <c>;</c> を除去する。PL/SQL ブロックは末尾の <c>;</c>（<c>END;</c>）を保持する。
    /// コメントのみ・空の文は実行対象から除外する。
    /// </para>
    /// <para>
    /// 文字列リテラル（<c>'…''…'</c>）・引用識別子（<c>"…"</c>）・ブロックコメント（<c>/* … */</c>）の
    /// <b>内側</b>にある「/」だけの行と行末の <c>;</c> は区切りとして扱わない。行コメント（<c>--</c>）の中の <c>;</c> も
    /// 区切りにしない。リテラルとブロックコメントは行をまたぐため、判定は行の走査が終わった時点の状態で行う。
    /// </para>
    /// <para>
    /// Oracle の代替引用 <c>q'[…]'</c> は追跡しない（QuickER 自身は出力しないため実害経路はない）。
    /// </para>
    /// </remarks>
    public static List<string> SplitStatements(string script)
    {
        var statements = new List<string>();
        var current = new List<string>();
        // 開いているリテラル・引用識別子の終端文字（null＝外側）と、ブロックコメントの内外
        char? closing = null;
        var inBlockComment = false;
        // 溜めている文が PL/SQL ブロックか（最初のコード行で決まる。null＝まだコード行が無い）
        bool? currentIsBlock = null;

        foreach (var rawLine in script.Replace("\r\n", "\n").Split('\n'))
        {
            var outsideAtStart = closing is null && !inBlockComment;

            // 「/」のみ（前後空白許容）の行は文の区切り（リテラル・コメントの外側に限る）。
            // 溜めている文が無ければ何もしない（通常文の後ろの「/」で同じ文を二重に実行しない）
            if (outsideAtStart && rawLine.Trim() == "/")
            {
                AddIfMeaningful(statements, current);
                current.Clear();
                currentIsBlock = null;
                continue;
            }

            var trimmed = rawLine.Trim();

            if (
                currentIsBlock is null
                && outsideAtStart
                && trimmed.Length > 0
                && !trimmed.StartsWith("--", StringComparison.Ordinal)
            )
            {
                currentIsBlock = IsPlSqlBlock(trimmed);
            }

            var endsWithSemicolon = ScanLine(rawLine, ref closing, ref inBlockComment);
            current.Add(rawLine);

            // 通常の文は、リテラル・コメントの外で行末が ; の行で終わる（SQL*Plus と同じ）
            if (currentIsBlock == false && endsWithSemicolon)
            {
                AddIfMeaningful(statements, current);
                current.Clear();
                currentIsBlock = null;
            }
        }

        AddIfMeaningful(statements, current);
        return statements;
    }

    /// <summary>1 行を走査してリテラル・引用識別子・ブロックコメントの開閉状態を更新する</summary>
    /// <returns>行の終わりでリテラル・コメントの外にいて、最後のコードの文字が <c>;</c> なら <c>true</c></returns>
    /// <remarks>
    /// Oracle のブロックコメントは入れ子にできないため真偽値で持つ。終端文字の二重化（<c>''</c> / <c>""</c>）は
    /// エスケープなので閉じたとみなさない。行コメント（<c>--</c>）は行末までを読み飛ばす＝そこに現れる引用符で
    /// リテラルを開かず、そこに現れる <c>;</c> も文の終わりとみなさない。
    /// </remarks>
    private static bool ScanLine(string line, ref char? closing, ref bool inBlockComment)
    {
        var i = 0;
        // 外側で最後に見たコードの文字（空白・コメントは数えない）
        char? last = null;

        while (i < line.Length)
        {
            var c = line[i];

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    inBlockComment = false;
                    i += 2;
                    continue;
                }

                i++;
                continue;
            }

            if (closing is not null)
            {
                // 終端文字の二重化はエスケープ（リテラルは閉じない）
                if (c == closing && i + 1 < line.Length && line[i + 1] == c)
                {
                    i += 2;
                    continue;
                }

                if (c == closing)
                {
                    closing = null;
                    last = c;
                }

                i++;
                continue;
            }

            // 行コメントは行末まで状態を変えない
            if (c == '-' && i + 1 < line.Length && line[i + 1] == '-')
            {
                break;
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                inBlockComment = true;
                i += 2;
                continue;
            }

            if (c is '\'' or '"')
            {
                closing = c;
                i++;
                continue;
            }

            if (!char.IsWhiteSpace(c))
            {
                last = c;
            }

            i++;
        }

        return closing is null && !inBlockComment && last == ';';
    }

    /// <summary>蓄積した行を 1 文として整形し、意味がある場合のみ追加する</summary>
    private static void AddIfMeaningful(List<string> statements, List<string> lines)
    {
        var block = string.Join("\n", lines).Trim();

        if (block.Length == 0)
        {
            return;
        }

        // コメント行のみで構成される文（-- で始まる行だけ）は実行しない
        if (IsCommentOnly(block))
        {
            return;
        }

        // PL/SQL 無名ブロックは末尾 ; を保持、通常文は末尾 ; を除去する
        if (IsPlSqlBlock(block))
        {
            statements.Add(block);
        }
        else
        {
            statements.Add(block.TrimEnd().TrimEnd(';').TrimEnd());
        }
    }

    /// <summary>文（またはその最初のコード行）が DECLARE / BEGIN で始まる PL/SQL 無名ブロックかどうかを判定する</summary>
    /// <remarks>先頭の行コメント・空行は読み飛ばす。<see cref="OracleSyncScriptBuilder"/> が「/」行を置くかの判定にも使う</remarks>
    public static bool IsPlSqlBlock(string block)
    {
        foreach (var line in block.Replace("\r\n", "\n").Split('\n'))
        {
            var head = line.Trim();

            if (head.Length == 0 || head.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            return StartsWithWord(head, "DECLARE") || StartsWithWord(head, "BEGIN");
        }

        return false;
    }

    /// <summary>行が指定の語（大文字小文字を区別しない）で始まり、その後ろが語の続きでないか</summary>
    private static bool StartsWithWord(string line, string word) =>
        line.StartsWith(word, StringComparison.OrdinalIgnoreCase)
        && (
            line.Length == word.Length
            || !(char.IsLetterOrDigit(line[word.Length]) || line[word.Length] == '_')
        );

    /// <summary>文がコメント行（-- 始まり）と空行のみで構成されているかどうかを判定する</summary>
    private static bool IsCommentOnly(string block)
    {
        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
