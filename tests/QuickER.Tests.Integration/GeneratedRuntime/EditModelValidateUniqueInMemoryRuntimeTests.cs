using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の照合糖衣（<c>ValidateUniqueAsync</c>）を、インメモリ Repository（クエリフィクスチャ）で検証する派生。
/// </summary>
/// <remarks>
/// インメモリの重複チェックは SQL を組まず一意制約ごとの索引を引く別の実装（<c>src/QuickER.Runtime.InMemory/CLAUDE.md</c>「一意制約の索引」）で、
/// EditModel がその結果を同じ形でバインディングプロパティへ写すことを、基底の同じ表明で確かめる。ユーザー定義の重複チェックは
/// <c>InMemoryOrderRepository</c> の拡張点（<c>QueryFixtureManualImplementations</c>）が QuickER 版と同じ規則を持つ。
/// </remarks>
[RuntimeCell(RuntimeFeatures.EditModelValidateUnique, RuntimeTarget.InMemory)]
public sealed class EditModelValidateUniqueInMemoryRuntimeTests
    : EditModelValidateUniqueQueryFixtureRuntimeTestsBase
{
    /// <summary>インメモリ Repository を登録した DI コンテナ（テストごとに空のストア）</summary>
    private ServiceProvider? _provider;

    protected override ICustomerRepository Customers =>
        _provider!.GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository Orders => _provider!.GetRequiredService<IOrderRepository>();

    protected override Task CreateStorageAsync()
    {
        _provider = new ServiceCollection()
            .AddGeneratedInMemoryRepositories(seedSampleData: false)
            .BuildServiceProvider();

        return Task.CompletedTask;
    }

    protected override Task TearDownAsync()
    {
        _provider?.Dispose();

        return Task.CompletedTask;
    }
}
