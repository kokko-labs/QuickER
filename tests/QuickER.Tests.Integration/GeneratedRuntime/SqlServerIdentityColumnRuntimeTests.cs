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
/// 本物の自動採番列（<c>IDENTITY(1,1)</c>）を持つ実 SQL Server へ、生成 Repository（QuickER 版・EF Core 版）が
/// 書き込めることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 自動採番列を普通の列として扱うと、INSERT がその列へ値を送って
/// <c>Cannot insert explicit value for identity column in table 'identity_items' when IDENTITY_INSERT is set to OFF</c>、
/// UPDATE が <c>Cannot update identity column 'seq'</c> でそのテーブルへの<b>全書き込みが失敗する</b>。
/// QuickER 版は <c>[IdentityColumn]</c> と <c>EntitySaveMetadata</c> の除外、EF Core 版は Fluent の
/// <c>ValueGeneratedOnAddOrUpdate()</c> が効いていれば通る。
/// </para>
/// <para>
/// スキーマはテスト側の DDL で作る（採番の開始値・増分は意味モデルに載らないため）。
/// 呼び出し側が明示の値（999）を持っていても、DB の採番した値（1）が保存される——
/// 明示値が黙って採用されるのでなく、無視されることを固定する。
/// PostgreSQL（<c>GENERATED ALWAYS AS IDENTITY</c>）の同一シナリオは <see cref="PostgreSqlIdentityColumnRuntimeTests"/> が担う。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerIdentityColumnRuntimeTests(SqlServerContainerFixture fixture)
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

    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER 版 Repository（SQL Server）を登録した DI コンテナ</summary>
    private ServiceProvider _adoProvider = null!;

    /// <summary>EF Core 版 Repository（UseSqlServer）を登録した DI コンテナ</summary>
    private ServiceProvider _efCoreProvider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>自動採番列つきのスキーマを作り、2 種類のリポジトリを DI へ登録する</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(Ddl, Ct);

        _adoProvider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        _efCoreProvider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _adoProvider?.Dispose();
        _efCoreProvider?.Dispose();

        return ValueTask.CompletedTask;
    }

    private IIdentityItemRepository AdoItems() =>
        _adoProvider.GetRequiredService<IIdentityItemRepository>();

    private IIdentityItemRepository EfCoreItems() =>
        _efCoreProvider.GetRequiredService<IIdentityItemRepository>();

    private static IdentityItemEntity NewItem(int id, string name) =>
        new()
        {
            ItemId = id,
            Seq = ExplicitSeq,
            Name = name,
        };

    /// <summary>
    /// 区切り 43（#1）：採番でない素の int の主キーが 0 の行も、アプリが決めたキーのまま書く（QuickER 版と EF Core）。
    /// 片方の経路で 0 を書いた後、もう片方の経路で同じ 0 を書くと主キーの重複になる
    /// </summary>
    [Theory(
        DisplayName = "[Identity/SqlServer] 主キー 0 の挿入とグラフ保存は、アプリが決めたキーのまま書く（QuickER 版と EF Core）"
    )]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task KeyZero_IsWrittenAsGiven(bool efCore, bool viaSave)
    {
        var repository = efCore ? EfCoreItems() : AdoItems();

        async Task WriteAsync(bool save, string name)
        {
            var item = NewItem(0, name);

            if (save)
            {
                item.MarkAdded();
                await repository.SaveAsync(item, cancellationToken: Ct);
            }
            else
            {
                await repository.InsertAsync(item, Ct);
            }
        }

        await WriteAsync(viaSave, "zero");

        (await repository.GetByIdAsync(0, Ct))!.Name.Should().Be("zero");

        var duplicate = () => WriteAsync(!viaSave, "again");
        await duplicate.Should().ThrowAsync<Exception>();
        (await repository.GetByIdAsync(0, Ct))!.Name.Should().Be("zero");
    }

    /// <summary>QuickER 版の INSERT が自動採番列へ値を送らず、DB の採番値を読み戻せることを検証する</summary>
    [Fact(
        DisplayName = "[Identity/SqlServer] QuickER 版 INSERT は自動採番列を書かずに成功し、DB の採番値を読み戻せる"
    )]
    public async Task Ado_Insert_IgnoresExplicitValueAndReadsAssignedValue()
    {
        var repository = AdoItems();

        await repository.InsertAsync(NewItem(1, "first"), Ct);

        var stored = await repository.GetByIdAsync(1, Ct);
        stored.Should().NotBeNull();
        stored!.Seq.Should().Be(1, "明示の 999 は送られず、DB が採番した値が SELECT で読める");
        stored.Name.Should().Be("first");
    }

    /// <summary>QuickER 版の UPDATE が自動採番列を SET 句に出さず成功し、採番値が変わらないことを検証する</summary>
    [Fact(
        DisplayName = "[Identity/SqlServer] QuickER 版 UPDATE は自動採番列を書かずに成功し、採番値が変わらない"
    )]
    public async Task Ado_Update_SucceedsAndSeqIsUnchanged()
    {
        var repository = AdoItems();
        // 準備は生 SQL（INSERT の経路に依存させず、UPDATE の除外だけを単独で確かめる）
        await _fixture.ExecuteAsync(
            "INSERT INTO identity_items (item_id, name) VALUES (2, N'before');",
            Ct
        );

        var loaded = await repository.GetByIdAsync(2, Ct);
        loaded!.Name = "after";
        (await repository.UpdateAsync(loaded, cancellationToken: Ct)).Should().BeTrue();

        var updated = await repository.GetByIdAsync(2, Ct);
        updated!.Name.Should().Be("after");
        updated.Seq.Should().Be(loaded.Seq, "自動採番列は UPDATE で触れない");
    }

    /// <summary>グラフ保存（SaveAsync）の新規行が自動採番列を書かずに成功することを検証する</summary>
    [Fact(
        DisplayName = "[Identity/SqlServer] QuickER 版 SaveAsync は新規行の自動採番列を書かずに成功する"
    )]
    public async Task Ado_Save_Succeeds()
    {
        var repository = AdoItems();
        var entity = NewItem(3, "saved");
        entity.MarkAdded();

        (await repository.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);

        var stored = await repository.GetByIdAsync(3, Ct);
        stored!.Seq.Should().Be(1);
    }

    /// <summary>BulkInsert（SqlBulkCopy）が自動採番列を書きに行かず成功することを検証する</summary>
    [Fact(
        DisplayName = "[Identity/SqlServer] QuickER 版 BulkInsert は自動採番列を書かずに成功する"
    )]
    public async Task Ado_BulkInsert_Succeeds()
    {
        var repository = AdoItems();

        var inserted = await repository.BulkInsertAsync(
            [NewItem(10, "bulk-a"), NewItem(11, "bulk-b")],
            Ct
        );

        inserted.Should().Be(2);
        (await repository.GetByIdAsync(10, Ct))!.Seq.Should().NotBe(ExplicitSeq);
        (await repository.GetByIdAsync(11, Ct))!.Seq.Should().NotBe(ExplicitSeq);
    }

    /// <summary>EF Core 版の INSERT が明示値を送らず、DB の採番値が保存されることを検証する</summary>
    [Fact(
        DisplayName = "[Identity/SqlServer] EF Core 版 INSERT は明示値を送らずに成功し、DB の採番値が保存される"
    )]
    public async Task EfCore_Insert_IgnoresExplicitValueAndStoresAssignedValue()
    {
        var repository = EfCoreItems();

        await repository.InsertAsync(NewItem(20, "first"), Ct);

        var stored = await repository.GetByIdAsync(20, Ct);
        stored.Should().NotBeNull();
        stored!
            .Seq.Should()
            .Be(1, "Fluent の ValueGeneratedOnAddOrUpdate により明示の 999 は送られない");
        stored.Name.Should().Be("first");
    }

    /// <summary>EF Core 版の UPDATE が自動採番列を書かずに成功し、採番値が変わらないことを検証する</summary>
    [Fact(
        DisplayName = "[Identity/SqlServer] EF Core 版 UPDATE は自動採番列を書かずに成功し、採番値が変わらない"
    )]
    public async Task EfCore_Update_SucceedsAndSeqIsUnchanged()
    {
        var repository = EfCoreItems();
        // 準備は生 SQL（INSERT の経路に依存させず、UPDATE の除外だけを単独で確かめる）
        await _fixture.ExecuteAsync(
            "INSERT INTO identity_items (item_id, name) VALUES (21, N'before');",
            Ct
        );

        var loaded = await repository.GetByIdAsync(21, Ct);
        loaded!.Name = "after";
        (await repository.UpdateAsync(loaded, cancellationToken: Ct)).Should().BeTrue();

        var updated = await repository.GetByIdAsync(21, Ct);
        updated!.Name.Should().Be("after");
        updated.Seq.Should().Be(loaded.Seq, "自動採番列は UPDATE で触れない");
    }

    /// <summary>EF Core 版のグラフ保存（SaveAsync）が新規行の自動採番列を書かずに成功することを検証する</summary>
    [Fact(
        DisplayName = "[Identity/SqlServer] EF Core 版 SaveAsync は新規行の自動採番列を書かずに成功する"
    )]
    public async Task EfCore_Save_Succeeds()
    {
        var repository = EfCoreItems();
        var entity = NewItem(22, "saved");
        entity.MarkAdded();

        (await repository.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);

        var stored = await repository.GetByIdAsync(22, Ct);
        stored!.Seq.Should().Be(1);
    }
}
