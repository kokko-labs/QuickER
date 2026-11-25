using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedUniquenessSqlServerFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 名前付きクエリの自由 SQL の戻り形の表（<see cref="NamedQueryRawSqlRows"/>）を、<b>QuickER 版 Repository（SQL Server 方言）</b>で
/// 実 SQL Server（Testcontainers・Docker 依存）に流す派生。
/// </summary>
/// <remarks>
/// <para>
/// 入力は <see cref="UniquenessSqlServerFixtureDefinition"/>（クエリフィクスチャと同一の図に、自由 SQL の SQL Server の SQL を足したもの）。
/// SQL Server の生 SQL のバインダ（パラメータの束縛・コレクションの IN の展開）は、SQLite の派生とは別の生成物を通るので、
/// 実 SQL Server で流すのはこのクラスだけ。SQL Server だけの行（IN の展開の置換の境界）も流す。
/// </para>
/// <para>
/// 生成物の型がクエリフィクスチャと別の名前空間なので、アダプタ（<see cref="INamedQueryRawSqlBoundaryBackend"/>）を SQL Server の型で
/// 書き下す（<see cref="NamedQueryRawSqlRuntimeTestsBase"/> と同じ内容）。シードも基底と同じ 4 件の注文。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(RuntimeFeatures.NamedQueryRawSql, RuntimeTarget.AdoSqlServer)]
public sealed class NamedQueryRawSqlAdoSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : INamedQueryRawSqlBoundaryBackend,
        IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER の SQL Server リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        return ValueTask.CompletedTask;
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>自由 SQL の戻り形の表の 1 行（SQL Server だけの行を含む全行）</summary>
    /// <param name="id">行の Id</param>
    [Theory(DisplayName = "[NamedQuery/SqlServer] 自由 SQL の戻り形の表の行が成り立つ")]
    [MemberData(
        nameof(NamedQueryRawSqlRows.SqlServerIds),
        MemberType = typeof(NamedQueryRawSqlRows)
    )]
    public Task RawSqlRow_Holds(string id) => NamedQueryRawSqlRows.RunAsync(id, this);

    /// <summary>注文リポジトリを解決する</summary>
    private IOrderRepository Orders() => _provider.GetRequiredService<IOrderRepository>();

    async Task INamedQueryRawSqlBackend.ResetAndSeedAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(UniquenessSqlServerFixtureDefinition.Build(), Ct);

        var customers = _provider.GetRequiredService<ICustomerRepository>();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await Orders().InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await Orders().InsertAsync(NewOrder(11, 1, 50m, "banana"), Ct);
        await Orders().InsertAsync(NewOrder(12, 2, 200m, "apple juice"), Ct);
        await Orders().InsertAsync(NewOrder(13, 1, 75m, null), Ct);
    }

    Task<decimal?> INamedQueryRawSqlBackend.SumAmountsAsync(int customerId) =>
        Orders().SumAmountsAsync(customerId, Ct);

    async Task<IReadOnlyList<int>> INamedQueryRawSqlBackend.GetByIdsRawAsync(
        IReadOnlyList<int> ids
    ) => (await Orders().GetByIdsRawAsync(ids, Ct)).Select(order => order.OrderId.Value).ToList();

    async Task<NamedQueryRawSqlOrderRow?> INamedQueryRawSqlBackend.FindTopRawAsync() =>
        await Orders().FindTopRawAsync(Ct) is { } order
            ? new NamedQueryRawSqlOrderRow(order.OrderId.Value, order.Memo?.Value)
            : null;

    Task<int> INamedQueryRawSqlBackend.CountByCustomerRawAsync(int customerId) =>
        Orders().CountByCustomerRawAsync(customerId, Ct);

    async Task<
        IReadOnlyList<NamedQueryRawSqlOrderRow>
    > INamedQueryRawSqlBackend.GetMemoRowsRawAsync(int customerId) =>
        (await Orders().GetMemoRowsRawAsync(customerId, Ct))
            .Select(row => new NamedQueryRawSqlOrderRow(row.OrderId, row.Memo))
            .ToList();

    Task INamedQueryRawSqlBackend.DeleteAllOrdersAsync() =>
        Orders().ExecuteSqlAsync("DELETE FROM [orders]", null, Ct);

    async Task<IReadOnlyList<int>> INamedQueryRawSqlBoundaryBackend.GetByIdsBoundaryRawAsync(
        IReadOnlyList<int> rowcount,
        int rowcountMin
    ) =>
        (await Orders().GetByIdsBoundaryRawAsync(rowcount, rowcountMin, Ct))
            .Select(order => order.OrderId.Value)
            .ToList();

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
}
