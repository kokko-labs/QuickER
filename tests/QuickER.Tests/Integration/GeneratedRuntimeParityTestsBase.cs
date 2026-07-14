using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// 生成ランタイム（QuickER の SQL Server 実装と EF Core 版）を、同一シナリオで実 SQL Server（Testcontainers）に
/// 流して検証するパリティスイートの共通基底。シナリオはすべて生成インターフェイス
/// （<see cref="ICustomerRepository"/> / <see cref="IOrderRepository"/> / <see cref="ISqlExecutor"/>）
/// 経由で記述し、リポジトリ・エグゼキュータの生成方法だけを派生クラスが与える。
/// </summary>
/// <remarks>
/// これにより「DI 登録の差し替えだけで交換可能」という契約を、両バックエンドで同一アサーションにより証明する。
/// テーブルは <see cref="GeneratedFixtureDefinition"/> の図から SQL Server 方言の DDL を生成して用意する。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
public abstract class GeneratedRuntimeParityTestsBase(SqlServerContainerFixture fixture)
{
    /// <summary>共有コンテナのフィクスチャ（派生からも参照する）</summary>
    protected SqlServerContainerFixture Fixture { get; } = fixture;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // --- 派生クラスが与えるバックエンド固有のファクトリ ---

    /// <summary>顧客リポジトリを生成する（QuickER = 直接 new / EF = DbContextFactory 経由）</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>エンティティ非依存の生 SQL 実行器を生成する</summary>
    protected abstract ISqlExecutor CreateSqlExecutor();

    /// <summary>フィクスチャ図の SQL Server 方言 DDL でテーブルを作成する（各テスト冒頭で呼ぶ）</summary>
    protected async Task CreateSchemaAsync()
    {
        var ddl = new SqlServerDdlGenerator().Build(GeneratedFixtureDefinition.Build());
        await Fixture.ExecuteAsync(ddl, Ct);
    }

    /// <summary>各テスト冒頭のセットアップ（スキーマ初期化＋作成）をまとめる</summary>
    protected async Task ResetAndCreateSchemaAsync()
    {
        Assert.SkipUnless(Fixture.IsAvailable, Fixture.UnavailableReason);
        await Fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();
    }

    // --- エンティティ組み立てヘルパー ---

