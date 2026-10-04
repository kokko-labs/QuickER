using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.SqlServer;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <see cref="TranslatorParityRuntimeTestsBase"/> の共通シナリオを、実 SQL Server（Testcontainers）の
/// QuickER 版 Repository へ流す派生。
/// </summary>
/// <remarks>
/// SQL Server は型の違う列の間に外部キー制約を作れないため、DDL は
/// <see cref="TranslatorParityFixtureDefinition.BuildWithoutCrossTypeForeignKey"/> から作る
/// （生成コードのナビゲーションはそのまま使える）。
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class TranslatorParitySqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : TranslatorParityRuntimeTestsBase
{
    protected override async Task<ServiceProvider> CreateProviderAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ApplyDdlAsync(
            TranslatorParityFixtureDefinition.BuildWithoutCrossTypeForeignKey(),
            Ct
        );

        return new ServiceCollection()
            .AddGeneratedSqlServerRepositories(fixture.ConnectionString)
            .BuildServiceProvider();
    }

    /// <summary>
    /// キー列と rowversion だけの表で、版を照合する UPDATE が新しい版を返し、古い版での更新を競合として拒むことを検証する。
    /// </summary>
    /// <remarks>
    /// SQL Server ではこの表に代入できる列が無いため、UPDATE はキー列を自分自身へ代入する。行は更新されるので版は進み、
    /// 照合（<c>AND [row_ver] = @originalRowVersion</c>）と <c>OUTPUT INSERTED</c> はほかの表と同じに働く。
    /// </remarks>
    [Fact(
        DisplayName = "[翻訳パリティ/SQL Server] 更新できる列の無い版つきの表でも版の照合と競合の検出が働く"
    )]
    public async Task VersionOnlyTable_GuardsTheVersion()
    {
        await SetUpAsync();

        var tags = Repository<IVersionedTagRepository>();
        await tags.InsertAsync(new VersionedTagEntity { Name = "v" }, Ct);

        var first = (await tags.GetByIdAsync("v", Ct))!;
        var stale = (await tags.GetByIdAsync("v", Ct))!;
        var original = first.RowVer;

        (await tags.UpdateAsync(first, cancellationToken: Ct)).Should().BeTrue();
        first.RowVer.Should().NotEqual(original);

        var update = () => tags.UpdateAsync(stale, cancellationToken: Ct);
        (await update.Should().ThrowAsync<SaveConflictException>())
            .Which.Reason.Should()
            .Be(SaveConflictReason.Modified);
    }
}
