using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using QuickER.Provider;
using Testcontainers.MsSql;

namespace QuickER.Tests.Integration;

/// <summary>
/// SQL Server の Testcontainers コンテナをコレクション内で 1 回だけ起動し共有するフィクスチャ。
/// </summary>
/// <remarks>
/// <para>
/// <c>mcr.microsoft.com/mssql/server:2022-latest</c> を <see cref="InitializeAsync"/> で起動する。
/// Docker 不在・起動失敗時は例外を握って <see cref="IsAvailable"/> を <c>false</c> にし、
/// <see cref="UnavailableReason"/> に理由を保持する（フィクスチャ自体は失敗させない）。
/// 各統合テストは冒頭で <c>Assert.SkipUnless(fixture.IsAvailable, ...)</c> によりスキップする。
/// </para>
/// <para>
/// テスト間の独立性は、コンテナ（＝サーバー）を使い回しつつ各テストの冒頭で
/// <see cref="ResetSchemaAsync"/>（全 FK 制約を DROP → 全テーブルを DROP）を実行し、
/// クリーンな状態から始めることで確保する。
/// </para>
/// <para>
/// 生成された自作 ORM ランタイムは <c>Microsoft.Data.SqlClient</c> を使うため、
/// このフィクスチャも同ドライバで接続を張る。
/// </para>
/// </remarks>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ（Docker 不在時は起動されないため <c>null</c>）</summary>
    private MsSqlContainer? _container;

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
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync().ConfigureAwait(false);
            // 開発用の自己署名証明書のため、暗号化は要求しつつサーバー証明書を信頼する
            var b = new SqlConnectionStringBuilder(_container.GetConnectionString())
            {
                TrustServerCertificate = true,
            };
            ConnectionString = b.ConnectionString;
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // Docker が無い・デーモンに接続できない等の場合はテストをスキップさせる
            IsAvailable = false;
            UnavailableReason =
                $"SQL Server コンテナを起動できませんでした（Docker 不在または起動失敗）: {ex.Message}";
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

    /// <summary>対象データベースの全 FK 制約と全テーブルを DROP し、各テストをクリーンな状態から始める</summary>
    /// <remarks>データベースは使い回し、FK を先に落としてから全テーブルを削除する方式で独立性を確保する</remarks>
    public async Task ResetSchemaAsync(CancellationToken ct = default)
    {
        // 先に全 FK 制約を落とし、その後で全ユーザーテーブルを削除する（削除順の依存を避ける）
        const string sql = """
            DECLARE @sql NVARCHAR(MAX) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
                + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
            FROM sys.foreign_keys fk
            JOIN sys.tables t ON fk.parent_object_id = t.object_id;
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';'
            FROM sys.tables;
            IF LEN(@sql) > 0 EXEC sp_executesql @sql;
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>コンテナに対して開いた新しい接続を返す（呼び出し側で破棄する）</summary>
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>任意の SQL スクリプトをコンテナ上で実行するヘルパー（DDL のセットアップ用）</summary>
    /// <remarks>GO 区切りは扱わないため、単一バッチとして実行できる DDL を渡すこと</remarks>
    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>コンテナの接続文字列を分解して共通接続設定 <see cref="DbConnectionSettings"/> を組み立てる</summary>
    public DbConnectionSettings ToDbConnectionSettings()
    {
        var b = new SqlConnectionStringBuilder(ConnectionString);
        return new DbConnectionSettings
        {
            Host = string.IsNullOrEmpty(b.DataSource) ? "localhost" : b.DataSource,
            Database = string.IsNullOrEmpty(b.InitialCatalog) ? "master" : b.InitialCatalog,
            UserId = b.UserID,
            Password = b.Password,
            AuthMode = DbAuthMode.UsernamePassword,
        };
    }
}

/// <summary>SQL Server 統合テスト用のコレクション定義（コンテナをコレクション内で共有する）</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerContainerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    /// <summary>コレクション名（各統合テストクラスの <c>[Collection]</c> で参照する）</summary>
    public const string Name = "SQL Server Integration";
}
