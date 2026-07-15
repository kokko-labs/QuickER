using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedMultiTargetFixture;
using QuickER.Tests.GeneratedMultiTargetFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedMultiTargetFixture.Repositories.SqlServer;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// QuickER 版 Repository のマルチターゲット構成（SQL Server / SQLite の 2 方言同時生成）を、同一プロセスの
/// 1 つの <see cref="ServiceCollection"/> へ keyed DI で登録し、実 DB（SQL Server=Testcontainers・
/// SQLite=一時ファイル）へ書き分け・読み分けできることを結合検証する。
/// </summary>
/// <remarks>
/// <para>
/// 入力は第4の固定フィクスチャ（<see cref="MultiTargetPortableFixtureDefinition"/>・方言可搬な図を
/// sqlserver / sqlite のQuickER 版 Repository・EF Core なしで生成したもの）。契約型（<see cref="ICustomerRepository"/> /
/// <see cref="IOrderRepository"/> / <see cref="ISqlExecutor"/>）は単一で、方言別 namespace
/// （<c>.Repositories.SqlServer</c> / <c>.Repositories.Sqlite</c>）が実装と DI 拡張を提供する。
/// </para>
/// <para>
/// 核心の検証は「同一契約型を keyed 解決で 2 接続へ正しく書き分け・読み分けられること」。スキーマは各方言の
/// DdlGenerator（<see cref="SqlServerDdlGenerator"/> / <see cref="SqliteDdlGenerator"/>）で作成する。
/// SQL Server 側は既存統合テストと同じく Docker（Testcontainers）依存のため、Docker 不在時は
/// <see cref="SqlServerContainerFixture"/> の検出でスキップされる（CI では常にスキップ）。SQLite 単独で足りる
/// 型検証は <see cref="MultiTargetRepositorySqliteKeyedResolutionTests"/> に分離し CI でも回す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class MultiTargetRepositoryRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ（server 側）</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>local 側の一時ファイル SQLite DB</summary>
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();

    /// <summary>両方言を keyed DI で登録した 1 つのコンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>server 側キー（SQL Server）</summary>
    private const string ServerKey = "server";

    /// <summary>local 側キー（SQLite）</summary>
    private const string LocalKey = "local";

    /// <summary>
    /// 両方言のスキーマを各 DdlGenerator で作成し、1 つの ServiceCollection へ keyed DI 登録する。
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        var diagram = MultiTargetPortableFixtureDefinition.Build();

        // server 側スキーマ（SQL Server 方言 DDL・コンテナは各テストで使い回すため先に初期化）
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(new SqlServerDdlGenerator().Build(diagram), Ct);

        // local 側スキーマ（SQLite 方言 DDL・一時ファイル）
        await _sqlite.ApplyDdlAsync(new SqliteDdlGenerator().Build(diagram), Ct);

        // 同一契約型を方言別に keyed 登録する（サーバー=SQL Server・ローカル=SQLite）
        var services = new ServiceCollection();
        services.AddGeneratedSqlServerRepositories(ServerKey, _fixture.ConnectionString);
        services.AddGeneratedSqliteRepositories(LocalKey, _sqlite.ReadWriteCreateConnectionString);
        _provider = services.BuildServiceProvider();
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        _sqlite.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>keyed で顧客リポジトリを解決する</summary>
    private ICustomerRepository Customers(string key) =>
        _provider.GetRequiredKeyedService<ICustomerRepository>(key);

    /// <summary>keyed で注文リポジトリを解決する</summary>
    private IOrderRepository Orders(string key) =>
        _provider.GetRequiredKeyedService<IOrderRepository>(key);

    /// <summary>指定 ID の顧客エンティティを組み立てる</summary>
    private static CustomerEntity NewCustomer(int id, string name, decimal? balance = null) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(id),
            Name = NameValue.Create(name),
            Balance = balance is null ? null : BalanceValue.Create(balance.Value),
        };

    /// <summary>指定 ID の注文エンティティを組み立てる</summary>
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

    // 1. keyed 解決: server / local が別インスタンス・別方言実装であること

    [Fact(
        DisplayName = "[MultiTarget] 1: keyed 解決で server / local が別インスタンス・別方言実装になる"
    )]
    public void KeyedResolution_DistinctInstances_DifferentDialectImplementations()
    {
        var server = Customers(ServerKey);
        var local = Customers(LocalKey);

        // 同一契約型だが別インスタンス
        server.Should().NotBeSameAs(local);

        // 実装型（方言別 namespace 配下の CustomerRepository）が異なる
        server
            .GetType()
            .FullName.Should()
            .Be(
                "QuickER.Tests.GeneratedMultiTargetFixture.Repositories.SqlServer.CustomerRepository"
            );
        local
            .GetType()
            .FullName.Should()
            .Be("QuickER.Tests.GeneratedMultiTargetFixture.Repositories.Sqlite.CustomerRepository");

        // 契約型はどちらも単一の共有 ICustomerRepository
        server.Should().BeAssignableTo<ICustomerRepository>();
        local.Should().BeAssignableTo<ICustomerRepository>();
    }

    // 2. 書き分け: server に顧客 A、local に顧客 B → 相互汚染なし

    [Fact(
        DisplayName = "[MultiTarget] 2: server と local へ別々に Insert しても相互汚染しない（書き分け）"
    )]
    public async Task WriteSegregation_NoCrossContamination()
    {
        // server 側に顧客 A のみ
        await Customers(ServerKey).InsertAsync(NewCustomer(1, "Alice-Server", balance: 100m), Ct);

        // local 側に顧客 B のみ
        await Customers(LocalKey).InsertAsync(NewCustomer(2, "Bob-Local", balance: 200m), Ct);

        // server 側からは A のみ取得できる
        var serverAll = await Customers(ServerKey).GetAllAsync(Ct);
        serverAll.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1]);
        serverAll.Single().Name.Value.Should().Be("Alice-Server");
        (await Customers(ServerKey).GetByIdAsync(CustomerIdValue.Create(2), Ct))
            .Should()
            .BeNull("local 側の顧客 B は server へは書かれていない");

        // local 側からは B のみ取得できる
        var localAll = await Customers(LocalKey).GetAllAsync(Ct);
        localAll.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([2]);
        localAll.Single().Name.Value.Should().Be("Bob-Local");
        (await Customers(LocalKey).GetByIdAsync(CustomerIdValue.Create(1), Ct))
            .Should()
            .BeNull("server 側の顧客 A は local へは書かれていない");
    }

    // 3. 双方で式木クエリ（Where/OrderBy/ページング）・Include・生 SQL が動作する

    [Fact(
        DisplayName = "[MultiTarget] 3: 両キーで式木クエリ・Include・生 SQL の代表シナリオが動作する"
    )]
    public async Task BothKeys_ExpressionQuery_Include_RawSql_Work()
    {
        foreach (var key in new[] { ServerKey, LocalKey })
        {
            var customers = Customers(key);
            var orders = Orders(key);

            await customers.InsertAsync(NewCustomer(1, "Alice", balance: 100m), Ct);
            await customers.InsertAsync(NewCustomer(2, "Bob", balance: 200m), Ct);
            await customers.InsertAsync(NewCustomer(3, "Alicia", balance: 300m), Ct);
            for (var i = 10; i <= 13; i++)
            {
                await orders.InsertAsync(NewOrder(i, 1, amount: i, memo: null), Ct);
            }

            // (a) Where 式木（Contains → LIKE）
            var likeAli = await customers
                .Query()
                .Where(c => c.Name.Contains("Ali"))
                .ToListAsync(Ct);
            likeAli.Select(c => c.CustomerId.Value).Should().BeEquivalentTo([1, 3], $"key={key}");

            // (b) OrderBy + ページング（整数キー・LIMIT/OFFSET or OFFSET/FETCH）
            var window = await orders
                .Query()
                .OrderBy(o => o.OrderId)
                .Skip(1)
                .Take(2)
                .ToListAsync(Ct);
            window.Select(o => o.OrderId.Value).Should().Equal(11, 12);

            // (c) Include（親→子コレクション）
            var withOrders = await customers
                .Query()
                .Where(c => c.CustomerId == CustomerIdValue.Create(1))
                .Include(c => c.Orders)
                .FirstOrDefaultAsync(Ct);
            withOrders.Should().NotBeNull($"key={key}");
            withOrders!
                .Orders.Select(o => o.OrderId.Value)
                .Should()
                .BeEquivalentTo([10, 11, 12, 13]);

            // (d) 生 SQL（COUNT を int）— 方言別 ISqlExecutor 経由
            var count = await customers.ExecuteScalarSqlAsync<int>(
                "SELECT COUNT(*) FROM " + Quote(key, "customers"),
                null,
                Ct
            );
            count.Should().Be(3, $"key={key}");
        }
    }

    // 4. 同一エンティティインスタンスの受け渡し: local で読んだ Entity を server へ Insert できる

    [Fact(
        DisplayName = "[MultiTarget] 4: local で読んだエンティティをそのまま server へ Insert できる（単一型ゆえマッピング不要）"
    )]
    public async Task SharedEntityInstance_ReadFromLocal_InsertIntoServer()
    {
        // local 側に顧客を用意して読み出す
        await Customers(LocalKey).InsertAsync(NewCustomer(7, "Roaming", balance: 42m), Ct);
        var fromLocal = await Customers(LocalKey).GetByIdAsync(CustomerIdValue.Create(7), Ct);
        fromLocal.Should().NotBeNull();

        // 単一契約型のため、読み出した Entity をそのまま server へ渡せる（型変換・マッピング一切なし）
        // RowState=Unchanged で読まれるため、Insert 経路では新規行として書く
        await Customers(ServerKey).InsertAsync(fromLocal!, Ct);

        var onServer = await Customers(ServerKey).GetByIdAsync(CustomerIdValue.Create(7), Ct);
        onServer.Should().NotBeNull("local で読んだ同一型 Entity が server へ書き込めた");
        onServer!.Name.Value.Should().Be("Roaming");
        onServer.Balance!.Value.Should().Be(42m);

        // 逆方向（server → local）も同一型ゆえ成立する
        var fromServer = await Customers(ServerKey).GetByIdAsync(CustomerIdValue.Create(7), Ct);
        fromServer!.CustomerId = CustomerIdValue.Create(8);
        await Customers(LocalKey).InsertAsync(fromServer, Ct);
        (await Customers(LocalKey).GetByIdAsync(CustomerIdValue.Create(8), Ct))
            .Should()
            .NotBeNull("server で読んだ同一型 Entity が local へ書き込めた");
    }

    // 5. ISqlExecutor も keyed で両方言解決できる

    [Fact(DisplayName = "[MultiTarget] 5: ISqlExecutor も keyed で両方言を別実装として解決できる")]
    public async Task SqlExecutor_KeyedResolution_BothDialects()
    {
        var serverExec = _provider.GetRequiredKeyedService<ISqlExecutor>(ServerKey);
        var localExec = _provider.GetRequiredKeyedService<ISqlExecutor>(LocalKey);

        serverExec.Should().NotBeSameAs(localExec);
        serverExec
            .GetType()
            .FullName.Should()
            .Be("QuickER.Tests.GeneratedMultiTargetFixture.Repositories.SqlServer.SqlExecutor");
        localExec
            .GetType()
            .FullName.Should()
            .Be("QuickER.Tests.GeneratedMultiTargetFixture.Repositories.Sqlite.SqlExecutor");

        // 各実行器が自分の接続先へ生 SQL を投げられる（server=A のみ・local=B のみ）
        await Customers(ServerKey).InsertAsync(NewCustomer(1, "Server"), Ct);
        await Customers(LocalKey).InsertAsync(NewCustomer(2, "Local"), Ct);

        var serverCount = await serverExec.ExecuteScalarSqlAsync<int>(
            "SELECT COUNT(*) FROM " + Quote(ServerKey, "customers"),
            null,
            Ct
        );
        var localCount = await localExec.ExecuteScalarSqlAsync<int>(
            "SELECT COUNT(*) FROM " + Quote(LocalKey, "customers"),
            null,
            Ct
        );

        serverCount.Should().Be(1);
        localCount.Should().Be(1);
    }

    /// <summary>方言ごとの識別子引用（SQL Server=角括弧・SQLite=二重引用符）</summary>
    private static string Quote(string key, string identifier) =>
        key == ServerKey ? $"[{identifier}]" : $"\"{identifier}\"";
}
