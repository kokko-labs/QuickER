using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期の構成の行列（<see cref="SyncConfigDimensions"/>）の行ごとに、生成された DI の登録で同期を組み、新しい行の上げ下げが
/// 1 回の同期で届き、続く洗い替え 1 回で両方の行がローカルにそろうことを確かめる（サーバー＝実 SQL Server・ローカル＝実ファイル SQLite）。
/// </summary>
/// <remarks>
/// <para>
/// 同期の登録は、ローカルの接続ファクトリを開くたびの確認つきの包みに差し替えて新しいエンジン（SyncEngine）を組む。
/// ローカルの書き込みは同期する表のトリガーが記録し、未送信はエンジンの未送信の一覧で読む。トリガーと状態の表は包んだ接続ファクトリの
/// 最初の接続で張られるので、ローカルの DB にはスキーマだけを先に作り、行は包んだ接続（リポジトリ）でだけ書く。
/// </para>
/// <para>
/// サーバーは実 SQL Server（生成された直結の差分ソースは SQL Server の語彙＝<c>MIN_ACTIVE_ROWVERSION()</c> を使うので、
/// SQLite の代役では差分ソースの登録を差し替えることになり、登録の形を確かめられない）。HTTP の行は、フィクスチャごとに
/// 平文の Kestrel のサーバーを 1 つ共有し、HTTP/2 の水準の行は HTTPS で待ち受ける別の Kestrel のサーバー（テストの中で作る
/// 自己署名の証明書 <see cref="TestHttpsCertificate"/>・Kestrel の既定のまま HTTP/1.1 と HTTP/2 を ALPN で交渉する）を 1 つ
/// 共有する（どちらも同じ SQL Server の DB）。行ごとにローカルの DB とコンテナを作り、キーは行ごとに別の値を使う
/// （サーバーの DB は全部の行で共有する）。
/// </para>
/// <para>
/// HTTP/2 の水準の行は、洗い替えの後に、両側で書いた無制限バイナリ列を運ぶ同期を 1 回流し（親の表を除外した行は除く）、
/// 行の終わりに、その行の間に HTTPS のサーバーへ届いた要求がどれも HTTP/2 で、同期の専用のエンドポイント・CRUD と保存の書き込み・
/// blob の PUT と GET を含むことを確かめる（サーバーは共有なので、行の前の要求の数を控えてその後の要求だけを見る）。
/// </para>
/// <para>
/// 既知の失敗は <see cref="SyncConfigDimensions.Known"/>（厳格＝載っていない失敗も、載っているのに起きない失敗も赤）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerSecondContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlServer)]
[RuntimeCell(
    RuntimeFeatures.SyncSupport,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.AdoSqlServer
)]
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

        var servers = new RemoteServers();

        try
        {
            foreach (
                var adapter in SyncConfigFixtures.Axis.Adapters.Where(a => a.HasRemoteServices)
            )
            {
                servers.Plain[adapter.AxisValue] = await adapter.StartRemoteServerAsync(
                    fixture.ConnectionString,
                    Ct
                );

                // HTTP/2 の水準の行が共有する HTTPS のサーバー（Kestrel の既定のまま＝HTTP/1.1 と HTTP/2 を ALPN で交渉する）。
                // 届いた要求の版を記録する
                var requests = new SyncConfigRequestLog();
                servers.Http2Requests[adapter.AxisValue] = requests;
                servers.Http2[adapter.AxisValue] = await adapter.StartRemoteServerAsync(
                    fixture.ConnectionString,
                    Ct,
                    TestHttpsCertificate.UseOn,
                    requests
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
                    // サーバーは行をまたいで共有するので、行の前の件数を控えて、その行の間に届いた要求だけを見る
                    var requestsBefore = c.OverHttp2 ? servers.Http2Requests[c.Fixture].Count : 0;
                    await RunAsync(c, keys, servers);

                    if (c.OverHttp2)
                    {
                        CheckHttp2Requests(c, servers.Http2Requests[c.Fixture], requestsBefore);
                    }
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
            foreach (var server in servers.Plain.Values.Concat(servers.Http2.Values))
            {
                await server.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// HTTP/2 の行の終わりに、その行の間に HTTPS のサーバーへ届いた要求がどれも HTTP/2 で、同期の専用のエンドポイント・
    /// CRUD と保存の書き込み・（無制限バイナリ列を運ぶ同期を流した行では）blob の PUT と GET を含むことを確かめる（食い違えば例外）
    /// </summary>
    private static void CheckHttp2Requests(
        SyncConfigCase c,
        SyncConfigRequestLog log,
        int requestsBefore
    )
    {
        var adapter = SyncConfigFixtures.Axis.Get(c.Fixture);
        var requests = log.Since(requestsBefore);
        var problems = SyncConfigFixtures.Http2Problems(
            requests,
            adapter.RemotePrefix,
            adapter.SyncOperationRoutes,
            adapter.UnboundedBinaryRoute,
            c.SyncsUnboundedBinary
        );

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"requests to the HTTPS server ({requests.Count}): {string.Join("; ", problems)}"
            );
        }
    }

    /// <summary>1 行を流す（届かなければ例外）</summary>
    /// <param name="c">行</param>
    /// <param name="keys">
    /// 行のキー（Local＝ローカルの新しい行・Server＝サーバーの新しい行・Extra＝サーバーだけに置く補助の親。サーバーの DB は全部の行で
    /// 共有するので行ごとに別の値）
    /// </param>
    /// <param name="servers">フィクスチャごとの HTTP のサーバー（平文と HTTPS）</param>
    private async Task RunAsync(
        SyncConfigCase c,
        (int Local, int Server, int Extra) keys,
        RemoteServers servers
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
            c.LocalFactoryAsInstance
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
                        servers.Plain[c.Fixture].BaseAddress(adapter.RemotePrefix)
                    );
                    break;
                case SyncConfigDimensions.HttpClientFactory:
                    // 呼び出し側が持つ HttpClient を渡す形（ファクトリは解決のたびに呼ばれる。クライアントは行の終わりに捨てる）
                    var address = new Uri(
                        servers.Plain[c.Fixture].BaseAddress(adapter.RemotePrefix) + "/"
                    );
                    adapter.AddHttpSources(services, _ => clients.Add(address));
                    break;
                case SyncConfigDimensions.HttpClientFactoryHttp2:
                    // 同じファクトリの形で、HTTPS のサーバーへ、HTTP/2 と RequestVersionOrLower にした HttpClient を渡す
                    // （docs が勧める HTTP/2 の構成。テストの証明書だけを受け入れる）
                    var http2Address = new Uri(
                        servers.Http2[c.Fixture].BaseAddress(adapter.RemotePrefix) + "/"
                    );
                    adapter.AddHttpSources(services, _ => clients.AddHttp2(http2Address));
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
                "the registration wraps the local connection factory, keeps the repositories plain, builds the source of the transport, and the first local connection puts triggers on the synchronised tables only"
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
                .Be(1, "the resume point of each table leaves only the new server row");
            (await adapter.ExistsAsync(scope.ServiceProvider, localKey, keys.Server, Ct))
                .Should()
                .BeTrue("the server new row reaches the local database in one sync");
        }

        // 洗い替え 1 回（サーバーの行で置き換える）が、同じ登録の形で例外なく終わり、両方の行がローカルにそろう
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

        // 無制限バイナリ列の段は、次の段が親を消す前に流す（GraphRemoveParentWithNewChild の段は 2 つの親を消す）
        if (c.SyncsUnboundedBinary)
        {
            await ExchangeUnboundedBinariesAsync(adapter, keys, localKey, provider, serverProvider);
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
    /// HTTP/2 の行の無制限バイナリ列の段：洗い替えでそろった 2 つの親（ローカルとサーバーの両方にある行）のうち、ローカルの新しい行に
    /// ローカルのリポジトリで、サーバーの新しい行にサーバーのリポジトリで、除外した無制限バイナリ列を書き、列を運ぶ同期 1 回で、
    /// それぞれの側が相手の側の書いた内容を持ち、未送信が残らない（列の送信は blob の PUT、写しは blob の GET で HTTP を通る）
    /// </summary>
    /// <remarks>
    /// ローカルの書き込みはストリームのアクセサで、トリガーが列の名前つきで記録する。親の表を構築時に除外した行
    /// （GraphViaExcludedParent）はこの段を流さない：どちらのフィクスチャも除外列を持つのは親の表だけで、その行では同期する表に
    /// 除外列が無い（<see cref="SyncConfigCase.SyncsUnboundedBinary"/>）。
    /// </remarks>
    private static async Task ExchangeUnboundedBinariesAsync(
        ISyncConfigFixture adapter,
        (int Local, int Server, int Extra) keys,
        object? localKey,
        IServiceProvider provider,
        IServiceProvider serverProvider
    )
    {
        byte[] localContent = [0x4C, 0x01, (byte)(keys.Local % 256)];
        byte[] serverContent = [0x53, 0x02, (byte)(keys.Server % 256)];

        await using (var scope = provider.CreateAsyncScope())
        {
            await adapter.WriteUnboundedBinaryAsync(
                scope.ServiceProvider,
                localKey,
                keys.Local,
                localContent,
                Ct
            );
        }

        await adapter.WriteUnboundedBinaryAsync(
            serverProvider,
            null,
            keys.Server,
            serverContent,
            Ct
        );

        await using (var scope = provider.CreateAsyncScope())
        {
            (await adapter.SyncAsync(scope.ServiceProvider, Ct, includeUnboundedBinary: true))
                .Problem.Should()
                .BeNull("each side wrote the binary column of a different row");
        }

        (await adapter.ReadUnboundedBinaryAsync(serverProvider, null, keys.Local, Ct))
            .Should()
            .Equal(localContent, "the sync that carries the binary columns uploads the local one");

        await using (var scope = provider.CreateAsyncScope())
        {
            (
                await adapter.ReadUnboundedBinaryAsync(
                    scope.ServiceProvider,
                    localKey,
                    keys.Server,
                    Ct
                )
            )
                .Should()
                .Equal(
                    serverContent,
                    "the sync that carries the binary columns copies the server one down"
                );
        }

        (await adapter.PendingAsync(provider, Ct))
            .Should()
            .BeEmpty("the sync sent the recorded binary column");
    }

    /// <summary>
    /// 水準 GraphRemoveParentWithNewChild の段：上げ下げした 2 つの親（ローカルとサーバーの両方にある行）に、キーの無い新しい子
    /// （Added）を足して親を消すグラフ保存（単数と複数の SaveAsync）と、新しい子だけを MarkRemoved したグラフ保存が保存され、
    /// 未送信は 2 つの親の削除だけになり、同期 1 回でサーバーから親が消え、未送信が残らない
    /// </summary>
    /// <remarks>
    /// 版なしの子を持つフィクスチャは、消す親の下に、サーバーに同じキーの行がある新しい版なしの子も入れる（サーバーの行は消さない
    /// 補助の親の下に置く）。新しい行は保存側の削除で DB のどの行も消さないのでトリガーが記録せず、後勝ちの同期の無条件の削除が
    /// サーバーの同じキーの行を消さない。同期は版なしの表も流す後勝ちのモードで流す。
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

            var removed = await adapter.PendingAsync(provider, Ct);
            removed
                .Select(row => (row.Table, row.IsDelete))
                .Should()
                .Equal(
                    [(adapter.ParentTableName, true), (adapter.ParentTableName, true)],
                    "only the deletes of the two saved parents are recorded; a new node deletes no row and leaves nothing"
                );

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

            (await adapter.PendingAsync(provider, Ct))
                .Should()
                .BeEmpty("the sync sent every recorded delete");
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
    /// 水準 GraphViaExcludedParent の流れ：親の表を構築時に除外した登録で、除外した親の直接の書き込みは未送信に何も残さず、
    /// 除外した親のリポジトリのグラフ保存で足した同期する子だけが未送信になり（同じ保存が書き換える除外した親の行は残らない）、
    /// 同期 1 回でサーバーへ上がりローカルに残る。続けてサーバーの新しい子を同期 1 回で降ろし、洗い替え 1 回で両方の子がローカルに
    /// そろう
    /// </summary>
    /// <remarks>
    /// エンジンは親の表を降ろさないので、親はテストがサーバーとローカルの両方へ直接入れる（ローカルへはリポジトリ＝包んだ接続で）。
    /// 未送信の一覧はエンジンの表の行だけを返すので、除外した親の表に記録の仕組みが無いことは登録の形の検査（トリガーの付いた表）が
    /// 受け持つ。サーバーの DB は全部の行で共有するので、行が足したサーバーの子と親は最後に消す（後の行の最初の同期と洗い替えが、
    /// 親の無い子を降ろさないようにする）。
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

            (await adapter.PendingAsync(provider, Ct))
                .Should()
                .BeEmpty(
                    "a direct write of a table excluded at construction leaves nothing unsent"
                );

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

            (await adapter.PendingAsync(provider, Ct))
                .Select(row => row.Table)
                .Should()
                .NotBeEmpty(
                    "the graph save through the excluded parent records the synchronised child it adds"
                )
                .And.OnlyContain(
                    table => table == adapter.ChildTableName,
                    "the rows of the excluded table are not recorded, whichever repository writes them"
                );

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
            (await adapter.PendingAsync(provider, Ct))
                .Should()
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
                    .Be(1, "only the new server child is past the resume point of its table");
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

    /// <summary>フィクスチャごとの HTTP のサーバー（全部の行で共有する）</summary>
    private sealed class RemoteServers
    {
        /// <summary>平文の HTTP のサーバー（水準 HttpAddress・HttpClientFactory）</summary>
        public Dictionary<string, InProcessRemoteServer> Plain { get; } =
            new(StringComparer.Ordinal);

        /// <summary>HTTPS のサーバー（水準 HttpClientFactoryHttp2）</summary>
        public Dictionary<string, InProcessRemoteServer> Http2 { get; } =
            new(StringComparer.Ordinal);

        /// <summary>HTTPS のサーバーに届いた要求の記録</summary>
        public Dictionary<string, SyncConfigRequestLog> Http2Requests { get; } =
            new(StringComparer.Ordinal);
    }

    /// <summary>行の中で HttpClient のファクトリが作ったクライアント（行の終わりに捨てる）</summary>
    private sealed class OwnedClients : IDisposable
    {
        private readonly List<HttpClient> _clients = [];

        /// <summary>既定の HttpClient（HTTP/1.1）</summary>
        public HttpClient Add(Uri baseAddress) => Own(new HttpClient { BaseAddress = baseAddress });

        /// <summary>テストの証明書だけを受け入れ、HTTP/2 と RequestVersionOrLower で送る HttpClient</summary>
        public HttpClient AddHttp2(Uri baseAddress) =>
            Own(TestHttpsCertificate.CreateHttp2Client(baseAddress));

        private HttpClient Own(HttpClient client)
        {
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
