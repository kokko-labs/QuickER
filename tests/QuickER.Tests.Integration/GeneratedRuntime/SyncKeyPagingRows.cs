using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using QuickER.Tests.GeneratedSyncKeyFixture;
using QuickER.Tests.TestSupport.Engines;
using Xunit;
using Ss = QuickER.Tests.GeneratedSyncKeyFixture.Repositories.SqlServer;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>版なしの表のキー順ページングの表の 1 行の入力（ハーネスを受けて、サーバーへ行を入れてページを読み切る）</summary>
/// <param name="Run">行の手と表明</param>
internal sealed record SyncKeyPagingCase(Func<SyncKeyPagingHarness, CancellationToken, Task> Run);

/// <summary>
/// 版なし（<c>rowversion</c> 列の無い）同期の表のキー順ページングの表（影響の地図の名前 <c>table:SyncKeyPaging</c>）。
/// 行 = 主キーの型 × ページの境目に置く値の組。
/// </summary>
/// <remarks>
/// <para>
/// 版なしの表の全量走査は、サーバー（SQL Server）で <c>SELECT TOP (@batchSize) … WHERE [key] &gt; @afterKey ORDER BY [key]</c>
/// を繰り返す。<c>@afterKey</c> は生 SQL のバインダが値の型から推定した型で束縛される（文字列の主キーだけ列の型へ CAST する）。
/// 列の型と束縛の型が違うと、比べる値が丸められ、ページの境目で行を読み飛ばすか同じ行を読み直す。どの行も、サーバーの全行を
/// <c>ORDER BY</c> の順にちょうど 1 回ずつ読み、例外が無いことを表明する（期待の順はサーバーの <c>ORDER BY</c> そのもの
/// ＝照合順序・uniqueidentifier の並びを C# で写さない）。
/// </para>
/// <para>
/// 図は <see cref="SyncKeyFixtureDefinition"/>（主キーの型の違う版なしの表 9 つ）。batchSize は 1 を既定にする（境目がすべての
/// 行の間に来る）。続きがあると答えたのに再開点が進まない形は、製品では取得の守り（<c>SyncTable</c>）の例外になる。
/// batchSize 2 の行は、同じ値の組で境目の行を黙って読み飛ばす形（取得の守りが拾わない形）を見る。
/// </para>
/// </remarks>
internal static class SyncKeyPagingRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<SyncKeyPagingCase> Table { get; } = Build();

    /// <summary>表の Id（Theory のデータ）</summary>
    public static TheoryData<string> Ids() => Table.Ids();

    /// <summary>datetime の 1/300 秒の刻みと重ならない時刻を作る基準の時刻</summary>
    private static readonly DateTime Noon = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

    private static ScenarioRow<SyncKeyPagingCase> Row(
        string id,
        string description,
        Func<SyncKeyPagingHarness, CancellationToken, Task> run
    ) => new(id, null, description, new SyncKeyPagingCase(run));

    private static ScenarioTable<SyncKeyPagingCase> Build() =>
        new(
            "SyncKeyPaging",
            [
                Row(
                    "datetime2-ticks-between-legacy-datetime-steps",
                    "datetime2(7) の主キーの .0010000・.0019999・.0030000・.0040000（datetime の 1/300 秒の刻みの間の値）を"
                        + "どれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDatetime2,
                            factory => new Ss.SynckeyDatetime2Repository(factory),
                            Ticks(Noon, 10_000, 19_999, 30_000, 40_000),
                            ct
                        )
                ),
                Row(
                    "datetime2-ticks-between-legacy-datetime-steps-in-pairs",
                    "datetime2(7) の主キーの .0010000・.0019999・.0030000・.0040000 を batchSize 2 で読んでも、ページの境目の"
                        + ".0030000 を読み飛ばさずにどれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDatetime2,
                            factory => new Ss.SynckeyDatetime2Repository(factory),
                            Ticks(Noon, 10_000, 19_999, 30_000, 40_000),
                            ct,
                            batchSize: 2
                        )
                ),
                Row(
                    "datetime2-sub-millisecond-neighbors",
                    "datetime2(7) の主キーの 1 tick ずつ違う値と、datetime の刻み（.0033333）を挟む値をどれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDatetime2,
                            factory => new Ss.SynckeyDatetime2Repository(factory),
                            Ticks(Noon, 0, 1, 2, 33_333, 33_334),
                            ct
                        )
                ),
                Row(
                    "datetime2-before-1753",
                    "datetime2(7) の主キーの 0001 年・1700 年・1752 年の末（datetime の範囲の外）と 1753 年の初めの値を、例外なしに"
                        + "どれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDatetime2,
                            factory => new Ss.SynckeyDatetime2Repository(factory),
                            [
                                new DateTime(1, 1, 1),
                                new DateTime(1700, 6, 15, 8, 30, 0),
                                new DateTime(1752, 12, 31, 23, 59, 59).AddTicks(9_999_999),
                                new DateTime(1753, 1, 1),
                            ],
                            ct
                        )
                ),
                Row(
                    "datetime2-whole-seconds",
                    "datetime2(7) の主キーの秒ちょうどの値（datetime でも表せる値）をどれも 1 回ずつ順に読む（対照）",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDatetime2,
                            factory => new Ss.SynckeyDatetime2Repository(factory),
                            [Noon, Noon.AddSeconds(1), Noon.AddSeconds(2)],
                            ct
                        )
                ),
                Row(
                    "datetime-read-back-values",
                    "datetime の主キーの .000・.003・.007・.010（読み戻すと 1/300 秒の刻みの値）をどれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDatetime,
                            factory => new Ss.SynckeyDatetimeRepository(factory),
                            [
                                Noon,
                                Noon.AddMilliseconds(3),
                                Noon.AddMilliseconds(7),
                                Noon.AddMilliseconds(10),
                            ],
                            ct
                        )
                ),
                Row(
                    "datetime-range-ends",
                    "datetime の主キーの範囲の両端（1753-01-01 と 9999-12-31 23:59:59.997）をどれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDatetime,
                            factory => new Ss.SynckeyDatetimeRepository(factory),
                            [new DateTime(1753, 1, 1), new DateTime(9999, 12, 31, 23, 59, 59, 997)],
                            ct
                        )
                ),
                Row(
                    "date-consecutive-days",
                    "date の主キーの続いた 3 日をどれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDate,
                            factory => new Ss.SynckeyDateRepository(factory),
                            [
                                new DateTime(2026, 1, 1),
                                new DateTime(2026, 1, 2),
                                new DateTime(2026, 1, 3),
                            ],
                            ct
                        )
                ),
                Row(
                    "date-before-1753",
                    "date の主キーの 0001 年・1000 年・1752 年の末（datetime の範囲の外）と 1753 年の初めの値を、例外なしに"
                        + "どれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDate,
                            factory => new Ss.SynckeyDateRepository(factory),
                            [
                                new DateTime(1, 1, 1),
                                new DateTime(1000, 1, 1),
                                new DateTime(1752, 12, 31),
                                new DateTime(1753, 1, 1),
                            ],
                            ct
                        )
                ),
                Row(
                    "datetimeoffset-offsets-around-same-instant",
                    "datetimeoffset(7) の主キーの、オフセットが違い UTC で 1 tick ずつ違う 3 つの値を UTC の順にどれも 1 回ずつ読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyOffset,
                            factory => new Ss.SynckeyOffsetRepository(factory),
                            [
                                new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(1),
                                new DateTimeOffset(2026, 1, 1, 21, 0, 0, TimeSpan.FromHours(9)),
                                new DateTimeOffset(
                                    2026,
                                    1,
                                    1,
                                    3,
                                    0,
                                    0,
                                    TimeSpan.FromHours(-9)
                                ).AddTicks(2),
                            ],
                            ct
                        )
                ),
                Row(
                    "datetimeoffset-before-1753",
                    "datetimeoffset(7) の主キーの 0001 年・1752 年の末と 1753 年の初めの値を、例外なしにどれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyOffset,
                            factory => new Ss.SynckeyOffsetRepository(factory),
                            [
                                new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero),
                                new DateTimeOffset(
                                    1752,
                                    12,
                                    31,
                                    23,
                                    59,
                                    59,
                                    TimeSpan.Zero
                                ).AddTicks(9_999_999),
                                new DateTimeOffset(1753, 1, 1, 0, 0, 0, TimeSpan.Zero),
                            ],
                            ct
                        )
                ),
                Row(
                    "guid-sql-server-byte-order",
                    "uniqueidentifier の主キーの、SQL Server の並び（後ろの 6 バイトから比べる）と .NET の並びが割れる 5 つの値を"
                        + "サーバーの順にどれも 1 回ずつ読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyGuid,
                            factory => new Ss.SynckeyGuidRepository(factory),
                            [
                                new Guid("00000000-0000-0000-0000-000000000001"),
                                new Guid("01000000-0000-0000-0000-000000000000"),
                                new Guid("00000000-0000-0000-0000-010000000000"),
                                new Guid("00000000-0000-0000-0100-000000000000"),
                                new Guid("00000000-0000-0001-0000-000000000000"),
                            ],
                            ct
                        )
                ),
                Row(
                    "bigint-range-ends",
                    "bigint の主キーの範囲の両端と 0 の近くの値をどれも 1 回ずつ順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyBigint,
                            factory => new Ss.SynckeyBigintRepository(factory),
                            [long.MinValue, -1L, 0L, 1L, long.MaxValue],
                            ct
                        )
                ),
                Row(
                    "decimal-scale-and-range-ends",
                    "decimal(18,4) の主キーの範囲の両端・スケールの最小の刻み（0.0001）の近く・スケールの短い値をどれも 1 回ずつ"
                        + "順に読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyDecimal,
                            factory => new Ss.SynckeyDecimalRepository(factory),
                            [
                                -99_999_999_999_999.9999m,
                                -0.0001m,
                                0m,
                                0.0001m,
                                0.0002m,
                                1.5m,
                                99_999_999_999_999.9999m,
                            ],
                            ct
                        )
                ),
                Row(
                    "varchar-collation-order-differs-from-ordinal",
                    "varchar(20) の主キーの、照合順序の並びと序数の並びが割れる値（ハイフン・空白・下線）をサーバーの順に"
                        + "どれも 1 回ずつ読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyVarchar,
                            factory => new Ss.SynckeyVarcharRepository(factory),
                            ["a-c", "ab", "a c", "aa", "a_b", "ad"],
                            ct
                        )
                ),
                Row(
                    "varchar-case-insensitive-order",
                    "varchar(20) の主キーの、大文字小文字の混ざった値をサーバーの順（大文字小文字を区別しない）にどれも 1 回ずつ読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyVarchar,
                            factory => new Ss.SynckeyVarcharRepository(factory),
                            ["A1", "a2", "B1", "b2"],
                            ct
                        )
                ),
                Row(
                    "nvarchar-non-ascii-order",
                    "nvarchar(20) の主キーの、アクセント・CJK・全角の文字を含む値をサーバーの順にどれも 1 回ずつ読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyNvarchar,
                            factory => new Ss.SynckeyNvarcharRepository(factory),
                            ["e", "é", "f", "日本", "Ｚ"],
                            ct
                        )
                ),
                Row(
                    "nvarchar-collation-order-differs-from-ordinal",
                    "nvarchar(20) の主キーの、照合順序の並びと序数の並びが割れる値（ハイフン・空白・下線）をサーバーの順に"
                        + "どれも 1 回ずつ読む",
                    (h, ct) =>
                        h.AssertPagesEveryRowOnceAsync(
                            GeneratedSyncTables.SynckeyNvarchar,
                            factory => new Ss.SynckeyNvarcharRepository(factory),
                            ["a-c", "ab", "a c", "aa", "a_b", "ad"],
                            ct
                        )
                ),
            ]
        );

    /// <summary>基準の時刻に tick を足した値の並び</summary>
    private static DateTime[] Ticks(DateTime origin, params long[] ticks) =>
        ticks.Select(origin.AddTicks).ToArray();
}

