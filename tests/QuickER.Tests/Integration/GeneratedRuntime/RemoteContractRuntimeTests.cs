using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedRemoteContractFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモート契約生成（GenerateRemoteContracts）の生成物を、実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）で意味検証する。
/// </summary>
/// <remarks>
/// <para>
/// 入力はリモート契約フィクスチャ（<see cref="RemoteContractFixtureDefinition"/>＝SQLite 方言の
/// QuickER 版 Repository＋EF Core 併存・名前付きクエリ入り）。検証の柱は 3 つ:
/// (1) リモート面（I{Entity}RemoteRepository）だけで CRUD・グラフ保存・名前付きクエリが完結する、
/// (2) リモート面と全機能面（I{Entity}Repository）が同一インスタンスとして解決され、全機能面では Query()・生 SQL も使える、
/// (3) リモート面の契約にローカル実行前提のメンバー（Query()・生 SQL・一括追加）が現れない（リフレクションで面の分割を証明）。
/// </para>
/// <para>
/// QuickER（AddGeneratedSqliteRepositories）・EF Core（AddGeneratedEfCoreRepositories）の両 DI 経路で同じ検証を流す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteContractRuntimeTests : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>破棄対象の DI コンテナ</summary>
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>スキーマを作成し、QuickER の SqliteRepository 版の DI コンテナを返す</summary>
    private async Task<ServiceProvider> CreateAdoProviderAsync()
    {
        await ApplySchemaAsync();
        var provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
            .BuildServiceProvider();
        _providers.Add(provider);

        return provider;
    }

    /// <summary>スキーマを作成し、EF Core Sqlite 版の DI コンテナを返す</summary>
    private async Task<ServiceProvider> CreateEfProviderAsync()
    {
        await ApplySchemaAsync();
        var provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlite(_db.ReadWriteCreateConnectionString)
            )
            .BuildServiceProvider();
        _providers.Add(provider);

        return provider;
    }

    /// <summary>フィクスチャ図から SQLite DDL を生成して一時 DB へ適用する</summary>
    private Task ApplySchemaAsync()
    {
        var ddl = new SqliteDdlGenerator().Build(RemoteContractFixtureDefinition.Build());

        return _db.ApplyDdlAsync(ddl, Ct);
    }

    /// <summary>リモート面だけで CRUD・グラフ保存・名前付きクエリが完結することを検証する共通シナリオ</summary>
    private static async Task RunRemoteFaceScenarioAsync(IServiceProvider provider)
    {
        // 将来リモート実装へ差し替える想定のリモート面（I{Entity}RemoteRepository）だけを解決する
        var customers = provider.GetRequiredService<ICustomerRemoteRepository>();
        var orders = provider.GetRequiredService<IOrderRemoteRepository>();

        await customers.InsertAsync(
            new CustomerEntity
            {
                CustomerId = CustomerIdValue.Create(1),
                Name = NameValue.Create("Alice"),
            },
            Ct
        );

        // グラフ保存（SaveAsync）もリモート面に載っている
        var order = new OrderEntity
        {
            OrderId = OrderIdValue.Create(10),
            CustomerId = CustomerIdValue.Create(1),
            Amount = AmountValue.Create(100m),
            Memo = MemoValue.Create("apple pie"),
        };
        order.MarkAdded();
        (await orders.SaveAsync(order, cancellationToken: Ct)).Should().Be(1);

        await orders.InsertAsync(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(11),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(50m),
                Memo = null,
            },
            Ct
        );

        // 名前付きクエリ（リモート面の契約メソッド）
        var found = await orders.GetByCustomerAsync(1, take: 10, skip: 0, Ct);
        found.Select(o => o.OrderId.Value).Should().Equal(11, 10);
        (await orders.CountByCustomerAsync(1, Ct)).Should().Be(2);

        // CRUD の残り（更新・単一取得・削除）
        var loaded = await orders.GetByIdAsync(OrderIdValue.Create(10), Ct);
        loaded.Should().NotBeNull();
        loaded!.Memo = MemoValue.Create("apple tart");
        (await orders.UpdateAsync(loaded, Ct)).Should().BeTrue();
        (await orders.DeleteAsync(OrderIdValue.Create(11), Ct)).Should().BeTrue();
        (await orders.GetAllAsync(Ct)).Should().ContainSingle();
    }

    /// <summary>QuickER の SqliteRepository 版: リモート面だけで CRUD・保存・名前付きクエリが動く</summary>
    [Fact(
        DisplayName = "[remote] QuickER の Sqlite: リモート面だけで CRUD・保存・名前付きクエリが動く"
    )]
    public async Task Ado_RemoteFace_SupportsCrudSaveAndNamedQueries()
    {
        var provider = await CreateAdoProviderAsync();

        await RunRemoteFaceScenarioAsync(provider);
    }

    /// <summary>EF Core 版: リモート面だけで CRUD・保存・名前付きクエリが動く</summary>
    [Fact(DisplayName = "[remote] EF Core: リモート面だけで CRUD・保存・名前付きクエリが動く")]
    public async Task EfCore_RemoteFace_SupportsCrudSaveAndNamedQueries()
    {
        var provider = await CreateEfProviderAsync();

        await RunRemoteFaceScenarioAsync(provider);
    }

    /// <summary>リモート面は全機能面と同一インスタンスで解決され、全機能面では Query()・生 SQL も使える</summary>
    [Fact(
        DisplayName = "[remote] リモート面は同一インスタンス解決・全機能面では Query()・生 SQL も使える"
    )]
    public async Task FullFace_ResolvesSameInstance_AndSupportsLocalMembers()
    {
        var provider = await CreateAdoProviderAsync();

        using var scope = provider.CreateScope();
        var full = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var remote = scope.ServiceProvider.GetRequiredService<IOrderRemoteRepository>();

        // 両面は同一実装インスタンス（Scoped）へ解決される
        remote.Should().BeSameAs(full);

        // FK 制約（orders → customers）を満たすため親を先に投入する
        await scope
            .ServiceProvider.GetRequiredService<ICustomerRepository>()
            .InsertAsync(
                new CustomerEntity
                {
                    CustomerId = CustomerIdValue.Create(1),
                    Name = NameValue.Create("Alice"),
                },
                Ct
            );

        await remote.InsertAsync(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(10),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(100m),
                Memo = MemoValue.Create("apple pie"),
            },
            Ct
        );

        // 全機能面（I{Entity}Repository）は従来どおり式木クエリ・生 SQL を持つ
        var byQuery = await full.Query()
            .Where(e => e.OrderId == OrderIdValue.Create(10))
            .ToListAsync(Ct);
        byQuery.Should().ContainSingle();

        var count = await full.ExecuteScalarSqlAsync<long>(
            "SELECT COUNT(*) FROM \"orders\"",
            cancellationToken: Ct
        );
        count.Should().Be(1);
    }

    /// <summary>リモート面の契約にローカル実行前提のメンバーが現れないこと（面の分割）をリフレクションで検証する</summary>
    [Fact(DisplayName = "[remote] リモート面の契約に Query()・生 SQL・一括追加が現れない")]
    public void RemoteFace_DoesNotExposeLocalOnlyMembers()
    {
        // 継承階層（IRemoteRepository 含む）越しに見えるメソッド名を集める
        static string[] MethodNames(Type type) =>
            type.GetInterfaces()
                .Prepend(type)
                .SelectMany(t => t.GetMethods())
                .Select(m => m.Name)
                .Distinct()
                .ToArray();

        var remote = MethodNames(typeof(IOrderRemoteRepository));
        var full = MethodNames(typeof(IOrderRepository));

        // リモート面: CRUD・保存・名前付きクエリのみ（ローカル実行前提のメンバーなし）
        remote
            .Should()
            .Contain([
                "GetByIdAsync",
                "GetAllAsync",
                "InsertAsync",
                "SaveAsync",
                "GetByCustomerAsync",
            ]);
        remote
            .Should()
            .NotContain([
                "Query",
                "QueryBySqlAsync",
                "ExecuteSqlAsync",
                "ExecuteScalarSqlAsync",
                "BulkInsertAsync",
            ]);

        // 全機能面: リモート面＋ローカル実行前提のメンバーの全部入り（従来どおり）
        full.Should()
            .Contain([
                "Query",
                "QueryBySqlAsync",
                "ExecuteSqlAsync",
                "ExecuteScalarSqlAsync",
                "BulkInsertAsync",
                "GetByCustomerAsync",
            ]);
    }

    /// <summary>使い終えた DI コンテナと一時 DB を破棄する</summary>
    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _db.Dispose();
    }
}
