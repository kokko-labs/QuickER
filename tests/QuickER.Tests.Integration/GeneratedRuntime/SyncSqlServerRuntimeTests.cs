using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedSyncFixture.Repositories.SqlServer;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成した同期支援を、実 SQL Server（サーバー）と実ファイル SQLite（ローカル）の間で動かす統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// ここに置くのは、SyncFixture にしか無い表（NULL を許す一意制約の <c>sync_markers</c>）を使うもの。本物の <c>rowversion</c> との噛み合わせ（DB が採番する版・<c>MIN_ACTIVE_ROWVERSION()</c> による上限・版ガードの
/// 競合・外部キーエラーの分類・版なしの表の TOP のページング）は、同期のモデルの決定的なシナリオのうち実 SQL Server の転送でも
/// 流すもの（<see cref="SyncModelSqlServerRuntimeTests"/>）が期待の手で表明し、DI 登録糖衣 <c>AddGeneratedSyncSupport</c> の
/// 組み上がりは同期の構成の行列（<see cref="SyncConfigMatrixRuntimeTests"/>）が見る。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlServer)]
public sealed class SyncSqlServerRuntimeTests(SqlServerContainerFixture fixture) : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture = fixture;
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();
    private ServiceProvider _provider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバー側キー（SQL Server）</summary>
    private const string ServerKey = "server";

    /// <summary>ローカル側キー（SQLite）</summary>
    private const string LocalKey = "local";

    /// <summary>サーバーは元の図（rowversion）、ローカルは方言変換した図（BLOB）でスキーマを作り DI を組む</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(SyncFixtureDefinition.Build(), Ct);
        await _sqlite.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var services = new ServiceCollection();
        services.AddGeneratedSqlServerRepositories(ServerKey, _fixture.ConnectionString);
        services.AddGeneratedSqliteRepositories(LocalKey, _sqlite.ReadWriteCreateConnectionString);
        services.AddGeneratedDirectSyncSources(ServerKey);
        _provider = services.BuildServiceProvider();
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        _sqlite.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>サーバー側（SQL Server）のマーカーリポジトリ</summary>
    private ISyncMarkerRepository ServerMarkers() =>
        _provider.GetRequiredKeyedService<ISyncMarkerRepository>(ServerKey);

    /// <summary>
    /// サーバーに NULL の値の行があるとき、端末で作った 2 行目の NULL の行は、Collect・LocalWins では
    /// UniqueValueTakenOnServer として報告されて未送信が残り、ServerWins では決着する（どのポリシー・モードでも止まらない）。
    /// </summary>
    /// <remarks>
    /// SQL Server の UNIQUE 制約は NULL を値として比べ、同じ組の 2 行目の NULL を断る（エラー 2627）。サーバーの事前チェック
    /// （QuickER 版 Repository の <c>CheckUniquenessAsync</c>）が NULL を照合から外していると、エンジンは断られた書き込みの
    /// 理由を見つけられずに元の例外を投げ直し、同期が毎回止まる。単一列の制約と、NULL を含む複合の制約の両方で確かめる。
    /// 端末は新しいエンジン（<see cref="SyncFixtureEngineDevice"/>・トリガーで記録する）で、サーバーの差分ソースは DI に登録した
    /// 生成の直結の差分ソース。
    /// </remarks>
    [Theory(
        DisplayName = "[Sync/SqlServer] NULL の一意な値がサーバーで埋まっていると、どのポリシーとモードでも同期は止まらない"
    )]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.LocalWins, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.LocalWins, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.Versioned, true)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.LastWriteWins, true)]
    public async Task NullUniqueValueTakenOnServer_IsReportedOrSettled(
        SyncConflictPolicy policy,
        SyncMode mode,
        bool composite
    )
    {
        using var device = await SyncFixtureEngineDevice.CreateAsync(
            SyncFixtureServerSources.Resolve(_provider),
            Ct
        );
        await ServerMarkers().InsertAsync(NewMarker(1, composite), Ct);
        var options = new SyncOptions { ConflictPolicy = policy, Mode = mode };
        await device.Engine.SyncAsync(options, Ct);
        await device.Markers.InsertAsync(NewMarker(2, composite), Ct);

        var result = await device.Engine.SyncAsync(options, Ct);

        if (policy == SyncConflictPolicy.ServerWins)
        {
            result.Conflicts.Should().BeEmpty();
            (await device.ReadUnsentAsync(Ct)).Should().BeEmpty();
            (await device.Markers.GetAllAsync(Ct))
                .Select(marker => marker.MarkerId)
                .Should()
                .Equal(1);
        }
        else
        {
            result
                .Conflicts.Should()
                .ContainSingle()
                .Which.Should()
                .Match<SyncConflict>(conflict =>
                    conflict.TableName == "sync_markers"
                    && conflict.KeyText == "2"
                    && conflict.Reason == SyncConflictReason.UniqueValueTakenOnServer
                );
            (await device.ReadUnsentAsync(Ct))
                .Should()
                .ContainSingle(row => row.Table == "sync_markers" && row.KeyText == "2");
            (await device.Markers.GetByIdAsync(2, Ct)).Should().NotBeNull();
        }

        (await ServerMarkers().GetAllAsync(Ct)).Select(marker => marker.MarkerId).Should().Equal(1);
    }

    /// <summary>
    /// SQL Server の QuickER 版 Repository の事前チェックは、UNIQUE 制約と同じく NULL を値として比べる
    /// （単一列の NULL・複合の制約の NULL を含む組・全部が NULL の組は重複、NULL 以外の値が違えば重複ではない）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] SQL Server の CheckUniquenessAsync は NULL を値として比べる（単一列・複合）"
    )]
    public async Task ServerCheckUniqueness_ComparesNullsAsValues()
    {
        await ServerMarkers()
            .InsertAsync(
                new SyncMarkerEntity
                {
                    MarkerId = 1,
                    GroupName = null,
                    Slot = 1,
                },
                Ct
            );
        await ServerMarkers()
            .InsertAsync(
                new SyncMarkerEntity
                {
                    MarkerId = 2,
                    Code = "a",
                    GroupName = "g",
                },
                Ct
            );

        async Task<string[]> Check(SyncMarkerEntity marker) =>
            (await ServerMarkers().CheckUniquenessAsync(marker, Ct))
                .Select(violation => violation.ConstraintName)
                .ToArray();

        // 行 1 の code は NULL＝NULL の code は重複・(NULL, 1) も重複
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = null,
                    GroupName = "x",
                    Slot = 9,
                }
            )
        )
            .Should()
            .Equal("UQ_sync_markers_code");
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = null,
                    Slot = 1,
                }
            )
        ).Should().Equal("UQ_sync_markers_group_name_slot");

        // 行 2 の (g, NULL)＝NULL を含む組でも NULL 以外の列が一致すれば重複・違えば重複ではない
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = "g",
                    Slot = null,
                }
            )
        )
            .Should()
            .Equal("UQ_sync_markers_group_name_slot");
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = null,
                    Slot = 2,
                }
            )
        ).Should().BeEmpty();

        // 自分自身は除外する（行 1 を同じ値で確かめても重複にならない）
        (await Check((await ServerMarkers().GetByIdAsync(1, Ct))!))
            .Should()
            .BeEmpty();
    }

    /// <summary>マーカーを組み立てる（composite なら複合の制約だけがぶつかる値、そうでなければ code だけが NULL でぶつかる値）</summary>
    private static SyncMarkerEntity NewMarker(int id, bool composite) =>
        composite
            ? new SyncMarkerEntity
            {
                MarkerId = id,
                Code = $"c{id}",
                GroupName = null,
                Slot = 1,
            }
            : new SyncMarkerEntity
            {
                MarkerId = id,
                Code = null,
                GroupName = $"g{id}",
                Slot = id,
            };
}
