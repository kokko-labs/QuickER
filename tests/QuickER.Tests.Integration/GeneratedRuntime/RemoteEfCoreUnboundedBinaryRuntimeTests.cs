using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// サーバーの実体が EF Core のリモートで、除外した無制限バイナリ列を値として運ばず、未取得のまま送った更新が
/// 保存済みの blob を消さないことを、BinaryFixture（SQLite の EF Core）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// リモートの通信では除外列を値として運ばない（blob はバイナリのエンドポイントだけで運ぶ）。サーバーの応答は
/// エンティティの除外列を未取得の値へ戻し、EF Core のリポジトリはリモートの要求の中に限り、更新で除外列を書かない。
/// 直結の EF Core は従来どおり全列を読み書きする（対照）。
/// </para>
/// <para>
/// 射影の DTO（名前付きクエリの射影が除外列を参照するもの）はエンティティでないので戻さない。
/// </para>
/// <para>
/// GetById・GetAll の読み取り・読んで編集した更新・挿入は実装先の一致のシナリオ
/// （<c>blob-excluded-columns-read-and-write-guard</c>）が、未取得のまま組み立てた行の UpdateAsync（Optimistic と ForceOverwrite）と
/// グラフ保存（単数形と複数形）は <c>blob-unset-update-from-new-entity-keeps-blobs</c> が、EF Core 単独で生成したサーバーの
/// リモート（RemoteEfCoreOnly）を含む 5 つの実装先で見る。ここには名前付きクエリ（その図に無い）と、同じプロセスで直結の
/// EF Core を続けて呼ぶ形だけを残す。
/// </para>
/// </remarks>
[RuntimeCell(
    RuntimeFeatures.BinaryColumn,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.EfCoreSqlite
)]
public sealed class RemoteEfCoreUnboundedBinaryRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] StoredPayload = [9, 9, 9];
    private static readonly byte[] StoredThumb = [7];
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private InProcessRemoteServer? _server;
    private ServiceProvider? _client;

    public async ValueTask InitializeAsync()
    {
        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);

        using var ado = Ado();
        await ado.GetRequiredService<IDocumentRepository>()
            .InsertAsync(
                new DocumentEntity
                {
                    DocumentId = 1,
                    Title = "t1",
                    Payload = StoredPayload,
                    Thumb = StoredThumb,
                },
                Ct
            );
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _db.Dispose();
    }

    private ServiceProvider Ado() =>
        new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
            .BuildServiceProvider();

    private ServiceProvider EfCore() =>
        new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options
                    .UseSqlite(_db.ReadWriteCreateConnectionString)
                    .IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();

    /// <summary>サーバーの実体が EF Core のリモートを立て、クライアントの Repository を返す</summary>
    private async Task<IDocumentRemoteRepository> RemoteAsync()
    {
        _server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedEfCoreRepositories(options =>
                    options
                        .UseSqlite(_db.ReadWriteCreateConnectionString)
                        .IgnoreServiceProviderCacheLimit()
                ),
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );
        _client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
        return _client.GetRequiredService<IDocumentRemoteRepository>();
    }

    /// <summary>保存先の行を、除外列も含めて生 SQL で読む（SELECT に含めた除外列は取得される）</summary>
    private async Task<DocumentEntity> StoredAsync(int id)
    {
        using var ado = Ado();
        var rows = await ado.GetRequiredService<IDocumentRepository>()
            .QueryBySqlAsync($"SELECT * FROM documents WHERE document_id = {id}", null, Ct);
        return rows.Single();
    }

    [Fact(DisplayName = "EF Core のサーバー: 名前付きクエリの一覧は除外列を運ばない")]
    public async Task NamedQueryList_DoesNotCarryExcludedColumns()
    {
        var documents = await RemoteAsync();

        var byTitle = await documents.GetByTitleAsync("t1", Ct);
        byTitle.Should().ContainSingle().Which.Payload.Should().BeNull();
        byTitle[0].Thumb.Should().BeEmpty();
    }

    [Fact(DisplayName = "EF Core のサーバー: 除外列を参照する射影の DTO は値を運ぶ")]
    public async Task Projection_StillCarriesExcludedColumnValue()
    {
        var documents = await RemoteAsync();

        var rows = await documents.GetPayloadsAsync(Ct);

        rows.Should().ContainSingle().Which.Payload.Should().Equal(StoredPayload);
    }

    [Fact(DisplayName = "EF Core のサーバーへの要求の後も、直結の EF Core の更新は全列を書く")]
    public async Task DirectEfCoreUpdate_AfterRemoteRequest_StillWritesAllColumns()
    {
        var documents = await RemoteAsync();
        (await documents.GetByIdAsync(1, Ct)).Should().NotBeNull();

        using var efCore = EfCore();
        var direct = new DocumentEntity { DocumentId = 1, Title = "direct" };

        (
            await efCore
                .GetRequiredService<IDocumentRepository>()
                .UpdateAsync(direct, ConcurrencyMode.ForceOverwrite, Ct)
        )
            .Should()
            .BeTrue();

        var stored = await StoredAsync(1);
        stored.Title.Should().Be("direct");
        stored.Payload.Should().BeNull();
        stored.Thumb.Should().BeEmpty();
    }
}
