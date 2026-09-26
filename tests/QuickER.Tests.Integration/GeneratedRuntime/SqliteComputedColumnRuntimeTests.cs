using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedComputedColumnFixture;
using QuickER.Tests.GeneratedComputedColumnFixture.Repositories.Sqlite;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成列（<c>GENERATED ALWAYS AS ... STORED</c>）を持つ実 SQLite へ、生成 Repository が書き込めることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 計算列を普通の列として扱うと、INSERT / UPDATE がその列を書きに行き
/// 「cannot INSERT into generated column」でそのテーブルへの<b>全書き込みが失敗</b>する。
/// <c>[ComputedColumn]</c> と <c>EntitySaveMetadata</c> の除外が効いていれば通る——それを実 DB で固定する。
/// </para>
/// <para>
/// スキーマはテスト側の DDL で作る（式は意味モデルに載らないため、図から生成した DDL は普通の列になる）。
/// Docker 不要のため CI でも常時実行される。SQL Server 側は
/// <see cref="SqlServerComputedColumnRuntimeTests"/> が同じシナリオを実コンテナで回す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteComputedColumnRuntimeTests : IAsyncLifetime
{
    /// <summary>計算列を持つ実スキーマ（式は図に無いため DDL をここで書く）</summary>
    private const string Ddl = """
        CREATE TABLE items (
            item_id INTEGER NOT NULL PRIMARY KEY,
            qty INTEGER NOT NULL,
            price decimal(18,2) NOT NULL,
            total decimal(21,2) GENERATED ALWAYS AS (qty * price) STORED
        );
        """;

    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();

    private ServiceProvider _provider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>計算列つきのスキーマを作り、SQLite リポジトリを DI へ登録する</summary>
    public async ValueTask InitializeAsync()
    {
        await _sqlite.ApplyDdlAsync(Ddl, Ct);

        _provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(_sqlite.ReadWriteCreateConnectionString)
            .BuildServiceProvider();
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        _sqlite.Dispose();

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
    [Fact(DisplayName = "[Computed/SQLite] INSERT は計算列を書かずに成功し、計算値を読み戻せる")]
    public async Task Insert_SucceedsAndReadsComputedValue()
    {
        var repository = Items();

        await repository.InsertAsync(NewItem(1, 3, 2.5m), Ct);

        var stored = await repository.GetByIdAsync(1, Ct);
        stored.Should().NotBeNull();
        stored!.Total.Should().Be(7.5m, "DB が式から作った値が SELECT で読める");
    }

    /// <summary>UPDATE が計算列を書きに行かず成功し、計算値が追従することを検証する</summary>
    [Fact(DisplayName = "[Computed/SQLite] UPDATE は計算列を書かずに成功し、計算値が追従する")]
    public async Task Update_SucceedsAndComputedValueFollows()
    {
        var repository = Items();
        await repository.InsertAsync(NewItem(2, 3, 2.5m), Ct);

        var loaded = await repository.GetByIdAsync(2, Ct);
        loaded!.Qty = 4;
        (await repository.UpdateAsync(loaded, cancellationToken: Ct)).Should().BeTrue();

        (await repository.GetByIdAsync(2, Ct))!.Total.Should().Be(10.0m);
    }

    /// <summary>BulkInsert が計算列を書きに行かず成功することを検証する</summary>
    [Fact(DisplayName = "[Computed/SQLite] BulkInsert は計算列を書かずに成功する")]
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
    [Fact(DisplayName = "[Computed/SQLite] SaveAsync は計算列を書かずに成功する")]
    public async Task Save_Succeeds()
    {
        var repository = Items();
        var entity = NewItem(20, 6, 0.5m);
        entity.MarkAdded();

        (await repository.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);
        (await repository.GetByIdAsync(20, Ct))!.Total.Should().Be(3.0m);
    }

    /// <summary>
    /// EditModel 経由の保存でも計算列が未入力のまま通ることを検証する
    /// （計算列は入力必須にならず、未入力なら実体の現在値を保つ）。
    /// </summary>
    [Fact(DisplayName = "[Computed/SQLite] EditModel は計算列を必須にせず未入力のまま保存できる")]
    public async Task EditModel_ComputedColumnIsNotRequired_SaveSucceeds()
    {
        var mapper = new ItemMapper();
        var editModel = mapper.CreateEditModel();
        editModel.BindingItemId = "30";
        editModel.BindingQty = "7";
        editModel.BindingPrice = "2";

        // 計算列（Total）は未入力のまま＝必須検証に掛からない
        editModel.Validate().Should().BeTrue();

        var entity = mapper.CreateEntity(editModel, includeRemoved: false);
        await Items().InsertAsync(entity, Ct);

        (await Items().GetByIdAsync(30, Ct))!.Total.Should().Be(14.0m);
    }
}
