using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを <b>rowversion 列 × 値オブジェクト</b>の図で、実 HTTP のリモート 3 階層へ流す派生のうち、
/// サーバー実体が <b>EF Core 版 Repository × 実 SQL Server</b>（Testcontainers・Docker 依存）のもの。
/// </summary>
/// <remarks>
/// <para>
/// EF Core のサーバーは、保存で DB が採番した rowversion を EF Core の変更追跡が読み戻してエンティティへ入れ、
/// リモートのエンドポイントがそのエンティティから版の表と列の値を作る。QuickER 版のサーバー
/// （<see cref="ConcurrencyVoRemoteAdoSqlServerRuntimeTests"/>）と同じシナリオ・同じ応答の経路で、
/// 版の書き戻し・版が古い更新と削除の競合・行なしと版が古いの区別が HTTP 409 を越えて同じに戻ることを確かめる。
/// </para>
/// <para>
/// <c>AddGeneratedEfCoreRepositories</c> はリモート面への転送登録も行う。保存先はテストごとにスキーマを作り直して空にする。
/// Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(
    RuntimeFeatures.ConcurrencyVo,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.EfCoreSqlServer
)]
public sealed class ConcurrencyVoRemoteEfCoreSqlServerRuntimeTests(
    SqlServerContainerFixture fixture
) : ConcurrencyVoRemoteRuntimeTestsBase
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>Docker の有無を判定する</summary>
    protected override void BeforeServerStart() =>
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

    /// <summary>サーバー実体は EF Core 版 Repository（SQL Server プロバイダ）</summary>
    protected override void RegisterServerServices(IServiceCollection services) =>
        services.AddGeneratedEfCoreRepositories(options =>
            options.UseSqlServer(_fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
        );

    /// <summary>スキーマを作り直して空にする</summary>
    protected override async Task ClearStoreAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
    }
}
