using System.Linq;
using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝RowState 等が他フィクスチャの同名型と曖昧にならないようにする
namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// 保存確定（AcceptChanges / AcceptRemoved）で解放された削除行の「部分木ごと Added」を、
/// 3 段（customers → orders → order_lines）のコミット済みフィクスチャ
/// （<c>QueryFixture.g.cs</c>）の実型で検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 固定する規則: 退避要素の受理は行本体だけでなくカスケード子孫も Added にし、子コレクションの削除追跡も
/// 解放する（<c>MarkSubtreeAdded</c>）。行本体だけ Added にすると、同じインスタンスを戻して保存したとき
/// 親だけが INSERT され、Unchanged のままの子孫が黙って保存から抜け落ちる（グラフ保存は Unchanged の行を
/// 書かない＝無音の部分書き込み）。
/// </remarks>
public sealed class EditModelAcceptRemovedSubtreeTests
{
    /// <summary>注文 1 件（明細 1 件つき）を持つ Unchanged な CustomerEntity を作る。</summary>
    private static CustomerEntity BuildCustomerEntity()
    {
        var customer = new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(1),
            Name = NameValue.Create("customer"),
            Balance = BalanceValue.Create(10m),
        };

        var order = new OrderEntity
        {
            OrderId = OrderIdValue.Create(100),
            CustomerId = CustomerIdValue.Create(1),
            Amount = AmountValue.Create(5m),
            Memo = MemoValue.Create("memo"),
        };

        var line = new OrderLineEntity
        {
            LineId = LineIdValue.Create(1000),
            OrderId = OrderIdValue.Create(100),
            ItemName = ItemNameValue.Create("item"),
            Quantity = QuantityValue.Create(2),
        };

        order.OrderLines.Add(line);
        customer.Orders.Add(order);

        customer.MarkUnchanged();
        order.MarkUnchanged();
        line.MarkUnchanged();
        return customer;
    }

    [Fact]
    public void 受理で解放された削除行は子孫ごと挿入対象になり戻せばグラフ全体が保存に載る()
    {
        var model = new CustomerMapper().CreateEditModel(BuildCustomerEntity());
        var order = model.Orders[0];
        var line = order.OrderLines[0];

        model.Orders.Remove(order);
        model.AcceptChanges();

        order.RowState.Should().Be(RowState.Added, "行の実体は消えたので戻すなら挿入");
        line.RowState.Should()
            .Be(
                RowState.Added,
                "カスケード削除は子孫も消した＝子孫も挿入でなければ親だけが保存され黙って欠落する"
            );

        model.Orders.Add(order);
        var graph = new CustomerMapper().CreateEntity(model, includeRemoved: true);

        var reAdded = graph.Orders.Should().ContainSingle().Subject;
        reAdded.RowState.Should().Be(RowState.Added);
        reAdded
            .OrderLines.Should()
            .ContainSingle()
            .Which.RowState.Should()
            .Be(RowState.Added, "戻したグラフは子孫まで INSERT 対象として保存に載る");
    }

    [Fact]
    public void 受理で解放された削除行の子コレクションの削除追跡も解放される()
    {
        var model = new CustomerMapper().CreateEditModel(BuildCustomerEntity());
        var order = model.Orders[0];
        var line = order.OrderLines[0];

        // 先に明細を Remove して注文の削除追跡へ入れてから、注文ごと削除する
        order.OrderLines.Remove(line);
        model.Orders.Remove(order);
        model.AcceptChanges();

        order
            .OrderLines.RemovedItems.Should()
            .BeEmpty("子コレクションの退避リストも受理で解放される");
        line.RowState.Should().Be(RowState.Added, "解放された明細も戻せば挿入になる");
    }
}
