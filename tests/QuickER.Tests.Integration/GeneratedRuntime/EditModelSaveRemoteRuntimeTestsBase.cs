using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の保存と <c>AcceptSaved</c> の共通シナリオを、リモート（in-process の Kestrel・クライアントは生成された
/// HTTP リモート実装だけ）で流す派生の共通部。Save フック（<see cref="EditModelSaveRuntimeTestsBase.Hooks"/>）はサーバー側に登録する。
/// </summary>
/// <remarks>
/// 派生は、サーバーの DI（どの実装先のリポジトリを置くか）と、保存先を空にする処理だけを差し込む。
/// 版・DB とフックが書いた値は、保存の応答に載ってクライアントのエンティティへ書き戻され、そこから EditModel へ読み戻される。
/// </remarks>
public abstract class EditModelSaveRemoteRuntimeTestsBase
    : EditModelSaveRuntimeTestsBase,
        IAsyncLifetime
{
    private InProcessRemoteServer? _server;
    private ServiceProvider? _clientProvider;

    /// <summary>サーバーの起動の前に行う準備（実 DB の派生は Docker の有無を判定する）</summary>
    protected virtual void BeforeServerStart() { }

    /// <summary>サーバー側の DI に、リモート面を転送するリポジトリを登録する（Save フックは共通部が足す）</summary>
    protected abstract void RegisterServerServices(IServiceCollection services);

    /// <summary>保存先を空にする（テストごとに空で起動するサーバーなら何もしない）</summary>
    protected abstract Task ClearStoreAsync();

    public async ValueTask InitializeAsync()
    {
        BeforeServerStart();

        _server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                RegisterServerServices(services);
                services.AddSaveHook(Hooks);
            },
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

    /// <summary>保存先を空にしてから種を入れる</summary>
    protected override async Task ResetAsync()
    {
        await ClearStoreAsync();
        await SeedAsync();
    }

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
