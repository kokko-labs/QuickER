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
using EfOnly = QuickER.Tests.GeneratedBackendParityEfCoreOnlyFixture;

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

    /// <summary>
    /// リモート（in-process Kestrel・サーバー側は EF Core 単独で生成した構成＝BackendParityEfCoreOnlyFixture のエンドポイントと
    /// EF Core 版 Repository の SQLite 実ファイル。クライアントは実装先一致フィクスチャの HTTP クライアント）
    /// </summary>
    RemoteEfCoreOnly,
}

/// <summary>実装先の性質</summary>
public static class ParityBackendKinds
{
    /// <summary>HTTP クライアント越しの実装先か（リモート面しか持たない）</summary>
    public static bool IsRemote(ParityBackendKind kind) =>
        kind is ParityBackendKind.Remote or ParityBackendKind.RemoteEfCoreOnly;

    /// <summary>除外 blob の Stream アクセサが動く実装先か（EF Core の実装先は NotSupported＝docs に書いてある違い）</summary>
    public static bool HasBlobStream(ParityBackendKind kind) =>
        kind is not (ParityBackendKind.EfCore or ParityBackendKind.RemoteEfCoreOnly);
}

/// <summary>シナリオが実装先の組み立てに求める条件</summary>
/// <param name="WithHooks">記録するだけの Save フックを登録するか（EF Core では保存の経路そのものが変わる）</param>
/// <param name="DisableEfCoreSavepoints">EF Core のトランザクションで自動セーブポイントを切るか（MARS と同じ状態を SQLite で作る）</param>
/// <param name="SkipBefore">Before フックが false を返して飛ばす呼び出し（<c>Delete:order_lines/100</c> の形）</param>
/// <param name="HookRules">Save フックの振る舞いの規則（<see cref="ParityHookRule"/>。先に当たった規則だけが効く）</param>
public sealed record ParityBackendOptions(
    bool WithHooks = true,
    bool DisableEfCoreSavepoints = false,
    IReadOnlyList<string>? SkipBefore = null,
    IReadOnlyList<ParityHookRule>? HookRules = null
);

/// <summary>Save フックの振る舞い</summary>
public enum ParityHookBehavior
{
    /// <summary>記録だけして続ける（規則の無い呼び出しと同じ）</summary>
    Record,

    /// <summary>Before が false を返してその行を飛ばす</summary>
    Skip,

    /// <summary>Before が行の値を書き換えてから false を返して飛ばす（H3）</summary>
    EditThenSkip,

    /// <summary>Before が行の値を書き換えて続ける（監査列を埋めるような使い方）</summary>
    EditThenContinue,

    /// <summary>Before が例外を投げる（保存の前に失敗する）</summary>
    ThrowBefore,

    /// <summary>After が例外を投げる（行を書いた後に失敗し、保存を巻き戻す）</summary>
    ThrowAfter,
}

/// <summary>
/// Save フックの振る舞いの規則。<paramref name="Target"/> は呼び出し（<c>Update:customers/1</c>）か、表の全行（<c>Update:customers</c>）
/// </summary>
public sealed record ParityHookRule(string Target, ParityHookBehavior Behavior)
{
    /// <summary>規則が呼び出し（<c>操作:表/キー</c>）に当たるか</summary>
    public bool Matches(string call) =>
        call == Target || call.StartsWith(Target + "/", StringComparison.Ordinal);
}

