using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedRemoteServiceFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// HTTP クライアントの keyed DI 登録（<c>AddGeneratedHttpRemoteRepositories(serviceKey, ...)</c>）を、
/// 実 HTTP（127.0.0.1 の空きポートで起動した Kestrel 2 台）と実 SQLite で検証する（Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 狙いはハイブリッド構成の骨格＝「同一契約型をキーで書き分ける」こと。ここでは差が見えるように 2 台のサーバーを
/// 別々の DB に向けて立て、キー <c>"server"</c> と <c>"local"</c> で登録したクライアントがそれぞれ自分のサーバーへ
/// 届くこと（相互汚染がないこと）を往復で確かめる。実運用では片方を方言エンジン
/// （<c>AddGeneratedSqliteRepositories(serviceKey, ...)</c>）に差し替えるだけで、解決規則は同じになる。
/// </para>
/// <para>
/// あわせて DI の解決規則そのもの（keyed と非 keyed は互いを見ない・同一キーの二重登録は後勝ち）も必要最小で固定する。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteKeyedRegistrationRuntimeTests : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>"server" 側のサーバーが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _serverDb = SqliteTempDatabase.Create();

    /// <summary>"local" 側のサーバーが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _localDb = SqliteTempDatabase.Create();

    private InProcessRemoteServer _serverSide = null!;
    private InProcessRemoteServer _localSide = null!;

    /// <summary>2 台のサーバーを別々の DB へ向けて起動する</summary>
    public async ValueTask InitializeAsync()
    {
        await _serverDb.ApplyDdlAsync(RemoteServiceFixtureDefinition.Build(), Ct);
        await _localDb.ApplyDdlAsync(RemoteServiceFixtureDefinition.Build(), Ct);

        _serverSide = await StartAsync(_serverDb);
        _localSide = await StartAsync(_localDb);
    }

    /// <summary>サーバーと一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        await _serverSide.DisposeAsync();
        await _localSide.DisposeAsync();
        _serverDb.Dispose();
        _localDb.Dispose();
    }

    /// <summary>指定 DB を実体とする生成エンドポイントを空きポートで公開する</summary>
    private static Task<InProcessRemoteServer> StartAsync(SqliteTempDatabase db) =>
        InProcessRemoteServer.StartAsync(
            services => services.AddGeneratedSqliteRepositories(db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(),
            Ct
        );

    /// <summary>2 つのベースアドレスをキー付きで登録した ServiceProvider を組む</summary>
    private ServiceProvider BuildKeyedProvider() =>
        new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(
                "server",
                _serverSide.BaseAddress(RemotePaths.DefaultPrefix)
            )
            .AddGeneratedHttpRemoteRepositories(
                "local",
                _localSide.BaseAddress(RemotePaths.DefaultPrefix)
            )
            .BuildServiceProvider();

    /// <summary>顧客エンティティを組み立てる</summary>
    private static CustomerEntity NewCustomer(int id, string name) =>
        new() { CustomerId = CustomerIdValue.Create(id), Name = NameValue.Create(name) };

    /// <summary>
    /// キー別に解決した 2 つの <see cref="ICustomerRemoteRepository"/> が、それぞれのサーバー（＝別 DB）へ届く。
    /// </summary>
    [Fact(
        DisplayName = "[RemoteKeyed/CI] キー別のクライアントがそれぞれのサーバーへ届く（相互汚染なし）"
    )]
    public async Task KeyedClients_ReachTheirOwnServer()
    {
        using var provider = BuildKeyedProvider();

        var server = provider.GetRequiredKeyedService<ICustomerRemoteRepository>("server");
        var local = provider.GetRequiredKeyedService<ICustomerRemoteRepository>("local");

        server.Should().NotBeSameAs(local);

        await server.InsertAsync(NewCustomer(1, "OnServer"), Ct);
        await local.InsertAsync(NewCustomer(2, "OnLocal"), Ct);

        var fromServer = await server.GetAllAsync(Ct);
        var fromLocal = await local.GetAllAsync(Ct);

        fromServer.Should().ContainSingle().Which.Name!.Value.Should().Be("OnServer");
        fromLocal.Should().ContainSingle().Which.Name!.Value.Should().Be("OnLocal");

        // それぞれの相手側の行は見えない（＝別の HttpClient・別のベースアドレスが効いている）
        (await server.GetByIdAsync(CustomerIdValue.Create(2), Ct))
            .Should()
            .BeNull();
        (await local.GetByIdAsync(CustomerIdValue.Create(1), Ct)).Should().BeNull();
    }

    /// <summary>
    /// keyed 登録は非 keyed の解決に応えない（キーを付け忘れた解決が黙ってどちらかへ流れない）。
    /// </summary>
    [Fact(DisplayName = "[RemoteKeyed/CI] keyed 登録だけでは非 keyed 解決に応えない")]
    public void KeyedRegistration_DoesNotAnswerPlainResolution()
    {
        using var provider = BuildKeyedProvider();

        provider
            .GetService<ICustomerRemoteRepository>()
            .Should()
            .BeNull("keyed 登録は GetRequiredKeyedService 専用で、非 keyed 解決とは別の名簿になる");

        // 別のエンティティのリモート面も同じ規則で登録されている
        provider.GetRequiredKeyedService<IOrderRemoteRepository>("server").Should().NotBeNull();
        provider.GetService<IOrderRemoteRepository>().Should().BeNull();
    }

    /// <summary>同一キーへ 2 回登録すると後勝ちになる（DI の既定規則がそのまま出る）</summary>
    [Fact(DisplayName = "[RemoteKeyed/CI] 同一キーへの二重登録は後勝ち")]
    public async Task SameKeyRegisteredTwice_LastOneWins()
    {
        using var provider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(
                "shared",
                _serverSide.BaseAddress(RemotePaths.DefaultPrefix)
            )
            .AddGeneratedHttpRemoteRepositories(
                "shared",
                _localSide.BaseAddress(RemotePaths.DefaultPrefix)
            )
            .BuildServiceProvider();

        var customers = provider.GetRequiredKeyedService<ICustomerRemoteRepository>("shared");
        await customers.InsertAsync(NewCustomer(7, "LastWins"), Ct);

        // 後から登録した "local" 側の DB へ入る
        var viaLocal = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(
                "only",
                _localSide.BaseAddress(RemotePaths.DefaultPrefix)
            )
            .BuildServiceProvider();

        using (viaLocal)
        {
            var check = viaLocal.GetRequiredKeyedService<ICustomerRemoteRepository>("only");
            (await check.GetByIdAsync(CustomerIdValue.Create(7), Ct))
                .Should()
                .NotBeNull("後に登録したベースアドレスのサーバーが応答する");
        }
    }

    /// <summary>
    /// キー付きで作った共有 HttpClient がコンテナ所有のまま（プロバイダ破棄で破棄される）であることを確認する。
    /// </summary>
    /// <remarks>
    /// 非 keyed 版と同じ所有契約。破棄済みプロバイダから解決済みのクライアントを使うと
    /// <see cref="ObjectDisposedException"/> になる＝コンテナが実際に破棄している証拠になる。
    /// </remarks>
    [Fact(DisplayName = "[RemoteKeyed/CI] キー付きの共有 HttpClient もコンテナが破棄する")]
    public async Task KeyedSharedHttpClient_IsOwnedByTheContainer()
    {
        var provider = BuildKeyedProvider();
        var customers = provider.GetRequiredKeyedService<ICustomerRemoteRepository>("server");

        // 破棄前は普通に使える
        await customers.GetAllAsync(Ct);

        await provider.DisposeAsync();

        var act = async () => await customers.GetAllAsync(Ct);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
