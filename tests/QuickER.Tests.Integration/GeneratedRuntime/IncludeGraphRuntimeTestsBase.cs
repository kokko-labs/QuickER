using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先に依らない形へ写し取った孫行（<c>OrderLineEntity</c> はフィクスチャごとに別型のため）。
/// </summary>
/// <param name="LineId">明細の主キー</param>
/// <param name="ItemName">品目名</param>
/// <param name="Quantity">数量</param>
/// <param name="RowState">変更追跡状態（列挙体もフィクスチャごとに別型のため名前で持つ）</param>
public sealed record GraphLineRow(int LineId, string ItemName, int Quantity, string RowState);

/// <summary>実装先に依らない形へ写し取った子行（注文）</summary>
/// <param name="OrderId">注文の主キー</param>
/// <param name="Amount">金額</param>
/// <param name="Memo">メモ（NULL 許容）</param>
/// <param name="RowState">変更追跡状態</param>
/// <param name="Lines">明細（<see cref="GraphLineRow.LineId"/> 昇順）</param>
public sealed record GraphOrderRow(
    int OrderId,
    decimal Amount,
    string? Memo,
    string RowState,
    IReadOnlyList<GraphLineRow> Lines
);

/// <summary>実装先に依らない形へ写し取ったルート（顧客）</summary>
/// <param name="CustomerId">顧客の主キー</param>
/// <param name="Name">氏名</param>
/// <param name="RowState">変更追跡状態</param>
/// <param name="Orders">注文（<see cref="GraphOrderRow.OrderId"/> 昇順）</param>
public sealed record GraphCustomerRow(
    int CustomerId,
    string Name,
    string RowState,
    IReadOnlyList<GraphOrderRow> Orders
);

