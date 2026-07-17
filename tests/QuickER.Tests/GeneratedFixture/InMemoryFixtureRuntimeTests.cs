using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// コミット済みインメモリフィクスチャ（<c>InMemoryFixture.g.cs</c>）が実際にコンパイルされ、
/// インメモリ Repository の実行時挙動（CRUD・クエリ・Include・カスケード保存・複製性・生 SQL 非対応・DI 解決）が
/// 期待どおりであることを検証する。
/// </summary>
/// <remarks>
/// 生成コードはこのテストアセンブリに直接コンパイルされるため、生成型（<c>CustomerEntity</c>・
/// <c>InMemoryDataStore</c>・<c>AddGeneratedInMemoryRepositories</c> 等）をそのまま参照して動作を検証する。
/// </remarks>
public sealed class InMemoryFixtureRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>シード無効のデータストアと各インメモリ Repository を生成する（テストごとに独立）</summary>
    private static (
        InMemoryDataStore Store,
        ICustomerRepository Customers,
        IOrderRepository Orders,
        ICustomerProfileRepository Profiles
    ) BuildFresh(bool seed = false)
    {
        var store = new InMemoryDataStore();

        if (seed)
        {
            InMemorySampleData.Seed(store);
        }

        return (
            store,
            new InMemoryCustomerRepository(store),
            new InMemoryOrderRepository(store),
            new InMemoryCustomerProfileRepository(store)
        );
    }

    private static CustomerEntity NewCustomer(int id, string name, decimal? balance = null) =>
        new()
        {
            CustomerId = id,
            Name = name,
            Balance = balance,
            RowState = RowState.Added,
        };

    private static OrderEntity NewOrder(int id, int customerId, decimal amount) =>
        new()
        {
            OrderId = id,
            CustomerId = customerId,
            Amount = amount,
            RowState = RowState.Added,
        };

    [Fact(DisplayName = "Insert / GetById / GetAll / Update / Delete の CRUD が動く")]
    public async Task Crud_Works()
    {
        var (_, customers, _, _) = BuildFresh();

        await customers.InsertAsync(NewCustomer(1, "Alice", 100m), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);

        var byId = await customers.GetByIdAsync(1, Ct);
        byId.Should().NotBeNull();
        byId!.Name.Should().Be("Alice");
        byId.Balance.Should().Be(100m);
        byId.RowState.Should()
            .Be(RowState.Unchanged, "取得系はストアのスナップショット複製を Unchanged で返す");

        (await customers.GetByIdAsync(999, Ct)).Should().BeNull();
        (await customers.GetAllAsync(Ct)).Should().HaveCount(2);

        // 更新
        var updated = NewCustomer(1, "Alice2", 200m);
        updated.RowState = RowState.Updated;
        (await customers.UpdateAsync(updated, Ct)).Should().BeTrue();
        (await customers.GetByIdAsync(1, Ct))!.Name.Should().Be("Alice2");

        // 存在しない主キーの更新は false
        var missing = NewCustomer(42, "Ghost");
        missing.RowState = RowState.Updated;
        (await customers.UpdateAsync(missing, Ct)).Should().BeFalse();

        // 削除
        (await customers.DeleteAsync(2, Ct))
            .Should()
            .BeTrue();
        (await customers.DeleteAsync(2, Ct)).Should().BeFalse();
        (await customers.GetAllAsync(Ct)).Should().ContainSingle();
    }

    [Fact(DisplayName = "BulkInsert が件数を返して全件投入する")]
    public async Task BulkInsert_Works()
    {
        var (_, customers, _, _) = BuildFresh();

        var count = await customers.BulkInsertAsync(
            [NewCustomer(1, "A"), NewCustomer(2, "B"), NewCustomer(3, "C")],
            Ct
        );

        count.Should().Be(3);
        (await customers.GetAllAsync(Ct)).Should().HaveCount(3);
    }

    [Fact(DisplayName = "取得したエンティティを変更してもストアは不変（複製性）")]
    public async Task GetById_ReturnsClone_StoreImmutable()
    {
        var (_, customers, _, _) = BuildFresh();
        await customers.InsertAsync(NewCustomer(1, "Alice", 100m), Ct);

        var first = await customers.GetByIdAsync(1, Ct);
        first!.Name = "Mutated";
        first.Balance = 999m;

        var second = await customers.GetByIdAsync(1, Ct);
        second!.Name.Should().Be("Alice", "返却エンティティの変更はストアへ波及しない");
        second.Balance.Should().Be(100m);
    }

    [Fact(DisplayName = "Query: Where / OrderBy / Skip / Take / Count / Any / FirstOrDefault")]
    public async Task Query_Works()
    {
        var (_, customers, _, _) = BuildFresh();
        await customers.BulkInsertAsync(
            [
                NewCustomer(1, "Alice", 300m),
                NewCustomer(2, "Bob", 100m),
                NewCustomer(3, "Carol", 200m),
            ],
            Ct
        );

        // Where + OrderBy 昇順
        var ordered = await customers
            .Query()
            .Where(c => c.Balance != null)
            .OrderBy(c => c.Balance)
            .ToListAsync(Ct);
        ordered.Select(c => c.Name).Should().ContainInOrder("Bob", "Carol", "Alice");

        // OrderByDescending + Skip + Take
        var page = await customers
            .Query()
            .OrderByDescending(c => c.Balance)
            .Skip(1)
            .Take(1)
            .ToListAsync(Ct);
        page.Should().ContainSingle().Which.Name.Should().Be("Carol");

        (await customers.Query().Where(c => c.Balance >= 200m).CountAsync(Ct)).Should().Be(2);
        (await customers.Query().Where(c => c.Name == "Bob").AnyAsync(Ct)).Should().BeTrue();
        (await customers.Query().Where(c => c.Name == "Zed").AnyAsync(Ct)).Should().BeFalse();

        var firstBob = await customers.Query().Where(c => c.Name == "Bob").FirstOrDefaultAsync(Ct);
        firstBob!.CustomerId.Should().Be(2);
        (await customers.Query().Where(c => c.Name == "Zed").FirstOrDefaultAsync(Ct))
            .Should()
            .BeNull();
    }

    [Fact(
        DisplayName = "Include: 子コレクション・単一参照子・親参照・ThenInclude を FK から復元する"
    )]
    public async Task Include_Works()
    {
        var (_, customers, orders, profiles) = BuildFresh();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);

        await orders.InsertAsync(NewOrder(10, 1, 5m), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 6m), Ct);
        await profiles.InsertAsync(
            new CustomerProfileEntity
            {
                ProfileId = 100,
                CustomerId = 1,
                Bio = "hello",
                RowState = RowState.Added,
            },
            Ct
        );

        // 親→子コレクション（Orders）＋子単一参照（CustomerProfile）
        var withChildren = await customers
            .Query()
            .Where(c => c.CustomerId == 1)
            .Include(c => c.Orders)
            .Include(c => c.CustomerProfile)
            .FirstOrDefaultAsync(Ct);
        withChildren!.Orders.Should().HaveCount(2);
        withChildren.Orders.Select(o => o.OrderId).Should().BeEquivalentTo(new[] { 10, 11 });
        withChildren.CustomerProfile.Should().NotBeNull();
        withChildren.CustomerProfile.Bio.Should().Be("hello");

        // 親を持たない顧客の子は空・null
        var bob = await customers
            .Query()
            .Where(c => c.CustomerId == 2)
            .Include(c => c.Orders)
            .Include(c => c.CustomerProfile)
            .FirstOrDefaultAsync(Ct);
        bob!.Orders.Should().BeEmpty();
        bob.CustomerProfile.Should().BeNull();

        // 子→親参照（Customer）と ThenInclude（親→その子コレクション）
        var orderWithParent = await orders
            .Query()
            .Where(o => o.OrderId == 10)
            .Include(o => o.Customer)
                .ThenInclude(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        orderWithParent!.Customer.Should().NotBeNull();
        orderWithParent.Customer.Name.Should().Be("Alice");
        orderWithParent.Customer.Orders.Should().HaveCount(2);
    }

    [Fact(DisplayName = "SaveAsync: Added/Updated/Removed と子カスケードが RowState 駆動で動く")]
    public async Task SaveAsync_Cascade_Works()
    {
        var (_, customers, orders, _) = BuildFresh();

        // Added の親＋子コレクションをカスケード保存
        var alice = NewCustomer(1, "Alice");
        alice.Orders.Add(NewOrder(10, 1, 5m));
        alice.Orders.Add(NewOrder(11, 1, 6m));

        var rows = await customers.SaveAsync(alice, cancellationToken: Ct);
        rows.Should().Be(3, "親 1 + 子 2");
        alice.RowState.Should().Be(RowState.Unchanged, "保存後は Unchanged 確定");
        alice.Orders.All(o => o.RowState == RowState.Unchanged).Should().BeTrue();
        (await orders.GetAllAsync(Ct)).Should().HaveCount(2);

        // 子 1 件を Updated、1 件を Removed にして再保存
        var loaded = await customers
            .Query()
            .Where(c => c.CustomerId == 1)
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        var toUpdate = loaded!.Orders.First(o => o.OrderId == 10);
        toUpdate.Amount = 50m;
        toUpdate.RowState = RowState.Updated;
        var toRemove = loaded.Orders.First(o => o.OrderId == 11);
        toRemove.RowState = RowState.Removed;

        await customers.SaveAsync(loaded, cancellationToken: Ct);
        var remaining = await orders.GetAllAsync(Ct);
        remaining.Should().ContainSingle();
        remaining[0].OrderId.Should().Be(10);
        remaining[0].Amount.Should().Be(50m);
    }

    [Fact(DisplayName = "SaveAsync: insertWhenUpdateMissing で更新対象なしを INSERT へ切替")]
    public async Task SaveAsync_InsertWhenUpdateMissing()
    {
        var (_, customers, _, _) = BuildFresh();

        var ghost = NewCustomer(7, "Ghost");
        ghost.RowState = RowState.Updated; // 存在しないのに Updated

        // 既定（false）は競合例外
        var act = async () => await customers.SaveAsync(ghost, cancellationToken: Ct);
        await act.Should().ThrowAsync<SaveConflictException>();

        // insertWhenUpdateMissing=true は INSERT へ切替
        var ghost2 = NewCustomer(8, "Ghost2");
        ghost2.RowState = RowState.Updated;
        var rows = await customers.SaveAsync(
            ghost2,
            insertWhenUpdateMissing: true,
            cancellationToken: Ct
        );
        rows.Should().Be(1);
        (await customers.GetByIdAsync(8, Ct))!.Name.Should().Be("Ghost2");
    }

    [Fact(DisplayName = "Query.ExecuteDeleteAsync: cascadeDelete で子孫も削除する")]
    public async Task ExecuteDelete_Cascade()
    {
        var (_, customers, orders, _) = BuildFresh();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 5m), Ct);

        var deleted = await customers
            .Query()
            .Where(c => c.CustomerId == 1)
            .ExecuteDeleteAsync(cascadeDelete: true, Ct);
        deleted.Should().Be(2, "親 1 + 子 1");
        (await customers.GetAllAsync(Ct)).Should().BeEmpty();
        (await orders.GetAllAsync(Ct)).Should().BeEmpty();
    }

    [Fact(
        DisplayName = "ToProjectionListAsync: 列参照のみの射影が並び順込みで DTO を返す（刈り込み経路）"
    )]
    public async Task Projection_ColumnsOnly_ReturnsRows()
    {
        var (_, customers, orders, _) = BuildFresh();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 5m), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 6m), Ct);

        // 列参照のみ・Include なし＝刈り込み可能経路（完全クローンから射影）
        var rows = await orders
            .Query()
            .Where(o => o.CustomerId == 1)
            .OrderBy(o => o.OrderId)
            .ToProjectionListAsync(o => new { o.OrderId, o.Amount }, Ct);

        rows.Select(r => r.OrderId).Should().Equal(10, 11);
        rows.Select(r => r.Amount).Should().Equal(5m, 6m);
    }

    [Fact(
        DisplayName = "ToProjectionListAsync: Include＋ナビゲーション参照射影はフォールバックして動く"
    )]
    public async Task Projection_WithInclude_ProjectsNavigation()
    {
        var (_, customers, orders, _) = BuildFresh();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 5m), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 6m), Ct);

        // Include したナビ（Customer）をセレクタが参照＝刈り込み不可。従来経路（strip 済み複製）で Include を装着し射影する
        var rows = await orders
            .Query()
            .Where(o => o.CustomerId == 1)
            .Include(o => o.Customer)
            .OrderBy(o => o.OrderId)
            .ToProjectionListAsync(o => new { o.OrderId, CustomerName = o.Customer.Name }, Ct);

        rows.Select(r => r.OrderId).Should().Equal(10, 11);
        rows.Should().OnlyContain(r => r.CustomerName == "Alice");
    }

    [Fact(DisplayName = "生 SQL 系メソッドは NotSupportedException を投げる")]
    public async Task RawSql_NotSupported()
    {
        var (_, customers, _, _) = BuildFresh();

        (
            await FluentActions
                .Awaiting(() => customers.QueryBySqlAsync("SELECT 1", null, Ct))
                .Should()
                .ThrowAsync<NotSupportedException>()
        )
            .Which.Message.Should()
            .Contain("インメモリ");
        await FluentActions
            .Awaiting(() => customers.ExecuteSqlAsync("DELETE FROM x", null, Ct))
            .Should()
            .ThrowAsync<NotSupportedException>();
        await FluentActions
            .Awaiting(() => customers.ExecuteScalarSqlAsync<int>("SELECT COUNT(*)", null, Ct))
            .Should()
            .ThrowAsync<NotSupportedException>();
    }

    [Fact(DisplayName = "AddGeneratedInMemoryRepositories: DI 解決とシードデータ（件数・FK 整合）")]
    public async Task Di_Resolution_And_Seed()
    {
        var services = new ServiceCollection();
        services.AddGeneratedInMemoryRepositories();
        using var provider = services.BuildServiceProvider();

        var customers = provider.GetRequiredService<ICustomerRepository>();
        var orders = provider.GetRequiredService<IOrderRepository>();
        var profiles = provider.GetRequiredService<ICustomerProfileRepository>();
        // ストアは Singleton 共有
        provider.GetRequiredService<InMemoryDataStore>().Should().NotBeNull();

        var allCustomers = await customers.GetAllAsync(Ct);
        allCustomers.Should().HaveCount(InMemorySampleData.RowsPerEntity);
        (await orders.GetAllAsync(Ct)).Should().HaveCount(InMemorySampleData.RowsPerEntity);
        (await profiles.GetAllAsync(Ct)).Should().HaveCount(InMemorySampleData.RowsPerEntity);

        // シードの FK 整合: 各注文の customer_id は実在する customer を指す
        var customerIds = allCustomers.Select(c => c.CustomerId).ToHashSet();

        foreach (var order in await orders.GetAllAsync(Ct))
        {
            customerIds.Should().Contain(order.CustomerId);
        }

        // Include でシードの親子を突き合わせられる
        var withOrders = await customers
            .Query()
            .Where(c => c.CustomerId == 1)
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        withOrders!.Orders.Should().ContainSingle(o => o.OrderId == 1);
    }

    [Fact(DisplayName = "seedSampleData=false は空ストアで登録する")]
    public async Task Di_NoSeed()
    {
        var services = new ServiceCollection();
        services.AddGeneratedInMemoryRepositories(seedSampleData: false);
        using var provider = services.BuildServiceProvider();

        (await provider.GetRequiredService<ICustomerRepository>().GetAllAsync(Ct))
            .Should()
            .BeEmpty();
    }

    // ===== 名前付きクエリ（ミニ DSL 共有本体がインメモリ実装にも出力されることの実行検証） =====

    /// <summary>名前付きクエリ検証用のシード（顧客 2 件＋注文 4 件）を投入した注文リポジトリを作る</summary>
    private async Task<IOrderRepository> SeedNamedQueryOrdersAsync()
    {
        var (_, customers, orders, _) = BuildFresh();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 100m), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m), Ct);
        await orders.InsertAsync(NewOrder(12, 2, 200m), Ct);
        await orders.InsertAsync(NewOrder(13, 1, 75m), Ct);
        return orders;
    }

    [Fact(DisplayName = "名前付きクエリ: 一覧＋条件＋並び順＋ページングが正しい窓を返す")]
    public async Task NamedQuery_List_WithPaging_ReturnsOrderedWindow()
    {
        var orders = await SeedNamedQueryOrdersAsync();

        // 顧客 1 の注文は 13, 11, 10（注文ID降順）。skip=1, take=2 → 11, 10
        var window = await orders.GetByCustomerAsync(1, take: 2, skip: 1, Ct);
        window.Select(o => o.OrderId).Should().Equal(11, 10);

        // skip 既定（0）
        var top = await orders.GetByCustomerAsync(1, take: 2, cancellationToken: Ct);
        top.Select(o => o.OrderId).Should().Equal(13, 11);
    }

    [Fact(DisplayName = "名前付きクエリ: 単一が並び順先頭の 1 件（空ストアは null）を返す")]
    public async Task NamedQuery_Single_ReturnsFirstByOrdering()
    {
        var orders = await SeedNamedQueryOrdersAsync();

        var top = await orders.FindTopAsync(Ct);
        top.Should().NotBeNull();
        top!.OrderId.Should().Be(13);

        // 空ストアでは null
        var (_, _, emptyOrders, _) = BuildFresh();
        (await emptyOrders.FindTopAsync(Ct)).Should().BeNull();
    }

    [Fact(DisplayName = "名前付きクエリ: 件数が条件一致数を返す")]
    public async Task NamedQuery_Count_ReturnsMatchingCount()
    {
        var orders = await SeedNamedQueryOrdersAsync();

        (await orders.CountByCustomerAsync(1, Ct)).Should().Be(3);
        (await orders.CountByCustomerAsync(2, Ct)).Should().Be(1);
        (await orders.CountByCustomerAsync(999, Ct)).Should().Be(0);
    }

    [Fact(DisplayName = "名前付きクエリ: 射影が DTO 一覧を並び順込みで返す")]
    public async Task NamedQuery_Projection_ReturnsDtoRows()
    {
        var orders = await SeedNamedQueryOrdersAsync();

        // 顧客 1 の注文（10, 11, 13 の昇順）→ Amount は 100, 50, 75
        var rows = await orders.GetSummariesAsync(1, Ct);
        rows.Should().HaveCount(3);
        rows.Select(r => r.CustomerId).Should().OnlyContain(v => v == 1);
        rows.Select(r => r.Amount).Should().Equal(100m, 50m, 75m);

        (await orders.GetSummariesAsync(999, Ct)).Should().BeEmpty();
    }
}
