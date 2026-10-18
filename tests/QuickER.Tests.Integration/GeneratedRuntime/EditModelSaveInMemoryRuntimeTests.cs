using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>EditModel の保存と <c>AcceptSaved</c> の共通シナリオを、インメモリ Repository で流す派生（CI 常時）。</summary>
public sealed class EditModelSaveInMemoryRuntimeTests
    : EditModelSaveRuntimeTestsBase,
        System.IDisposable
{
    private readonly ServiceProvider _provider;

    public EditModelSaveInMemoryRuntimeTests() =>
        _provider = new ServiceCollection()
            .AddGeneratedInMemoryRepositories(seedSampleData: false)
            .AddSaveHook(Hooks)
            .BuildServiceProvider();

    public void Dispose() => _provider.Dispose();

    protected override IGadgetRemoteRepository Gadgets =>
        _provider.GetRequiredService<IGadgetRemoteRepository>();

    protected override IGadgetNoteRemoteRepository Notes =>
        _provider.GetRequiredService<IGadgetNoteRemoteRepository>();

    /// <summary>コンテナはテストごとに作り直すので、ストアは最初から空</summary>
    protected override Task ResetAsync() => SeedAsync();

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
