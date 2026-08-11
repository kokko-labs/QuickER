using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedRemoteServiceFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 重複事前チェック（<c>CheckUniquenessAsync</c>）が実 HTTP 越し（Kestrel を 127.0.0.1 の空きポートで起動）でも
/// 同じ結果を返すことを検証する。実 SQLite（一時ファイル DB）＋生成サーバー／クライアントの 3 階層構成。
/// </summary>
/// <remarks>
/// クライアント（<c>Http{Entity}RemoteRepository</c>）は転送するだけで、判定もユーザー定義フックも
/// サーバー側リポジトリで走る。<c>UniquenessViolation</c> が JSON（RemoteJson の設定）で往復できること
/// ＝位置引数レコードのデシリアライズが成立することも同時に固定する。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class UniquenessCheckRemoteRuntimeTests : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>合成される複合制約の名前（UniqueConstraint.SynthesizeName と同じ規則）</summary>
    private const string CompositeConstraintName = "UQ_orders_customer_id_amount";

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

    /// <summary>スキーマ作成 → Kestrel 起動（空きポート）→ HTTP クライアント DI 構築 → シード投入を行う</summary>
    public async ValueTask InitializeAsync()
    {
        await _db.ApplyDdlAsync(RemoteServiceFixtureDefinition.Build(), Ct);

        _server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();

        await Customers.InsertAsync(
            new CustomerEntity
            {
                CustomerId = CustomerIdValue.Create(1),
                Name = NameValue.Create("Alice"),
            },
            Ct
        );
        await Customers.InsertAsync(
            new CustomerEntity
            {
                CustomerId = CustomerIdValue.Create(2),
                Name = NameValue.Create("Bob"),
            },
            Ct
        );
        await Orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
    }

    /// <summary>注文エンティティを組み立てる</summary>
    private static OrderEntity NewOrder(
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

    /// <summary>1. 重複が HTTP 越しに違反として返る（違反レコードが JSON で往復する）</summary>
    [Fact(DisplayName = "[Uniqueness/Remote] 1: 重複が HTTP 越しに違反として返る")]
    public async Task Duplicate_IsReportedOverHttp()
    {
        var violations = await Orders.CheckUniquenessAsync(NewOrder(99, 2, 12m, "apple pie"), Ct);

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be("UQ_orders_memo");
        violations[0].PropertyNames.Should().Equal(nameof(OrderEntity.Memo));
        violations[0].Message.Should().BeNull();
    }

    /// <summary>2. 重複がなければ空リストが返る（自分自身は除外される）</summary>
    [Fact(DisplayName = "[Uniqueness/Remote] 2: 重複なしは空リストが返る")]
    public async Task NoDuplicate_ReturnsEmptyOverHttp()
    {
        (await Orders.CheckUniquenessAsync(NewOrder(99, 2, 12m, "pear"), Ct)).Should().BeEmpty();

        var loaded = await Orders.GetByIdAsync(OrderIdValue.Create(10), Ct);
        loaded.Should().NotBeNull();
        (await Orders.CheckUniquenessAsync(loaded!, Ct)).Should().BeEmpty();
    }

    /// <summary>3. 複合制約の違反（合成名・構成列 2 つ）も HTTP 越しに正しく返る</summary>
    [Fact(DisplayName = "[Uniqueness/Remote] 3: 複合制約の違反が HTTP 越しに返る")]
    public async Task CompositeViolation_IsReportedOverHttp()
    {
        var violations = await Orders.CheckUniquenessAsync(NewOrder(99, 1, 100m, "pear"), Ct);

        violations.Should().ContainSingle();
        violations[0].ConstraintName.Should().Be(CompositeConstraintName);
        violations[0]
            .PropertyNames.Should()
            .Equal(nameof(OrderEntity.CustomerId), nameof(OrderEntity.Amount));
    }

    /// <summary>4. ユーザー定義フックはサーバー側リポジトリで走る（クライアントにフックは無い）</summary>
    [Fact(DisplayName = "[Uniqueness/Remote] 4: ユーザー定義フックがサーバー側で走る")]
    public async Task CustomCheck_RunsOnServer()
    {
        var violations = await Orders.CheckUniquenessAsync(
            NewOrder(99, 2, OrderRepository.ReservedAmount, "pear"),
            Ct
        );

        violations
            .Select(violation => violation.ConstraintName)
            .Should()
            .Equal(OrderRepository.CustomConstraintName);
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
}
