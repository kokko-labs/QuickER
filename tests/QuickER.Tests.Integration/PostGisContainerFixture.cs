using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// PostGIS 拡張を入れた PostgreSQL コンテナを共有するフィクスチャ。
/// </summary>
/// <remarks>
/// <para>
/// 通常の <see cref="PostgreSqlContainerFixture"/>（<c>postgres:16-alpine</c>）と分けるのは、PostGIS が
/// <b>括弧引数に語を取る型</b>（<c>geometry(Point,4326)</c>）を持ち込む唯一の現実的な経路で、
/// そこが取込 → DDL 生成の通しで壊れていないことを実物で確かめる必要があるため。素の PostgreSQL では
/// この形の型を作れない。
/// </para>
/// <para>
/// Docker 不在時の扱い・厳格モードは他のコンテナフィクスチャと同じ規約に従う。
/// </para>
/// </remarks>
public sealed class PostGisContainerFixture : IAsyncLifetime
{
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
            _container = new PostgreSqlBuilder("postgis/postgis:16-3.4-alpine").Build();
            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex) when (!DockerRequirement.IsStrict)
        {
            // 厳格モード（QUICKER_REQUIRE_DOCKER=1）ではフィルタが成立せずそのまま失敗する
            // ＝壊れた Docker 構成がスキップ緑に化けない（DockerRequirement を参照）
            IsAvailable = false;
            UnavailableReason =
                $"PostGIS コンテナを起動できませんでした（Docker 不在または起動失敗）: {ex}";
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

    /// <summary>public スキーマを作り直し、PostGIS 拡張を入れ直す</summary>
    /// <remarks>
    /// <c>DROP SCHEMA public CASCADE</c> は public に作られた拡張のオブジェクトごと落とすため、
    /// 拡張の作成もここでやり直す。
    /// </remarks>
    public async Task ResetSchemaAsync(CancellationToken ct = default) =>
        await ExecuteAsync(
                """
                DROP SCHEMA public CASCADE;
                CREATE SCHEMA public;
                CREATE EXTENSION IF NOT EXISTS postgis WITH SCHEMA public;
                """,
                ct
            )
            .ConfigureAwait(false);

    /// <summary>コンテナに対して開いた新しい接続を返す（呼び出し側で破棄する）</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>任意の SQL スクリプトをコンテナ上で実行するヘルパー</summary>
    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>PostGIS コンテナを共有するテストコレクション</summary>
[CollectionDefinition(Name)]
public sealed class PostGisContainerCollection : ICollectionFixture<PostGisContainerFixture>
{
    public const string Name = "PostGIS Integration";
}
