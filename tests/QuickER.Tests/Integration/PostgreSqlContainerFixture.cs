using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using QuickER.PostgreSql;
using QuickER.Provider;
using Testcontainers.PostgreSql;

namespace QuickER.Tests.Integration;

/// <summary>
/// PostgreSQL の Testcontainers コンテナをコレクション内で 1 回だけ起動し共有するフィクスチャ。
/// </summary>
/// <remarks>
/// <para>
/// <c>postgres:16-alpine</c> を <see cref="InitializeAsync"/> で起動する。Docker 不在・起動失敗時は
/// 例外を握って <see cref="IsAvailable"/> を <c>false</c> にし、<see cref="UnavailableReason"/> に理由を保持する
/// （フィクスチャ自体は失敗させない）。各統合テストは冒頭で
/// <c>Assert.SkipUnless(fixture.IsAvailable, ...)</c> によりスキップする。
/// </para>
/// <para>
/// テスト間の独立性は、コンテナ（＝データベース）を使い回しつつ各テストの冒頭で
/// <see cref="ResetSchemaAsync"/>（<c>DROP SCHEMA public CASCADE; CREATE SCHEMA public;</c>）を実行し、
/// クリーンな <c>public</c> スキーマから始めることで確保する。
/// </para>
/// </remarks>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    /// <summary>共有する PostgreSQL コンテナ（Docker 不在時は起動されないため <c>null</c>）</summary>
    private PostgreSqlContainer? _container;

    /// <summary>コンテナが起動しテストを実行できるかどうか（<c>false</c> ならテストはスキップ）</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>利用不可の場合の理由（Docker 不在・起動失敗時のメッセージ）</summary>
    public string UnavailableReason { get; private set; } = string.Empty;

    /// <summary>コンテナへの ADO.NET 接続文字列（<see cref="IsAvailable"/> が <c>true</c> のときのみ有効）</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>コンテナを起動する。Docker 不在・起動失敗は握りつぶし <see cref="IsAvailable"/> を <c>false</c> にする</summary>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // Docker が無い・デーモンに接続できない等の場合はテストをスキップさせる
            IsAvailable = false;
            UnavailableReason =
                $"PostgreSQL コンテナを起動できませんでした（Docker 不在または起動失敗）: {ex.Message}";
        }
    }

    /// <summary>コンテナを破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary><c>public</c> スキーマを作り直し、各テストをクリーンな状態から始める</summary>
    /// <remarks>データベースは使い回し、スキーマのみ初期化する方式でテスト間の独立性を確保する</remarks>
    public async Task ResetSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DROP SCHEMA public CASCADE; CREATE SCHEMA public;",
            conn
        );
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>コンテナに対して開いた新しい接続を返す（呼び出し側で破棄する）</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>任意の SQL スクリプトをコンテナ上で実行するヘルパー（DDL のセットアップ用）</summary>
    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>コンテナの接続文字列を分解して共通接続設定 <see cref="DbConnectionSettings"/> を組み立てる</summary>
    /// <remarks>接続文字列ファクトリの実接続検証（D）で用いる</remarks>
    public DbConnectionSettings ToDbConnectionSettings()
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionString);
        return new DbConnectionSettings
        {
            Host = b.Host ?? "localhost",
            Port = b.Port,
            Database = b.Database ?? "postgres",
            UserId = b.Username ?? "postgres",
            Password = b.Password ?? "postgres",
            AuthMode = DbAuthMode.UsernamePassword,
        };
    }
}

/// <summary>PostgreSQL 統合テスト用のコレクション定義（コンテナをコレクション内で共有する）</summary>
[CollectionDefinition(Name)]
public sealed class PostgreSqlContainerCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
    /// <summary>コレクション名（各統合テストクラスの <c>[Collection]</c> で参照する）</summary>
    public const string Name = "PostgreSQL Integration";
}
