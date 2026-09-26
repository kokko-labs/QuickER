using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedComputedColumnFixture;
using QuickER.Tests.GeneratedComputedColumnFixture.Repositories.Sqlite;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 「図から作った DDL」を当てた DB（＝式を持たない普通の列になる側。マルチターゲットのミラーや
/// 双方向同期のローカルがこれにあたる）で、計算列の NULL 許容がそのまま書けるかどうかを分ける
/// ことを実 DB で固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 意味モデルは式を持たないため、図から生成する DDL は計算列を<b>普通の列</b>として作る。
/// 一方で生成コードは計算列を INSERT / UPDATE から外す（式が値を作るはずなので正しい）。
/// この 2 つが噛み合うかは<b>その列の NULL 許容だけ</b>で決まる:
/// </para>
/// <list type="bullet">
///   <item>NULL 許容なら書き込めて、その列は NULL のまま読める＝<b>これが利用者へ案内する逃げ道</b>。</item>
///   <item>NOT NULL なら送られない列の NOT NULL 制約で必ず落ちる＝<b>正しい失敗</b>（黙って壊さない）。</item>
/// </list>
/// <para>
/// 後者を DDL のコメント（<c>ComputedColumnComment</c>）と生成時 Warning
/// （<c>CodeGen_Warning_NotNullComputedColumns</c>）が事前に予告する。ここはその予告が
/// 指している実挙動そのものを押さえる。
/// </para>
/// <para>
/// Docker 不要のため CI でも常時実行される。本物の生成列（<c>GENERATED ALWAYS AS</c>）を持つ
/// DB の側は <see cref="SqliteComputedColumnRuntimeTests"/> が担う。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteComputedColumnDiagramDdlRuntimeTests : IAsyncLifetime
{
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();

    private ServiceProvider _provider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>フィクスチャの図から生成した SQLite DDL を当て、SQLite リポジトリを DI へ登録する</summary>
    public async ValueTask InitializeAsync()
    {
        // 手書きの DDL ではなく図から生成する（＝ミラー側の DB が実際にどう作られるかをそのまま再現する）
        await _sqlite.ApplyDdlAsync(ComputedColumnFixtureDefinition.Build(), Ct);

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

    /// <summary>
    /// NULL 許容の計算列なら、図から作った DDL の DB へ書き込めて、その列は NULL のまま読めることを検証する
    /// （利用者へ案内する逃げ道が実際に機能すること）。
    /// </summary>
    [Fact(
        DisplayName = "[Computed/SQLite] 図の DDL: NULL 許容の計算列は書き込めて NULL のまま読める"
    )]
    public async Task NullableComputedColumn_WriteSucceedsAndReadsNull()
    {
        var repository = _provider.GetRequiredService<IItemRepository>();

        await repository.InsertAsync(
            new ItemEntity
            {
                ItemId = 1,
                Qty = 3,
                Price = 2.5m,
            },
            Ct
        );

        var stored = await repository.GetByIdAsync(1, Ct);
        stored.Should().NotBeNull();
        stored!.Qty.Should().Be(3);
        stored.Total.Should().BeNull("式を持たない DB では計算列に値が入らない（NULL のまま）");
    }

    /// <summary>
    /// NOT NULL の計算列は、図から作った DDL の DB への追加が NOT NULL 制約で失敗することを検証する
    /// （生成コードがその列を送らないため。正しい失敗であって、黙って既定値を入れたりはしない）。
    /// </summary>
    [Fact(
        DisplayName = "[Computed/SQLite] 図の DDL: NOT NULL の計算列は NOT NULL 制約で追加に失敗する"
    )]
    public async Task NotNullComputedColumn_InsertFailsOnNotNullConstraint()
    {
        var repository = _provider.GetRequiredService<ITemporalItemRepository>();

        var act = async () =>
            await repository.InsertAsync(new TemporalItemEntity { Id = 1, Name = "first" }, Ct);

        var error = await act.Should().ThrowAsync<SqliteException>();
        error
            .Which.Message.Should()
            .Contain("NOT NULL constraint failed")
            .And.Contain(
                "temporal_items.period_start",
                "どの列で落ちたのかが分かる（予告した列名と突き合わせられる）"
            );
    }
}
