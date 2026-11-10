using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 削除する行の代替キーを取る保存（EF Core は 2 つめの文脈で後回しの行を書く）を、DbContext のファクトリの作り方ごとに 2 回続ける
/// （SQL Server の系統）。2 つめの文脈は共有の接続へ差し替えてから戻すので、戻した接続でその文脈・ほかの文脈がその後も使えることを、
/// QuickER 版（ファクトリを持たない）と比べる。
/// </summary>
/// <remarks>
/// ファクトリの作り方は <see cref="HeldBackFactory"/> の 3 つ。値オブジェクトありの生成コードは OnConfiguring を持つので EF Core の
/// 本物のプールを断る。そこで、接続文字列の値は、破棄された文脈を次の貸し出しで同じインスタンスのまま返すファクトリ
/// （プールの観測できる振る舞い）で確かめる。
/// </remarks>
public sealed partial class BackendParitySqlServerRuntimeTests
{
    /// <summary>EF Core の DbContext のファクトリの作り方</summary>
    private enum HeldBackFactory
    {
        /// <summary>接続文字列で構成した文脈を、破棄の後に同じインスタンスのまま貸し出し直す（プールの形）</summary>
        ConnectionStringReused,

        /// <summary>接続なしで構成した文脈に、利用者が持つ 2 本の接続を実行時に交互に付ける（<c>SetDbConnection</c>）</summary>
        RuntimeAttachedConnections,

        /// <summary>利用者が持つ 2 本の接続を、オプションに交互に渡す（<c>contextOwnsConnection: false</c>）。今も通る対照</summary>
        PassedConnections,
    }

    /// <summary>ファクトリの作り方の各値のケースの名前</summary>
    private static IEnumerable<string> HeldBackFactoryCaseNames =>
        Enum.GetValues<HeldBackFactory>().Select(HeldBackFactoryCaseName);

    private static string HeldBackFactoryCaseName(HeldBackFactory factory) =>
        factory switch
        {
            HeldBackFactory.ConnectionStringReused =>
                "alternate-key-taken-from-deleted-twice-connection-string-reused",
            HeldBackFactory.RuntimeAttachedConnections =>
                "alternate-key-taken-from-deleted-twice-runtime-attached-connections",
            HeldBackFactory.PassedConnections =>
                "alternate-key-taken-from-deleted-twice-passed-connections",
            _ => throw new ArgumentOutOfRangeException(nameof(factory)),
        };

    private static HeldBackFactory? FindHeldBackFactory(string name) =>
        Enum.GetValues<HeldBackFactory>()
            .Select(factory => (HeldBackFactory?)factory)
            .FirstOrDefault(factory => HeldBackFactoryCaseName(factory!.Value) == name);

