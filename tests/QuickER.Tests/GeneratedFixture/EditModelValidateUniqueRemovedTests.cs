using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// 削除マーク（<c>MarkRemoved</c>）した EditModel が DB 照合糖衣（<c>ValidateUniqueAsync</c>）の対象外に
/// なることを、インメモリ Repository の実装（DB 不要・CI 常時実行）で検証する。
/// </summary>
/// <remarks>
/// 削除される行は保存でキーしか使われないため、読み側の検証はすべて削除行を飛ばす
/// （<c>Validate</c>・エラー収集・兄弟間の重複検証）。DB 照合だけがその線引きから漏れると、
/// 「<c>ValidateUniqueAsync</c> は false を返すのに <c>CollectErrors</c> は空」という自己矛盾になり、
/// 画面へ理由を出せないまま保存が止まる。エラーストアには触れないので、削除を取り消した行は次の検証が判断し直す。
/// </remarks>
public sealed class EditModelValidateUniqueRemovedTests
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>注文 10（顧客 1・100・apple pie）を投入したインメモリ注文リポジトリを組む</summary>
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
    private static OrderEditModel NewOrder(int orderId, decimal amount, string memo) =>
        new()
        {
            BindingOrderId = orderId.ToString(),
            BindingCustomerId = "1",
            BindingAmount = amount.ToString(),
            BindingMemo = memo,
        };

    /// <summary>削除マーク行は DB に重複があっても照合を素通りし、エラーも積まれない</summary>
    [Fact(DisplayName = "[ValidateUnique] 削除マーク行は DB 照合の対象外（true・エラーなし）")]
    public async Task RemovedModel_SkipsDatabaseCheck()
    {
        var orders = await SeededOrdersAsync();
        var model = NewOrder(11, 200m, "apple pie");
        model.MarkRemoved();

        (await model.ValidateUniqueAsync(orders, Ct)).Should().BeTrue();

        // 「false かつ CollectErrors 空」の自己矛盾が起きない（読み側フィルタと同じ結論に揃う）
        model.HasErrors.Should().BeFalse();
        model.CollectErrors().Should().BeEmpty();
    }

    /// <summary>削除マークが無ければ従来どおり重複を検出し、取り消せば再び検出対象へ戻る</summary>
    [Fact(
        DisplayName = "[ValidateUnique] 削除マークの有無だけで結論が変わる（対照＋取り消し後の復帰）"
    )]
    public async Task LiveModel_StillDetectsDuplicate()
    {
        var orders = await SeededOrdersAsync();
        var model = NewOrder(11, 200m, "apple pie");

        (await model.ValidateUniqueAsync(orders, Ct)).Should().BeFalse();
        model.CollectErrors().Should().NotBeEmpty();

        // 削除マークを付けると同じ値・同じリポジトリでも素通りする
        model.MarkRemoved();
        (await model.ValidateUniqueAsync(orders, Ct)).Should().BeTrue();

        // 取り消せば次の検証が判断し直す（エラーストアには触れていない）
        model.MarkUnchanged();
        (await model.ValidateUniqueAsync(orders, Ct)).Should().BeFalse();
        model
            .CollectErrors()
            .Select(error => error.Property)
            .Should()
            .Contain(nameof(OrderEditModel.BindingMemo));
    }
}
