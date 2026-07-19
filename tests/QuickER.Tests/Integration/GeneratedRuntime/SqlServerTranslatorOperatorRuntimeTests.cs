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
/// 生成ランタイムの <c>SqlExpressionTranslator</c>（SQL Server 方言）が持つ Where 式木の<b>個別分岐</b>を、
/// 実 SQL Server（Testcontainers）への往復で意味論検証する追加スイート。既存のパリティ／列引数テストが
/// 未到達だった演算子・述語フォーム（OR・&lt;&gt;・&gt;・&lt;・&lt;=・値引数の Equals/StartsWith/EndsWith・
/// IsNullOrWhiteSpace・配列 Contains の IN・空コレクション IN・NOT）と、<c>SqlQuery&lt;T&gt;.AnyAsync</c> を
/// カバーする。「その演算子を含む式 → 期待する行集合」の実 DB 往復で、生成 SQL が意味論的に正しいことを示す。
/// </summary>
/// <remarks>
/// <para>
/// 入力は SQL Server 全カバレッジの固定フィクスチャ（<see cref="GeneratedFixtureDefinition"/>）。リポジトリは
/// <see cref="GeneratedRuntimeAdoParityTests"/> と同じく <see cref="ISqlConnectionFactory"/> を直接渡して new する。
/// テーブルは各テスト冒頭で <see cref="SqlServerDdlGenerator"/> の DDL を用いて作り直す（共有コンテナは
/// <see cref="SqlServerContainerFixture.ResetSchemaAsync"/> でスキーマを初期化してから使う）。
/// </para>
/// <para>
/// SQLite 版 <see cref="SqliteTranslatorOperatorRuntimeTests"/> と対称構造で、方言差（角括弧・<c>+</c> 連結）は
/// 生成 SQL 文字列の見た目に閉じており、<b>行集合の観測結果は両方言で一致</b>する。方言差はコメントで明示する。
/// </para>
/// <para>
/// <b>到達不能な分岐（bool メンバ）について</b>: 翻訳器には「bool 列メンバ真＝<c>[col]=1</c>」「bool 列の NOT＝
/// <c>[col]=0</c>」の短縮分岐があるが、これらは <c>member.Expression</c> が<b>ラムダパラメータ直下</b>の bool
/// プロパティであることを要求する（<c>IsColumn</c>）。本フィクスチャに素の bool 列は無く、<c>IsActive</c> は
/// bool 値オブジェクト（<c>IsActiveValue</c>）のため <c>c =&gt; c.IsActive.Value</c> は <c>.Value</c> の
/// <c>MemberExpression</c>（その <c>Expression</c> は <c>c.IsActive</c>＝パラメータ直下ではない）となり
/// <c>IsColumn</c> が false → 既定分岐で <c>NotSupportedException</c> になる（翻訳段階で throw されクエリに至らない）。
/// したがって bool メンバ短縮分岐は<b>本フィクスチャでは実 DB 往復で到達できず、本スイートでは検証を見送る</b>。
/// 代わりに、到達可能な一般 NOT 分岐（bool 列以外の否定＝<c>NOT (...)</c>）を <c>Not_NegatesCondition</c> で検証する。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class SqlServerTranslatorOperatorRuntimeTests(SqlServerContainerFixture fixture)
{
    /// <summary>共有コンテナのフィクスチャ</summary>
    private SqlServerContainerFixture Fixture { get; } = fixture;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>接続ファクトリを生成する（コンテナの接続文字列を使う）</summary>
    private SqlConnectionFactory Factory() => new(Fixture.ConnectionString);

    private ICustomerRepository CreateCustomerRepository() => new CustomerRepository(Factory());

    private IOrderRepository CreateOrderRepository() => new OrderRepository(Factory());

    /// <summary>スキーマを初期化し、フィクスチャ図の SQL Server 方言 DDL でテーブルを作成する</summary>
    private async Task ResetAndCreateSchemaAsync()
    {
        Assert.SkipUnless(Fixture.IsAvailable, Fixture.UnavailableReason);
        await Fixture.ResetSchemaAsync(Ct);
        var ddl = new SqlServerDdlGenerator().Build(GeneratedFixtureDefinition.Build());
        await Fixture.ExecuteAsync(ddl, Ct);
    }

    /// <summary>指定 ID の顧客エンティティを組み立てる（VO は Create で検証生成）</summary>
    private static CustomerEntity NewCustomer(int id, string name, decimal? balance = null) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = balance is null ? null : BalanceValue.Create(balance.Value),
            IsActive = IsActiveValue.Create(true),
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
    /// 演算子検証の共通シード（4 件）。名前・残高・NULL 残高を混ぜ、比較演算子・OR・IN・NOT の各分岐で
    /// 一意な期待集合が作れるようにする。残高は 3 桁の 100/200/300 と NULL（Carol）で、比較の期待行が明瞭。
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
    [Fact(DisplayName = "[SqlServer演算子] OrElse（OR）が両条件の和集合を返す")]
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
    /// <c>NULL &lt;&gt; @p</c> が unknown となり NULL 行が<b>除外</b>される（C# の <c>!=</c> なら null は不一致＝含める、
    /// という意味論と異なる点を検証する）。
    /// </summary>
    [Fact(
        DisplayName = "[SqlServer演算子] NotEqual（<>）が等値行を除外し、NULL 列は SQL 意味論で不一致（除外）"
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

        // (b) decimal VO の <>（.Value 展開）: [balance] <> 100。
        //     100 の Alice(1) は等値で除外、NULL 残高の Carol(4) は NULL <> 100 が unknown となり除外。
        //     → 200(Bob) / 300(Alicia) のみ。C# の != 意味論なら null 行も一致するが、SQL 側は除外する。
        var notHundred = await repo.Query().Where(c => c.Balance!.Value != 100m).ToListAsync(Ct);
        notHundred.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 3]);
    }

    /// <summary>
    /// GreaterThan（&gt;）・LessThan（&lt;）・LessThanOrEqual（&lt;=）: 数値比較で正しい範囲を返す
    /// （既存カバレッジは = と &gt;= のみ）。境界値（&lt;= の等値）と NULL 列の除外も確認する。
    /// </summary>
    [Fact(
        DisplayName = "[SqlServer演算子] GreaterThan/LessThan/LessThanOrEqual が正しい範囲を返す"
    )]
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
    /// 正しい行を返す（列同士フォームは列引数テストで検証済み。ここは値引数の未到達フォーム）。
    /// </summary>
    [Fact(DisplayName = "[SqlServer演算子] 値引数の Equals/StartsWith/EndsWith が正しい行を返す")]
    public async Task ValueArgument_Equals_StartsWith_EndsWith()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // Equals(値): [name] = @p → "Bob" のみ
        var equals = await repo.Query().Where(c => c.Name.Value.Equals("Bob")).ToListAsync(Ct);
        equals.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);

        // StartsWith(値): [name] LIKE 'Al%' → Alice(1) / Alicia(3)
        var startsWith = await repo.Query().Where(c => c.Name.StartsWith("Al")).ToListAsync(Ct);
        startsWith.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3]);

        // EndsWith(値): [name] LIKE '%ce' → Alice(1) のみ（Alicia は "ia" 終わり）
        var endsWith = await repo.Query().Where(c => c.Name.EndsWith("ce")).ToListAsync(Ct);
        endsWith.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1]);
    }

    /// <summary>
    /// string.IsNullOrWhiteSpace: <c>([col] IS NULL OR LTRIM(RTRIM([col])) = '')</c> へ翻訳され、
    /// NULL・空文字・<b>空白のみ</b>の行を返す（IsNullOrEmpty は空白のみを含めない点との差を検証する）。
    /// </summary>
    [Fact(DisplayName = "[SqlServer演算子] IsNullOrWhiteSpace が NULL・空文字・空白のみの行を返す")]
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
    /// 配列の Contains が <c>[col] IN (...)</c> へ翻訳される。自然な書き方（C# 14 以降は
    /// <c>MemoryExtensions.Contains</c>＝Span 版・比較子 null の 3 引数形に解決）と、
    /// 静的 <c>Enumerable.Contains</c>（2 引数）の両経路を検証する。
    /// List&lt;VO&gt; のインスタンス Contains 経路は既存検証済み。
    /// </summary>
    [Fact(
        DisplayName = "[SqlServer演算子] 配列 Contains（Span 形・静的形の両方）が IN へ翻訳され対象行を返す"
    )]
    public async Task ArrayContains_TranslatesToInClause()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // 自然な書き方（Span 版 3 引数形へ解決される）
        var ids = new[] { CustomerIdValue.Create(2), CustomerIdValue.Create(4) };
        var spanList = await repo.Query().Where(c => ids.Contains(c.CustomerId)).ToListAsync(Ct);
        spanList.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 4]);

        // 静的 Enumerable.Contains（2 引数）経路も同一結果になる
        var inList = await repo.Query()
            .Where(c => Enumerable.Contains(ids, c.CustomerId))
            .ToListAsync(Ct);
        inList.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2, 4]);
    }

    /// <summary>
    /// 式木経路の空コレクション Contains は <c>1 = 0</c>（常偽）へ翻訳され、1 件も返さない
    /// （<c>IN ()</c> が不正 SQL になるのを避ける no-match）。
    /// </summary>
    [Fact(DisplayName = "[SqlServer演算子] 空コレクションの IN が 1=0（常偽）で 0 件を返す")]
    public async Task EmptyCollection_In_ReturnsNoRows()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        var empty = Array.Empty<CustomerIdValue>();
        var none = await repo.Query().Where(c => empty.Contains(c.CustomerId)).ToListAsync(Ct);
        none.Should().BeEmpty();
    }

    /// <summary>
    /// NOT: bool 列以外（比較式）の否定は一般 <c>NOT (...)</c> 分岐へ落ち、条件の補集合を返す。
    /// bool 列短縮分岐（<c>[col]=0</c>）は本フィクスチャに素の bool 列が無く到達しないため、ここでは
    /// 到達可能な一般 NOT を検証する（クラス doc 参照）。
    /// </summary>
    [Fact(DisplayName = "[SqlServer演算子] NOT（一般否定）が条件の補集合を返す")]
    public async Task Not_NegatesCondition()
    {
        await ResetAndCreateSchemaAsync();
        var repo = await SeedCustomersAsync();

        // NOT ([name] = "Bob") → Bob 以外（1,3,4）
        var notBob = await repo.Query()
            .Where(c => !(c.Name == NameValue.Create("Bob")))
            .ToListAsync(Ct);
        notBob.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3, 4]);
    }

    /// <summary>
    /// <c>SqlQuery&lt;T&gt;.AnyAsync</c>: 条件あり／なし・真／偽の 4 通りを実 DB で検証する
    /// （既存の実行検証は InMemory のみ）。<c>SELECT CASE WHEN EXISTS(...)</c> 相当の存在判定。
    /// </summary>
    [Fact(DisplayName = "[SqlServer演算子] AnyAsync が条件あり/なし・真/偽で正しく判定する")]
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
}
