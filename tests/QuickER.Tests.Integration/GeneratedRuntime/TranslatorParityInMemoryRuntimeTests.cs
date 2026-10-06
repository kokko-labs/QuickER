using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <see cref="TranslatorParityRuntimeTestsBase"/> の共通シナリオを、インメモリ Repository へ流す派生。
/// </summary>
/// <remarks>
/// インメモリは式木をコンパイルして評価するので、翻訳器を通らない C# の意味論そのものの実装先になる。
/// Include の照合・更新の経路はインメモリ独自の実装を持つため、同じシナリオで突き合わせる。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class TranslatorParityInMemoryRuntimeTests : TranslatorParityRuntimeTestsBase
{
    protected override Task<ServiceProvider> CreateProviderAsync() =>
        Task.FromResult(
            new ServiceCollection()
                .AddGeneratedInMemoryRepositories(seedSampleData: false)
                .BuildServiceProvider()
        );
}
