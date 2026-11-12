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
/// SaveChanges を変えないこと。
/// </para>
/// </remarks>
public class SqlServerQueryParameterInterceptorTests
{
    /// <summary>表の行（DbContext への操作と、記録したコマンドの値を持つパラメータの期待の型）</summary>
    /// <param name="Act">DbContext への操作</param>
    /// <param name="Expected">値が null でないパラメータの「値 型 精度 スケール 長さ」（コマンドの並び順）。値の型で束縛したパラメータの精度・スケール・長さは、SqlClient が値から決めた数</param>
    private sealed record InterceptRow(
        Func<ProbeContext, CancellationToken, Task> Act,
        string[] Expected
    );

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
                Where("日本", v => x => x.Code == v.Value),
                ["日本 String 0 0 2"]
            ),
            ["char-non-ascii-is-bound-as-unicode"] = new(
                Where("日本", v => x => x.Fixed == v.Value),
                ["日本 String 0 0 2"]
            ),
            ["varchar-like-pattern-non-ascii-is-bound-as-unicode"] = new(
                Where("日", v => x => x.Code != null && x.Code.Contains(v.Value)),
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
            // ---- SaveChanges のコマンドは変えない（列に入らない値は今までどおり列の型で送る） ----
            ["save-changes-is-left-alone"] = new(
                async (c, ct) =>
                {
                    c.Rows.Add(
                        new ProbeRow
                        {
                            Id = 1,
                            Amount = 1.234m,
                            Code = "日本",
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
        parameter.Value = "日本";
        command.Parameters.Add(parameter);

        SqlServerQueryParameterInterceptor.Instance.ReaderExecuting(command, null!, default);

        Describe(parameter).Should().Be("日本 AnsiString 0 0 20");
    }

    /// <summary>パラメータを「値 型 精度 スケール 長さ」で書く</summary>
    private static string Describe(DbParameter parameter) =>
        string.Join(
            ' ',
            Convert.ToString(parameter.Value, CultureInfo.InvariantCulture),
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
