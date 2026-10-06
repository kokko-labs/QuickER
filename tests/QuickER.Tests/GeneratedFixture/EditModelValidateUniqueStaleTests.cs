using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// DB 照合糖衣（<c>ValidateUniqueAsync</c>）の照会が出ている間に値が変わったとき、照会の結果を今の値へ付けないことを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 照会は開始時の確定値で組んだ Entity について行われるため、その間に確定値を編集したり別の照合が始まったりすると、
/// 返ってきた所見は EditModel がもう持っていない値についての結論になる。そのまま登録すると、一意な新しい値に重複エラーが付き、
/// 重複する新しい値は一意として通る。照会の途中で値が変わったら、何も登録せず <c>false</c>（今の値は確かめられていない）を返す。
/// </para>
/// <para>
/// 照会を途中で止めるため、インメモリ Repository を <see cref="DispatchProxy"/> で包み、<c>CheckUniquenessAsync</c> の
/// 結果を受け取った後で門が開くまで返さないようにする（照会が DB を読み終えた後・EditModel へ戻る前の窓）。
/// </para>
/// </remarks>
public sealed class EditModelValidateUniqueStaleTests
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>注文 10（memo = apple pie）を投入したインメモリ注文リポジトリを組む</summary>
    private static async Task<IOrderRepository> SeededOrdersAsync()
    {
        var orders = new InMemoryOrderRepository(new InMemoryDataStore());

        await orders.InsertAsync(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(10),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(100m),
                Memo = MemoValue.Create("apple pie"),
            },
            Ct
        );

        return orders;
    }

    /// <summary>入力済みの注文 EditModel を作る（memo は UQ_orders_memo の構成列）</summary>
    private static OrderEditModel NewOrder(string memo) =>
        new()
        {
            BindingOrderId = "11",
            BindingCustomerId = "1",
            BindingAmount = "200",
            BindingMemo = memo,
        };

    [Fact(
        DisplayName = "[ValidateUnique] 照会中に値を一意なものへ変えると、古い値の重複を付けずに false を返す"
    )]
    public async Task EditDuringCheck_DiscardsStaleFindings()
    {
        var orders = await SeededOrdersAsync();
        var gated = GatedRepository.Wrap(orders);
        var model = NewOrder("apple pie");

        var check = model.ValidateUniqueAsync(gated.Repository, Ct);
        await gated.Answered.Task.WaitAsync(Ct);

        // 照会は apple pie（重複）について答え終えている。ここで一意な値へ変える
        model.BindingMemo = "banana";
        gated.Release();

        (await check).Should().BeFalse("今の値（banana）が一意だとは確かめていない");
        model.HasErrors.Should().BeFalse("apple pie についての重複を banana へ付けない");

        // やり直せば今の値について判断する
        (await model.ValidateUniqueAsync(orders, Ct))
            .Should()
            .BeTrue();
    }

    [Fact(
        DisplayName = "[ValidateUnique] 照会中に値を重複するものへ変えると true にせず、やり直しで重複を見つける"
    )]
    public async Task EditToDuplicateDuringCheck_IsNotReportedUnique()
    {
        var orders = await SeededOrdersAsync();
        var gated = GatedRepository.Wrap(orders);
        var model = NewOrder("banana");

        var check = model.ValidateUniqueAsync(gated.Repository, Ct);
        await gated.Answered.Task.WaitAsync(Ct);

        model.BindingMemo = "apple pie";
        gated.Release();

        (await check).Should().BeFalse("banana について一意だった結論を apple pie へ持ち越さない");

        (await model.ValidateUniqueAsync(orders, Ct)).Should().BeFalse();
        model
            .CollectErrors()
            .Select(error => error.Property)
            .Should()
            .Contain(nameof(OrderEditModel.BindingMemo));
    }

    [Fact(DisplayName = "[ValidateUnique] 照合が重なると、先に始めた照合の結果は捨てられる")]
    public async Task OverlappingChecks_OnlyTheLatestRegisters()
    {
        var orders = await SeededOrdersAsync();
        var gated = GatedRepository.Wrap(orders);
        var model = NewOrder("apple pie");

        // 先の照合は「重複あり」と答えたところで止める
        var first = model.ValidateUniqueAsync(gated.Repository, Ct);
        await gated.Answered.Task.WaitAsync(Ct);

        // その間に相手の行が消え、後から始めた照合は「重複なし」と結論する
        await orders.DeleteAsync(OrderIdValue.Create(10), Ct);
        (await model.ValidateUniqueAsync(orders, Ct)).Should().BeTrue();

        gated.Release();

        (await first).Should().BeFalse();
        model
            .HasErrors.Should()
            .BeFalse("後から始めた照合の結論を、先の照合の古い所見で上書きしない");
    }

    /// <summary><c>CheckUniquenessAsync</c> の結果を、門が開くまで呼び出し元へ返さない Repository の包み</summary>
    public class GatedRepository : DispatchProxy
    {
        private IOrderRepository _inner = null!;
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        /// <summary>包んだ照会が結果を受け取った（門の前で待ち始めた）ことの合図</summary>
        public TaskCompletionSource Answered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>包んだ Repository</summary>
        public IOrderRepository Repository { get; private set; } = null!;

        /// <summary>インメモリ Repository を包む</summary>
        public static GatedRepository Wrap(IOrderRepository inner)
        {
            var proxy = Create<IOrderRepository, GatedRepository>();
            var gated = (GatedRepository)(object)proxy;
            gated._inner = inner;
            gated.Repository = proxy;
            return gated;
        }

        /// <summary>止めている照会を返させる</summary>
        public void Release() => _gate.TrySetResult();

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (targetMethod.Name == nameof(IOrderRepository.CheckUniquenessAsync))
            {
                return CheckAfterGateAsync((OrderEntity)args![0]!, (CancellationToken)args[1]!);
            }

            return targetMethod.Invoke(_inner, args);
        }

        /// <summary>照会を済ませてから門で待ち、開いたら結果を返す</summary>
        private async Task<IReadOnlyList<UniquenessViolation>> CheckAfterGateAsync(
            OrderEntity entity,
            CancellationToken cancellationToken
        )
        {
            var violations = await _inner.CheckUniquenessAsync(entity, cancellationToken);
            Answered.TrySetResult();
            await _gate.Task.WaitAsync(cancellationToken);
            return violations;
        }
    }
}
