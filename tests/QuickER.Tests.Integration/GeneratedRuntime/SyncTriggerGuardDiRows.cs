using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のトリガーの確認の表（<see cref="SyncTriggerGuardRows"/>）の DI の配線の行（Id は <c>di-</c> で始まる）：生成された
/// <c>AddGeneratedSyncEngine</c> が、ローカルの接続ファクトリの登録を開くたびの確認つきの包みへ差し替え、新しいエンジン
/// （<c>SyncEngine</c>）を組むことを、SyncFixture の SQLite の端末で確かめる。
/// </summary>
/// <remarks>
/// <para>
/// どの行も、行の手 <c>Open</c> の中でサービスの登録を組み（プロバイダはスコープを検査する）、そのコンテナで開いた接続・書き込み・
/// エンジンの実行を表明する。行のガード（表のランナーが作る確認）は使わないので、回数の増え方は 0 で、Repository の書き込みと
/// 速い道の共通の表明は掛けない。DB のファイルの共通の表明（行のガードが見る表のトリガーと状態の表）は、DI の登録が接続を開いて
/// トリガーを張る行だけに掛け、除外の行は行のガードが見る表を同期する表に絞る。
/// </para>
/// <para>
/// サーバーが要る行は、テストのサーバー役（<see cref="SyncFixtureTestServer"/>）の差分ソースをスコープの登録で渡す
/// （差分ソースの半分は <c>AddGeneratedSyncEngine</c> が引くだけで登録しない）。
/// </para>
/// </remarks>
internal static class SyncTriggerGuardDiRows
{
    /// <summary>ローカル（SQLite）の登録のキー</summary>
    private const string LocalKey = "local";

    /// <summary>同じコンテナのサーバーの側の登録のキー（どちらも SQLite の方言の型で登録する）</summary>
    private const string ServerKey = "server";

    /// <summary>除外の行で同期する表（<c>sync_notes</c> を除く）</summary>
    private static readonly IReadOnlyList<string> WithoutNotes =
    [
        "sync_orders",
        "sync_markers",
        "sync_order_lines",
    ];

    /// <summary>DI の配線の行</summary>
    public static IEnumerable<ScenarioRow<SyncTriggerGuardCase>> Rows() =>
        [
            Row(
                "di-keyed-registration-records-repository-writes",
                "keyed のローカルの登録に AddGeneratedSyncEngine を掛けると、keyed の接続ファクトリが確認つきの包みになり、keyed の"
                    + " Repository の書き込みと生 SQL の書き込みがどちらも行の状態の表に残る（同じコンテナのサーバーの側の登録は包まない）",
                KeyedRegistrationRecordsWritesAsync
            ),
            Row(
                "di-unkeyed-registration-records-repository-writes",
                "キーなしのローカルの登録に AddGeneratedSyncEngine(null) を掛けると、コンストラクタ注入の Repository と生 SQL の"
                    + "書き込みがどちらも行の状態の表に残る",
                UnkeyedRegistrationRecordsWritesAsync
            ),
            Row(
                "di-instance-registered-connection-factory-is-wrapped",
                "Repository の登録の後にインスタンスで登録し直した接続ファクトリは、そのインスタンスを内側にして包まれ（書き込みは"
                    + "そのインスタンスの DB へ行き記録される）、シングルトンのままどのスコープでも同じ包みが返る",
                InstanceRegisteredFactoryIsWrappedAsync
            ),
            Row(
                "di-excluded-table-gets-no-trigger-and-is-not-recorded",
                "excludeFromSync に sync_notes を渡すと、最初の接続を開いた後 sync_notes にトリガーが無く、ほかの 3 表には在り、"
                    + "sync_notes への書き込みは記録されず sync_orders への書き込みは記録される",
                ExcludedTableGetsNoTriggerAsync,
                WithoutNotes
            ),
            Row(
                "di-unknown-exclusion-is-refused",
                "同期する型でない除外の指定は、AddGeneratedSyncEngine が ArgumentException で断る（黙って無視すると効いたつもりになる）",
                UnknownExclusionIsRefusedAsync,
                checksFile: false
            ),
            Row(
                "di-excluding-everything-is-refused",
                "同期する表を全部除外する指定は、AddGeneratedSyncEngine が ArgumentException で断る",
                ExcludingEverythingIsRefusedAsync,
                checksFile: false
            ),
            Row(
                "di-no-local-connection-factory-is-refused",
                "ローカルの Repository（接続ファクトリ）をそのキーで登録する前の AddGeneratedSyncEngine は、キーを名指しした"
                    + " InvalidOperationException で断る（別のキーの登録は数えない）",
                NoLocalConnectionFactoryIsRefusedAsync,
                checksFile: false
            ),
            Row(
                "di-connection-factory-registered-after-sync-engine-is-refused",
                "AddGeneratedSyncEngine の後で同じキー（keyed とキーなしの両方）の接続ファクトリを登録し直したコンテナからエンジンを"
                    + "引くと、名指しの InvalidOperationException で断る",
                FactoryRegisteredAfterSyncEngineIsRefusedAsync,
                checksFile: false
            ),
            Row(
                "di-tables-created-on-first-connection-are-recorded-from-next-connection",
                "業務の表が 1 つも無い DB では、包んだ接続ファクトリの最初の接続で状態の表だけができ、同じ接続で作った表へ書いた行は"
                    + "記録されず、次に開く接続からはトリガーが在って書き込みが記録される",
                TablesCreatedOnFirstConnectionAsync,
                external: DropBusinessTablesAsync
            ),
            Row(
                "di-excluded-table-rows-referring-to-scope-refuse-refresh",
                "sync_notes を除外した登録で、ローカルの sync_notes の行が sync_orders の行を参照していると、DI から引いたエンジンの"
                    + "洗い替えは SyncRefreshScopeException（sync_notes → sync_orders）で断り、行が残る",
                ExcludedRowsReferringToScopeRefuseRefreshAsync,
                WithoutNotes
            ),
            Row(
                "di-sync-through-registered-engine-uploads-and-downloads",
                "DI から引いたエンジンの同期で、ローカルの新しい行が上がりサーバーの新しい行が降りる。除外した sync_notes のサーバーの"
                    + "行は降りない。行を飛ばすローカルの Save フックを登録しても、降りた行は入りフックは呼ばれない",
                SyncThroughRegisteredEngineAsync,
                WithoutNotes
            ),
        ];