/// <summary>
/// 版なしの表のキー順ページングの表のハーネス（実 SQL Server の 1 つの DB に、生成の直結の差分ソースを組む）
/// </summary>
/// <param name="connectionString">図の DDL を当てた SQL Server の DB の接続文字列</param>
internal sealed class SyncKeyPagingHarness(string connectionString)
{
    private readonly Ss.SqlConnectionFactory _factory = new(connectionString);

    /// <summary>
    /// サーバーに主キーの値の行を入れ、生成の <c>VersionlessDirectSyncSource</c> で batchSize のページを最後まで読み、
    /// サーバーの全行を <c>ORDER BY</c> の順にちょうど 1 回ずつ読み、例外が無いことを表明する。
    /// </summary>
    /// <remarks>
    /// 続きがあると答えたのに再開点が進まない形（同じ行を読み直す）は、呼び出しの回数がサーバーの行の数 + 1 を超えたところで
    /// 止めて「進まない」と書く（製品の取得の 1 か所〔<c>SyncTable</c>〕の守りは差分ソースの外にあるので、ここでは自分で止める）。
    /// </remarks>
    /// <param name="descriptor">表の記述子</param>
    /// <param name="repository">サーバー（SQL Server）の QuickER 版 Repository を作る関数</param>
    /// <param name="keys">入れる主キーの値</param>
    /// <param name="ct">取り消し</param>
    /// <param name="batchSize">1 ページの行の数</param>
    public async Task AssertPagesEveryRowOnceAsync<TEntity, TKey>(
        SyncTableDescriptor<TEntity, TKey> descriptor,
        Func<Ss.ISqlConnectionFactory, IRemoteRepositoryCore<TEntity, TKey>> repository,
        IReadOnlyList<TKey> keys,
        CancellationToken ct,
        int batchSize = 1
    )
        where TEntity : EntityBaseCore, new()
    {
        var writer = repository(_factory);

        foreach (var key in keys)
        {
            var entity = new TEntity();
            descriptor.WriteKey(entity, key);
            await writer.InsertAsync(entity, ct);
        }

        var expected = await ReadServerKeysInOrderAsync(descriptor, ct);
        expected.Should().HaveCount(keys.Count, "入れた行はどれもサーバーに別々の行として残る");

        var source = new VersionlessDirectSyncSource<TEntity, TKey>(
            new Ss.SqlExecutor(_factory),
            writer,
            descriptor
        );
        var read = new List<TKey>();
        Exception? failure = null;
        var stalled = false;

        try
        {
            var batch = await source.GetFirstPageAsync(batchSize, ct);
            var calls = 1;

            while (true)
            {
                read.AddRange(batch.Rows.Select(descriptor.ReadKey));

                if (!batch.HasMore)
                {
                    break;
                }

                if (calls > expected.Count + 1)
                {
                    stalled = true;
                    break;
                }

                batch = await source.GetPageAfterAsync(
                    descriptor.ReadKey(batch.Rows[^1]),
                    batchSize,
                    ct
                );
                calls++;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = exception;
        }

        var readTexts = read.Select(descriptor.FormatKey).ToList();
        var expectedTexts = expected.Select(descriptor.FormatKey).ToList();
        var report =
            $"表 {descriptor.TableName}: "
            + $"例外={(failure is null ? "なし" : failure.GetType().Name + ": " + failure.Message)}・"
            + $"進まない={stalled}・"
            + $"読んだキー=[{string.Join(", ", readTexts)}]・"
            + $"サーバーの ORDER BY の順=[{string.Join(", ", expectedTexts)}]";

        (
            failure is null
            && !stalled
            && readTexts.SequenceEqual(expectedTexts, StringComparer.Ordinal)
        )
            .Should()
            .BeTrue(
                "キー順のページングはサーバーの全行を ORDER BY の順にちょうど 1 回ずつ読む（"
                    + report
                    + "）"
            );
    }

    /// <summary>サーバーの主キーを <c>ORDER BY</c> の順に読む（記述子のキーの SQL に並びを足す）</summary>
    private async Task<List<TKey>> ReadServerKeysInOrderAsync<TEntity, TKey>(
        SyncTableDescriptor<TEntity, TKey> descriptor,
        CancellationToken ct
    )
        where TEntity : EntityBaseCore, new()
    {
        var keys = new List<TKey>();

        await using var connection = _factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            descriptor.ServerKeysSql + " ORDER BY 1",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            keys.Add((TKey)reader.GetValue(0));
        }

        return keys;
    }
}
