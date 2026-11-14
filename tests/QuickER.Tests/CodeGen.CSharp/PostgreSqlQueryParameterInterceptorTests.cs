using System;
using System.Collections;
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
using Npgsql;
using QuickER.Runtime.EntityFrameworkCore;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core のランタイムの <see cref="PostgreSqlQueryParameterInterceptor"/> が、PostgreSQL のクエリのコマンドで、値が列の型に入らない
/// パラメータだけを値の型で束縛し直し、入る値・書き込みのコマンド（SaveChanges・ExecuteUpdate の SET）・ほかのプロバイダの接続には
/// 触れないことを、行の表で検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 各行は、生成の Fluent と同じ構成（<c>date</c>・<c>time</c> の列に <c>HasColumnType</c>）の DbContext へ PostgreSQL（Npgsql）の
/// プロバイダで LINQ を流し、インターセプターの後ろに置いた記録役が、接続を開かずにコマンドを止めてパラメータを読む（DB は使わない）。
/// パラメータの型は EF Core の Npgsql のプロバイダが比べる列から決めたもので、記録役はインターセプターが直した後の型を見る。
/// </para>
/// <para>
/// 答えが C# と同じになることは、簡易 DSL の総当たり（<c>Dsl/PostgreSql</c>）と式の翻訳の一致の表（<c>table:TranslatorParityPredicates</c>）が、
/// 範囲の外の値の書き込みを DB が断ることは方言の型の表の実 DB の段（<c>Dialect/TypesRealDb</c>）が、実 PostgreSQL で確かめる。
/// この表が持つのは、どのコマンドのどのパラメータを直し、どれを直さないか（直すと time の列へ範囲の外の値を interval で書き、
/// PostgreSQL が日の部分を黙って落とす）。
/// </para>
/// <para>
/// EF Core 10 は ExecuteDelete のコマンドも出どころを <see cref="CommandSource.ExecuteUpdate"/> として渡す（実測）ので、time の枝は
/// LinqQuery のコマンドだけを直し、ExecuteDelete の条件の範囲の外の値は DB が断る（docs の TimeSpan の既知の割り切り）。
/// </para>
/// </remarks>
public class PostgreSqlQueryParameterInterceptorTests
{
    /// <summary>表の行（DbContext への操作と、記録したコマンドの値を持つパラメータの期待）</summary>
    /// <param name="Act">DbContext への操作</param>
    /// <param name="Expected">値が null でないパラメータの「値 DbType Npgsql の型の名前」（コマンドの並び順）</param>
    private sealed record InterceptRow(
        Func<ProbeContext, CancellationToken, Task> Act,
        string[] Expected
    );

    private static readonly DateTime Midnight = new(2026, 1, 2);

    private static readonly DateTime Noon = new(2026, 1, 2, 12, 0, 0);

    private static readonly TimeSpan BeyondDay = new(1, 1, 0, 0);

