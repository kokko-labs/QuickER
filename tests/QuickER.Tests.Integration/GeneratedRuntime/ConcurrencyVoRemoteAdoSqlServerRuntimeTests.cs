using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを <b>rowversion 列 × 値オブジェクト</b>の図で、実 HTTP のリモート 3 階層へ流す派生のうち、
/// サーバー実体が <b>QuickER 版 Repository（SQL Server 方言）× 実 SQL Server</b>（Testcontainers・Docker 依存）のもの。
/// </summary>
/// <remarks>
/// <para>
/// 実 rowversion（DB が採番した 8 バイト）を VO へ包んだ版が、サーバーの収集（<c>OUTPUT INSERTED</c> で回収した版を VO から開く）→
/// 応答の版対応表→クライアントの書き戻し（素値を VO へ包む）を通り、版が古い更新・削除が DB の版条件で断られて
/// HTTP 409 経由で同じ競合例外に戻ることを、インメモリの擬似の版でなく本物の版で確かめる。
/// </para>
/// <para>
/// <c>AddGeneratedSqlServerRepositories</c> はリモート面への転送登録も行う。保存先はテストごとにスキーマを作り直して空にする。
/// Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(
    RuntimeFeatures.ConcurrencyVo,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.AdoSqlServer
)]
public sealed class ConcurrencyVoRemoteAdoSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : ConcurrencyVoRemoteRuntimeTestsBase
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>Docker の有無を判定する</summary>
    protected override void BeforeServerStart() =>
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

    /// <summary>サーバー実体は QuickER 版の SQL Server リポジトリ</summary>
    protected override void RegisterServerServices(IServiceCollection services) =>
        services.AddGeneratedSqlServerRepositories(_fixture.ConnectionString);

    /// <summary>スキーマを作り直して空にする</summary>
    protected override async Task ClearStoreAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
    }
}
