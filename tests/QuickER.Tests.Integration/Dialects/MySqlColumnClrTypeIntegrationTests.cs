using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.MySql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// MySQL の列型について「ドライバが実際に返す CLR 型」と「<see cref="MySqlCSharpTypeMapper"/> の解決結果」が
/// 一致することを実 DB で固定する統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// <c>bit(n&gt;1)</c> と <c>year</c> は従来「未知の型 → string」のフォールバックへ落ちており、生成コードが
/// 読み出し（<c>GetFieldValue&lt;string&gt;</c> 相当）で <c>InvalidCastException</c> になっていた。
/// マッパーの分岐は<b>ドライバの実測値が正</b>なので、一致の検証を実 DB で行う。
/// </para>
/// <para>
/// <c>bit(1)</c> だけは例外で、ドライバは <c>ulong</c> を返すが QuickER は <c>tinyint(1)</c> と対の
/// 真偽値慣習で <c>bool</c> へ寄せる（意図的な非対称。その宣言も併せて固定する）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlColumnClrTypeIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>実測した CLR 型を C# のキーワード表記へ直す（マッパーの TypeName と突き合わせるため）</summary>
    private static readonly IReadOnlyDictionary<Type, string> CSharpKeywords = new Dictionary<
        Type,
        string
    >
    {
        [typeof(bool)] = "bool",
        [typeof(sbyte)] = "sbyte",
        [typeof(byte)] = "byte",
        [typeof(ushort)] = "ushort",
        [typeof(uint)] = "uint",
        [typeof(short)] = "short",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(byte[])] = "byte[]",
        [typeof(DateTime)] = "DateTime",
        [typeof(TimeSpan)] = "TimeSpan",
    };

    /// <summary>
    /// <c>bit(n&gt;1)</c> / <c>year</c> のマッパー解決結果が、ドライバが返す CLR 型と一致する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] bit(n>1) は ulong、year は int（ドライバの実測 CLR 型と一致する）"
    )]
    public async Task Map_BitFieldAndYear_MatchDriverClrTypes()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE clr_probe (
                id int NOT NULL PRIMARY KEY,
                flag bit(1) NOT NULL,
                flags8 bit(8) NOT NULL,
                flags64 bit(64) NOT NULL,
                built year NOT NULL,
                built_opt year NULL
            ) ENGINE=InnoDB;
            INSERT INTO clr_probe (id, flag, flags8, flags64, built, built_opt)
                VALUES (1, b'1', b'10101010', b'1111', 2026, NULL);
            """,
            Ct
        );

        // 1) 図としての取込（COLUMN_TYPE をそのまま型表記に採る）
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new MySqlSchemaImporter().ImportAsync(conn, Ct);
        var entity = import.Entities.Should().ContainSingle().Subject;
        var declaredTypes = entity.Columns.ToDictionary(c => c.Name, c => c.DataType);

        declaredTypes["flags8"].Should().Be("bit(8)");
        declaredTypes["built"].Should().Be("year");

        // 2) ドライバが実際に返す CLR 型
        var driverTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT flag, flags8, flags64, built, built_opt FROM clr_probe WHERE id = 1";
            await using var reader = await cmd.ExecuteReaderAsync(Ct);

            (await reader.ReadAsync(Ct)).Should().BeTrue();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                driverTypes[reader.GetName(i)] = reader.GetFieldType(i);
            }
        }

        driverTypes["flags8"].Should().Be<ulong>();
        driverTypes["built"].Should().Be<int>();

        // 3) マッパーの解決結果がドライバの CLR 型と一致する（bit(1) だけは真偽値慣習で意図的に外れる）
        var mapper = new MySqlCSharpTypeMapper();

        foreach (var columnName in new[] { "flags8", "flags64", "built", "built_opt" })
        {
            mapper
                .Map(declaredTypes[columnName])
                .TypeName.Should()
                .Be(
                    CSharpKeywords[driverTypes[columnName]],
                    $"{columnName}（{declaredTypes[columnName]}）はドライバが返す CLR 型と同じ C# 型へ解決されること"
                );
        }

        // bit(1) はドライバの ulong に対し bool（tinyint(1) と対の真偽値慣習・意図的な非対称）
        driverTypes["flag"].Should().Be<ulong>();
        mapper.Map(declaredTypes["flag"]).TypeName.Should().Be("bool");
    }

    /// <summary>
    /// 取り込んだ図から生成した Entity で、NULL 許容の <c>year</c> / <c>bit(8)</c> が
    /// <c>int?</c> / <c>ulong?</c>（NULL 許容の値型）になる。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] NULL 許容の bit(8) / year は生成 Entity で ulong? / int? になる"
    )]
    public async Task Generate_NullableBitAndYear_EmitNullableValueTypes()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE clr_nullable (
                id int NOT NULL PRIMARY KEY,
                flags8 bit(8) NULL,
                built year NULL
            ) ENGINE=InnoDB;
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new MySqlSchemaImporter().ImportAsync(conn, Ct);
        var diagram = new ErDiagram();

        foreach (var entity in import.Entities)
        {
            diagram.Entities.Add(entity);
        }

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            MySqlCSharpTypeMapper.ResolveColumnTypes(diagram),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCoreRepositories = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = string.Join("\n", result.Files.Select(file => file.Content));

        content.Should().Contain("public ulong? Flags8 { get; set; }");
        content.Should().Contain("public int? Built { get; set; }");
    }

    /// <summary>
    /// 符号なし整数（および <c>mediumint</c>）のマッパー解決結果が、ドライバが返す CLR 型と一致する（TM1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 従来は <c>unsigned</c> 修飾子を落として符号付きの型で解決しており、符号付きの範囲を超える値の
    /// 読み出しが <c>OverflowException</c> になっていた。<c>mediumint</c> は符号の有無に依らず
    /// 「未知の型 → string」のフォールバックへ落ちていた。
    /// </para>
    /// <para>
    /// <c>tinyint(1) unsigned</c> だけは宣言と取込の表記が食い違う——MySQL 8.4 は符号なし tinyint から
    /// 表示幅を落とすため、<c>COLUMN_TYPE</c> は <c>tinyint unsigned</c> を返す（符号付きの
    /// <c>tinyint(1)</c> だけが表示幅を保つ）。ドライバも <c>byte</c> を返すため、
    /// 真偽値慣習の対象外であることが実 DB 側からも裏づけられる。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "[Integration] 符号なし整数と mediumint はドライバの実測 CLR 型と一致する")]
    public async Task Map_UnsignedIntegers_MatchDriverClrTypes()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE unsigned_probe (
                id int NOT NULL PRIMARY KEY,
                ti tinyint NOT NULL,
                ti_u tinyint unsigned NOT NULL,
                ti1_u tinyint(1) unsigned NOT NULL,
                si_u smallint unsigned NOT NULL,
                mi mediumint NOT NULL,
                mi_u mediumint unsigned NOT NULL,
                i_u int unsigned NOT NULL,
                bi_u bigint unsigned NOT NULL,
                de_u decimal(10,2) unsigned NOT NULL
            ) ENGINE=InnoDB;
            INSERT INTO unsigned_probe VALUES
                (1, -128, 255, 1, 65535, -8388608, 16777215, 4294967295, 18446744073709551615, 1.25);
            """,
            Ct
        );

        // 1) 図としての取込（COLUMN_TYPE をそのまま型表記に採る）
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new MySqlSchemaImporter().ImportAsync(conn, Ct);
        var entity = import.Entities.Should().ContainSingle().Subject;
        var declaredTypes = entity.Columns.ToDictionary(c => c.Name, c => c.DataType);

        declaredTypes["i_u"].Should().Be("int unsigned");
        declaredTypes["mi_u"].Should().Be("mediumint unsigned");

        // MySQL 8.4 は符号なし tinyint から表示幅を落とす（符号付きの tinyint(1) だけが表示幅を保つ）
        declaredTypes["ti1_u"].Should().Be("tinyint unsigned");

        // 2) ドライバが実際に返す CLR 型
        var driverTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT ti, ti_u, ti1_u, si_u, mi, mi_u, i_u, bi_u, de_u FROM unsigned_probe WHERE id = 1";
            await using var reader = await cmd.ExecuteReaderAsync(Ct);

            (await reader.ReadAsync(Ct)).Should().BeTrue();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                driverTypes[reader.GetName(i)] = reader.GetFieldType(i);
            }
        }

        driverTypes["ti_u"].Should().Be<byte>();
        driverTypes["ti1_u"].Should().Be<byte>();
        driverTypes["si_u"].Should().Be<ushort>();
        driverTypes["mi"].Should().Be<int>();
        driverTypes["mi_u"].Should().Be<uint>();
        driverTypes["i_u"].Should().Be<uint>();
        driverTypes["bi_u"].Should().Be<ulong>();

        // 3) マッパーの解決結果がドライバの CLR 型と一致する（全列・例外なし）
        var mapper = new MySqlCSharpTypeMapper();

        foreach (var (columnName, driverType) in driverTypes)
        {
            mapper
                .Map(declaredTypes[columnName])
                .TypeName.Should()
                .Be(
                    CSharpKeywords[driverType],
                    $"{columnName}（{declaredTypes[columnName]}）はドライバが返す CLR 型と同じ C# 型へ解決されること"
                );
        }
    }

    /// <summary>
    /// 符号なし整数の上限値が、EF Core（<c>UseMySQL</c>）経由で符号なしの CLR 型として往復する（TM1）。
    /// </summary>
    /// <remarks>
    /// MySQL 方言の生成コードは QuickER 版 Repository を持たない（EF Core 経路のみ）ため、
    /// 生成 Entity のプロパティ型が実際に使われるのはこの経路になる。
    /// 符号付きの型で解決していた従来は、ここが <c>OverflowException</c> で落ちていた。
    /// </remarks>
    [Fact(DisplayName = "[Integration] 符号なし整数の上限値は EF Core（UseMySQL）経由で往復する")]
    public async Task EfCore_UnsignedIntegers_RoundTripMaxValues()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE unsigned_roundtrip (
                id int NOT NULL PRIMARY KEY,
                ti_u tinyint unsigned NOT NULL,
                si_u smallint unsigned NOT NULL,
                mi_u mediumint unsigned NOT NULL,
                i_u int unsigned NOT NULL,
                bi_u bigint unsigned NOT NULL
            ) ENGINE=InnoDB;
            """,
            Ct
        );

        await using (var write = new UnsignedRoundTripContext(fixture.ConnectionString))
        {
            write.Rows.Add(
                new UnsignedRow
                {
                    Id = 1,
                    TinyUnsigned = byte.MaxValue,
                    SmallUnsigned = ushort.MaxValue,
                    MediumUnsigned = 16777215,
                    IntUnsigned = uint.MaxValue,
                    BigUnsigned = ulong.MaxValue,
                }
            );
            await write.SaveChangesAsync(Ct);
        }

        await using var read = new UnsignedRoundTripContext(fixture.ConnectionString);
        var row = await read.Rows.SingleAsync(Ct);

        row.TinyUnsigned.Should().Be(byte.MaxValue);
        row.SmallUnsigned.Should().Be(ushort.MaxValue);
        row.MediumUnsigned.Should().Be(16777215u);
        row.IntUnsigned.Should().Be(uint.MaxValue);
        row.BigUnsigned.Should().Be(ulong.MaxValue);
    }

    /// <summary>
    /// EF Core の往復検証で使う行。プロパティの型は <see cref="MySqlCSharpTypeMapper"/> の解決結果に合わせる
    /// （生成 Entity と同じ型でなければ検証にならない）。
    /// </summary>
    private sealed class UnsignedRow
    {
        public int Id { get; set; }
        public byte TinyUnsigned { get; set; }
        public ushort SmallUnsigned { get; set; }
        public uint MediumUnsigned { get; set; }
        public uint IntUnsigned { get; set; }
        public ulong BigUnsigned { get; set; }
    }

    /// <summary>EF Core（<c>UseMySQL</c>）の最小 DbContext</summary>
    private sealed class UnsignedRoundTripContext(string connectionString) : DbContext
    {
        public DbSet<UnsignedRow> Rows => Set<UnsignedRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseMySQL(connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<UnsignedRow>();
            row.ToTable("unsigned_roundtrip");
            row.HasKey(r => r.Id);
            row.Property(r => r.Id).HasColumnName("id");
            row.Property(r => r.TinyUnsigned).HasColumnName("ti_u");
            row.Property(r => r.SmallUnsigned).HasColumnName("si_u");
            row.Property(r => r.MediumUnsigned).HasColumnName("mi_u");
            row.Property(r => r.IntUnsigned).HasColumnName("i_u");
            row.Property(r => r.BigUnsigned).HasColumnName("bi_u");
        }
    }
}
