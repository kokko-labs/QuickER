using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートサーバーの HTTP 500 応答が、既定では内部例外メッセージを公開せず
/// （汎用文言＋相関 ID）、<c>exposeErrorDetails: true</c> のときだけ従来どおり透過することを実 HTTP で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 500 の本文は「サーバー内部で何が起きたか」そのもの（テーブル名・列名・接続文字列・ファイルパス）を運びうるため、
/// 既定は非公開とし、代わりに <c>HttpContext.TraceIdentifier</c> を相関 ID として返す。サーバーログは
/// 従来どおりスタックトレース込みの完全な詳細を、同じ相関 ID を添えて出す＝利用者はエラー報告の ID と
/// サーバーログを突き合わせられる。ここではその突き合わせが実際に成立することまで（テスト用ロガーで捕捉して）固定する。
/// </para>
/// <para>
/// スイッチは 500 経路だけに効く。400（リクエスト解釈の失敗）と 409（楽観排他の競合）は自前の分類文言・
/// 再取得リトライの材料であって内部情報ではないため、非公開時も従来どおり透過する（テスト 3 が対照）。
/// </para>
/// <para>
/// フィクスチャは BinaryFixture（除外バイナリ列あり＝JSON エンドポイントとバイナリストリーミング
/// エンドポイントの両方を持つ）で、サーバー実体はQuickER の SqliteRepository・実ファイル DB＝Docker 不要の CI 常時実行。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteErrorDetailRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>非公開時にクライアントへ返る固定文言（生成テンプレートの GenericServerErrorMessage と同一）</summary>
    private const string GenericMessage = "An unexpected error occurred on the server.";

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>既定（非公開）でマップしたサーバー</summary>
    private InProcessRemoteServer? _hidden;

    /// <summary>exposeErrorDetails: true でマップしたサーバー</summary>
    private InProcessRemoteServer? _exposed;

    /// <summary>非公開サーバーのログを捕捉するシンク（相関 ID の突き合わせ検証に使う）</summary>
    private readonly CapturingLoggerProvider _hiddenLog = new();

    private ServiceProvider? _hiddenClients;

    /// <summary>スキーマ作成 → 非公開／公開の 2 サーバーを空きポートで起動 → 非公開側のクライアント DI を構築する</summary>
    public async ValueTask InitializeAsync()
    {
        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);

        _hidden = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(),
            Ct,
            builder => builder.Logging.AddProvider(_hiddenLog)
        );

        _exposed = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(exposeErrorDetails: true),
            Ct
        );

        _hiddenClients = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_hidden.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        _hiddenClients?.Dispose();

        if (_hidden is not null)
        {
            await _hidden.DisposeAsync();
        }

        if (_exposed is not null)
        {
            await _exposed.DisposeAsync();
        }

        _db.Dispose();
    }

    /// <summary>非公開サーバーに対するクライアント側の文書リモート面</summary>
    private IDocumentRemoteRepository HiddenDocuments =>
        _hiddenClients!.GetRequiredService<IDocumentRemoteRepository>();

    /// <summary>主キー重複の挿入（SQLite の UNIQUE 制約違反＝サーバー側の一般例外→ 500）を素の HTTP で起こす</summary>
    private static async Task<HttpResponseMessage> PostDuplicateInsertAsync(string baseUrl)
    {
        using var raw = new HttpClient();
        using var content = new StringContent(
            """{"Entity":{"DocumentId":1,"Title":"alpha","Thumb":""}}""",
            System.Text.Encoding.UTF8,
            "application/json"
        );

        // 1 件目は成功する（応答は捨てる）ので、同じボディをもう一度送って UNIQUE 制約違反を起こす
        using (var seed = await raw.PostAsync($"{baseUrl}/quicker/Document/Insert", content, Ct))
        {
            seed.IsSuccessStatusCode.Should().BeTrue("1 件目の挿入は成功する前提");
        }

        using var duplicate = new StringContent(
            """{"Entity":{"DocumentId":1,"Title":"alpha","Thumb":""}}""",
            System.Text.Encoding.UTF8,
            "application/json"
        );
        return await raw.PostAsync($"{baseUrl}/quicker/Document/Insert", duplicate, Ct);
    }

    /// <summary>
    /// 1. 既定（引数なし）では 500 の本文が汎用文言になり内部例外メッセージが漏れない。代わりに相関 ID が返り、
    /// クライアント例外へ復元される。サーバーログには同じ相関 ID とともに完全な詳細が残り、突き合わせが成立する。
    /// </summary>
    [Fact(DisplayName = "[RemoteErrorDetail] 1: 既定では 500 の詳細が非公開になり相関 ID が返る")]
    public async Task Default_HidesServerErrorDetailAndReturnsCorrelationId()
    {
        using var response = await PostDuplicateInsertAsync(_hidden!.BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var error = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        error!.Type.Should().Be("Error", "500 の種別は従来どおり固定値");
        error.Message.Should().Be(GenericMessage, "内部例外メッセージは差し替えられる");
        error
            .Message.Should()
            .NotContain("UNIQUE constraint", "DB のテーブル名・列名を含む文言が漏れない");
        error.CorrelationId.Should().NotBeNullOrEmpty("突き合わせ用の相関 ID が代わりに載る");

        // サーバーログは従来どおり完全な詳細を、同じ相関 ID とともに出す＝報告された ID からログを引ける
        var logged = _hiddenLog.Entries.Where(e => e.Contains(error.CorrelationId!)).ToList();
        logged.Should().ContainSingle("相関 ID はサーバーログにも出る");
        logged[0]
            .Should()
            .Contain("UNIQUE constraint", "サーバー側は非公開時も完全な詳細を記録する");

        // 生成クライアント経由でも同じ（型は従来どおり RemoteRepositoryException のまま）
        await HiddenDocuments.InsertAsync(new DocumentEntity { DocumentId = 2, Title = "b" }, Ct);

        var act = () =>
            HiddenDocuments.InsertAsync(new DocumentEntity { DocumentId = 2, Title = "b" }, Ct);

        var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
        thrown.StatusCode.Should().Be(500);
        thrown.Message.Should().Be(GenericMessage);
        thrown.CorrelationId.Should().NotBeNullOrEmpty("相関 ID は例外プロパティとして復元される");
    }

    /// <summary>2. exposeErrorDetails: true では従来どおりメッセージが透過し、相関 ID は載らない</summary>
    [Fact(DisplayName = "[RemoteErrorDetail] 2: exposeErrorDetails: true では従来どおり透過する")]
    public async Task ExposeErrorDetails_PassesTheMessageThrough()
    {
        using var response = await PostDuplicateInsertAsync(_exposed!.BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var error = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        error!.Type.Should().Be("Error");
        error.Message.Should().Contain("UNIQUE constraint", "公開時は内部例外メッセージが透過する");
        error.CorrelationId.Should().BeNull("公開時のボディは従来と同一＝相関 ID は載らない");

        // 生成クライアント経由でも同じ（例外の CorrelationId は null のまま）
        using var provider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_exposed.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
        var documents = provider.GetRequiredService<IDocumentRemoteRepository>();

        await documents.InsertAsync(new DocumentEntity { DocumentId = 2, Title = "b" }, Ct);

        var act = () =>
            documents.InsertAsync(new DocumentEntity { DocumentId = 2, Title = "b" }, Ct);

        var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
        thrown.Message.Should().Contain("UNIQUE constraint");
        thrown.CorrelationId.Should().BeNull();
    }

    /// <summary>
    /// 3. 対照: 非公開設定でも 400 の分類文言と 409 の構造化材料（Reason / EntityType / Key）は従来どおり透過する
    /// （どちらも自前の文言・再取得リトライの契約であって内部情報ではないため）。
    /// </summary>
    [Fact(DisplayName = "[RemoteErrorDetail] 3: 非公開でも 400 の文言と 409 の内訳は透過する")]
    public async Task HiddenDetails_DoNotAffectBadRequestOrConflict()
    {
        var baseUrl = _hidden!.BaseUrl;

        // 400: 必須フィールドを省いたボディ。自前の分類文言がそのまま返り、相関 ID は載らない
        using var raw = new HttpClient();
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await raw.PostAsync($"{baseUrl}/quicker/Document/Insert", content, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRequest = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        badRequest!.Type.Should().Be("BadRequest");
        badRequest.Message.Should().Contain("Entity", "400 の文言は差し替えられない");
        badRequest.CorrelationId.Should().BeNull("400 は 500 専用の相関 ID を持たない");

        // 409: 行のない更新保存。理由・型名・キーがそのまま復元される
        var ghost = new DocumentEntity
        {
            DocumentId = 999,
            Title = "ghost",
            Thumb = [],
        };
        ghost.MarkUpdated();

        var act = () => HiddenDocuments.SaveAsync(ghost, cancellationToken: Ct);

        var conflict = (await act.Should().ThrowAsync<SaveConflictException>()).Which;
        conflict.Reason.Should().Be(SaveConflictReason.NotFound, "競合の理由は隠さない");
        conflict.EntityTypeName.Should().Be(nameof(DocumentEntity));
        conflict.Key.Should().Be("999");
    }

    /// <summary>
    /// 4. 旧ボディ互換: 相関 ID を持たない 500 の <c>RemoteError</c> JSON を、クライアントは
    /// <c>CorrelationId = null</c> へ安全に退化させて復元する（メッセージ・型・ステータスは従来どおり）。
    /// </summary>
    [Fact(
        DisplayName = "[RemoteErrorDetail] 4: 相関 ID のない旧ボディは null へ退化して復元される"
    )]
    public async Task LegacyErrorBody_DegradesCorrelationIdToNull()
    {
        // 相関 ID フィールドを持たない旧ボディ（Type / Message だけ）を返すだけの最小サーバー
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        await using var legacy = builder.Build();
        legacy.MapPost(
            "/quicker/Document/Insert",
            async (HttpContext context) =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    """{"Type":"Error","Message":"legacy boom"}""",
                    context.RequestAborted
                );
            }
        );
        await legacy.StartAsync(Ct);

        using var provider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{legacy.Urls.First()}/quicker")
            .BuildServiceProvider();
        var documents = provider.GetRequiredService<IDocumentRemoteRepository>();

        var act = () =>
            documents.InsertAsync(new DocumentEntity { DocumentId = 1, Title = "a" }, Ct);

        var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
        thrown.StatusCode.Should().Be(500);
        thrown.Message.Should().Be("legacy boom", "メッセージの復元は従来どおり");
        thrown.CorrelationId.Should().BeNull("相関 ID のない旧ボディは null へ退化する");
    }

    /// <summary>
    /// 5. バイナリストリーミングエンドポイント（GET <c>{prefix}/{エンティティ}/{列名}</c>）の 500 も同じスイッチに従う。
    /// JSON エンドポイントとは別のラッパー（ExecuteDownloadAsync）を通るため、独立に固定する。
    /// </summary>
    [Fact(DisplayName = "[RemoteErrorDetail] 5: バイナリエンドポイントの 500 も同じスイッチに従う")]
    public async Task BinaryEndpoint_ServerError_FollowsTheSameSwitch()
    {
        // テーブルごと落として、応答本文を書き始める前にサーバー側で一般例外が起きる状態を作る
        await _db.ResetSchemaAsync(Ct);

        using var raw = new HttpClient();

        using var hidden = await raw.GetAsync(
            $"{_hidden!.BaseUrl}/quicker/Document/Payload?id=1",
            Ct
        );

        hidden.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var hiddenError = await hidden.Content.ReadFromJsonAsync<RemoteError>(Ct);
        hiddenError!.Message.Should().Be(GenericMessage, "バイナリ経路の 500 も非公開になる");
        hiddenError.Message.Should().NotContain("no such table");
        hiddenError.CorrelationId.Should().NotBeNullOrEmpty();

        using var exposed = await raw.GetAsync(
            $"{_exposed!.BaseUrl}/quicker/Document/Payload?id=1",
            Ct
        );

        exposed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var exposedError = await exposed.Content.ReadFromJsonAsync<RemoteError>(Ct);
        exposedError!
            .Message.Should()
            .Contain("no such table", "公開時はバイナリ経路でもメッセージが透過する");
        exposedError.CorrelationId.Should().BeNull();
    }

    /// <summary>
    /// 生成サーバーのログ出力（カテゴリ <c>QuickER.RemoteServer</c>）を素朴に文字列化して溜めるテスト用プロバイダ。
    /// </summary>
    /// <remarks>
    /// 相関 ID による「クライアントの報告 → サーバーログ」の突き合わせが実際に成立することを検証するためだけの器で、
    /// フォーマット済みメッセージと例外の文字列表現を連結して保持する。
    /// </remarks>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        /// <summary>捕捉済みのログ行</summary>
        public string[] Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => entries.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}
