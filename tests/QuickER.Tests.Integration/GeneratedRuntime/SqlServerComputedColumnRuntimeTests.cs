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

    /// <summary>
    /// システムバージョン管理テーブル（テンポラルテーブル）の本表を作る DDL。
    /// </summary>
    /// <remarks>
    /// 期間列は <c>GENERATED ALWAYS AS ROW START / END</c>＝<b>NOT NULL の計算列</b>で、DB が値を採番する。
    /// 取込（<c>SqlServerSchemaImporter</c>）はこの列に <c>IsComputed</c> を立てるため、生成物では
    /// <c>[ComputedColumn]</c> が付いて INSERT / UPDATE から外れる。外れていなければ
    /// <c>Cannot insert an explicit value into a GENERATED ALWAYS column</c> でそのテーブルへの
    /// 全書き込みが失敗する。
    /// </remarks>
    /// <param name="hidden">
    /// true なら期間列へ <c>HIDDEN</c> を付ける。<c>SELECT *</c> の結果から消えるが、生成コードは列を
    /// 明示列挙するので同じように読める——その非対称を実 DB で固定するためのアーム。
    /// </param>
    private static string TemporalDdl(bool hidden) =>
        $"""
            CREATE TABLE temporal_items (
                id int NOT NULL PRIMARY KEY,
                name nvarchar(50) NOT NULL,
                period_start datetime2 GENERATED ALWAYS AS ROW START {(
                hidden ? "HIDDEN " : string.Empty
            )}NOT NULL,
                period_end datetime2 GENERATED ALWAYS AS ROW END {(
                hidden ? "HIDDEN " : string.Empty
            )}NOT NULL,
                PERIOD FOR SYSTEM_TIME (period_start, period_end)
            )
            WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.temporal_items_history));
            """;

    /// <summary>
    /// テンポラルテーブルの後始末（システムバージョン管理を切ってから本表・履歴表を落とす）。
    /// </summary>
    /// <remarks>
    /// <see cref="SqlServerContainerFixture.ResetSchemaAsync"/> の一括 DROP はシステムバージョン管理の
    /// 有効なテーブルを落とせない。ここで片付けないと<b>後続の全 SQL Server 統合テスト</b>が
    /// スキーマのリセットで失敗する。
    /// </remarks>
    private const string TemporalCleanup = """
        IF OBJECT_ID('dbo.temporal_items', 'U') IS NOT NULL
        BEGIN
            IF EXISTS (
                SELECT 1 FROM sys.tables
                WHERE object_id = OBJECT_ID('dbo.temporal_items') AND temporal_type = 2
            )
                ALTER TABLE dbo.temporal_items SET (SYSTEM_VERSIONING = OFF);
            DROP TABLE IF EXISTS dbo.temporal_items_history;
            DROP TABLE dbo.temporal_items;
        END
        """;

    private readonly SqlServerContainerFixture _fixture = fixture;

    private ServiceProvider _provider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>計算列つきのスキーマを作り、SQL Server リポジトリを DI へ登録する</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ExecuteAsync(TemporalCleanup, Ct);
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(Ddl, Ct);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
    }

    /// <summary>DI コンテナを破棄し、テンポラルテーブルを残さずに片付ける</summary>
    public async ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        if (_fixture.IsAvailable)
        {
            // 後始末は打ち切らない（残すと後続の全 SQL Server 統合テストがリセットで落ちる）
            await _fixture.ExecuteAsync(TemporalCleanup, CancellationToken.None);
        }
    }

    private IItemRepository Items() => _provider.GetRequiredService<IItemRepository>();

    private ITemporalItemRepository TemporalItems() =>
        _provider.GetRequiredService<ITemporalItemRepository>();

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

    /// <summary>
    /// 本物のテンポラルテーブル（NOT NULL の期間列）へ、生成 Repository が INSERT / UPDATE でき、
    /// DB が採番した期間の値を読み戻せることを検証する。
    /// </summary>
    /// <remarks>
    /// 期間列を普通の列として扱うと、INSERT が値を送って
    /// <c>Cannot insert an explicit value into a GENERATED ALWAYS column</c> で必ず落ちる。
    /// <c>HIDDEN</c> の有無で <c>SELECT *</c> の見え方は変わるが、生成コードは列を明示列挙するため
    /// どちらでも同じ結果になる——その両アームを 1 つの Theory で押さえる。
    /// </remarks>
    /// <param name="hidden">期間列へ <c>HIDDEN</c> を付けるかどうか</param>
    [Theory(
        DisplayName = "[Computed/SqlServer] テンポラルテーブルへ INSERT / UPDATE でき、期間列を読み戻せる"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporalTable_InsertAndUpdate_SucceedAndReadPeriodColumns(bool hidden)
    {
        await _fixture.ExecuteAsync(TemporalDdl(hidden), Ct);

        var repository = TemporalItems();

        // INSERT（期間列は送らない＝送ると GENERATED ALWAYS で失敗する）
        await repository.InsertAsync(new TemporalItemEntity { Id = 1, Name = "first" }, Ct);

        var inserted = await repository.GetByIdAsync(1, Ct);
        inserted.Should().NotBeNull();
        inserted!.Name.Should().Be("first");
        inserted.PeriodStart.Should().NotBe(default, "DB が採番した行の開始時刻が読める");
        inserted.PeriodEnd.Should().NotBe(default, "DB が採番した行の終了時刻が読める");
        inserted.PeriodEnd.Should().BeAfter(inserted.PeriodStart);

        // UPDATE（期間列は SET 句に出ない）
        inserted.Name = "second";
        (await repository.UpdateAsync(inserted, cancellationToken: Ct)).Should().BeTrue();

        var updated = await repository.GetByIdAsync(1, Ct);
        updated!.Name.Should().Be("second");
        updated.PeriodStart.Should().NotBe(default);
        // 期間の開始はトランザクション開始時刻なので、更新後は必ず挿入時以降になる
        // （同一時刻を排除する保証は無いため「以降」で見る＝時刻の粒度に依存させない）
        updated
            .PeriodStart.Should()
            .BeOnOrAfter(inserted.PeriodStart, "更新のたびに DB が行の開始時刻を採番し直す");
    }
}
