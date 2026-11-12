using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを <b>rowversion 列 × 値オブジェクト</b>の図で、実 HTTP のリモート 3 階層へ流す派生のうち、
/// サーバー実体が本フィクスチャの<b>インメモリ Repository</b>のもの（Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <c>AddGeneratedInMemoryRepositories</c> はリモート面への転送登録も行う。サーバーはテストごとに空で起動するので、
/// 保存先を空にする処理は要らない。シナリオは <see cref="ConcurrencyVoRemoteRuntimeTestsBase"/> が持つ。
/// </remarks>
[RuntimeCell(RuntimeFeatures.ConcurrencyVo, RuntimeTarget.Remote, Server = RuntimeTarget.InMemory)]
public sealed class ConcurrencyVoRemoteRuntimeTests : ConcurrencyVoRemoteRuntimeTestsBase
{
    /// <summary>サーバー実体はインメモリ Repository（サンプルデータなし）</summary>
    protected override void RegisterServerServices(IServiceCollection services) =>
        services.AddGeneratedInMemoryRepositories(seedSampleData: false);

    /// <summary>サーバーはテストごとに空で起動するため何もしない</summary>
    protected override Task ClearStoreAsync() => Task.CompletedTask;
}
