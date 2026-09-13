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
/// <c>AllowUserVariables=true</c> を付与する。あわせて、スクリプトのリテラル組み立てが前提とする
/// エスケープ規則へセッションを揃えるため <see cref="ClearNoBackslashEscapes"/> を先に実行する。
/// </para>
/// </remarks>
public sealed class MySqlSchemaSyncExecutor : ISchemaSyncExecutor
{
    /// <summary>
    /// スクリプト実行前にセッションの <c>sql_mode</c> から <c>NO_BACKSLASH_ESCAPES</c> を外す文。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同期スクリプトの文字列リテラルは <c>MySqlIdentifier.EscapeStringLiteral</c> が組み立てており、
    /// MySQL 既定の「バックスラッシュもエスケープ文字」という規則を前提に <c>\</c> を <c>\\</c> へ
    /// 二重化している。<c>NO_BACKSLASH_ESCAPES</c> が立ったセッションではこの前提が崩れ、
    /// 説明に含まれる <c>\</c> が二重のまま格納されて無言で化ける（実測: <c>a\b</c> → <c>a\\\\b</c>）。
    /// <c>'</c> を含む式を持つ生成列では、ライブ再構成した定義が構文エラーになりスクリプトが
    /// 途中で止まる（DDL は暗黙コミットのため部分適用になる）。
    /// </para>
    /// <para>
    /// 接続は QuickER が開いて閉じるものなので、<b>セッション限定</b>で外す（グローバルには触れない）。
    /// <c>REPLACE</c> が残す空要素や連続カンマは MySQL 自身が読み飛ばすため整形は不要
    /// （実測: <c>'A,NO_BACKSLASH_ESCAPES,B'</c> → <c>'A,,B'</c> が <c>'A,B'</c> として受理される）。
    /// </para>
    /// </remarks>
    private const string ClearNoBackslashEscapes =
        "SET SESSION sql_mode = REPLACE(@@SESSION.sql_mode, 'NO_BACKSLASH_ESCAPES', '');";

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

        // スクリプトのリテラル組み立てが前提にしている規則へセッションを揃える（接続を開くのと同じ扱いで、
        // ここで失敗したらスクリプトは 1 文も実行していない＝そのまま呼び出し元へ投げる）
        await using (
            var sqlMode = DbCommands.Create(
                conn,
                ClearNoBackslashEscapes,
                settings.CommandTimeoutSeconds
            )
        )
        {
            await sqlMode.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var index = 0;

        foreach (var statement in statements)
        {
            index++;

            try
            {
                await using var cmd = DbCommands.Create(
                    conn,
                    statement,
                    settings.CommandTimeoutSeconds
                );
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
