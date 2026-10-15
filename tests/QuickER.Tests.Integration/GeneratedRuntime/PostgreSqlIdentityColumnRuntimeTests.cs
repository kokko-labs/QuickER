using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedIdentityColumnFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 本物の <c>GENERATED ALWAYS AS IDENTITY</c> 列を持つ実 PostgreSQL へ、生成した EF Core 版 Repository が
/// 書き込めることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL の <c>GENERATED ALWAYS</c> は明示値を<b>常に拒否する</b>
/// （<c>cannot insert a non-DEFAULT value into column "seq"</c>）。EF Core の Fluent が
/// <c>ValueGeneratedOnAddOrUpdate()</c> を出していれば、INSERT / UPDATE ともその列を送らず通る。
/// </para>
/// <para>
/// 生成物は SQL Server 方言の図から作ったもの（EF Core の DbContext は方言に依らない）で、
/// 接続だけを <c>UseNpgsql</c> に差し替える。スキーマはテスト側の DDL で作る。
/// SQL Server の同一シナリオは <see cref="SqlServerIdentityColumnRuntimeTests"/> が担う。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlIdentityColumnRuntimeTests(PostgreSqlContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>非主キーの GENERATED ALWAYS 自動採番列を持つ実スキーマ</summary>
    private const string Ddl = """
        CREATE TABLE identity_items (
            item_id integer NOT NULL PRIMARY KEY,
            seq integer GENERATED ALWAYS AS IDENTITY NOT NULL,
            name varchar(50) NOT NULL
        );
        """;

    /// <summary>呼び出し側が持たせる明示の値。DB の採番値（1 から）と取り違えない大きさにする</summary>
    private const int ExplicitSeq = 999;

    private readonly PostgreSqlContainerFixture _fixture = fixture;

    /// <summary>EF Core 版 Repository（UseNpgsql）を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>自動採番列つきのスキーマを作り、EF Core 版リポジトリを DI へ登録する</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(Ddl, Ct);

        _provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseNpgsql(_fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    private IIdentityItemRepository Items() =>
        _provider.GetRequiredService<IIdentityItemRepository>();

    private static IdentityItemEntity NewItem(int id, string name) =>
        new()
        {
            ItemId = id,
            Seq = ExplicitSeq,
            Name = name,
        };

    /// <summary>INSERT が明示値を送らず、DB の採番値が保存されることを検証する</summary>
    [Fact(
        DisplayName = "[Identity/PostgreSql] EF Core 版 INSERT は明示値を送らずに成功し、DB の採番値が保存される"
    )]
    public async Task Insert_IgnoresExplicitValueAndStoresAssignedValue()
    {
        var repository = Items();

        await repository.InsertAsync(NewItem(1, "first"), Ct);

        var stored = await repository.GetByIdAsync(1, Ct);
        stored.Should().NotBeNull();
        stored!
            .Seq.Should()
            .Be(1, "Fluent の ValueGeneratedOnAddOrUpdate により明示の 999 は送られない");
        stored.Name.Should().Be("first");
    }

    /// <summary>UPDATE が自動採番列を書かずに成功し、採番値が変わらないことを検証する</summary>
    [Fact(
        DisplayName = "[Identity/PostgreSql] EF Core 版 UPDATE は自動採番列を書かずに成功し、採番値が変わらない"
    )]
    public async Task Update_SucceedsAndSeqIsUnchanged()
    {
        var repository = Items();
        // 準備は生 SQL（INSERT の経路に依存させず、UPDATE の除外だけを単独で確かめる）
        await _fixture.ExecuteAsync(
            "INSERT INTO identity_items (item_id, name) VALUES (2, 'before');",
            Ct
        );

        var loaded = await repository.GetByIdAsync(2, Ct);
        loaded!.Name = "after";
        (await repository.UpdateAsync(loaded, cancellationToken: Ct)).Should().BeTrue();

        var updated = await repository.GetByIdAsync(2, Ct);
        updated!.Name.Should().Be("after");
        updated.Seq.Should().Be(loaded.Seq, "自動採番列は UPDATE で触れない");
    }
}
