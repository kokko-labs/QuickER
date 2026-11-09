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
/// 1 回の同期で届き、続く洗い替え 1 回で両方の行がローカルにそろうことを確かめる（サーバー＝実 SQL Server・ローカル＝実ファイル SQLite）。
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
        DisplayName = "[Sync/構成] DI の登録の形・転送経路・キーの型・キーを決めるフックの組ごとに、新しい行が 1 回の同期で上がり・降り、洗い替え 1 回でそろう（既知の失敗は除く）"
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
                var keys = (
                    Local: 1000 + (index * 3),
                    Server: 1001 + (index * 3),
                    Extra: 1002 + (index * 3)
                );
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
    /// <param name="c">行</param>
    /// <param name="keys">
    /// 行のキー（Local＝ローカルの新しい行・Server＝サーバーの新しい行・Extra＝サーバーだけに置く補助の親。サーバーの DB は全部の行で
    /// 共有するので行ごとに別の値）
    /// </param>
    /// <param name="servers">フィクスチャごとの HTTP のサーバー</param>
    private async Task RunAsync(
        SyncConfigCase c,
        (int Local, int Server, int Extra) keys,
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
        adapter.AddLocalRepositories(
            services,
            localKey,
            local.ReadWriteCreateConnectionString,
            c.LocalAsInstances
        );

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
            adapter.AddSyncSupport(services, serverKey, localKey, c.ExcludesParent);
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

            adapter.AddSyncEngine(services, localKey, c.ExcludesParent);
        }

        // スコープの検査つき（シングルトンがスコープのサービスを掴む登録の形は、解決で断られる）。行の確かめはスコープの中で
        // 解決する（同期はそのつど別のスコープを作る）
        await using var root = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await using var rowScope = root.CreateAsyncScope();
        var provider = rowScope.ServiceProvider;

        // サーバーの他者の書き込みと確かめは、同期と別のコンテナ（キーなしの SQL Server のリポジトリ）で行う
        var serverSide = new ServiceCollection();
        adapter.AddServerRepositories(serverSide, null, fixture.ConnectionString);
        await using var serverProvider = serverSide.BuildServiceProvider();

        adapter
            .RegistrationProblems(provider, localKey, serverKey, c.OverHttp, c.ExcludesParent)
            .Should()
            .BeEmpty(
                "the registration builds the decorated local repositories and the source of the transport"
            );

        if (c.ExcludesParent)
        {
            await RunExcludedParentAsync(adapter, keys, localKey, provider, serverProvider);

            return;
        }

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
                .Problem.Should()
                .BeNull("the upload of a new row has nothing to conflict with");
        }

        (await adapter.ExistsAsync(serverProvider, null, keys.Local, Ct))
            .Should()
            .BeTrue("the local new row reaches the server in one sync");

        await adapter.InsertAsync(serverProvider, null, keys.Server, Ct);

        await using (var scope = provider.CreateAsyncScope())
        {
            var second = await adapter.SyncAsync(scope.ServiceProvider, Ct);
            second
                .Problem.Should()
                .BeNull("the download of a new row has nothing to conflict with");
            second
                .Downloaded.Should()
                .Be(
                    1,
                    "the anchor derived from the mirrored server versions leaves only the new server row"
                );
            (await adapter.ExistsAsync(scope.ServiceProvider, localKey, keys.Server, Ct))
                .Should()
                .BeTrue("the server new row reaches the local database in one sync");
        }

        // 洗い替え 1 回（全消し→流し込み）が、同じ登録の形で例外なく終わり、両方の行がローカルにそろう
        await using (var scope = provider.CreateAsyncScope())
        {
            await adapter.RefreshAsync(scope.ServiceProvider, Ct);

            foreach (var id in new[] { keys.Local, keys.Server })
            {
                (await adapter.ExistsAsync(scope.ServiceProvider, localKey, id, Ct))
                    .Should()
                    .BeTrue(
                        "a refresh reloads every server row, the ones this row synchronised included"
                    );
            }
        }

        if (c.RemovesParentWithNewChild)
        {
            await RemoveParentsWithNewChildrenAsync(
                adapter,
                keys,
                localKey,
                provider,
                serverProvider
            );
        }
    }

    /// <summary>
    /// 水準 GraphRemoveParentWithNewChild の段：上げ下げした 2 つの親（ローカルとサーバーの両方にある行）に、キーの無い新しい子
    /// （Added）を足して親を消すグラフ保存（単数と複数の SaveAsync）と、新しい子だけを MarkRemoved したグラフ保存が、記録の段で
    /// 投げずに保存され、同期 1 回でサーバーから親が消え、ジャーナルに何も残らない
    /// </summary>
    /// <remarks>
    /// 版なしの子を持つフィクスチャは、消す親の下に、サーバーに同じキーの行がある新しい版なしの子も入れる（サーバーの行は消さない
    /// 補助の親の下に置く）。新しい行は保存側の削除で何も消さないので記録されず、後勝ちの同期の無条件の削除がサーバーの同じキーの
    /// 行を消さない。同期は版なしの表も流す後勝ちのモードで流す。
    /// </remarks>
    private static async Task RemoveParentsWithNewChildrenAsync(
        ISyncConfigFixture adapter,
        (int Local, int Server, int Extra) keys,
        object? localKey,
        IServiceProvider provider,
        IServiceProvider serverProvider
    )
    {
        int? noteId = adapter.HasVersionlessChild ? keys.Local : null;

        try
        {
            if (noteId is { } serverNote)
            {
                await adapter.InsertAsync(serverProvider, null, keys.Extra, Ct);
                await adapter.InsertVersionlessChildAsync(
                    serverProvider,
                    null,
                    keys.Extra,
                    serverNote,
                    Ct
                );
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var local = scope.ServiceProvider;
                await adapter.SaveNewChildRemovedAsync(local, localKey, keys.Local, Ct);
                (await adapter.ExistsAsync(local, localKey, keys.Local, Ct))
                    .Should()
                    .BeTrue("removing only the new child leaves its saved parent in place");
                await adapter.SaveParentRemovedWithNewChildAsync(
                    local,
                    localKey,
                    keys.Local,
                    noteId,
                    asList: false,
                    Ct
                );
                await adapter.SaveParentRemovedWithNewChildAsync(
                    local,
                    localKey,
                    keys.Server,
                    null,
                    asList: true,
                    Ct
                );

                foreach (var parent in new[] { keys.Local, keys.Server })
                {
                    (await adapter.ExistsAsync(local, localKey, parent, Ct))
                        .Should()
                        .BeFalse("the graph save removed the parent locally");
                    (await adapter.ChildCountAsync(local, localKey, parent, Ct))
                        .Should()
                        .Be(0, "the new child of a removed parent is never written");
                }
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                (await adapter.SyncAsync(scope.ServiceProvider, Ct, lastWriteWins: true))
                    .Problem.Should()
                    .BeNull("the deletes of the removed parents have nothing to conflict with");
            }

            foreach (var parent in new[] { keys.Local, keys.Server })
            {
                (await adapter.ExistsAsync(serverProvider, null, parent, Ct))
                    .Should()
                    .BeFalse("one sync deletes the removed parent on the server");
            }

            if (noteId is { } note)
            {
                (await adapter.VersionlessChildExistsAsync(serverProvider, null, note, Ct))
                    .Should()
                    .BeTrue(
                        "a new versionless child of a removed parent is not recorded, so no unguarded delete reaches the server row with its key"
                    );
            }

            var journal = await adapter.JournalAsync(provider, localKey, Ct);
            journal.Tables.Should().BeEmpty("the sync sent every recorded delete");
            journal
                .UnsetKeyEntries.Should()
                .Be(0, "a node without a key deletes nothing and is not recorded");
        }
        finally
        {
            await adapter.DeleteServerRowsAsync(
                serverProvider,
                [],
                noteId is { } leftNote ? [leftNote] : [],
                noteId is null ? [] : [keys.Extra],
                CancellationToken.None
            );
        }
    }

    /// <summary>
    /// 水準 GraphViaExcludedParent の流れ：親の表を構築時に除外した登録で、除外した親の直接の書き込みはジャーナルに何も残さず、
    /// 除外した親のリポジトリのグラフ保存で足した同期する子は記録され（同じ保存が書き換える除外した親の行は記録しない）、同期 1 回で
    /// サーバーへ上がりローカルに残る。続けてサーバーの
    /// 新しい子を同期 1 回で降ろし、洗い替え 1 回で両方の子がローカルにそろう
    /// </summary>
    /// <remarks>
    /// エンジンは親の表を降ろさないので、親はテストがサーバーとローカルの両方へ直接入れる。サーバーの DB は全部の行で共有するので、
    /// 行が足したサーバーの子と親は最後に消す（後の行の最初の同期と洗い替えが、親の無い子を降ろさないようにする）。
    /// </remarks>
    private static async Task RunExcludedParentAsync(
        ISyncConfigFixture adapter,
        (int Local, int Server, int Extra) keys,
        object? localKey,
        IServiceProvider provider,
        IServiceProvider serverProvider
    )
    {
        var parent = keys.Local;

        try
        {
            await adapter.InsertAsync(serverProvider, null, parent, Ct);

            await using (var scope = provider.CreateAsyncScope())
            {
                await adapter.InsertAsync(scope.ServiceProvider, localKey, parent, Ct);
                await adapter.UpdateAsync(scope.ServiceProvider, localKey, parent, Ct);
            }

            var direct = await adapter.JournalAsync(provider, localKey, Ct);
            direct
                .Tables.Should()
                .BeEmpty("a direct write of a table excluded at construction is not recorded");
            direct.UnsetKeyEntries.Should().Be(0);

            await using (var scope = provider.CreateAsyncScope())
            {
                await adapter.SaveNewChildAsync(
                    scope.ServiceProvider,
                    localKey,
                    parent,
                    keys.Local,
                    Ct
                );
            }

            var pending = await adapter.JournalAsync(provider, localKey, Ct);
            pending
                .Tables.Should()
                .NotBeEmpty(
                    "the graph save through the excluded parent records the synchronised child it adds"
                )
                .And.OnlyContain(
                    table => table == adapter.ChildTableName,
                    "the rows of the excluded table are not recorded, whichever repository writes them"
                );
            pending.UnsetKeyEntries.Should().Be(0);

            await using (var scope = provider.CreateAsyncScope())
            {
                (await adapter.SyncAsync(scope.ServiceProvider, Ct))
                    .Problem.Should()
                    .BeNull("the upload of a new child has nothing to conflict with");
            }

            (await adapter.ChildExistsAsync(serverProvider, null, keys.Local, Ct))
                .Should()
                .BeTrue("the child added under the excluded parent reaches the server in one sync");
            (await adapter.ChildExistsAsync(provider, localKey, keys.Local, Ct))
                .Should()
                .BeTrue("the sync keeps the recorded child locally");
            (await adapter.JournalAsync(provider, localKey, Ct))
                .Tables.Should()
                .BeEmpty("the sync sent the recorded child and nothing of the excluded table");

            await adapter.InsertChildAsync(serverProvider, null, parent, keys.Server, Ct);

            await using (var scope = provider.CreateAsyncScope())
            {
                var second = await adapter.SyncAsync(scope.ServiceProvider, Ct);
                second
                    .Problem.Should()
                    .BeNull("the download of a new child has nothing to conflict with");
                second
                    .Downloaded.Should()
                    .Be(1, "only the new server child is newer than the mirrored versions");
                (await adapter.ChildExistsAsync(scope.ServiceProvider, localKey, keys.Server, Ct))
                    .Should()
                    .BeTrue("the server new child reaches the local database in one sync");
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                await adapter.RefreshAsync(scope.ServiceProvider, Ct);

                foreach (var id in new[] { keys.Local, keys.Server })
                {
                    (await adapter.ChildExistsAsync(scope.ServiceProvider, localKey, id, Ct))
                        .Should()
                        .BeTrue("a refresh reloads every server row of the synchronised tables");
                }
            }
        }
        finally
        {
            await adapter.DeleteServerRowsAsync(
                serverProvider,
                [keys.Local, keys.Server],
                [],
                [parent],
                CancellationToken.None
            );
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
