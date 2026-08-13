using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.GeneratedSqliteFixture;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの失敗経路が「元の例外を握りつぶさない」ことを、共有ヘルパー <c>SqlTransactions</c> の
/// 実挙動（実 SQLite トランザクション）と、生成コードの構造（コミット後処理を try の外へ出す）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 従来は <c>CommitAsync</c> とコミット後処理（版の反映・<c>AcceptChanges</c>）が同じ try に入っており、
/// catch は無条件に <c>RollbackAsync</c> を呼んでいた。完了済みトランザクションへの Rollback は
/// <see cref="InvalidOperationException"/> を投げるため、catch から出る例外が元の例外を置き換え、
/// 本当の失敗原因が完全に失われていた。
/// </para>
/// <para>
/// ここでは (1) 完了済みトランザクションへ素の Rollback を投げると実際に例外になること（＝問題の実在）と、
/// (2) ヘルパーがそれを吸収すること、(3) 未コミットなら実際にロールバックすること、(4) 生成コードが
/// コミット後処理を try の外へ置いていること、を固定する。
/// </para>
/// </remarks>
public sealed class SqlTransactionRollbackTests
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

    [Fact(
        DisplayName = "前提: コミット済みトランザクションへの素の Rollback は例外になる（元の例外を置き換える）"
    )]
    public async Task RawRollbackAfterCommit_Throws()
    {
        await using var connection = await OpenAsync();
        var transaction = await connection.BeginTransactionAsync(Ct);
        await transaction.CommitAsync(Ct);

        var act = async () => await transaction.RollbackAsync(Ct);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "RollbackQuietlyAsync: コミット済みトランザクションでも例外を投げない")]
    public async Task RollbackQuietly_AfterCommit_DoesNotThrow()
    {
        await using var connection = await OpenAsync();
        var transaction = await connection.BeginTransactionAsync(Ct);
        await InsertAsync(connection, transaction);
        await transaction.CommitAsync(Ct);

        var act = async () => await SqlTransactions.RollbackQuietlyAsync(transaction);
        await act.Should().NotThrowAsync("catch から出る例外は伝播中の元の例外を置き換えてしまう");

        (await CountAsync(connection)).Should().Be(1, "コミット済みの結果は取り消されない");
    }

    [Fact(DisplayName = "RollbackQuietlyAsync: 未コミットのトランザクションは実際に取り消す")]
    public async Task RollbackQuietly_BeforeCommit_RollsBack()
    {
        await using var connection = await OpenAsync();
        var transaction = await connection.BeginTransactionAsync(Ct);
        await InsertAsync(connection, transaction);

        await SqlTransactions.RollbackQuietlyAsync(transaction);

        (await CountAsync(connection)).Should().Be(0, "未コミットの書き込みは取り消される");
    }

    /// <summary>
    /// 生成コードの構造固定: コミット後処理（版の反映・<c>AcceptChanges</c>）は try の外にあり、
    /// catch は共有ヘルパーだけを呼ぶ。
    /// </summary>
    /// <remarks>
    /// 「commit 後処理が throw したら元の例外が失われる」という失敗は実 DB では作りにくい（版の反映も
    /// <c>AcceptChanges</c> も正常系では投げない）ため、構造そのものを固定して回帰を止める。
    /// </remarks>
    [Fact(DisplayName = "生成コード: コミット後処理は try の外・catch は共有ヘルパーのみを呼ぶ")]
    public void GeneratedSave_KeepsPostCommitWorkOutsideTry()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BuildProbeDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        // 生成物は CRLF のため、比較の前に LF へ正規化する
        var content = string.Join("\n", result.Files.Select(file => file.Content))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        content
            .Should()
            .Contain(
                "            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);\n"
                    + "        }\n"
                    + "        catch\n"
                    + "        {\n"
                    + "            await SqlTransactions.RollbackQuietlyAsync(transaction).ConfigureAwait(false);\n"
                    + "            throw;\n"
                    + "        }\n",
                "コミットの直後に try が閉じ、catch は共有ヘルパーだけを呼ぶ"
            );
        content
            .Split("await transaction.RollbackAsync(")
            .Length.Should()
            .Be(
                2,
                "素の Rollback 呼び出しは共有ヘルパーの中の 1 箇所だけ（呼び出し側は必ずヘルパー経由）"
            );
        content
            .Should()
            .Contain("        versions.Apply();\n", "版の反映は try の外（8 スペース）に置かれる");
        content
            .Should()
            .Contain(
                "        EntityGraphSaver.AcceptChanges(entity, cascadeSave, hooks?.Skipped);\n"
                    + "        return rows;",
                "AcceptChanges も try の外（8 スペース）に置かれる"
            );
    }

    /// <summary>構造検証用の最小の図（Repository が 1 つ出れば十分）</summary>
    private static ErDiagram BuildProbeDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "notes",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "note_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };
}
