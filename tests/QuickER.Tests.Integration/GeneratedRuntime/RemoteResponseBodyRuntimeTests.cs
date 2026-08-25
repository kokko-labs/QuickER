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

    /// <summary>指定した本文・Content-Type・ステータスを返すだけのサーバーを立て、生成クライアントを繋ぐ</summary>
    private static async Task<(
        InProcessRemoteServer Server,
        ServiceProvider Clients
    )> StartFakeAsync(string body, string contentType, int statusCode = StatusCodes.Status200OK)
    {
        var server = await InProcessRemoteServer.StartAsync(
            _ => { },
            app =>
                app.MapPost(
                    GetAllRoute,
                    async (HttpContext context) =>
                    {
                        context.Response.StatusCode = statusCode;
                        context.Response.ContentType = contentType;
                        await context.Response.WriteAsync(body, context.RequestAborted);
                    }
                ),
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

            var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
            thrown.StatusCode.Should().Be(200, "分類には応答のステータスをそのまま添える");
            thrown
                .InnerException.Should()
                .NotBeNull(
                    "本文が読めなかった理由そのものが「宛先が違う」と「形が違う」を見分ける材料になる"
                );
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

            var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
            thrown.InnerException.Should().BeOfType<System.Text.Json.JsonException>();
        }
    }

    [Fact(DisplayName = "[Remote/応答本文] 失敗応答の本文が読めなかった理由も inner として残る")]
    public async Task FailureWithNonJsonBody_KeepsParseFailureAsInnerException()
    {
        // 502＋HTML＝プロキシやゲートウェイが自分で返した失敗応答（RemoteError ではない）
        var (server, clients) = await StartFakeAsync(
            "<html><body>Bad gateway</body></html>",
            "text/html",
            StatusCodes.Status502BadGateway
        );

        await using (server)
        using (clients)
        {
            var documents = clients.GetRequiredService<IDocumentRemoteRepository>();

            var act = async () => await documents.GetAllAsync(Ct);

            var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
            thrown.StatusCode.Should().Be(502);

            // 「本文が RemoteError ではなかった」のか「message が空の RemoteError だった」のかは、
            // 読めなかった理由が残っていて初めて呼び出し側から区別できる
            var inner = thrown.InnerException;
            inner.Should().NotBeNull();

            // 読めなかった理由（JSON として壊れている／そもそも JSON でない）がそのまま残る
            (inner is System.Text.Json.JsonException or NotSupportedException)
                .Should()
                .BeTrue($"本文を読めなかった失敗そのものを保つ（実際: {inner!.GetType().Name}）");
        }
    }

    [Fact(DisplayName = "[Remote/応答本文] 対照: RemoteError の失敗応答には inner が付かない")]
    public async Task FailureWithRemoteErrorBody_HasNoInnerException()
    {
        var (server, clients) = await StartFakeAsync(
            """{"Type":"Error","Message":"boom"}""",
            "application/json",
            StatusCodes.Status500InternalServerError
        );

        await using (server)
        using (clients)
        {
            var documents = clients.GetRequiredService<IDocumentRemoteRepository>();

            var act = async () => await documents.GetAllAsync(Ct);

            var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
            thrown.Message.Should().Be("boom");
            thrown.InnerException.Should().BeNull("本文は読めているので保全すべき失敗が無い");
        }
    }

    [Fact(
        DisplayName = "[Remote/応答本文] 例外の追加コンストラクタは inner を保ったまま既存プロパティを埋める"
    )]
    public void ExceptionConstructors_PreserveInnerException()
    {
        var cause = new InvalidOperationException("cause");

        var remote = new RemoteRepositoryException(500, "message", "trace-1", cause);
        remote.StatusCode.Should().Be(500);
        remote.CorrelationId.Should().Be("trace-1");
        remote.InnerException.Should().BeSameAs(cause);

        // 3 引数形は inner を取らない（追加的な変更のため）
        new RemoteRepositoryException(500, "message")
            .InnerException.Should()
            .BeNull();

        var conflict = new SaveConflictException("message", cause);
        conflict.InnerException.Should().BeSameAs(cause);
        conflict
            .Reason.Should()
            .Be(SaveConflictReason.Unknown, "詳細を伴わない構築なので分類は Unknown のまま");

        new SaveConflictException("message").InnerException.Should().BeNull();
    }

    [Fact(DisplayName = "[Remote/応答本文] 対照: 正しい JSON 本文は解釈される")]
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
