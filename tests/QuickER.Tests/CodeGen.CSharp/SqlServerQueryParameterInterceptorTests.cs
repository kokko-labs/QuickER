using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QuickER.Runtime.EntityFrameworkCore;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core のランタイムの <see cref="SqlServerQueryParameterInterceptor"/> が、SQL Server のクエリのコマンドで、値が列の型に入らない
/// パラメータだけを値の型で束縛し、入る値・SaveChanges のコマンド・ほかのプロバイダの接続には触れないことを、行の表で検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 各行は、生成の Fluent と同じ構成（<c>HasPrecision(10, 2)</c>・<c>HasMaxLength(n).IsUnicode(false)</c>）の DbContext へ
/// SQL Server のプロバイダで LINQ を流し、インターセプターの後ろに置いた記録役が、接続を開かずにコマンドを止めてパラメータを読む
/// （DB は使わない）。パラメータの型は EF Core の SQL Server のプロバイダが比べる列から決めたもので、記録役はインターセプターが
/// 直した後の値を見る。
/// </para>
/// <para>
/// 答えが C# と同じになることは、簡易 DSL の総当たり（<c>Dsl/SqlServer</c>）と式の翻訳の一致の表（<c>table:TranslatorParityPredicates</c>）が
/// 実 SQL Server で確かめる。この表が持つのは答えに出ない側＝入る値は列の型のまま（SQL と索引の使われ方を変えない）と、
/// SaveChanges を変えないこと。datetime・smalldatetime の値は、EF Core が出どころを同じに渡す ExecuteDelete と ExecuteUpdate を、
/// EF Core が実際に作ったコマンドの本文（TagWith のコメントの行つきも）で見分けて、ExecuteDelete の条件だけを直し、UPDATE の値を変えないこと。
/// </para>
/// </remarks>
public class SqlServerQueryParameterInterceptorTests
{
    /// <summary>表の行（DbContext への操作と、記録したコマンドの値を持つパラメータの期待の型）</summary>
    /// <param name="Act">DbContext への操作</param>
    /// <param name="Expected">値が null でないパラメータの「値 型 精度 スケール 長さ」（コマンドの並び順）。値の型で束縛したパラメータの精度・スケール・長さは、SqlClient が値から決めた数</param>
    /// <param name="Tag">新しい行の札（減るだけの写し row-baseline.json に無い行だけ。tests/QuickER.Tests/TestSupport/CLAUDE.md「分岐の軸の台帳と新しい行の札」）</param>
    private sealed record InterceptRow(
        Func<ProbeContext, CancellationToken, Task> Act,
        string[] Expected,
        RowTag? Tag = null
    );

    /// <summary>札の対象の表の Id と札（減るだけの写しと照合する＝<see cref="RowBaselineTables"/>）</summary>
    internal static IReadOnlyList<BaselineEntry> BaselineEntries =>
        [.. Rows.Select(pair => new BaselineEntry(pair.Key, pair.Value.Tag))];

