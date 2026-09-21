using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using QuickER.Provider;

namespace QuickER.Provider.SqlServer;

/// <summary>生成済みの T-SQL スクリプトを単一トランザクションで SQL Server に対し実行する</summary>
/// <remarks>sqlcmd の慣習に従い、行頭の <c>GO</c> でバッチ分割する</remarks>
public sealed class SqlServerSchemaSyncExecutor : ISchemaSyncExecutor
{
    /// <summary>スクリプトを単一トランザクション内で実行する（途中で例外発生時は ROLLBACK する）</summary>
    /// <remarks>全バッチ成功時のみ COMMIT し、原子性を保証する</remarks>
    public async Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    )
    {
        var result = new SchemaSyncResult();
        var batches = SplitBatches(script);

        if (batches.Count == 0)
        {
            result.Committed = true;
            return result;
        }

        await using var conn = new SqlConnection(SqlServerConnectionStringFactory.Build(settings));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tran = (SqlTransaction)
            await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            for (var i = 0; i < batches.Count; i++)
            {
                var sql = batches[i];
                await using var cmd = DbCommands.Create(
                    conn,
                    sql,
                    settings.CommandTimeoutSeconds,
                    tran
                );
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                result.Batches.Add(new SchemaSyncBatchResult(i + 1, sql, true, null));
            }

            await tran.CommitAsync(ct).ConfigureAwait(false);
            result.Committed = true;
        }
        catch (Exception ex)
        {
            // ロールバック済みである旨は方言差があるため、表示側の見出しではなくエラー本文へ含める
            result.Error = string.Format(
                QuickER.Provider.Resources.Strings.Sync_Error_RolledBack,
                ex.Message
            );

            // 後始末の作法（完了済みなら no-op・失敗は握りつぶし・キャンセル不可）は共有ヘルパーに集約
            await DbTransactions.RollbackQuietlyAsync(tran).ConfigureAwait(false);

            result.Batches.Add(
                new SchemaSyncBatchResult(result.Batches.Count + 1, "", false, ex.Message)
            );
        }

        return result;
    }

    /// <summary>バッチ区切り（<c>GO</c> のみの行・前後空白と大文字小文字を許容）の判定パターン</summary>
    private static readonly Regex GoPattern = new(@"^\s*GO\s*$", RegexOptions.IgnoreCase);

    /// <summary>スクリプトを行頭の <c>GO</c> で分割する（大文字小文字を無視し、前後空白を許容する）</summary>
    /// <remarks>
    /// <para>
    /// 文字列リテラル（<c>'…''…'</c>）・引用識別子（<c>"…"</c> / <c>[…]]…]</c>）・ブロックコメント（<c>/* … */</c>）の
    /// <b>内側</b>にある <c>GO</c> だけの行は区切りとして扱わない。説明文は改行を含められるため、追跡しないと
    /// 拡張プロパティの値に紛れた <c>GO</c> 行でバッチが割れて構文エラーになる。
    /// </para>
    /// <para>
    /// リテラルとブロックコメントは行をまたぐため、判定は「その行の先頭時点で外側に居るか」で行う
    /// （<c>GO</c> だけの行はそれ自体が状態を変えないので、この判定で必要十分）。
    /// </para>
    /// <para>
    /// SSMS の拡張構文 <c>GO 5</c>（バッチの反復回数）・<c>GO;</c> は区切りとして認識しない
    /// （QuickER 自身はどちらも出力しないため実害経路はない）。
    /// </para>
    /// </remarks>
    public static List<string> SplitBatches(string script)
    {
        var list = new List<string>();

        if (string.IsNullOrWhiteSpace(script))
        {
            return list;
        }

        var lines = script.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        // 開いているリテラル・引用識別子の終端文字（null＝外側）と、ブロックコメントの入れ子段数
        char? closing = null;
        var blockCommentDepth = 0;

        foreach (var line in lines)
        {
            if (closing is null && blockCommentDepth == 0 && GoPattern.IsMatch(line))
            {
                var batch = current.ToString().Trim();

                if (batch.Length > 0)
                {
                    list.Add(batch);
                }

                current.Clear();
                continue;
            }

            ScanLine(line, ref closing, ref blockCommentDepth);
            current.AppendLine(line);
        }

        var last = current.ToString().Trim();

        if (last.Length > 0)
        {
            list.Add(last);
        }

        return list;
    }

    /// <summary>1 行を走査してリテラル・引用識別子・ブロックコメントの開閉状態を更新する</summary>
    /// <remarks>
    /// T-SQL のブロックコメントは入れ子にできるため段数で数える。終端文字の二重化（<c>''</c> / <c>""</c> / <c>]]</c>）は
    /// エスケープなので閉じたとみなさない。行コメント（<c>--</c>）は行末までを読み飛ばす＝そこに現れる引用符で
    /// リテラルを開かない。
    /// </remarks>
    private static void ScanLine(string line, ref char? closing, ref int blockCommentDepth)
    {
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            if (blockCommentDepth > 0)
            {
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    blockCommentDepth++;
                    i += 2;
                    continue;
                }

                if (c == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    blockCommentDepth--;
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
                blockCommentDepth = 1;
                i += 2;
                continue;
            }

            if (c is '\'' or '"')
            {
                closing = c;
                i++;
                continue;
            }

            if (c == '[')
            {
                closing = ']';
                i++;
                continue;
            }

            i++;
        }
    }
}