    /// <summary>
    /// kit 1（K1）を消して kit 2 が K1 を取る保存と、kit 3（K3）を消して kit 4 が K3 を取る保存を続け、その間と後に kits を読む
    /// （ファクトリが返す文脈を順に使い、2 つめの文脈だった文脈も読み取りに回る）。結果と保存先を並べる
    /// </summary>
    private async Task<IReadOnlyList<string>> HeldBackTwiceAsync(
        bool efCore,
        HeldBackFactory factory
    )
    {
        await using var connections = new HeldBackConnections(_fixture.ConnectionString, factory);
        var services = new ServiceCollection();

        if (efCore)
        {
            services.AddSingleton<IDbContextFactory<QuickErDbContext>>(connections);
            services.AddGeneratedEfCoreRepositories(builder =>
                builder.UseSqlServer(_fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
            );
        }
        else
        {
            services.AddGeneratedSqlServerRepositories(_fixture.ConnectionString);
        }

        await using var provider = services.BuildServiceProvider();
        var kits = provider.GetRequiredService<IKitRepository>();

        foreach (var id in new[] { 1, 2, 3, 4 })
        {
            await kits.InsertAsync(
                new KitEntity
                {
                    KitId = KitIdValue.Create(id),
                    Code = CodeValue.Create($"K{id}"),
                    Name = NameValue.Create($"k{id}"),
                },
                Ct
            );
        }

        List<string> lines = [];

        foreach (var (deletedId, takerId) in new[] { (1, 2), (3, 4) })
        {
            var deleted = (await kits.GetByIdAsync(KitIdValue.Create(deletedId), Ct))!;
            var taker = (await kits.GetByIdAsync(KitIdValue.Create(takerId), Ct))!;
            deleted.MarkRemoved();
            taker.Code = CodeValue.Create($"K{deletedId}");
            taker.MarkUpdated();
            lines.Add(
                $"save {await OutcomeAsync(() => kits.SaveAsync([taker, deleted], cancellationToken: Ct))}"
            );

            // 続けて 2 回読む（ファクトリが続けて返す文脈＝2 つめの文脈だった文脈も使う）
            for (var read = 0; read < 2; read++)
            {
                lines.Add(
                    $"read {await OutcomeAsync(async () => (await kits.GetAllAsync(Ct)).Count)}"
                );
            }
        }

        lines.AddRange(await KitsStateAsync());
        return lines;
    }

    /// <summary>
    /// <see cref="HeldBackFactory"/> の作り方の DbContext のファクトリと、利用者が持つ接続（テストの終わりに閉じる）
    /// </summary>
    private sealed class HeldBackConnections(string connectionString, HeldBackFactory factory)
        : IDbContextFactory<QuickErDbContext>,
            IAsyncDisposable
    {
        private readonly SqlConnection[] _connections =
        [
            new(connectionString),
            new(connectionString),
        ];

        private readonly ConcurrentQueue<ReusedDbContext> _returned = new();
        private readonly List<ReusedDbContext> _created = [];
        private int _next = -1;

        public QuickErDbContext CreateDbContext()
        {
            var connection = _connections[Interlocked.Increment(ref _next) & 1];

            switch (factory)
            {
                case HeldBackFactory.ConnectionStringReused:
                    // 破棄された順に貸し出し直す（2 つめの文脈だった文脈が、次の読み取りの文脈になる）
                    if (_returned.TryDequeue(out var reused))
                    {
                        return reused;
                    }

                    var created = new ReusedDbContext(
                        new DbContextOptionsBuilder<QuickErDbContext>()
                            .UseSqlServer(connectionString)
                            .IgnoreServiceProviderCacheLimit()
                            .Options,
                        _returned
                    );
                    _created.Add(created);
                    return created;

                case HeldBackFactory.RuntimeAttachedConnections:
                    var attached = new QuickErDbContext(
                        new DbContextOptionsBuilder<QuickErDbContext>()
                            .UseSqlServer()
                            .IgnoreServiceProviderCacheLimit()
                            .Options
                    );
                    attached.Database.SetDbConnection(connection, contextOwnsConnection: false);
                    return attached;

                default:
                    return new QuickErDbContext(
                        new DbContextOptionsBuilder<QuickErDbContext>()
                            .UseSqlServer(connection, contextOwnsConnection: false)
                            .IgnoreServiceProviderCacheLimit()
                            .Options
                    );
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var context in _created)
            {
                await context.DisposeForGoodAsync();
            }

            foreach (var connection in _connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>破棄されると、破棄せずにファクトリの貸し出しの列へ戻る文脈（プールの観測できる振る舞い）</summary>
    private sealed class ReusedDbContext(
        DbContextOptions<QuickErDbContext> options,
        ConcurrentQueue<ReusedDbContext> returned
    ) : QuickErDbContext(options)
    {
        public override ValueTask DisposeAsync()
        {
            ChangeTracker.Clear();
            returned.Enqueue(this);
            return ValueTask.CompletedTask;
        }

        public override void Dispose()
        {
            ChangeTracker.Clear();
            returned.Enqueue(this);
        }

        /// <summary>テストの終わりに本当に破棄する</summary>
        public ValueTask DisposeForGoodAsync() => base.DisposeAsync();
    }
}