    /// <summary>行（Id → 行）</summary>
    /// <remarks>
    /// 値は閉包のフィールド（<see cref="Box{T}"/>）から読む。生成コードの名前付きクエリと同じく EF Core はパラメータにする
    /// （ラムダの中の定数や静的フィールドは SQL のリテラルになり、パラメータを作らない）。
    /// </remarks>
    private static IReadOnlyDictionary<string, InterceptRow> Rows { get; } =
        new Dictionary<string, InterceptRow>(StringComparer.Ordinal)
        {
            // ---- decimal(10,2) の列：入る値は列の型のまま、入らない値だけ精度とスケールを外す ----
            ["decimal-fitting-value-keeps-column-type"] = new(
                Where(1.23m, v => x => x.Amount == v.Value),
                ["1.23 Decimal 10 2 0"]
            ),
            ["decimal-trailing-zeros-fit-the-scale"] = new(
                Where(1.500m, v => x => x.Amount == v.Value),
                ["1.500 Decimal 10 2 0"]
            ),
            ["decimal-beyond-scale-is-bound-by-value"] = new(
                Where(1.234m, v => x => x.Amount == v.Value),
                ["1.234 Decimal 4 3 0"]
            ),
            ["decimal-negative-beyond-scale-is-bound-by-value"] = new(
                Where(-1.234m, v => x => x.Amount < v.Value),
                ["-1.234 Decimal 4 3 0"]
            ),
            ["decimal-beyond-precision-is-bound-by-value"] = new(
                Where(123456789.5m, v => x => x.Amount >= v.Value),
                ["123456789.5 Decimal 10 1 0"]
            ),
            // ---- 整数の列と decimal：EF Core は列を decimal(18,2) へ CAST して比べ、パラメータも decimal(18,2) にする ----
            ["int-column-decimal-rounding-to-integer-is-bound-by-value"] = new(
                Where(0.999m, v => x => x.Quantity == v.Value),
                ["0.999 Decimal 3 3 0"]
            ),
            ["int-column-fitting-decimal-keeps-cast-type"] = new(
                Where(1.5m, v => x => x.Quantity < v.Value),
                ["1.5 Decimal 18 2 0"]
            ),
            // ---- Unicode でない文字列の列：ASCII の外の文字を含む値だけ Unicode にする ----
            ["varchar-ascii-keeps-column-type"] = new(
                Where("abc", v => x => x.Code == v.Value),
                ["abc AnsiString 0 0 20"]
            ),
            ["varchar-non-ascii-is-bound-as-unicode"] = new(
                Where(TextEdges.KanjiNihon, v => x => x.Code == v.Value),
                ["日本 String 0 0 2"]
            ),
            ["char-non-ascii-is-bound-as-unicode"] = new(
                Where(TextEdges.KanjiNihon, v => x => x.Fixed == v.Value),
                ["日本 String 0 0 2"]
            ),
            ["varchar-like-pattern-non-ascii-is-bound-as-unicode"] = new(
                Where(TextEdges.KanjiNichi, v => x => x.Code != null && x.Code.Contains(v.Value)),
                ["%日% String 0 0 3"]
            ),
            // ---- IN のリストは要素ごとのパラメータで、要素ごとに判定する ----
            ["in-list-judges-each-element"] = new(
                Where(new List<decimal> { 1.23m, 1.234m }, v => x => v.Value.Contains(x.Amount)),
                ["1.23 Decimal 10 2 0", "1.234 Decimal 4 3 0"]
            ),
            // ---- ExecuteDelete の条件も同じ ----
            ["execute-delete-condition-is-bound-by-value"] = new(
                (c, ct) =>
                {
                    var v = new Box<decimal>(1.234m);
                    return c.Rows.Where(x => x.Amount == v.Value).ExecuteDeleteAsync(ct);
                },
                ["1.234 Decimal 4 3 0"]
            ),
            // ---- datetime の列：読み戻す値は列の型のまま、読み戻せない値は読み戻す値の間へ置き直して datetime2 ----
            ["datetime-read-back-keeps-column-type"] = new(
                Where<DateTime?>(LegacyBase.AddMilliseconds(3), v => x => x.Legacy == v.Value),
                [$"{Stamp(LegacyBase.AddMilliseconds(3))} DateTime 0 0 0"]
            ),
            ["datetime-between-read-back-and-step-is-moved"] = new(
                Where<DateTime?>(
                    LegacyBase.AddTicks(DateTimeEdges.LegacyWindowAfterReadBackTicks),
                    v => x => x.Legacy < v.Value
                ),
                [$"{Stamp(LegacyBase.AddMilliseconds(4))} DateTime2 0 7 0"]
            ),
            ["datetime-between-step-and-read-back-is-moved"] = new(
                Where<DateTime?>(
                    LegacyBase.AddTicks(DateTimeEdges.LegacyWindowBeforeReadBackTicks),
                    v => x => x.Legacy > v.Value
                ),
                [$"{Stamp(LegacyBase.AddMilliseconds(4))} DateTime2 0 7 0"]
            ),
            ["datetime-past-max-is-moved"] = new(
                Where<DateTime?>(
                    DateTimeEdges.SqlDateTimeMaxRoundingOverflow,
                    v => x => x.Legacy < v.Value
                ),
                [$"{Stamp(DateTimeEdges.SqlDateTimeMaxRoundingDown)} DateTime2 0 7 0"]
            ),
            ["datetime-before-min-is-bound-by-value"] = new(
                Where<DateTime?>(
                    DateTimeEdges.DayBeforeSqlDateTimeMin,
                    v => x => x.Legacy > v.Value
                ),
                [$"{Stamp(DateTimeEdges.DayBeforeSqlDateTimeMin)} DateTime2 0 7 0"]
            ),
            ["datetime-in-list-judges-each-element"] = new(
                Where(
                    new List<DateTime?>
                    {
                        LegacyBase.AddMilliseconds(7),
                        LegacyBase.AddTicks(DateTimeEdges.LegacyWindowBeforeReadBackTicks),
                    },
                    v => x => v.Value.Contains(x.Legacy)
                ),
                [
                    $"{Stamp(LegacyBase.AddMilliseconds(7))} DateTime 0 0 0",
                    $"{Stamp(LegacyBase.AddMilliseconds(4))} DateTime2 0 7 0",
                ]
            ),
            // ---- smalldatetime の列：分ちょうどの値は列の型のまま、ほかは値のまま datetime2（読み戻す値は書いた値と同じなので置き直さない） ----
            ["smalldatetime-whole-minute-keeps-column-type"] = new(
                Where<DateTime?>(SmallBase, v => x => x.Small == v.Value),
                [$"{Stamp(SmallBase)} DateTime 0 0 0"]
            ),
            ["smalldatetime-seconds-is-bound-by-value"] = new(
                Where<DateTime?>(SmallBase.AddSeconds(10), v => x => x.Small == v.Value),
                [$"{Stamp(SmallBase.AddSeconds(10))} DateTime2 0 7 0"]
            ),
            ["smalldatetime-past-max-is-bound-by-value"] = new(
                Where<DateTime?>(
                    DateTimeEdges.DayAfterSmallDateTimeMax,
                    v => x => x.Small < v.Value
                ),
                [$"{Stamp(DateTimeEdges.DayAfterSmallDateTimeMax)} DateTime2 0 7 0"]
            ),
            // ---- ExecuteDelete の条件も同じ（EF Core は出どころを ExecuteUpdate と同じに渡すので、本文の DELETE で見分ける） ----
            ["execute-delete-datetime-condition-is-moved"] = new(
                (c, ct) =>
                {
                    var v = new Box<DateTime?>(
                        LegacyBase.AddTicks(DateTimeEdges.LegacyWindowAfterReadBackTicks)
                    );
                    return c.Rows.Where(x => x.Legacy < v.Value).ExecuteDeleteAsync(ct);
                },
                [$"{Stamp(LegacyBase.AddMilliseconds(4))} DateTime2 0 7 0"]
            ),
            ["execute-delete-tagged-smalldatetime-condition-is-bound-by-value"] = new(
                (c, ct) =>
                {
                    var v = new Box<DateTime?>(SmallBase.AddSeconds(10));
                    return c
                        .Rows.TagWith("first tag" + (char)10 + "second tag")
                        .Where(x => x.Small == v.Value)
                        .ExecuteDeleteAsync(ct);
                },
                [$"{Stamp(SmallBase.AddSeconds(10))} DateTime2 0 7 0"]
            ),
            // ---- ExecuteUpdate の UPDATE は SET と WHERE を見分けられないので、datetime・smalldatetime の値を変えない ----
            ["execute-update-datetime-values-are-left-alone"] = new(
                (c, ct) =>
                {
                    var set = new Box<DateTime?>(
                        LegacyBase.AddTicks(DateTimeEdges.LegacyWindowAfterReadBackTicks)
                    );
                    var where = new Box<DateTime?>(SmallBase.AddSeconds(10));
                    return c
                        .Rows.Where(x => x.Small == where.Value)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Legacy, set.Value), ct);
                },
                [
                    $"{Stamp(LegacyBase.AddTicks(DateTimeEdges.LegacyWindowAfterReadBackTicks))} DateTime 0 0 0",
                    $"{Stamp(SmallBase.AddSeconds(10))} DateTime 0 0 0",
                ]
            ),
            // ---- 値のプールの正本：Kind（Utc・Local）。判定は Kind を読まず、置き直した値も元の Kind のまま（SqlClient は Kind で送り方を変えない） ----
            ["datetime-utc-read-back-keeps-column-type-and-kind"] = new(
                Where<DateTime?>(
                    DateTime.SpecifyKind(LegacyBase.AddMilliseconds(3), DateTimeKind.Utc),
                    v => x => x.Legacy == v.Value
                ),
                [$"{Stamp(LegacyBase.AddMilliseconds(3))}(Utc) DateTime 0 0 0"],
                CanonTag("FitsDateTime は Kind を読まない：読み戻す値は Utc でも列の型のまま")
            ),
            ["datetime-local-between-read-back-and-step-is-moved-keeping-kind"] = new(
                Where<DateTime?>(
                    DateTime.SpecifyKind(
                        LegacyBase.AddTicks(DateTimeEdges.LegacyWindowAfterReadBackTicks),
                        DateTimeKind.Local
                    ),
                    v => x => x.Legacy < v.Value
                ),
                [$"{Stamp(LegacyBase.AddMilliseconds(4))}(Local) DateTime2 0 7 0"],
                CanonTag("BetweenDateTimeReadBacks は置き直した値に元の Kind を付ける")
            ),
            ["smalldatetime-utc-whole-minute-keeps-column-type-and-kind"] = new(
                Where<DateTime?>(
                    DateTime.SpecifyKind(SmallBase, DateTimeKind.Utc),
                    v => x => x.Small == v.Value
                ),
                [$"{Stamp(SmallBase)}(Utc) DateTime 0 0 0"],
                CanonTag("FitsSmallDateTime は Kind を読まない：分ちょうどは Utc でも列の型のまま")
            ),
            ["smalldatetime-local-seconds-is-bound-by-value-keeping-kind"] = new(
                Where<DateTime?>(
                    DateTime.SpecifyKind(SmallBase.AddSeconds(10), DateTimeKind.Local),
                    v => x => x.Small == v.Value
                ),
                [$"{Stamp(SmallBase.AddSeconds(10))}(Local) DateTime2 0 7 0"],
                CanonTag("smalldatetime の置き直しは値のまま（元の Kind を保つ）")
            ),
            // ---- 値のプールの正本：翌日の 1 tick 前 ----
            ["datetime-tick-before-next-day-is-moved-below-read-back"] = new(
                Where<DateTime?>(
                    DateTimeEdges.OneTickBefore(LegacyBase.Date.AddDays(1)),
                    v => x => x.Legacy < v.Value
                ),
                [$"{Stamp(new DateTime(2024, 1, 1, 23, 59, 59, 998))} DateTime2 0 7 0"],
                CanonTag(
                    "BetweenDateTimeReadBacks の帯 7〜9（.9999999 は読み戻す .997 の 1 ms 後へ置き直す）"
                )
            ),
            ["smalldatetime-tick-before-next-day-is-bound-by-value"] = new(
                Where<DateTime?>(
                    DateTimeEdges.OneTickBefore(SmallBase.Date.AddDays(1)),
                    v => x => x.Small < v.Value
                ),
                [
                    $"{Stamp(DateTimeEdges.OneTickBefore(SmallBase.Date.AddDays(1)))} DateTime2 0 7 0",
                ],
                CanonTag(
                    "FitsSmallDateTime の分ちょうどの腕（その日の最後の tick は分ちょうどでない）"
                )
            ),
            // ---- 値のプールの正本：smalldatetime が分へ丸める境目（29.998 秒は切り下げ・30 秒は切り上げ）。判定は分ちょうどかだけで、どちらも値のまま datetime2 ----
            ["smalldatetime-29-998-seconds-is-bound-by-value"] = new(
                Where<DateTime?>(
                    SmallBase.AddTicks(29_998 * TimeSpan.TicksPerMillisecond),
                    v => x => x.Small == v.Value
                ),
                [
                    $"{Stamp(SmallBase.AddTicks(29_998 * TimeSpan.TicksPerMillisecond))} DateTime2 0 7 0",
                ],
                CanonTag("FitsSmallDateTime の分ちょうどの腕（29.998 秒は列が切り下げる側）")
            ),
            ["smalldatetime-30-seconds-is-bound-by-value"] = new(
                Where<DateTime?>(SmallBase.AddSeconds(30), v => x => x.Small == v.Value),
                [$"{Stamp(SmallBase.AddSeconds(30))} DateTime2 0 7 0"],
                CanonTag("FitsSmallDateTime の分ちょうどの腕（30 秒は列が切り上げる側）")
            ),
            // ---- 値のプールの正本：範囲の端ちょうどは列の型のまま（範囲の比較の境目の腕） ----
            ["datetime-range-min-keeps-column-type"] = new(
                Where<DateTime?>(ValueEdges.SqlDateTimeMin, v => x => x.Legacy == v.Value),
                [$"{Stamp(ValueEdges.SqlDateTimeMin)} DateTime 0 0 0"],
                CanonTag("FitsDateTime の value >= DateTimeMin（範囲の最初ちょうど）")
            ),
            ["datetime-range-max-keeps-column-type"] = new(
                Where<DateTime?>(ValueEdges.SqlDateTimeMax, v => x => x.Legacy == v.Value),
                [$"{Stamp(ValueEdges.SqlDateTimeMax)} DateTime 0 0 0"],
                CanonTag("FitsDateTime の value <= DateTimeMax（範囲の最後ちょうど）")
            ),
            ["smalldatetime-range-min-keeps-column-type"] = new(
                Where<DateTime?>(ValueEdges.SmallDateTimeMin, v => x => x.Small == v.Value),
                [$"{Stamp(ValueEdges.SmallDateTimeMin)} DateTime 0 0 0"],
                CanonTag("FitsSmallDateTime の value >= SmallDateTimeMin（範囲の最初ちょうど）")
            ),
            ["smalldatetime-range-max-keeps-column-type"] = new(
                Where<DateTime?>(ValueEdges.SmallDateTimeMax, v => x => x.Small == v.Value),
                [$"{Stamp(ValueEdges.SmallDateTimeMax)} DateTime 0 0 0"],
                CanonTag("FitsSmallDateTime の value <= SmallDateTimeMax（範囲の最後ちょうど）")
            ),
            ["decimal-column-range-max-keeps-column-type"] = new(
                Where(99999999.99m, v => x => x.Amount == v.Value),
                ["99999999.99 Decimal 10 2 0"],
                CanonTag(
                    "特性の固定：FitsDecimal の整数部の桁の境目（decimal(10,2) の最大）。束縛し直しても SqlParameter が値から精度とスケールを決め直すので、束縛し直したかは出力に出ない"
                )
            ),
            ["decimal-column-range-min-keeps-column-type"] = new(
                Where(-99999999.99m, v => x => x.Amount == v.Value),
                ["-99999999.99 Decimal 10 2 0"],
                CanonTag(
                    "特性の固定：FitsDecimal の Math.Abs の腕（decimal(10,2) の最小）。束縛し直しても SqlParameter が値から精度とスケールを決め直すので、束縛し直したかは出力に出ない（Math.Abs を外す mutation は赤にならない）"
                )
            ),
            // ---- SaveChanges のコマンドは変えない（列に入らない値は今までどおり列の型で送る） ----
            ["save-changes-is-left-alone"] = new(
                async (c, ct) =>
                {
                    c.Rows.Add(
                        new ProbeRow
                        {
                            Id = 1,
                            Amount = 1.234m,
                            Code = TextEdges.KanjiNihon,
                        }
                    );

                    try
                    {
                        await c.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateException)
                    {
                        // 記録役は空の結果を返すので、行数の照合で失敗してよい（見るのは記録したパラメータだけ）
                    }
                },
                ["1 Int64 0 0 0", "1.234 Decimal 10 2 0", "日本 AnsiString 0 0 20", "0 Int32 0 0 0"]
            ),
        };

    /// <summary>値のプールの正本で足した行の札（種別は既存の水準の対照。どの判定の腕かを理由に書く）</summary>
    private static RowTag CanonTag(string arm) =>
        RowTag.Reason(
            RowTagKind.ExistingLevelControl,
            $"値のプールの正本（決定 value-pool-canon-phase3-decisions）：{arm}"
        );

    /// <summary>datetime の列 legacy と比べる値の基準（ミリ秒の 0・3・7 が読み戻す値。<see cref="Rows"/> の初期化より前に読まれるのでプロパティにする）</summary>
    private static DateTime LegacyBase => new(2024, 1, 1, 10, 0, 0);

    /// <summary>smalldatetime の列 small と比べる値の基準（分ちょうど）</summary>
    private static DateTime SmallBase => new(2024, 1, 1, 10, 0, 0);

    /// <summary>日時を tick まで書く（<see cref="Describe"/> と同じ書式）</summary>
    private static string Stamp(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    /// <summary>値を閉包のフィールドから読む述語で、一覧を読む操作を作る</summary>
    private static Func<ProbeContext, CancellationToken, Task> Where<T>(
        T value,
        Func<Box<T>, Expression<Func<ProbeRow, bool>>> predicate
    ) => (c, ct) => c.Rows.Where(predicate(new Box<T>(value))).ToListAsync(ct);

    /// <summary>閉包の代わり（EF Core はフィールドの値をパラメータにする）</summary>
    private sealed class Box<T>(T value)
    {
        public readonly T Value = value;
    }

    /// <summary>表の行の Id</summary>
    public static TheoryData<string> Ids() => [.. Rows.Keys];

    [Theory(
        DisplayName = "SQL Server のクエリのパラメータは、値が列の型に入らないときだけ値の型で束縛する"
    )]
    [MemberData(nameof(Ids))]
    public async Task Parameters_AreRetypedOnlyWhenTheValueDoesNotFit(string id)
    {
        var row = Rows[id];
        var recorder = new CommandRecorder();
        var options = new DbContextOptionsBuilder<ProbeContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true")
            .IgnoreServiceProviderCacheLimit()
            .AddInterceptors(SqlServerQueryParameterInterceptor.Instance, recorder)
            .Options;
        await using var context = new ProbeContext(options);

        await row.Act(context, TestContext.Current.CancellationToken);

        recorder.LastParameters.Should().Equal(row.Expected);
    }

    [Fact(DisplayName = "SQL Server でない接続のコマンドには触れない")]
    public void OtherConnection_IsLeftUntouched()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var command = connection.CreateCommand();
        var parameter = command.CreateParameter();
        parameter.DbType = DbType.AnsiString;
        parameter.Size = 20;
        parameter.Value = TextEdges.KanjiNihon;
        command.Parameters.Add(parameter);

        SqlServerQueryParameterInterceptor.Instance.ReaderExecuting(command, null!, default);

        Describe(parameter).Should().Be("日本 AnsiString 0 0 20");
    }

    /// <summary>パラメータを「値 型 精度 スケール 長さ」で書く</summary>
    private static string Describe(DbParameter parameter) =>
        string.Join(
            ' ',
            parameter.Value is DateTime moment
                ? Stamp(moment)
                    + (moment.Kind == DateTimeKind.Unspecified ? "" : $"({moment.Kind})")
                : Convert.ToString(parameter.Value, CultureInfo.InvariantCulture),
            parameter.DbType,
            parameter.Precision.ToString(CultureInfo.InvariantCulture),
            parameter.Scale.ToString(CultureInfo.InvariantCulture),
            parameter.Size.ToString(CultureInfo.InvariantCulture)
        );

    /// <summary>生成の Fluent と同じ構成の行</summary>
    public sealed class ProbeRow
    {
        public long Id { get; set; }

        public decimal Amount { get; set; }

        public int Quantity { get; set; }

        public string? Code { get; set; }

        public string? Fixed { get; set; }

        public DateTime? Legacy { get; set; }

        public DateTime? Small { get; set; }
    }

    /// <summary>生成の Fluent と同じ構成の DbContext</summary>
    public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ProbeRow>(entity =>
            {
                entity.ToTable("probes");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
                entity.Property(e => e.Amount).HasColumnName("amount").HasPrecision(10, 2);
                entity.Property(e => e.Quantity).HasColumnName("quantity");
                entity
                    .Property(e => e.Code)
                    .HasColumnName("code")
                    .HasMaxLength(20)
                    .IsUnicode(false);
                entity
                    .Property(e => e.Fixed)
                    .HasColumnName("fixed")
                    .HasMaxLength(4)
                    .IsUnicode(false);
                entity.Property(e => e.Legacy).HasColumnName("legacy").HasColumnType("datetime");
                entity.Property(e => e.Small).HasColumnName("small").HasColumnType("smalldatetime");
            });
    }

    /// <summary>
    /// 接続を開かずに、コマンドを実行せずに止める記録役（インターセプターの後ろに置き、直した後のパラメータを記録する）
    /// </summary>
    private sealed class CommandRecorder : DbCommandInterceptor, IDbConnectionInterceptor
    {
        /// <summary>最後に止めたコマンドの、値が null でないパラメータ</summary>
        public IReadOnlyList<string> LastParameters { get; private set; } = [];

        public InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        ) => InterceptionResult.Suppress();

        public ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(InterceptionResult.Suppress());

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return ValueTask.FromResult(
                InterceptionResult<DbDataReader>.SuppressWithResult(
                    new DataTable().CreateDataReader()
                )
            );
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }

        /// <summary>最後に止めたコマンドの、値が null でないパラメータそのもの（値のプールの観測が読む）</summary>
        public IReadOnlyList<DbParameter> LastRaw { get; private set; } = [];

        private void Record(DbCommand command)
        {
            LastRaw =
            [
                .. command
                    .Parameters.Cast<DbParameter>()
                    .Where(parameter => parameter.Value is not (null or DBNull)),
            ];
            LastParameters = [.. LastRaw.Select(Describe)];
        }
    }

    /// <summary>
    /// 値のプールの観測：表の行の操作を、インターセプターを外して（直す前の値と型で）流し、コマンドのパラメータの値と、EF Core が比べる列から
    /// 決めた SqlParameter の型を列の相として読む（DB は使わない。値は変えない）
    /// </summary>
    /// <param name="ct">取り消し</param>
    internal static async Task<
        IReadOnlyList<TestSupport.ValuePools.ValueObservation>
    > ObserveValuesAsync(CancellationToken ct)
    {
        var observations = new List<TestSupport.ValuePools.ValueObservation>();

        foreach (var (id, row) in Rows)
        {
            var recorder = new CommandRecorder();
            var options = new DbContextOptionsBuilder<ProbeContext>()
                .UseSqlServer("Server=unused;Database=unused;Integrated Security=true")
                .IgnoreServiceProviderCacheLimit()
                .AddInterceptors(recorder)
                .Options;
            await using var context = new ProbeContext(options);
            await row.Act(context, ct);

            observations.AddRange(
                recorder.LastRaw.Select(parameter => new TestSupport.ValuePools.ValueObservation(
                    parameter.Value,
                    TestSupport.ValuePools.ValueColumn.FromSqlType(SqlTypeOf(parameter)),
                    $"行 {id}"
                ))
            );
        }

        return observations;
    }

    /// <summary>EF Core が比べる列から決めた SqlParameter の型を、宣言の型の名前で書く</summary>
    private static string SqlTypeOf(DbParameter parameter)
    {
        var sql = (Microsoft.Data.SqlClient.SqlParameter)parameter;
        var size = sql.Size < 0 ? "max" : sql.Size.ToString(CultureInfo.InvariantCulture);

        return sql.SqlDbType switch
        {
            SqlDbType.Decimal => string.Create(
                CultureInfo.InvariantCulture,
                $"decimal({sql.Precision},{sql.Scale})"
            ),
            SqlDbType.VarChar => $"varchar({size})",
            SqlDbType.Char => $"char({size})",
            SqlDbType.NVarChar => $"nvarchar({size})",
            SqlDbType.NChar => $"nchar({size})",
            SqlDbType.DateTime2 => string.Create(
                CultureInfo.InvariantCulture,
                $"datetime2({sql.Scale})"
            ),
            _ => sql.SqlDbType.ToString().ToLowerInvariant(),
        };
    }
}
