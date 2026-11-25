using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 名前付きクエリの自由 SQL の戻り形を、生成物の型に依らず呼ぶアダプタ（答えはプリミティブ）
/// </summary>
/// <remarks>
/// 生成物はフィクスチャごとに別の名前空間の型になるので（クエリフィクスチャ＝SQLite・<c>UniquenessSqlServerFixture</c>＝SQL Server）、
/// 行の表はこの面だけを通して流す。共通のシード（顧客 1=Alice / 2=Bob・注文 (10,顧客1,100,apple pie)・(11,顧客1,50,banana)・
/// (12,顧客2,200,apple juice)・(13,顧客1,75,メモなし)）は <see cref="ResetAndSeedAsync"/> が入れる。
/// </remarks>
public interface INamedQueryRawSqlBackend
{
    /// <summary>保存先を空にして共通のシードを入れる</summary>
    Task ResetAndSeedAsync();

    /// <summary>SumAmounts（スカラーの集計）</summary>
    Task<decimal?> SumAmountsAsync(int customerId);

    /// <summary>GetByIdsRaw（コレクションのパラメータの IN の展開）の注文 ID</summary>
    Task<IReadOnlyList<int>> GetByIdsRawAsync(IReadOnlyList<int> ids);

    /// <summary>FindTopRaw（単一の戻り形）の注文 ID とメモ（行なしは null）</summary>
    Task<NamedQueryRawSqlOrderRow?> FindTopRawAsync();

    /// <summary>CountByCustomerRaw（件数の戻り形）</summary>
    Task<int> CountByCustomerRawAsync(int customerId);

    /// <summary>GetMemoRowsRaw（射影の戻り形＝列の別名で DTO へ写す）の行</summary>
    Task<IReadOnlyList<NamedQueryRawSqlOrderRow>> GetMemoRowsRawAsync(int customerId);

    /// <summary>注文を全行消す（生 SQL の実行）</summary>
    Task DeleteAllOrdersAsync();
}

/// <summary>
/// SQL Server だけが持つ自由 SQL（IN の展開の置換の境界のクエリ <c>GetByIdsBoundaryRaw</c>）も呼べるアダプタ
/// </summary>
public interface INamedQueryRawSqlBoundaryBackend : INamedQueryRawSqlBackend
{
    /// <summary>
    /// GetByIdsBoundaryRaw：コレクションのパラメータ <c>rowcount</c> の IN と、下限 <c>rowcountMin</c> と、システム変数
    /// <c>@@rowcount</c> を同じ SQL に持つクエリの注文 ID
    /// </summary>
    Task<IReadOnlyList<int>> GetByIdsBoundaryRawAsync(IReadOnlyList<int> rowcount, int rowcountMin);
}

/// <summary>自由 SQL が返した注文の ID とメモ</summary>
/// <param name="OrderId">注文 ID（射影の DTO は NULL 許容の列で受けるので null 許容）</param>
/// <param name="Memo">メモ（NULL は null）</param>
public sealed record NamedQueryRawSqlOrderRow(int? OrderId, string? Memo);

/// <summary>自由 SQL の戻り形の行の入力</summary>
/// <param name="SqlServerOnly">SQL Server の生成物だけが持つクエリを使う行か（SQLite の派生には流さない）</param>
/// <param name="RunAsync">シードを入れた後の保存先に対して流す本体</param>
public sealed record NamedQueryRawSqlCase(
    bool SqlServerOnly,
    Func<INamedQueryRawSqlBackend, Task> RunAsync
);

