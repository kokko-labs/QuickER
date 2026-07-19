using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <b>QuickER の SQL Server 実装</b>のクエリパイプラインのうち、SQLite 側の実行検証スイート
/// （<see cref="GeneratedSqliteRuntimeTestsBase"/> / <see cref="NamedQueryRuntimeTestsBase"/>）では
/// 実 DB を通っているのに SQL Server 側（<see cref="GeneratedRuntimeParityTestsBase"/>）では未実行だった
/// メソッド群を、実 SQL Server（Testcontainers）に対して補完的に検証する単独スイート。
/// </summary>
/// <remarks>
/// <para>
/// 入力は <see cref="GeneratedFixtureDefinition"/> の図（SQL Server 方言のQuickER 版 Repository）。
/// パリティ基底のシナリオとは重複しないよう、パリティ基底を継承せず独立クラスとして持ち、
/// リポジトリ・エグゼキュータ・スキーマ準備・シードヘルパは基底と同じ流儀で内製する
/// （<see cref="GeneratedRuntimeAdoParityTests"/> と同じく <see cref="SqlConnectionFactory"/> を直接 new）。
/// </para>
/// <para>
/// 補完対象（かっこ内は対称な SQLite 側テスト）:
/// <list type="bullet">
///   <item><c>SqlQuery&lt;T&gt;.CountAsync</c>（条件あり/なし）</item>
///   <item><c>SqlQuery&lt;T&gt;.ToProjectionListAsync</c>（列刈り込み SELECT・Include 併用フォールバックの 2 経路。
///     <see cref="NamedQueryRuntimeTestsBase.DslProjection_ReturnsDtoRows"/> /
///     <see cref="NamedQueryRuntimeTestsBase.Projection_WithInclude_FallsBackAndProjectsNavigation"/>）</item>
///   <item><c>SqlQuery&lt;T&gt;.ExecuteDeleteAsync</c>（cascade / 非 cascade。
///     <see cref="GeneratedSqliteRuntimeTestsBase.ExecuteDelete_Cascade_DeletesChildrenAndParent"/>）</item>
///   <item><c>SqlQuery&lt;T&gt;.OrderByDescending</c>＋Skip/Take（OFFSET/FETCH。
///     <see cref="GeneratedSqliteRuntimeTestsBase.OrderBy_And_Paging_ReturnsOrderedWindow"/>）</item>
///   <item><c>ThenInclude</c> 再帰（親→子→親のサイクル。
///     <see cref="GeneratedSqliteRuntimeTestsBase.ThenInclude_Recursive_LoadsParentReference"/>）</item>
///   <item><c>Repository.SaveAsync(コレクション)</c> オーバーロード（複数グラフの一括保存。SQLite 側は
///     <see cref="SaveHookRuntimeTestsBase"/> 系が共有基底で流すが SQL Server 側は未実行だった）</item>
///   <item>DatePart 翻訳（Year/Month/Day/Hour/Minute/Second/DayOfYear/Date → SQL Server の
///     YEAR()/MONTH()/DAY()/DATEPART()/CAST AS date。
///     <see cref="GeneratedSqliteAdoRuntimeTests.DateParts_StrftimeFragments_ReturnCorrectIntegersOnRealData"/> と対称）</item>
/// </list>
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class SqlServerQueryPipelineRuntimeTests(SqlServerContainerFixture fixture)
{
    /// <summary>共有コンテナのフィクスチャ</summary>
    private SqlServerContainerFixture Fixture { get; } = fixture;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // --- リポジトリ・エグゼキュータの生成（QuickER の SQL Server 実装を直接 new） ---

    /// <summary>接続ファクトリを生成する（コンテナの接続文字列を使う）</summary>
    private SqlConnectionFactory Factory() => new(Fixture.ConnectionString);

    private ICustomerRepository CreateCustomerRepository() => new CustomerRepository(Factory());

    private IOrderRepository CreateOrderRepository() => new OrderRepository(Factory());

    private ISqlExecutor CreateSqlExecutor() => new SqlExecutor(Factory());

    /// <summary>フィクスチャ図の SQL Server 方言 DDL でテーブルを作成する</summary>
    private async Task CreateSchemaAsync()
    {
        var ddl = new SqlServerDdlGenerator().Build(GeneratedFixtureDefinition.Build());
        await Fixture.ExecuteAsync(ddl, Ct);
    }

    /// <summary>各テスト冒頭のセットアップ（スキーマ初期化＋作成）をまとめる</summary>
    private async Task ResetAndCreateSchemaAsync()
    {
        Assert.SkipUnless(Fixture.IsAvailable, Fixture.UnavailableReason);
        await Fixture.ResetSchemaAsync(Ct);
        await CreateSchemaAsync();
    }

    // --- エンティティ組み立てヘルパー（パリティ基底と同一） ---

    /// <summary>指定 ID の顧客エンティティを組み立てる（VO は Create で検証生成）</summary>
    private static CustomerEntity NewCustomer(
        int id,
        string name,
        decimal? balance = null,
        bool isActive = true
    ) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = balance is null ? null : BalanceValue.Create(balance.Value),
            IsActive = IsActiveValue.Create(isActive),
        };

    /// <summary>指定 ID の注文エンティティを組み立てる</summary>
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

    // ==================== 補完シナリオ ====================

    /// <summary>
    /// 1. <c>SqlQuery&lt;T&gt;.CountAsync</c>（条件なし＝全件・条件あり＝WHERE 一致数）が正しい件数を返す。
    /// </summary>
    /// <remarks>SQLite 側は名前付きクエリ経由で件数を検証しているが、SQL Server 側は式木 <c>Query().CountAsync()</c> が未実行だった。</remarks>
    [Fact(DisplayName = "[SqlServerPipeline] 1: CountAsync（条件あり/なし）が正しい件数を返す")]
    public async Task CountAsync_WithAndWithoutPredicate_ReturnsCount()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", isActive: false), Ct);
        await repo.InsertAsync(NewCustomer(3, "Carol", isActive: true), Ct);
        await repo.InsertAsync(NewCustomer(4, "Dave", isActive: true), Ct);

        // 条件なし＝全件
        (await repo.Query().CountAsync(Ct))
            .Should()
            .Be(4);

        // 条件あり＝is_active=true の 3 件
        var activeCount = await repo.Query()
            .Where(c => c.IsActive == IsActiveValue.Create(true))
            .CountAsync(Ct);
        activeCount.Should().Be(3);

        // 一致なしは 0
        var noneCount = await repo.Query()
            .Where(c => c.Name == NameValue.Create("Nobody"))
            .CountAsync(Ct);
        noneCount.Should().Be(0);
    }

    /// <summary>
    /// 2. <c>SqlQuery&lt;T&gt;.ToProjectionListAsync</c> の 2 経路を検証する:
    /// (a) セレクタが列のみ参照＝サーバー側の列刈り込み SELECT・(b) Include したナビ参照＝全列取得→メモリ内射影のフォールバック。
    /// </summary>
    /// <remarks>
    /// SQLite 側は <see cref="NamedQueryRuntimeTestsBase.DslProjection_ReturnsDtoRows"/>（刈り込み）と
    /// <see cref="NamedQueryRuntimeTestsBase.Projection_WithInclude_FallsBackAndProjectsNavigation"/>（フォールバック）で
    /// 両経路を通すが、SQL Server 側は式木 API の <c>ToProjectionListAsync</c> が未実行だった。
    /// </remarks>
    [Fact(
        DisplayName = "[SqlServerPipeline] 2: ToProjectionListAsync が刈り込み・Include フォールバックの両経路で射影する"
    )]
    public async Task ToProjectionListAsync_PruningAndIncludeFallback()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 100m, "a"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m, "b"), Ct);
        await orders.InsertAsync(NewOrder(13, 1, 75m, null), Ct);

        // (a) 列刈り込み経路: セレクタは列（OrderId / Amount）だけを参照する＝Include なし＝サーバー側で列を刈り込む
        var pruned = await orders
            .Query()
            .Where(o => o.CustomerId == CustomerIdValue.Create(1))
            .OrderBy(o => o.OrderId)
            .ToProjectionListAsync(o => new OrderAmountRow(o.OrderId.Value, o.Amount.Value), Ct);
        pruned.Select(r => r.OrderId).Should().Equal(10, 11, 13);
        pruned.Select(r => r.Amount).Should().Equal(100m, 50m, 75m);

        // (b) Include フォールバック経路: セレクタが Include したナビ（Customer）を参照する＝列刈り込み不可。
        // 従来経路（全列取得→Customer をロード→メモリ内射影）へフォールバックする
        var joined = await orders
            .Query()
            .Where(o => o.CustomerId == CustomerIdValue.Create(1))
            .Include(o => o.Customer)
            .OrderBy(o => o.OrderId)
            .ToProjectionListAsync(
                o => new OrderCustomerRow(o.OrderId.Value, o.Customer.Name.Value),
                Ct
            );
        joined.Select(r => r.OrderId).Should().Equal(10, 11, 13);
        joined.Should().OnlyContain(r => r.CustomerName == "Alice");
    }

    /// <summary>
    /// 3a. <c>SqlQuery&lt;T&gt;.ExecuteDeleteAsync(cascadeDelete: false)</c> が条件一致の葉行（注文）のみを削除し、親は残す。
    /// </summary>
    [Fact(
        DisplayName = "[SqlServerPipeline] 3a: ExecuteDeleteAsync（非 cascade）が葉行のみ削除し親を残す"
    )]
    public async Task ExecuteDeleteAsync_NonCascade_DeletesLeafOnly()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, null), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, null), Ct);

        // 注文 10 のみを非 cascade 削除（葉行＝子を持たないので cascade 不要）
        var deleted = await orders
            .Query()
            .Where(o => o.OrderId == OrderIdValue.Create(10))
            .ExecuteDeleteAsync(cascadeDelete: false, cancellationToken: Ct);
        deleted.Should().Be(1);

        (await orders.GetAllAsync(Ct)).Select(o => o.OrderId.Value).Should().Equal(11);
        (await customers.GetAllAsync(Ct)).Select(c => c.CustomerId.Value).Should().Equal(1);
    }

    /// <summary>
    /// 3b. <c>SqlQuery&lt;T&gt;.ExecuteDeleteAsync(cascadeDelete: true)</c> が FK チェーンをたどり子ごと削除する。
    /// </summary>
    /// <remarks>SQLite 側 <see cref="GeneratedSqliteRuntimeTestsBase.ExecuteDelete_Cascade_DeletesChildrenAndParent"/> と対称（子 2＋親 1＝3 件）。</remarks>
    [Fact(
        DisplayName = "[SqlServerPipeline] 3b: ExecuteDeleteAsync（cascade）が子ごと削除し件数が一致する"
    )]
    public async Task ExecuteDeleteAsync_Cascade_DeletesChildrenAndParent()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, null), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, null), Ct);

        // customer_profiles は 1 件も入れないため cascade は 子（注文 2）＋親（顧客 1）の 3 件になる
        var deleted = await customers
            .Query()
            .Where(c => c.CustomerId == CustomerIdValue.Create(1))
            .ExecuteDeleteAsync(cascadeDelete: true, cancellationToken: Ct);
        deleted.Should().Be(3, "子 2 件＋親 1 件をアプリが明示削除する");

        (await customers.GetAllAsync(Ct)).Should().BeEmpty();
        (await orders.GetAllAsync(Ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 4. <c>SqlQuery&lt;T&gt;.OrderByDescending</c>＋Skip/Take（SQL Server の OFFSET/FETCH）が降順の正しい窓を返す。
    /// </summary>
    /// <remarks>SQLite 側 <see cref="GeneratedSqliteRuntimeTestsBase.OrderBy_And_Paging_ReturnsOrderedWindow"/> の LIMIT/OFFSET と対称。</remarks>
    [Fact(
        DisplayName = "[SqlServerPipeline] 4: OrderByDescending＋Skip/Take（OFFSET/FETCH）が降順の窓を返す"
    )]
    public async Task OrderByDescending_WithPaging_ReturnsDescendingWindow()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        for (var i = 10; i <= 15; i++)
        {
            await orders.InsertAsync(NewOrder(i, 1, amount: i, memo: null), Ct);
        }

        // 降順全件（15..10）
        var descending = await orders.Query().OrderByDescending(o => o.OrderId).ToListAsync(Ct);
        descending.Select(o => o.OrderId.Value).Should().Equal(15, 14, 13, 12, 11, 10);

        // 降順＋Skip(1)+Take(2)＝OFFSET 1 ROWS FETCH NEXT 2＝14, 13
        var window = await orders
            .Query()
            .OrderByDescending(o => o.OrderId)
            .Skip(1)
            .Take(2)
            .ToListAsync(Ct);
        window.Select(o => o.OrderId.Value).Should().Equal(14, 13);

        // 降順＋Take のみ（TOP/FETCH 相当）
        var topTwo = await orders.Query().OrderByDescending(o => o.OrderId).Take(2).ToListAsync(Ct);
        topTwo.Select(o => o.OrderId.Value).Should().Equal(15, 14);
    }

    /// <summary>
    /// 5. <c>ThenInclude</c> 再帰（親→子→親のサイクル）が子の親参照を正しくロードする。
    /// </summary>
    /// <remarks>
    /// SQLite 側 <see cref="GeneratedSqliteRuntimeTestsBase.ThenInclude_Recursive_LoadsParentReference"/> と対称。
    /// QuickER の SQL Server 版 <c>IncludeLoader</c> はサイクルを段階的なクエリで解決する
    /// （EF Core の no-tracking クエリと異なりサイクルを拒否しない）。
    /// </remarks>
    [Fact(
        DisplayName = "[SqlServerPipeline] 5: ThenInclude 再帰（親→子→親のサイクル）が子の親参照をロードする"
    )]
    public async Task ThenInclude_Recursive_LoadsParentReference()
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
    /// 6. <c>Repository.SaveAsync(コレクション)</c> オーバーロードが複数グラフ（親＋子）を一括保存する。
    /// </summary>
    /// <remarks>
    /// SQLite 側は SaveHook 系スイートが共有基底でコレクション保存を通すが、SQL Server 側の
    /// <c>SaveHookSqlServerRuntimeTests</c> は共有基底を継承しないためコレクション版が未実行だった。
    /// </remarks>
    [Fact(
        DisplayName = "[SqlServerPipeline] 6: SaveAsync（コレクション）が複数グラフを一括保存する"
    )]
    public async Task SaveAsync_Collection_PersistsMultipleGraphs()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        // 2 つの独立した Added グラフ（各: 親 1＋子 1）を 1 回のコレクション保存で永続化する
        var alice = NewCustomer(1, "Alice");
        alice.MarkAdded();
        var aliceOrder = NewOrder(10, 1, 10m, null);
        aliceOrder.MarkAdded();
        alice.Orders.Add(aliceOrder);

        var bob = NewCustomer(2, "Bob");
        bob.MarkAdded();
        var bobOrder = NewOrder(20, 2, 20m, null);
        bobOrder.MarkAdded();
        bob.Orders.Add(bobOrder);

        var savedRows = await customers.SaveAsync([alice, bob], cancellationToken: Ct);
        savedRows.Should().Be(4, "親 2 件＋子 2 件が挿入される");

        (await customers.GetAllAsync(Ct))
            .Select(c => c.CustomerId.Value)
            .Should()
            .BeEquivalentTo([1, 2]);
        (await orders.GetAllAsync(Ct))
            .Select(o => o.OrderId.Value)
            .Should()
            .BeEquivalentTo([10, 20]);
    }

    /// <summary>
    /// 7. 式木トランスレータが日付部品（Year/Month/Day/Hour/Minute/Second/DayOfYear/Date）へ生成する SQL Server
    /// フラグメント（YEAR()/MONTH()/DAY()/DATEPART()/CAST AS date）が、実 SQL Server の <c>datetime2</c> に対して
    /// 正しい値を返すことを検証する。
    /// </summary>
    /// <remarks>
    /// <b>フィクスチャに DateTime 列がない</b>ため（<see cref="GeneratedFixtureDefinition"/> は int/varchar/decimal/bit のみ）、
    /// 式木クエリ API から翻訳器を通す実データ検証はできない。そこで SQLite 側
    /// <see cref="GeneratedSqliteAdoRuntimeTests.DateParts_StrftimeFragments_ReturnCorrectIntegersOnRealData"/> と
    /// <b>同型</b>のアプローチを採る＝翻訳器がテンプレート <c>CSharpRuntime/_05_QueryPipeline.scriban</c> の
    /// <c>TryGetDatePart</c>（<c>repository_dialect == "sqlserver"</c> 分岐）で DateTime 列参照に対して生成する SQL
    /// フラグメントそのものを、<c>datetime2</c> 列を持つ一時テーブルへ格納したうえで <c>ExecuteScalarSqlAsync</c> で
    /// 実行し、部品の実値を検証する。フラグメントの生成側（式木からの吐き分け）は <c>SqlServerRepositoryDialectTests</c>
    /// と Roslyn コンパイル検証が守る。フィクスチャは変更しない。
    /// </remarks>
    [Fact(
        DisplayName = "[SqlServerPipeline] 7: 式木の日付部品が生成する SQL Server フラグメントが実 datetime2 で正しい値を返す"
    )]
    public async Task DateParts_SqlServerFragments_ReturnCorrectValuesOnRealData()
    {
        await ResetAndCreateSchemaAsync();

        var executor = CreateSqlExecutor();

        // DateTime 列（datetime2）を持つ検証専用テーブルを用意する（ResetSchemaAsync で毎回クリーンに落ちる）
        await executor.ExecuteSqlAsync(
            "CREATE TABLE [events] ([event_id] INT PRIMARY KEY, [occurred_at] datetime2 NOT NULL);",
            null,
            Ct
        );
        await executor.ExecuteSqlAsync(
            "INSERT INTO [events] ([event_id], [occurred_at]) VALUES (1, '2026-07-05 13:47:09');",
            null,
            Ct
        );

        // 翻訳器（SQL Server 方言）が各日付部品に対して生成するフラグメント（列名 occurred_at）を実行し検証する
        async Task<int> PartAsync(string fragment) =>
            await executor.ExecuteScalarSqlAsync<int>(
                $"SELECT {fragment} FROM [events] WHERE [event_id] = 1",
                null,
                Ct
            );

        (await PartAsync("YEAR([occurred_at])")).Should().Be(2026);
        (await PartAsync("MONTH([occurred_at])")).Should().Be(7);
        (await PartAsync("DAY([occurred_at])")).Should().Be(5);
        (await PartAsync("DATEPART(HOUR, [occurred_at])")).Should().Be(13);
        (await PartAsync("DATEPART(MINUTE, [occurred_at])")).Should().Be(47);
        (await PartAsync("DATEPART(SECOND, [occurred_at])")).Should().Be(9);
        // 2026-07-05 は年初から 186 日目（2026 は非うるう年）
        (await PartAsync("DATEPART(DAYOFYEAR, [occurred_at])"))
            .Should()
            .Be(186);

        // Date（CAST AS date）は時刻を切り落とした日付を返す
        var dateOnly = await executor.ExecuteScalarSqlAsync<DateTime>(
            "SELECT CAST([occurred_at] AS date) FROM [events] WHERE [event_id] = 1",
            null,
            Ct
        );
        dateOnly.Should().Be(new DateTime(2026, 7, 5));
    }
}

/// <summary>列刈り込み経路の射影で使う DTO（注文ID と金額）</summary>
public sealed record OrderAmountRow(int OrderId, decimal Amount);
