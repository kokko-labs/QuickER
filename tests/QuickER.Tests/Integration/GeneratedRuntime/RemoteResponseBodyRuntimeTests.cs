using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートクライアントが「成功ステータスなのに本文が期待した JSON でない」応答を、
/// <c>RemoteRepositoryException</c> として分類することを実 HTTP で検証する。
/// </summary>
/// <remarks>
/// <para>
/// このとき応答しているのは生成エンドポイントではない別物（リバースプロキシやポータルのログイン画面、
/// 認証ゲートウェイの 200 応答など）であり、失敗の実体は「宛先が違う」という転送の問題である。素の
/// <c>JsonException</c> を通すとそれが解析の詳細として現れ、呼び出し側は他のリモート失敗と同じ
/// <c>catch</c> で拾えなくなる（失敗応答の側は元から <c>RemoteRepositoryException</c> へ畳んでいる）。
/// </para>
/// <para>
/// サーバーは生成エンドポイントを張らず、同じルートへ 200＋<c>text/html</c> を返すだけの偽物を立てる
/// （DB も不要＝Docker 不要の CI 常時実行）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteResponseBodyRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>生成クライアントが GetAll で叩くルート（POST {prefix}/{エンティティ}/{操作}）</summary>
    private const string GetAllRoute = "/quicker/Document/GetAll";

    /// <summary>指定した本文・Content-Type を 200 で返すだけのサーバーを立て、生成クライアントを繋ぐ</summary>
    private static async Task<(
        InProcessRemoteServer Server,
        ServiceProvider Clients
    )> StartFakeAsync(string body, string contentType)
    {
        var server = await InProcessRemoteServer.StartAsync(
            _ => { },
            app => app.MapPost(GetAllRoute, () => Results.Content(body, contentType)),
            Ct
        );

        var clients = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();

        return (server, clients);
    }

    [Fact(
        DisplayName = "[Remote/応答本文] 2xx でも JSON でない本文は RemoteRepositoryException になる"
    )]
    public async Task SuccessWithNonJsonBody_ThrowsRemoteRepositoryException()
    {
        var (server, clients) = await StartFakeAsync(
            "<html><body>Sign in to continue</body></html>",
            "text/html"
        );

        await using (server)
        using (clients)
        {
            var documents = clients.GetRequiredService<IDocumentRemoteRepository>();

            var act = async () => await documents.GetAllAsync(Ct);

            (await act.Should().ThrowAsync<RemoteRepositoryException>())
                .Which.StatusCode.Should()
                .Be(200, "分類には応答のステータスをそのまま添える");
        }
    }

    [Fact(
        DisplayName = "[Remote/応答本文] 2xx でも壊れた JSON は RemoteRepositoryException になる"
    )]
    public async Task SuccessWithMalformedJson_ThrowsRemoteRepositoryException()
    {
        var (server, clients) = await StartFakeAsync("[{\"DocumentId\":", "application/json");

        await using (server)
        using (clients)
        {
            var documents = clients.GetRequiredService<IDocumentRemoteRepository>();

            var act = async () => await documents.GetAllAsync(Ct);

            await act.Should().ThrowAsync<RemoteRepositoryException>();
        }
    }

    [Fact(DisplayName = "[Remote/応答本文] 対照: 正しい JSON 本文は従来どおり解釈される")]
    public async Task SuccessWithValidJson_IsDeserialized()
    {
        var (server, clients) = await StartFakeAsync(
            """[{"DocumentId":7,"Title":"alpha"}]""",
            "application/json"
        );

        await using (server)
        using (clients)
        {
            var documents = clients.GetRequiredService<IDocumentRemoteRepository>();

            var all = await documents.GetAllAsync(Ct);

            all.Should().ContainSingle().Which.DocumentId.Should().Be(7);
        }
    }
}
