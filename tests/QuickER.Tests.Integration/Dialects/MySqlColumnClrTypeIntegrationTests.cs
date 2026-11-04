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
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// MySQL の列型を、マッパーが解決した C# 型のまま生成コードと EF Core（<c>UseMySQL</c>）で使えることを実 DB で固定する統合テスト。
/// </summary>
/// <remarks>
/// マッパーの型とドライバの実測の CLR 型の一致は、方言の型の表の実 DB の段（<see cref="MySqlDialectTypeRealDbTests"/>）が確かめる。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlColumnClrTypeIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

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
            optionsBuilder.UseMySQL(connectionString).IgnoreServiceProviderCacheLimit();

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
