using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// コマンド生成の共通ヘルパー <see cref="DbCommands"/> の検証。
/// </summary>
/// <remarks>
/// スキーマ取込・同期のコマンドは全方言がこのヘルパー経由で作られるため、ここが「タイムアウトを必ず設定する」
/// ことがそのまま 5 方言の保証になる（生成箇所を迂回できないことは
/// <see cref="SchemaCommandTimeoutGuardTests"/> が構造的に固定する）。
/// </remarks>
public class DbCommandsTests
{
    /// <summary>既定値は従来ハードコードされていた 60 秒（ADO.NET 既定の 30 秒ではない）</summary>
    [Fact(DisplayName = "DbCommands の既定タイムアウトは 60 秒")]
    public void DefaultTimeout_Is60Seconds()
    {
        DbCommands.DefaultTimeoutSeconds.Should().Be(60);
    }

    /// <summary>生成したコマンドへ指定のタイムアウトが実際に設定される</summary>
    [Fact(DisplayName = "Create は CommandText とタイムアウトを設定する")]
    public void Create_SetsCommandTextAndTimeout()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var command = DbCommands.Create(connection, "SELECT 1;", 123);

        command.CommandText.Should().Be("SELECT 1;");
        command.CommandTimeout.Should().Be(123);
    }

    /// <summary>0 は ADO.NET の規約どおり「無制限」としてそのまま通す</summary>
    [Fact(DisplayName = "Create は 0（無制限）を許容する")]
    public void Create_AllowsZeroAsUnlimited()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var command = DbCommands.Create(connection, "SELECT 1;", 0);

        command.CommandTimeout.Should().Be(0);
    }

    /// <summary>負値は呼び出し側のバグとして入口で弾く（ADO も受け付けない）</summary>
    [Fact(DisplayName = "Create は負のタイムアウトを拒否する")]
    public void Create_RejectsNegativeTimeout()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        var act = () => DbCommands.Create(connection, "SELECT 1;", -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>トランザクション付きオーバーロードはコマンドをその取引へ参加させる</summary>
    [Fact(DisplayName = "Create はトランザクションへ参加させる")]
    public async Task Create_AttachesTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        await using var command = DbCommands.Create(connection, "SELECT 1;", 30, transaction);

        command.Transaction.Should().BeSameAs(transaction);
        command.CommandTimeout.Should().Be(30);
    }
}