/// <summary>
/// グラフ取得糖衣（<c>SqlQuery&lt;T&gt;.IncludeGraph()</c>）を、実装先
/// （QuickER 版 Repository の SQLite・SQL Server／EF Core／インメモリ）を跨いでパリティ検証する共通基底。
/// </summary>
/// <remarks>
/// <para>
/// <b>ここで確かめるのは実行器の側</b>である。生成される Include ツリーの「形」（兄弟分岐で同一ナビのノードが
/// 1 本・パス上の再訪辺を展開しない）は生成テキストに現れるため単体テスト
/// （<c>IncludeGraphGenerationTests</c>）が固定できる。しかし「その深さのツリーを実行器が本当に解けるか」は
/// 生成テキストに現れない——SQL Server の FOR JSON・SQLite の <c>IncludeLoader</c> マルチクエリ・
/// EF Core の Include 変換・インメモリの FK 復元は<b>それぞれ別実装</b>で、2 階層で緑でも 3 階層目が
/// 黙って空になり得る。
/// </para>
/// <list type="bullet">
///   <item>3 階層チェーン（顧客 → 注文 → 明細）を手動 <c>Include/ThenInclude</c> と同じ結果グラフで返す</item>
///   <item>子 0 件・孫 0 件は空コレクション（＝欠落と区別できる形）で返る</item>
///   <item>取得したグラフは全ノードが <c>Unchanged</c>（＝そのまま保存しても何も起きない）</item>
///   <item>取得 → 孫の追加・変更・削除 → ルートの <c>SaveAsync</c> が往復する</item>
///   <item>葉エンティティの <c>IncludeGraph()</c> は no-op で、素の取得と同じ結果を返す</item>
///   <item>該当キーが無ければ <c>null</c>（＝空グラフや例外ではない）</item>
///   <item>手動の <c>Include(...)</c> 連鎖の途中からでも <c>GetByIdAsync</c> が呼べる</item>
/// </list>
/// <para>
/// キー指定の 1 件取得はすべて糖衣 <c>GetByIdAsync</c> を通す（＝上の全シナリオが 4 実装先で糖衣を経由する）。
/// 比較対照の手動 Include 側だけは <c>Where(...).FirstOrDefaultAsync()</c> のまま残し、糖衣と手書き述語の
/// 結果一致がシナリオ 1 で表明され続けるようにしている。
/// </para>
/// <para>
/// <b>型パラメータで橋を架ける理由</b>: 生成物はフィクスチャごとに別 namespace へ出るため、<c>CustomerEntity</c> も
/// <c>RowState</c> も共通基底からは名指しできない。エンティティ型だけを型引数で受け、値の読み出しと編集を
/// 派生のアダプタへ委ねる（<see cref="UniquenessCheckRuntimeTestsBase{TOrder}"/> と同じ流儀）。
/// </para>
/// </remarks>
/// <typeparam name="TCustomer">顧客エンティティ型（グラフのルート）</typeparam>
/// <typeparam name="TOrder">注文エンティティ型（第 2 階層）</typeparam>
/// <typeparam name="TOrderLine">注文明細エンティティ型（第 3 階層＝葉）</typeparam>
[Trait("Category", "Integration")]
public abstract class IncludeGraphRuntimeTestsBase<TCustomer, TOrder, TOrderLine>
    where TCustomer : class
    where TOrder : class
    where TOrderLine : class
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // ── 派生が差し込むアダプタ ──

    /// <summary>
    /// 保存先（スキーマまたはストア）を用意し、共通のシードデータを投入する。
    /// </summary>
    /// <remarks>
    /// customers: 1=Alice / 2=Bob / 3=Carol（注文なし）。
    /// orders: 10=(顧客1, 100, "apple pie") / 11=(顧客1, 50, memo なし＝明細なし) / 12=(顧客2, 30, "banana")。
    /// order_lines: 100=(注文10, "pen", 2) / 101=(注文10, "ink", 5) / 102=(注文12, "mug", 1)。
    /// </remarks>
    protected abstract Task ResetAndSeedAsync();

    /// <summary><c>Query().IncludeGraph().GetByIdAsync(id)</c> で顧客 1 件を取得する（行なしは null）</summary>
    protected abstract Task<TCustomer?> FetchCustomerWithGraphAsync(int customerId);

    /// <summary>手動の <c>Include(...).ThenInclude(...)</c> ＋ <c>Where</c> で顧客 1 件を取得する（比較対照）</summary>
    protected abstract Task<TCustomer?> FetchCustomerWithManualIncludeAsync(int customerId);

    /// <summary>
    /// 手動の <c>Include(...)</c> 連鎖（＝<c>IncludableSqlQuery</c>）の途中から <c>GetByIdAsync</c> で顧客 1 件を取得する。
    /// </summary>
    /// <remarks>2 階層（顧客 → 注文）で足りる。確かめたいのは連鎖の型のまま糖衣が呼べることそのもの。</remarks>
    protected abstract Task<TCustomer?> FetchCustomerByIdThroughIncludeChainAsync(int customerId);

    /// <summary><c>Query().IncludeGraph()</c> で全顧客を取得する</summary>
    protected abstract Task<IReadOnlyList<TCustomer>> FetchAllCustomersWithGraphAsync();

    /// <summary>葉エンティティ（注文明細）を <c>IncludeGraph()</c> 付きで全件取得する</summary>
    protected abstract Task<IReadOnlyList<TOrderLine>> FetchAllOrderLinesWithGraphAsync();

    /// <summary>顧客グラフを実装先非依存の形へ写し取る（コレクションは主キー昇順へ整列すること）</summary>
    protected abstract GraphCustomerRow Project(TCustomer customer);

    /// <summary>注文明細の主キーを取り出す</summary>
    protected abstract int LineIdOf(TOrderLine line);

    /// <summary>顧客が持つ注文を主キー昇順で取り出す</summary>
    protected abstract IReadOnlyList<TOrder> OrdersOf(TCustomer customer);

    /// <summary>注文が持つ明細を主キー昇順で取り出す</summary>
    protected abstract IReadOnlyList<TOrderLine> LinesOf(TOrder order);

    /// <summary>注文へ新しい明細を追加する（<c>RowState=Added</c> にすること）</summary>
    protected abstract void AddLine(TOrder order, int lineId, string itemName, int quantity);

    /// <summary>明細の数量を変更する（<c>RowState=Updated</c> にすること）</summary>
    protected abstract void ChangeLineQuantity(TOrderLine line, int quantity);

    /// <summary>明細を削除対象にする（<c>RowState=Removed</c> にすること）</summary>
    protected abstract void RemoveLine(TOrderLine line);

    /// <summary>顧客をルートとしてグラフ保存する（カスケード既定＝子孫まで）</summary>
    protected abstract Task<int> SaveCustomerAsync(TCustomer customer);

    /// <summary>
    /// <c>Query().IncludeGraph().Include(親参照).GetByIdAsync(key)</c> の合成で注文 1 件を取得する
    /// （行なしは null）。
    /// </summary>
    protected abstract Task<TOrder?> FetchOrderWithGraphAndParentAsync(int orderId);

    /// <summary>注文の親参照（顧客）ナビゲーションから氏名を取り出す（未ロードなら null）</summary>
    protected abstract string? CustomerNameOf(TOrder order);

    // ── 期待値（全実装先で同一） ──

    /// <summary>シード直後の顧客 1（注文 2 件・うち 1 件は明細 2 件、もう 1 件は明細なし）</summary>
    private static GraphCustomerRow SeededCustomer1() =>
        new(
            1,
            "Alice",
            "Unchanged",
            [
                new GraphOrderRow(
                    10,
                    100m,
                    "apple pie",
                    "Unchanged",
                    [
                        new GraphLineRow(100, "pen", 2, "Unchanged"),
                        new GraphLineRow(101, "ink", 5, "Unchanged"),
                    ]
                ),
                new GraphOrderRow(11, 50m, null, "Unchanged", []),
            ]
        );

    // ── 1. 3 階層チェーンの解決 ──

    /// <summary>1. 3 階層（顧客 → 注文 → 明細）を、手動 Include/ThenInclude と同一の結果グラフで返す</summary>
    /// <remarks>
    /// 手動連鎖との一致だけでなく、期待値そのものも名指しで置く（両方が同じ壊れ方をしても気づけるように）。
    /// </remarks>
    [Fact(DisplayName = "[IncludeGraph] 1: 3 階層が手動 Include/ThenInclude と同一の結果になる")]
    public async Task ThreeLevelChain_MatchesManualInclude()
    {
        await ResetAndSeedAsync();

        var byGraph = await FetchCustomerWithGraphAsync(1);
        var byManual = await FetchCustomerWithManualIncludeAsync(1);

        byGraph.Should().NotBeNull();
        byManual.Should().NotBeNull();

        var expected = SeededCustomer1();

        Project(byGraph!)
            .Should()
            .BeEquivalentTo(expected, options => options.WithStrictOrdering());
        Project(byManual!)
            .Should()
            .BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    // ── 2. 0 件の子・孫 ──

    /// <summary>2. 親 N 件・子 0..N 件・孫 0..N 件のいずれも、欠落ではなく空コレクションとして返る</summary>
    [Fact(DisplayName = "[IncludeGraph] 2: 子 0 件・孫 0 件は空コレクションで返る")]
    public async Task EmptyChildren_AreMaterializedAsEmptyCollections()
    {
        await ResetAndSeedAsync();

        var rows = (await FetchAllCustomersWithGraphAsync())
            .Select(Project)
            .OrderBy(row => row.CustomerId)
            .ToList();

        rows.Should()
            .BeEquivalentTo(
                new[]
                {
                    SeededCustomer1(),
                    new GraphCustomerRow(
                        2,
                        "Bob",
                        "Unchanged",
                        [
                            new GraphOrderRow(
                                12,
                                30m,
                                "banana",
                                "Unchanged",
                                [new GraphLineRow(102, "mug", 1, "Unchanged")]
                            ),
                        ]
                    ),
                    // 注文を 1 件も持たない顧客（＝ルート直下が空）
                    new GraphCustomerRow(3, "Carol", "Unchanged", []),
                },
                options => options.WithStrictOrdering()
            );
    }

    // ── 3. 変更追跡状態 ──

    /// <summary>3. 取得したグラフは全ノードが Unchanged（そのまま保存しても何も起きない状態で返る）</summary>
    [Fact(DisplayName = "[IncludeGraph] 3: 取得したグラフは全ノードが Unchanged")]
    public async Task FetchedGraph_IsEntirelyUnchanged()
    {
        await ResetAndSeedAsync();

        var row = Project((await FetchCustomerWithGraphAsync(1))!);

        var states = new List<string> { row.RowState };
        states.AddRange(row.Orders.Select(order => order.RowState));
        states.AddRange(row.Orders.SelectMany(order => order.Lines).Select(line => line.RowState));

        states.Should().HaveCount(5).And.AllBe("Unchanged");
    }

    // ── 4. 取得 → 編集 → 保存の往復 ──

    /// <summary>4. IncludeGraph で取ったグラフの孫を追加・変更・削除し、ルートの SaveAsync で往復する</summary>
    /// <remarks>
    /// 取得したグラフがそのまま保存の入力になる（＝主キー・親キー・RowState が保存側の期待どおりに満たされている）
    /// ことを確かめる。ここが崩れると「読めるが書き戻せないグラフ」になり、取得糖衣の意味が失われる。
    /// </remarks>
    [Fact(DisplayName = "[IncludeGraph] 4: 取得 → 孫の追加・変更・削除 → ルート保存が往復する")]
    public async Task FetchedGraph_RoundTripsThroughSave()
    {
        await ResetAndSeedAsync();

        var customer = await FetchCustomerWithGraphAsync(1);
        customer.Should().NotBeNull();

        var order = OrdersOf(customer!)[0];
        var lines = LinesOf(order);
        lines.Should().HaveCount(2);

        ChangeLineQuantity(lines[0], 7); // 100: pen 2 → 7
        RemoveLine(lines[1]); // 101: ink を削除
        AddLine(order, 110, "book", 3); // 110: 新規追加

        var saved = await SaveCustomerAsync(customer!);
        saved.Should().BePositive();

        var reloaded = Project((await FetchCustomerWithGraphAsync(1))!);

        reloaded
            .Should()
            .BeEquivalentTo(
                new GraphCustomerRow(
                    1,
                    "Alice",
                    "Unchanged",
                    [
                        new GraphOrderRow(
                            10,
                            100m,
                            "apple pie",
                            "Unchanged",
                            [
                                new GraphLineRow(100, "pen", 7, "Unchanged"),
                                new GraphLineRow(110, "book", 3, "Unchanged"),
                            ]
                        ),
                        new GraphOrderRow(11, 50m, null, "Unchanged", []),
                    ]
                ),
                options => options.WithStrictOrdering()
            );
    }

    // ── 5. 葉エンティティ ──

    /// <summary>5. 子方向ナビを持たない葉エンティティの IncludeGraph() は no-op として素通りする</summary>
    [Fact(DisplayName = "[IncludeGraph] 5: 葉エンティティの IncludeGraph は no-op で行を返す")]
    public async Task LeafEntity_IncludeGraphIsNoOp()
    {
        await ResetAndSeedAsync();

        var lines = await FetchAllOrderLinesWithGraphAsync();

        lines.Select(LineIdOf).OrderBy(id => id).Should().Equal(100, 101, 102);
    }

    // ── 6. 該当キーなし ──

    /// <summary>6. 該当する行が無いキーでは null を返す（空グラフのインスタンスや例外ではない）</summary>
    /// <remarks>
    /// 糖衣は <c>FirstOrDefaultAsync</c> の上に乗るため「行なし＝null」が契約。ここが実装先ごとに割れる
    /// （例外・空インスタンス）と、呼び出し側の null チェックが実装先依存になる。
    /// </remarks>
    [Fact(DisplayName = "[IncludeGraph] 6: 該当キーが無ければ null を返す")]
    public async Task GetById_ReturnsNullForMissingKey()
    {
        await ResetAndSeedAsync();

        var missing = await FetchCustomerWithGraphAsync(999);

        missing.Should().BeNull();
    }

    // ── 7. 手動 Include 連鎖からの呼び出し ──

    /// <summary>7. 手動の Include 連鎖（IncludableSqlQuery）の途中からでも GetByIdAsync が呼べる</summary>
    /// <remarks>
    /// fluent の <c>Include(...)</c> は <c>IncludableSqlQuery</c> を返すため、<c>SqlQuery</c> 版の糖衣だけでは
    /// この呼び出しがそもそも解決しない（＝コンパイルエラー）。オーバーロードが消えたことに気づくための表明で、
    /// 実行結果（Include した子まで載ること）もあわせて確かめる。
    /// </remarks>
    [Fact(DisplayName = "[IncludeGraph] 7: 手動 Include 連鎖の途中から GetByIdAsync が呼べる")]
    public async Task GetById_IsCallableFromIncludeChain()
    {
        await ResetAndSeedAsync();

        var customer = await FetchCustomerByIdThroughIncludeChainAsync(1);
        customer.Should().NotBeNull();

        var row = Project(customer!);

        row.CustomerId.Should().Be(1);
        row.Name.Should().Be("Alice");
        row.Orders.Select(order => order.OrderId).Should().Equal(10, 11);
    }

    // ── 8. IncludeGraph ＋ 追加 Include の合成 ──

    /// <summary>8. IncludeGraph に親参照の Include を重ねて GetByIdAsync できる（両方向が同時に載る）</summary>
    /// <remarks>
    /// <c>Query().IncludeGraph().Include(親参照).GetByIdAsync(key)</c> の合成形。IncludeGraph の閉包は
    /// 子方向のみなので、親参照の Include はツリーに重複ノードを作らず安全に足せる（＝この合成が
    /// 「グラフ＋親」の推奨レシピ）。閉包が含む子方向ナビを重ねて Include すると既知の重複ノード問題を
    /// 踏むため、ここでは安全側の合成だけを契約として固定する。
    /// </remarks>
    [Fact(
        DisplayName = "[IncludeGraph] 8: IncludeGraph に親参照 Include を重ねて GetByIdAsync できる"
    )]
    public async Task GraphWithParentInclude_LoadsBothDirections()
    {
        await ResetAndSeedAsync();

        var order = await FetchOrderWithGraphAndParentAsync(10);
        order.Should().NotBeNull();

        // IncludeGraph 側: 子方向（明細）が末端まで載る
        LinesOf(order!).Select(LineIdOf).Should().Equal(100, 101);

        // 追加 Include 側: 閉包に含まれない親参照が載る
        CustomerNameOf(order!).Should().Be("Alice");

        // 同じ合成経路でも「行なし＝null」の契約は変わらない
        (await FetchOrderWithGraphAndParentAsync(999))
            .Should()
            .BeNull();
    }
}
