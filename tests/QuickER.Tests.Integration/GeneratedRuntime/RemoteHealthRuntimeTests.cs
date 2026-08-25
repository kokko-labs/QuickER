using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedRemoteServiceFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートサービス生成の liveness エンドポイント（<c>GET {prefix}/health</c>）と、
/// クライアント側 <c>HttpRemoteRepository.PingAsync</c> を実 HTTP（Kestrel を 127.0.0.1 の空きポートで起動）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 起動待ちの口が無かったため、サンプルや CI が「全件 SELECT が通るか」で代用していたのを解消したもの。
/// liveness は DB を触らないことが要件なので、本スイートは<b>スキーマを作らないまま</b>サーバーを起動する
/// （それでも 200 が返ることが「health は DB に依存しない」の実証になる）。
/// </para>
/// <para>
/// ルートは公開定数 <c>RemotePaths</c> から組み立て、テスト側にもリテラルを置かない。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteHealthRuntimeTests : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバー側リポジトリの登録先となる一時ファイル DB（スキーマは作らない）</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>in-process 起動した Kestrel サーバー</summary>
    private InProcessRemoteServer _server = null!;

    /// <summary>プレフィックスまで含むクライアントのベースアドレス</summary>
    private string _baseAddress = string.Empty;

    /// <summary>Kestrel を空きポートで起動する（スキーマ作成はしない＝health が DB に触らないことの前提）</summary>
    public async ValueTask InitializeAsync()
    {
        _server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );

        _baseAddress = _server.BaseAddress(RemotePaths.DefaultPrefix);
    }

    /// <summary>サーバーと一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        _db.Dispose();
    }

    /// <summary>生成クライアント（BaseAddress は末尾スラッシュ必須）を組み立てる</summary>
    private HttpCustomerRemoteRepository CreateClient(string baseAddress) =>
        new(new HttpClient { BaseAddress = new Uri($"{baseAddress}/") });

    /// <summary>health エンドポイントが 200 を返す（本文は空・DB 未作成でも成立する）</summary>
    [Fact(DisplayName = "[Remote health] GET {prefix}/health が 200 を返す")]
    public async Task Health_ReturnsOk()
    {
        using var http = new HttpClient();

        using var response = await http.GetAsync($"{_baseAddress}/{RemotePaths.HealthRoute}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().BeEmpty();
    }

    /// <summary>起動中のサーバーに対して <c>PingAsync</c> が true を返す</summary>
    [Fact(DisplayName = "[Remote health] 起動中のサーバーへ PingAsync が true を返す")]
    public async Task PingAsync_ReturnsTrue_WhenServerIsUp()
    {
        var client = CreateClient(_baseAddress);

        (await client.PingAsync(Ct)).Should().BeTrue();
    }

    /// <summary>停止したサーバーに対しては例外を投げず false を返す（起動待ちループで使える形）</summary>
    [Fact(DisplayName = "[Remote health] 停止後の PingAsync は例外でなく false を返す")]
    public async Task PingAsync_ReturnsFalse_WhenServerIsDown()
    {
        var client = CreateClient(_baseAddress);
        (await client.PingAsync(Ct)).Should().BeTrue();

        await _server.StopAsync(Ct);

        // 接続拒否（HttpRequestException）を握って false（起動待ちループの条件に使える）
        (await client.PingAsync(Ct))
            .Should()
            .BeFalse();
    }
}
