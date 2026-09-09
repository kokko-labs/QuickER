using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// インメモリ Repository が図の UNIQUE 制約を実 DB と同じ場面で拒否することを、コミット済みフィクスチャ
/// （<c>InMemoryFixture.g.cs</c> の <c>orders</c> テーブル）の実型に対して検証する。
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
/// </remarks>
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

    [Fact(DisplayName = "Insert: 単一列 UNIQUE 制約の重複を拒否する")]
    public async Task Insert_DuplicateSingleColumn_Rejected()
    {
        var (_, orders) = BuildFresh();
        await orders.InsertAsync(NewOrder(1, 1, 10m, "same"), Ct);

        var act = async () => await orders.InsertAsync(NewOrder(2, 2, 20m, "same"), Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*UQ_orders_memo*"
        );
        (await orders.GetAllAsync(Ct)).Should().ContainSingle("拒否された挿入は何も残さない");
    }

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

    // ===== 直接 Update =====

    [Fact(DisplayName = "Update: 他行と同じ値へ書き換えようとすると拒否される")]
    public async Task Update_CollidesWithOtherRow_Rejected()
    {
        var (_, orders) = BuildFresh();
        await orders.InsertAsync(NewOrder(1, 1, 10m, "alpha"), Ct);
        await orders.InsertAsync(NewOrder(2, 2, 20m, "beta"), Ct);

        var moved = NewOrder(2, 2, 20m, "alpha");
        moved.RowState = RowState.Updated;

        var act = async () => await orders.UpdateAsync(moved, cancellationToken: Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*UQ_orders_memo*"
        );
        (await orders.GetByIdAsync(2, Ct))!
            .Memo.Should()
            .Be("beta", "拒否された更新は書き込まれない");
    }

    [Fact(DisplayName = "Update: 自分自身と同じ値のまま更新しても衝突しない（主キーで自己除外）")]
    public async Task Update_SameValuesOnSelf_Succeeds()
    {
        var (_, orders) = BuildFresh();
        await orders.InsertAsync(NewOrder(1, 1, 10m, "alpha"), Ct);

        var again = NewOrder(1, 1, 10m, "alpha");
        again.RowState = RowState.Updated;

        (await orders.UpdateAsync(again, cancellationToken: Ct)).Should().BeTrue();
        (await orders.GetByIdAsync(1, Ct))!.Memo.Should().Be("alpha");
    }

    // ===== BulkInsert =====

    [Fact(DisplayName = "BulkInsert: バッチ内部の重複を拒否し、部分投入を残さない")]
    public async Task BulkInsert_DuplicateWithinBatch_RejectsWholeBatch()
    {
        var (_, orders) = BuildFresh();

        var act = async () =>
            await orders.BulkInsertAsync(
                [
                    NewOrder(1, 1, 10m, "dup"),
                    NewOrder(2, 2, 20m, "other"),
                    NewOrder(3, 3, 30m, "dup"),
                ],
                Ct
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*UQ_orders_memo*"
        );
        (await orders.GetAllAsync(Ct)).Should().BeEmpty("事前検証で弾くため 1 件も入らない");
    }

    [Fact(DisplayName = "BulkInsert: 既存行との重複も事前に弾き、部分投入を残さない")]
    public async Task BulkInsert_DuplicateWithStoredRow_RejectsWholeBatch()
    {
        var (_, orders) = BuildFresh();
        await orders.InsertAsync(NewOrder(1, 1, 10m, "stored"), Ct);

        var act = async () =>
            await orders.BulkInsertAsync(
                [NewOrder(2, 2, 20m, "fresh"), NewOrder(3, 3, 30m, "stored")],
                Ct
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await orders.GetAllAsync(Ct)).Should().ContainSingle("既存の 1 件だけが残る");
    }

    // ===== グラフ保存（staging 公開時） =====

    [Fact(DisplayName = "SaveAsync: グラフ内の重複を拒否し、保存単位ごと何も公開しない")]
    public async Task SaveAsync_DuplicateWithinGraph_RejectsWholeSave()
    {
        var store = new InMemoryDataStore();
        var customers = new InMemoryCustomerRepository(store);
        var orders = new InMemoryOrderRepository(store);

        var alice = new CustomerEntity
        {
            CustomerId = 1,
            Name = "Alice",
            RowState = RowState.Added,
        };
        alice.Orders.Add(NewOrder(10, 1, 5m, "same"));
        alice.Orders.Add(NewOrder(11, 1, 6m, "same"));

        var act = async () => await customers.SaveAsync(alice, cancellationToken: Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*UQ_orders_memo*"
        );
        (await customers.GetAllAsync(Ct)).Should().BeEmpty("親も含めて 1 行も公開されない");
        (await orders.GetAllAsync(Ct)).Should().BeEmpty();
    }

    [Fact(DisplayName = "SaveAsync: 既に保存済みの行と衝突する追加も拒否する")]
    public async Task SaveAsync_DuplicateWithStoredRow_Rejected()
    {
        var store = new InMemoryDataStore();
        var customers = new InMemoryCustomerRepository(store);
        var orders = new InMemoryOrderRepository(store);
        await orders.InsertAsync(NewOrder(1, 9, 99m, "taken"), Ct);

        var alice = new CustomerEntity
        {
            CustomerId = 1,
            Name = "Alice",
            RowState = RowState.Added,
        };
        alice.Orders.Add(NewOrder(10, 1, 5m, "taken"));

        var act = async () => await customers.SaveAsync(alice, cancellationToken: Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await customers.GetAllAsync(Ct)).Should().BeEmpty();
        (await orders.GetAllAsync(Ct)).Should().ContainSingle();
    }

    /// <summary>
    /// 判定は「保存が残していく状態」に対して行うため、同じ保存単位で値を手放す行と受け取る行があっても通る。
    /// グラフの走査順に依存しないことがここの本題。
    /// </summary>
    [Fact(DisplayName = "SaveAsync: 同じ保存で削除する行の値を別の行が引き継げる")]
    public async Task SaveAsync_ValueFreedByDeleteInSameUnit_Succeeds()
    {
        var store = new InMemoryDataStore();
        var customers = new InMemoryCustomerRepository(store);
        var orders = new InMemoryOrderRepository(store);

        var alice = new CustomerEntity
        {
            CustomerId = 1,
            Name = "Alice",
            RowState = RowState.Added,
        };
        alice.Orders.Add(NewOrder(10, 1, 5m, "handover"));
        await customers.SaveAsync(alice, cancellationToken: Ct);

        // 旧行を削除し、同じ保存で新行が同じ memo を取る
        alice.Orders.Clear();
        var released = NewOrder(10, 1, 5m, "handover");
        released.MarkRemoved();
        alice.Orders.Add(released);
        alice.Orders.Add(NewOrder(11, 1, 6m, "handover"));
        alice.RowState = RowState.Updated;

        await customers.SaveAsync(alice, cancellationToken: Ct);

        var remaining = await orders.GetAllAsync(Ct);
        remaining.Should().ContainSingle();
        remaining[0].OrderId.Should().Be(11);
        remaining[0].Memo.Should().Be("handover");
    }
}
