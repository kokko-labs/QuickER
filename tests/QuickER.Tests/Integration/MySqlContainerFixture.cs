using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using QuickER.Provider;
using Testcontainers.MySql;

namespace QuickER.Tests.Integration;

/// <summary>
/// MySQL の Testcontainers コンテナをコレクション内で 1 回だけ起動し共有するフィクスチャ。
/// </summary>
/// <remarks>
/// <para>
/// <c>mysql:8.4</c> を <see cref="InitializeAsync"/> で起動する。Docker 不在・起動失敗時は
/// 例外を握って <see cref="IsAvailable"/> を <c>false</c> にし、<see cref="UnavailableReason"/> に理由を保持する
/// （フィクスチャ自体は失敗させない）。各統合テストは冒頭で
/// <c>Assert.SkipUnless(fixture.IsAvailable, ...)</c> によりスキップする。
/// </para>
/// <para>
/// テスト間の独立性は、コンテナ（＝サーバー）を使い回しつつ各テストの冒頭で
/// <see cref="ResetSchemaAsync"/>（対象データベースの全テーブルを DROP）を実行し、
/// クリーンな状態から始めることで確保する。
/// </para>
/// </remarks>
public sealed class MySqlContainerFixture : IAsyncLifetime
{
    /// <summary>共有する MySQL コンテナ（Docker 不在時は起動されないため <c>null</c>）</summary>
    private MySqlContainer? _container;

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
            _container = new MySqlBuilder().WithImage("mysql:8.4").Build();
            await _container.StartAsync().ConfigureAwait(false);
            // @fk 等のユーザー変数を使う動的 SQL のため AllowUserVariables を付与する
            var baseCs = _container.GetConnectionString();
            var b = new MySqlConnectionStringBuilder(baseCs) { AllowUserVariables = true };
            ConnectionString = b.ConnectionString;
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // Docker が無い・デーモンに接続できない等の場合はテストをスキップさせる
            IsAvailable = false;
            UnavailableReason =
                $"MySQL コンテナを起動できませんでした（Docker 不在または起動失敗）: {ex.Message}";
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

    /// <summary>対象データベースの全テーブルを DROP し、各テストをクリーンな状態から始める</summary>
    /// <remarks>データベースは使い回し、外部キー制約を一時無効化して全テーブルを削除する方式で独立性を確保する</remarks>
    public async Task ResetSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // 現在の DB の全テーブル名を取得する
        var tables = new List<string>();
        await using (
            var cmd = new MySqlCommand(
                "SELECT TABLE_NAME FROM information_schema.TABLES "
                    + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE';",
                conn
            )
        )
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (tables.Count == 0)
        {
            return;
        }

        // FK 制約を無視して全テーブルを DROP する
        var dropList = string.Join(", ", tables.Select(t => $"`{t.Replace("`", "``")}`"));
        await using var drop = new MySqlCommand(
            $"SET FOREIGN_KEY_CHECKS = 0; DROP TABLE IF EXISTS {dropList}; SET FOREIGN_KEY_CHECKS = 1;",
            conn
        );
        await drop.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>コンテナに対して開いた新しい接続を返す（呼び出し側で破棄する）</summary>
    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>任意の SQL スクリプトをコンテナ上で実行するヘルパー（DDL のセットアップ用）</summary>
    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>コンテナの接続文字列を分解して共通接続設定 <see cref="DbConnectionSettings"/> を組み立てる</summary>
    /// <remarks>接続文字列ファクトリの実接続検証（D）で用いる</remarks>
    public DbConnectionSettings ToDbConnectionSettings()
    {
        var b = new MySqlConnectionStringBuilder(ConnectionString);
        return new DbConnectionSettings
        {
            Host = string.IsNullOrEmpty(b.Server) ? "localhost" : b.Server,
            Port = b.Port == 0 ? null : (int)b.Port,
            Database = b.Database,
            UserId = b.UserID,
            Password = b.Password,
            AuthMode = DbAuthMode.UsernamePassword,
        };
    }
}

/// <summary>MySQL 統合テスト用のコレクション定義（コンテナをコレクション内で共有する）</summary>
[CollectionDefinition(Name)]
public sealed class MySqlContainerCollection : ICollectionFixture<MySqlContainerFixture>
{
    /// <summary>コレクション名（各統合テストクラスの <c>[Collection]</c> で参照する）</summary>
    public const string Name = "MySQL Integration";
}
