using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.Tests.GeneratedPortableFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// 方言非依存の生成物（EF 版 <see cref="QuickErDbContext"/> + <c>EfCore{Entity}Repository</c> + VO 翻訳プラグイン）が
/// 実 PostgreSQL / MySQL / Oracle（Testcontainers）で動くことを証明する共通シナリオの基底。
/// </summary>
/// <remarks>
/// <para>
/// SQL Server 上で証明済みのパリティスイートと同じ骨子を、方言可搬フィクスチャ
/// （<see cref="PortableFixtureDefinition"/>・rowversion なし・int/string/decimal のみ）で流す。
/// リポジトリ・エグゼキュータは実運用と同じ DI 経路（<c>AddGeneratedEfCoreRepositories</c> →
/// 方言の <c>UseNpgsql/UseMySQL/UseOracle</c>）で解決する。
/// </para>
/// <para>
/// スキーマはその方言の QuickER DdlGenerator が生成した DDL をコンテナに流して用意する。生 SQL は
/// 利用者が方言ごとに書く前提のため、識別子引用（<see cref="Quote"/>）とプレースホルダ（<see cref="Param"/>）を
/// 派生が方言に合わせて与える。
/// </para>
/// </remarks>
public abstract class GeneratedEfCoreDialectRuntimeTestsBase
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // --- 派生クラスが与える方言固有の要素 ---

    /// <summary>顧客リポジトリを DI から解決する</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを DI から解決する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>エンティティ非依存の生 SQL 実行器を DI から解決する</summary>
    protected abstract ISqlExecutor CreateSqlExecutor();

    /// <summary>スキーマを初期化し、方言の DdlGenerator が生成した DDL でテーブルを作成する</summary>
    protected abstract Task ResetAndCreateSchemaAsync();

    /// <summary>識別子（テーブル名・列名）を方言の規則で引用する</summary>
    protected abstract string Quote(string identifier);

    /// <summary>パラメータ名を方言のプレースホルダ表記へ変換する（@名 / :名）</summary>
    protected abstract string Param(string name);

    // --- エンティティ組み立てヘルパー ---

    /// <summary>指定 ID の顧客エンティティを組み立てる（VO は Create で検証生成）</summary>
    protected static CustomerEntity NewCustomer(int id, string name, decimal? balance = null) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = balance is null ? null : BalanceValue.Create(balance.Value),
        };

    /// <summary>指定 ID の注文エンティティを組み立てる</summary>
    protected static OrderEntity NewOrder(
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

    // ==================== 共通シナリオ ====================

    /// <summary>1. CRUD 往復（VO 込み）: Insert→GetById→Update→Delete で値・VO が正しく復元される</summary>
    [Fact(DisplayName = "[Dialect] 1: CRUD 往復（VO 込み）で値と VO が正しく復元される")]
    public async Task Crud_RoundTrips_WithValueObjects()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();

        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 123.45m), Ct);

        var loaded = await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct);
        loaded.Should().NotBeNull();
        loaded!.CustomerId.Value.Should().Be(1);
        loaded.Name.Value.Should().Be("Alice");
        loaded.Balance.Should().NotBeNull();
        loaded.Balance!.Value.Should().Be(123.45m);

        loaded.Name = NameValue.Create("Alice Updated");
        loaded.Balance = null;
        (await repo.UpdateAsync(loaded, Ct)).Should().BeTrue();

        var reloaded = await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct);
        reloaded!.Name.Value.Should().Be("Alice Updated");
        reloaded.Balance.Should().BeNull("NULL 許容の decimal 列を null 更新したため");

        (await repo.DeleteAsync(CustomerIdValue.Create(1), Ct)).Should().BeTrue();
        (await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct)).Should().BeNull();
    }

    /// <summary>
    /// 2. 式木クエリ（VO の Contains・.Value 比較を含む）: 翻訳プラグインが方言横断で正しい行を返す。
    /// LIKE のワイルドカード（% _）のリテラル一致（エスケープ）も検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Dialect] 2: 式木クエリ（VO の Contains・.Value 比較・% _ エスケープ）が正しい行を返す"
    )]
    public async Task Where_ValueObjectPredicates_TranslateAcrossDialects()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m), Ct);
        await repo.InsertAsync(NewCustomer(3, "Alicia", balance: 300m), Ct);
        await repo.InsertAsync(NewCustomer(4, "50%OFF"), Ct);
        await repo.InsertAsync(NewCustomer(5, "A_B"), Ct);
        await repo.InsertAsync(NewCustomer(6, "AxB"), Ct);

        // (a) 文字列 VO の Contains → LIKE '%...%'
        var likeAli = await repo.Query().Where(c => c.Name.Contains("Ali")).ToListAsync(Ct);
        likeAli.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3]);

        // (b) % のリテラル一致（エスケープ）: ワイルドカード扱いだと余計な行まで一致してしまう
        var percent = await repo.Query().Where(c => c.Name.Contains("0%")).ToListAsync(Ct);
        percent.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([4]);

        // (c) _ のリテラル一致（エスケープ）: 未エスケープの LIKE '%A_B%' は AxB(6) にも一致してしまう
        var underscore = await repo.Query().Where(c => c.Name.Contains("A_B")).ToListAsync(Ct);
        underscore.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([5]);

        // (d) 変数パターン（パラメータ束縛の経路でも同じエスケープ規則が効く）
        var keyword = "0%";
        var byVariable = await repo.Query().Where(c => c.Name.Contains(keyword)).ToListAsync(Ct);
        byVariable.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([4]);

        // (e) VO の .Value を開いた等値比較（string VO）
        var byValue = await repo.Query().Where(c => c.Name.Value == "Bob").ToListAsync(Ct);
        byValue.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);

        // (f) VO の .Value を開いた数値比較（decimal VO・NULL 行は不一致のまま）
        var byBalance = await repo.Query().Where(c => c.Balance!.Value >= 150m).ToListAsync(Ct);
        byBalance.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 3]);

        // (g) VO 引数のオーバーロード（TSelf）: 素値へ開いて部分一致
        var byVo = await repo.Query()
            .Where(c => c.Name.Contains(NameValue.Create("lic")))
            .ToListAsync(Ct);
        byVo.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3]);
    }

    /// <summary>
    /// 2b. LIKE の <c>[</c> を含む Contains（予見リスク: SQL Server は文字クラス開始のためエスケープ必須。
    /// Oracle は非ワイルドカードのエスケープを ORA-01424 で拒否する）。方言横断でリテラル一致することを検証する。
    /// </summary>
    [Fact(DisplayName = "[Dialect] 2b: LIKE の [ を含む Contains が方言横断でリテラル一致する")]
    public async Task Where_Contains_LeftBracket_LiteralMatch()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "a[b]c"), Ct);
        await repo.InsertAsync(NewCustomer(2, "abc"), Ct);
        await repo.InsertAsync(NewCustomer(3, "x[y"), Ct);

        // "[b]" を含む行のみ一致すべき（SQL Server で未エスケープだと [b] が文字クラスになり誤一致する）
        var bracket = await repo.Query().Where(c => c.Name.Contains("[b]")).ToListAsync(Ct);
        bracket.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1]);

        // 変数パターン経路でも同じ
        var pattern = "[y";
        var byVariable = await repo.Query().Where(c => c.Name.Contains(pattern)).ToListAsync(Ct);
        byVariable.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([3]);
    }

    /// <summary>3. Include グラフロード: 親→子コレクション・子→親参照の Include 復元</summary>
    [Fact(DisplayName = "[Dialect] 3: Include（親→子コレクション・子→親参照）が正しく復元される")]
    public async Task Include_LoadsNavigations()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, "a"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, "b"), Ct);

        var customer = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        customer.Should().NotBeNull();
        customer!.Orders.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11]);

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

    /// <summary>4. TrackGraph グラフ保存（追加・更新・削除混在）: SaveAsync で再取得結果が一致する</summary>
    [Fact(
        DisplayName = "[Dialect] 4: TrackGraph グラフ保存（追加・更新・削除混在）が再取得で一致する"
    )]
    public async Task SaveAsync_PersistsMixedGraph()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        // まず親＋子 2 件を追加保存する
        var customer = NewCustomer(1, "Alice");
        customer.MarkAdded();
        var order10 = NewOrder(10, 1, 10m, null);
        order10.MarkAdded();
        var order11 = NewOrder(11, 1, 20m, null);
        order11.MarkAdded();
        customer.Orders.Add(order10);
        customer.Orders.Add(order11);

        (await customers.SaveAsync(customer, cancellationToken: Ct))
            .Should()
            .Be(3, "親 1 件＋子 2 件が挿入される");

        // 再取得（Include）で親子が一致する
        var reloaded = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        reloaded.Should().NotBeNull();
        reloaded!.Orders.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11]);

        // 追加・更新・削除の混在グラフを 1 回の SaveAsync で保存する
        reloaded.Name = NameValue.Create("Alice Updated");
        reloaded.MarkUpdated();
        var order10Reloaded = reloaded.Orders.Single(o => o.OrderId.Value == 10);
        order10Reloaded.MarkRemoved();
        var order12 = NewOrder(12, 1, 30m, null);
        order12.MarkAdded();
        reloaded.Orders.Add(order12);

        // 更新 1（親）＋削除 1（子10）＋追加 1（子12）＝ 3 行
        (await customers.SaveAsync(reloaded, cancellationToken: Ct))
            .Should()
            .Be(3, "親の更新 1＋子の削除 1＋子の追加 1");

        var finalCustomer = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        finalCustomer!.Name.Value.Should().Be("Alice Updated");
        finalCustomer.Orders.Select(o => o.OrderId.Value).Should().BeEquivalentTo([11, 12]);
    }

    /// <summary>5. ExecuteDeleteAsync（cascadeDelete=true）: 子を持つ親を子ごとアプリ削除し、件数と最終状態が一致する</summary>
    [Fact(
        DisplayName = "[Dialect] 5: ExecuteDeleteAsync（cascadeDelete=true）で子ごと削除し件数が一致する"
    )]
    public async Task ExecuteDelete_Cascade_DeletesChildrenAndParent()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, null), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, null), Ct);

        var deleted = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .ExecuteDeleteAsync(cascadeDelete: true, cancellationToken: Ct);
        deleted.Should().Be(3, "子 2 件＋親 1 件をアプリが明示削除する");

        (await customers.GetAllAsync(Ct)).Should().BeEmpty();
        (await orders.GetAllAsync(Ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 6. 生 SQL 4 系統（Repository の QueryBySqlAsync 厳密／SqlExecutor の QueryProjectionBySqlAsync 寛容＋単一値／
    /// ExecuteSqlAsync／匿名オブジェクトパラメータ）。SQL は方言に合わせて識別子引用・プレースホルダを切り替える。
    /// </summary>
    [Fact(
        DisplayName = "[Dialect] 6: 生 SQL（厳密全列・寛容射影・単一値・影響行数・匿名パラメータ）が機能する"
    )]
    public virtual async Task RawSql_AllModes()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        var orders = CreateOrderRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 100m, null), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m, null), Ct);
        await orders.InsertAsync(NewOrder(12, 2, 200m, null), Ct);

        var customers = Quote("customers");
        var ordersT = Quote("orders");
        var customerId = Quote("customer_id");
        var name = Quote("name");
        var balance = Quote("balance");
        var amount = Quote("amount");

        // (a) Repository.QueryBySqlAsync（厳密全列・VO 復元・匿名パラメータ）
        var rows = await repo.QueryBySqlAsync(
            $"SELECT * FROM {customers} WHERE {balance} >= {Param("minBalance")} ORDER BY {customerId}",
            new { minBalance = 150m },
            Ct
        );
        rows.Select(c => c.Name.Value).Should().BeEquivalentTo(["Bob"]);
        rows.Single().RowState.Should().Be(RowState.Unchanged);

        // (b) SqlExecutor.QueryProjectionBySqlAsync（JOIN + 集計を DTO へ寛容射影）
        var executor = CreateSqlExecutor();
        var totals = await executor.QueryProjectionBySqlAsync<CustomerTotal>(
            $"SELECT c.{name} AS Name, SUM(o.{amount}) AS Total "
                + $"FROM {customers} c JOIN {ordersT} o ON o.{customerId} = c.{customerId} "
                + $"GROUP BY c.{name} ORDER BY c.{name}",
            null,
            Ct
        );
        totals.Should().HaveCount(2);
        totals[0].Name.Should().Be("Alice");
        totals[0].Total.Should().Be(150m);
        totals[1].Name.Should().Be("Bob");
        totals[1].Total.Should().Be(200m);

        // (c) QueryProjectionBySqlAsync（単一値モード: string と VO 型）
        var names = await executor.QueryProjectionBySqlAsync<string>(
            $"SELECT {name} FROM {customers} ORDER BY {customerId}",
            null,
            Ct
        );
        names.Should().BeEquivalentTo(["Alice", "Bob"], o => o.WithStrictOrdering());

        var voNames = await executor.QueryProjectionBySqlAsync<NameValue>(
            $"SELECT {name} FROM {customers} ORDER BY {customerId}",
            null,
            Ct
        );
        voNames.Select(v => v.Value).Should().Equal("Alice", "Bob");

        // (d) ExecuteSqlAsync（UPDATE 影響行数・匿名パラメータ束縛）
        var affected = await repo.ExecuteSqlAsync(
            $"UPDATE {customers} SET {balance} = {Param("v")} WHERE {balance} >= {Param("min")}",
            new { v = 0m, min = 150m },
            Ct
        );
        affected.Should().Be(1);

        // (e) ExecuteScalarSqlAsync（COUNT を int）
        var count = await repo.ExecuteScalarSqlAsync<int>(
            $"SELECT COUNT(*) FROM {customers}",
            null,
            Ct
        );
        count.Should().Be(2);
    }

    /// <summary>7. SqlExecutor.QueryBySqlAsync&lt;TEntity&gt;（厳密全列マップ・VO 復元・匿名パラメータ）</summary>
    [Fact(
        DisplayName = "[Dialect] 7: SqlExecutor.QueryBySqlAsync<TEntity>（厳密全列・VO 復元・匿名パラメータ）"
    )]
    public async Task SqlExecutor_QueryBySql_MapsEntity()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m), Ct);

        var executor = CreateSqlExecutor();
        var customers = Quote("customers");
        var customerId = Quote("customer_id");

        var entities = await executor.QueryBySqlAsync<CustomerEntity>(
            $"SELECT * FROM {customers} WHERE {customerId} = {Param("id")}",
            new { id = 1 },
            Ct
        );
        var alice = entities.Should().ContainSingle().Subject;
        alice.CustomerId.Value.Should().Be(1);
        alice.Name.Value.Should().Be("Alice");
        alice.Balance!.Value.Should().Be(100m);
        alice.RowState.Should().Be(RowState.Unchanged);
    }

    /// <summary>射影 DTO（JOIN + 集計を寛容マップで受ける先）</summary>
    private sealed class CustomerTotal
    {
        public string Name { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }
}
