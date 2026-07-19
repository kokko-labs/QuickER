using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedSqliteFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c>（SQLite 方言）が持つ Where 式木の<b>個別分岐</b>を、
/// 実 SQLite（一時ファイル DB・Docker 不要）への往復で意味論検証する追加スイート。
/// <see cref="SqlServerTranslatorOperatorRuntimeTests"/> と<b>対称構造</b>で、同じ観点（OR・&lt;&gt;・&gt;・&lt;・
/// &lt;=・値引数の Equals/StartsWith/EndsWith・IsNullOrWhiteSpace・配列 Contains の IN・空コレクション IN・NOT・
/// <c>AnyAsync</c>）を突く。
/// </summary>
/// <remarks>
/// <para>
/// 入力は方言可搬な図を SQLite 方言＋EF Core 併存で生成した第 3 フィクスチャ
/// （<see cref="SqlitePortableFixtureDefinition"/>）。リポジトリは実運用と同じ DI 経路
/// （<c>AddGeneratedSqliteRepositories(connectionString)</c>）で解決する（QuickER の <c>SqliteRepository</c> 版）。
/// </para>
/// <para>
/// <b>方言差</b>: SQLite の翻訳器は識別子を二重引用符で、LIKE パターンを <c>||</c> で連結する（SQL Server は
/// 角括弧・<c>+</c>）。ただしこの差は生成 SQL 文字列の見た目に閉じ、<b>行集合の観測結果は両方言で一致</b>する。
/// decimal 比較は QuickER の SQLite 版 Repository が数値として扱う（EF Core Sqlite の decimal 制約は本経路には
/// 無関係。既存 <see cref="GeneratedSqliteRuntimeTestsBase"/> の <c>Balance!.Value &gt;= 300m</c> と同経路）。
/// </para>
/// <para>
/// <b>到達不能な分岐（bool メンバ）について</b>: 翻訳器の「bool 列メンバ真＝<c>"col"=1</c>」「bool 列の NOT＝
/// <c>"col"=0</c>」短縮分岐は、ラムダパラメータ直下の素の bool プロパティを要求する。本 SQLite フィクスチャには
/// bool 列自体が無い（<c>Customer</c> は Id/Name/Balance のみ）ため、実 DB 往復では到達できず<b>検証を見送る</b>。
/// SQL Server 版と同様、到達可能な一般 NOT 分岐（<c>NOT (...)</c>）を <c>Not_NegatesCondition</c> で検証する。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteTranslatorOperatorRuntimeTests : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列</summary>
    private string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ（接続文字列は一時 DB）</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(ConnectionString)
            .BuildServiceProvider();

    private ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    private IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    /// <summary>スキーマを初期化し、SQLite の DdlGenerator が生成した DDL でテーブルを作成する</summary>
    /// <remarks>子（orders）→ 親（customers）の順で DROP してから作り直す。</remarks>
    private async Task ResetAndCreateSchemaAsync()
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

    /// <summary>指定 ID の顧客エンティティを組み立てる（VO は Create で検証生成）</summary>
    private static CustomerEntity NewCustomer(int id, string name, decimal? balance = null) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = balance is null ? null : BalanceValue.Create(balance.Value),
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

    /// <summary>
    /// 演算子検証の共通シード（4 件・SQL Server 版と対称）。残高は 3 桁の 100/200/300 と NULL（Carol）で、
    /// 比較演算子・OR・IN・NOT の各分岐で一意な期待集合が作れる。
    /// </summary>
    private async Task<ICustomerRepository> SeedCustomersAsync()
    {
        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice", balance: 100m), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob", balance: 200m), Ct);
        await repo.InsertAsync(NewCustomer(3, "Alicia", balance: 300m), Ct);
        await repo.InsertAsync(NewCustomer(4, "Carol", balance: null), Ct);
        return repo;
    }

    /// <summary>OrElse（OR）: いずれか一方の等値に一致する行の和集合を返す（<c>(A OR B)</c> 分岐）</summary>
    [Fact(DisplayName = "[SQLite演算子] OrElse（OR）が両条件の和集合を返す")]
    public async Task OrElse_ReturnsUnionOfBothSides()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // Name == "Alice" OR Name == "Bob" → {1, 2}
        var union = await repo.Query()
            .Where(c => c.Name == NameValue.Create("Alice") || c.Name == NameValue.Create("Bob"))
            .ToListAsync(Ct);
        union.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 2]);
    }

    /// <summary>
    /// NotEqual（&lt;&gt;）: 等値でない行を返す。NULL 許容列に対しては SQL の三値論理により
    /// <c>NULL &lt;&gt; @p</c> が unknown となり NULL 行が<b>除外</b>される（C# の <c>!=</c> の null 意味論と異なる）。
    /// </summary>
    [Fact(
        DisplayName = "[SQLite演算子] NotEqual（<>）が等値行を除外し、NULL 列は SQL 意味論で不一致（除外）"
    )]
    public async Task NotEqual_ExcludesEqualAndNullRows()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // (a) 文字列 VO の <>: "Bob" 以外の全行（NULL 名は無いので 1,3,4）
        var notBob = await repo.Query()
            .Where(c => c.Name != NameValue.Create("Bob"))
            .ToListAsync(Ct);
        notBob.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3, 4]);

        // (b) decimal VO の <>（.Value 展開）: "balance" <> 100。
        //     100 の Alice(1) は等値で除外、NULL 残高の Carol(4) は NULL <> 100 が unknown となり除外。
        //     → 200(Bob) / 300(Alicia) のみ。C# の != 意味論なら null 行も一致するが、SQL 側は除外する。
        var notHundred = await repo.Query().Where(c => c.Balance!.Value != 100m).ToListAsync(Ct);
        notHundred.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 3]);
    }

    /// <summary>
    /// GreaterThan（&gt;）・LessThan（&lt;）・LessThanOrEqual（&lt;=）: 数値比較で正しい範囲を返す
    /// （既存カバレッジは = と &gt;= のみ）。境界値（&lt;= の等値）と NULL 列の除外も確認する。
    /// </summary>
    [Fact(DisplayName = "[SQLite演算子] GreaterThan/LessThan/LessThanOrEqual が正しい範囲を返す")]
    public async Task Comparison_GtLtLe_ReturnCorrectRanges()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // > 150 → 200,300（NULL 残高の Carol は除外）
        var greater = await repo.Query().Where(c => c.Balance!.Value > 150m).ToListAsync(Ct);
        greater.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 3]);

        // < 250 → 100,200
        var less = await repo.Query().Where(c => c.Balance!.Value < 250m).ToListAsync(Ct);
        less.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 2]);

        // <= 200 → 100,200（境界値 200 を含む）
        var lessOrEqual = await repo.Query().Where(c => c.Balance!.Value <= 200m).ToListAsync(Ct);
        lessOrEqual.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 2]);
    }

    /// <summary>
    /// 値引数フォームの Equals(値) / StartsWith(値) / EndsWith(値) がパラメータ化された条件へ翻訳され、
    /// 正しい行を返す（列同士フォームは列引数テストで検証済み）。
    /// </summary>
    [Fact(DisplayName = "[SQLite演算子] 値引数の Equals/StartsWith/EndsWith が正しい行を返す")]
    public async Task ValueArgument_Equals_StartsWith_EndsWith()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // Equals(値): "name" = @p → "Bob" のみ
        var equals = await repo.Query().Where(c => c.Name.Value.Equals("Bob")).ToListAsync(Ct);
        equals.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);

        // StartsWith(値): "name" LIKE 'Al%' → Alice(1) / Alicia(3)
        var startsWith = await repo.Query().Where(c => c.Name.StartsWith("Al")).ToListAsync(Ct);
        startsWith.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3]);

        // EndsWith(値): "name" LIKE '%ce' → Alice(1) のみ（Alicia は "ia" 終わり）
        var endsWith = await repo.Query().Where(c => c.Name.EndsWith("ce")).ToListAsync(Ct);
        endsWith.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1]);
    }

    /// <summary>
    /// string.IsNullOrWhiteSpace: <c>("col" IS NULL OR LTRIM(RTRIM("col")) = '')</c> へ翻訳され、
    /// NULL・空文字・<b>空白のみ</b>の行を返す（IsNullOrEmpty は空白のみを含めない点との差を検証する）。
    /// </summary>
    [Fact(DisplayName = "[SQLite演算子] IsNullOrWhiteSpace が NULL・空文字・空白のみの行を返す")]
    public async Task IsNullOrWhiteSpace_MatchesNullEmptyAndWhitespace()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);

        var orders = CreateOrderRepository();
        await orders.InsertAsync(NewOrder(10, 1, 10m, memo: "shipped"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, memo: ""), Ct); // 空文字
        await orders.InsertAsync(NewOrder(12, 1, 30m, memo: "   "), Ct); // 空白のみ（LTRIM/RTRIM で空になる）
        await orders.InsertAsync(NewOrder(13, 1, 40m, memo: null), Ct); // NULL

        // NULL・空文字・空白のみ＝{11, 12, 13}。空白のみ(12) を含むことが IsNullOrEmpty との差
        var blank = await orders
            .Query()
            .Where(o => string.IsNullOrWhiteSpace(o.Memo!.Value))
            .ToListAsync(Ct);
        blank.Select(o => o.OrderId.Value).Should().BeEquivalentTo([11, 12, 13]);
    }

    /// <summary>
    /// 配列の <c>Enumerable.Contains</c>（静的 2 引数経路）が <c>"col" IN (...)</c> へ翻訳される。
    /// List&lt;VO&gt; のインスタンス Contains 経路は既存検証済みで、ここは配列＝静的経路を突く。
    /// </summary>
    [Fact(DisplayName = "[SQLite演算子] 配列 Contains（静的経路）が IN へ翻訳され対象行を返す")]
    public async Task ArrayContains_TranslatesToInClause()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // 配列は C# 14 以降 ids.Contains(x) が MemoryExtensions.Contains（Span 版）に解決され
        // 翻訳器未対応の形になるため、検証対象の静的 Enumerable.Contains 経路を明示呼び出しで突く
        var ids = new[] { CustomerIdValue.Create(2), CustomerIdValue.Create(4) };
        var inList = await repo.Query()
            .Where(c => Enumerable.Contains(ids, c.CustomerId))
            .ToListAsync(Ct);
        inList.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 4]);
    }

    /// <summary>
    /// 式木経路の空コレクション Contains は <c>1 = 0</c>（常偽）へ翻訳され、1 件も返さない
    /// （<c>IN ()</c> が不正 SQL になるのを避ける no-match）。
    /// </summary>
    [Fact(DisplayName = "[SQLite演算子] 空コレクションの IN が 1=0（常偽）で 0 件を返す")]
    public async Task EmptyCollection_In_ReturnsNoRows()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        var empty = Array.Empty<CustomerIdValue>();
        var none = await repo.Query()
            .Where(c => Enumerable.Contains(empty, c.CustomerId))
            .ToListAsync(Ct);
        none.Should().BeEmpty();
    }

    /// <summary>
    /// NOT: bool 列以外（比較式）の否定は一般 <c>NOT (...)</c> 分岐へ落ち、条件の補集合を返す。
    /// bool 列短縮分岐（<c>"col"=0</c>）は本フィクスチャに bool 列が無く到達しないため、ここでは
    /// 到達可能な一般 NOT を検証する（クラス doc 参照）。
    /// </summary>
    [Fact(DisplayName = "[SQLite演算子] NOT（一般否定）が条件の補集合を返す")]
    public async Task Not_NegatesCondition()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // NOT ("name" = "Bob") → Bob 以外（1,3,4）
        var notBob = await repo.Query()
            .Where(c => !(c.Name == NameValue.Create("Bob")))
            .ToListAsync(Ct);
        notBob.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3, 4]);
    }

    /// <summary>
    /// <c>SqlQuery&lt;T&gt;.AnyAsync</c>: 条件あり／なし・真／偽の 4 通りを実 DB で検証する
    /// （既存の実行検証は InMemory のみ）。
    /// </summary>
    [Fact(DisplayName = "[SQLite演算子] AnyAsync が条件あり/なし・真/偽で正しく判定する")]
    public async Task AnyAsync_WithAndWithoutCondition()
    {
        await ResetAndCreateSchemaAsync();
        var customers = await SeedCustomersAsync();
        var orders = CreateOrderRepository(); // 注文は投入しない（空テーブル）

        // (a) 条件なし・真: 顧客が存在する
        (await customers.Query().AnyAsync(Ct))
            .Should()
            .BeTrue();

        // (b) 条件なし・偽: 注文テーブルは空
        (await orders.Query().AnyAsync(Ct))
            .Should()
            .BeFalse();

        // (c) 条件あり・真: "Bob" が存在する
        (await customers.Query().Where(c => c.Name == NameValue.Create("Bob")).AnyAsync(Ct))
            .Should()
            .BeTrue();

        // (d) 条件あり・偽: 存在しない名前
        (await customers.Query().Where(c => c.Name == NameValue.Create("Zoe")).AnyAsync(Ct))
            .Should()
            .BeFalse();
    }

    /// <summary>使い終えた一時 DB と DI コンテナを破棄する</summary>
    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }
}
