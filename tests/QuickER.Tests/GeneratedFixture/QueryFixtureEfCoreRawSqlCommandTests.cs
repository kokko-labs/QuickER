using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// EF Core モードの生 SQL が作るコマンドの後始末を検証するテストクラス。
/// </summary>
/// <remarks>
/// コマンドは DbContext の接続から作り、パラメータを束縛してから呼び出し元へ渡す。束縛が例外になると呼び出し元は
/// コマンドを受け取れないので、作った側が破棄しなければ破棄されないまま残る。
/// 接続を差し替えて、作ったコマンドが破棄されたかを数える（DB はメモリ上の SQLite）。
/// </remarks>
public class QueryFixtureEfCoreRawSqlCommandTests
{
    /// <summary>作ったコマンドを覚えておく SQLite の接続</summary>
    private sealed class TrackingConnection(string connectionString)
        : SqliteConnection(connectionString)
    {
        public List<TrackingCommand> Commands { get; } = [];

        public override SqliteCommand CreateCommand()
        {
            var command = new TrackingCommand { Connection = this };
            Commands.Add(command);
            return command;
        }
    }

    /// <summary>破棄されたかを覚えておくコマンド</summary>
    private sealed class TrackingCommand : SqliteCommand
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>読むと例外になるプロパティを持つパラメータ（束縛で失敗させる）</summary>
    private sealed class FailingParameters
    {
        public int Id => throw new InvalidOperationException("parameter getter failed");
    }

    /// <summary>決まった構成の DbContext を作るファクトリ</summary>
    private sealed class Factory(DbContextOptions<QuickErDbContext> options)
        : IDbContextFactory<QuickErDbContext>
    {
        public QuickErDbContext CreateDbContext() => new(options);
    }

    [Fact(DisplayName = "EF Core: 生 SQL のパラメータの束縛が失敗したら、作ったコマンドを破棄する")]
    public async Task BindingFailure_DisposesTheCommand()
    {
        await using var connection = new TrackingConnection("Data Source=:memory:");
        var options = new DbContextOptionsBuilder<QuickErDbContext>()
            .UseSqlite(connection)
            .IgnoreServiceProviderCacheLimit()
            .Options;
        var executor = new EfCoreSqlExecutor<QuickErDbContext>(new Factory(options));

        var act = () => executor.ExecuteSqlAsync("SELECT @Id", new FailingParameters());

        await act.Should().ThrowAsync<Exception>();
        connection.Commands.Should().ContainSingle().Which.IsDisposed.Should().BeTrue();
    }
}
