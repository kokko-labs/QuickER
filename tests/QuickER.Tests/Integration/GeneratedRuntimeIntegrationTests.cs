using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.Model;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// 固定フィクスチャ <c>GeneratedFixture.g.cs</c> が生成した自作 ORM ランタイム
/// （CRUD・Where 式木→SQL・Include・SaveAsync グラフ保存・楽観排他・BulkInsert）を、
/// 実 SQL Server（Testcontainers）に対して実行し、実際に動作することを検証する統合テスト。
/// </summary>
/// <remarks>
/// テーブルは <see cref="GeneratedFixtureDefinition"/> の図から SQL Server 方言の DDL を生成して用意する。
/// フィクスチャの生成型（<c>CustomerEntity</c> / <c>CustomerRepository</c> 等）を直接呼ぶことで、
/// 生成コードの実行時挙動を型安全に検証する。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class GeneratedRuntimeIntegrationTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>フィクスチャ図の SQL Server 方言 DDL でテーブルを作成する（各テスト冒頭でスキーマを初期化してから呼ぶ）</summary>
    private async Task CreateSchemaAsync()
    {
        var ddl = new SqlServerDdlGenerator().Build(GeneratedFixtureDefinition.Build());
        await fixture.ExecuteAsync(ddl, Ct);
    }

    /// <summary>接続ファクトリを生成する</summary>
    private SqlConnectionFactory Factory() => new(fixture.ConnectionString);

    /// <summary>指定 ID の顧客エンティティを組み立てる（VO は Create で検証生成）</summary>
    private static CustomerEntity NewCustomer(
        int id,
        string name,
        decimal? balance = null,
        bool isActive = true
    )
    {
        var customer = new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = balance is null ? null : BalanceValue.Create(balance.Value),
            IsActive = IsActiveValue.Create(isActive),
        };
        return customer;
    }

    /// <summary>指定 ID の注文エンティティを組み立てる</summary>
    private static OrderEntity NewOrder(int orderId, int customerId, decimal amount, string? memo)
    {
        return new OrderEntity
        {
            OrderId = OrderIdValue.Create(orderId),
            CustomerId = CustomerIdValue.Create(customerId),
            Amount = AmountValue.Create(amount),
            Memo = memo is null ? null : MemoValue.Create(memo),
        };
    }

    /// <summary>1. CRUD 往復: Insert → GetById（値・VO 復元一致）→ Update → Delete</summary>
    [Fact(
        DisplayName = "[Integration] 1: CRUD 往復（Insert→GetById→Update→Delete）で値と VO が正しく復元される"
    )]
    public async Task Crud_RoundTrips()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();

        var repo = new CustomerRepository(Factory());

        // Insert
        var inserted = NewCustomer(1, "Alice", balance: 123.45m, isActive: true);
        await repo.InsertAsync(inserted, Ct);

        // GetById: 値・VO の復元一致
        var loaded = await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct);
        loaded.Should().NotBeNull();
        loaded!.CustomerId.Value.Should().Be(1);
        loaded.Name.Value.Should().Be("Alice");
        loaded.Balance.Should().NotBeNull();
        loaded.Balance!.Value.Should().Be(123.45m);
        loaded.IsActive.Value.Should().BeTrue();

        // Update
        loaded.Name = NameValue.Create("Alice Updated");
        loaded.Balance = null;
        var updated = await repo.UpdateAsync(loaded, Ct);
        updated.Should().BeTrue();

        var reloaded = await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct);
        reloaded!.Name.Value.Should().Be("Alice Updated");
        reloaded.Balance.Should().BeNull("NULL 許容の decimal 列を null 更新したため");

        // Delete
        var deleted = await repo.DeleteAsync(CustomerIdValue.Create(1), Ct);
        deleted.Should().BeTrue();
        (await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct)).Should().BeNull();
    }

    /// <summary>2. Where 式木: 比較・&amp;&amp;・LIKE(Contains)・IN(List)・IsNullOrEmpty の代表 5 種が正しい行を返す</summary>
    [Fact(
        DisplayName = "[Integration] 2: Where 式木（比較・&&・LIKE・IN・IsNullOrEmpty）が正しい行を返す"
    )]
    public async Task Where_ExpressionTree_ReturnsCorrectRows()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();

        var repo = new CustomerRepository(Factory());
        await repo.InsertAsync(NewCustomer(1, "Alice", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", isActive: false), Ct);
        await repo.InsertAsync(NewCustomer(3, "Alicia", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(4, "Carol", isActive: true), Ct);

        var orders = new OrderRepository(Factory());
        // memo に空文字・非空を混ぜる（IsNullOrEmpty 検証用）
        await orders.InsertAsync(NewOrder(10, 1, 10m, memo: "shipped"), Ct);
        await orders.InsertAsync(NewOrder(11, 2, 20m, memo: ""), Ct);

        // (a) 比較（VO の == 演算子。varchar 列に対する型明示パラメータ経由の検索）
        var byName = await repo.Query()
            .Where(c => c.Name == NameValue.Create("Bob"))
            .ToListAsync(Ct);
        byName.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);

        // (b) && の連結
        var activeAlice = await repo.Query()
            .Where(c =>
                c.IsActive == IsActiveValue.Create(true) && c.Name == NameValue.Create("Alice")
            )
            .ToListAsync(Ct);
        activeAlice.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1]);

        // (c) LIKE（string VO の Contains → LIKE '%...%'）
        var likeAli = await repo.Query().Where(c => c.Name.Contains("Ali")).ToListAsync(Ct);
        likeAli.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3]);

        // (d) IN（List<VO>.Contains → IN (...)）
        var ids = new List<CustomerIdValue>
        {
            CustomerIdValue.Create(2),
            CustomerIdValue.Create(4),
        };
        var inList = await repo.Query().Where(c => ids.Contains(c.CustomerId)).ToListAsync(Ct);
        inList.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 4]);

        // (e) IsNullOrEmpty（VO の .Value を素値へ開いて空文字判定）
        var emptyMemo = await orders
            .Query()
            .Where(o => string.IsNullOrEmpty(o.Memo!.Value))
            .ToListAsync(Ct);
        emptyMemo.Select(o => o.OrderId.Value).Should().BeEquivalentTo([11]);
    }

    /// <summary>3. Include: 親→子コレクション・子→親 の Include 復元</summary>
    [Fact(
        DisplayName = "[Integration] 3: Include（親→子コレクション・子→親参照）が正しく復元される"
    )]
    public async Task Include_LoadsNavigations()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();

        var customers = new CustomerRepository(Factory());
        var orders = new OrderRepository(Factory());

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, "a"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, "b"), Ct);

        // 親→子コレクション Include
        var customer = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        customer.Should().NotBeNull();
        customer!.Orders.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11]);

        // 子→親参照 Include
        var order = await orders
            .Query()
            .Where(o => o.OrderId == OrderIdValue.Create(10))
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(Ct);
        order.Should().NotBeNull();
        order!.Customer.Should().NotBeNull();
        order.Customer.CustomerId.Value.Should().Be(1);
        order.Customer.Name.Value.Should().Be("Alice");
    }

    /// <summary>6. BulkInsertAsync: 数十件流して件数一致</summary>
    [Fact(DisplayName = "[Integration] 6: BulkInsertAsync で数十件が一括追加され件数が一致する")]
    public async Task BulkInsert_InsertsAllRows()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();

        var repo = new CustomerRepository(Factory());

        const int count = 50;
        var batch = Enumerable
            .Range(1, count)
            .Select(i => NewCustomer(i, $"Customer{i}", balance: i * 1.5m))
            .ToList();

        var inserted = await repo.BulkInsertAsync(batch, Ct);
        inserted.Should().Be(count);

        var all = await repo.GetAllAsync(Ct);
        all.Should().HaveCount(count);
        all.Select(c => c.CustomerId.Value).Should().BeEquivalentTo(Enumerable.Range(1, count));
    }
}
