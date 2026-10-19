using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBackendParityFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>一致テストで比べる実装先</summary>
public enum ParityBackendKind
{
    /// <summary>QuickER 版 Repository（SQLite 実ファイル）＝ほかの実装先の基準</summary>
    Ado,

    /// <summary>EF Core（SQLite 実ファイル）</summary>
    EfCore,

    /// <summary>インメモリ Repository</summary>
    InMemory,

    /// <summary>リモート（in-process Kestrel・サーバー側の実体は QuickER 版 Repository の SQLite 実ファイル）</summary>
    Remote,
}

/// <summary>シナリオが実装先の組み立てに求める条件</summary>
/// <param name="WithHooks">記録するだけの Save フックを登録するか（EF Core では保存の経路そのものが変わる）</param>
/// <param name="DisableEfCoreSavepoints">EF Core のトランザクションで自動セーブポイントを切るか（MARS と同じ状態を SQLite で作る）</param>
/// <param name="SkipBefore">Before フックが false を返して飛ばす呼び出し（<c>Delete:order_lines/100</c> の形）</param>
public sealed record ParityBackendOptions(
    bool WithHooks = true,
    bool DisableEfCoreSavepoints = false,
    IReadOnlyList<string>? SkipBefore = null
);

/// <summary>
/// 一致テストの 1 つの実装先。操作が使うリポジトリ（リモート面と、あれば全機能面）・同じ保存先を Save フックなしで触る
/// 「ほかの書き手」・フックの記録・読み込んだグラフの置き場（スロット）を持つ。
/// </summary>
/// <remarks>
/// <para>
/// 「ほかの書き手」は、テストの種まきと、別の利用者の書き込み（行の削除・更新）を作るのに使う。SQLite を保存先にする
/// 実装先（QuickER 版・EF Core・リモート）は同じファイルへの QuickER 版 Repository、インメモリは同じストアへの
/// インメモリ Repository で、どちらも Save フックを登録しない。
/// </para>
/// <para>
/// 保存先の状態もこの書き手で読む（<see cref="ReadStateAsync"/>）。SQLite の生の値（decimal・日時の TEXT の書式）は
/// 書いた実装先によって表記が違い得るので、型に戻してから比べる。型に戻せない値を書いた実装先は、読み取りの例外として
/// 違いに現れる。
/// </para>
/// </remarks>
public sealed class ParityBackend : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly IDisposable? _storage;
    private readonly InProcessRemoteServer? _server;

    private ParityBackend(
        ParityBackendKind kind,
        ServiceProvider provider,
        ParityRepositories outside,
        List<string> hooks,
        IDisposable? storage,
        InProcessRemoteServer? server
    )
    {
        Kind = kind;
        _provider = provider;
        _scope = provider.CreateScope();
        Outside = outside;
        Hooks = hooks;
        _storage = storage;
        _server = server;
    }

    /// <summary>実装先の種類</summary>
    public ParityBackendKind Kind { get; }

    /// <summary>Save フックを登録しない、同じ保存先への全機能面のリポジトリ（種まき・ほかの書き手・状態の読み取り）</summary>
    public ParityRepositories Outside { get; }

    /// <summary>Save フックが記録した呼び出し（<c>Before:Delete:order_lines/100</c> の形）</summary>
    public List<string> Hooks { get; }

    /// <summary>操作が読み込んだ・組み立てたグラフの置き場（スロット名 → 根）</summary>
    public Dictionary<string, EntityBaseCore> Slots { get; } = new(StringComparer.Ordinal);

    /// <summary>リモート面の顧客リポジトリ（全実装先にある）</summary>
    public ICustomerRemoteRepository Customers =>
        _scope.ServiceProvider.GetRequiredService<ICustomerRemoteRepository>();

    /// <summary>リモート面の注文リポジトリ（全実装先にある）</summary>
    public IOrderRemoteRepository Orders =>
        _scope.ServiceProvider.GetRequiredService<IOrderRemoteRepository>();

    /// <summary>リモート面の明細リポジトリ（全実装先にある）</summary>
    public IOrderLineRemoteRepository Lines =>
        _scope.ServiceProvider.GetRequiredService<IOrderLineRemoteRepository>();

    /// <summary>全機能面の顧客リポジトリ（リモートには無い）</summary>
    public ICustomerRepository FullCustomers =>
        Kind == ParityBackendKind.Remote
            ? throw new InvalidOperationException(
                "The remote backend has no full-featured surface."
            )
            : _scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

    /// <summary>全機能面の注文リポジトリ（リモートには無い）</summary>
    public IOrderRepository FullOrders =>
        Kind == ParityBackendKind.Remote
            ? throw new InvalidOperationException(
                "The remote backend has no full-featured surface."
            )
            : _scope.ServiceProvider.GetRequiredService<IOrderRepository>();

    /// <summary>全機能面の明細リポジトリ（リモートには無い）</summary>
    public IOrderLineRepository FullLines =>
        Kind == ParityBackendKind.Remote
            ? throw new InvalidOperationException(
                "The remote backend has no full-featured surface."
            )
            : _scope.ServiceProvider.GetRequiredService<IOrderLineRepository>();

    /// <summary>実装先を組み立てる（保存先は空のスキーマ）</summary>
    public static async Task<ParityBackend> CreateAsync(
        ParityBackendKind kind,
        ParityBackendOptions options,
        CancellationToken ct
    )
    {
        var hooks = new List<string>();
        var hook = new RecordingSaveHook(hooks, options.SkipBefore ?? []);

        if (kind == ParityBackendKind.InMemory)
        {
            var services = new ServiceCollection().AddGeneratedInMemoryRepositories(
                seedSampleData: false
            );

            if (options.WithHooks)
            {
                services.AddSaveHook(hook);
            }

            var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<InMemoryDataStore>();
            var outside = new ParityRepositories(
                new InMemoryCustomerRepository(store),
                new InMemoryOrderRepository(store),
                new InMemoryOrderLineRepository(store)
            );

            return new ParityBackend(kind, provider, outside, hooks, null, null);
        }

        var db = SqliteTempDatabase.Create();
        await db.ResetSchemaAsync(ct);
        await db.ApplyDdlAsync(BackendParityFixtureDefinition.Build(), ct);
        var connectionString = db.ReadWriteCreateConnectionString;
        var outsideProvider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(connectionString)
            .AddLightJournal(connectionString)
            .BuildServiceProvider();
        var sqliteOutside = new ParityRepositories(
            outsideProvider.GetRequiredService<ICustomerRepository>(),
            outsideProvider.GetRequiredService<IOrderRepository>(),
            outsideProvider.GetRequiredService<IOrderLineRepository>()
        );
        var storage = new CompositeDisposable(outsideProvider, db);

        switch (kind)
        {
            case ParityBackendKind.Ado:
            {
                var services = new ServiceCollection()
                    .AddGeneratedSqliteRepositories(connectionString)
                    .AddLightJournal(connectionString);

                if (options.WithHooks)
                {
                    services.AddSaveHook(hook);
                }

                return new ParityBackend(
                    kind,
                    services.BuildServiceProvider(),
                    sqliteOutside,
                    hooks,
                    storage,
                    null
                );
            }

            case ParityBackendKind.EfCore:
            {
                var services = new ServiceCollection().AddGeneratedEfCoreRepositories(builder =>
                {
                    if (options.DisableEfCoreSavepoints)
                    {
                        builder.UseSqliteWith(
                            connectionString,
                            DisableSavepointsInterceptor.Instance
                        );
                    }
                    else
                    {
                        builder
                            .UseSqlite(connectionString)
                            .IgnoreServiceProviderCacheLimit()
                            .AddInterceptors(SqliteLightJournal.EfCoreInterceptor);
                    }
                });

                if (options.WithHooks)
                {
                    services.AddSaveHook(hook);
                }

                return new ParityBackend(
                    kind,
                    services.BuildServiceProvider(),
                    sqliteOutside,
                    hooks,
                    storage,
                    null
                );
            }

            case ParityBackendKind.Remote:
            {
                var server = await InProcessRemoteServer.StartAsync(
                    services =>
                    {
                        services
                            .AddGeneratedSqliteRepositories(connectionString)
                            .AddLightJournal(connectionString);

                        if (options.WithHooks)
                        {
                            services.AddSaveHook(hook);
                        }
                    },
                    app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
                    ct
                );
                var client = new ServiceCollection()
                    .AddGeneratedHttpRemoteRepositories(
                        server.BaseAddress(RemotePaths.DefaultPrefix)
                    )
                    .BuildServiceProvider();

                return new ParityBackend(kind, client, sqliteOutside, hooks, storage, server);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>
    /// 保存先の全行を、型に戻した値で 1 行 1 文字列に整える（テーブル名順・キー順。除外 blob は 16 進で含める）。
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadStateAsync(CancellationToken ct)
    {
        var rows = new List<string>();

        foreach (
            var customer in (await Outside.Customers.GetAllAsync(ct)).OrderBy(c =>
                c.CustomerId.Value
            )
        )
        {
            rows.Add(ParityFormat.Customer(customer));
        }

        foreach (
            var order in (
                await Outside.Orders.Query().WithUnboundedBinary().ToListAsync(ct)
            ).OrderBy(o => o.OrderId.Value)
        )
        {
            rows.Add(ParityFormat.Order(order, includeBlobs: true));
        }

        foreach (var line in (await Outside.Lines.GetAllAsync(ct)).OrderBy(l => l.LineId.Value))
        {
            rows.Add(ParityFormat.Line(line));
        }

        return rows;
    }

    /// <summary>スコープ・コンテナ・サーバー・一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _provider.DisposeAsync();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _storage?.Dispose();
    }

    /// <summary>EF Core のトランザクションが始まるたびに、その DbContext の自動セーブポイントを切る</summary>
    /// <remarks>
    /// 状態を持たないので 1 インスタンスを使い回す（インスタンスが違うと EF Core は内部のサービスプロバイダを別に作り、プロセス
    /// 全体で 20 を超えると ManyServiceProvidersCreatedWarning でほかのテストを巻き込んで失敗させる）。
    /// </remarks>
    private sealed class DisableSavepointsInterceptor : DbTransactionInterceptor
    {
        public static readonly DisableSavepointsInterceptor Instance = new();

        public override DbTransaction TransactionStarted(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result
        )
        {
            Disable(eventData);

            return result;
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default
        )
        {
            Disable(eventData);

            return ValueTask.FromResult(result);
        }

        private static void Disable(TransactionEndEventData eventData)
        {
            if (eventData.Context is { } context)
            {
                context.Database.AutoSavepointsEnabled = false;
            }
        }
    }

    /// <summary>複数の破棄対象を順に破棄する</summary>
    private sealed class CompositeDisposable(params IDisposable[] items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    /// <summary>
    /// 呼び出しを記録する Save フック（3 テーブルとも）。<paramref name="skip"/> に挙げた呼び出しだけ Before が false を返して
    /// その行を飛ばし、ほかは常に続行する。
    /// </summary>
    private sealed class RecordingSaveHook(List<string> log, IReadOnlyList<string> skip)
        : ISaveHook<CustomerEntity>,
            ISaveHook<OrderEntity>,
            ISaveHook<OrderLineEntity>
    {
        public Task<bool> BeforeSaveAsync(
            CustomerEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Record("Before", operation, ParityFormat.KeyOf(entity));

        public Task AfterSaveAsync(
            CustomerEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => Record("After", operation, ParityFormat.KeyOf(entity));

        public Task<bool> BeforeSaveAsync(
            OrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Record("Before", operation, ParityFormat.KeyOf(entity));

        public Task AfterSaveAsync(
            OrderEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => Record("After", operation, ParityFormat.KeyOf(entity));

        public Task<bool> BeforeSaveAsync(
            OrderLineEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Record("Before", operation, ParityFormat.KeyOf(entity));

        public Task AfterSaveAsync(
            OrderLineEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => Record("After", operation, ParityFormat.KeyOf(entity));

        private Task<bool> Record(string phase, SaveOperation operation, string key)
        {
            lock (log)
            {
                log.Add($"{phase}:{operation}:{key}");
            }

            return Task.FromResult(!skip.Contains($"{operation}:{key}"));
        }
    }
}

/// <summary>同じ保存先への全機能面のリポジトリ 3 つ</summary>
public sealed record ParityRepositories(
    ICustomerRepository Customers,
    IOrderRepository Orders,
    IOrderLineRepository Lines
);

/// <summary>エンティティ・例外を、実装先に依らない文字列へ整える</summary>
public static class ParityFormat
{
    /// <summary>行のキー（<c>orders/10</c> の形）</summary>
    public static string KeyOf(EntityBaseCore entity) =>
        entity switch
        {
            CustomerEntity c => $"customers/{c.CustomerId?.Value}",
            OrderEntity o => $"orders/{o.OrderId?.Value}",
            OrderLineEntity l => $"order_lines/{l.LineId?.Value}",
            _ => throw new ArgumentException(entity.GetType().Name, nameof(entity)),
        };

    /// <summary>顧客の列</summary>
    public static string Customer(CustomerEntity c) =>
        $"{KeyOf(c)} name={c.Name?.Value} note={Text(c.Note?.Value)}";

    /// <summary>注文の列（<paramref name="includeBlobs"/> のときだけ除外 blob を含める）</summary>
    public static string Order(OrderEntity o, bool includeBlobs)
    {
        var text =
            $"{KeyOf(o)} customer={o.CustomerId?.Value} amount={Number(o.Amount?.Value)} "
            + $"memo={Text(o.Memo?.Value)} at={Time(o.OrderedAt?.Value)} paid={o.IsPaid?.Value}";

        return includeBlobs
            ? $"{text} payload={Hex(o.Payload?.Value)} thumb={Hex(o.Thumb?.Value)}"
            : text;
    }

    /// <summary>明細の列</summary>
    public static string Line(OrderLineEntity l) =>
        $"{KeyOf(l)} order={l.OrderId?.Value} item={l.Item?.Value} qty={l.Quantity?.Value} "
        + $"discount={Number(l.Discount?.Value)}";

    /// <summary>
    /// 呼び出し側のグラフ（根から子への方向）を、行ごとの RowState と列の値で整える。除外 blob は含めない
    /// （EF Core の取得は除外列も読むが、それは docs に書いてある違いで、比べたいのは保存先の値のほう）。
    /// </summary>
    public static IReadOnlyList<string> Graph(EntityBaseCore root)
    {
        var lines = new List<string>();
        Walk(root, 0);

        return lines;

        void Walk(EntityBaseCore entity, int depth)
        {
            var columns = entity switch
            {
                CustomerEntity c => Customer(c),
                OrderEntity o => Order(o, includeBlobs: false),
                OrderLineEntity l => Line(l),
                _ => entity.GetType().Name,
            };
            lines.Add($"{new string(' ', depth * 2)}{entity.RowState} {columns}");

            // 子はキーの順に並べる（Include が返す子の並びは、SQL でも ORDER BY が無ければ決まらない。比べたいのは
            // どの子がどの状態で居るかで、並びではない）
            foreach (
                var child in entity
                    .EnumerateCascadeChildren()
                    .OrderBy(c => KeyOf(c).Split('/')[0], StringComparer.Ordinal)
                    .ThenBy(c => int.TryParse(KeyOf(c).Split('/')[1], out var n) ? n : int.MaxValue)
            )
            {
                Walk(child, depth + 1);
            }
        }
    }

    /// <summary>
    /// 例外を、型・保存の競合の理由・引数名で整える（文言は比べない。リモートの <c>RemoteRepositoryException</c> は
    /// HTTP の状態を添える）。保存先が制約で断った例外は、実装先に依らず <c>throws store-rejected</c> にまとめる。
    /// </summary>
    /// <remarks>
    /// 制約違反（主キー・一意制約・外部キー・NOT NULL）の例外の型は実装先ごとに違い、どれも docs に書いてある：
    /// SQLite の <c>SqliteException</c>（エラーコード 19）、EF Core はそれを包んだ <c>DbUpdateException</c>、インメモリは
    /// 「a real database would raise」と添えた <c>InvalidOperationException</c>、リモートは詳細を隠した 500
    /// （このテストのサーバーは QuickER 版の SQLite なので、500 になるのはサーバー側の制約違反）。比べたいのは「断ったか」で、
    /// 型の違いではない。
    /// </remarks>
    public static string Exception(Exception ex)
    {
        if (IsStoreRejection(ex))
        {
            return "throws store-rejected";
        }

        // 取り消しの例外の型（OperationCanceledException か、その派生の TaskCanceledException）は、取り消しに気付いた場所で
        // 決まる。比べたいのは「取り消したか」で、型の違いではない
        if (ex is OperationCanceledException)
        {
            return "throws canceled";
        }

        var builder = new StringBuilder("throws ").Append(ex.GetType().Name);
        switch (ex)
        {
            case SaveConflictException conflict:
                builder
                    .Append(" reason=")
                    .Append(conflict.Reason)
                    .Append(" key=")
                    .Append(conflict.Key);
                break;
            case ArgumentException argument:
                builder.Append(" param=").Append(argument.ParamName);
                break;
            case RemoteRepositoryException remote:
                builder.Append(" status=").Append((int?)remote.StatusCode);
                break;
        }

        return builder.ToString();
    }

    /// <summary>保存先が制約で断った例外か（<see cref="Exception(System.Exception)"/> の remarks を参照）</summary>
    private static bool IsStoreRejection(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
            {
                return true;
            }
        }

        return ex switch
        {
            InvalidOperationException
                when ex.Message.Contains("a real database would raise", StringComparison.Ordinal) =>
                true,
            RemoteRepositoryException { StatusCode: 500 } => true,
            _ => false,
        };
    }

    private static string Text(string? value) => value is null ? "null" : $"'{value}'";

    private static string Number(decimal? value) =>
        value?.ToString("0.############################", CultureInfo.InvariantCulture) ?? "null";

    private static string Time(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture) ?? "null";

    private static string Hex(byte[]? value) =>
        value is null ? "null"
        : value.Length == 0 ? "empty"
        : Convert.ToHexString(value);
}