    /// <summary>指定 ID の顧客エンティティを組み立てる（VO は Create で検証生成）</summary>
    protected static CustomerEntity NewCustomer(
        int id,
        string name,
        decimal? balance = null,
        bool isActive = true
    )
    {
        return new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = balance is null ? null : BalanceValue.Create(balance.Value),
            IsActive = IsActiveValue.Create(isActive),
        };
    }

    /// <summary>指定 ID の注文エンティティを組み立てる</summary>
    protected static OrderEntity NewOrder(int orderId, int customerId, decimal amount, string? memo)
    {
        return new OrderEntity
        {
            OrderId = OrderIdValue.Create(orderId),
            CustomerId = CustomerIdValue.Create(customerId),
            Amount = AmountValue.Create(amount),
            Memo = memo is null ? null : MemoValue.Create(memo),
        };
    }

    // ==================== 共通シナリオ（両バックエンドで実行） ====================

    /// <summary>1. CRUD 往復: Insert → GetById（値・VO 復元一致）→ Update → Delete</summary>
    [Fact(
        DisplayName = "[Parity] 1: CRUD 往復（Insert→GetById→Update→Delete）で値と VO が正しく復元される"
    )]
    public async Task Crud_RoundTrips()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();

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
        DisplayName = "[Parity] 2: Where 式木（比較・&&・LIKE・IN・IsNullOrEmpty）が正しい行を返す"
    )]
    public async Task Where_ExpressionTree_ReturnsCorrectRows()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", isActive: false), Ct);
        await repo.InsertAsync(NewCustomer(3, "Alicia", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(4, "Carol", isActive: true), Ct);

        var orders = CreateOrderRepository();
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

    /// <summary>
    /// 2b. VO 述語の追加パリティ: LIKE ワイルドカードのリテラル一致（エスケープ）・変数パターン・
    /// VO 引数オーバーロード・.Value を開いた比較が、両バックエンドで同じ行を返す
    /// </summary>
    [Fact(
        DisplayName = "[Parity] 2b: VO 述語（ワイルドカードのエスケープ・変数パターン・VO 引数・.Value 比較）が正しい行を返す"
    )]
    public async Task Where_ValueObjectPredicates_EscapingAndValueAccess()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m), Ct);
        await repo.InsertAsync(NewCustomer(3, "50%OFF"), Ct);
        await repo.InsertAsync(NewCustomer(4, "A_B"), Ct);
        await repo.InsertAsync(NewCustomer(5, "AxB"), Ct);

        // (a) % のリテラル一致（エスケープ）: ワイルドカード扱いだと余計な行まで一致してしまう
        var percent = await repo.Query().Where(c => c.Name.Contains("0%")).ToListAsync(Ct);
        percent.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([3]);

        // (b) _ のリテラル一致（エスケープ）: 未エスケープの LIKE '%A_B%' は AxB(5) にも一致してしまう
        var underscore = await repo.Query().Where(c => c.Name.Contains("A_B")).ToListAsync(Ct);
        underscore.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([4]);

        // (c) 変数パターン（パラメータ束縛の経路でも同じエスケープ規則が効く）
        var keyword = "0%";
        var byVariable = await repo.Query().Where(c => c.Name.Contains(keyword)).ToListAsync(Ct);
        byVariable.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([3]);

        // (d) VO 引数のオーバーロード（TSelf）: 素値へ開いて部分一致
        var byVo = await repo.Query()
            .Where(c => c.Name.Contains(NameValue.Create("lic")))
            .ToListAsync(Ct);
        byVo.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1]);

        // (d2) VO 引数を変数（クロージャ）で渡す経路（パラメータ束縛＋素値変換）でも同じ
        var voKeyword = NameValue.Create("lic");
        var byVoVariable = await repo.Query()
            .Where(c => c.Name.Contains(voKeyword))
            .ToListAsync(Ct);
        byVoVariable.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1]);

        // (e) .Value を開いた等値比較（string VO）
        var byValue = await repo.Query().Where(c => c.Name.Value == "Bob").ToListAsync(Ct);
        byValue.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);

        // (f) .Value を開いた数値比較（decimal VO・NULL 行は不一致のまま）
        var byBalance = await repo.Query().Where(c => c.Balance!.Value >= 150m).ToListAsync(Ct);
        byBalance.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);
    }

    /// <summary>3. Include: 親→子コレクション・子→親 の Include 復元</summary>
    [Fact(DisplayName = "[Parity] 3: Include（親→子コレクション・子→親参照）が正しく復元される")]
    public async Task Include_LoadsNavigations()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

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

    /// <summary>4. SaveAsync グラフ保存: Added 親＋子を SaveAsync→再取得で一致、子を MarkRemoved→SaveAsync で削除</summary>
    [Fact(
        DisplayName = "[Parity] 4: SaveAsync グラフ保存（親＋子の追加・子の削除）が再取得で一致する"
    )]
    public async Task SaveAsync_PersistsAndRemovesGraph()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        // Added の親＋子（2 件）をグラフ保存する
        var customer = NewCustomer(1, "Alice");
        customer.MarkAdded();
        var order10 = NewOrder(10, 1, 10m, null);
        order10.MarkAdded();
        var order11 = NewOrder(11, 1, 20m, null);
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
        DisplayName = "[Parity] 5a: 他者削除後の SaveAsync（更新）が SaveConflictException を投げる"
    )]
    public async Task SaveAsync_MissingUpdateTarget_ThrowsSaveConflict()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
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
        DisplayName = "[Parity] 5b: insertWhenUpdateMissing=true で更新対象なしが INSERT に切り替わる"
    )]
    public async Task SaveAsync_InsertWhenUpdateMissing_InsertsRow()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();

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

    /// <summary>6. BulkInsertAsync: 数十件流して件数一致</summary>
    [Fact(DisplayName = "[Parity] 6: BulkInsertAsync で数十件が一括追加され件数が一致する")]
    public async Task BulkInsert_InsertsAllRows()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();

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

    /// <summary>7. QueryBySqlAsync: WHERE 付き全列 SELECT で対象行のみ取得し、VO 復元・匿名オブジェクトパラメータが機能する</summary>
    [Fact(
        DisplayName = "[Parity] 7: QueryBySqlAsync（WHERE 付き全列 SELECT・匿名パラメータ）で対象行のみ VO 復元して取得できる"
    )]
    public async Task QueryBySql_ReturnsMappedRows_WithAnonymousParameters()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m, isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m, isActive: false), Ct);
        await repo.InsertAsync(NewCustomer(3, "Carol", balance: 300m, isActive: true), Ct);

        // 全列 SELECT + WHERE。@名 は匿名オブジェクトのプロパティ名で束縛する。
        var rows = await repo.QueryBySqlAsync(
            "SELECT * FROM [customers] WHERE [is_active] = @active AND [balance] >= @minBalance ORDER BY [customer_id];",
            new { active = true, minBalance = 150m },
            Ct
        );

        // is_active=true かつ balance>=150 の Carol(3) のみ
        rows.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([3]);
        var carol = rows.Single();
        // VO 復元一致
        carol.Name.Value.Should().Be("Carol");
        carol.Balance.Should().NotBeNull();
        carol.Balance!.Value.Should().Be(300m);
        carol.IsActive.Value.Should().BeTrue();
        // DB ロード行は Unchanged
        carol.RowState.Should().Be(RowState.Unchanged);

        // parameters: null はパラメータなし（全件）
        var allRows = await repo.QueryBySqlAsync(
            "SELECT * FROM [customers] ORDER BY [customer_id];",
            null,
            Ct
        );
        allRows.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 2, 3]);
    }

    /// <summary>8. QueryBySqlAsync: 部分 SELECT（列不足）で、欠けている列名が分かる例外を投げる</summary>
    [Fact(
        DisplayName = "[Parity] 8: QueryBySqlAsync の部分 SELECT（列不足）で列名の分かる例外を投げる"
    )]
    public async Task QueryBySql_PartialSelect_ThrowsWithMissingColumnName()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m, isActive: true), Ct);

        // name / balance / is_active を欠く部分 SELECT はマッピングできない
        var act = async () =>
            await repo.QueryBySqlAsync("SELECT [customer_id] FROM [customers];", null, Ct);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        // 全列が必要である旨と、欠けた列名（name 等）がメッセージに含まれる
        ex.Which.Message.Should().Contain("全列");
        ex.Which.Message.Should().Contain("name");
    }

    /// <summary>9. ExecuteSqlAsync: UPDATE で影響行数を返し、パラメータ束縛が機能する</summary>
    [Fact(DisplayName = "[Parity] 9: ExecuteSqlAsync（UPDATE・パラメータ束縛）で影響行数を返す")]
    public async Task ExecuteSql_Update_ReturnsAffectedRows()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(3, "Carol", isActive: false), Ct);

        // is_active=true の 2 行を false に更新（パラメータ束縛）
        var affected = await repo.ExecuteSqlAsync(
            "UPDATE [customers] SET [is_active] = @newValue WHERE [is_active] = @oldValue;",
            new { newValue = false, oldValue = true },
            Ct
        );
        affected.Should().Be(2);

        // 全員 is_active=false になっていること
        var active = await repo.Query()
            .Where(c => c.IsActive == IsActiveValue.Create(true))
            .ToListAsync(Ct);
        active.Should().BeEmpty();
    }

    /// <summary>10. ExecuteScalarSqlAsync: COUNT(*) を int で取得し、該当なし集計（SUM が NULL）は default になる</summary>
    [Fact(
        DisplayName = "[Parity] 10: ExecuteScalarSqlAsync（COUNT を int 取得・SUM が NULL で default）"
    )]
    public async Task ExecuteScalarSql_ReturnsScalar_AndDefaultOnNull()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m, isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m, isActive: true), Ct);

        // COUNT(*) を int で取得
        var count = await repo.ExecuteScalarSqlAsync<int>(
            "SELECT COUNT(*) FROM [customers];",
            null,
            Ct
        );
        count.Should().Be(2);

        // WHERE 付き COUNT（パラメータ束縛）
        var activeCount = await repo.ExecuteScalarSqlAsync<int>(
            "SELECT COUNT(*) FROM [customers] WHERE [is_active] = @active;",
            new { active = true },
            Ct
        );
        activeCount.Should().Be(2);

        // 該当なしの SUM は NULL → default（decimal? では null）
        var nullSum = await repo.ExecuteScalarSqlAsync<decimal?>(
            "SELECT SUM([balance]) FROM [customers] WHERE [customer_id] = @id;",
            new { id = 999 },
            Ct
        );
        nullSum.Should().BeNull();
    }

    /// <summary>射影 DTO（JOIN + GROUP BY の集計を寛容マップで受ける先）</summary>
    private sealed class CustomerTotal
    {
        public string Name { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    /// <summary>射影先が引数なしコンストラクタを持たない位置指定 record（分かる例外の検証用）</summary>
    private sealed record PositionalTotal(string Name, decimal Total);

    /// <summary>11. SqlExecutor 経由の QueryBySqlAsync&lt;TEntity&gt;: 厳密マップ・VO 復元・匿名パラメータが機能する</summary>
    [Fact(
        DisplayName = "[Parity] 11: SqlExecutor.QueryBySqlAsync<TEntity>（厳密マップ・VO 復元・匿名パラメータ）"
    )]
    public async Task SqlExecutor_QueryBySql_MapsEntity_WithValueObjects()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m, isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m, isActive: false), Ct);

        var executor = CreateSqlExecutor();
        var rows = await executor.QueryBySqlAsync<CustomerEntity>(
            "SELECT * FROM [customers] WHERE [is_active] = @active ORDER BY [customer_id];",
            new { active = true },
            Ct
        );

        var alice = rows.Should().ContainSingle().Subject;
        alice.CustomerId.Value.Should().Be(1);
        alice.Name.Value.Should().Be("Alice");
        alice.Balance!.Value.Should().Be(100m);
        alice.IsActive.Value.Should().BeTrue();
        alice.RowState.Should().Be(RowState.Unchanged);
    }

    /// <summary>12. QueryProjectionBySqlAsync（DTO モード）: JOIN + 集計を寛容マップ（列名⇔プロパティを大文字小文字無視で突き合わせ）</summary>
    [Fact(DisplayName = "[Parity] 12: QueryProjectionBySqlAsync（JOIN + 集計を DTO へ寛容射影）")]
    public async Task SqlExecutor_QueryProjection_JoinAggregate_MapsDto()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        var orderRepo = CreateOrderRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await orderRepo.InsertAsync(NewOrder(10, 1, 100m, "a1"), Ct);
        await orderRepo.InsertAsync(NewOrder(11, 1, 50m, "a2"), Ct);
        await orderRepo.InsertAsync(NewOrder(12, 2, 200m, null), Ct);

        var executor = CreateSqlExecutor();
        var totals = await executor.QueryProjectionBySqlAsync<CustomerTotal>(
            "SELECT c.[name] AS Name, SUM(o.[amount]) AS Total "
                + "FROM [customers] c JOIN [orders] o ON o.[customer_id] = c.[customer_id] "
                + "GROUP BY c.[name] ORDER BY c.[name];",
            null,
            Ct
        );

        totals.Should().HaveCount(2);
        totals[0].Name.Should().Be("Alice");
        totals[0].Total.Should().Be(150m);
        totals[1].Name.Should().Be("Bob");
        totals[1].Total.Should().Be(200m);
    }

    /// <summary>13. QueryProjectionBySqlAsync（単一値モード）: 1 列 SELECT を List&lt;string&gt; と VO 型へ変換する</summary>
    [Fact(
        DisplayName = "[Parity] 13: QueryProjectionBySqlAsync（単一値モード: string と 値オブジェクト）"
    )]
    public async Task SqlExecutor_QueryProjection_SingleValueMode()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob"), Ct);

        var executor = CreateSqlExecutor();

        // 1 列 SELECT を string の列へ（先頭列を変換）
        var names = await executor.QueryProjectionBySqlAsync<string>(
            "SELECT [name] FROM [customers] ORDER BY [customer_id];",
            null,
            Ct
        );
        names.Should().BeEquivalentTo(["Alice", "Bob"], options => options.WithStrictOrdering());

        // 値オブジェクト型（IValueObject 実装）は SqlValueObjectActivator.Wrap で包む
        var voNames = await executor.QueryProjectionBySqlAsync<NameValue>(
            "SELECT [name] FROM [customers] ORDER BY [customer_id];",
            null,
            Ct
        );
        voNames.Select(v => v.Value).Should().BeEquivalentTo(["Alice", "Bob"]);
    }

    /// <summary>14. QueryProjectionBySqlAsync: 1 列も一致しない DTO は、列名・プロパティ名を含む分かる例外を投げる</summary>
    [Fact(
        DisplayName = "[Parity] 14: QueryProjectionBySqlAsync の非一致 DTO で列名・プロパティ名入り例外を投げる"
    )]
    public async Task SqlExecutor_QueryProjection_NoColumnMatch_Throws()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice"), Ct);

        var executor = CreateSqlExecutor();
        // 別名が DTO のプロパティ（Name/Total）のどれとも一致しない
        var act = async () =>
            await executor.QueryProjectionBySqlAsync<CustomerTotal>(
                "SELECT [name] AS wrong_alias FROM [customers];",
                null,
                Ct
            );

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("wrong_alias");
        ex.Which.Message.Should().Contain("Name");
        ex.Which.Message.Should().Contain("Total");
    }

    /// <summary>15. QueryProjectionBySqlAsync: 位置指定 record（引数なしコンストラクタ無し）は分かる例外を投げる</summary>
    [Fact(
        DisplayName = "[Parity] 15: QueryProjectionBySqlAsync の位置指定 record で分かる例外を投げる"
    )]
    public async Task SqlExecutor_QueryProjection_PositionalRecord_Throws()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice"), Ct);

        var executor = CreateSqlExecutor();
        var act = async () =>
            await executor.QueryProjectionBySqlAsync<PositionalTotal>(
                "SELECT [name] AS Name FROM [customers];",
                null,
                Ct
            );

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("位置指定 record は非対応");
    }

    /// <summary>16. Repository 経由の生 SQL 3 メソッドが従来どおり動く（委譲後リグレッション）</summary>
    [Fact(
        DisplayName = "[Parity] 16: Repository の生 SQL 3 メソッドが委譲後も従来どおり動く（リグレッション）"
    )]
    public async Task Repository_RawSqlMethods_StillWorkAfterDelegation()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m, isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m, isActive: true), Ct);

        // QueryBySqlAsync（全列・VO 復元）
        var rows = await repo.QueryBySqlAsync(
            "SELECT * FROM [customers] WHERE [balance] >= @min ORDER BY [customer_id];",
            new { min = 150m },
            Ct
        );
        rows.Select(c => c.Name.Value).Should().BeEquivalentTo(["Bob"]);

        // ExecuteScalarSqlAsync（COUNT を int）
        var count = await repo.ExecuteScalarSqlAsync<int>(
            "SELECT COUNT(*) FROM [customers];",
            null,
            Ct
        );
        count.Should().Be(2);

        // ExecuteSqlAsync（UPDATE 影響行数）
        var affected = await repo.ExecuteSqlAsync(
            "UPDATE [customers] SET [is_active] = @v WHERE [balance] >= @min;",
            new { v = false, min = 150m },
            Ct
        );
        affected.Should().Be(1);
    }
}
