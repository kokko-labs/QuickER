using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickER.Tests.GeneratedRemoteServiceFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <c>MapGeneratedRemoteEndpoints</c> の必須引数 <c>RemoteAccess</c> の実挙動を実 HTTP（in-process Kestrel）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 生成エンドポイントは全行の読み書き削除を受け付けるため、認可を要求するかどうかは既定値でなく
/// 呼び出し側の明示選択（<c>RequireAuthorization</c> / <c>AllowAnonymous</c>）にしている。ここでは
/// (1) <c>RequireAuthorization</c> がグループ全体（health 含む）へ既定認可ポリシーを実際に効かせること、
/// (2) <c>AllowAnonymous</c> が<b>メタデータを一切付けない</b>こと（ホスト側の FallbackPolicy を
/// <c>[AllowAnonymous]</c> で弱体化させない＝FallbackPolicy 構成下では 401 になる）、
/// (3) 未定義値がマップ時に fail-fast すること、を固定する。
/// </para>
/// <para>
/// 認証はテスト専用の固定ヘッダースキーム（<c>X-Test-Auth: valid</c> で認証成立）で構成する。
/// スキーマは作らない＝health と GetById（該当行なし＝null）だけで完結させ、DB 準備を持ち込まない。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteAccessRuntimeTests
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>認証成立の合図となるリクエストヘッダー名</summary>
    private const string AuthHeader = "X-Test-Auth";

    /// <summary>固定ヘッダーで認証するテスト専用スキーム（値が valid なら認証成立・無ければ未認証）</summary>
    private sealed class StubAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        /// <summary>スキーム名（登録と既定スキーム指定で共有する。基底の Scheme プロパティと衝突しない別名）</summary>
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (
                !Request.Headers.TryGetValue(AuthHeader, out var value)
                || !string.Equals(value, "valid", StringComparison.Ordinal)
            )
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "tester")],
                Options.ClaimsIssuer ?? SchemeName
            );

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)
                )
            );
        }
    }

    /// <summary>テスト認証スキーム＋認可サービスを登録する（WebApplication が認証・認可ミドルウェアを自動挿入する）</summary>
    private static void AddStubAuthentication(IServiceCollection services)
    {
        services
            .AddAuthentication(StubAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>(
                StubAuthHandler.SchemeName,
                null
            );
        services.AddAuthorization();
    }

    /// <summary>認証ヘッダー付き（withAuth=true）または無しの HttpClient を組み立てる</summary>
    private static HttpClient CreateHttp(bool withAuth)
    {
        var http = new HttpClient();

        if (withAuth)
        {
            http.DefaultRequestHeaders.Add(AuthHeader, "valid");
        }

        return http;
    }

    [Fact(
        DisplayName = "[RemoteAccess] RequireAuthorization は未認証の health を 401 で拒み、認証済みなら 200 を返す"
    )]
    public async Task RequireAuthorization_GuardsWholeGroup()
    {
        using var db = SqliteTempDatabase.Create();
        await using var server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqliteRepositories(db.ReadWriteCreateConnectionString);
                AddStubAuthentication(services);
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.RequireAuthorization),
            Ct
        );
        var healthUrl =
            $"{server.BaseAddress(RemotePaths.DefaultPrefix)}/{RemotePaths.HealthRoute}";

        using (var anonymous = CreateHttp(withAuth: false))
        using (var response = await anonymous.GetAsync(healthUrl, Ct))
        {
            // health もグループの一員＝グループ規約の認可が等しく効く（生成 XmlDoc の宣言どおり）
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var authenticated = CreateHttp(withAuth: true))
        using (var response = await authenticated.GetAsync(healthUrl, Ct))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact(
        DisplayName = "[RemoteAccess] RequireAuthorization 下でも認証済みクライアントは操作エンドポイントへ到達できる"
    )]
    public async Task RequireAuthorization_AllowsAuthenticatedOperations()
    {
        using var db = SqliteTempDatabase.Create();
        await db.ApplyDdlAsync(RemoteServiceFixtureDefinition.Build(), Ct);
        await using var server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqliteRepositories(db.ReadWriteCreateConnectionString);
                AddStubAuthentication(services);
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.RequireAuthorization),
            Ct
        );

        var client = new HttpCustomerRemoteRepository(
            new HttpClient(new HttpClientHandler(), disposeHandler: true)
            {
                BaseAddress = new Uri($"{server.BaseAddress(RemotePaths.DefaultPrefix)}/"),
                DefaultRequestHeaders = { { AuthHeader, "valid" } },
            }
        );

        // 該当行なしの null＝操作エンドポイントまで到達して 200 が返った証拠（401 なら例外になる）
        (await client.GetByIdAsync(CustomerIdValue.Create(1), Ct))
            .Should()
            .BeNull();
    }

    [Fact(
        DisplayName = "[RemoteAccess] AllowAnonymous はメタデータを付けない＝FallbackPolicy を弱体化させない"
    )]
    public async Task AllowAnonymous_DoesNotWeakenFallbackPolicy()
    {
        using var db = SqliteTempDatabase.Create();
        await using var server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqliteRepositories(db.ReadWriteCreateConnectionString);
                AddStubAuthentication(services);
                // ホストが「全エンドポイント既定で認証必須」を敷く構成。AllowAnonymous が
                // [AllowAnonymous] メタデータを付けてしまうと、この網から生成面だけが抜け落ちる
                services.Configure<AuthorizationOptions>(options =>
                    options.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build()
                );
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );
        var healthUrl =
            $"{server.BaseAddress(RemotePaths.DefaultPrefix)}/{RemotePaths.HealthRoute}";

        using (var anonymous = CreateHttp(withAuth: false))
        using (var response = await anonymous.GetAsync(healthUrl, Ct))
        {
            // FallbackPolicy がそのまま効く＝AllowAnonymous は「開放を強制」でなく「何も要求しない」
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var authenticated = CreateHttp(withAuth: true))
        using (var response = await authenticated.GetAsync(healthUrl, Ct))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact(
        DisplayName = "[RemoteAccess] 未定義値はマップ時に ArgumentOutOfRangeException で fail-fast する"
    )]
    public async Task UndefinedValue_FailsFastAtMappingTime()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();

        // キャスト経由の未定義値が黙って「開放面をマップした」ことにならない（ConcurrencyMode と同じ流儀）
        var act = () => app.MapGeneratedRemoteEndpoints((RemoteAccess)99);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("access");
    }
}
