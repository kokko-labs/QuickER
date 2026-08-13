using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedInMemoryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// UNIQUE 制約ベースの重複事前チェック（<c>CheckUniquenessAsync</c>）を<b>インメモリ Repository</b>
/// （<see cref="InMemoryDataStore"/> 共有）で検証する。実 DB を使わないため Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// 共有本体（式木クエリ 1 本）はQuickER 版・EF Core 版と同一テキストで出力されるため、
/// ここではインメモリ実行器がその式木を同じ意味論で評価できること（値オブジェクト無効の図でも同様であること）を確かめる。
/// フィクスチャの orders には単一列制約 <c>UQ_orders_memo</c>（NULL 許容列）と複合制約
/// （<c>customer_id</c>＋<c>amount</c>・名前なし＝合成名）がある。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class UniquenessCheckInMemoryRuntimeTests
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>合成される複合制約の名前（UniqueConstraint.SynthesizeName と同じ規則）</summary>
    private const string CompositeConstraintName = "UQ_orders_customer_id_amount";

    /// <summary>全リポジトリで共有するインメモリストア</summary>
    private readonly InMemoryDataStore _store = new();

    /// <summary>注文リポジトリ（シード済みストアを共有する）</summary>
    private InMemoryOrderRepository Orders => new(_store);

    /// <summary>共通シード（注文 10=顧客1/100/apple pie・11=顧客1/50/memo なし）を投入する</summary>
    private async Task SeedAsync()
    {
        _store.Clear();

        var orders = Orders;
        await orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m, null), Ct);
    }

    /// <summary>注文エンティティを組み立てる（インメモリフィクスチャは値オブジェクト無効）</summary>
    private static OrderEntity NewOrder(
        int orderId,
        int customerId,
        decimal amount,
        string? memo
    ) =>
        new()
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Memo = memo,
        };

    /// <summary>既存要素と同じ値の新規エンティティが単一列制約の違反として報告される</summary>
    [Fact(DisplayName = "[Uniqueness/InMemory] 重複が違反として報告される")]
    public async Task Duplicate_ReportsViolation()
    {
        await SeedAsync();

        var violations = await Orders.CheckUniquenessAsync(NewOrder(99, 2, 12m, "apple pie"), Ct);

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be("UQ_orders_memo");
        violations[0].PropertyNames.Should().Equal(nameof(OrderEntity.Memo));
    }

    /// <summary>同一主キーの行（自分自身）は除外される</summary>
    [Fact(DisplayName = "[Uniqueness/InMemory] 自分自身は除外される")]
    public async Task SameKeyRow_IsExcluded()
    {
        await SeedAsync();

        var loaded = await Orders.GetByIdAsync(10, Ct);
        loaded.Should().NotBeNull();

        (await Orders.CheckUniquenessAsync(loaded!, Ct)).Should().BeEmpty();
    }

    /// <summary>構成列の値に null を含む組は判定対象外になる</summary>
    [Fact(DisplayName = "[Uniqueness/InMemory] NULL を含む組は判定対象外になる")]
    public async Task NullMember_IsSkipped()
    {
        await SeedAsync();

        (await Orders.CheckUniquenessAsync(NewOrder(99, 2, 12m, memo: null), Ct))
            .Should()
            .BeEmpty();
    }

    /// <summary>複合制約は構成列がすべて一致したときだけ違反になり、名前は合成名になる</summary>
    [Fact(DisplayName = "[Uniqueness/InMemory] 複合制約は全列一致のときだけ違反になる")]
    public async Task CompositeConstraint_MatchesAllMembers()
    {
        await SeedAsync();

        (await Orders.CheckUniquenessAsync(NewOrder(99, 1, 12m, "pear"), Ct)).Should().BeEmpty();

        var violations = await Orders.CheckUniquenessAsync(NewOrder(99, 1, 100m, "pear"), Ct);

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be(CompositeConstraintName);
        violations[0]
            .PropertyNames.Should()
            .Equal(nameof(OrderEntity.CustomerId), nameof(OrderEntity.Amount));
    }

    /// <summary>主キー未設定（挿入前）の新規エンティティでも重複が検出される</summary>
    /// <remarks>
    /// このフィクスチャの主キーは非 NULL の <c>int</c>（＝未設定は既定値 0）なので、自分自身の除外は
    /// 従来どおり無条件に連なる。QuickER 版 Repository 側（値オブジェクト／string 主キー）で null になる
    /// 経路と<b>同じ観測結果</b>になることを、インメモリ実行器（式木コンパイル＝C# 意味論）でも固定する。
    /// </remarks>
    [Fact(DisplayName = "[Uniqueness/InMemory] 主キー未設定の新規エンティティでも重複が検出される")]
    public async Task NewEntityWithoutKey_ReportsViolation()
    {
        await SeedAsync();

        var candidate = new OrderEntity
        {
            CustomerId = 2,
            Amount = 12m,
            Memo = "apple pie",
        };

        candidate.OrderId.Should().Be(0, "挿入前のエンティティは主キーを持たない");

        var violations = await Orders.CheckUniquenessAsync(candidate, Ct);

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be("UQ_orders_memo");
    }

    /// <summary>null 変数との等値比較は IS NULL 相当（C# / EF Core / QuickER 版 ADO と同じ意味論）になる</summary>
    /// <remarks>
    /// インメモリ実行器は式木をコンパイルして評価するため元から C# 意味論だが、翻訳器の null 補償を入れた
    /// 実装先（QuickER 版 ADO）と観測結果が一致することをパリティとして固定する。
    /// </remarks>
    [Fact(DisplayName = "[Uniqueness/InMemory] null 変数との == / != が NULL 行と一致する")]
    public async Task NullVariableComparison_MatchesNullRows()
    {
        await SeedAsync();

        string? missing = null;

        var nullRows = await Orders.Query().Where(o => o.Memo == missing).ToListAsync(Ct);
        nullRows.Select(o => o.OrderId).Should().Equal(11);

        var nonNullRows = await Orders.Query().Where(o => o.Memo != missing).ToListAsync(Ct);
        nonNullRows.Select(o => o.OrderId).Should().Equal(10);
    }

    /// <summary>等値の否定 <c>!(==)</c> は <c>!=</c> と同じ行集合を返す（NULL 行を含む）</summary>
    /// <remarks>
    /// インメモリは式木をコンパイルして C# の意味論で評価するため元からこの結果になる。翻訳器側
    /// （QuickER 版 ADO / EF Core）が否定を補償の外側で包まないことのパリティ基準として固定する。
    /// </remarks>
    [Fact(DisplayName = "[Uniqueness/InMemory] !(==) が != と同じ行集合（NULL 行を含む）を返す")]
    public async Task NegatedEqualComparison_MatchesNotEqual()
    {
        await SeedAsync();

        var negated = await Orders.Query().Where(o => !(o.Memo == "apple pie")).ToListAsync(Ct);
        negated.Select(o => o.OrderId).Should().Equal(11);

        var notEqual = await Orders.Query().Where(o => o.Memo != "apple pie").ToListAsync(Ct);
        negated.Select(o => o.OrderId).Should().Equal(notEqual.Select(o => o.OrderId));
    }

    /// <summary>ユーザー定義フック（partial 実装）の違反が生成分の後ろへ合流する</summary>
    [Fact(DisplayName = "[Uniqueness/InMemory] ユーザー定義フックの違反が合流する")]
    public async Task CustomCheck_ContributesViolation()
    {
        await SeedAsync();

        var violations = await Orders.CheckUniquenessAsync(
            NewOrder(99, 2, InMemoryOrderRepository.ReservedAmount, "pear"),
            Ct
        );

        violations
            .Select(violation => violation.ConstraintName)
            .Should()
            .Equal(InMemoryOrderRepository.CustomConstraintName);
    }
}
