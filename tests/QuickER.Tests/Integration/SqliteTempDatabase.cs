using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Sqlite;

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

    /// <summary>同期実行用の書き込み可能（新規作成はしない）接続文字列（同期 Executor と同じモード）</summary>
    public string ReadWriteConnectionString =>
        new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ConnectionString;

    /// <summary>DDL スクリプト（複数文可）を一時 DB に適用する</summary>
    /// <remarks>
    /// Microsoft.Data.Sqlite は 1 コマンドでセミコロン区切りの複数文を実行できる
    /// （プロダクションの <c>SqliteSchemaSyncExecutor</c> / 生成物の <c>SqliteSchemaBootstrap</c> と同じ前提）。
    /// テーブル作成は FK 強制の有無に影響されないため PRAGMA は送らない。
    /// </remarks>
    public async Task ApplyDdlAsync(string ddl, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(ReadWriteCreateConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>図から SQLite 方言の DDL を生成して一時 DB に適用する（テスト定番の 2 行を 1 行にまとめる糖衣）</summary>
    public Task ApplyDdlAsync(ErDiagram diagram, CancellationToken ct = default) =>
        ApplyDdlAsync(new SqliteDdlGenerator().Build(diagram), ct);

    /// <summary>
    /// 一時 DB のユーザーテーブルをすべて DROP し、各テストをクリーンな状態から始める
    /// （SQL Server 側の <c>SqlServerContainerFixture.ResetSchemaAsync</c> と対称の汎用実装）。
    /// </summary>
    /// <remarks>
    /// <c>sqlite_master</c> から実テーブルを列挙して落とすため、テスト側がテーブル名や削除順を持たなくてよい。
    /// FK 依存順を気にせず落とせるよう <c>PRAGMA foreign_keys = OFF</c> の下で実行する
    /// （PRAGMA は接続単位＝この接続を閉じれば元に戻る）。DB ファイルが未作成なら何もしない。
    /// </remarks>
    public async Task ResetSchemaAsync(CancellationToken ct = default)
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        await using var conn = new SqliteConnection(ReadWriteCreateConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // sqlite_ で始まる内部テーブル（sqlite_sequence 等）は DROP できないため除外する
        var tables = new List<string>();

        await using (var select = conn.CreateCommand())
        {
            select.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (tables.Count == 0)
        {
            return;
        }

        var script = new StringBuilder("PRAGMA foreign_keys = OFF;\n");

        foreach (var table in tables)
        {
            script
                .Append("DROP TABLE IF EXISTS \"")
                .Append(table.Replace("\"", "\"\""))
                .Append("\";\n");
        }

        await using var drop = conn.CreateCommand();
        drop.CommandText = script.ToString();
        await drop.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>取込専用（ReadOnly）接続を開いて返す（呼び出し側で破棄する）</summary>
    public async Task<SqliteConnection> OpenReadOnlyConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(ReadOnlyConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
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

        using (var rw = new SqliteConnection(ReadWriteConnectionString))
        {
            SqliteConnection.ClearPool(rw);
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
