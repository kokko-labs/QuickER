using System;
using System.Globalization;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の照合糖衣の共通基底を、クエリフィクスチャの生成物（QuickER 版 SQLite とインメモリ）で流す中間の基底。
/// アダプタ（EditModel の組み立て・照合・親への追加）と共通のシードを持ち、派生は注文と顧客のリポジトリの作り方だけを差し込む。
/// </summary>
public abstract class EditModelValidateUniqueQueryFixtureRuntimeTestsBase
    : EditModelValidateUniqueRuntimeTestsBase<OrderEditModel>
{
    /// <summary>実装先の顧客リポジトリ</summary>
    protected abstract ICustomerRepository Customers { get; }

    /// <summary>実装先の注文リポジトリ</summary>
    protected abstract IOrderRepository Orders { get; }

    /// <summary>空の保存先を作る（シードは基底の <see cref="SeedAsync"/> が入れる）</summary>
    protected abstract Task CreateStorageAsync();

    protected override decimal CustomCheckAmount => OrderUniquenessCustomCheck.ReservedAmount;

    protected override string? CustomCheckMessage => OrderUniquenessCustomCheck.Message;

    protected override async Task SetUpAsync()
    {
        await CreateStorageAsync();
        await SeedAsync();
    }

    /// <summary>顧客 1（Alice）と注文 10（顧客 1・100・apple pie）を入れる</summary>
    private async Task SeedAsync()
    {
        await Customers.InsertAsync(
            new CustomerEntity
            {
                CustomerId = CustomerIdValue.Create(1),
                Name = NameValue.Create("Alice"),
            },
            Ct
        );
        await Orders.InsertAsync(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(10),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(100m),
                Memo = MemoValue.Create("apple pie"),
            },
            Ct
        );
    }

    protected override OrderEditModel NewOrder(
        string orderId,
        int customerId,
        decimal amount,
        string memo
    ) =>
        new()
        {
            BindingOrderId = orderId,
            BindingCustomerId = customerId.ToString(CultureInfo.InvariantCulture),
            BindingAmount = amount.ToString(CultureInfo.InvariantCulture),
            BindingMemo = memo,
        };

    protected override Task<bool> ValidateUniqueAsync(OrderEditModel model) =>
        model.ValidateUniqueAsync(Orders, Ct);

    protected override void SetMemo(OrderEditModel model, string memo) => model.BindingMemo = memo;

    protected override bool HasOrderId(OrderEditModel model) => model.OrderId is not null;

    protected override Func<bool> AttachToNewCustomer(OrderEditModel model)
    {
        var customer = new CustomerEditModel { BindingCustomerId = "1", BindingName = "Alice" };
        customer.Orders.Add(model);

        return () => customer.Validate(includeChildren: true);
    }
}
