using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 構築時除外（<c>AddGeneratedSyncEngine</c> の <c>excludeFromSync</c>）＝「このテーブルは同期に参加しない」
/// というローカル専用宣言を検証する。
/// </summary>
/// <remarks>
/// ラン単位の除外（<c>SyncOptions.ExcludedEntityTypes</c>）との違いは高度＝構築時に除外したテーブルは
/// ジャーナル記録デコレータで包まれず（記録ゼロ・書き込みの追加コストもゼロ）、記述子も登録されない
/// （どのランのダウンロード・削除伝搬・洗い替えにも入らない）。記録だけを止めて同期対象に残すと、
/// 削除伝搬が保護エントリを持たないローカル専用データを消すため、除外は必ずこの 2 点セットで効く。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncConstructionExclusionTests : IDisposable
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

    /// <summary>サーバー・ローカルの 2 キー登録だけを済ませたコレクションを作る</summary>
    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqliteRepositories(ServerKey, _server.ReadWriteCreateConnectionString);
        services.AddGeneratedSqliteRepositories(LocalKey, _local.ReadWriteCreateConnectionString);

        return services;
    }

    /// <summary>構築時除外はデコレータも記述子も登録しない（ローカル専用宣言の 2 点セット）</summary>
    [Fact(
        DisplayName = "[Sync/除外] 構築時除外はデコレータも記述子も登録しない（ローカル専用宣言）"
    )]
    public async Task ExcludeFromSync_SkipsDecoratorAndDescriptor()
    {
        var services = CreateServices();
        services.AddGeneratedSyncSupport(
            ServerKey,
            LocalKey,
            excludeFromSync: [typeof(SyncNoteEntity)]
        );
        await using var provider = services.BuildServiceProvider();

        // 除外テーブル: 素のリポジトリのまま（記録ゼロ・追加コストゼロ）
        provider
            .GetRequiredKeyedService<ISyncNoteRepository>(LocalKey)
            .Should()
            .BeOfType<QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SyncNoteRepository>();

        // 非除外テーブル: ジャーナル記録デコレータ
        provider
            .GetRequiredKeyedService<ISyncOrderRepository>(LocalKey)
            .Should()
            .BeOfType<JournalingSyncOrderRepository>();

        // 記述子: 除外テーブルはエンジンの対象から外れる（どのランにも入らない）
        await using var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetServices<ISyncTable>()
            .Select(table => table.TableName)
            .Should()
            .BeEquivalentTo(["sync_orders", "sync_order_lines", "sync_markers"]);
    }

    /// <summary>実際に書き込んでも除外テーブルの書き込みはジャーナルへ記録されない</summary>
    [Fact(DisplayName = "[Sync/除外] 実際に書き込んでも除外テーブルはジャーナルへ記録されない")]
    public async Task ExcludeFromSync_WritesAreNotJournaled()
    {
        await _local.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var services = CreateServices();
        services.AddGeneratedSyncSupport(
            ServerKey,
            LocalKey,
            excludeFromSync: [typeof(SyncNoteEntity)]
        );
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var orders = scope.ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(LocalKey);
        var notes = scope.ServiceProvider.GetRequiredKeyedService<ISyncNoteRepository>(LocalKey);
        await orders.InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "a" }, Ct);
        await notes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 101,
                OrderId = 1,
                Body = "local only",
            },
            Ct
        );

        var journal = scope.ServiceProvider.GetRequiredService<SyncJournal>();
        var entries = await journal.ReadAllAsync(Ct);

        entries.Should().ContainSingle(entry => entry.TableName == "sync_orders");
        entries.Should().NotContain(entry => entry.TableName == "sync_notes");
    }

    /// <summary>同期対象でない型の除外指定は登録時に拒否される（黙殺すると「効いたつもり」になるため）</summary>
    [Fact(DisplayName = "[Sync/除外] 同期対象でない型の除外指定は登録時に ArgumentException")]
    public void ExcludeFromSync_UnknownType_IsRejectedAtRegistration()
    {
        var services = CreateServices();

        var act = () =>
            services.AddGeneratedSyncEngine(LocalKey, excludeFromSync: [typeof(string)]);

        act.Should().Throw<ArgumentException>().WithMessage("*not synchronised*");
    }

    /// <summary>全テーブルの除外は「同期する物が無い」構成ミスとして登録時に拒否される</summary>
    [Fact(DisplayName = "[Sync/除外] 全テーブルの除外は登録時に拒否される")]
    public void ExcludeFromSync_ExcludingEverything_IsRejected()
    {
        var services = CreateServices();

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
    }

    /// <summary>
    /// 構築時に除外した表（ローカル専用の表）の行が同期対象の親を参照していると、洗い替えは何も消す前に断ることを検証する
    /// （除外した表はどのランの範囲にも入らないので、参照する行を取り除くまで洗い替えは通らない）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/除外] 構築時に除外した表の行が親を参照していると、洗い替えは何も消す前に断る"
    )]
    public async Task ExcludeFromSync_ReferringLocalRows_RefuseRefresh()
    {
        await _server.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);
        await _local.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var services = CreateServices();
        services.AddGeneratedSyncSupport(
            ServerKey,
            LocalKey,
            excludeFromSync: [typeof(SyncNoteEntity)]
        );
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var localSql = scope.ServiceProvider.GetRequiredKeyedService<ISqlExecutor>(LocalKey);
        var localOrders = scope.ServiceProvider.GetRequiredKeyedService<ISyncOrderRepository>(
            LocalKey
        );
        var localNotes = scope.ServiceProvider.GetRequiredKeyedService<ISyncNoteRepository>(
            LocalKey
        );
        var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();

        // 降りてきた行の代わりに、ジャーナルを通さずにローカルへ直接置く（サーバー役の SQLite は生成された直結の
        // 差分ソースの SQL Server の関数を持たないので、ここでは同期しない＝断るのはサーバーへ何も問い合わせる前）
        await localSql.ExecuteSqlAsync(
            "INSERT INTO sync_orders (order_id, customer_name) VALUES (1, 'a')",
            null,
            Ct
        );
        await localNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 101,
                OrderId = 1,
                Body = "local only",
            },
            Ct
        );

        var act = async () =>
            await engine.RefreshAsync(
                new SyncRefreshOptions { DiscardLocalUnboundedBinaries = true },
                Ct
            );

        var refused = (await act.Should().ThrowAsync<SyncRefreshScopeException>()).Which;
        refused
            .References.Should()
            .Equal(new SyncRefreshScopeReference("sync_notes", "sync_orders"));
        (await localOrders.GetByIdAsync(1, Ct)).Should().NotBeNull();
        (await localNotes.GetByIdAsync(101, Ct)).Should().NotBeNull();
    }
}
