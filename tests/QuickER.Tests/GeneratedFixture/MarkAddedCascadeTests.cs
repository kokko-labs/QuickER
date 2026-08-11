using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// <c>EntityBase.MarkAdded(includeChildren)</c> のカスケード形を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 走査対象はグラフ保存と同じ「カスケード対象のナビゲーション」（<c>[NavigationReference(cascade: true)]</c>）で、
/// コレクション（<c>Customer.Orders</c>）と単一参照（<c>Customer.CustomerProfile</c>）の両方を辿る。親方向の参照
/// （<c>Order.Customer</c>）は cascade ではないため辿らない＝走査は木で、循環しない。
/// </para>
/// <para>
/// 既定（引数なし）は従来どおり自ノードだけを Added にする（呼び出し互換）。
/// </para>
/// </remarks>
public sealed class MarkAddedCascadeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>親 1 件・子 2 件・単一参照の子 1 件からなる、状態を触っていないグラフを組む</summary>
    private static CustomerEntity BuildGraph()
    {
        var customer = new CustomerEntity { CustomerId = 1, Name = "Alice" };

        customer.Orders.Add(
            new OrderEntity
            {
                OrderId = 10,
                CustomerId = 1,
                Amount = 10m,
            }
        );
        customer.Orders.Add(
            new OrderEntity
            {
                OrderId = 11,
                CustomerId = 1,
                Amount = 20m,
            }
        );
        customer.CustomerProfile = new CustomerProfileEntity
        {
            ProfileId = 100,
            CustomerId = 1,
            Bio = "bio",
        };

        return customer;
    }

    /// <summary>既定（引数なし）は自ノードのみ＝従来の呼び出しと挙動が変わらない</summary>
    [Fact(DisplayName = "[MarkAdded] 既定は自ノードのみ Added（従来挙動）")]
    public void MarkAdded_WithoutArgument_MarksOnlyItself()
    {
        var customer = BuildGraph();

        customer.MarkAdded();

        customer.RowState.Should().Be(RowState.Added);
        customer.Orders.Should().OnlyContain(o => o.RowState == RowState.Unchanged);
        customer.CustomerProfile!.RowState.Should().Be(RowState.Unchanged);
    }

    /// <summary>includeChildren: true はコレクション・単一参照の両方のカスケード子を Added にする</summary>
    [Fact(
        DisplayName = "[MarkAdded] includeChildren: true はコレクションと単一参照の子を辿って Added にする"
    )]
    public void MarkAdded_WithIncludeChildren_MarksEveryCascadeChild()
    {
        var customer = BuildGraph();

        customer.MarkAdded(includeChildren: true);

        customer.RowState.Should().Be(RowState.Added);
        customer
            .Orders.Should()
            .OnlyContain(o => o.RowState == RowState.Added, "コレクションのカスケード子も Added");
        customer
            .CustomerProfile!.RowState.Should()
            .Be(RowState.Added, "単一参照のカスケード子も Added");
    }

    /// <summary>親方向の参照（cascade でないナビゲーション）は辿らない＝循環せず、無関係な親も巻き込まない</summary>
    [Fact(DisplayName = "[MarkAdded] cascade でない親参照は辿らない（循環しない）")]
    public void MarkAdded_WithIncludeChildren_DoesNotFollowParentReferences()
    {
        var customer = BuildGraph();

        // 子から別の（保存対象でない）親インスタンスへ参照を張る。cascade=false のため辿られてはならない
        var otherCustomer = new CustomerEntity { CustomerId = 2, Name = "Bob" };
        customer.Orders.First().Customer = otherCustomer;
        customer.CustomerProfile!.Customer = otherCustomer;

        customer.MarkAdded(includeChildren: true);

        otherCustomer
            .RowState.Should()
            .Be(RowState.Unchanged, "親方向の参照は cascade 対象でないため辿られない");
    }

    /// <summary>null の単一参照・空コレクションでも例外にならない</summary>
    [Fact(DisplayName = "[MarkAdded] 子が null / 空でも安全に自ノードだけ Added になる")]
    public void MarkAdded_WithIncludeChildren_HandlesEmptyGraph()
    {
        var customer = new CustomerEntity { CustomerId = 1, Name = "Alice" };

        customer.MarkAdded(includeChildren: true);

        customer.RowState.Should().Be(RowState.Added);
        customer.CustomerProfile.Should().BeNull();
    }

    /// <summary>マークしたグラフをそのまま SaveAsync すると全行が INSERT される</summary>
    [Fact(DisplayName = "[MarkAdded] マークしたグラフをそのまま保存すると全行 INSERT される")]
    public async Task MarkAdded_WithIncludeChildren_SavesEveryRow()
    {
        var store = new InMemoryDataStore();
        var customers = new InMemoryCustomerRepository(store);
        var customer = BuildGraph();

        customer.MarkAdded(includeChildren: true);

        (await customers.SaveAsync(customer, cancellationToken: Ct))
            .Should()
            .Be(4, "親 1 件＋注文 2 件＋プロフィール 1 件");

        (await new InMemoryOrderRepository(store).GetAllAsync(Ct)).Should().HaveCount(2);
        (await new InMemoryCustomerProfileRepository(store).GetAllAsync(Ct)).Should().HaveCount(1);
        customer
            .RowState.Should()
            .Be(RowState.Unchanged, "保存後の状態確定は従来どおりカスケードで行われる");
    }
}
