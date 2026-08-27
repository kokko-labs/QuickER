using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedMultiTargetFixture;
using QuickER.Tests.GeneratedMultiTargetFixture.Repositories.SqlServer;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// edge-skip の実行時観測を<b>QuickER 版 Repository（SQL Server 方言）</b>で実 SQL Server
/// （Testcontainers・Docker 依存）に流す派生。
/// </summary>
/// <remarks>
/// SQL Server の Include は単一クエリ＋FOR JSON で、SQLite のマルチクエリとは別実装。空のツリーが
/// 「JSON を組まないプレーン SELECT」へ落ちることまで含めて、同じ観測結果になることを確かめる。
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class IncludeGraphSelfReferenceSqlServerRuntimeTests(
    SqlServerContainerFixture fixture
) : IncludeGraphSelfReferenceMultiTargetRuntimeTestsBase, IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER の SQL Server リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        return ValueTask.CompletedTask;
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    protected override INodeRepository Nodes() => _provider.GetRequiredService<INodeRepository>();

    protected override async Task ResetSchemaAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(MultiTargetPortableFixtureDefinition.Build(), Ct);
    }
}
