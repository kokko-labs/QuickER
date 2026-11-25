using System;
using System.Threading.Tasks;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 決定的なシナリオを、HTTP 越し（SQLite のサーバー役）の小さい塊の端末で流し、1 回で全部の道と同じ結果になることを確かめる
/// （<see cref="SyncModelSmallChunkScenarioRuns"/>）。塊の経路は取得のページを流しながら塊を確定するので、HTTP のページ分けと
/// 再開点の Base64 の往復の上でも同じ答えになるかを見る
/// </summary>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.Remote, Server = RuntimeTarget.AdoSqlite)]
[RuntimeCell(RuntimeFeatures.SyncRefresh, RuntimeTarget.Remote, Server = RuntimeTarget.AdoSqlite)]
public sealed class SyncSupportRemoteAdoSqliteSmallChunkRuntimeTests(ITestOutputHelper output)
{
    [Fact(
        DisplayName = "[Sync/モデル/HTTP] 小さい塊の端末で流した決定的なシナリオは、1 回で全部の道と同じ結果（違う行は理由の宣言どおり）"
    )]
    public Task SmallChunkScenarios_MatchFullRun() =>
        SyncModelSmallChunkScenarioRuns.AssertMatchesDirectAsync(
            SyncModelTransport.Http,
            SyncModelHttpRuntimeTests.Http,
            Math.Clamp(Environment.ProcessorCount, 1, 4),
            output,
            TestContext.Current.CancellationToken
        );
}

/// <summary>
/// 決定的なシナリオを、実 SQL Server のサーバー役の小さい塊の端末で流し、1 回で全部の道と同じ結果になることを確かめる
/// （<see cref="SyncModelSmallChunkScenarioRuns"/>・Docker）。実 SQL Server は取得の上限を実行の始めに固定する
/// （<see cref="ISyncModelServerRole.FixesFetchCeilingAtRunStart"/>）ので、塊の間にサーバーへ書かれた行の降り方が SQLite の代役と
/// 違いうる。その上でも同じ答えになるかを見る
/// </summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerDslFuzzContainerCollection.Name)]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlServer)]
[RuntimeCell(RuntimeFeatures.SyncRefresh, RuntimeTarget.AdoSqlServer)]
public sealed class SyncSupportAdoSqlServerSmallChunkRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
)
{
    [Fact(
        DisplayName = "[Sync/モデル/SqlServer] 小さい塊の端末で流した決定的なシナリオは、1 回で全部の道と同じ結果（違う行は理由の宣言どおり）"
    )]
    public async Task SmallChunkScenarios_MatchFullRun()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await SyncModelSmallChunkScenarioRuns.AssertMatchesDirectAsync(
            SyncModelTransport.SqlServer,
            SyncModelSqlServerRuntimeTests.OptionsFor(fixture),
            Math.Clamp(Environment.ProcessorCount, 1, 4),
            output,
            TestContext.Current.CancellationToken
        );
    }
}
