using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の保存と <c>AcceptSaved</c> の共通シナリオを、リモート（in-process の Kestrel・サーバー実体はインメモリ
/// Repository）で流す派生（CI 常時）。Save フックはサーバー側に登録する。
/// </summary>
public sealed class EditModelSaveRemoteRuntimeTests : EditModelSaveRuntimeTestsBase, IAsyncLifetime
{
    private InProcessRemoteServer? _server;
    private ServiceProvider? _clientProvider;

    public async ValueTask InitializeAsync()
    {
        _server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedInMemoryRepositories(seedSampleData: false).AddSaveHook(Hooks),
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    protected override IGadgetRemoteRepository Gadgets =>
        _clientProvider!.GetRequiredService<IGadgetRemoteRepository>();

    protected override IGadgetNoteRemoteRepository Notes =>
        _clientProvider!.GetRequiredService<IGadgetNoteRemoteRepository>();

    /// <summary>サーバーはテストごとに空で起動する</summary>
    protected override Task ResetAsync() => SeedAsync();

    /// <summary>リモート面は <c>Query()</c> を持たないので、親とメモを別々に取得して組み立てる</summary>
    protected override async Task<GadgetEntity?> GetWithNotesAsync(int id)
    {
        var root = await Gadgets.GetByIdAsync(GadgetIdValue.Create(id), Ct);

        if (root is null)
        {
            return null;
        }

        foreach (var noteId in new[] { 10, 11 })
        {
            if (
                await Notes.GetByIdAsync(NoteIdValue.Create(noteId), Ct) is { } note
                && note.GadgetId.Value == id
            )
            {
                root.GadgetNotes.Add(note);
            }
        }

        return root;
    }
}
