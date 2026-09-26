using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// PostgreSQL の列型について「ドライバが実際に返す CLR 型」と
/// <see cref="PostgreSqlCSharpTypeMapper"/> の解決結果が一致することを実 DB で固定する統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL の生成コードは EF Core 経路だけ（QuickER 版 Repository の方言は sqlserver / sqlite）なので、
/// 生成 Entity のプロパティ型が実際に使われるのは EF Core です。読み書きまで通して固定します。
/// </para>
/// <para>
/// <c>time with time zone</c> は従来 <c>TimeSpan</c> へ解決しており、Npgsql が返す
/// <c>DateTimeOffset</c> と食い違うため<b>読み取り自体が</b>失敗していました。行の組み立ては全列を
/// まとめて読むため、その列が 1 本あるだけでテーブルが丸ごと使えない状態でした。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlColumnClrTypeIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// <c>time with time zone</c> の解決結果が、ドライバが返す CLR 型と一致する（TM4）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] time with time zone はドライバの実測 CLR 型（DateTimeOffset）と一致する"
    )]
    public async Task Map_TimeWithTimeZone_MatchesDriverClrType()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE tz_columns (
                id integer NOT NULL PRIMARY KEY,
                t_tz time with time zone NOT NULL,
                t_plain time without time zone NOT NULL
            );
            INSERT INTO tz_columns VALUES (1, '10:30:00+07', '10:30:00');
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);
        var declared = import
            .Entities.Single(entity => entity.TableName == "tz_columns")
            .Columns.ToDictionary(column => column.Name, column => column.DataType);

        declared["t_tz"].Should().Be("time with time zone");

        var driverTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT t_tz FROM tz_columns WHERE id = 1";
            await using var reader = await cmd.ExecuteReaderAsync(Ct);

            (await reader.ReadAsync(Ct)).Should().BeTrue();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                driverTypes[reader.GetName(i)] = reader.GetFieldType(i);
            }
        }

        driverTypes["t_tz"].Should().Be<DateTimeOffset>();

        var mapper = new PostgreSqlCSharpTypeMapper();
        mapper.Map(declared["t_tz"]).TypeName.Should().Be("DateTimeOffset");

        // 時間帯なしの time は従来どおり TimeSpan（EF Core が TimeSpan で読み書きできる）
        mapper.Map(declared["t_plain"]).TypeName.Should().Be("TimeSpan");
    }

    /// <summary>
    /// 解決後の型（<c>DateTimeOffset</c>）で、EF Core 経由の読み取りと UTC オフセットの書き込みが通る（TM4）。
    /// </summary>
    [Fact(DisplayName = "[Integration] timetz は DateTimeOffset で読め、UTC オフセットなら書ける")]
    public async Task EfCore_Timetz_ReadsAndWritesWithUtcOffset()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE tz_rw (
                id integer NOT NULL PRIMARY KEY,
                t_tz time with time zone NOT NULL
            );
            INSERT INTO tz_rw VALUES (1, '10:30:00+07');
            """,
            Ct
        );

        await using (var read = new TimetzContext(fixture.ConnectionString))
        {
            var row = await read.Rows.SingleAsync(Ct);
            row.TimeTz.Offset.Should().Be(TimeSpan.FromHours(7));
            row.TimeTz.TimeOfDay.Should().Be(new TimeSpan(10, 30, 0));
        }

        await using (var write = new TimetzContext(fixture.ConnectionString))
        {
            write.Rows.Add(
                new TimetzRow
                {
                    Id = 2,
                    TimeTz = new DateTimeOffset(1, 1, 1, 9, 0, 0, TimeSpan.Zero),
                }
            );
            await write.SaveChangesAsync(Ct);
        }

        (await ReadRawAsync("SELECT t_tz::text FROM tz_rw WHERE id = 2"))
            .Should()
            .Be("09:00:00+00");
    }

    /// <summary>
    /// 非 UTC のオフセットで書くには <c>HasColumnType</c> の明示が要る（既知の制限の裏づけ・TM4）。
    /// </summary>
    /// <remarks>
    /// QuickER の生成コードは <c>HasColumnType</c> を出さない方針（EF Core は既存スキーマへの接続専用）のため、
    /// 既定では EF Core が <c>DateTimeOffset</c> を <c>timestamp with time zone</c> と推論し、Npgsql が
    /// UTC 以外のオフセットを拒否する。必要な利用者は生成される <c>QuickErDbContext</c> の
    /// <c>OnModelCreatingPartial</c> で列型を明示できる——その経路が実際に効くことをここで示す。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] timetz の非 UTC 書き込みは HasColumnType の明示で通る（既定では通らない）"
    )]
    public async Task EfCore_Timetz_NonUtcOffsetRequiresExplicitColumnType()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE tz_offset (
                id integer NOT NULL PRIMARY KEY,
                t_tz time with time zone NOT NULL
            );
            INSERT INTO tz_offset VALUES (1, '00:00:00+00');
            """,
            Ct
        );

        var nonUtc = new DateTimeOffset(1, 1, 1, 3, 4, 5, TimeSpan.FromHours(2));

        // 既定（HasColumnType なし）＝生成コードと同じ条件では通らない
        await using (var plain = new TimetzPlainContext(fixture.ConnectionString))
        {
            var row = await plain.Rows.SingleAsync(Ct);
            row.TimeTz = nonUtc;

            var act = async () => await plain.SaveChangesAsync(Ct);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        // 列型を明示すれば通る（利用者が OnModelCreatingPartial で足せる逃げ道）
        await using (var typed = new TimetzTypedContext(fixture.ConnectionString))
        {
            var row = await typed.Rows.SingleAsync(Ct);
            row.TimeTz = nonUtc;
            await typed.SaveChangesAsync(Ct);
        }

        (await ReadRawAsync("SELECT t_tz::text FROM tz_offset WHERE id = 1"))
            .Should()
            .Be("03:04:05+02");
    }

    /// <summary>
    /// 配列列の解決結果（要素型の配列）で、EF Core 経由の読み書きが通る（TM5）。
    /// </summary>
    /// <remarks>
    /// 従来は配列列が <c>string</c> へ落ちており、読み取りは <c>InvalidCastException</c>、
    /// 書き込みは <c>42804</c>（型不一致）で、その列を持つテーブルがまるごと使えなかった。
    /// <c>HasColumnType</c> は不要（要素型の配列という CLR 型だけで決まる）。
    /// </remarks>
    [Fact(DisplayName = "[Integration] 配列列は要素型の配列で読み書きできる")]
    public async Task EfCore_ArrayColumns_ReadAndWrite()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE arr_rw (
                id integer NOT NULL PRIMARY KEY,
                tags integer[] NOT NULL,
                labels varchar(20)[] NOT NULL,
                codes bigint[] NOT NULL
            );
            INSERT INTO arr_rw VALUES (1, '{1,2,3}', '{a,b}', '{10,20}');
            """,
            Ct
        );

        // 取込の型表記と、マッパーが解決する CLR 型
        await using (var conn = await fixture.OpenConnectionAsync(Ct))
        {
            var import = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);
            var declared = import
                .Entities.Single(entity => entity.TableName == "arr_rw")
                .Columns.ToDictionary(column => column.Name, column => column.DataType);
            var mapper = new PostgreSqlCSharpTypeMapper();

            mapper.Map(declared["tags"]).TypeName.Should().Be("int[]");
            mapper.Map(declared["labels"]).TypeName.Should().Be("string[]");
            mapper.Map(declared["codes"]).TypeName.Should().Be("long[]");

            // 配列は「要素の長さ」を生成コードへ運ばない（[MaxLength] は要素数の意味になるため）
            mapper.Map(declared["labels"]).MaxLength.Should().BeNull();
        }

        await using (var read = new ArrayContext(fixture.ConnectionString))
        {
            var row = await read.Rows.SingleAsync(Ct);
            row.Tags.Should().Equal(1, 2, 3);
            row.Labels.Should().Equal("a", "b");
            row.Codes.Should().Equal(10L, 20L);

            row.Tags = [7, 8];
            row.Labels = ["x", "y", "z"];
            await read.SaveChangesAsync(Ct);
        }

        await using (var reread = new ArrayContext(fixture.ConnectionString))
        {
            var row = await reread.Rows.SingleAsync(Ct);
            row.Tags.Should().Equal(7, 8);
            row.Labels.Should().Equal("x", "y", "z");
        }
    }

    /// <summary>生成 Entity と同じ型（要素型の配列）を持つ行</summary>
    private sealed class ArrayRow
    {
        public int Id { get; set; }
        public int[] Tags { get; set; } = [];
        public string[] Labels { get; set; } = [];
        public long[] Codes { get; set; } = [];
    }

    /// <summary>生成コードと同じ条件（<c>HasColumnType</c> なし）の DbContext</summary>
    private sealed class ArrayContext(string connectionString) : DbContext
    {
        public DbSet<ArrayRow> Rows => Set<ArrayRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql(connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<ArrayRow>();
            row.ToTable("arr_rw");
            row.HasKey(r => r.Id);
            row.Property(r => r.Id).HasColumnName("id");
            row.Property(r => r.Tags).HasColumnName("tags");
            row.Property(r => r.Labels).HasColumnName("labels");
            row.Property(r => r.Codes).HasColumnName("codes");
        }
    }

    /// <summary>生の SQL で 1 つのテキスト値を読む（格納された値そのものを確かめる）</summary>
    private async Task<string> ReadRawAsync(string sql)
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync(Ct);
        return value as string ?? string.Empty;
    }

    /// <summary>生成 Entity と同じ型（<c>DateTimeOffset</c>）を持つ行</summary>
    private sealed class TimetzRow
    {
        public int Id { get; set; }
        public DateTimeOffset TimeTz { get; set; }
    }

    /// <summary>生成コードと同じ条件（<c>HasColumnType</c> なし）の DbContext</summary>
    private sealed class TimetzContext(string connectionString) : DbContext
    {
        public DbSet<TimetzRow> Rows => Set<TimetzRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql(connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<TimetzRow>();
            row.ToTable("tz_rw");
            row.HasKey(r => r.Id);
            row.Property(r => r.Id).HasColumnName("id");
            row.Property(r => r.TimeTz).HasColumnName("t_tz");
        }
    }

    /// <summary>
    /// 既定（<c>HasColumnType</c> なし）の DbContext。
    /// </summary>
    /// <remarks>
    /// <b>列型の有無をコンストラクタ引数で切り替えてはいけない</b>——EF Core はモデルを
    /// <b>コンテキストの型</b>でキャッシュするため、同じ型で条件を変えても最初に作られたモデルが
    /// 使い回され、両方の条件が同じ挙動に見えてしまう。対照は型を分けて作る。
    /// </remarks>
    private sealed class TimetzPlainContext(string connectionString) : DbContext
    {
        public DbSet<TimetzRow> Rows => Set<TimetzRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql(connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<TimetzRow>();
            row.ToTable("tz_offset");
            row.HasKey(r => r.Id);
            row.Property(r => r.Id).HasColumnName("id");
            row.Property(r => r.TimeTz).HasColumnName("t_tz");
        }
    }

    /// <summary>列型を明示した DbContext（利用者の逃げ道の対照。型を分ける理由は上を参照）</summary>
    private sealed class TimetzTypedContext(string connectionString) : DbContext
    {
        public DbSet<TimetzRow> Rows => Set<TimetzRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql(connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<TimetzRow>();
            row.ToTable("tz_offset");
            row.HasKey(r => r.Id);
            row.Property(r => r.Id).HasColumnName("id");
            row.Property(r => r.TimeTz).HasColumnName("t_tz").HasColumnType("time with time zone");
        }
    }
}
