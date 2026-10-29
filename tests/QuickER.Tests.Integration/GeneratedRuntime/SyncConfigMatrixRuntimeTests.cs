using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期の構成の行列（<see cref="SyncConfigDimensions"/>）の行ごとに、生成された DI の登録で同期を組み、新しい行の上げ下げが
/// 1 回の同期で届くことを確かめる（サーバー＝実 SQL Server・ローカル＝実ファイル SQLite）。
/// </summary>
/// <remarks>
/// <para>
/// サーバーは実 SQL Server（生成された直結の差分ソースは SQL Server の語彙＝<c>MIN_ACTIVE_ROWVERSION()</c> を使うので、
/// SQLite の代役では差分ソースの登録を差し替えることになり、登録の形を確かめられない）。HTTP の行は 1 つの Kestrel の
/// サーバー（同じ SQL Server の DB）を共有する。行ごとにローカルの DB とコンテナを作り、キーは行ごとに別の値を使う
/// （サーバーの DB は全部の行で共有する）。
/// </para>
/// <para>
/// 既知の失敗は <see cref="SyncConfigDimensions.Known"/>（厳格＝載っていない失敗も、載っているのに起きない失敗も赤）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SyncConfigMatrixRuntimeTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバーのリポジトリのキー（直結・キーつきの行）</summary>
    private const string ServerKey = "server";

    /// <summary>ローカルのリポジトリのキー（キーつきの行）</summary>
    private const string LocalKey = "local";

    [Fact(
        DisplayName = "[Sync/構成] DI の登録の形・転送経路・キーの型・キーを決めるフックの組ごとに、新しい行が 1 回の同期で上がり・降りる（既知の失敗は除く）"
    )]
    public async Task EveryConfiguration_RoundTripsNewRows()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        await fixture.ResetSchemaAsync(Ct);

        foreach (var adapter in SyncConfigFixtures.Axis.Adapters)
        {
            await fixture.ApplyDdlAsync(adapter.ServerDiagram, Ct);
        }

        var servers = new Dictionary<string, InProcessRemoteServer>(StringComparer.Ordinal);

        try
        {
            foreach (
                var adapter in SyncConfigFixtures.Axis.Adapters.Where(a => a.HasRemoteServices)
            )
            {
                servers[adapter.AxisValue] = await adapter.StartRemoteServerAsync(
                    fixture.ConnectionString,
                    Ct
                );
            }

            var coverage = new CoverageCounter(SyncConfigDimensions.Table);
            var problems = new List<string>();
            var index = 0;

            foreach (var row in SyncConfigDimensions.Rows)
            {
                var c = row.Input;
                coverage.Observe(c.Assignment);
                var keys = (Local: 1000 + (index * 2), Server: 1001 + (index * 2));
                index++;
                Exception? failure = null;

                try
                {
                    await RunAsync(c, keys, servers);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                SyncConfigDimensions.Known.Run(
                    c.Id,
                    () =>
                    {
                        if (failure is not null)
                        {
                            ExceptionDispatchInfo.Throw(failure);
                        }
                    },
                    problems
                );
            }

            var report = coverage.Report();
            report.WriteSummary();
            KnownCaseLedger.ThrowIfAny(problems);
            report.AssertGaps(SyncConfigDimensions.Gaps, SyncConfigDimensions.Scope);
        }
        finally
        {
            foreach (var server in servers.Values)
            {
                await server.DisposeAsync();
            }
        }
    }

    /// <summary>1 行を流す（届かなければ例外）</summary>
    private async Task RunAsync(
        SyncConfigCase c,
        (int Local, int Server) keys,
        IReadOnlyDictionary<string, InProcessRemoteServer> servers
    )
    {
        var adapter = SyncConfigFixtures.Axis.Get(c.Fixture);
        using var local = SqliteTempDatabase.Create();
        using var clients = new OwnedClients();
        await local.ApplyDdlAsync(adapter.LocalDiagram, Ct);

        object? localKey = c.LocalKeyed ? LocalKey : null;
        object? serverKey = c.Server == SyncConfigDimensions.DirectKeyed ? ServerKey : null;
        var services = new ServiceCollection();
        adapter.AddLocalRepositories(services, localKey, local.ReadWriteCreateConnectionString);

        if (!c.OverHttp)
        {
            adapter.AddServerRepositories(services, serverKey, fixture.ConnectionString);
        }

        if (c.HookAssignsKey)
        {
            adapter.AddKeyAssigningHook(services, keys.Local);
        }

        if (c.Registration == SyncConfigDimensions.Support)
        {
            adapter.AddSyncSupport(services, serverKey, localKey);
        }
        else
        {
            switch (c.Server)
            {
                case SyncConfigDimensions.HttpAddress:
                    adapter.AddHttpSources(
                        services,
                        servers[c.Fixture].BaseAddress(adapter.RemotePrefix)
                    );
                    break;
                case SyncConfigDimensions.HttpClientFactory:
                    // 呼び出し側が持つ HttpClient を渡す形（ファクトリは解決のたびに呼ばれる。クライアントは行の終わりに捨てる）
                    var address = new Uri(
                        servers[c.Fixture].BaseAddress(adapter.RemotePrefix) + "/"
                    );
                    adapter.AddHttpSources(services, _ => clients.Add(address));
                    break;
                default:
                    adapter.AddDirectSources(services, serverKey);
                    break;
            }

            adapter.AddSyncEngine(services, localKey);
        }

        await using var provider = services.BuildServiceProvider();

        // サーバーの他者の書き込みと確かめは、同期と別のコンテナ（キーなしの SQL Server のリポジトリ）で行う
        var serverSide = new ServiceCollection();
        adapter.AddServerRepositories(serverSide, null, fixture.ConnectionString);
        await using var serverProvider = serverSide.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await adapter.WriteLocalAsync(
                scope.ServiceProvider,
                localKey,
                keys.Local,
                c.HookAssignsKey,
                c.ViaGraph,
                Ct
            );
            (await adapter.SyncAsync(scope.ServiceProvider, Ct))
                .Should()
                .BeNull("the upload of a new row has nothing to conflict with");
        }

        (await adapter.ExistsAsync(serverProvider, null, keys.Local, Ct))
            .Should()
            .BeTrue("the local new row reaches the server in one sync");

        await adapter.InsertAsync(serverProvider, null, keys.Server, Ct);

        await using (var scope = provider.CreateAsyncScope())
        {
            (await adapter.SyncAsync(scope.ServiceProvider, Ct))
                .Should()
                .BeNull("the download of a new row has nothing to conflict with");
            (await adapter.ExistsAsync(scope.ServiceProvider, localKey, keys.Server, Ct))
                .Should()
                .BeTrue("the server new row reaches the local database in one sync");
        }
    }

    /// <summary>行の中で HttpClient のファクトリが作ったクライアント（行の終わりに捨てる）</summary>
    private sealed class OwnedClients : IDisposable
    {
        private readonly List<HttpClient> _clients = [];

        public HttpClient Add(Uri baseAddress)
        {
            var client = new HttpClient { BaseAddress = baseAddress };

            lock (_clients)
            {
                _clients.Add(client);
            }

            return client;
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
        }
    }
}
