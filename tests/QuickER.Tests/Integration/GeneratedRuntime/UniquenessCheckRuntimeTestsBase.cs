using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// UNIQUE 制約ベースの重複事前チェック（<c>CheckUniquenessAsync</c>）を、実 SQLite
/// （一時ファイル DB・Docker 不要＝CI 常時実行）で意味検証するパリティスイートの共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 入力はクエリフィクスチャ（<see cref="QueryFixtureDefinition"/>）で、orders には単一列制約
/// <c>UQ_orders_memo</c>（NULL 許容列）と複合制約（<c>customer_id</c>＋<c>amount</c>・名前なし＝合成名）がある。
/// QuickER の <c>SqliteRepository</c> 版と EF Core Sqlite 版の派生が同一シナリオを流し、
/// 「全実装先で同一テキストの共有本体」が同じ意味論で動くことを証明する。
/// </para>
/// <para>
/// 検証の柱: (1) 重複あり→違反、(2) 自分自身（同一主キーの行）は除外、(3) NULL を含む組はスキップ、
/// (4) 複合制約の判定と合成名、(5) ユーザー定義フック（テスト側の partial 実装）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class UniquenessCheckRuntimeTestsBase : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>合成される複合制約の名前（UniqueConstraint.SynthesizeName と同じ規則）</summary>
    private const string CompositeConstraintName = "UQ_orders_customer_id_amount";

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列（バックエンドはこの実ファイルへ読み書きする）</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>顧客リポジトリを生成する</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>スキーマを作成し、共通のシードデータを投入する</summary>
    /// <remarks>customers: 1=Alice / 2=Bob。orders: (10,顧客1,100,apple pie)・(11,顧客1,50,memo なし)。</remarks>
    private async Task ResetAndSeedAsync()
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
        await orders.InsertAsync(NewOrder(11, 1, 50m, null), Ct);
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

    /// <summary>1. 既存行と同じ値を持つ新規エンティティは単一列制約の違反として報告される</summary>
    [Fact(DisplayName = "[Uniqueness] 1: 既存行と同じ値の新規エンティティが違反になる")]
    public async Task Duplicate_ReportsViolation()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // memo は既存の注文 10 と同じ。金額・顧客は複合制約に触れない組み合わせにする
        var candidate = NewOrder(99, 2, 12m, "apple pie");

        var violations = await orders.CheckUniquenessAsync(candidate, Ct);

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be("UQ_orders_memo");
        violations[0].PropertyNames.Should().Equal(nameof(OrderEntity.Memo));
        violations[0].Message.Should().BeNull();
    }

    /// <summary>2. 同一主キーの行（自分自身）は除外されるため、既存行の再チェックは違反にならない</summary>
    [Fact(DisplayName = "[Uniqueness] 2: 自分自身（同一主キーの行）は除外される")]
    public async Task SameKeyRow_IsExcluded()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var loaded = await orders.GetByIdAsync(OrderIdValue.Create(10), Ct);
        loaded.Should().NotBeNull();

        (await orders.CheckUniquenessAsync(loaded!, Ct)).Should().BeEmpty();
    }

    /// <summary>3. 構成列の値に null を含む組は判定対象外（既存の NULL 行があっても違反にならない）</summary>
    [Fact(DisplayName = "[Uniqueness] 3: NULL を含む組は判定対象外になる")]
    public async Task NullMember_IsSkipped()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // 注文 11 は memo が NULL。memo が NULL の新規エンティティは単一列制約の対象外
        var candidate = NewOrder(99, 2, 12m, memo: null);

        (await orders.CheckUniquenessAsync(candidate, Ct)).Should().BeEmpty();
    }

    /// <summary>4. 複合制約は構成列がすべて一致したときだけ違反になり、名前は合成名になる</summary>
    [Fact(DisplayName = "[Uniqueness] 4: 複合制約は全列一致のときだけ違反になる")]
    public async Task CompositeConstraint_MatchesAllMembers()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // 顧客だけ一致（金額が違う）＝違反なし
        (await orders.CheckUniquenessAsync(NewOrder(99, 1, 12m, "pear"), Ct))
            .Should()
            .BeEmpty();

        // 顧客・金額とも注文 10 と一致＝複合制約の違反（合成名・構成列 2 つ）
        var violations = await orders.CheckUniquenessAsync(NewOrder(99, 1, 100m, "pear"), Ct);

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be(CompositeConstraintName);
        violations[0]
            .PropertyNames.Should()
            .Equal(nameof(OrderEntity.CustomerId), nameof(OrderEntity.Amount));
    }

    /// <summary>5. 複数の制約に同時に違反すると宣言順で両方が報告される</summary>
    [Fact(DisplayName = "[Uniqueness] 5: 複数制約の同時違反が宣言順で並ぶ")]
    public async Task MultipleViolations_AreReportedInDeclarationOrder()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var violations = await orders.CheckUniquenessAsync(NewOrder(99, 1, 100m, "apple pie"), Ct);

        violations
            .Select(violation => violation.ConstraintName)
            .Should()
            .Equal("UQ_orders_memo", CompositeConstraintName);
    }

    /// <summary>6. ユーザー定義フック（partial 実装）の違反が生成分の後ろへ合流する</summary>
    [Fact(DisplayName = "[Uniqueness] 6: ユーザー定義フックの違反が合流する")]
    public async Task CustomCheck_ContributesViolation()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var violations = await orders.CheckUniquenessAsync(
            NewOrder(99, 2, OrderUniquenessCustomCheck.ReservedAmount, "pear"),
            Ct
        );

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be(OrderUniquenessCustomCheck.ConstraintName);
        violations[0].Message.Should().Be(OrderUniquenessCustomCheck.Message);
    }

    /// <summary>一時 DB を破棄する</summary>
    public virtual void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
