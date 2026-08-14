using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値オブジェクト併用時（<c>GenerateValueObjects=true</c>）、応答本文の値が VO の検証に落ちるケースも
/// リモートクライアントが <c>RemoteRepositoryException</c> へ分類することを実 HTTP で検証する。
/// </summary>
/// <remarks>
/// <para>
/// <c>ValueObjectJsonConverter.Read</c> は <c>TVo.Create(value)</c> を呼ぶため、検証違反は
/// <c>JsonException</c> ではなく <c>ValueObjectValidationException</c> で表に出る。分類フィルタが
/// これを素通りさせると、他のリモート失敗と同じ <c>catch</c> で拾えない例外が呼び出し側へ抜ける
/// （サーバー受信側は広い catch で 400 に畳んでいるので、非対称でもあった）。
/// </para>
/// <para>
/// サーバーは生成エンドポイントを張らず、同じルートへ「列の宣言長を超える名前」を含む 200 応答を返すだけの
/// 偽物を立てる（DB 不要＝Docker 不要の CI 常時実行）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteValueObjectBodyRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>生成クライアントが GetAll で叩くルート（POST {prefix}/{エンティティ}/{操作}）</summary>
    private const string GetAllRoute = "/quicker/Gadget/GetAll";

    /// <summary>指定した本文を 200＋application/json で返すだけのサーバーを立て、生成クライアントを繋ぐ</summary>
    private static async Task<(
        InProcessRemoteServer Server,
        ServiceProvider Clients
    )> StartFakeAsync(string body)
    {
        var server = await InProcessRemoteServer.StartAsync(
            _ => { },
            app =>
                app.MapPost(
                    GetAllRoute,
                    async (HttpContext context) =>
                    {
                        context.Response.ContentType = "application/json";
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
        DisplayName = "[Remote/VO 本文] 2xx でも VO 検証に落ちる値は RemoteRepositoryException になる"
    )]
    public async Task SuccessWithInvalidValueObject_ThrowsRemoteRepositoryException()
    {
        // name 列の宣言長は 50。読み取り時の NameValue.Create がここで検証違反になる
        var tooLong = new string('x', 60);
        var (server, clients) = await StartFakeAsync($$"""[{"GadgetId":1,"Name":"{{tooLong}}"}]""");

        await using (server)
        using (clients)
        {
            var gadgets = clients.GetRequiredService<IGadgetRemoteRepository>();

            var act = async () => await gadgets.GetAllAsync(Ct);

            var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
            thrown.StatusCode.Should().Be(200, "分類には応答のステータスをそのまま添える");
            thrown
                .InnerException.Should()
                .BeOfType<ValueObjectValidationException>(
                    "分類しても元の失敗そのものは診断材料として残す"
                );
        }
    }

    [Fact(DisplayName = "[Remote/VO 本文] 対照: 制約を満たす値は従来どおり解釈される")]
    public async Task SuccessWithValidValueObject_IsDeserialized()
    {
        var (server, clients) = await StartFakeAsync("""[{"GadgetId":7,"Name":"alpha"}]""");

        await using (server)
        using (clients)
        {
            var gadgets = clients.GetRequiredService<IGadgetRemoteRepository>();

            var all = await gadgets.GetAllAsync(Ct);

            all.Should().ContainSingle().Which.Name.Value.Should().Be("alpha");
        }
    }
}
