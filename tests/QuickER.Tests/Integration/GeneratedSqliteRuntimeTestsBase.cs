using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedSqliteFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// SQLite 方言の生成物（QuickER の <c>SqliteRepository</c> 版と EF Core Sqlite 版）を、同一シナリオで実 SQLite
/// （一時ファイル DB・インプロセス）に流して検証するパリティ／ランタイムスイートの共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 入力は第3の固定フィクスチャ（<see cref="SqlitePortableFixtureDefinition"/>・方言可搬な図を SQLite 方言＋
/// EF Core 併存で生成したもの）。シナリオはすべて生成インターフェイス
/// （<see cref="ICustomerRepository"/> / <see cref="IOrderRepository"/> / <see cref="ISqlExecutor"/>）
/// 経由で記述し、リポジトリ・エグゼキュータの生成方法だけを派生クラスが与える。
/// </para>
/// <para>
/// これにより「AddGeneratedSqliteRepositories（QuickER の SQLite）と AddGeneratedEfCoreRepositories＋UseSqlite（EF Core）を
/// 差し替えるだけで交換可能」という契約を、両バックエンドで同一アサーションにより証明する。Docker 不要のため
/// CI でも常時実行される。スキーマは <see cref="SqliteDdlGenerator"/> が生成する DDL で用意する。
/// </para>
/// <para>
/// EF Core Sqlite は <c>decimal</c> を TEXT として格納しサーバー側の <c>ORDER BY</c> / 比較 / 集計を直接は
/// サポートしないため（"SQLite does not support expressions of type 'decimal'"）、両バックエンドで同一に走る
/// シナリオは並び替え・ページングを <b>整数キー</b>で行う。decimal に依存する検証は少量データのクライアント評価
/// （EF Core）で成立する範囲に限る。生 SQL の集計はQuickER／EF Core 双方で <c>ExecuteScalarSqlAsync&lt;decimal&gt;</c>
/// （<c>Convert.ChangeType</c> 経路）を用いる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class GeneratedSqliteRuntimeTestsBase : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列（バックエンドはこの実ファイルへ読み書きする）</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    // --- 派生クラスが与えるバックエンド固有のファクトリ ---

    /// <summary>顧客リポジトリを生成する（QuickER = DI 直接 / EF Core = AddGeneratedEfCoreRepositories 経由）</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>エンティティ非依存の生 SQL 実行器を生成する</summary>
    protected abstract ISqlExecutor CreateSqlExecutor();

    /// <summary>SQLite は二重引用符で識別子を引用する</summary>
    protected static string Quote(string identifier) => $"\"{identifier}\"";

    /// <summary>SQLite（Microsoft.Data.Sqlite）は @ プレフィックスのプレースホルダを用いる</summary>
    protected static string Param(string name) => $"@{name}";

    /// <summary>スキーマを初期化し、SQLite の DdlGenerator が生成した DDL でテーブルを作成する</summary>
    /// <remarks>子（orders）→ 親（customers）の順で DROP してから作り直す。EF Core Migrations は使わない。</remarks>
    protected async Task ResetAndCreateSchemaAsync()
    {
        await using (var conn = new SqliteConnection(ConnectionString))
        {
            await conn.OpenAsync(Ct);

            await using var drop = conn.CreateCommand();
            drop.CommandText =
                "DROP TABLE IF EXISTS \"orders\"; DROP TABLE IF EXISTS \"customers\";";
            await drop.ExecuteNonQueryAsync(Ct);
        }

        var ddl = new SqliteDdlGenerator().Build(SqlitePortableFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);
    }

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

    // ==================== 共通シナリオ（QuickER の SQLite・EF Core Sqlite の双方で実行） ====================

    /// <summary>1. CRUD 往復（VO 込み・null 混在・RowState）: Insert→GetById→Update→Delete で値・VO・状態が復元される</summary>
    [Fact(
        DisplayName = "[SQLite] 1: CRUD 往復（VO 込み・null 混在・RowState）で値と VO が正しく復元される"
    )]
    public async Task Crud_RoundTrips_WithValueObjectsAndRowState()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();

        var inserted = NewCustomer(1, "Alice", balance: 123.45m);
        await repo.InsertAsync(inserted, Ct);

        var loaded = await repo.GetByIdAsync(CustomerIdValue.Create(1), Ct);
        loaded.Should().NotBeNull();
        loaded!.CustomerId.Value.Should().Be(1);
        loaded.Name.Value.Should().Be("Alice");
        loaded.Balance.Should().NotBeNull();
        loaded.Balance!.Value.Should().Be(123.45m);
        // DB ロード行は Unchanged
        loaded.RowState.Should().Be(RowState.Unchanged);

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
    /// 2. Where 式木（等値・比較・&amp;&amp;・Contains の LIKE エスケープ・変数パターン・IN・.Value 比較）が正しい行を返す。
    /// LIKE のワイルドカード（% _）のリテラル一致（エスケープ）も検証する。
    /// </summary>
    [Fact(
        DisplayName = "[SQLite] 2: Where 式木（等値・比較・&&・Contains の % _ エスケープ・変数・IN・.Value）が正しい行を返す"
    )]
    public async Task Where_ExpressionTree_ReturnsCorrectRows()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m), Ct);
        await repo.InsertAsync(NewCustomer(3, "Alicia", balance: 300m), Ct);
        await repo.InsertAsync(NewCustomer(4, "50%OFF"), Ct);
        await repo.InsertAsync(NewCustomer(5, "A_B"), Ct);
        await repo.InsertAsync(NewCustomer(6, "AxB"), Ct);

        // (a) 等値（VO の == 演算子）
        var byName = await repo.Query()
            .Where(c => c.Name == NameValue.Create("Bob"))
            .ToListAsync(Ct);
        byName.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);

        // (b) && の連結（.Value を開いた数値比較 && 文字列 Contains）
        var activeAlice = await repo.Query()
            .Where(c => c.Balance!.Value >= 300m && c.Name.Contains("Alic"))
            .ToListAsync(Ct);
        activeAlice.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([3]);

        // (c) Contains → LIKE '%...%'
        var likeAli = await repo.Query().Where(c => c.Name.Contains("Ali")).ToListAsync(Ct);
        likeAli.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3]);

        // (d) % のリテラル一致（エスケープ）: ワイルドカード扱いだと余計な行まで一致してしまう
        var percent = await repo.Query().Where(c => c.Name.Contains("0%")).ToListAsync(Ct);
        percent.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([4]);

        // (e) _ のリテラル一致（エスケープ）: 未エスケープの LIKE '%A_B%' は AxB(6) にも一致してしまう
        var underscore = await repo.Query().Where(c => c.Name.Contains("A_B")).ToListAsync(Ct);
        underscore.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([5]);

        // (f) 変数パターン（パラメータ束縛の経路でも同じエスケープ規則が効く）
        var keyword = "0%";
        var byVariable = await repo.Query().Where(c => c.Name.Contains(keyword)).ToListAsync(Ct);
        byVariable.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([4]);

        // (g) IN（List<VO>.Contains → IN (...)）
        var ids = new List<CustomerIdValue>
        {
            CustomerIdValue.Create(2),
            CustomerIdValue.Create(6),
        };
        var inList = await repo.Query().Where(c => ids.Contains(c.CustomerId)).ToListAsync(Ct);
        inList.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 6]);

        // (h) .Value を開いた等値比較（string VO）
        var byValue = await repo.Query().Where(c => c.Name.Value == "Bob").ToListAsync(Ct);
        byValue.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);
    }

    /// <summary>3. OrderBy/ThenBy・Skip/Take（LIMIT/OFFSET）が整数キーで正しい順序・範囲を返す</summary>
    [Fact(
        DisplayName = "[SQLite] 3: OrderBy/ThenBy・Skip/Take（LIMIT/OFFSET）が正しい順序・範囲を返す"
    )]
    public async Task OrderBy_And_Paging_ReturnsOrderedWindow()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        // 注文 10..15 を挿入（customer_id は 1 で共通、order_id で並び替える）
        for (var i = 10; i <= 15; i++)
        {
            await orders.InsertAsync(NewOrder(i, 1, amount: i, memo: null), Ct);
        }

        // OrderBy(customer_id) ThenBy(order_id) の降順・昇順ミックス（整数キー）
        var ascending = await orders.Query().OrderBy(o => o.OrderId).ToListAsync(Ct);
        ascending.Select(o => o.OrderId.Value).Should().Equal(10, 11, 12, 13, 14, 15);

        var descending = await orders.Query().OrderByDescending(o => o.OrderId).ToListAsync(Ct);
        descending.Select(o => o.OrderId.Value).Should().Equal(15, 14, 13, 12, 11, 10);

        // Skip + Take（LIMIT/OFFSET）: 昇順で 2 件飛ばして 3 件
        var window = await orders.Query().OrderBy(o => o.OrderId).Skip(2).Take(3).ToListAsync(Ct);
        window.Select(o => o.OrderId.Value).Should().Equal(12, 13, 14);

        // Take のみ（LIMIT）
        var topTwo = await orders.Query().OrderBy(o => o.OrderId).Take(2).ToListAsync(Ct);
        topTwo.Select(o => o.OrderId.Value).Should().Equal(10, 11);

        // Skip のみ（LIMIT -1 OFFSET）: 先頭 4 件を飛ばして残り
        var tail = await orders.Query().OrderBy(o => o.OrderId).Skip(4).ToListAsync(Ct);
        tail.Select(o => o.OrderId.Value).Should().Equal(14, 15);
    }

    /// <summary>
    /// 4. Include マルチクエリ: 親→子コレクション・子→親参照・空コレクション・
    /// Include＋Where/ページング併用が正しく復元される。
    /// </summary>
    /// <remarks>
    /// ThenInclude 再帰（親→子→親のサイクル）は EF Core の no-tracking クエリがサイクルを拒否するため
    /// 本基底からは分離し、<see cref="ThenInclude_Recursive_LoadsParentReference"/> にバックエンド別で置く。
    /// </remarks>
    [Fact(
        DisplayName = "[SQLite] 4: Include マルチクエリ（親→子・子→親・空コレクション・Where/ページング併用）"
    )]
    public async Task Include_MultiQuery_LoadsNavigations()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct); // 子なし（空コレクション検証用）
        await orders.InsertAsync(NewOrder(10, 1, 10m, "a"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, "b"), Ct);
        await orders.InsertAsync(NewOrder(12, 1, 30m, "c"), Ct);

        // (a) 親→子コレクション Include
        var withOrders = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        withOrders.Should().NotBeNull();
        withOrders!.Orders.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11, 12]);

        // (b) 空コレクション（子なしの親）は空のまま
        var noOrders = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(2))
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(Ct);
        noOrders.Should().NotBeNull();
        noOrders!.Orders.Should().BeEmpty();

        // (c) 子→親参照 Include
        var order = await orders
            .Query()
            .Where(o => o.OrderId == OrderIdValue.Create(10))
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(Ct);
        order.Should().NotBeNull();
        order!.Customer.Should().NotBeNull();
        order.Customer.CustomerId.Value.Should().Be(1);
        order.Customer.Name.Value.Should().Be("Alice");

        // (d) Include ＋ Where/ページング併用（親を Where で絞り、子コレクションを Include）
        var paged = await customers
            .Query()
            .Where(c => c.Name.Contains("Alic"))
            .OrderBy(c => c.CustomerId)
            .Take(1)
            .Include(c => c.Orders)
            .ToListAsync(Ct);
        paged.Should().ContainSingle();
        paged[0].CustomerId.Value.Should().Be(1);
        paged[0].Orders.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11, 12]);
    }

    /// <summary>
    /// 4b. ThenInclude 再帰（親→子→親のサイクル）が子の親参照を正しくロードする。
    /// </summary>
    /// <remarks>
    /// QuickER の <c>IncludeLoader</c>（マルチクエリ）はサイクルを段階的なクエリで解決できるが、EF Core の
    /// no-tracking クエリは Include パス <c>Orders-&gt;Customer</c> のサイクルを拒否する
    /// （"Cycles are not allowed in no-tracking queries"）。そのため本テストはバックエンド別に置き、
    /// EF Core 派生では非サイクルの等価経路（子を Include(Customer) で別ロード）へ置き換える
    /// （<see cref="GeneratedSqliteEfCoreParityRuntimeTests"/> でオーバーライド）。
    /// </remarks>
    [Fact(
        DisplayName = "[SQLite] 4b: ThenInclude 再帰（親→子→親のサイクル）が子の親参照をロードする"
    )]
    public virtual async Task ThenInclude_Recursive_LoadsParentReference()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, "a"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, "b"), Ct);
        await orders.InsertAsync(NewOrder(12, 1, 30m, "c"), Ct);

        // customer→Orders→Customer（子の親参照を再帰的にロード）
        var recursive = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .Include(c => c.Orders)
                .ThenInclude(o => o.Customer)
            .FirstOrDefaultAsync(Ct);
        recursive.Should().NotBeNull();
        recursive!.Orders.Should().HaveCount(3);
        recursive
            .Orders.Should()
            .OnlyContain(o => o.Customer != null && o.Customer.CustomerId.Value == 1);
    }

    /// <summary>
    /// 5. 生 SQL 4 系統（厳密全列・射影・単一値・影響行数・匿名パラメータ）が機能する。
    /// SQLite の decimal 制約に合わせ、JOIN 集計は <c>ExecuteScalarSqlAsync&lt;decimal&gt;</c>（数値変換経路）で検証する。
    /// </summary>
    [Fact(
        DisplayName = "[SQLite] 5: 生 SQL（厳密全列・射影・単一値・影響行数・匿名パラメータ）が機能する"
    )]
    public async Task RawSql_AllModes()
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

        // (b) JOIN + 集計（SQLite の decimal 制約＋厳密射影マッパのため ExecuteScalarSqlAsync<decimal> で顧客ごとの合計を検証）
        var aliceTotal = await repo.ExecuteScalarSqlAsync<decimal>(
            $"SELECT SUM(o.{amount}) FROM {customers} c "
                + $"JOIN {ordersT} o ON o.{customerId} = c.{customerId} "
                + $"WHERE c.{name} = {Param("n")}",
            new { n = "Alice" },
            Ct
        );
        aliceTotal.Should().Be(150m);

        // (c) QueryProjectionBySqlAsync（単一値モード: string と VO 型）
        var executor = CreateSqlExecutor();
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

    /// <summary>6. グラフ保存（TrackGraph 相当・追加/更新/削除の混在）が再取得で一致する</summary>
    [Fact(
        DisplayName = "[SQLite] 6: グラフ保存（TrackGraph 相当・追加/更新/削除混在）が再取得で一致する"
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

    /// <summary>7. BulkInsertAsync（QuickER は 1Tx INSERT ループ）で数十件が一括追加され件数が一致する</summary>
    [Fact(DisplayName = "[SQLite] 7: BulkInsertAsync で数十件が一括追加され件数が一致する")]
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

    /// <summary>8. ExecuteDeleteAsync（cascadeDelete=true）で子ごと削除し件数が一致する（削除カスケード）</summary>
    [Fact(
        DisplayName = "[SQLite] 8: ExecuteDeleteAsync（cascadeDelete=true）で子ごと削除し件数が一致する"
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

    /// <summary>使い終えた一時 DB を破棄する（派生の DI コンテナ破棄は派生側で行う）</summary>
    public virtual void Dispose() => _db.Dispose();
}
