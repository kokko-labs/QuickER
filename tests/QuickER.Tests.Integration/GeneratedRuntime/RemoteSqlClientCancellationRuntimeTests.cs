using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成したリモートサーバー（SQL Server の QuickER 版 Repository）で、クエリの実行中にクライアントが要求を取り消したときの
/// 扱いを、実 SQL Server（Testcontainers・Docker 依存）で確かめるテスト。
/// </summary>
/// <remarks>
/// <para>
/// 要求のトークンが取り消されると、SqlClient は実行中のコマンドを取り消し、その取り消しを自分の型の例外
/// （<see cref="SqlException"/>「Operation cancelled by user」）で報告する。応答の相手はもういないので、サーバーの障害
/// （Error のログ・500 のフック）として扱ってはいけない。
/// </para>
/// <para>
/// 一方で、取り消しと重なっただけの別の失敗（DB の例外でないもの）と、取り消していない要求の DB のエラーは、
/// 今までどおりサーバーの障害として記録されなければならない（取り消しの扱いが本物の障害を隠さないことの対照）。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class RemoteSqlClientCancellationRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
) : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly RecordingLoggerProvider _log = new();
    private readonly ConcurrentQueue<Exception> _hooks = new();
    private readonly SemaphoreSlim _completed = new(0);
    private InProcessRemoteServer? _server;
    private ServiceProvider? _client;

    /// <summary>テストごとに差し込む、サーバー側のゲジェットの Save フック</summary>
    private ISaveHook<GadgetEntity>? _saveHook;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        await fixture.ResetSchemaAsync(Ct);
        await fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    /// <summary>サーバーを立て、クライアントの DI を組む</summary>
    private async Task StartAsync()
    {
        _server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqlServerRepositories(fixture.ConnectionString);

                if (_saveHook is not null)
                {
                    services.AddSingleton(_saveHook);
                }
            },
            app =>
            {
                app.Use(
                    async (context, next) =>
                    {
                        try
                        {
                            await next(context);
                        }
                        finally
                        {
                            _completed.Release();
                        }
                    }
                );
                app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous)
                    .WithMetadata(
                        new RemoteErrorDetailPolicy(false, (_, ex) => _hooks.Enqueue(ex))
                    );
            },
            Ct,
            builder =>
            {
                builder.Logging.AddProvider(_log);
                builder.Logging.SetMinimumLevel(LogLevel.Debug);
            }
        );
        _client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
    }

    private IGadgetRemoteRepository Gadgets =>
        _client!.GetRequiredService<IGadgetRemoteRepository>();

    /// <summary>サーバーが要求を処理し終えるのを待つ（Insert などの先行の要求の分も数える）</summary>
    private async Task WaitCompletedAsync(int requests = 1)
    {
        for (var i = 0; i < requests; i++)
        {
            (await _completed.WaitAsync(TimeSpan.FromSeconds(30), Ct)).Should().BeTrue();
        }
    }

    /// <summary>エンジンのログとフックを出力へ書く（報告用）</summary>
    private void Describe(string label, Exception? failure)
    {
        output.WriteLine($"{label}: failure={failure?.GetType().Name ?? "-"} {failure?.Message}");

        foreach (var entry in _log.Entries.Where(e => e.Category == "QuickER.RemoteServer"))
        {
            output.WriteLine(
                $"  log {entry.Level}: {entry.Exception?.GetType().FullName} {entry.Exception?.Message}"
            );
        }

        foreach (var hook in _hooks)
        {
            output.WriteLine($"  hook {hook.GetType().FullName}: {hook.Message}");
        }
    }

    [Fact(
        DisplayName = "[RemoteSqlClient] クエリの実行中にクライアントが取り消した要求は、サーバーの障害として記録しない"
    )]
    public async Task ClientCancelDuringQuery_IsNotAFault()
    {
        await StartAsync();

        // 表全体を排他ロックで押さえ、GetAll の SELECT をロックの待ちで止める
        await using var locker = new SqlConnection(fixture.ConnectionString);
        await locker.OpenAsync(Ct);
        await using var transaction = (SqlTransaction)await locker.BeginTransactionAsync(Ct);
        await using (var lockCommand = locker.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "SELECT COUNT(*) FROM gadgets WITH (TABLOCKX, HOLDLOCK)";
            await lockCommand.ExecuteScalarAsync(Ct);
        }

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));
        Exception? failure = null;

        try
        {
            await Gadgets.GetAllAsync(cancel.Token);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await WaitCompletedAsync();
        await transaction.RollbackAsync(Ct);
        Describe("cancel during query", failure);

        failure
            .Should()
            .BeAssignableTo<OperationCanceledException>("クライアントは自分で取り消した");
        _log.Engine(LogLevel.Error)
            .Should()
            .BeEmpty("応答の相手のいない取り消しはサーバーの障害ではない");
        _hooks.Should().BeEmpty();
        _log.Engine(LogLevel.Debug)
            .Should()
            .ContainSingle("切断として扱った 1 件は Debug で記録する")
            .Which.Exception.Should()
            .BeOfType<SqlException>();
    }

    [Fact(
        DisplayName = "[RemoteSqlClient] 対照: 取り消しと重なった DB の例外でない失敗は、サーバーの障害として記録する"
    )]
    public async Task NonDatabaseFailureDuringCancel_IsStillAFault()
    {
        _saveHook = new FailingWhenCancelledHook();
        await StartAsync();

        var gadget = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(1),
            Name = NameValue.Create("alpha"),
        };
        gadget.MarkAdded();

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));
        Exception? failure = null;

        try
        {
            await Gadgets.SaveAsync(gadget, cancellationToken: cancel.Token);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await WaitCompletedAsync();
        Describe("non-database failure during cancel", failure);

        _log.Engine(LogLevel.Error)
            .Select(entry => entry.Exception?.Message)
            .Should()
            .Contain("failed after the request was cancelled");
        _hooks.Select(ex => ex.Message).Should().Contain("failed after the request was cancelled");
    }

    /// <summary>要求が取り消されるまで待ち、取り消された後に DB の例外でない失敗を投げる Save フック</summary>
    private sealed class FailingWhenCancelledHook : ISaveHook<GadgetEntity>
    {
        public async Task<bool> BeforeSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 取り消しと重なった本物の不具合（取り消しの例外を内側に持たない）
                throw new InvalidOperationException("failed after the request was cancelled");
            }

            return true;
        }
    }

    [Fact(
        DisplayName = "[RemoteSqlClient] 対照: 取り消していない要求の DB のエラーは、サーバーの障害として記録する"
    )]
    public async Task DatabaseErrorWithoutCancel_IsAFault()
    {
        await StartAsync();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE gadget_notes";
            await drop.ExecuteNonQueryAsync(Ct);
        }

        Exception? failure = null;

        try
        {
            await _client!.GetRequiredService<IGadgetNoteRemoteRepository>().GetAllAsync(Ct);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await WaitCompletedAsync();
        Describe("database error without cancel", failure);

        failure.Should().BeOfType<RemoteRepositoryException>().Which.StatusCode.Should().Be(500);
        _log.Engine(LogLevel.Error)
            .Should()
            .ContainSingle()
            .Which.Exception.Should()
            .BeOfType<SqlException>();
        _hooks.Should().ContainSingle().Which.Should().BeOfType<SqlException>();
    }
}
