using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using BackendParitySqlite = QuickER.Tests.GeneratedBackendParityFixture;
using SyncModelSqlite = QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;
using SyncSqlite = QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;

namespace QuickER.Tests.Integration;

/// <summary>
/// テストが自分で作る SQLite の実ファイル DB（同期のローカル・サーバー役、実装先の一致の SQLite）の接続を、開くたびに
/// ジャーナルを軽い設定にする（<c>journal_mode=MEMORY</c>・<c>synchronous=OFF</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 既定のジャーナル（<c>DELETE</c>・<c>synchronous=FULL</c>）は、トランザクションごとにジャーナルのファイルを作って消し、
/// そのたびに fsync する。トランザクションを多く流す同期のテストでは、ファイルの作成・削除と fsync が時間の大半を占め、
/// ディスクの遅い CI のランナーではそれが数十倍に延びる。
/// </para>
/// <para>
/// <c>MEMORY</c> はロールバックジャーナルをファイルでなくメモリに持つ。ロールバックジャーナルのままなので、ロックの意味
/// （共有・予約・排他の取り方と、ほかの接続から見える待ち）は <c>DELETE</c> と同じで、プロセスの中の例外によるロールバックも
/// メモリのジャーナルから正しく戻る。失うのはプロセスの停止・電源断のときの復旧と耐久性だけで（<c>synchronous=OFF</c> も
/// 同じ）、テストはどちらも見ない（DB はテストごとの一時ファイル）。<c>WAL</c> は読み手と書き手が待ち合わなくなり、並行の
/// テストが見るロックの意味が変わるので使わない。
/// </para>
/// <para>
/// 設定は接続ごと（プールから再利用された接続も開くたびに当てる）なので、同じ DB をこの設定を通さずに開く接続（スキーマの
/// 準備・読み取りの比較・EF Core の一部の構成）が混ざる。<c>MEMORY</c> の接続はジャーナルのファイルに触れないので、既定の
/// 接続が自分のジャーナルを作って消すのを妨げない。<c>PERSIST</c>（ファイルを消さずに残す）は Windows ではジャーナルの
/// ファイルを開いたまま持ち、既定の接続がコミットでそれを消せずに <c>disk I/O error</c> になる
/// （<see cref="SqliteLightJournalTests"/> が固定する）。
/// </para>
/// </remarks>
internal static class SqliteLightJournal
{
    /// <summary>開くたびにジャーナルの設定を当てるよう、接続へ登録して返す</summary>
    public static SqliteConnection Apply(SqliteConnection connection)
    {
        connection.StateChange += (sender, e) =>
        {
            if (e.CurrentState != ConnectionState.Open || e.OriginalState == ConnectionState.Open)
            {
                return;
            }

            Execute((SqliteConnection)sender!);
        };

        return connection;
    }

    /// <summary>EF Core が開く接続へ同じ設定を当てるインターセプター（状態を持たないので 1 つを共有する）</summary>
    public static readonly IInterceptor EfCoreInterceptor = new OpenedInterceptor();

    private const string Pragmas = "PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF;";

    private static void Execute(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    private sealed class OpenedInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(
            DbConnection connection,
            ConnectionEndEventData eventData
        ) => Execute((SqliteConnection)connection);

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Execute((SqliteConnection)connection);

            return Task.CompletedTask;
        }
    }
}

/// <summary>同期のモデルベーステストの接続ファクトリ（生成された既定の接続へジャーナルの設定を足す）</summary>
internal sealed class LightJournalSyncModelConnectionFactory(string connectionString)
    : SyncModelSqlite.ISqlConnectionFactory
{
    private readonly SyncModelSqlite.SqlConnectionFactory _inner = new(connectionString);

    public SqliteConnection CreateConnection() =>
        SqliteLightJournal.Apply(_inner.CreateConnection());
}

/// <summary>同期のパリティスイートの接続ファクトリ（生成された既定の接続へジャーナルの設定を足す）</summary>
internal sealed class LightJournalSyncConnectionFactory(string connectionString)
    : SyncSqlite.ISqlConnectionFactory
{
    private readonly SyncSqlite.SqlConnectionFactory _inner = new(connectionString);

    public SqliteConnection CreateConnection() =>
        SqliteLightJournal.Apply(_inner.CreateConnection());
}

/// <summary>実装先どうしの一致テストの接続ファクトリ（生成された既定の接続へジャーナルの設定を足す）</summary>
internal sealed class LightJournalBackendParityConnectionFactory(string connectionString)
    : BackendParitySqlite.ISqlConnectionFactory
{
    private readonly BackendParitySqlite.SqlConnectionFactory _inner = new(connectionString);

    public SqliteConnection CreateConnection() =>
        SqliteLightJournal.Apply(_inner.CreateConnection());
}

/// <summary>実装先どうしの一致テストの DI 登録へ、ジャーナルの設定を足した接続ファクトリを差し込む</summary>
internal static class LightJournalServiceCollectionExtensions
{
    /// <summary>
    /// <c>AddGeneratedSqliteRepositories</c> の後で呼ぶ（接続ファクトリは <c>AddSingleton</c> の後勝ちなので、
    /// リポジトリと生 SQL の実行器はこのファクトリを受け取る）
    /// </summary>
    public static IServiceCollection AddLightJournal(
        this IServiceCollection services,
        string connectionString
    ) =>
        services.AddSingleton<BackendParitySqlite.ISqlConnectionFactory>(
            _ => new LightJournalBackendParityConnectionFactory(connectionString)
        );
}
