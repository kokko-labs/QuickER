using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 名前付きクエリの生成メソッドを、実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）で意味検証する
/// パリティスイートの共通基底。QuickER の <c>SqliteRepository</c> 版と EF Core Sqlite 版の派生が同一シナリオを流す。
/// </summary>
/// <remarks>
/// <para>
/// 入力はクエリフィクスチャ（<see cref="QueryFixtureDefinition"/>）。ミニ DSL の全戻り形
/// （一覧＋ページング・単一・件数・射影）・文字列一致（LIKE→Contains）・IN（VO 列×リストパラメータ）・
/// 自由 SQL の全戻り形（一覧＝IN のリスト展開・空リスト・単一・件数・スカラー集計・射影）・
/// manual（partial 実装）を、生成された <see cref="IOrderRepository"/> のメソッド呼び出しだけで検証する。
/// </para>
/// <para>
/// EF Core Sqlite の decimal 制約（サーバーサイド比較・並び替え非対応）に合わせ、フィクスチャの
/// クエリ定義は条件・並び替えを整数キーで行う（decimal は射影の実体化にのみ使用）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class NamedQueryRuntimeTestsBase : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列（バックエンドはこの実ファイルへ読み書きする）</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>顧客リポジトリを生成する（QuickER 版 Repository = AddGenerated{方言}Repositories / EF Core = AddGeneratedEfCoreRepositories）</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>スキーマを作成し、共通のシードデータを投入する</summary>
    /// <remarks>
    /// customers: 1=Alice / 2=Bob。orders: (10,顧客1,100,apple pie)・(11,顧客1,50,banana)・
    /// (12,顧客2,200,apple juice)・(13,顧客1,75,memo なし)。
    /// </remarks>
    protected async Task ResetAndSeedAsync()
    {
        await using (var conn = new SqliteConnection(ConnectionString))
        {
            await conn.OpenAsync(Ct);

            await using var drop = conn.CreateCommand();
            drop.CommandText =
                "DROP TABLE IF EXISTS \"orders\"; DROP TABLE IF EXISTS \"customers\";";
            await drop.ExecuteNonQueryAsync(Ct);
        }

        var ddl = new SqliteDdlGenerator().Build(QueryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

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

    /// <summary>7. 自由 SQL のスカラー集計（SumAmounts）が合計を返す（該当なしは null）</summary>
    [Fact(DisplayName = "[NamedQuery] 7: 自由 SQL スカラー（SUM）が合計を返す")]
    public async Task SqlScalar_ReturnsSum()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        (await orders.SumAmountsAsync(1, Ct)).Should().Be(225m);
        (await orders.SumAmountsAsync(999, Ct)).Should().BeNull();
    }

    /// <summary>8. 自由 SQL の IN リスト展開（GetByIdsRaw）が正しい行を返し、空リストは空を返す</summary>
    [Fact(DisplayName = "[NamedQuery] 8: 自由 SQL の IN リスト展開が機能する（空リスト含む）")]
    public async Task SqlList_WithCollectionParameter_ExpandsIn()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var found = await orders.GetByIdsRawAsync([12, 10], Ct);
        found.Select(o => o.OrderId.Value).Should().Equal(10, 12);

        // 空リストは IN (NULL) へ展開され、どの行にも一致しない
        (await orders.GetByIdsRawAsync([], Ct))
            .Should()
            .BeEmpty();
    }

    /// <summary>10. 列参照型付け（VO 型引数）のクエリが VO のまま呼び出せ、両バックエンドで正しい行を返す</summary>
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
    /// フォールバックし、Include で読み込んだナビゲーションを射影できる（Ado・EF Core 両実装で同一結果）。
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

    /// <summary>12. 自由 SQL の単一戻り形（FindTopRaw）が 1 件を返す（行なしは null）</summary>
    [Fact(DisplayName = "[NamedQuery] 12: 自由 SQL の単一戻り形が 1 件（行なしは null）を返す")]
    public async Task SqlSingle_ReturnsFirstRowOrNull()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var top = await orders.FindTopRawAsync(Ct);
        top.Should().NotBeNull();
        top!.OrderId.Value.Should().Be(13);
        top.Memo.Should().BeNull("注文 13 のメモは NULL（VO 復元込みの行マップを確認）");

        // 全行削除後は null
        await orders.ExecuteSqlAsync("DELETE FROM \"orders\"", null, Ct);
        (await orders.FindTopRawAsync(Ct)).Should().BeNull();
    }

    /// <summary>13. 自由 SQL の件数戻り形（CountByCustomerRaw）が条件一致数を返す</summary>
    [Fact(DisplayName = "[NamedQuery] 13: 自由 SQL の件数戻り形が条件一致数を返す")]
    public async Task SqlCount_ReturnsMatchingCount()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        (await orders.CountByCustomerRawAsync(1, Ct)).Should().Be(3);
        (await orders.CountByCustomerRawAsync(2, Ct)).Should().Be(1);
        (await orders.CountByCustomerRawAsync(999, Ct)).Should().Be(0);
    }

    /// <summary>14. 自由 SQL の射影戻り形（GetMemoRowsRaw）が列別名で DTO へマップされる（NULL 列含む）</summary>
    [Fact(DisplayName = "[NamedQuery] 14: 自由 SQL の射影戻り形が DTO 一覧を返す（NULL 列含む）")]
    public async Task SqlProjection_ReturnsDtoRows()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // 顧客 1 の注文（10, 11, 13 の昇順）。13 のメモは NULL＝DTO の null 許容プロパティで受ける
        var rows = await orders.GetMemoRowsRawAsync(1, Ct);
        rows.Select(r => r.OrderId).Should().Equal(10, 11, 13);
        rows.Select(r => r.Memo).Should().Equal("apple pie", "banana", null);

        (await orders.GetMemoRowsRawAsync(999, Ct)).Should().BeEmpty();
    }

    /// <summary>使い終えた一時 DB を破棄する（派生の DI コンテナ破棄は派生側で行う）</summary>
    public virtual void Dispose() => _db.Dispose();
}

/// <summary>Include＋射影のフォールバック検証で使う DTO（注文ID と Include で読んだ顧客名）</summary>
public sealed record OrderCustomerRow(int OrderId, string CustomerName);
