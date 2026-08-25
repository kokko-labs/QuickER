using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 失敗経路の後始末ヘルパー <see cref="DbTransactions.RollbackQuietlyAsync"/> の検証（実 SQLite トランザクション）。
/// </summary>
/// <remarks>
/// <para>
/// 完了済みトランザクションへの素の Rollback は <see cref="InvalidOperationException"/> になり、
/// catch 節から呼ぶと伝播中の元の例外を置き換えてしまう。ヘルパーが (1) 完了済みでも投げないこと、
/// (2) 未コミットなら実際に取り消すこと、を実挙動で固定する（呼び出し側がヘルパーを迂回できないことは
/// <see cref="SchemaSyncRollbackGuardTests"/> が構造的に固定する）。
/// </para>
/// <para>
/// 生成コード側の同じ解法（<c>SqlTransactions.RollbackQuietlyAsync</c>）は
/// <c>SqlTransactionRollbackTests</c> が同型のシナリオで検証している。
/// </para>
/// </remarks>
public sealed class DbTransactionsTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>1 行だけのテーブルを持つインメモリ SQLite 接続を開く（接続を閉じるまで存続する）</summary>
    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(Ct);

        await using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY);";
        await create.ExecuteNonQueryAsync(Ct);

        return connection;
    }

    /// <summary>テーブルの行数を数える</summary>
    private static async Task<long> CountAsync(SqliteConnection connection)
    {
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM t;";
        return (long)(await count.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>トランザクション内で 1 行挿入する</summary>
    private static async Task InsertAsync(SqliteConnection connection, DbTransaction transaction)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = (SqliteTransaction)transaction;
        insert.CommandText = "INSERT INTO t (id) VALUES (1);";
        await insert.ExecuteNonQueryAsync(Ct);
    }

    [Fact(DisplayName = "RollbackQuietlyAsync: コミット済みトランザクションでも例外を投げない")]
    public async Task RollbackQuietly_AfterCommit_DoesNotThrow()
    {
        await using var connection = await OpenAsync();
        var transaction = await connection.BeginTransactionAsync(Ct);
        await InsertAsync(connection, transaction);
        await transaction.CommitAsync(Ct);

        var act = async () => await DbTransactions.RollbackQuietlyAsync(transaction);
        await act.Should()
            .NotThrowAsync("catch 節から出る例外は伝播中の元の例外を置き換えてしまう");

        (await CountAsync(connection)).Should().Be(1, "コミット済みの結果は取り消されない");
    }

    [Fact(DisplayName = "RollbackQuietlyAsync: 未コミットのトランザクションは実際に取り消す")]
    public async Task RollbackQuietly_BeforeCommit_RollsBack()
    {
        await using var connection = await OpenAsync();
        var transaction = await connection.BeginTransactionAsync(Ct);
        await InsertAsync(connection, transaction);

        await DbTransactions.RollbackQuietlyAsync(transaction);

        (await CountAsync(connection)).Should().Be(0, "未コミットの書き込みは取り消される");
    }
}
