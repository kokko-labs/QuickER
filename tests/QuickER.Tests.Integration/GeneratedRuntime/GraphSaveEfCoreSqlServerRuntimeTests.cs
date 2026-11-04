using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ保存の J3（削除する行の一意な値を別の行の更新が取る）を、<b>EF Core 版 Repository を実 SQL Server</b>
/// （Testcontainers・Docker 依存）で流す。
/// </summary>
/// <remarks>
/// <para>
/// EF Core 版の DbContext は方言に依らないため、クエリフィクスチャ（SQLite 方言で生成）の <c>QuickErDbContext</c> を
/// <c>UseSqlServer</c> でそのまま使い、スキーマは同じ図から作った SQL Server の DDL で用意する。Docker 不在時は
/// <see cref="SqlServerContainerFixture"/> の検出でスキップされる。
/// </para>
/// <para>
/// グラフ保存のほかのシナリオ（カスケードの閉包だけを保存する・付け替えと元の親の削除）は、SQLite の実装先どうしの一致
/// （<see cref="BackendParityRuntimeTests"/> の <c>graph-include-*</c> ほか）と、SQL Server の 2 者比較
/// （<see cref="BackendParitySqlServerRuntimeTests"/> の <c>include-child-delete-keeps-parent-and-siblings</c>・
/// <c>move-note-and-delete-old-gadget-kept-first</c>）が持つ。代替キーでない一意な値の J3 を SQL Server の EF Core で見るのは
/// このクラスだけ。SQL Server の 2 者比較の図（<c>ConcurrencyFixture</c>）の一意な列は代替キー（kit_parts が参照する kits の
/// code）しか無く、EF Core は代替キーを <c>HasIndex(...).IsUnique()</c> で並べない（自分で書き、削除する行の値を取るときは後回しの
/// 行を 2 つめの文脈で削除の後に保存する＝<c>alternate-key-taken-from-deleted-kit-*</c>）。そのため、一意インデックスを外す
/// 壊し方を SQL Server で捕まえる形はここにしか作れない（注文のメモは代替キーでない一意制約）。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class GraphSaveEfCoreSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>テストのキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlServer）</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();

        return ValueTask.CompletedTask;
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 区切り 43（判断 D・J3）: 削除する注文（明細を持つ）の一意な値（メモ）を取る更新は、根の並びに依らず成功する（EF Core は
    /// 1 回の SaveChanges の中で自分で並べる。一意インデックスが無いと、どの行にも依存しない更新が明細の削除と同じ最初の段に
    /// 並び、注文の削除より先に走る）
    /// </summary>
    [Theory(
        DisplayName = "グラフ保存: 削除する注文の一意な値を別の注文の更新で取る保存は、根の並びに依らず成功する"
    )]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdatingToDeletedRowsUniqueValue_Succeeds(bool updatedFirst)
    {
        await ResetAndSeedAsync();

        var orders = Orders();
        var deleted = await orders
            .Query()
            .Include(o => o.OrderLines)
            .GetByIdAsync(OrderIdValue.Create(10), Ct);
        deleted!.MarkRemoved();
        var updated = (await orders.GetByIdAsync(OrderIdValue.Create(12), Ct))!;
        updated.Memo = MemoValue.Create("apple pie");
        updated.MarkUpdated();
        OrderEntity[] roots = updatedFirst ? [updated, deleted] : [deleted, updated];

        await orders.SaveAsync(roots, cancellationToken: Ct);

        (await OrderIdsAsync()).Should().Equal(11, 12);
        (await orders.GetByIdAsync(OrderIdValue.Create(12), Ct))!
            .Memo?.Value.Should()
            .Be("apple pie");
        (await LineIdsAsync()).Should().Equal(102);
    }

    private ICustomerRepository Customers() => _provider.GetRequiredService<ICustomerRepository>();

    private IOrderRepository Orders() => _provider.GetRequiredService<IOrderRepository>();

    private IOrderLineRepository Lines() => _provider.GetRequiredService<IOrderLineRepository>();

    /// <summary>空のスキーマへ顧客 2・注文 3・明細 3 を投入する</summary>
    private async Task ResetAndSeedAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        await Customers().InsertAsync(NewCustomer(1, "Alice"), Ct);
        await Customers().InsertAsync(NewCustomer(2, "Bob"), Ct);

        await Orders().InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await Orders().InsertAsync(NewOrder(11, 1, 50m, null), Ct);
        await Orders().InsertAsync(NewOrder(12, 2, 30m, "banana"), Ct);

        await Lines().InsertAsync(NewLine(100, 10, "pen", 2), Ct);
        await Lines().InsertAsync(NewLine(101, 10, "ink", 5), Ct);
        await Lines().InsertAsync(NewLine(102, 12, "mug", 1), Ct);
    }

    /// <summary>保存先に残っている注文の ID（昇順）</summary>
    private async Task<IReadOnlyList<int>> OrderIdsAsync() =>
        (await Orders().Query().ToListAsync(Ct)).Select(o => o.OrderId.Value).Order().ToList();

    /// <summary>保存先に残っている明細の ID（昇順）</summary>
    private async Task<IReadOnlyList<int>> LineIdsAsync() =>
        (await Lines().Query().ToListAsync(Ct)).Select(l => l.LineId.Value).Order().ToList();

    private static CustomerEntity NewCustomer(int id, string name) =>
        new() { CustomerId = CustomerIdValue.Create(id), Name = NameValue.Create(name) };

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

    private static OrderLineEntity NewLine(
        int lineId,
        int orderId,
        string itemName,
        int quantity
    ) =>
        new()
        {
            LineId = LineIdValue.Create(lineId),
            OrderId = OrderIdValue.Create(orderId),
            ItemName = ItemNameValue.Create(itemName),
            Quantity = QuantityValue.Create(quantity),
        };
}
