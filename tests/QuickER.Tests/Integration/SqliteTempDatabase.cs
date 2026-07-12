using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace QuickER.Tests.Integration;

/// <summary>
/// SQLite 統合テスト用の一時ファイル DB を扱うヘルパー（Testcontainers / Docker を一切使わない）。
/// </summary>
/// <remarks>
/// <para>
/// 一時ディレクトリ内に <c>.db</c> ファイルを作り、そのパスを保持する。破棄時に接続プールを解放して
/// ファイルとディレクトリを削除する。<c>using</c> スコープで使うことを想定する。
/// </para>
/// <para>
/// スキーマ準備は <c>Mode=ReadWriteCreate</c> の接続文字列（<see cref="ReadWriteCreateConnectionString"/>）で
/// 直接組み立てて実行する。取込・接続検証はプロダクションの取込専用（<c>Mode=ReadOnly</c>）経路を用いる。
/// </para>
/// </remarks>
internal sealed class SqliteTempDatabase : IDisposable
{
    private readonly string _directory;

    private SqliteTempDatabase(string directory, string filePath)
    {
        _directory = directory;
        FilePath = filePath;
    }

    /// <summary>一時 DB ファイルの絶対パス</summary>
    public string FilePath { get; }

    /// <summary>
    /// 一時ディレクトリを作り、その中に空でない一意な <c>.db</c> パスを用意する
    /// （ファイル自体はまだ作らない＝ReadOnly の空 DB 自動生成ガードを検証できるようにするため）。
    /// </summary>
    public static SqliteTempDatabase Create()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "quicker-sqlite-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "test.db");
        return new SqliteTempDatabase(dir, filePath);
    }

    /// <summary>スキーマ準備用の書き込み可能（存在しなければ作成）接続文字列</summary>
    public string ReadWriteCreateConnectionString =>
        new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString;

    /// <summary>取込専用の読み取り専用接続文字列（プロダクションのファクトリと同じモード）</summary>
    public string ReadOnlyConnectionString =>
        new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString;

    /// <summary>
    /// DDL スクリプトを一時 DB に適用する。SQLite は 1 回の <c>ExecuteNonQuery</c> で複数文を実行できないため、
    /// ステートメントごとに分割して順に実行する（外部キー制約を含むため <c>foreign_keys</c> は明示 ON）。
    /// </summary>
    public async Task ApplyDdlAsync(string ddl, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(ReadWriteCreateConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // 生成 DDL の FK 制約を有効化した状態で作成する
        await using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var statement in SplitStatements(ddl))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>取込専用（ReadOnly）接続を開いて返す（呼び出し側で破棄する）</summary>
    public async Task<SqliteConnection> OpenReadOnlyConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(ReadOnlyConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>
    /// DDL スクリプトを実行可能なステートメント単位に分割する。
    /// <c>--</c> 行コメントを除去し、文字列リテラル内のセミコロンを無視してセミコロンで区切る。
    /// </summary>
    /// <remarks>
    /// 生成 DDL は識別子を二重引用符、リテラルを含まない構造のため単純な分割で足りるが、
    /// 文字列リテラル（<c>'...'</c>）内のセミコロン混入に備えてクォート状態を追跡する。
    /// </remarks>
    private static IEnumerable<string> SplitStatements(string ddl)
    {
        var current = new StringBuilder();
        var inSingleQuote = false;

        // 行コメントを除去したうえで 1 文字ずつ走査し、クォート外のセミコロンで区切る
        foreach (var rawLine in ddl.Split('\n'))
        {
            var line = StripLineComment(rawLine);

            foreach (var ch in line)
            {
                if (ch == '\'')
                {
                    inSingleQuote = !inSingleQuote;
                }

                if (ch == ';' && !inSingleQuote)
                {
                    var stmt = current.ToString().Trim();

                    if (stmt.Length > 0)
                    {
                        yield return stmt;
                    }

                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            current.Append('\n');
        }

        var tail = current.ToString().Trim();

        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    /// <summary>クォート外の <c>--</c> 以降を行コメントとして除去する</summary>
    private static string StripLineComment(string line)
    {
        var inSingleQuote = false;

        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '\'')
            {
                inSingleQuote = !inSingleQuote;
            }

            if (!inSingleQuote && line[i] == '-' && line[i + 1] == '-')
            {
                return line.Substring(0, i);
            }
        }

        return line;
    }

    /// <summary>接続プールを解放し、一時ファイル・ディレクトリを削除する</summary>
    public void Dispose()
    {
        // Microsoft.Data.Sqlite はプールでファイルを掴むため、削除前にプールを解放する。
        // ClearAllPools はプロセス全体のプールを破棄し、並列実行中の他テスト（別の一時 DB）が
        // 使用中の native ハンドルまで破棄して稀に ObjectDisposedException を誘発するため、
        // この DB の接続文字列（ReadWriteCreate / ReadOnly の 2 種）に限定して解放する
        using (var rw = new SqliteConnection(ReadWriteCreateConnectionString))
        {
            SqliteConnection.ClearPool(rw);
        }

        using (var ro = new SqliteConnection(ReadOnlyConnectionString))
        {
            SqliteConnection.ClearPool(ro);
        }

        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 後始末の失敗はテスト結果に影響させない（一時ディレクトリは OS が最終的に回収する）
        }
    }
}
