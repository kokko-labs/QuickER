using System;
using System.IO;
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
/// 生成クライアントのバイナリ転送が、生成エンドポイントの応答の形（GET＝200＋<c>application/octet-stream</c>・
/// PUT / DELETE＝204）のときだけ成功にし、それ以外の 2xx を <see cref="RemoteRepositoryException"/> にすることを固定する。
/// </summary>
/// <remarks>
/// 生成エンドポイントの手前で答える別物（セッション切れでログインページへ転送する認証プロキシ・何にでも 200 を返す
/// 中継）の 2xx を成功と読むと、GET は HTML を blob として書き出して true、PUT / DELETE は何も保存されていないのに true になる。
/// JSON の経路は期待した本文でない成功応答を失敗にしており、バイナリの経路もそろえる。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BinaryTransferResponseRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>生成エンドポイントの手前で答える中継の振る舞い</summary>
    public enum Proxy
    {
        /// <summary>API への要求をすべてログインページ（200・text/html）へ転送する（セッション切れの認証プロキシ）</summary>
        RedirectToLoginPage,

        /// <summary>API への要求にすべて 200・application/octet-stream・本文なしで答える</summary>
        AlwaysOkOctetStream,
    }

    /// <summary>中継だけを立てたサーバーと、それへ向けた生成クライアントで本体を走らせる</summary>
    private static async Task WithProxyAsync(
        Proxy proxy,
        Func<IDocumentRemoteRepository, Task> body
    )
    {
        await using var server = await InProcessRemoteServer.StartAsync(
            _ => { },
            app =>
                app.Use(
                    async (context, next) =>
                    {
                        if (context.Request.Path.StartsWithSegments(RemotePaths.DefaultPrefix))
                        {
                            if (proxy == Proxy.RedirectToLoginPage)
                            {
                                context.Response.Redirect("/login");
                            }
                            else
                            {
                                context.Response.StatusCode = StatusCodes.Status200OK;
                                context.Response.ContentType = "application/octet-stream";
                            }

                            return;
                        }

                        if (context.Request.Path.StartsWithSegments("/login"))
                        {
                            context.Response.ContentType = "text/html";
                            await context.Response.WriteAsync("<html>Sign in</html>");
                            return;
                        }

                        await next();
                    }
                ),
            Ct
        );
        await using var client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();

        await body(client.GetRequiredService<IDocumentRemoteRepository>());
    }

    [Fact(
        DisplayName = "[Binary/Remote] 転送するプロキシの後ろでは、JSON の操作は失敗になる（対照）"
    )]
    public Task Json_BehindRedirect_IsFailure() =>
        WithProxyAsync(
            Proxy.RedirectToLoginPage,
            async documents =>
            {
                var act = () => documents.GetByIdAsync(1, Ct);
                await act.Should().ThrowAsync<RemoteRepositoryException>();
            }
        );

    [Fact(
        DisplayName = "[Binary/Remote] ログインページへ転送されたダウンロードは、HTML を書き出さずに RemoteRepositoryException になる"
    )]
    public Task Download_BehindRedirect_ThrowsAndWritesNothing() =>
        WithProxyAsync(
            Proxy.RedirectToLoginPage,
            async documents =>
            {
                var destination = new MemoryStream();

                var act = () => documents.ReadPayloadAsync(1, destination, Ct);

                (await act.Should().ThrowAsync<RemoteRepositoryException>())
                    .Which.Message.Should()
                    .Contain("text/html");
                destination
                    .Length.Should()
                    .Be(0, "生成エンドポイントの応答でない本文を blob として書き出さない");
            }
        );

    [Theory(
        DisplayName = "[Binary/Remote] 204 でない 2xx のアップロードは、保存されたとみなさず RemoteRepositoryException になる"
    )]
    [InlineData(Proxy.RedirectToLoginPage)]
    [InlineData(Proxy.AlwaysOkOctetStream)]
    public Task Upload_WithoutNoContent_Throws(Proxy proxy) =>
        WithProxyAsync(
            proxy,
            async documents =>
            {
                var act = () =>
                    documents.WritePayloadAsync(
                        1,
                        new MemoryStream([1, 2, 3]),
                        cancellationToken: Ct
                    );

                await act.Should().ThrowAsync<RemoteRepositoryException>();
            }
        );

    [Theory(
        DisplayName = "[Binary/Remote] 204 でない 2xx の NULL 化（DELETE）は、保存されたとみなさず RemoteRepositoryException になる"
    )]
    [InlineData(Proxy.RedirectToLoginPage)]
    [InlineData(Proxy.AlwaysOkOctetStream)]
    public Task SetNull_WithoutNoContent_Throws(Proxy proxy) =>
        WithProxyAsync(
            proxy,
            async documents =>
            {
                var act = () => documents.WritePayloadAsync(1, null, cancellationToken: Ct);

                await act.Should().ThrowAsync<RemoteRepositoryException>();
            }
        );
}
