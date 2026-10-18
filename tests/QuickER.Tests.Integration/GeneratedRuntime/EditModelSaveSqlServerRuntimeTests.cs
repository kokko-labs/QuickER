using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>EditModel の保存と <c>AcceptSaved</c> の共通シナリオを、QuickER 版 Repository（SQL Server・実コンテナ）で流す派生（Docker）。</summary>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class EditModelSaveSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : EditModelSaveRuntimeTestsBase,
        IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture = fixture;

    private ServiceProvider _provider = null!;

    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .AddSaveHook(Hooks)
            .BuildServiceProvider();

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    protected override IGadgetRemoteRepository Gadgets =>
        _provider.GetRequiredService<IGadgetRemoteRepository>();

    protected override IGadgetNoteRemoteRepository Notes =>
        _provider.GetRequiredService<IGadgetNoteRemoteRepository>();

    protected override async Task ResetAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
        await SeedAsync();
    }

    protected override Task<GadgetEntity?> GetWithNotesAsync(int id)
    {
        var key = GadgetIdValue.Create(id);

        return _provider
            .GetRequiredService<IGadgetRepository>()
            .Query()
            .Where(g => g.GadgetId == key)
            .Include(g => g.GadgetNotes)
            .FirstOrDefaultAsync(Ct);
    }
}
