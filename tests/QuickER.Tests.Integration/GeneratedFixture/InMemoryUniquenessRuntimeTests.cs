using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.Integration.GeneratedRuntime;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// インメモリ Repository が図の UNIQUE 制約の照合規則（複合制約は構成列がすべて一致したときだけ・値に null を含む組は照合しない）を
/// 実 DB と同じに扱うことを、コミット済みフィクスチャ（<c>InMemoryFixture.g.cs</c> の <c>orders</c> テーブル）の実型に対して検証する。
/// </summary>
/// <remarks>
/// <para>
/// <c>orders</c> は単一列制約 <c>UQ_orders_memo</c>（NULL 許容列 <c>memo</c>）と複合制約
/// <c>UQ_orders_customer_id_amount</c> を持つ。強制点は直接 Insert / Update / BulkInsert と、グラフ保存の
/// staging 公開時の 4 つ。照合規則は重複事前チェック（<c>CheckUniquenessAsync</c>）と同じで、値に null を
/// 含む組は判定対象外・自分自身は主キーで除外する。
/// </para>
/// <para>
/// 違反は <see cref="InvalidOperationException"/>（主キー重複と同じ扱い）。実 DB ではプロバイダ例外として
/// 表面化するため、例外<b>型</b>の一致は原理的に取れない——ここで固定するのは「拒否されること」まで。
/// </para>
/// <para>
/// 単一列の一意制約の重複（直接の挿入・更新・BulkInsert・グラフ保存）と、同じ保存で削除する行の値を引き継ぐ形は、
/// 実装先の一致のシナリオ（<c>constraint-violations</c>・<c>unique-*</c>・<c>bulk-insert-duplicate-rejects-whole-batch</c>・
/// <c>graph-delete-then-add-same-unique-value-*</c>）が顧客の名前・顧客コードで、QuickER 版・EF Core・インメモリ（リモートを含む
/// ものもある）を比べて見る。ここに残すのは、その図に無い複合の一意制約と NULL を許す一意な列の規則だけ。
/// </para>
/// </remarks>
[RuntimeCell(RuntimeFeatures.UniquenessEnforcement, RuntimeTarget.InMemory)]
public sealed class InMemoryUniquenessRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>シード無しのデータストアと orders リポジトリを作る（テストごとに独立）</summary>
    private static (InMemoryDataStore Store, IOrderRepository Orders) BuildFresh()
    {
        var store = new InMemoryDataStore();
        return (store, new InMemoryOrderRepository(store));
    }

    private static OrderEntity NewOrder(int id, int customerId, decimal amount, string? memo) =>
        new()
        {
            OrderId = id,
            CustomerId = customerId,
            Amount = amount,
            Memo = memo,
            RowState = RowState.Added,
        };

    // ===== 直接 Insert =====

    [Fact(
        DisplayName = "Insert: 複合 UNIQUE 制約の重複を拒否する（構成列がすべて一致したときだけ）"
    )]
    public async Task Insert_DuplicateComposite_Rejected()
    {
        var (_, orders) = BuildFresh();
        await orders.InsertAsync(NewOrder(1, 7, 10m, "a"), Ct);

        // 片方だけ一致は違反ではない
        await orders.InsertAsync(NewOrder(2, 7, 11m, "b"), Ct);
        await orders.InsertAsync(NewOrder(3, 8, 10m, "c"), Ct);

        var act = async () => await orders.InsertAsync(NewOrder(4, 7, 10m, "d"), Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*UQ_orders_customer_id_amount*"
        );
        (await orders.GetAllAsync(Ct)).Should().HaveCount(3);
    }

    [Fact(DisplayName = "Insert: 値に null を含む組は照合対象外（NULL の memo は何行でも入る）")]
    public async Task Insert_NullMemberTuple_Allowed()
    {
        var (_, orders) = BuildFresh();

        await orders.InsertAsync(NewOrder(1, 1, 10m, null), Ct);
        await orders.InsertAsync(NewOrder(2, 2, 20m, null), Ct);
        await orders.InsertAsync(NewOrder(3, 3, 30m, null), Ct);

        (await orders.GetAllAsync(Ct)).Should().HaveCount(3);
    }
}
