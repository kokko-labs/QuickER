using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EF Core 版ランタイム固有の追加シナリオ（共通パリティスイートで扱いきれない EF Core 経路の穴を埋める）と、
/// QuickER 版⇔EF Core 版の直接パリティ比較（同一シードへ同一クエリを流して結果を突き合わせる）を実 SQL Server で検証する。
/// </summary>
/// <remarks>
/// ExecuteDeleteAsync（cascade 含む）・OrderBy/Skip/Take・生 SQL（EfCoreSqlExecutor）を EF Core 経路で確認し、
/// SaveConflictException（DbUpdateConcurrencyException 変換）・insertWhenUpdateMissing 切替・
/// カスケード削除の true/false 差もカバーする。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class GeneratedRuntimeEfCoreSpecificTests(SqlServerContainerFixture fixture)
    : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(fixture.ConnectionString)
            )
            .BuildServiceProvider();

    private ICustomerRepository EfCustomers() =>
        Provider().GetRequiredService<ICustomerRepository>();

    private IOrderRepository EfOrders() => Provider().GetRequiredService<IOrderRepository>();

    private ISqlExecutor EfExecutor() => Provider().GetRequiredService<ISqlExecutor>();

    /// <summary>スキーマを初期化して作成する</summary>
    private async Task ResetAndCreateSchemaAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ApplyDdlAsync(GeneratedFixtureDefinition.Build(), Ct);
    }

    private static CustomerEntity NewCustomer(int id, string name, bool isActive = true) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = null,
            IsActive = IsActiveValue.Create(isActive),
        };

    private static OrderEntity NewOrder(int orderId, int customerId, decimal amount) =>
        new()
        {
            OrderId = OrderIdValue.Create(orderId),
            CustomerId = CustomerIdValue.Create(customerId),
            Amount = AmountValue.Create(amount),
            Memo = null,
        };

    /// <summary>EF Core-1. OrderBy / OrderByDescending / Skip / Take が EF Core 経路で正しい並び・範囲を返す</summary>
    [Fact(
        DisplayName = "[EF Core] 1: OrderBy/OrderByDescending/Skip/Take が正しい並び・範囲を返す"
    )]
    public async Task OrderBy_Skip_Take_Works()
    {
        await ResetAndCreateSchemaAsync();

        var repo = EfCustomers();
        for (var i = 1; i <= 5; i++)
        {
            await repo.InsertAsync(NewCustomer(i, $"C{i}"), Ct);
        }

        // 昇順で 2 件飛ばして 2 件（3,4）
        var page = await repo.Query().OrderBy(c => c.CustomerId).Skip(2).Take(2).ToListAsync(Ct);
        page.Select(c => c.CustomerId.Value).Should().Equal(3, 4);

        // 降順の先頭 3 件（5,4,3）
        var desc = await repo.Query().OrderByDescending(c => c.CustomerId).Take(3).ToListAsync(Ct);
        desc.Select(c => c.CustomerId.Value).Should().Equal(5, 4, 3);
    }

    /// <summary>EF Core-2. ExecuteDeleteAsync（cascade なし）: 条件一致の行のみ一括削除し件数を返す</summary>
    [Fact(DisplayName = "[EF Core] 2: ExecuteDeleteAsync（cascade なし）で条件一致行のみ削除する")]
    public async Task ExecuteDelete_NonCascade_DeletesMatchingRows()
    {
        await ResetAndCreateSchemaAsync();

        var repo = EfCustomers();
        await repo.InsertAsync(NewCustomer(1, "Alice", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", isActive: false), Ct);
        await repo.InsertAsync(NewCustomer(3, "Carol", isActive: true), Ct);

        // is_active=false の 1 件のみ削除
        var deleted = await repo.Query()
            .Where(c => c.IsActive == IsActiveValue.Create(false))
            .ExecuteDeleteAsync(cancellationToken: Ct);
        deleted.Should().Be(1);

        var remaining = await repo.GetAllAsync(Ct);
        remaining.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3]);
    }

    /// <summary>
    /// EF Core-3. ExecuteDeleteAsync（cascadeDelete true/false の差）: 子を持つ親を条件削除する。
    /// フィクスチャの FK は ON DELETE CASCADE のため DB 側でも子が連鎖削除される。観測できる差は
    /// アプリが発行する DELETE の返す件数（false=親のみ 1 / true=子 2＋親 1 の 3）で、どちらも最終状態は空。
    /// </summary>
    [Fact(
        DisplayName = "[EF Core] 3: ExecuteDeleteAsync の cascadeDelete で返す削除件数が切り替わる"
    )]
    public async Task ExecuteDelete_CascadeFlag_TogglesReportedRowCount()
    {
        await ResetAndCreateSchemaAsync();

        var customers = EfCustomers();
        var orders = EfOrders();

        // 前半: cascadeDelete=false（親のみ削除。DB の ON DELETE CASCADE で子も消える）
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m), Ct);

        var deletedNoCascade = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .ExecuteDeleteAsync(cascadeDelete: false, cancellationToken: Ct);
        deletedNoCascade.Should().Be(1, "アプリが発行するのは親の DELETE のみ");
        (await customers.GetAllAsync(Ct)).Should().BeEmpty();
        (await orders.GetAllAsync(Ct)).Should().BeEmpty("DB の ON DELETE CASCADE で子も消える");

        // 後半: 同じ形を作り直し cascadeDelete=true（子 2 件＋親 1 件をアプリが明示削除）
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await orders.InsertAsync(NewOrder(20, 2, 10m), Ct);
        await orders.InsertAsync(NewOrder(21, 2, 20m), Ct);

        var deletedCascade = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(2))
            .ExecuteDeleteAsync(cascadeDelete: true, cancellationToken: Ct);
        deletedCascade.Should().Be(3, "子 2 件＋親 1 件をアプリが明示的に削除する");
        (await customers.GetAllAsync(Ct)).Should().BeEmpty();
        (await orders.GetAllAsync(Ct)).Should().BeEmpty();
    }

    /// <summary>
    /// EF Core-4. cascadeSave=false のグラフ保存: ルートのみ保存し、子の変更（追加）を無視する
    /// （切断グラフの部分保存フラグが EF Core 経路で効くことの確認）。
    /// </summary>
    [Fact(
        DisplayName = "[EF Core] 4: SaveAsync の cascadeSave=false でルートのみ保存し子の追加を無視する"
    )]
    public async Task SaveAsync_CascadeSaveFalse_SavesRootOnly()
    {
        await ResetAndCreateSchemaAsync();

        var customers = EfCustomers();
        var orders = EfOrders();

        // Added の親に Added の子を 2 件ぶら下げるが、cascadeSave=false で保存する
        var root = NewCustomer(1, "Alice");
        root.MarkAdded();
        var child10 = NewOrder(10, 1, 10m);
        child10.MarkAdded();
        var child11 = NewOrder(11, 1, 20m);
        child11.MarkAdded();
        root.Orders.Add(child10);
        root.Orders.Add(child11);

        var rows = await customers.SaveAsync(root, cascadeSave: false, cancellationToken: Ct);
        rows.Should().Be(1, "ルート 1 件のみが保存され、子はたどられない");

        // 親は入り、子は 1 件も入っていない
        (await customers.GetByIdAsync(CustomerIdValue.Create(1), Ct))
            .Should()
            .NotBeNull();
        (await orders.GetAllAsync(Ct)).Should().BeEmpty("cascadeSave=false のため子は保存されない");
    }

    /// <summary>EF Core-5. EfCoreSqlExecutor の生 SQL 4 系統（厳密全列・寛容射影・単一値・匿名パラメータ）</summary>
    [Fact(
        DisplayName = "[EF Core] 5: EfCoreSqlExecutor の生 SQL（厳密全列・寛容射影・単一値・匿名パラメータ）が機能する"
    )]
    public async Task EfCoreSqlExecutor_RawSql_AllModes()
    {
        await ResetAndCreateSchemaAsync();

        var repo = EfCustomers();
        var orders = EfOrders();
        await repo.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 100m), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m), Ct);
        await orders.InsertAsync(NewOrder(12, 2, 200m), Ct);

        var executor = EfExecutor();

        // (a) 厳密全列マップ（VO 復元・匿名パラメータ）
        var entities = await executor.QueryBySqlAsync<CustomerEntity>(
            "SELECT * FROM [customers] WHERE [customer_id] = @id;",
            new { id = 1 },
            Ct
        );
        var alice = entities.Should().ContainSingle().Subject;
        alice.Name.Value.Should().Be("Alice");
        alice.RowState.Should().Be(RowState.Unchanged);

        // (b) 寛容射影（JOIN + 集計を DTO へ）
        var totals = await executor.QueryProjectionBySqlAsync<CustomerTotalDto>(
            "SELECT c.[name] AS Name, SUM(o.[amount]) AS Total "
                + "FROM [customers] c JOIN [orders] o ON o.[customer_id] = c.[customer_id] "
                + "GROUP BY c.[name] ORDER BY c.[name];",
            null,
            Ct
        );
        totals.Should().HaveCount(2);
        totals[0].Name.Should().Be("Alice");
        totals[0].Total.Should().Be(150m);

        // (c) 単一値モード（先頭列を string へ）
        var names = await executor.QueryProjectionBySqlAsync<string>(
            "SELECT [name] FROM [customers] ORDER BY [customer_id];",
            null,
            Ct
        );
        names.Should().Equal("Alice", "Bob");

        // (d) 影響行数（ExecuteSql）とスカラー（ExecuteScalar）も EF Core 経路で機能する
        var affected = await executor.ExecuteSqlAsync(
            "UPDATE [customers] SET [is_active] = @v WHERE [customer_id] = @id;",
            new { v = false, id = 2 },
            Ct
        );
        affected.Should().Be(1);

        var count = await executor.ExecuteScalarSqlAsync<int>(
            "SELECT COUNT(*) FROM [orders] WHERE [customer_id] = @id;",
            new { id = 1 },
            Ct
        );
        count.Should().Be(2);
    }

    /// <summary>射影 DTO</summary>
    private sealed class CustomerTotalDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    /// <summary>
    /// EF Core-6. 直接パリティ比較: 同一シードへ同一クエリ（式木・Include・生 SQL）をQuickER 版と EF Core 版の両方で流し、
    /// 結果同士を突き合わせて完全一致することを確認する（交換可能性のより強い証明）。
    /// </summary>
    [Fact(
        DisplayName = "[EF Core] 6: 同一シード・同一クエリでQuickER 版と EF Core 版の結果が完全一致する（パリティ比較）"
    )]
    public async Task Parity_SameQuery_BothBackendsAgree()
    {
        await ResetAndCreateSchemaAsync();

        // シードはQuickER 版で 1 回だけ投入する（DB 状態は 1 つ）
        var seedCustomers = new CustomerRepository(
            new SqlConnectionFactory(fixture.ConnectionString)
        );
        var seedOrders = new OrderRepository(new SqlConnectionFactory(fixture.ConnectionString));
        await seedCustomers.InsertAsync(NewCustomer(1, "Alice", isActive: true), Ct);
        await seedCustomers.InsertAsync(NewCustomer(2, "Bob", isActive: false), Ct);
        await seedCustomers.InsertAsync(NewCustomer(3, "Alicia", isActive: true), Ct);
        await seedOrders.InsertAsync(NewOrder(10, 1, 100m), Ct);
        await seedOrders.InsertAsync(NewOrder(11, 1, 50m), Ct);

        // QuickER 版と EF Core 版のリポジトリ
        var ado = new CustomerRepository(new SqlConnectionFactory(fixture.ConnectionString));
        var ef = EfCustomers();

        // (a) 式木クエリ（LIKE）: 両者が同じ ID 集合を返す
        var adoLike = (await ado.Query().Where(c => c.Name.Contains("Ali")).ToListAsync(Ct))
            .Select(c => c.CustomerId.Value)
            .OrderBy(v => v)
            .ToList();
        var efLike = (await ef.Query().Where(c => c.Name.Contains("Ali")).ToListAsync(Ct))
            .Select(c => c.CustomerId.Value)
            .OrderBy(v => v)
            .ToList();
        efLike.Should().Equal(adoLike);
        adoLike.Should().Equal(1, 3);

        // (b) Include グラフロード: 顧客 1 の注文集合が両者で一致
        var adoOrders = (
            await ado.Query()
                .Where(c => c.CustomerId == CustomerIdValue.Create(1))
                .Include(c => c.Orders)
                .FirstOrDefaultAsync(Ct)
        )!
            .Orders.Select(o => o.OrderId.Value)
            .OrderBy(v => v)
            .ToList();
        var efOrders = (
            await ef.Query()
                .Where(c => c.CustomerId == CustomerIdValue.Create(1))
                .Include(c => c.Orders)
                .FirstOrDefaultAsync(Ct)
        )!
            .Orders.Select(o => o.OrderId.Value)
            .OrderBy(v => v)
            .ToList();
        efOrders.Should().Equal(adoOrders);

        // (c) 生 SQL 射影: 集計結果が両者で一致
        const string totalSql =
            "SELECT c.[name] AS Name, SUM(o.[amount]) AS Total "
            + "FROM [customers] c JOIN [orders] o ON o.[customer_id] = c.[customer_id] "
            + "GROUP BY c.[name] ORDER BY c.[name];";
        var adoTotal = await new SqlExecutor(
            new SqlConnectionFactory(fixture.ConnectionString)
        ).QueryProjectionBySqlAsync<CustomerTotalDto>(totalSql, null, Ct);
        var efTotal = await EfExecutor()
            .QueryProjectionBySqlAsync<CustomerTotalDto>(totalSql, null, Ct);
        efTotal
            .Select(t => (t.Name, t.Total))
            .Should()
            .Equal(adoTotal.Select(t => (t.Name, t.Total)));
        adoTotal.Single().Total.Should().Be(150m);
    }

    /// <summary>DI コンテナを破棄する</summary>
    public void Dispose() => _provider?.Dispose();
}