    /// <summary>行（Id → 行）</summary>
    /// <remarks>
    /// 値は閉包のフィールド（<see cref="Box{T}"/>）から読む。生成コードの名前付きクエリと同じく EF Core はパラメータにする
    /// （ラムダの中の定数や静的フィールドは SQL のリテラルになり、パラメータを作らない）。
    /// </remarks>
    private static IReadOnlyDictionary<string, InterceptRow> Rows { get; } =
        new Dictionary<string, InterceptRow>(StringComparer.Ordinal)
        {
            // ---- date の列：0 時ちょうどは列の型のまま、時刻つきは timestamp で束縛する ----
            ["date-midnight-keeps-column-type"] = new(
                Where(Midnight, v => x => x.Day == v.Value),
                ["2026-01-02T00:00:00 Date date"]
            ),
            ["date-time-of-day-is-bound-as-timestamp"] = new(
                Where(Noon, v => x => x.Day < v.Value),
                ["2026-01-02T12:00:00 DateTime2 timestamp without time zone"]
            ),
            ["date-execute-delete-condition-is-bound-as-timestamp"] = new(
                (c, ct) =>
                {
                    var v = new Box<DateTime>(Noon);
                    return c.Rows.Where(x => x.Day == v.Value).ExecuteDeleteAsync(ct);
                },
                ["2026-01-02T12:00:00 DateTime2 timestamp without time zone"]
            ),
            ["date-save-changes-is-left-alone"] = new(
                (c, ct) => Save(c, new ProbeRow { Id = 1, Day = Noon }, ct),
                [
                    "1 Int64 bigint",
                    "00:00:00 Time time without time zone",
                    "2026-01-02T12:00:00 Date date",
                ]
            ),
            // ---- time の列：0〜24:00:00 は列の型のまま、範囲の外の値は値から決まる型（interval）で束縛する ----
            ["time-in-day-keeps-column-type"] = new(
                Where(TimeSpan.FromHours(10), v => x => x.Clock == v.Value),
                ["10:00:00 Time time without time zone"]
            ),
            ["time-24-hours-keeps-column-type"] = new(
                Where(TimeSpan.FromDays(1), v => x => x.Clock <= v.Value),
                ["1.00:00:00 Time time without time zone"]
            ),
            ["time-beyond-day-is-bound-as-interval"] = new(
                Where(BeyondDay, v => x => x.Clock < v.Value),
                ["1.01:00:00 Object interval"]
            ),
            ["time-negative-is-bound-as-interval"] = new(
                Where(TimeSpan.FromHours(-1), v => x => x.Clock != v.Value),
                ["-01:00:00 Object interval"]
            ),
            ["time-just-beyond-24-hours-is-bound-as-interval"] = new(
                Where(TimeSpan.FromDays(1) + TimeSpan.FromTicks(10), v => x => x.Clock > v.Value),
                ["1.00:00:00.0000010 Object interval"]
            ),
            // ---- IN のリストは 1 つの配列のパラメータ：範囲の外の要素を含むときだけ値から決まる型にする ----
            ["time-in-list-in-day-keeps-column-type"] = new(
                Where(
                    new List<TimeSpan> { TimeSpan.FromHours(1), TimeSpan.FromHours(2) },
                    v => x => v.Value.Contains(x.Clock)
                ),
                ["[01:00:00, 02:00:00] Object time without time zone[]"]
            ),
            ["time-in-list-with-beyond-element-is-bound-as-interval-array"] = new(
                Where(
                    new List<TimeSpan> { TimeSpan.FromHours(1), BeyondDay },
                    v => x => v.Value.Contains(x.Clock)
                ),
                ["[01:00:00, 1.01:00:00] Object interval[]"]
            ),
            // ---- 書き込みのコマンドの time のパラメータは直さない（直すと範囲の外の値を interval で書き、DB が日の部分を黙って落とす） ----
            ["time-save-changes-is-left-alone"] = new(
                (c, ct) =>
                    Save(
                        c,
                        new ProbeRow
                        {
                            Id = 1,
                            Day = Midnight,
                            Clock = BeyondDay,
                        },
                        ct
                    ),
                [
                    "1 Int64 bigint",
                    "1.01:00:00 Time time without time zone",
                    "2026-01-02T00:00:00 Date date",
                ]
            ),
            ["time-execute-update-set-is-left-alone"] = new(
                (c, ct) =>
                {
                    var v = new Box<TimeSpan>(BeyondDay);
                    return c
                        .Rows.Where(x => x.Id == 1)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Clock, v.Value), ct);
                },
                ["1.01:00:00 Time time without time zone"]
            ),
            // EF Core は ExecuteDelete のコマンドも ExecuteUpdate として渡すので、その条件も直さない（DB が範囲の外として断る）
            ["time-execute-delete-condition-is-left-alone"] = new(
                (c, ct) =>
                {
                    var v = new Box<TimeSpan>(BeyondDay);
                    return c.Rows.Where(x => x.Clock < v.Value).ExecuteDeleteAsync(ct);
                },
                ["1.01:00:00 Time time without time zone"]
            ),
        };

    /// <summary>値を閉包のフィールドから読む述語で、一覧を読む操作を作る</summary>
    private static Func<ProbeContext, CancellationToken, Task> Where<T>(
        T value,
        Func<Box<T>, Expression<Func<ProbeRow, bool>>> predicate
    ) => (c, ct) => c.Rows.Where(predicate(new Box<T>(value))).ToListAsync(ct);

    /// <summary>行を 1 つ挿入する（記録役は空の結果を返すので、行数の照合で失敗してよい。見るのは記録したパラメータだけ）</summary>
    private static async Task Save(ProbeContext context, ProbeRow row, CancellationToken ct)
    {
        context.Rows.Add(row);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { }
    }

    /// <summary>閉包の代わり（EF Core はフィールドの値をパラメータにする）</summary>
    private sealed class Box<T>(T value)
    {
        public readonly T Value = value;
    }

    /// <summary>表の行の Id</summary>
    public static TheoryData<string> Ids() => [.. Rows.Keys];

    [Theory(
        DisplayName = "PostgreSQL のクエリのパラメータは、値が列の型に入らないときだけ値の型で束縛し、書き込みのコマンドには触れない"
    )]
    [MemberData(nameof(Ids))]
    public async Task Parameters_AreRetypedOnlyWhenTheValueDoesNotFit(string id)
    {
        var row = Rows[id];
        var recorder = new CommandRecorder();
        var options = new DbContextOptionsBuilder<ProbeContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .IgnoreServiceProviderCacheLimit()
            .AddInterceptors(PostgreSqlQueryParameterInterceptor.Instance, recorder)
            .Options;
        await using var context = new ProbeContext(options);

        await row.Act(context, TestContext.Current.CancellationToken);

        recorder.LastParameters.Should().Equal(row.Expected);
    }

    [Fact(DisplayName = "PostgreSQL でない接続のコマンドには触れない")]
    public void OtherConnection_IsLeftUntouched()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var command = connection.CreateCommand();
        var date = command.CreateParameter();
        date.DbType = DbType.Date;
        date.Value = Noon;
        command.Parameters.Add(date);
        var time = command.CreateParameter();
        time.DbType = DbType.Time;
        time.Value = BeyondDay;
        command.Parameters.Add(time);

        PostgreSqlQueryParameterInterceptor.Instance.ReaderExecuting(command, null!, default);

        command
            .Parameters.Cast<DbParameter>()
            .Select(parameter => parameter.DbType)
            .Should()
            .Equal(DbType.Date, DbType.Time);
    }

    /// <summary>パラメータを「値 DbType Npgsql の型の名前」で書く（リストは要素を並べる）</summary>
    private static string Describe(DbParameter parameter) =>
        string.Join(
            ' ',
            Show(parameter.Value),
            parameter.DbType,
            parameter is NpgsqlParameter { DataTypeName: { Length: > 0 } name } ? name : "-"
        );

    private static string Show(object? value) =>
        value switch
        {
            DateTime d => d.ToString("s", CultureInfo.InvariantCulture),
            TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
            IEnumerable items and not string => "["
                + string.Join(", ", items.Cast<object?>().Select(Show))
                + "]",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    /// <summary>生成の Fluent と同じ構成の行</summary>
    public sealed class ProbeRow
    {
        public long Id { get; set; }

        public DateTime Day { get; set; }

        public TimeSpan Clock { get; set; }
    }

    /// <summary>生成の Fluent と同じ構成の DbContext（PostgreSQL の型マッパーが EF Core に教える列の型を付ける）</summary>
    public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ProbeRow>(entity =>
            {
                entity.ToTable("probes");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
                entity.Property(e => e.Day).HasColumnName("day").HasColumnType("date");
                entity.Property(e => e.Clock).HasColumnName("clock").HasColumnType("time");
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

        private void Record(DbCommand command) =>
            LastParameters = [
                .. command
                    .Parameters.Cast<DbParameter>()
                    .Where(parameter => parameter.Value is not (null or DBNull))
                    .Select(Describe),
            ];
    }
}
