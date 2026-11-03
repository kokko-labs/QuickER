using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Provider.Sqlite;
using QuickER.Tests.GeneratedSqliteFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

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
/// CRUD・Where の式木（NULL 許容の列の != を含む）・並びと窓・Include・グラフ保存・BulkInsert・カスケードの一括削除は、実装先の一致
/// （<c>BackendParityRuntimeTests</c> の <c>crud-basic</c>・<c>query-*</c>・<c>include-children-and-parents</c>・
/// <c>graph-save-added-tree-then-mixed-changes</c>・<c>bulk-insert-*</c>・<c>execute-delete-cascade-column-predicate</c> の
/// シナリオ）が同じ入力をインメモリも含めて流して見る。ここに残すのは、インメモリが C# の意味で評価して比べられない自列参照の
/// Contains（NULL の行で NullReferenceException）・EF Core の追跡しないクエリが断る親→子→親の ThenInclude・生 SQL の 5 系統。
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
    /// <remarks>既存テーブルを全 DROP してから作り直す。EF Core Migrations は使わない。</remarks>
    protected async Task ResetAndCreateSchemaAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(SqlitePortableFixtureDefinition.Build(), Ct);
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

    /// <summary>
    /// 2c. 自列参照の Contains（値の位置にエンティティ列を置く LIKE 系）が、両バックエンドで同一に
    /// 「引数列が NULL の行を除外して非 NULL 行のみ返す」ことを検証する。
    /// </summary>
    /// <remarks>
    /// QuickER 版は列同士の LIKE を素の列参照（<c>[memo] LIKE '%'‖…‖'%'</c>）へ翻訳し、引数列が NULL の行は
    /// 連結が NULL となり不一致になる。EF Core 版（<c>ValueObjectStringMethodTranslator</c>）は非定数引数を
    /// <c>COALESCE(arg,'')</c>＋SQL 側 REPLACE で組むが、NULL 行は<b>左辺（対象列）</b>も NULL のため
    /// <c>NULL LIKE …</c> で同じく不一致になる。したがって「非 NULL 行のみ返る」という観測結果は両バックエンドで
    /// 一致する。自列 <c>Equals</c> は EF Core が C# の null 等価（両辺 NULL を等しいとみなす）で翻訳し NULL 行を
    /// 含めるため両バックエンドで一致せず、QuickER 版の SQL null 意味論（NULL 除外）は ADO 専用テスト
    /// <c>SelfColumnEquals_ExcludesNullRows</c> で検証する。
    /// </remarks>
    [Fact(
        DisplayName = "[SQLite] 2c: 自列参照の Contains が非 NULL 行のみ返す（両バックエンド一致）"
    )]
    public async Task Where_SelfColumnContains_ReturnsNonNullRows()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, "abc"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, "xyz"), Ct);
        await orders.InsertAsync(NewOrder(12, 1, 30m, memo: null), Ct); // memo が NULL の行

        // 自列 Contains: memo が自身を含む＝非 NULL の全行が一致し、NULL 行は不一致
        var selfContains = await orders
            .Query()
            .Where(o => o.Memo!.Contains(o.Memo!))
            .ToListAsync(Ct);
        selfContains.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11]);
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

    /// <summary>使い終えた一時 DB を破棄する（派生の DI コンテナ破棄は派生側で行う）</summary>
    public virtual void Dispose() => _db.Dispose();
}
