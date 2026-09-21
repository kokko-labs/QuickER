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
/// スクリプトを <see cref="OracleSyncScriptBuilder"/> の規約に従い「/」のみの行で分割し、1 文ずつ順次実行する。
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
    /// <summary>スクリプトを「/」区切りで分割し、1 文ずつ順次実行する</summary>
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

    /// <summary>スクリプトを「/」のみの行で分割し、各文を実行可能な形へ整える</summary>
    /// <remarks>
    /// <para>
    /// 通常文は末尾の <c>;</c> を除去する。PL/SQL ブロック（<c>DECLARE</c> / <c>BEGIN</c> 開始）は
    /// 末尾の <c>;</c> を保持する。コメントのみ・空の文は実行対象から除外する。
    /// </para>
    /// <para>
    /// 文字列リテラル（<c>'…''…'</c>）・引用識別子（<c>"…"</c>）・ブロックコメント（<c>/* … */</c>）の
    /// <b>内側</b>にある「/」だけの行は区切りとして扱わない。説明文は改行を含められるため、追跡しないと
    /// <c>COMMENT ON</c> の値に紛れた「/」行で文が割れ、Oracle は暗黙コミットのため部分適用になる。
    /// </para>
    /// <para>
    /// リテラルとブロックコメントは行をまたぐため、判定は「その行の先頭時点で外側に居るか」で行う
    /// （「/」だけの行はそれ自体が状態を変えないので、この判定で必要十分）。
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

        foreach (var rawLine in script.Replace("\r\n", "\n").Split('\n'))
        {
            // 「/」のみ（前後空白許容）の行を文の区切りとする（リテラル・コメントの外側に限る）
            if (closing is null && !inBlockComment && rawLine.Trim() == "/")
            {
                AddIfMeaningful(statements, current);
                current.Clear();
                continue;
            }

            ScanLine(rawLine, ref closing, ref inBlockComment);
            current.Add(rawLine);
        }

        AddIfMeaningful(statements, current);
        return statements;
    }

    /// <summary>1 行を走査してリテラル・引用識別子・ブロックコメントの開閉状態を更新する</summary>
    /// <remarks>
    /// Oracle のブロックコメントは入れ子にできないため真偽値で持つ。終端文字の二重化（<c>''</c> / <c>""</c>）は
    /// エスケープなので閉じたとみなさない。行コメント（<c>--</c>）は行末までを読み飛ばす＝そこに現れる引用符で
    /// リテラルを開かない。
    /// </remarks>
    private static void ScanLine(string line, ref char? closing, ref bool inBlockComment)
    {
        var i = 0;

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
                }

                i++;
                continue;
            }

            // 行コメントは行末まで状態を変えない
            if (c == '-' && i + 1 < line.Length && line[i + 1] == '-')
            {
                return;
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

            i++;
        }
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

    /// <summary>文が DECLARE / BEGIN で始まる PL/SQL 無名ブロックかどうかを判定する</summary>
    private static bool IsPlSqlBlock(string block)
    {
        var head = block.TrimStart();
        return head.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase);
    }

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
