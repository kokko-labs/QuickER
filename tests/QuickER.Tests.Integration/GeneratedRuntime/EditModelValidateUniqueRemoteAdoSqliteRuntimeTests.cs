using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedRemoteServiceFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の照合糖衣（<c>ValidateUniqueAsync</c>）を、リモート（HTTP クライアント＋in-process の Kestrel・サーバーは QuickER 版
/// Repository の SQLite）で検証する派生。
/// </summary>
/// <remarks>
/// <para>
/// リモートサービスを生成した図では、EditModel の <c>ValidateUniqueAsync</c> がリモートの面（<c>IOrderRemoteRepository</c>）を受け、
/// 重複チェックはユーザー定義のチェックも含めてサーバー側のリポジトリで走る。違反が HTTP を往復して同じ形でバインディングプロパティへ
/// 写ることを、基底の同じ表明で確かめる。
/// </para>
/// <para>
/// サーバーのユーザー定義のチェック（<c>RemoteServiceFixtureManualImplementations</c>）はメッセージを指定しないので、メッセージの優先は
/// 確かめない（<see cref="CustomCheckMessage"/> が null）。
/// </para>
/// </remarks>
[RuntimeCell(
    RuntimeFeatures.EditModelValidateUnique,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.AdoSqlite
)]
public sealed class EditModelValidateUniqueRemoteAdoSqliteRuntimeTests
    : EditModelValidateUniqueRuntimeTestsBase<OrderEditModel>
{
    /// <summary>サーバーが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>in-process 起動した Kestrel サーバー</summary>
    private InProcessRemoteServer? _server;

    /// <summary>HTTP クライアント実装を登録した DI コンテナ</summary>
    private ServiceProvider? _clientProvider;

    /// <summary>クライアント側の注文リモート面</summary>
    private IOrderRemoteRepository Orders =>
        _clientProvider!.GetRequiredService<IOrderRemoteRepository>();

    protected override decimal CustomCheckAmount => OrderRepository.ReservedAmount;

    protected override string? CustomCheckMessage => null;

    protected override async Task SetUpAsync()
    {
        await _db.ApplyDdlAsync(RemoteServiceFixtureDefinition.Build(), Ct);

        _server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();

        await _clientProvider
            .GetRequiredService<ICustomerRemoteRepository>()
            .InsertAsync(
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

    protected override async Task TearDownAsync()
    {
        _clientProvider?.Dispose();

        if (_server is not null)
        {
            await _server.StopAsync(CancellationToken.None);
            await _server.DisposeAsync();
        }

        _db.Dispose();
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
