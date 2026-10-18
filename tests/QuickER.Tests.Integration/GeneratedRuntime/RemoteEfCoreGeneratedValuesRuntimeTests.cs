using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedIdentityColumnFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// サーバーの実体が EF Core 版 Repository のリモートで、DB が決めた値（自動採番列）が、EF Core の読み戻しを経て
/// クライアントのエンティティへ戻ることを、実 SQL Server（<c>IDENTITY(1,1)</c>）で検証する。
/// </summary>
/// <remarks>
/// EF Core は採番した値を保存の後にエンティティへ書くが、リモートの応答が版しか運ばないと、その値はサーバーの
/// エンティティに残るだけでクライアントへ届かない。単一の挿入・更新と、グラフ保存の 3 つの経路で確かめる。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class RemoteEfCoreGeneratedValuesRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>非主キーの自動採番列を持つ実スキーマ（採番の定義は図に無いため DDL をここで書く）</summary>
    private const string Ddl = """
        CREATE TABLE identity_items (
            item_id int NOT NULL PRIMARY KEY,
            seq int IDENTITY(1,1) NOT NULL,
            name nvarchar(50) NOT NULL
        );
        """;

    /// <summary>呼び出し側が持たせる明示の値。DB の採番値（1 から）と取り違えない大きさにする</summary>
    private const int ExplicitSeq = 999;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly SqlServerContainerFixture _fixture = fixture;
    private InProcessRemoteServer? _server;
    private ServiceProvider? _client;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(Ddl, Ct);

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
    }

    private IIdentityItemRemoteRepository Items() =>
        _client!.GetRequiredService<IIdentityItemRemoteRepository>();

    private static IdentityItemEntity NewItem(int id, string name)
    {
        var item = new IdentityItemEntity
        {
            ItemId = id,
            Seq = ExplicitSeq,
            Name = name,
        };
        item.MarkAdded();
        return item;
    }

    [Fact(
        DisplayName = "[Remote/EF Core] 単一の挿入で DB が採番した値がクライアントのエンティティへ戻る"
    )]
    public async Task Insert_WritesBackGeneratedValue()
    {
        var item = NewItem(1, "alpha");

        await Items().InsertAsync(item, Ct);

        item.Seq.Should().Be(1, "EF Core が読み戻した採番値が応答で運ばれる");
    }

    [Fact(
        DisplayName = "[Remote/EF Core] グラフ保存で DB が採番した値がクライアントのエンティティへ戻る"
    )]
    public async Task Save_WritesBackGeneratedValue()
    {
        var first = NewItem(1, "alpha");
        var second = NewItem(2, "beta");

        await Items().SaveAsync([first, second], cancellationToken: Ct);

        first.Seq.Should().Be(1);
        second.Seq.Should().Be(2);
    }

    [Fact(
        DisplayName = "[Remote/EF Core] 単一の更新で、クライアントが持つ明示の値は DB の採番値で置き換わる"
    )]
    public async Task Update_WritesBackGeneratedValue()
    {
        var item = NewItem(1, "alpha");
        await Items().InsertAsync(item, Ct);

        item.Name = "beta";
        item.Seq = ExplicitSeq;
        (await Items().UpdateAsync(item, cancellationToken: Ct)).Should().BeTrue();

        item.Seq.Should().Be(1, "採番列は更新で書かれず、DB の値が戻る");
        (await Items().GetByIdAsync(1, Ct))!.Name.Should().Be("beta");
    }
}
