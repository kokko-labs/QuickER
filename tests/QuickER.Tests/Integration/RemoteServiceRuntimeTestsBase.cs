using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedRemoteServiceFixture;

namespace QuickER.Tests.Integration;

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
    private WebApplication? _app;

    /// <summary>HTTP クライアント実装を登録した DI コンテナ</summary>
    private ServiceProvider? _clientProvider;

    /// <summary>サーバー側 DI へ実体リポジトリ群を登録する（QuickER = AddGeneratedRepositories / EF = AddGeneratedEfCoreRepositories）</summary>
    protected abstract void ConfigureServerRepositories(
        IServiceCollection services,
        string connectionString
    );

    /// <summary>スキーマ作成 → Kestrel 起動（空きポート）→ HTTP クライアント DI 構築を行う</summary>
    public async ValueTask InitializeAsync()
    {
        var ddl = new SqliteDdlGenerator().Build(RemoteServiceFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        ConfigureServerRepositories(builder.Services, _db.ReadWriteCreateConnectionString);

        _app = builder.Build();
        _app.MapGeneratedRemoteEndpoints();
        await _app.StartAsync(Ct);

        var baseUrl = _app.Urls.First();
        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{baseUrl}/quicker")
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
        (await Orders.UpdateAsync(loaded, Ct)).Should().BeTrue();
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

    /// <summary>使い終えたクライアント DI・サーバー・一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
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
    ) => services.AddGeneratedRepositories(connectionString);
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
