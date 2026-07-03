using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// 固定フィクスチャの生成ランタイムのうち、SaveAsync グラフ保存と楽観排他（SaveConflictException・
/// insertWhenUpdateMissing 切替）を実 SQL Server（Testcontainers）で検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class GeneratedRuntimeGraphAndConcurrencyTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task CreateSchemaAsync()
    {
        var ddl = new SqlServerDdlGenerator().Build(GeneratedFixtureDefinition.Build());
        await fixture.ExecuteAsync(ddl, Ct);
    }

    private SqlConnectionFactory Factory() => new(fixture.ConnectionString);

    private static CustomerEntity NewCustomer(int id, string name) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = null,
            IsActive = IsActiveValue.Create(true),
        };

    private static OrderEntity NewOrder(int orderId, int customerId, decimal amount) =>
        new()
        {
            OrderId = OrderIdValue.Create(orderId),
            CustomerId = CustomerIdValue.Create(customerId),
            Amount = AmountValue.Create(amount),
            Memo = null,
        };

    /// <summary>4. SaveAsync グラフ保存: Added 親＋子を SaveAsync→再取得で一致、子を MarkRemoved→SaveAsync で削除</summary>
    [Fact(
        DisplayName = "[Integration] 4: SaveAsync グラフ保存（親＋子の追加・子の削除）が再取得で一致する"
    )]
    public async Task SaveAsync_PersistsAndRemovesGraph()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();

        var customers = new CustomerRepository(Factory());
        var orders = new OrderRepository(Factory());

        // Added の親＋子（2 件）をグラフ保存する
        var customer = NewCustomer(1, "Alice");
        customer.MarkAdded();
        var order10 = NewOrder(10, 1, 10m);
        order10.MarkAdded();
        var order11 = NewOrder(11, 1, 20m);
        order11.MarkAdded();
        customer.Orders.Add(order10);
        customer.Orders.Add(order11);

        var savedRows = await customers.SaveAsync(customer, cancellationToken: Ct);
        savedRows.Should().Be(3, "親 1 件＋子 2 件が挿入される");

        // 再取得（Include）で親子が一致する
        var reloaded = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        reloaded.Should().NotBeNull();
        reloaded!.Orders.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11]);

        // 子 1 件を MarkRemoved → SaveAsync で削除される
        var toRemove = reloaded.Orders.Single(o => o.OrderId.Value == 10);
        toRemove.MarkRemoved();
        var removedRows = await customers.SaveAsync(reloaded, cancellationToken: Ct);
        removedRows.Should().Be(1, "子 1 件の削除");

        var remaining = await orders.GetAllAsync(Ct);
        remaining.Select(o => o.OrderId.Value).Should().BeEquivalentTo([11]);
    }

    /// <summary>5a. 楽観排他: 他者が削除済みのレコードを SaveAsync（Updated）すると SaveConflictException</summary>
    [Fact(
        DisplayName = "[Integration] 5a: 他者削除後の SaveAsync（更新）が SaveConflictException を投げる"
    )]
    public async Task SaveAsync_MissingUpdateTarget_ThrowsSaveConflict()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();

        var repo = new CustomerRepository(Factory());
        await repo.InsertAsync(NewCustomer(1, "Alice"), Ct);

        // 取得したうえで別経路（他ユーザー相当）で削除する
        var loaded = await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct);
        loaded.Should().NotBeNull();
        (await repo.DeleteAsync(CustomerIdValue.Create(1), Ct)).Should().BeTrue();

        // 手元のエンティティを更新扱いにして保存 → 更新対象が無く競合
        loaded!.Name = NameValue.Create("Alice Renamed");
        loaded.MarkUpdated();

        var act = async () => await repo.SaveAsync(loaded, cancellationToken: Ct);
        await act.Should().ThrowAsync<SaveConflictException>();
    }

    /// <summary>5b. insertWhenUpdateMissing=true: 更新対象が無い場合に INSERT へ切り替わる</summary>
    [Fact(
        DisplayName = "[Integration] 5b: insertWhenUpdateMissing=true で更新対象なしが INSERT に切り替わる"
    )]
    public async Task SaveAsync_InsertWhenUpdateMissing_InsertsRow()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();

        var repo = new CustomerRepository(Factory());

        // DB には存在しないが Updated 状態のエンティティ
        var entity = NewCustomer(1, "Alice");
        entity.MarkUpdated();

        var rows = await repo.SaveAsync(
            entity,
            insertWhenUpdateMissing: true,
            cancellationToken: Ct
        );
        rows.Should().Be(1);

        var loaded = await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct);
        loaded.Should().NotBeNull();
        loaded!.Name.Value.Should().Be("Alice");
    }
}