/// <summary>
/// 名前付きクエリの自由 SQL の戻り形の表（QuickER 版と EF Core 版の SQLite・QuickER 版の SQL Server が同じ行を流す）
/// </summary>
/// <remarks>
/// <para>
/// 自由 SQL は「SQL が与えられた実装先だけが実装を生成される」ので、流すのは実 DB を持つ派生だけ（インメモリは manual＝テスト側の
/// partial になるので流さない）。EF Core 版はクエリフィクスチャの partial 実装（<c>QueryFixtureManualImplementations</c>）が同じ意味論を
/// 担うので、同じ表明で緑になることが「実装先が違っても意味論が揃う」ことの裏付けになる。
/// </para>
/// <para>
/// 行 <c>raw-sql-in-expansion-keeps-prefixed-names-and-system-variables</c> は SQL Server の生成物だけが持つクエリ（<c>@@rowcount</c>
/// は SQL Server の綴り）を使い、生 SQL のバインダの IN の展開の置換の境界（左右）を実 SQL Server で確かめる
/// （<c>src/QuickER.Runtime/CLAUDE.md</c>「生 SQL バインダ」）。
/// </para>
/// </remarks>
public static class NamedQueryRawSqlRows
{
    /// <summary>自由 SQL の戻り形の表</summary>
    public static ScenarioTable<NamedQueryRawSqlCase> Table { get; } =
        new(
            "NamedQueryRawSql",
            [
                Row(
                    "raw-sql-scalar-sum",
                    "自由 SQL のスカラーの集計（SUM）が合計を返し、該当なしは null",
                    async backend =>
                    {
                        (await backend.SumAmountsAsync(1)).Should().Be(225m);
                        (await backend.SumAmountsAsync(999)).Should().BeNull();
                    }
                ),
                Row(
                    "raw-sql-in-expansion-and-empty-list",
                    "自由 SQL のコレクションのパラメータを IN へ展開して正しい行を返し、空のリストは IN (NULL) でどの行にも一致しない",
                    async backend =>
                    {
                        (await backend.GetByIdsRawAsync([12, 10])).Should().Equal(10, 12);
                        (await backend.GetByIdsRawAsync([])).Should().BeEmpty();
                    }
                ),
                Row(
                    "raw-sql-single-or-null",
                    "自由 SQL の単一の戻り形が並び順の先頭の 1 件（値オブジェクトの復元を含む NULL のメモ）を返し、行なしは null",
                    async backend =>
                    {
                        (await backend.FindTopRawAsync())
                            .Should()
                            .Be(new NamedQueryRawSqlOrderRow(13, null));

                        await backend.DeleteAllOrdersAsync();
                        (await backend.FindTopRawAsync()).Should().BeNull();
                    }
                ),
                Row(
                    "raw-sql-count",
                    "自由 SQL の件数の戻り形が条件に一致する数を返す",
                    async backend =>
                    {
                        (await backend.CountByCustomerRawAsync(1)).Should().Be(3);
                        (await backend.CountByCustomerRawAsync(2)).Should().Be(1);
                        (await backend.CountByCustomerRawAsync(999)).Should().Be(0);
                    }
                ),
                Row(
                    "raw-sql-projection",
                    "自由 SQL の射影の戻り形が列の別名で DTO の一覧へ写し（NULL の列を含む）、該当なしは空",
                    async backend =>
                    {
                        (await backend.GetMemoRowsRawAsync(1))
                            .Should()
                            .Equal(
                                new NamedQueryRawSqlOrderRow(10, "apple pie"),
                                new NamedQueryRawSqlOrderRow(11, "banana"),
                                new NamedQueryRawSqlOrderRow(13, null)
                            );
                        (await backend.GetMemoRowsRawAsync(999)).Should().BeEmpty();
                    }
                ),
                new(
                    "raw-sql-in-expansion-keeps-prefixed-names-and-system-variables",
                    null,
                    "コレクションのパラメータ rowcount の IN の展開が、名前がその前置きで始まるパラメータ（@rowcountMin）とシステム変数（@@rowcount）を置き換えない（境界が右だけ・左だけだと SQL Server が SQL を断る）",
                    new NamedQueryRawSqlCase(
                        SqlServerOnly: true,
                        async backend =>
                        {
                            var boundary = (INamedQueryRawSqlBoundaryBackend)backend;
                            (await boundary.GetByIdsBoundaryRawAsync([13, 10, 11], 11))
                                .Should()
                                .Equal(11, 13);
                            (await boundary.GetByIdsBoundaryRawAsync([], 0)).Should().BeEmpty();
                        }
                    )
                ),
            ]
        );

    /// <summary>SQLite の派生が流す行の Id（SQL Server だけの行を除く）</summary>
    public static TheoryData<string> SqliteIds() => Ids(sqlServer: false);

    /// <summary>SQL Server の派生が流す行の Id（全行）</summary>
    public static TheoryData<string> SqlServerIds() => Ids(sqlServer: true);

    /// <summary>1 行を流す（シードを入れてから本体を流す）</summary>
    /// <param name="id">行の Id</param>
    /// <param name="backend">流す先</param>
    public static async Task RunAsync(string id, INamedQueryRawSqlBackend backend)
    {
        var row = Table[id];
        await backend.ResetAndSeedAsync();
        await row.Input.RunAsync(backend);
    }

    private static TheoryData<string> Ids(bool sqlServer)
    {
        var data = new TheoryData<string>();

        foreach (var row in Table.Where(row => sqlServer || !row.Input.SqlServerOnly))
        {
            data.Add(row.Id);
        }

        return data;
    }

    private static ScenarioRow<NamedQueryRawSqlCase> Row(
        string id,
        string description,
        Func<INamedQueryRawSqlBackend, Task> runAsync
    ) => new(id, null, description, new NamedQueryRawSqlCase(SqlServerOnly: false, runAsync));
}