/// <summary>Save フックの <see cref="ParityHookBehavior.ThrowBefore"/>・<see cref="ParityHookBehavior.ThrowAfter"/> が投げる例外</summary>
public sealed class ParityHookException(string message) : Exception(message);

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

    /// <summary>リモート面の連絡先リポジトリ（全実装先にある）</summary>
    public IContactRemoteRepository Contacts =>
        _scope.ServiceProvider.GetRequiredService<IContactRemoteRepository>();

    /// <summary>全機能面の顧客リポジトリ（リモートには無い）</summary>
    public ICustomerRepository FullCustomers =>
        ParityBackendKinds.IsRemote(Kind)
            ? throw new InvalidOperationException(
                "The remote backend has no full-featured surface."
            )
            : _scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

    /// <summary>全機能面の注文リポジトリ（リモートには無い）</summary>
    public IOrderRepository FullOrders =>
        ParityBackendKinds.IsRemote(Kind)
            ? throw new InvalidOperationException(
                "The remote backend has no full-featured surface."
            )
            : _scope.ServiceProvider.GetRequiredService<IOrderRepository>();

    /// <summary>全機能面の明細リポジトリ（リモートには無い）</summary>
    public IOrderLineRepository FullLines =>
        ParityBackendKinds.IsRemote(Kind)
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
        var recorder = new ParityHookRecorder(
            hooks,
            [
                .. (options.SkipBefore ?? []).Select(target => new ParityHookRule(
                    target,
                    ParityHookBehavior.Skip
                )),
                .. options.HookRules ?? [],
            ]
        );
        var hook = new RecordingSaveHook(recorder);

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
                new InMemoryOrderLineRepository(store),
                new InMemoryContactRepository(store)
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
            outsideProvider.GetRequiredService<IOrderLineRepository>(),
            outsideProvider.GetRequiredService<IContactRepository>()
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

            case ParityBackendKind.RemoteEfCoreOnly:
            {
                var server = await InProcessRemoteServer.StartAsync(
                    services =>
                    {
                        EfOnly.GeneratedEfCoreRepositoryServiceCollectionExtensions.AddGeneratedEfCoreRepositories(
                            services,
                            builder =>
                                builder
                                    .UseSqlite(connectionString)
                                    .IgnoreServiceProviderCacheLimit()
                                    .AddInterceptors(SqliteLightJournal.EfCoreInterceptor)
                        );

                        if (options.WithHooks)
                        {
                            EfOnly.SaveHookServiceCollectionExtensions.AddSaveHook(
                                services,
                                new EfCoreOnlyRecordingSaveHook(recorder)
                            );
                        }
                    },
                    app =>
                        EfOnly.GeneratedRemoteEndpoints.MapGeneratedRemoteEndpoints(
                            app,
                            EfOnly.RemoteAccess.AllowAnonymous
                        ),
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

        foreach (
            var contact in (await Outside.Contacts.GetAllAsync(ct)).OrderBy(c => c.ContactId.Value)
        )
        {
            rows.Add(ParityFormat.Contact(contact));
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

    /// <summary>実装先一致フィクスチャの 4 テーブルの Save フック（呼び出しの記録と振る舞いは <see cref="ParityHookRecorder"/>）</summary>
    /// <remarks>書き換える値は表ごとに 1 つ（顧客の note・注文の memo・明細の discount・連絡先の label）で、操作ごとに違う値にする。</remarks>
    private sealed class RecordingSaveHook(ParityHookRecorder recorder)
        : ISaveHook<CustomerEntity>,
            ISaveHook<OrderEntity>,
            ISaveHook<OrderLineEntity>,
            ISaveHook<ContactEntity>
    {
        public Task<bool> BeforeSaveAsync(
            CustomerEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            recorder.Before(
                $"{operation}:{ParityFormat.KeyOf(entity)}",
                () => entity.Note = NoteValue.Create($"hook-{operation}")
            );

        public Task AfterSaveAsync(
            CustomerEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => recorder.After($"{operation}:{ParityFormat.KeyOf(entity)}");

        public Task<bool> BeforeSaveAsync(
            OrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            recorder.Before(
                $"{operation}:{ParityFormat.KeyOf(entity)}",
                () => entity.Memo = MemoValue.Create($"hook-{operation}")
            );

        public Task AfterSaveAsync(
            OrderEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => recorder.After($"{operation}:{ParityFormat.KeyOf(entity)}");

        public Task<bool> BeforeSaveAsync(
            OrderLineEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            recorder.Before(
                $"{operation}:{ParityFormat.KeyOf(entity)}",
                () => entity.Discount = DiscountValue.Create((int)operation + 0.5m)
            );

        public Task AfterSaveAsync(
            OrderLineEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => recorder.After($"{operation}:{ParityFormat.KeyOf(entity)}");

        public Task<bool> BeforeSaveAsync(
            ContactEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            recorder.Before(
                $"{operation}:{ParityFormat.KeyOf(entity)}",
                () => entity.Label = LabelValue.Create($"hook-{operation}")
            );

        public Task AfterSaveAsync(
            ContactEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => recorder.After($"{operation}:{ParityFormat.KeyOf(entity)}");
    }

    /// <summary>
    /// EF Core 単独で生成したサーバーの 4 テーブルの Save フック（<see cref="RecordingSaveHook"/> と同じ呼び出しの形・同じ書き換え。
    /// 型が別の名前空間なので別のクラスにする）
    /// </summary>
    private sealed class EfCoreOnlyRecordingSaveHook(ParityHookRecorder recorder)
        : EfOnly.ISaveHook<EfOnly.CustomerEntity>,
            EfOnly.ISaveHook<EfOnly.OrderEntity>,
            EfOnly.ISaveHook<EfOnly.OrderLineEntity>,
            EfOnly.ISaveHook<EfOnly.ContactEntity>
    {
        public Task<bool> BeforeSaveAsync(
            EfOnly.CustomerEntity entity,
            EfOnly.SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            recorder.Before(
                $"{operation}:customers/{entity.CustomerId?.Value}",
                () => entity.Note = EfOnly.NoteValue.Create($"hook-{operation}")
            );

        public Task AfterSaveAsync(
            EfOnly.CustomerEntity entity,
            EfOnly.SaveOperation operation,
            EfOnly.ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => recorder.After($"{operation}:customers/{entity.CustomerId?.Value}");

        public Task<bool> BeforeSaveAsync(
            EfOnly.OrderEntity entity,
            EfOnly.SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            recorder.Before(
                $"{operation}:orders/{entity.OrderId?.Value}",
                () => entity.Memo = EfOnly.MemoValue.Create($"hook-{operation}")
            );

        public Task AfterSaveAsync(
            EfOnly.OrderEntity entity,
            EfOnly.SaveOperation operation,
            EfOnly.ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => recorder.After($"{operation}:orders/{entity.OrderId?.Value}");

        public Task<bool> BeforeSaveAsync(
            EfOnly.OrderLineEntity entity,
            EfOnly.SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            recorder.Before(
                $"{operation}:order_lines/{entity.LineId?.Value}",
                () => entity.Discount = EfOnly.DiscountValue.Create((int)operation + 0.5m)
            );

        public Task AfterSaveAsync(
            EfOnly.OrderLineEntity entity,
            EfOnly.SaveOperation operation,
            EfOnly.ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => recorder.After($"{operation}:order_lines/{entity.LineId?.Value}");

        public Task<bool> BeforeSaveAsync(
            EfOnly.ContactEntity entity,
            EfOnly.SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            recorder.Before(
                $"{operation}:contacts/{entity.ContactId?.Value}",
                () => entity.Label = EfOnly.LabelValue.Create($"hook-{operation}")
            );

        public Task AfterSaveAsync(
            EfOnly.ContactEntity entity,
            EfOnly.SaveOperation operation,
            EfOnly.ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => recorder.After($"{operation}:contacts/{entity.ContactId?.Value}");
    }
}

/// <summary>
/// Save フックの呼び出しを記録し、<see cref="ParityHookRule"/> の最初に当たった規則の振る舞いをする（当たらなければ記録だけして
/// 続ける）。呼び出しは <c>操作:表/キー</c> の形
/// </summary>
public sealed class ParityHookRecorder(List<string> log, IReadOnlyList<ParityHookRule> rules)
{
    /// <summary>Before：記録し、振る舞いに従って値を書き換え、続けるか（true）飛ばすか（false）を返すか、投げる</summary>
    public Task<bool> Before(string call, Action edit)
    {
        Record("Before", call);

        switch (BehaviorOf(call))
        {
            case ParityHookBehavior.Skip:
                return Task.FromResult(false);
            case ParityHookBehavior.EditThenSkip:
                edit();
                return Task.FromResult(false);
            case ParityHookBehavior.EditThenContinue:
                edit();
                return Task.FromResult(true);
            case ParityHookBehavior.ThrowBefore:
                Record(ParityStep.HookThrewPrefix + "Before", call);
                throw new ParityHookException($"Before {call}");
            default:
                return Task.FromResult(true);
        }
    }

    /// <summary>After：記録し、振る舞いが <see cref="ParityHookBehavior.ThrowAfter"/> なら投げる</summary>
    public Task After(string call)
    {
        Record("After", call);

        if (BehaviorOf(call) != ParityHookBehavior.ThrowAfter)
        {
            return Task.CompletedTask;
        }

        Record(ParityStep.HookThrewPrefix + "After", call);

        throw new ParityHookException($"After {call}");
    }

    private ParityHookBehavior BehaviorOf(string call) =>
        rules.FirstOrDefault(rule => rule.Matches(call))?.Behavior ?? ParityHookBehavior.Record;

    private void Record(string phase, string call)
    {
        lock (log)
        {
            log.Add($"{phase}:{call}");
        }
    }
}

/// <summary>同じ保存先への全機能面のリポジトリ 4 つ</summary>
public sealed record ParityRepositories(
    ICustomerRepository Customers,
    IOrderRepository Orders,
    IOrderLineRepository Lines,
    IContactRepository Contacts
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
            ContactEntity c => $"contacts/{c.ContactId?.Value}",
            _ => throw new ArgumentException(entity.GetType().Name, nameof(entity)),
        };

    /// <summary>顧客の列</summary>
    public static string Customer(CustomerEntity c) =>
        $"{KeyOf(c)} name={c.Name?.Value} note={Text(c.Note?.Value)} code={Text(c.Code?.Value)} "
        + $"referrer={Id(c.ReferrerId?.Value)}";

    /// <summary>連絡先の列</summary>
    public static string Contact(ContactEntity c) =>
        $"{KeyOf(c)} label={Text(c.Label?.Value)} owner={Id(c.OwnerId?.Value)} "
        + $"watcher={Id(c.WatcherId?.Value)} backup={Id(c.BackupId?.Value)} "
        + $"fallback={Id(c.FallbackId?.Value)} customer_code={Text(c.CustomerCode?.Value)}";

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
                ContactEntity c => Contact(c),
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

    private static string Id(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string Number(decimal? value) =>
        value?.ToString("0.############################", CultureInfo.InvariantCulture) ?? "null";

    private static string Time(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture) ?? "null";

    private static string Hex(byte[]? value) =>
        value is null ? "null"
        : value.Length == 0 ? "empty"
        : Convert.ToHexString(value);
}
