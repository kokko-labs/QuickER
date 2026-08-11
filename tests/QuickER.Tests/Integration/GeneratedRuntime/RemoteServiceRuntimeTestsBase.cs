using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedRemoteServiceFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートサービス生成（GenerateRemoteServices）の生成物を、実 HTTP（Kestrel を 127.0.0.1 の空きポートで起動）＋
/// 実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）の 3 階層構成で end-to-end 検証するパリティスイートの共通基底。
/// </summary>
/// <remarks>
/// <para>
/// サーバー側は生成された <c>MapGeneratedRemoteEndpoints</c>＋実体リポジトリ（派生がQuickER の SqliteRepository 版と
/// EF Core 版を差し替える）、クライアント側は生成された <c>AddGeneratedHttpRemoteRepositories</c> の
/// <c>Http{Entity}RemoteRepository</c> だけを使う＝利用者が組む 3 階層とまったく同じ経路で検証する。
/// </para>
/// <para>
/// 検証の柱: (1) リモート面の CRUD・グラフ保存（保存後の RowState 確定＝直結と同じ挙動）、
/// (2) 名前付きクエリの全形（DSL・VO 型引数・IN・射影・スカラー・自由 SQL・manual）が HTTP 越しに同じ結果を返す、
/// (3) 例外伝搬（サーバーの SaveConflictException がクライアントで同型として復元される）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class RemoteServiceRuntimeTestsBase : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>in-process 起動した Kestrel サーバー</summary>
    private InProcessRemoteServer? _server;

    /// <summary>HTTP クライアント実装を登録した DI コンテナ</summary>
    private ServiceProvider? _clientProvider;

    /// <summary>サーバー側 DI へ実体リポジトリ群を登録する（QuickER = AddGeneratedSqliteRepositories / EF Core = AddGeneratedEfCoreRepositories）</summary>
    protected abstract void ConfigureServerRepositories(
        IServiceCollection services,
        string connectionString
    );

    /// <summary>スキーマ作成 → Kestrel 起動（空きポート）→ HTTP クライアント DI 構築を行う</summary>
    public async ValueTask InitializeAsync()
    {
        await _db.ApplyDdlAsync(RemoteServiceFixtureDefinition.Build(), Ct);

        _server = await InProcessRemoteServer.StartAsync(
            services => ConfigureServerRepositories(services, _db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
    }

    /// <summary>クライアント側の顧客リモート面を解決する</summary>
    private ICustomerRemoteRepository Customers =>
        _clientProvider!.GetRequiredService<ICustomerRemoteRepository>();

    /// <summary>クライアント側の注文リモート面を解決する</summary>
    private IOrderRemoteRepository Orders =>
        _clientProvider!.GetRequiredService<IOrderRemoteRepository>();

    /// <summary>共通のシードデータ（customers: 1=Alice, 2=Bob / orders: 4 件）を HTTP 経由で投入する</summary>
    private async Task SeedAsync()
    {
        await Customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await Customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await Orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await Orders.InsertAsync(NewOrder(11, 1, 50m, "banana"), Ct);
        await Orders.InsertAsync(NewOrder(12, 2, 200m, "apple juice"), Ct);
        await Orders.InsertAsync(NewOrder(13, 1, 75m, null), Ct);
    }

    /// <summary>顧客エンティティを組み立てる</summary>
    private static CustomerEntity NewCustomer(int id, string name) =>
        new() { CustomerId = CustomerIdValue.Create(id), Name = NameValue.Create(name) };

    /// <summary>注文エンティティを組み立てる</summary>
    private static OrderEntity NewOrder(
        int orderId,
        int customerId,
        decimal amount,
        string? memo
    ) =>
        new()
        {
            OrderId = OrderIdValue.Create(orderId),
            CustomerId = CustomerIdValue.Create(customerId),
            Amount = AmountValue.Create(amount),
            Memo = memo is null ? null : MemoValue.Create(memo),
        };

    /// <summary>1. CRUD（挿入・単一取得・更新・削除・全件）が HTTP 越しに機能する</summary>
    [Fact(DisplayName = "[RemoteService] 1: CRUD が HTTP 越しに機能する")]
    public async Task Crud_WorksOverHttp()
    {
        await Customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await Orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);

        var loaded = await Orders.GetByIdAsync(OrderIdValue.Create(10), Ct);
        loaded.Should().NotBeNull();
        loaded!.Memo!.Value.Should().Be("apple pie");
        loaded.Amount!.Value.Should().Be(100m);

        loaded.Memo = MemoValue.Create("apple tart");
        (await Orders.UpdateAsync(loaded, cancellationToken: Ct)).Should().BeTrue();
        (await Orders.GetByIdAsync(OrderIdValue.Create(10), Ct))!
            .Memo!.Value.Should()
            .Be("apple tart");

        (await Orders.DeleteAsync(OrderIdValue.Create(10), Ct)).Should().BeTrue();
        (await Orders.GetAllAsync(Ct)).Should().BeEmpty();
        (await Orders.GetByIdAsync(OrderIdValue.Create(10), Ct)).Should().BeNull();
    }

    /// <summary>2. グラフ保存（Save）が機能し、保存後にローカルの RowState が直結時と同じく確定する</summary>
    [Fact(DisplayName = "[RemoteService] 2: グラフ保存が機能しローカル RowState が確定する")]
    public async Task Save_PersistsAndAcceptsLocalChanges()
    {
        await Customers.InsertAsync(NewCustomer(1, "Alice"), Ct);

        var order = NewOrder(10, 1, 100m, "apple pie");
        order.MarkAdded();
        order.IsAdded.Should().BeTrue();

        (await Orders.SaveAsync(order, cancellationToken: Ct)).Should().Be(1);

        // 直結（EntityGraphSaver.AcceptChanges）と同じく、保存成功後は Unchanged に確定する
        order.IsAdded.Should().BeFalse();
        order.HasChanges.Should().BeFalse();

        (await Orders.GetByIdAsync(OrderIdValue.Create(10), Ct)).Should().NotBeNull();

        // 変更→再保存（更新経路）も機能する（エンティティは自動変更追跡しないため MarkUpdated が必要＝直結時と同じ）
        order.Memo = MemoValue.Create("apple tart");
        order.MarkUpdated();
        (await Orders.SaveAsync(order, cancellationToken: Ct)).Should().Be(1);
        (await Orders.GetByIdAsync(OrderIdValue.Create(10), Ct))!
            .Memo!.Value.Should()
            .Be("apple tart");
    }

    /// <summary>3. 名前付きクエリ（DSL 一覧＋ページング・件数・単一）が HTTP 越しに同じ結果を返す</summary>
    [Fact(DisplayName = "[RemoteService] 3: DSL クエリ（一覧・件数・単一）が HTTP 越しに機能する")]
    public async Task DslQueries_WorkOverHttp()
    {
        await SeedAsync();

        // 顧客 1 の注文は 13, 11, 10（注文ID降順）。skip=1, take=2 → 11, 10
        var window = await Orders.GetByCustomerAsync(1, take: 2, skip: 1, Ct);
        window.Select(o => o.OrderId.Value).Should().Equal(11, 10);

        (await Orders.CountByCustomerAsync(1, Ct)).Should().Be(3);
        (await Orders.CountByCustomerAsync(999, Ct)).Should().Be(0);

        var top = await Orders.FindTopAsync(Ct);
        top!.OrderId.Value.Should().Be(13);
    }

    /// <summary>4. VO 型引数・IN（リスト）・文字列一致のクエリが HTTP 越しに機能する</summary>
    [Fact(DisplayName = "[RemoteService] 4: VO 型引数・IN・LIKE クエリが HTTP 越しに機能する")]
    public async Task TypedAndListQueries_WorkOverHttp()
    {
        await SeedAsync();

        // VO 型引数（列参照型付け）: エンベロープの VO は JSON コンバータで内包値として運ばれる
        var typed = await Orders.GetByCustomerTypedAsync(CustomerIdValue.Create(1), Ct);
        typed.Select(o => o.OrderId.Value).Should().Equal(10, 11, 13);

        // IN（VO 列×リストパラメータ）
        var byIds = await Orders.GetByIdsAsync([10, 12, 999], Ct);
        byIds.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 12]);
        (await Orders.GetByIdsAsync([], Ct)).Should().BeEmpty();

        // LIKE（部分一致）
        var apples = await Orders.SearchMemoAsync("apple", Ct);
        apples.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 12]);
    }

    /// <summary>5. 射影・スカラー・自由 SQL・manual クエリが HTTP 越しに機能する</summary>
    [Fact(
        DisplayName = "[RemoteService] 5: 射影・スカラー・自由 SQL・manual が HTTP 越しに機能する"
    )]
    public async Task ProjectionScalarAndManualQueries_WorkOverHttp()
    {
        await SeedAsync();

        // 射影（DTO 一覧）
        var rows = await Orders.GetSummariesAsync(1, take: 2, skip: 0, Ct);
        rows.Should().HaveCount(2);
        rows.Select(r => r.Amount!.Value).Should().Equal(75m, 50m);

        // スカラー（自由 SQL・該当なしは null）
        (await Orders.SumAmountsAsync(1, Ct))
            .Should()
            .Be(225m);
        (await Orders.SumAmountsAsync(999, Ct)).Should().BeNull();

        // 自由 SQL の IN 展開
        var raw = await Orders.GetByIdsRawAsync([12, 10], Ct);
        raw.Select(o => o.OrderId.Value).Should().Equal(10, 12);

        // manual（サーバー側 partial 実装へ委譲される）
        var first = await Orders.SpecialLookupAsync(1, Ct);
        first!.OrderId.Value.Should().Be(10);
    }

    /// <summary>6. サーバーの SaveConflictException がクライアントで同型として復元される（直結時と同じ catch が機能する）</summary>
    [Fact(DisplayName = "[RemoteService] 6: SaveConflictException が HTTP 越しに型ごと復元される")]
    public async Task SaveConflict_IsRestoredAsSameExceptionType()
    {
        await Customers.InsertAsync(NewCustomer(1, "Alice"), Ct);

        // 存在しない注文の更新保存（insertWhenUpdateMissing=false）はサーバー側で SaveConflictException になる
        var missing = NewOrder(999, 1, 10m, null);
        missing.MarkUpdated();

        var act = () => Orders.SaveAsync(missing, cancellationToken: Ct);

        await act.Should().ThrowAsync<SaveConflictException>();
    }

    /// <summary>
    /// 7. SaveMany（複数集約の一括保存）が HTTP 越しに機能し、Added+Updated 混在でも保存後に各ローカル RowState が確定する。
    /// 既存の単一 Save テスト（#2）の複数版で、サーバー側 SaveMany エンドポイントを経由する。
    /// </summary>
    [Fact(
        DisplayName = "[RemoteService] 7: SaveMany（複数一括・Added+Updated 混在）が HTTP 越しに機能する"
    )]
    public async Task SaveMany_PersistsMixedStatesAndAcceptsLocalChanges()
    {
        await Customers.InsertAsync(NewCustomer(1, "Alice"), Ct);

        // まず 2 件を一括 Added で保存する（IEnumerable 版の SaveAsync＝SaveMany エンドポイント経由）
        var order10 = NewOrder(10, 1, 100m, "apple pie");
        var order11 = NewOrder(11, 1, 50m, "banana");
        order10.MarkAdded();
        order11.MarkAdded();

        (await Orders.SaveAsync([order10, order11], cancellationToken: Ct)).Should().Be(2);

        // 直結（EntityGraphSaver.AcceptChanges）と同じく、保存成功後は各ローカル RowState が Unchanged に確定する
        order10.IsAdded.Should().BeFalse();
        order11.IsAdded.Should().BeFalse();
        order10.HasChanges.Should().BeFalse();
        order11.HasChanges.Should().BeFalse();

        (await Orders.GetByIdAsync(OrderIdValue.Create(10), Ct)).Should().NotBeNull();
        (await Orders.GetByIdAsync(OrderIdValue.Create(11), Ct)).Should().NotBeNull();

        // Added（新規 12）＋ Updated（既存 10 の更新）を混在させた一括保存
        order10.Memo = MemoValue.Create("apple tart");
        order10.MarkUpdated();
        var order12 = NewOrder(12, 1, 200m, "cherry");
        order12.MarkAdded();

        (await Orders.SaveAsync([order10, order12], cancellationToken: Ct)).Should().Be(2);

        order10.HasChanges.Should().BeFalse();
        order12.IsAdded.Should().BeFalse();

        (await Orders.GetByIdAsync(OrderIdValue.Create(10), Ct))!
            .Memo!.Value.Should()
            .Be("apple tart");
        (await Orders.GetByIdAsync(OrderIdValue.Create(12), Ct))!.Amount!.Value.Should().Be(200m);
    }

    /// <summary>
    /// 8. 自由 SQL 名前付きクエリの 3 戻り形（単一・件数・射影）が HTTP 越しに正しい値を返す。
    /// ローカル実行（NamedQuery 系）とは別に、リモート面のメソッドがサーバーへ転送され自由 SQL を実行することを確認する。
    /// </summary>
    [Fact(
        DisplayName = "[RemoteService] 8: 自由 SQL クエリ（単一・件数・射影）が HTTP 越しに機能する"
    )]
    public async Task RawSqlQueries_WorkOverHttp()
    {
        await SeedAsync();

        // 単一（自由 SQL・最大 order_id）: 注文 13 が最新
        var top = await Orders.FindTopRawAsync(Ct);
        top!.OrderId.Value.Should().Be(13);

        // 件数（自由 SQL・SELECT COUNT）: 顧客 1 の注文は 3 件・存在しない顧客は 0
        (await Orders.CountByCustomerRawAsync(1, Ct))
            .Should()
            .Be(3);
        (await Orders.CountByCustomerRawAsync(999, Ct)).Should().Be(0);

        // 射影（自由 SQL・OrderMemoRow）: 顧客 1 の注文を order_id 昇順（10, 11, 13）で返す
        var rows = await Orders.GetMemoRowsRawAsync(1, Ct);
        rows.Select(r => r.OrderId).Should().Equal(10, 11, 13);
        rows.Select(r => r.Memo).Should().Equal("apple pie", "banana", null);
    }

    /// <summary>
    /// 9. AddGeneratedHttpRemoteRepositories の HttpClient ファクトリ形オーバーロード
    /// （<c>Func&lt;IServiceProvider, HttpClient&gt;</c> を直接渡す公開入口）でも解決でき、1 往復できる。
    /// </summary>
    [Fact(
        DisplayName = "[RemoteService] 9: HttpClient ファクトリ形オーバーロードで解決し 1 往復できる"
    )]
    public async Task HttpClientFactoryOverload_ResolvesAndRoundTrips()
    {
        var baseUrl = _server!.BaseUrl;

        // ファクトリ版は「prefix を含み末尾スラッシュ付き」の BaseAddress を持つ HttpClient を供給する契約
        using var provider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_ => new HttpClient
            {
                BaseAddress = new Uri($"{baseUrl}/quicker/"),
            })
            .BuildServiceProvider();

        var customers = provider.GetRequiredService<ICustomerRemoteRepository>();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        (await customers.GetByIdAsync(CustomerIdValue.Create(1), Ct))!
            .Name.Value.Should()
            .Be("Alice");
    }

    /// <summary>
    /// 10. 一般例外の 500 経路で partial 拡張点 OnServerError が発火する
    /// （テスト側の partial 実装＝<see cref="GeneratedRemoteEndpoints"/> の別パートが受け取る）。
    /// </summary>
    [Fact(DisplayName = "[RemoteService] 10: 500 経路で OnServerError フックが発火する")]
    public async Task ServerError_InvokesOnServerErrorHook()
    {
        await Customers.InsertAsync(NewCustomer(1, "Hook"), Ct);
        var before = GeneratedRemoteEndpoints.ServerErrorHookCallCount;

        // 同一主キーの再挿入は DB の一意制約違反＝サーバー側の一般例外→ HTTP 500 になる
        var act = () => Customers.InsertAsync(NewCustomer(1, "Hook"), Ct);

        await act.Should().ThrowAsync<RemoteRepositoryException>();

        // 静的カウンタは並列実行される派生スイート間で共有されるため、増分（>= 1）で検証する
        GeneratedRemoteEndpoints.ServerErrorHookCallCount.Should().BeGreaterThan(before);
    }

    /// <summary>
    /// 11. OnServerError フックの実装が例外を投げても、元例外の 500 応答（RemoteError JSON）は失われない
    /// （フックの例外はサーバー側で隔離・ログされ、クライアントは元のメッセージを受け取る）。
    /// </summary>
    [Fact(
        DisplayName = "[RemoteService] 11: OnServerError フックが例外を投げても元の 500 応答が失われない"
    )]
    public async Task ServerErrorHookException_DoesNotReplaceOriginalErrorResponse()
    {
        await Customers.InsertAsync(NewCustomer(1, "Hook"), Ct);

        var baseUrl = _server!.BaseUrl;

        // 「フックが投げるモード」はリクエストヘッダでスコープする（静的フラグにすると並列実行される派生スイートへ漏れる）
        using var client = new HttpClient { BaseAddress = new Uri($"{baseUrl}/quicker/") };
        client.DefaultRequestHeaders.Add(GeneratedRemoteEndpoints.ThrowInHookHeaderName, "1");

        using var provider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_ => client)
            .BuildServiceProvider();

        var customers = provider.GetRequiredService<ICustomerRemoteRepository>();
        var throwsBefore = GeneratedRemoteEndpoints.ServerErrorHookThrowCount;

        // 同一主キーの再挿入＝サーバー側の一般例外→ HTTP 500（このリクエストではフックも例外を投げる）
        var act = () => customers.InsertAsync(NewCustomer(1, "Hook"), Ct);

        var thrown = (await act.Should().ThrowAsync<RemoteRepositoryException>()).Which;
        thrown.StatusCode.Should().Be(500);

        // フックの例外が素通りしていれば RemoteError JSON 自体が書かれず、クライアントは
        // 「本文を読めなかった」ときの文言（The remote call failed …）へ退化する。ここではボディが
        // 書かれたこと＝サーバー側の 500 応答（既定では詳細非公開の固定文言＋相関 ID）が届いたことを見る
        thrown.Message.Should().NotContain("The remote call failed");
        thrown
            .CorrelationId.Should()
            .NotBeNullOrEmpty("既定の 500 は相関 ID を伴う＝RemoteError 本文が書かれている");

        // このリクエストのフックが実際に投げたこと（＝隔離が効いた経路を通ったこと）も確認する
        GeneratedRemoteEndpoints.ServerErrorHookThrowCount.Should().BeGreaterThan(throwsBefore);
    }

    /// <summary>
    /// 12. ベースアドレス版で作られる共有 HttpClient は DI コンテナが所有し、provider の破棄と同時に破棄される
    /// （取得済みリポジトリの呼び出しは ObjectDisposedException になる＝コンテナ所有資源の標準的な意味論）。
    /// </summary>
    [Fact(
        DisplayName = "[RemoteService] 12: ベースアドレス版の共有 HttpClient は provider 破棄で破棄される"
    )]
    public async Task BaseAddressOverload_SharedHttpClientIsDisposedWithProvider()
    {
        var baseUrl = _server!.BaseUrl;
        var provider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{baseUrl}/quicker")
            .BuildServiceProvider();

        var customers = provider.GetRequiredService<ICustomerRemoteRepository>();

        // 破棄前は共有 HttpClient が生きており 1 往復できる
        (await customers.GetAllAsync(Ct))
            .Should()
            .BeEmpty();

        provider.Dispose();

        var act = () => customers.GetAllAsync(Ct);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>
    /// 13. 不正 JSON のボディはリクエスト解釈の失敗＝400（RemoteError.Type="BadRequest"）になり、
    /// 生成クライアント経由でも RemoteRepositoryException.StatusCode が 400 になる（従来は 500 だった経路の回帰防止）。
    /// </summary>
    [Fact(DisplayName = "[RemoteService] 13: 不正 JSON のボディは 400（BadRequest）になる")]
    public async Task MalformedJsonBody_ReturnsBadRequest()
    {
        var baseUrl = _server!.BaseUrl;

        // 生成クライアントは必ず正しい JSON を送るため、壊れた JSON は素の HttpClient で直接送る
        using var raw = new HttpClient();
        using var content = new StringContent(
            "{\"broken",
            System.Text.Encoding.UTF8,
            "application/json"
        );
        using var response = await raw.PostAsync($"{baseUrl}/quicker/Customer/Insert", content, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        error!.Type.Should().Be("BadRequest", "リクエスト解釈の失敗は BadRequest として分類される");
    }

    /// <summary>14. 空ボディ（Content-Length 0）もリクエスト解釈の失敗＝400 になる</summary>
    [Fact(DisplayName = "[RemoteService] 14: 空ボディの POST は 400（BadRequest）になる")]
    public async Task EmptyBody_ReturnsBadRequest()
    {
        var baseUrl = _server!.BaseUrl;

        using var raw = new HttpClient();
        using var content = new StringContent("", System.Text.Encoding.UTF8, "application/json");
        using var response = await raw.PostAsync($"{baseUrl}/quicker/Customer/Insert", content, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        error!.Type.Should().Be("BadRequest");
    }

    /// <summary>
    /// 15. VO の制約違反（NameValue は最大 50 文字）はデシリアライズ中の ValueObjectValidationException になるが、
    /// これもリクエスト解釈の失敗＝400 として分類される（従来は 500 だった）。
    /// </summary>
    [Fact(DisplayName = "[RemoteService] 15: VO 制約違反のボディは 400（BadRequest）になる")]
    public async Task ValueObjectViolationInBody_ReturnsBadRequest()
    {
        var baseUrl = _server!.BaseUrl;

        // NameValue の上限は 50 文字。生成クライアントでは VO 生成時点で弾かれるため素の HttpClient で送る
        var tooLong = new string('a', 51);
        using var raw = new HttpClient();
        using var content = new StringContent(
            $"{{\"Entity\":{{\"CustomerId\":1,\"Name\":\"{tooLong}\"}}}}",
            System.Text.Encoding.UTF8,
            "application/json"
        );
        using var response = await raw.PostAsync($"{baseUrl}/quicker/Customer/Insert", content, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        error!.Type.Should().Be("BadRequest");

        // 400 で終わっているため行は作られていない
        (await Customers.GetAllAsync(Ct))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// 16. リクエストボディのサイズ上限超過（Kestrel の BadHttpRequestException）はステータスが素通しされ 413 になる
    /// （汎用 catch に落ちて 500 へ化けていた不具合の回帰防止）。上限を小さく設定した専用サーバーを別途起動して観測する。
    /// </summary>
    [Fact(
        DisplayName = "[RemoteService] 16: ボディサイズ上限超過は 413 が素通しされる（500 にならない）"
    )]
    public async Task RequestBodyTooLarge_PassesThroughStatusCode()
    {
        await using var server = await InProcessRemoteServer.StartAsync(
            services => ConfigureServerRepositories(services, _db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(),
            Ct,
            builder =>
                builder.WebHost.ConfigureKestrel(options =>
                    options.Limits.MaxRequestBodySize = 1024
                )
        );

        var baseUrl = server.BaseUrl;

        // 上限 1KB を確実に超える JSON ボディ（形としては正しい JSON）
        var payload = $"{{\"Entity\":{{\"CustomerId\":1,\"Name\":\"{new string('a', 4096)}\"}}}}";
        using var raw = new HttpClient();
        using var content = new StringContent(
            payload,
            System.Text.Encoding.UTF8,
            "application/json"
        );
        using var response = await raw.PostAsync($"{baseUrl}/quicker/Customer/Insert", content, Ct);

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.RequestEntityTooLarge,
                "BadHttpRequestException が持つステータスコードを素通しする"
            );

        await server.StopAsync(Ct);
    }

    /// <summary>
    /// 17. 必須フィールドを省いたボディ（<c>{}</c>）は 400（BadRequest）になる。エンベロープは positional record のため
    /// 欠落メンバは既定の null のまま通ってしまい、従来はリポジトリ奥の null 引数例外＝500 に化けていた経路の回帰防止。
    /// </summary>
    [Theory(DisplayName = "[RemoteService] 17: 必須フィールドを省いた {} のボディは 400 になる")]
    [InlineData("Customer/Insert")]
    [InlineData("Customer/Update")]
    [InlineData("Customer/Save")]
    [InlineData("Customer/SaveMany")]
    public async Task BodyMissingRequiredField_ReturnsBadRequest(string operation)
    {
        var baseUrl = _server!.BaseUrl;

        // 生成クライアントは必ず全フィールドを送るため、欠落したボディは素の HttpClient で直接送る
        using var raw = new HttpClient();
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await raw.PostAsync($"{baseUrl}/quicker/{operation}", content, Ct);

        response
            .StatusCode.Should()
            .Be(HttpStatusCode.BadRequest, "必須フィールドの欠落はクライアント側の不備＝400");

        var error = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        error!.Type.Should().Be("BadRequest");

        // 400 で終わっているため行は作られていない
        (await Customers.GetAllAsync(Ct))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// 18. 参照型キー（このフィクスチャの主キーは値オブジェクト）を省いたボディも 400 になる
    /// （キーの型が値型の場合は省略形が無いため素通しする＝この検証は参照型キー限定）。
    /// </summary>
    [Theory(DisplayName = "[RemoteService] 18: 参照型キーを省いた {} のボディは 400 になる")]
    [InlineData("Customer/GetById")]
    [InlineData("Customer/Delete")]
    public async Task BodyMissingKey_ReturnsBadRequest(string operation)
    {
        var baseUrl = _server!.BaseUrl;

        using var raw = new HttpClient();
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await raw.PostAsync($"{baseUrl}/quicker/{operation}", content, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        error!.Type.Should().Be("BadRequest");
    }

    /// <summary>
    /// 19. 400 の経路ではサーバー側のログ出力も <c>OnServerError</c> フックも呼ばれない（どちらも 500 専用）。
    /// </summary>
    /// <remarks>
    /// 静的カウンタは並列実行される派生スイート間で共有されるため、リクエスト単位の相関 ID ヘッダで数える。
    /// 同じ相関 ID で意図的に 500 を起こして 1 回数えられることも確認する＝「そもそも数えられていないから 0」という
    /// 空虚な検証にならないようにしている。ログ出力（<c>LogServerError</c>）はフック呼び出しと同じ catch 節の中に
    /// あるため、フックが呼ばれていないことはログも出ていないことを意味する。
    /// </remarks>
    [Fact(DisplayName = "[RemoteService] 19: 400 の経路では OnServerError フックが発火しない")]
    public async Task BadRequest_DoesNotInvokeOnServerErrorHook()
    {
        var baseUrl = _server!.BaseUrl;
        var correlationId = Guid.NewGuid().ToString();

        using var raw = new HttpClient();
        raw.DefaultRequestHeaders.Add(
            GeneratedRemoteEndpoints.CorrelationHeaderName,
            correlationId
        );

        // 必須フィールド欠落＝400。500 専用のフックは呼ばれない
        using var badContent = new StringContent(
            "{}",
            System.Text.Encoding.UTF8,
            "application/json"
        );
        using var badResponse = await raw.PostAsync(
            $"{baseUrl}/quicker/Customer/Insert",
            badContent,
            Ct
        );

        badResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        GeneratedRemoteEndpoints
            .CorrelatedHookCallCount(correlationId)
            .Should()
            .Be(0, "400 はサーバー側の失敗ではないためフックもログも呼ばれない");

        // 対照: 同じ相関 ID で 500（主キー重複）を起こすとフックは数えられる＝計測機構が生きている
        await Customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        var payload = "{\"Entity\":{\"CustomerId\":1,\"Name\":\"Alice\"}}";
        using var conflictContent = new StringContent(
            payload,
            System.Text.Encoding.UTF8,
            "application/json"
        );
        using var conflictResponse = await raw.PostAsync(
            $"{baseUrl}/quicker/Customer/Insert",
            conflictContent,
            Ct
        );

        conflictResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        GeneratedRemoteEndpoints
            .CorrelatedHookCallCount(correlationId)
            .Should()
            .Be(1, "500 の経路ではフックが発火する（計測機構が機能していることの対照）");
    }

    /// <summary>使い終えたクライアント DI・サーバー・一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _db.Dispose();
    }
}

/// <summary>リモートサービスの e2e スイートを、サーバー実体＝QuickER の SqliteRepository で実行する派生</summary>
public sealed class RemoteServiceAdoRuntimeTests : RemoteServiceRuntimeTestsBase
{
    protected override void ConfigureServerRepositories(
        IServiceCollection services,
        string connectionString
    ) => services.AddGeneratedSqliteRepositories(connectionString);
}

/// <summary>リモートサービスの e2e スイートを、サーバー実体＝EF Core Sqlite で実行する派生</summary>
public sealed class RemoteServiceEfCoreRuntimeTests : RemoteServiceRuntimeTestsBase
{
    protected override void ConfigureServerRepositories(
        IServiceCollection services,
        string connectionString
    ) =>
        services.AddGeneratedEfCoreRepositories(options =>
            Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(
                options,
                connectionString
            )
        );
}
