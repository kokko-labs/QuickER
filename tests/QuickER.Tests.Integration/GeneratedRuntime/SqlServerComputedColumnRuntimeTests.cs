using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedComputedColumnFixture;
using QuickER.Tests.GeneratedComputedColumnFixture.Repositories.SqlServer;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 本物の計算列（<c>AS (式)</c>）を持つ実 SQL Server へ、生成 Repository が書き込めることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 計算列を普通の列として扱うと、INSERT / UPDATE がその列を書きに行き
/// <c>The column "total" cannot be modified because it is either a computed column or is the result of a UNION operator</c>
/// でそのテーブルへの<b>全書き込みが失敗</b>する（この修正前の実挙動）。
/// <c>[ComputedColumn]</c> と <c>EntitySaveMetadata</c> の除外が効いていれば通る。
/// </para>
/// <para>
/// スキーマはテスト側の DDL で作る（式は意味モデルに載らないため、図から生成した DDL は普通の列になる）。
/// SQLite 側の同一シナリオは <see cref="SqliteComputedColumnRuntimeTests"/>（Docker 不要）が担う。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerComputedColumnRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>計算列を持つ実スキーマ（式は図に無いため DDL をここで書く）</summary>
    private const string Ddl = """
        CREATE TABLE items (
            item_id int NOT NULL PRIMARY KEY,
            qty int NOT NULL,
            price decimal(18,2) NOT NULL,
            total AS (qty * price)
        );
        """;

    private readonly SqlServerContainerFixture _fixture = fixture;

    private ServiceProvider _provider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>計算列つきのスキーマを作り、SQL Server リポジトリを DI へ登録する</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(Ddl, Ct);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    private IItemRepository Items() => _provider.GetRequiredService<IItemRepository>();

    private static ItemEntity NewItem(int id, int qty, decimal price) =>
        new()
        {
            ItemId = id,
            Qty = qty,
            Price = price,
        };

    /// <summary>INSERT が計算列を書きに行かず成功し、DB が作った値を読み戻せることを検証する</summary>
    [Fact(DisplayName = "[Computed/SqlServer] INSERT は計算列を書かずに成功し、計算値を読み戻せる")]
    public async Task Insert_SucceedsAndReadsComputedValue()
    {
        var repository = Items();

        await repository.InsertAsync(NewItem(1, 3, 2.5m), Ct);

        var stored = await repository.GetByIdAsync(1, Ct);
        stored.Should().NotBeNull();
        stored!.Total.Should().Be(7.5m, "DB が式から作った値が SELECT で読める");
    }

    /// <summary>UPDATE が計算列を書きに行かず成功し、計算値が追従することを検証する</summary>
    [Fact(DisplayName = "[Computed/SqlServer] UPDATE は計算列を書かずに成功し、計算値が追従する")]
    public async Task Update_SucceedsAndComputedValueFollows()
    {
        var repository = Items();
        await repository.InsertAsync(NewItem(2, 3, 2.5m), Ct);

        var loaded = await repository.GetByIdAsync(2, Ct);
        loaded!.Qty = 4;
        (await repository.UpdateAsync(loaded, cancellationToken: Ct)).Should().BeTrue();

        (await repository.GetByIdAsync(2, Ct))!.Total.Should().Be(10.0m);
    }

    /// <summary>BulkInsert（SqlBulkCopy）が計算列を書きに行かず成功することを検証する</summary>
    [Fact(DisplayName = "[Computed/SqlServer] BulkInsert は計算列を書かずに成功する")]
    public async Task BulkInsert_Succeeds()
    {
        var repository = Items();

        var inserted = await repository.BulkInsertAsync(
            [NewItem(10, 2, 1.5m), NewItem(11, 5, 2m)],
            Ct
        );

        inserted.Should().Be(2);
        (await repository.GetByIdAsync(10, Ct))!.Total.Should().Be(3.0m);
        (await repository.GetByIdAsync(11, Ct))!.Total.Should().Be(10.0m);
    }

    /// <summary>グラフ保存（SaveAsync）が計算列を書きに行かず成功することを検証する</summary>
    [Fact(DisplayName = "[Computed/SqlServer] SaveAsync は計算列を書かずに成功する")]
    public async Task Save_Succeeds()
    {
        var repository = Items();
        var entity = NewItem(20, 6, 0.5m);
        entity.MarkAdded();

        (await repository.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);
        (await repository.GetByIdAsync(20, Ct))!.Total.Should().Be(3.0m);
    }
}