    /// <summary>DI の配線の行を 1 つ作る（行のガードは使わない＝回数は 0・Repository の書き込みと速い道の表明は掛けない）</summary>
    /// <param name="id">行の Id</param>
    /// <param name="description">行の説明</param>
    /// <param name="open">登録を組んで表明する手</param>
    /// <param name="guardTables">DB のファイルの共通の表明でトリガーを見る表（null なら同期する表の全部）</param>
    /// <param name="checksFile">DB のファイルに共通の表明を掛けるか（接続を開かずに断る行は false）</param>
    /// <param name="external">登録を組む前に流す外からの変更（null なら何もしない）</param>
    private static ScenarioRow<SyncTriggerGuardCase> Row(
        string id,
        string description,
        Func<SyncTriggerGuardContext, CancellationToken, Task> open,
        IReadOnlyList<string>? guardTables = null,
        bool checksFile = true,
        Func<SyncTriggerGuardContext, CancellationToken, Task>? external = null
    ) =>
        new(
            id,
            null,
            description,
            new SyncTriggerGuardCase(
                SyncTriggerFixture.Sync,
                Prepared: false,
                External: external,
                new SyncTriggerGuardCounts(0, 0),
                WriteRecorded: false,
                FastPathAfter: false,
                Open: open,
                ChecksFile: checksFile,
                GuardTables: guardTables
            )
        );

