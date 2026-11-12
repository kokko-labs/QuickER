using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の保存と <c>AcceptSaved</c> の共通シナリオを、リモート（サーバー実体はインメモリ Repository）で流す派生（CI 常時）。
/// </summary>
[RuntimeCell(RuntimeFeatures.EditModelSave, RuntimeTarget.Remote, Server = RuntimeTarget.InMemory)]
public sealed class EditModelSaveRemoteRuntimeTests : EditModelSaveRemoteRuntimeTestsBase
{
    protected override void RegisterServerServices(IServiceCollection services) =>
        services.AddGeneratedInMemoryRepositories(seedSampleData: false);

    /// <summary>サーバーはテストごとに空で起動する</summary>
    protected override Task ClearStoreAsync() => Task.CompletedTask;
}
