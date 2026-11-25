using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// 戻り形ごとの表明は行の表 <see cref="NamedQueryRawSqlRows"/> が持ち、この基底はクエリフィクスチャの型のアダプタ
/// （<see cref="INamedQueryRawSqlBackend"/>）として表を流す。SQL Server の QuickER 版
/// （<see cref="NamedQueryRawSqlAdoSqlServerRuntimeTests"/>）も同じ表を流す。
/// </para>
/// </remarks>
public abstract class NamedQueryRawSqlRuntimeTestsBase
    : NamedQueryRuntimeTestsBase,
        INamedQueryRawSqlBackend,
        IDisposable
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

    /// <summary>自由 SQL の戻り形の表の 1 行（SQLite の派生には SQL Server だけの行を流さない）</summary>
    /// <param name="id">行の Id</param>
    [Theory(DisplayName = "[NamedQuery] 自由 SQL の戻り形の表の行が成り立つ")]
    [MemberData(nameof(NamedQueryRawSqlRows.SqliteIds), MemberType = typeof(NamedQueryRawSqlRows))]
    public Task RawSqlRow_Holds(string id) => NamedQueryRawSqlRows.RunAsync(id, this);

    Task INamedQueryRawSqlBackend.ResetAndSeedAsync() => ResetAndSeedAsync();

    Task<decimal?> INamedQueryRawSqlBackend.SumAmountsAsync(int customerId) =>
        CreateOrderRepository().SumAmountsAsync(customerId, Ct);

    async Task<IReadOnlyList<int>> INamedQueryRawSqlBackend.GetByIdsRawAsync(
        IReadOnlyList<int> ids
    ) =>
        (await CreateOrderRepository().GetByIdsRawAsync(ids, Ct))
            .Select(order => order.OrderId.Value)
            .ToList();

    async Task<NamedQueryRawSqlOrderRow?> INamedQueryRawSqlBackend.FindTopRawAsync() =>
        await CreateOrderRepository().FindTopRawAsync(Ct) is { } order
            ? new NamedQueryRawSqlOrderRow(order.OrderId.Value, order.Memo?.Value)
            : null;

    Task<int> INamedQueryRawSqlBackend.CountByCustomerRawAsync(int customerId) =>
        CreateOrderRepository().CountByCustomerRawAsync(customerId, Ct);

    async Task<
        IReadOnlyList<NamedQueryRawSqlOrderRow>
    > INamedQueryRawSqlBackend.GetMemoRowsRawAsync(int customerId) =>
        (await CreateOrderRepository().GetMemoRowsRawAsync(customerId, Ct))
            .Select(row => new NamedQueryRawSqlOrderRow(row.OrderId, row.Memo))
            .ToList();

    Task INamedQueryRawSqlBackend.DeleteAllOrdersAsync() =>
        CreateOrderRepository().ExecuteSqlAsync("DELETE FROM \"orders\"", null, Ct);

    /// <summary>使い終えた一時 DB を破棄する（派生の DI コンテナ破棄は派生側で行う）</summary>
    public virtual void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
