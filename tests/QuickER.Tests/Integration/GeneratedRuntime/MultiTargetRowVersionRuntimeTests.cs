using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture.Repositories.SqlServer;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// サーバー＝SQL Server・ローカル＝SQLite のハイブリッド構成を、同一プロセスの keyed DI で組んで実 DB へ流す。
/// </summary>
/// <remarks>
/// <para>
/// 守るのは 2 点。(1) マルチターゲットにしても SQL Server 側の版ガードは効く
/// （<c>row_ver</c> は DB 採番・INSERT / UPDATE から除外・古い版での更新は <see cref="SaveConflictException"/>）。
/// (2) その版をローカル SQLite の同じ列へ格納して読み戻せる（同期の最小形）。
/// </para>
/// <para>
/// SQL Server は Testcontainers を使うため Docker 不在時はスキップされる。SQLite 単独で足りるミラー列の
/// 読み書きは <see cref="MultiTargetRowVersionSqliteRuntimeTests"/> に分離し CI でも常時回す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MultiTargetRowVersionRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ（サーバー側）</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>ローカル側の一時ファイル SQLite DB</summary>
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();

    /// <summary>両方言を keyed DI で登録した 1 つのコンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバー側キー（SQL Server）</summary>
    private const string ServerKey = "server";

    /// <summary>ローカル側キー（SQLite）</summary>
    private const string LocalKey = "local";

    /// <summary>
    /// サーバー側は元の図（<c>rowversion</c>）、ローカル側は方言変換した図（<c>BLOB</c>・NULL 許容）で
    /// スキーマを作り、1 つの ServiceCollection へ keyed 登録する。
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(MultiTargetRowVersionFixtureDefinition.Build(), Ct);
        await _sqlite.ApplyDdlAsync(MultiTargetRowVersionFixtureDefinition.BuildSqliteMirror(), Ct);

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

    /// <summary>サーバー側（SQL Server）のリポジトリ</summary>
    private ISyncItemRepository Server() =>
        _provider.GetRequiredKeyedService<ISyncItemRepository>(ServerKey);

    /// <summary>ローカル側（SQLite）のリポジトリ</summary>
    private ISyncItemRepository Local() =>
        _provider.GetRequiredKeyedService<ISyncItemRepository>(LocalKey);

    /// <summary>
    /// マルチターゲットでも SQL Server 側は版を DB が採番し、古い版での更新が競合になることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[MultiTarget/RowVersion] SQL Server 側は版を採番し古い版の更新を SaveConflictException で拒否する"
    )]
    public async Task SqlServer_KeepsOptimisticConcurrency()
    {
        var entity = new SyncItemEntity { ItemId = 1, Name = "alpha" };
        await Server().InsertAsync(entity, Ct);

        // INSERT は row_ver を送らず（DB 採番）、採番された版が呼び出し元のエンティティへ書き戻される
        entity.RowVer.Should().NotBeEmpty("DB が採番した版が書き戻される");
        var firstVersion = entity.RowVer;

        // 他者による更新（Repository を経由しないので手元の版は古いまま）
        await Server()
            .ExecuteSqlAsync(
                "UPDATE [sync_items] SET [name] = @n WHERE [item_id] = 1;",
                new { n = "changed-by-other" },
                Ct
            );

        var stale = new SyncItemEntity
        {
            ItemId = 1,
            Name = "alpha-updated",
            RowVer = firstVersion,
        };

        await Assert.ThrowsAsync<SaveConflictException>(() =>
            Server().UpdateAsync(stale, cancellationToken: Ct)
        );
    }

    /// <summary>
    /// サーバーで採番された版を、ローカル SQLite の同じ列へ格納して読み戻せること（同期の最小形）を検証する。
    /// </summary>
    /// <remarks>
    /// これが成立しないと「ローカルの行がどのサーバー版に対応するか」を保持できず、次回同期で送り返す
    /// ガード値が作れない。SQL Server 側で除外されている列を SQLite 側では書けることが要点。
    /// </remarks>
    [Fact(
        DisplayName = "[MultiTarget/RowVersion] サーバーで採番した版をローカル SQLite へミラーできる"
    )]
    public async Task ServerVersion_MirrorsIntoLocalSqlite()
    {
        var onServer = new SyncItemEntity { ItemId = 2, Name = "beta" };
        await Server().InsertAsync(onServer, Ct);
        onServer.RowVer.Should().NotBeEmpty();

        // サーバーから読んだ行（版込み）をそのままローカルへ書き込む
        var fetched = await Server().GetByIdAsync(2, Ct);
        fetched.Should().NotBeNull();
        await Local()
            .InsertAsync(
                new SyncItemEntity
                {
                    ItemId = fetched!.ItemId,
                    Name = fetched.Name,
                    RowVer = fetched.RowVer,
                },
                Ct
            );

        var mirrored = await Local().GetByIdAsync(2, Ct);
        mirrored.Should().NotBeNull();
        mirrored!.RowVer.Should().Equal(fetched.RowVer, "ローカルはサーバー版をそのまま保持する");

        // 保持した版はサーバーへのガード付き更新にそのまま使える（同期の書き戻し方向）
        var pushBack = new SyncItemEntity
        {
            ItemId = mirrored.ItemId,
            Name = "beta-from-local",
            RowVer = mirrored.RowVer,
        };
        var updated = await Server().UpdateAsync(pushBack, cancellationToken: Ct);

        updated.Should().BeTrue("ミラーした版が版ガードを通る");
        pushBack.RowVer.Should().NotEqual(mirrored.RowVer, "更新後は新しい版が書き戻される");

        // 新しい版でローカルを更新できる（SQLite 側は UPDATE でも row_ver を書く）
        var localRow = await Local().GetByIdAsync(2, Ct);
        localRow!.Name = "beta-from-local";
        localRow.RowVer = pushBack.RowVer;
        (await Local().UpdateAsync(localRow, cancellationToken: Ct)).Should().BeTrue();

        (await Local().GetByIdAsync(2, Ct))!.RowVer.Should().Equal(pushBack.RowVer);
    }
}
