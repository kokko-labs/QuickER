using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の保存と <c>AcceptSaved</c> の共通シナリオを、リモート（サーバー実体は QuickER 版 Repository の SQL Server 方言×
/// 実 SQL Server）で流す派生（Docker）。DB が採番した版とフックが書いた値が、保存の応答を通って EditModel まで届くかを見る。
/// </summary>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(
    RuntimeFeatures.EditModelSave,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.AdoSqlServer
)]
public sealed class EditModelSaveRemoteAdoSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : EditModelSaveRemoteRuntimeTestsBase
{
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>Docker の有無を判定する</summary>
    protected override void BeforeServerStart() =>
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

    protected override void RegisterServerServices(IServiceCollection services) =>
        services.AddGeneratedSqlServerRepositories(_fixture.ConnectionString);

    /// <summary>スキーマを作り直して空にする</summary>
    protected override async Task ClearStoreAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
    }
}
