using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成された DI 登録（<c>AddGeneratedSyncSupport</c>）で組んだ同期の表が、ローカルのリポジトリが走らせる Save フックを
/// 受け取ることを確かめる。フックが適用の行を飛ばすと、表はその行で止まり（例外にならない）、飛ばすのをやめれば降りる。
/// </summary>
/// <remarks>
/// 表を手で組むテスト（<see cref="SyncRuntimeTestsBase"/>）は登録簿を自分で渡すので、DI の登録が登録簿を渡し忘れても
/// 気付けない。渡し忘れると、フックが飛ばした行の後ろも 1 回の保存で書かれ、エンジンは例外にする。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncLocalSaveHookRegistrationTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバー側キー（このテストではどちらも SQLite）</summary>
    private const string ServerKey = "server";

    /// <summary>ローカル側キー</summary>
    private const string LocalKey = "local";

    private readonly SqliteTempDatabase _server = SqliteTempDatabase.Create();
    private readonly SqliteTempDatabase _local = SqliteTempDatabase.Create();

    public void Dispose()
    {
        _server.Dispose();
        _local.Dispose();
    }

    [Fact(
        DisplayName = "[Sync/フック] 生成された DI の登録で組んだ表は Save フックを受け取り、飛ばした行で止まる（例外にならない）"
    )]
    public async Task GeneratedRegistration_PassesLocalSaveHooksToTables()
    {
        await _server.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);
        await _local.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var skip = new SkipOrderHook(2);
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(ServerKey, _server.ReadWriteCreateConnectionString);
        services.AddGeneratedSqliteRepositories(LocalKey, _local.ReadWriteCreateConnectionString);
        services.AddSaveHook(skip);
        services.AddGeneratedSyncSupport(ServerKey, LocalKey);

        // サーバー役は SQLite なので、差分ソースは SQLite の語彙で書いたテスト用のものへ差し替える（後の登録が勝つ）
        services.AddScoped(provider =>
            SyncTestServerSources.CreateOrders(
                provider.GetRequiredKeyedService<ISqlExecutor>(ServerKey),
                provider.GetRequiredKeyedService<ISyncOrderRepository>(ServerKey)
            )
        );
        services.AddScoped(provider =>
            SyncTestServerSources.CreateLines(
                provider.GetRequiredKeyedService<ISqlExecutor>(ServerKey),
                provider.GetRequiredKeyedService<ISyncOrderLineRepository>(ServerKey)
            )
        );
        await using var provider = services.BuildServiceProvider();

        await using (var seed = provider.CreateAsyncScope())
        {
            // サーバー役は SQLite なので、版はここで振る（昇順＝流れの順）
            var server = seed.ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(
                ServerKey
            );

            foreach (var id in new[] { 1, 2, 3 })
            {
                await server.InsertAsync(
                    new SyncOrderEntity
                    {
                        OrderId = id,
                        CustomerName = $"c{id}",
                        RowVer = [0, 0, 0, 0, 0, 0, 0, (byte)id],
                    },
                    Ct
                );
            }
        }

        skip.Skipping = true;

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope
                .ServiceProvider.GetRequiredService<SyncEngine>()
                .SyncAsync(cancellationToken: Ct);

            var stop = result
                .Truncations.Should()
                .ContainSingle(truncation => truncation.TableName == "sync_orders")
                .Subject;
            stop.PendingKeyText.Should().Be("2");
            stop.SkippedBySaveHook.Should().BeTrue();

            var local = scope.ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(
                LocalKey
            );
            (await local.GetByIdAsync(1, Ct)).Should().NotBeNull();
            (await local.GetByIdAsync(2, Ct)).Should().BeNull();
            (await local.GetByIdAsync(3, Ct)).Should().BeNull();
        }

        skip.Skipping = false;

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope
                .ServiceProvider.GetRequiredService<SyncEngine>()
                .SyncAsync(cancellationToken: Ct);

            result.Truncations.Should().BeEmpty();
            var local = scope.ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(
                LocalKey
            );
            (await local.GetAllAsync(Ct)).Select(order => order.OrderId).Should().Equal(1, 2, 3);
        }
    }

    /// <summary>指定した注文の保存を Before で飛ばす Save フック（<see cref="Skipping"/> が false の間は飛ばさない）。</summary>
    private sealed class SkipOrderHook(int orderId) : ISaveHook<SyncOrderEntity>
    {
        public bool Skipping { get; set; }

        public Task<bool> BeforeSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(!(Skipping && entity.OrderId == orderId));
    }
}