    /// <summary>スコープを検査するプロバイダを作る</summary>
    private static ServiceProvider BuildProvider(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    /// <summary>行の状態の表に、表とキーの送る行がちょうど <paramref name="expected"/> 行あることを表明する</summary>
    private static async Task ShouldBeRecordedAsync(
        SyncTriggerGuardContext ctx,
        string table,
        long key,
        long expected,
        string because,
        CancellationToken ct
    ) => (await ctx.Harness.CountRecordedAsync(table, key, ct)).Should().Be(expected, because);

    /// <summary>表のトリガーのうち、前置きで始まるものを包んでいない外の接続で全部落とす（次に開く包んだ接続が張り直す）</summary>
    private static async Task DropTriggersOutsideAsync(
        SyncTriggerGuardContext ctx,
        string table,
        CancellationToken ct
    )
    {
        foreach (var name in (await ctx.Harness.ReadTriggerTextsAsync(table, ct)).Keys)
        {
            await ctx.Harness.ExecuteOutsideAsync($"DROP TRIGGER \"{name}\"", ct);
        }
    }

    /// <summary>表のトリガーの名前が、<c>TriggersFor</c> の名前と一致する（<paramref name="present"/> が偽なら 1 本も無い）ことを表明する</summary>
    private static async Task ShouldHaveTriggersAsync(
        SyncTriggerGuardContext ctx,
        string table,
        bool present,
        string because,
        CancellationToken ct
    )
    {
        var expected = present
            ? ctx.Harness.ExpectedTriggerNames(table).Order(StringComparer.Ordinal).ToList()
            : [];

        (await ctx.Harness.ReadTriggerNamesAsync(table, ct))
            .Order(StringComparer.Ordinal)
            .Should()
            .Equal(expected, $"{because}（表 {table}）");
    }

    private static async Task KeyedRegistrationRecordsWritesAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        using var serverDb = SqliteTempDatabase.Create();
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(
            ServerKey,
            serverDb.ReadWriteCreateConnectionString
        );
        services.AddGeneratedSqliteRepositories(LocalKey, ctx.Harness.ConnectionString);
        services.AddGeneratedSyncEngine(LocalKey);
        await using var provider = BuildProvider(services);

        provider
            .GetRequiredKeyedService<ISqlConnectionFactory>(LocalKey)
            .Should()
            .BeOfType<ObservedSqlConnectionFactory>(
                "ローカルの keyed の接続ファクトリは確認つきの包み"
            );
        provider
            .GetRequiredKeyedService<ISqlConnectionFactory>(ServerKey)
            .Should()
            .NotBeOfType<ObservedSqlConnectionFactory>("別のキーの登録は包まない");

        await using var scope = provider.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(LocalKey)
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "repository" }, ct);

        // Repository の接続が張ったトリガーを落とす。生 SQL の接続も包みを通るなら、開いたときに張り直してから書く
        await DropTriggersOutsideAsync(ctx, "sync_orders", ct);
        await scope
            .ServiceProvider.GetRequiredKeyedService<ISqlExecutor>(LocalKey)
            .ExecuteSqlAsync(
                "INSERT INTO sync_orders (order_id, customer_name) VALUES (2, 'raw sql')",
                null,
                ct
            );

        await ShouldBeRecordedAsync(ctx, "sync_orders", 1, 1, "keyed の Repository の書き込み", ct);
        await ShouldBeRecordedAsync(ctx, "sync_orders", 2, 1, "keyed の生 SQL の書き込み", ct);
    }

    private static async Task UnkeyedRegistrationRecordsWritesAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(ctx.Harness.ConnectionString);
        services.AddGeneratedSyncEngine(null);
        await using var provider = BuildProvider(services);

        provider
            .GetRequiredService<ISqlConnectionFactory>()
            .Should()
            .BeOfType<ObservedSqlConnectionFactory>("キーなしの接続ファクトリは確認つきの包み");

        await using var scope = provider.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<ISyncOrderRepository>()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "repository" }, ct);

        // Repository の接続が張ったトリガーを落とす。生 SQL の接続も包みを通るなら、開いたときに張り直してから書く
        await DropTriggersOutsideAsync(ctx, "sync_orders", ct);
        await scope
            .ServiceProvider.GetRequiredService<ISqlExecutor>()
            .ExecuteSqlAsync(
                "INSERT INTO sync_orders (order_id, customer_name) VALUES (2, 'raw sql')",
                null,
                ct
            );

        await ShouldBeRecordedAsync(
            ctx,
            "sync_orders",
            1,
            1,
            "キーなしの Repository の書き込み",
            ct
        );
        await ShouldBeRecordedAsync(ctx, "sync_orders", 2, 1, "キーなしの生 SQL の書き込み", ct);
    }

    private static async Task InstanceRegisteredFactoryIsWrappedAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        // Repository の登録は別の DB を指し、後から登録し直したインスタンスが端末の DB を指す
        using var otherDb = SqliteTempDatabase.Create();
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(otherDb.ReadWriteCreateConnectionString);
        services.AddSingleton<ISqlConnectionFactory>(
            new SqlConnectionFactory(ctx.Harness.ConnectionString)
        );
        services.AddGeneratedSyncEngine(null);
        await using var provider = BuildProvider(services);

        ISqlConnectionFactory first;
        ISqlConnectionFactory second;

        await using (var scope = provider.CreateAsyncScope())
        {
            first = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();
            await scope
                .ServiceProvider.GetRequiredService<ISyncOrderRepository>()
                .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "instance" }, ct);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            second = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();
        }

        first
            .Should()
            .BeOfType<ObservedSqlConnectionFactory>("インスタンスの登録も確認つきの包みになる");
        second.Should().BeSameAs(first, "包みは元の登録の寿命（シングルトン）のまま");
        await ShouldBeRecordedAsync(
            ctx,
            "sync_orders",
            1,
            1,
            "包みはインスタンスを内側に使い、その DB への書き込みが記録される",
            ct
        );
        System
            .IO.File.Exists(otherDb.FilePath)
            .Should()
            .BeFalse("置き換えられた登録の DB は開かれない");
    }

    private static async Task ExcludedTableGetsNoTriggerAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(LocalKey, ctx.Harness.ConnectionString);
        services.AddGeneratedSyncEngine(LocalKey, excludeFromSync: [typeof(SyncNoteEntity)]);
        await using var provider = BuildProvider(services);

        await using (
            var connection = provider
                .GetRequiredKeyedService<ISqlConnectionFactory>(LocalKey)
                .CreateConnection()
        )
        {
            await connection.OpenAsync(ct);
        }

        await ShouldHaveTriggersAsync(
            ctx,
            "sync_notes",
            present: false,
            "除外した表にトリガーは無い",
            ct
        );

        foreach (var table in WithoutNotes)
        {
            await ShouldHaveTriggersAsync(
                ctx,
                table,
                present: true,
                "同期する表にトリガーが在る",
                ct
            );
        }

        await using var scope = provider.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(LocalKey)
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "a" }, ct);
        await scope
            .ServiceProvider.GetRequiredKeyedService<ISyncNoteRepository>(LocalKey)
            .InsertAsync(
                new SyncNoteEntity
                {
                    NoteId = 101,
                    OrderId = 1,
                    Body = "local only",
                },
                ct
            );

        await ShouldBeRecordedAsync(
            ctx,
            "sync_orders",
            1,
            1,
            "同期する表の書き込みは記録される",
            ct
        );
        await ShouldBeRecordedAsync(
            ctx,
            "sync_notes",
            101,
            0,
            "除外した表の書き込みは記録されない",
            ct
        );
    }

    private static Task UnknownExclusionIsRefusedAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(LocalKey, ctx.Harness.ConnectionString);

        var act = () =>
            services.AddGeneratedSyncEngine(LocalKey, excludeFromSync: [typeof(string)]);

        act.Should().Throw<ArgumentException>().WithMessage("*not synchronised*");

        return Task.CompletedTask;
    }

    private static Task ExcludingEverythingIsRefusedAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(LocalKey, ctx.Harness.ConnectionString);

        var act = () =>
            services.AddGeneratedSyncEngine(
                LocalKey,
                excludeFromSync:
                [
                    typeof(SyncOrderEntity),
                    typeof(SyncOrderLineEntity),
                    typeof(SyncNoteEntity),
                    typeof(SyncMarkerEntity),
                ]
            );

        act.Should().Throw<ArgumentException>().WithMessage("*every synchronised table*");

        return Task.CompletedTask;
    }

    private static Task NoLocalConnectionFactoryIsRefusedAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        // 同じ方言の接続ファクトリが別のキーで登録されていても、ローカルのキーの登録としては数えない
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(ServerKey, ctx.Harness.ConnectionString);

        var act = () => services.AddGeneratedSyncEngine(LocalKey);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "No registration of ISqlConnectionFactory was found for the service key 'local'.*"
                    + "Register the local repositories before calling AddGeneratedSyncEngine*"
            );

        return Task.CompletedTask;
    }

    private static async Task FactoryRegisteredAfterSyncEngineIsRefusedAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        var keyed = new ServiceCollection();
        keyed.AddGeneratedSqliteRepositories(LocalKey, ctx.Harness.ConnectionString);
        keyed.AddGeneratedSyncEngine(LocalKey);
        keyed.AddKeyedSingleton<ISqlConnectionFactory>(
            LocalKey,
            new SqlConnectionFactory(ctx.Harness.ConnectionString)
        );

        await ShouldRefuseEngineAsync(keyed, "'local'", "keyed の登録し直し");

        var unkeyed = new ServiceCollection();
        unkeyed.AddGeneratedSqliteRepositories(ctx.Harness.ConnectionString);
        unkeyed.AddGeneratedSyncEngine(null);
        unkeyed.AddSingleton<ISqlConnectionFactory>(
            new SqlConnectionFactory(ctx.Harness.ConnectionString)
        );

        await ShouldRefuseEngineAsync(unkeyed, "''", "キーなしの登録し直し");
    }

    /// <summary>コンテナからエンジンを引くと、キーを名指しした InvalidOperationException で断ることを表明する</summary>
    private static async Task ShouldRefuseEngineAsync(
        IServiceCollection services,
        string quotedKey,
        string because
    )
    {
        await using var provider = BuildProvider(services);
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<SyncEngine>();

        act.Should()
            .Throw<InvalidOperationException>(because)
            .WithMessage(
                $"The local connection factory for the service key {quotedKey} is not the one AddGeneratedSyncEngine wrapped*"
            );
    }

    /// <summary>業務の表を全部落とす外からの変更（状態の表もトリガーもまだ無い DB）</summary>
    private static async Task DropBusinessTablesAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        foreach (var table in ctx.Harness.AllTableNames.Reverse())
        {
            await ctx.Harness.ExecuteOutsideAsync($"DROP TABLE \"{table}\"", ct);
        }
    }

    private static async Task TablesCreatedOnFirstConnectionAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(ctx.Harness.ConnectionString);
        services.AddGeneratedSyncEngine(null);
        await using var provider = BuildProvider(services);
        var factory = provider.GetRequiredService<ISqlConnectionFactory>();

        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync(ct);

            (await ctx.Harness.ReadStateAsync(ct))
                .DbId.Should()
                .NotBeNullOrWhiteSpace("最初の接続で状態の表ができる");
            (await CountTriggersAsync(ctx, ct)).Should().Be(0, "業務の表が無いのでトリガーは無い");

            // 同じ接続で業務の表を作り、行を書く（この接続の確認は開いたときに済んでいる）
            await using var command = connection.CreateCommand();
            command.CommandText =
                ctx.Harness.Ddl
                + ";\nINSERT INTO sync_orders (order_id, customer_name) VALUES (1, 'same connection');";
            await command.ExecuteNonQueryAsync(ct);
        }

        (await CountTriggersAsync(ctx, ct)).Should().Be(0, "表を作った接続ではトリガーを張らない");
        await ShouldBeRecordedAsync(
            ctx,
            "sync_orders",
            1,
            0,
            "表を作った接続で書いた行は記録されない",
            ct
        );

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<ISyncOrderRepository>()
                .InsertAsync(
                    new SyncOrderEntity { OrderId = 2, CustomerName = "next connection" },
                    ct
                );
        }

        foreach (var table in ctx.Harness.AllTableNames)
        {
            await ShouldHaveTriggersAsync(
                ctx,
                table,
                present: true,
                "次に開いた接続がトリガーを張る",
                ct
            );
        }

        await ShouldBeRecordedAsync(
            ctx,
            "sync_orders",
            2,
            1,
            "次に開いた接続の書き込みは記録される",
            ct
        );
    }

    /// <summary>DB のトリガーの数</summary>
    private static async Task<long> CountTriggersAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    ) =>
        Convert.ToInt64(
            await ctx.Harness.ScalarOutsideAsync(
                "SELECT count(*) FROM sqlite_master WHERE type = 'trigger'",
                ct
            ),
            System.Globalization.CultureInfo.InvariantCulture
        );

    /// <summary>
    /// ローカルの keyed の登録に、sync_notes を除外した <c>AddGeneratedSyncEngine</c> と、テストのサーバー役の差分ソース
    /// （スコープの登録）を足す
    /// </summary>
    private static ServiceCollection ExcludingNotesWithServer(
        SyncTriggerGuardContext ctx,
        SyncFixtureTestServer server
    )
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(LocalKey, ctx.Harness.ConnectionString);
        services.AddGeneratedSyncEngine(LocalKey, excludeFromSync: [typeof(SyncNoteEntity)]);
        services.AddScoped(_ => server.Sources.Orders);
        services.AddScoped(_ => server.Sources.Markers);
        services.AddScoped(_ => server.Sources.Lines);
        services.AddScoped(_ => server.Sources.Notes);

        return services;
    }

    private static async Task ExcludedRowsReferringToScopeRefuseRefreshAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        using var server = await SyncFixtureTestServer.CreateAsync(ct);
        await using var provider = BuildProvider(ExcludingNotesWithServer(ctx, server));
        await using var scope = provider.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(LocalKey);
        var notes = scope.ServiceProvider.GetRequiredKeyedService<ISyncNoteRepository>(LocalKey);

        await orders.InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "a" }, ct);
        await notes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 101,
                OrderId = 1,
                Body = "local only",
            },
            ct
        );

        var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();
        var act = async () =>
            await engine.RefreshAsync(
                new SyncRefreshOptions { DiscardLocalUnboundedBinaries = true },
                ct
            );

        var refused = (await act.Should().ThrowAsync<SyncRefreshScopeException>()).Which;
        refused
            .References.Should()
            .Equal(new SyncRefreshScopeReference("sync_notes", "sync_orders"));
        (await orders.GetByIdAsync(1, ct)).Should().NotBeNull("断った洗い替えは行を消さない");
        (await notes.GetByIdAsync(101, ct)).Should().NotBeNull("除外した表の行も残る");
    }

    private static async Task SyncThroughRegisteredEngineAsync(
        SyncTriggerGuardContext ctx,
        CancellationToken ct
    )
    {
        using var server = await SyncFixtureTestServer.CreateAsync(ct);
        var hook = new SkipEveryOrderHook();
        var services = ExcludingNotesWithServer(ctx, server);
        services.AddSaveHook(hook);
        await using var provider = BuildProvider(services);

        // サーバーの新しい注文と、除外した表のメモ
        await server.Orders.InsertAsync(
            new SyncOrderEntity { OrderId = 2, CustomerName = "server" },
            ct
        );
        await server.Sql.ExecuteSqlAsync(
            "INSERT INTO sync_notes (note_id, order_id, body) VALUES (201, 2, 'server note')",
            null,
            ct
        );

        await using var scope = provider.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(LocalKey);
        var notes = scope.ServiceProvider.GetRequiredKeyedService<ISyncNoteRepository>(LocalKey);

        // 空振りでないことの確かめ：端末のリポジトリのグラフ保存ではフックが呼ばれ、行を飛ばす
        var skipped = new SyncOrderEntity { OrderId = 9, CustomerName = "skipped" };
        skipped.MarkAdded();
        await orders.SaveAsync(skipped, cancellationToken: ct);
        hook.Calls.Should().Be(1, "端末のグラフ保存ではフックが呼ばれる");
        (await orders.GetByIdAsync(9, ct)).Should().BeNull("フックが飛ばした行は書かれない");

        await orders.InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "local" }, ct);

        // 版なしの表（sync_notes）は後勝ちの実行でだけ同期されるので、除外が効くことを見るために後勝ちで流す
        var result = await scope
            .ServiceProvider.GetRequiredService<SyncEngine>()
            .SyncAsync(new SyncOptions { Mode = SyncMode.LastWriteWins }, ct);

        result.Conflicts.Should().BeEmpty();
        result.Truncations.Should().BeEmpty();
        (await server.Orders.GetByIdAsync(1, ct)).Should().NotBeNull("ローカルの新しい行が上がる");
        (await orders.GetByIdAsync(2, ct))
            .Should()
            .NotBeNull("サーバーの新しい行はフックに飛ばされずに降りる");
        (await notes.GetByIdAsync(201, ct)).Should().BeNull("除外した表のサーバーの行は降りない");
        hook.Calls.Should().Be(1, "適用はローカルの Save フックを呼ばない");
        await ShouldBeRecordedAsync(ctx, "sync_orders", 1, 0, "上げた行の未送信は外れる", ct);
    }

    /// <summary>注文の保存をすべて Before で飛ばし、呼ばれた回数を数える Save フック</summary>
    private sealed class SkipEveryOrderHook : ISaveHook<SyncOrderEntity>
    {
        private int _calls;

        /// <summary>呼ばれた回数</summary>
        public int Calls => Volatile.Read(ref _calls);

        public Task<bool> BeforeSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(false);
        }
    }
}
