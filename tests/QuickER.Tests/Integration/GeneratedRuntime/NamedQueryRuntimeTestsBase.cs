using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 名前付きクエリの生成メソッドのうち、<b>実装先を問わず生成される部分</b>（ミニ DSL の全戻り形と manual）を
/// 意味検証するパリティスイートの共通基底。QuickER の <c>SqliteRepository</c> 版・EF Core Sqlite 版・
/// インメモリ版の 3 派生が同一シナリオを流す。
/// </summary>
/// <remarks>
/// <para>
/// 入力はクエリフィクスチャ（<see cref="QueryFixtureDefinition"/>）。ミニ DSL の全戻り形
/// （一覧＋ページング・単一・件数・射影）・文字列一致（LIKE→Contains）・IN（VO 列×リストパラメータ）・
/// 列参照型付け（VO 引数）・Include＋射影のフォールバック・manual（partial 実装）を、生成された
/// <see cref="IOrderRepository"/> のメソッド呼び出しだけで検証する。
/// </para>
/// <para>
/// <b>自由 SQL の戻り形は本基底に置かない</b>。SQL 文そのもの（IN のリスト展開・列別名の DTO マップ）の検証であり、
/// SQL を持たないインメモリには対象が無いためで、実 DB を持つ派生の共通基底
/// <see cref="NamedQueryRawSqlRuntimeTestsBase"/> が担う（＝条件スキップではなくサブクラス階層で分ける）。
/// </para>
/// <para>
/// EF Core Sqlite の decimal 制約（サーバーサイド比較・並び替え非対応）に合わせ、フィクスチャの
/// クエリ定義は条件・並び替えを整数キーで行う（decimal は射影の実体化にのみ使用）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class NamedQueryRuntimeTestsBase
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>顧客リポジトリを生成する（QuickER 版 Repository = AddGenerated{方言}Repositories / EF Core = AddGeneratedEfCoreRepositories / インメモリ = 共有ストア）</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>保存先（スキーマまたはストア）を空にし、共通のシードデータを投入する</summary>
    protected abstract Task ResetAndSeedAsync();

    /// <summary>共通のシードデータをリポジトリ経由で投入する（保存先を空にした直後に派生が呼ぶ）</summary>
    /// <remarks>
    /// customers: 1=Alice / 2=Bob。orders: (10,顧客1,100,apple pie)・(11,顧客1,50,banana)・
    /// (12,顧客2,200,apple juice)・(13,顧客1,75,memo なし)。
    /// </remarks>
    protected async Task SeedAsync()
    {
        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m, "banana"), Ct);
        await orders.InsertAsync(NewOrder(12, 2, 200m, "apple juice"), Ct);
        await orders.InsertAsync(NewOrder(13, 1, 75m, null), Ct);
    }

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

    /// <summary>1. 一覧＋条件＋並び順＋ページング（GetByCustomer）が正しい窓を返す</summary>
    [Fact(DisplayName = "[NamedQuery] 1: 一覧＋条件＋並び順＋ページングが正しい窓を返す")]
    public async Task DslList_WithPaging_ReturnsOrderedWindow()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // 顧客 1 の注文は 13, 11, 10（注文ID降順）。skip=1, take=2 → 11, 10
        var window = await orders.GetByCustomerAsync(1, take: 2, skip: 1, Ct);
        window.Select(o => o.OrderId.Value).Should().Equal(11, 10);

        // skip 既定（0）
        var top = await orders.GetByCustomerAsync(1, take: 2, cancellationToken: Ct);
        top.Select(o => o.OrderId.Value).Should().Equal(13, 11);
    }

    /// <summary>2. 単一（FindTop）が並び順先頭の 1 件を返す（該当なしは null）</summary>
    [Fact(DisplayName = "[NamedQuery] 2: 単一クエリが並び順先頭の 1 件を返す")]
    public async Task DslSingle_ReturnsFirstByOrdering()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var top = await orders.FindTopAsync(Ct);
        top.Should().NotBeNull();
        top!.OrderId.Value.Should().Be(13);
    }

    /// <summary>3. 件数（CountByCustomer）が条件一致数を返す</summary>
    [Fact(DisplayName = "[NamedQuery] 3: 件数クエリが条件一致数を返す")]
    public async Task DslCount_ReturnsMatchingCount()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        (await orders.CountByCustomerAsync(1, Ct)).Should().Be(3);
        (await orders.CountByCustomerAsync(2, Ct)).Should().Be(1);
        (await orders.CountByCustomerAsync(999, Ct)).Should().Be(0);
    }

    /// <summary>4. 文字列一致（SearchMemo・LIKE→部分一致）が正しい行を返す</summary>
    [Fact(DisplayName = "[NamedQuery] 4: LIKE（部分一致）クエリが正しい行を返す")]
    public async Task DslStringMatch_ReturnsContainsMatches()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var apples = await orders.SearchMemoAsync("apple", Ct);
        apples.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 12]);

        (await orders.SearchMemoAsync("nothing", Ct)).Should().BeEmpty();
    }

    /// <summary>5. IN（GetByIds・VO 列×リストパラメータ）が正しい行を返す（存在しない ID は無視）</summary>
    [Fact(DisplayName = "[NamedQuery] 5: IN（VO 列×リストパラメータ）が正しい行を返す")]
    public async Task DslIn_ReturnsMatchingRows()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var found = await orders.GetByIdsAsync([10, 12, 999], Ct);
        found.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 12]);

        (await orders.GetByIdsAsync([], Ct)).Should().BeEmpty();
    }

    /// <summary>6. 射影（GetSummaries）が DTO の一覧を並び順・ページング込みで返す</summary>
    [Fact(DisplayName = "[NamedQuery] 6: 射影クエリが DTO 一覧を返す")]
    public async Task DslProjection_ReturnsDtoRows()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // 顧客 1 の注文（13, 11, 10 の順）→ take=2 で 13, 11 → Amount は 75, 50
        var rows = await orders.GetSummariesAsync(1, take: 2, skip: 0, Ct);
        rows.Should().HaveCount(2);
        rows.Select(r => r.CustomerId!.Value).Should().OnlyContain(v => v == 1);
        rows.Select(r => r.Amount!.Value).Should().Equal(75m, 50m);
    }

    /// <summary>9. manual クエリ（SpecialLookup・partial 実装）が契約経由で呼び出せる</summary>
    [Fact(DisplayName = "[NamedQuery] 9: manual クエリ（partial 実装）が契約経由で動く")]
    public async Task Manual_PartialImplementation_Works()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var first = await orders.SpecialLookupAsync(1, Ct);
        first.Should().NotBeNull();
        first!.OrderId.Value.Should().Be(10);
    }

    /// <summary>10. 列参照型付け（VO 型引数）のクエリが VO のまま呼び出せ、全バックエンドで正しい行を返す</summary>
    [Fact(DisplayName = "[NamedQuery] 10: VO 型引数のクエリが正しい行を返す")]
    public async Task DslColumnTypedParameter_AcceptsValueObject()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // 引数がプリミティブでなく VO 型（CustomerIdValue）そのもの＝呼び出し側の型安全性が上がる
        var found = await orders.GetByCustomerTypedAsync(CustomerIdValue.Create(1), Ct);
        found.Select(o => o.OrderId.Value).Should().Equal(10, 11, 13);

        (await orders.GetByCustomerTypedAsync(CustomerIdValue.Create(999), Ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 11. Include＋射影（ナビゲーション参照）は列刈り込み対象外で従来経路（全列取得→メモリ内射影）へ
    /// フォールバックし、Include で読み込んだナビゲーションを射影できる（全実装で同一結果）。
    /// </summary>
    [Fact(
        DisplayName = "[NamedQuery] 11: Include＋射影はフォールバックしナビゲーションを射影できる"
    )]
    public async Task Projection_WithInclude_FallsBackAndProjectsNavigation()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // Include したナビ（Customer）をセレクタが参照する＝列刈り込み不可。従来経路で Customer を読み射影する
        var rows = await orders
            .Query()
            .Where(o => o.CustomerId == CustomerIdValue.Create(1))
            .Include(o => o.Customer)
            .OrderBy(o => o.OrderId)
            .ToProjectionListAsync(
                o => new OrderCustomerRow(o.OrderId.Value, o.Customer.Name.Value),
                Ct
            );

        rows.Select(r => r.OrderId).Should().Equal(10, 11, 13);
        rows.Should().OnlyContain(r => r.CustomerName == "Alice");
    }
}

/// <summary>Include＋射影のフォールバック検証で使う DTO（注文ID と Include で読んだ顧客名）</summary>
public sealed record OrderCustomerRow(int OrderId, string CustomerName);
