using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedRemoteServiceFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 重複事前チェックのランタイムスイートを<b>実 HTTP 越し</b>（Kestrel を 127.0.0.1 の空きポートで起動）で流す派生。
/// 実 SQLite（一時ファイル DB）＋生成サーバー／クライアントの 3 階層構成で、Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// <para>
/// クライアント（<c>Http{Entity}RemoteRepository</c>）は転送するだけで、判定もユーザー定義フックも
/// サーバー側リポジトリで走る。基底のシナリオが緑になること自体が、<c>UniquenessViolation</c> が
/// JSON（RemoteJson の設定）で往復できること＝位置引数レコードのデシリアライズが成立することの証明になる。
/// </para>
/// <para>
/// 翻訳器の NULL 補償を <c>Query()</c> で直接観測する 2 シナリオは、リモート面に <c>Query()</c> が無い
/// （式木はネットワーク境界を越えられない）ため対象外＝<see cref="UniquenessCheckLocalRuntimeTestsBase{TOrder}"/> ではなく
/// 親の <see cref="UniquenessCheckRuntimeTestsBase{TOrder}"/> を継承する。
/// </para>
/// </remarks>
public sealed class UniquenessCheckRemoteRuntimeTests
    : UniquenessCheckRuntimeTestsBase<OrderEntity>,
        IAsyncLifetime
{
    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>in-process 起動した Kestrel サーバー</summary>
    private InProcessRemoteServer? _server;

    /// <summary>HTTP クライアント実装を登録した DI コンテナ</summary>
    private ServiceProvider? _clientProvider;

    /// <summary>クライアント側の顧客リモート面</summary>
    private ICustomerRemoteRepository Customers =>
        _clientProvider!.GetRequiredService<ICustomerRemoteRepository>();

    /// <summary>クライアント側の注文リモート面</summary>
    private IOrderRemoteRepository Orders =>
        _clientProvider!.GetRequiredService<IOrderRemoteRepository>();

    /// <summary>スキーマ作成 → Kestrel 起動（空きポート）→ HTTP クライアント DI 構築を行う</summary>
    public async ValueTask InitializeAsync()
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
    }

    /// <summary>サーバーを停止し一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_server is not null)
        {
            await _server.StopAsync(CancellationToken.None);
            await _server.DisposeAsync();
        }

        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>サーバーはテストごとに空のスキーマで起動するため、リモート経由でシードを投入するだけでよい</summary>
    protected override async Task ResetAndSeedAsync()
    {
        await Customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await Customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await Orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await Orders.InsertAsync(NewOrder(11, 1, 50m, null), Ct);
    }

    /// <summary>顧客エンティティを組み立てる</summary>
    private static CustomerEntity NewCustomer(int id, string name) =>
        new() { CustomerId = CustomerIdValue.Create(id), Name = NameValue.Create(name) };

    protected override OrderEntity NewOrder(
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

    protected override OrderEntity NewOrderWithoutKey(
        int customerId,
        decimal amount,
        string? memo
    ) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(customerId),
            Amount = AmountValue.Create(amount),
            Memo = memo is null ? null : MemoValue.Create(memo),
        };

    protected override void AssertKeyIsUnset(OrderEntity candidate) =>
        candidate.OrderId.Should().BeNull("挿入前のエンティティは主キーを持たない");

    protected override Task<OrderEntity?> GetOrderAsync(int orderId) =>
        Orders.GetByIdAsync(OrderIdValue.Create(orderId), Ct);

    protected override async Task<IReadOnlyList<UniquenessViolationRow>> CheckUniquenessAsync(
        OrderEntity candidate
    ) =>
        (await Orders.CheckUniquenessAsync(candidate, Ct))
            .Select(v => new UniquenessViolationRow(v.ConstraintName, v.PropertyNames, v.Message))
            .ToList();

    protected override decimal CustomCheckAmount => OrderRepository.ReservedAmount;

    protected override string CustomCheckConstraintName => OrderRepository.CustomConstraintName;

    /// <summary>本フィクスチャのフックはメッセージを指定しない（制約名だけを返す枝の担い手）</summary>
    protected override string? CustomCheckMessage => null;
}
