using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先に依らない形へ写し取った重複違反（<c>UniquenessViolation</c> はフィクスチャごとに別型のため）。
/// </summary>
/// <param name="ConstraintName">制約名（未設定なら合成名）</param>
/// <param name="PropertyNames">構成列に対応するプロパティ名（宣言順）</param>
/// <param name="Message">ユーザー定義チェックが指定したメッセージ（生成分は null）</param>
public sealed record UniquenessViolationRow(
    string ConstraintName,
    IReadOnlyList<string> PropertyNames,
    string? Message
);

/// <summary>
/// UNIQUE 制約ベースの重複事前チェック（<c>CheckUniquenessAsync</c>）を、実装先
/// （QuickER 版 Repository の SQLite・SQL Server／EF Core／インメモリ／HTTP リモート）を跨いで
/// パリティ検証する共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 図はいずれの派生でも同一の形（orders に単一列制約 <c>UQ_orders_memo</c>〔NULL 許容列〕と複合制約
/// <c>customer_id</c>＋<c>amount</c>〔名前なし＝合成名〕）で、判定の共有本体は全実装先で同一テキストの
/// 式木クエリとして出力される。ここではその式木が実装先ごとの実行器で<b>同じ意味論</b>で評価されることを確かめる。
/// </para>
/// <list type="bullet">
///   <item>重複あり→違反（制約名・構成列・メッセージ）</item>
///   <item>自分自身（同一主キーの行）は除外</item>
///   <item>NULL を含む組はスキップ</item>
///   <item>複合制約は全列一致のときだけ違反・名前は合成名</item>
///   <item>複数制約の同時違反は宣言順</item>
///   <item>ユーザー定義フック（テスト側の partial 実装）の違反が生成分の後ろへ合流</item>
///   <item>主キー未設定（挿入前）でも重複が検出される</item>
/// </list>
/// <para>
/// <b>型パラメータで橋を架ける理由</b>: 生成物はフィクスチャごとに別 namespace へ出るため、<c>OrderEntity</c> も
/// <c>UniquenessViolation</c> も共通基底からは名指しできない。エンティティ型だけを型引数で受け、値の組み立てと
/// 違反の写し取りを派生のアダプタへ委ねることで、VO 有効の図と無効の図（インメモリ）が同じシナリオを共有できる。
/// </para>
/// <para>
/// 翻訳器の NULL 補償そのものの検証（<c>== null</c> / <c>!(==)</c>）は <c>Query()</c> を必要とするため、
/// リモート面には無い。<see cref="UniquenessCheckLocalRuntimeTestsBase{TOrder}"/> が担う
/// （＝条件スキップではなくサブクラス階層で分ける）。
/// </para>
/// </remarks>
/// <typeparam name="TOrder">注文エンティティ型（フィクスチャごとに別型）</typeparam>
[Trait("Category", "Integration")]
public abstract class UniquenessCheckRuntimeTestsBase<TOrder>
    where TOrder : class
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>単一列制約の実名（図で明示している）</summary>
    protected const string SingleConstraintName = "UQ_orders_memo";

    /// <summary>合成される複合制約の名前（UniqueConstraint.SynthesizeName と同じ規則）</summary>
    protected const string CompositeConstraintName = "UQ_orders_customer_id_amount";

    // ── 派生が差し込むアダプタ ──

    /// <summary>保存先（スキーマまたはストア）を用意し、共通のシードデータを投入する</summary>
    /// <remarks>customers: 1=Alice / 2=Bob。orders: (10,顧客1,100,apple pie)・(11,顧客1,50,memo なし)。</remarks>
    protected abstract Task ResetAndSeedAsync();

    /// <summary>注文エンティティを組み立てる（VO 有効の図では VO へ包む）</summary>
    protected abstract TOrder NewOrder(int orderId, int customerId, decimal amount, string? memo);

    /// <summary>主キー未設定（挿入前）の注文エンティティを組み立てる</summary>
    protected abstract TOrder NewOrderWithoutKey(int customerId, decimal amount, string? memo);

    /// <summary>主キーが未設定であることを表明する（VO / 参照型キーは null・値型キーは既定値）</summary>
    protected abstract void AssertKeyIsUnset(TOrder candidate);

    /// <summary>主キーで注文を取得する（行なしは null）</summary>
    protected abstract Task<TOrder?> GetOrderAsync(int orderId);

    /// <summary>重複事前チェックを実行し、実装先非依存の形へ写し取って返す</summary>
    protected abstract Task<IReadOnlyList<UniquenessViolationRow>> CheckUniquenessAsync(
        TOrder candidate
    );

    /// <summary>ユーザー定義チェックが弾く金額</summary>
    protected abstract decimal CustomCheckAmount { get; }

    /// <summary>ユーザー定義チェックが返す制約名</summary>
    protected abstract string CustomCheckConstraintName { get; }

    /// <summary>ユーザー定義チェックが返すメッセージ（メッセージを指定しないフィクスチャでは null）</summary>
    protected abstract string? CustomCheckMessage { get; }

    // ── 1. 単一列制約 ──

    /// <summary>1. 既存行と同じ値を持つ新規エンティティは単一列制約の違反として報告される</summary>
    [Fact(DisplayName = "[Uniqueness] 1: 既存行と同じ値の新規エンティティが違反になる")]
    public async Task Duplicate_ReportsViolation()
    {
        await ResetAndSeedAsync();

        // memo は既存の注文 10 と同じ。金額・顧客は複合制約に触れない組み合わせにする
        var violations = await CheckUniquenessAsync(NewOrder(99, 2, 12m, "apple pie"));

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be(SingleConstraintName);
        violations[0].PropertyNames.Should().Equal("Memo");
        violations[0].Message.Should().BeNull("生成分の違反はメッセージを持たない");
    }

    /// <summary>2. 同一主キーの行（自分自身）は除外されるため、既存行の再チェックは違反にならない</summary>
    [Fact(DisplayName = "[Uniqueness] 2: 自分自身（同一主キーの行）は除外される")]
    public async Task SameKeyRow_IsExcluded()
    {
        await ResetAndSeedAsync();

        var loaded = await GetOrderAsync(10);
        loaded.Should().NotBeNull();

        (await CheckUniquenessAsync(loaded!)).Should().BeEmpty();
    }

    /// <summary>3. 構成列の値に null を含む組は判定対象外（既存の NULL 行があっても違反にならない）</summary>
    [Fact(DisplayName = "[Uniqueness] 3: NULL を含む組は判定対象外になる")]
    public async Task NullMember_IsSkipped()
    {
        await ResetAndSeedAsync();

        // 注文 11 は memo が NULL。memo が NULL の新規エンティティは単一列制約の対象外
        (await CheckUniquenessAsync(NewOrder(99, 2, 12m, memo: null)))
            .Should()
            .BeEmpty();
    }

    // ── 4〜5. 複合制約 ──

    /// <summary>4. 複合制約は構成列がすべて一致したときだけ違反になり、名前は合成名になる</summary>
    [Fact(DisplayName = "[Uniqueness] 4: 複合制約は全列一致のときだけ違反になる")]
    public async Task CompositeConstraint_MatchesAllMembers()
    {
        await ResetAndSeedAsync();

        // 顧客だけ一致（金額が違う）＝違反なし
        (await CheckUniquenessAsync(NewOrder(99, 1, 12m, "pear")))
            .Should()
            .BeEmpty();

        // 顧客・金額とも注文 10 と一致＝複合制約の違反（合成名・構成列 2 つ）
        var violations = await CheckUniquenessAsync(NewOrder(99, 1, 100m, "pear"));

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be(CompositeConstraintName);
        violations[0].PropertyNames.Should().Equal("CustomerId", "Amount");
    }

    /// <summary>5. 複数の制約に同時に違反すると宣言順で両方が報告される</summary>
    [Fact(DisplayName = "[Uniqueness] 5: 複数制約の同時違反が宣言順で並ぶ")]
    public async Task MultipleViolations_AreReportedInDeclarationOrder()
    {
        await ResetAndSeedAsync();

        var violations = await CheckUniquenessAsync(NewOrder(99, 1, 100m, "apple pie"));

        violations
            .Select(violation => violation.ConstraintName)
            .Should()
            .Equal(SingleConstraintName, CompositeConstraintName);
    }

    // ── 6. ユーザー定義フック ──

    /// <summary>6. ユーザー定義フック（partial 実装）の違反が生成分の後ろへ合流する</summary>
    [Fact(DisplayName = "[Uniqueness] 6: ユーザー定義フックの違反が合流する")]
    public async Task CustomCheck_ContributesViolation()
    {
        await ResetAndSeedAsync();

        var violations = await CheckUniquenessAsync(NewOrder(99, 2, CustomCheckAmount, "pear"));

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be(CustomCheckConstraintName);

        if (CustomCheckMessage is { } message)
        {
            violations[0].Message.Should().Be(message, "フックが指定したメッセージが優先される");
        }
    }

    // ── 7. 主キー未設定 ──

    /// <summary>7. 主キー未設定（挿入前）の新規エンティティでも重複が検出される</summary>
    /// <remarks>
    /// 自分自身の除外（主キー不一致）は主キーが設定されているときだけ足す。加えて翻訳器の null 補償により
    /// 「値が null の等値比較＝<c>IS [NOT] NULL</c>」となるため、除外条件が残っても全行 UNKNOWN にはならない
    /// （補償前は <c>[order_id] &lt;&gt; @p</c>（@p=NULL）で全行が弾かれ、重複を静かに見逃していた）。
    /// </remarks>
    [Fact(DisplayName = "[Uniqueness] 7: 主キー未設定の新規エンティティでも重複が検出される")]
    public async Task NewEntityWithoutKey_ReportsViolation()
    {
        await ResetAndSeedAsync();

        // 挿入前＝主キー未採番。memo は既存の注文 10 と同じ
        var candidate = NewOrderWithoutKey(2, 12m, "apple pie");
        AssertKeyIsUnset(candidate);

        var violations = await CheckUniquenessAsync(candidate);

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be(SingleConstraintName);
    }
}
