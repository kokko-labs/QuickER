using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// サーバーの実体が EF Core のリモートで、版の古い ForceOverwrite の更新がやり直しの後も除外した無制限バイナリ列を
/// 書かないことを、実 SQL Server（ConcurrencyFixture の gadgets.attachment）で検証する。
/// </summary>
/// <remarks>
/// 版の古い ForceOverwrite は、EF Core の UPDATE が 0 行になった後、保存先の値を元の値として取り込み
/// （<c>OriginalValues.SetValues</c>）、やり直す。取り込みで除外列の元の値が保存先の blob になるので、変更の検出が除外列を
/// 書き換えた列として扱い、未取得のまま送られた値（NULL）を書く。やり直しの前にも除外列を書かない状態へ戻すことを、
/// 単一の更新とグラフ保存の 2 つの経路で確かめる（rowversion を採番する SQL Server でしかこの経路を通らない）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class RemoteEfCoreUnboundedBinarySqlServerRuntimeTests(
    SqlServerContainerFixture fixture
) : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] StoredAttachment = [1, 2, 3];
    private readonly SqlServerContainerFixture _fixture = fixture;
    private ServiceProvider? _ado;
    private InProcessRemoteServer? _server;
    private ServiceProvider? _client;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);

        _ado = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        await Gadgets()
            .InsertAsync(
                new GadgetEntity
                {
                    GadgetId = GadgetIdValue.Create(1),
                    Name = NameValue.Create("seed"),
                    Attachment = AttachmentValue.Create(StoredAttachment),
                },
                Ct
            );

        _server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedEfCoreRepositories(options =>
                    options
                        .UseSqlServer(_fixture.ConnectionString)
                        .IgnoreServiceProviderCacheLimit()
                ),
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );
        _client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _ado?.Dispose();
    }

    private IGadgetRepository Gadgets() => _ado!.GetRequiredService<IGadgetRepository>();

    private IGadgetRemoteRepository Remote() =>
        _client!.GetRequiredService<IGadgetRemoteRepository>();

    /// <summary>別の利用者の更新（版を進める）</summary>
    private Task OtherWriterUpdatesAsync() =>
        Gadgets()
            .ExecuteSqlAsync("UPDATE gadgets SET name = N'other' WHERE gadget_id = 1", null, Ct);

    /// <summary>保存先の行を、除外列も含めて生 SQL で読む</summary>
    private async Task<GadgetEntity> StoredAsync()
    {
        var rows = await Gadgets()
            .QueryBySqlAsync("SELECT * FROM gadgets WHERE gadget_id = 1", null, Ct);
        return rows.Single();
    }

    [Fact(
        DisplayName = "[Remote/EF Core] 版の古い ForceOverwrite の UpdateAsync は、やり直しの後も除外列を書かない"
    )]
    public async Task StaleForceOverwriteUpdate_KeepsAttachment()
    {
        var loaded = await Remote().GetByIdAsync(GadgetIdValue.Create(1), Ct);
        loaded!.Attachment.Should().BeNull("リモートは除外列を運ばない");
        await OtherWriterUpdatesAsync();
        loaded.Name = NameValue.Create("mine");

        (await Remote().UpdateAsync(loaded, ConcurrencyMode.ForceOverwrite, Ct)).Should().BeTrue();

        var stored = await StoredAsync();
        stored.Name.Value.Should().Be("mine");
        stored.Attachment!.Value.Should().Equal(StoredAttachment);
    }

    [Fact(
        DisplayName = "[Remote/EF Core] 版の古い ForceOverwrite のグラフ保存は、やり直しの後も除外列を書かない"
    )]
    public async Task StaleForceOverwriteSave_KeepsAttachment()
    {
        var loaded = await Remote().GetByIdAsync(GadgetIdValue.Create(1), Ct);
        await OtherWriterUpdatesAsync();
        loaded!.Name = NameValue.Create("saved");
        loaded.MarkUpdated();

        await Remote()
            .SaveAsync(loaded, mode: ConcurrencyMode.ForceOverwrite, cancellationToken: Ct);

        var stored = await StoredAsync();
        stored.Name.Value.Should().Be("saved");
        stored.Attachment!.Value.Should().Equal(StoredAttachment);
    }
}
