using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 名前付きクエリのうち<b>自由 SQL 由来の戻り形</b>を実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）で
/// 意味検証する共通基底。DSL・manual 部は <see cref="NamedQueryRuntimeTestsBase"/> から継承する。
/// </summary>
/// <remarks>
/// <para>
/// 自由 SQL は「SQL 文が与えられた実装先だけが実装を生成される」ため、対象は実 DB を持つ派生
/// （QuickER の <c>SqliteRepository</c> 版・EF Core Sqlite 版）に限られる。インメモリ実装にとっては
/// manual＝テスト側の partial 実装になり、検証しても生成器ではなくテストコードを見ることになるので、
/// 条件スキップではなく<b>この階層に置かないこと</b>で対象外にする（スキップ 0 の原則）。
/// </para>
/// <para>
/// 検証する戻り形は一覧（IN のリスト展開・空リスト）・単一・件数・スカラー集計・射影（列別名の DTO マップ）。
/// EF Core 側は同じ意味論の partial 実装（<c>QueryFixtureManualImplementations</c>）が担うため、
/// 両派生が同じアサーションで緑になること自体が「実装先が違っても意味論が揃う」ことの証明になる。
/// </para>
/// </remarks>
public abstract class NamedQueryRawSqlRuntimeTestsBase : NamedQueryRuntimeTestsBase, IDisposable
{
    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列（バックエンドはこの実ファイルへ読み書きする）</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>スキーマを作り直し、共通のシードデータを投入する</summary>
    protected override async Task ResetAndSeedAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        await SeedAsync();
    }

    /// <summary>7. 自由 SQL のスカラー集計（SumAmounts）が合計を返す（該当なしは null）</summary>
    [Fact(DisplayName = "[NamedQuery] 7: 自由 SQL スカラー（SUM）が合計を返す")]
    public async Task SqlScalar_ReturnsSum()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        (await orders.SumAmountsAsync(1, Ct)).Should().Be(225m);
        (await orders.SumAmountsAsync(999, Ct)).Should().BeNull();
    }

    /// <summary>8. 自由 SQL の IN リスト展開（GetByIdsRaw）が正しい行を返し、空リストは空を返す</summary>
    [Fact(DisplayName = "[NamedQuery] 8: 自由 SQL の IN リスト展開が機能する（空リスト含む）")]
    public async Task SqlList_WithCollectionParameter_ExpandsIn()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var found = await orders.GetByIdsRawAsync([12, 10], Ct);
        found.Select(o => o.OrderId.Value).Should().Equal(10, 12);

        // 空リストは IN (NULL) へ展開され、どの行にも一致しない
        (await orders.GetByIdsRawAsync([], Ct))
            .Should()
            .BeEmpty();
    }

    /// <summary>12. 自由 SQL の単一戻り形（FindTopRaw）が 1 件を返す（行なしは null）</summary>
    [Fact(DisplayName = "[NamedQuery] 12: 自由 SQL の単一戻り形が 1 件（行なしは null）を返す")]
    public async Task SqlSingle_ReturnsFirstRowOrNull()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        var top = await orders.FindTopRawAsync(Ct);
        top.Should().NotBeNull();
        top!.OrderId.Value.Should().Be(13);
        top.Memo.Should().BeNull("注文 13 のメモは NULL（VO 復元込みの行マップを確認）");

        // 全行削除後は null
        await orders.ExecuteSqlAsync("DELETE FROM \"orders\"", null, Ct);
        (await orders.FindTopRawAsync(Ct)).Should().BeNull();
    }

    /// <summary>13. 自由 SQL の件数戻り形（CountByCustomerRaw）が条件一致数を返す</summary>
    [Fact(DisplayName = "[NamedQuery] 13: 自由 SQL の件数戻り形が条件一致数を返す")]
    public async Task SqlCount_ReturnsMatchingCount()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        (await orders.CountByCustomerRawAsync(1, Ct)).Should().Be(3);
        (await orders.CountByCustomerRawAsync(2, Ct)).Should().Be(1);
        (await orders.CountByCustomerRawAsync(999, Ct)).Should().Be(0);
    }

    /// <summary>14. 自由 SQL の射影戻り形（GetMemoRowsRaw）が列別名で DTO へマップされる（NULL 列含む）</summary>
    [Fact(DisplayName = "[NamedQuery] 14: 自由 SQL の射影戻り形が DTO 一覧を返す（NULL 列含む）")]
    public async Task SqlProjection_ReturnsDtoRows()
    {
        await ResetAndSeedAsync();
        var orders = CreateOrderRepository();

        // 顧客 1 の注文（10, 11, 13 の昇順）。13 のメモは NULL＝DTO の null 許容プロパティで受ける
        var rows = await orders.GetMemoRowsRawAsync(1, Ct);
        rows.Select(r => r.OrderId).Should().Equal(10, 11, 13);
        rows.Select(r => r.Memo).Should().Equal("apple pie", "banana", null);

        (await orders.GetMemoRowsRawAsync(999, Ct)).Should().BeEmpty();
    }

    /// <summary>使い終えた一時 DB を破棄する（派生の DI コンテナ破棄は派生側で行う）</summary>
    public virtual void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
