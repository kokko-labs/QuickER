using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>生成済みの DDL スクリプトを MySQL に対し文単位で順次実行する</summary>
/// <remarks>
/// <para>
/// <b>重要な制約</b>: MySQL の DDL 文（CREATE / ALTER / DROP TABLE 等）は暗黙コミットされるため、
/// スクリプト全体を単一トランザクションでロールバックすることはできない。トランザクションで包んでも
/// DDL 文ごとに確定してしまう。したがって本 Executor はロールバックによる原子性を保証しない。
/// </para>
/// <para>
/// 設計方針: 文を <c>;</c> 終端で分割し順次実行する。途中でエラーが起きた場合は、
/// <see cref="SchemaSyncResult.Committed"/> を <c>false</c> にし、
/// 「どこまで適用されたか（部分適用の可能性）」を <see cref="SchemaSyncResult.Error"/> に含めて正直に報告する。
/// 各文の実行結果は <see cref="SchemaSyncResult.Batches"/> に記録する。
/// </para>
/// <para>
/// プリペアド動的 SQL（<c>PREPARE</c> / <c>SET @fk = ...</c>）を扱うため、接続文字列には
/// <c>AllowUserVariables=true</c> を付与する。
/// </para>
/// </remarks>
public sealed class MySqlSchemaSyncExecutor : ISchemaSyncExecutor
{
    /// <summary>スクリプトを文単位で順次実行する（DDL は暗黙コミットのためロールバック不可）</summary>
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

        // @fk などのユーザー変数を使うプリペアド動的 SQL のため AllowUserVariables=true を付与する
        await using var conn = new MySqlConnection(
            MySqlConnectionStringFactory.Build(settings, true)
        );
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var index = 0;

        foreach (var statement in statements)
        {
            index++;

            try
            {
                await using var cmd = new MySqlCommand(statement, conn);
                cmd.CommandTimeout = 60;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                result.Batches.Add(new SchemaSyncBatchResult(index, statement, true, null));
            }
            catch (Exception ex)
            {
                // DDL は暗黙コミットのためここまでに実行済みの文は取り消せない（部分適用の可能性）
                var applied = index - 1;
                result.Error = string.Format(
                    QuickER.Provider.Resources.Strings.Sync_Error_MySqlStatementFailed,
                    index,
                    applied,
                    ex.Message
                );
                result.Batches.Add(new SchemaSyncBatchResult(index, statement, false, ex.Message));
                result.Committed = false;
                return result;
            }
        }

        result.Committed = true;
        return result;
    }

    /// <summary>
    /// スクリプトを <c>;</c> 終端で文へ分割する。文字列リテラル（<c>'...'</c> / <c>"..."</c> / <c>`...`</c>）内および
    /// 行コメント（<c>--</c>）内の <c>;</c> は区切りとして扱わない単純パーサ。
    /// </summary>
    internal static List<string> SplitStatements(string script)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var i = 0;

        while (i < script.Length)
        {
            var c = script[i];

            if (quote is not null)
            {
                current.Append(c);

                // 文字列リテラル内: バックスラッシュエスケープをスキップ（バッククォート内は除く）
                if (c == '\\' && quote != '`' && i + 1 < script.Length)
                {
                    current.Append(script[i + 1]);
                    i += 2;
                    continue;
                }

                // 同じクォートで閉じる（'' / "" / `` の二重化は次の反復で再度開く扱いになり実害なし）
                if (c == quote)
                {
                    quote = null;
                }

                i++;
                continue;
            }

            // 行コメント（-- ...）は行末まで読み飛ばす
            if (c == '-' && i + 1 < script.Length && script[i + 1] == '-')
            {
                while (i < script.Length && script[i] != '\n')
                {
                    current.Append(script[i]);
                    i++;
                }

                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
                current.Append(c);
                i++;
                continue;
            }

            if (c == ';')
            {
                AddStatement(statements, current);
                current.Clear();
                i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        AddStatement(statements, current);
        return statements;
    }

    /// <summary>空白・コメントのみでない文を確定してリストへ追加する</summary>
    private static void AddStatement(List<string> statements, StringBuilder current)
    {
        var text = current.ToString().Trim();

        if (text.Length == 0)
        {
            return;
        }

        // コメント行のみの塊は実行対象から除外する
        var hasStatement = false;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                hasStatement = true;
                break;
            }
        }

        if (hasStatement)
        {
            statements.Add(text);
        }
    }
}
