using System.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// EF Core で値オブジェクトの文字列一致（Contains / StartsWith / EndsWith）が組み立てる LIKE の、
/// パターンの型とエスケープの規則を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// パターン・エスケープ文字・string の引数は、列が Unicode でない（<c>IsUnicode(false)</c> や
/// <c>HasColumnType("varchar(…)")</c> で構成した）とき、Unicode でない文字列の型で作る。Unicode の型
/// （<c>N'…'</c>・<c>nvarchar</c>）で作ると、SQL Server は varchar の列の側を nvarchar へ変換してから比べるので、
/// 列のインデックスが使えなくなる。列の格納型そのもの（長さ・固定長）は使わない。固定長の型で作ると
/// パターンが空白で詰められて一致しなくなり、列より長い引数もあり得るため。
/// </para>
/// <para>SQL の形だけを見るので DB には接続しない（SQL Server プロバイダの <c>ToQueryString</c>）。</para>
/// </remarks>
public class QueryFixtureEfCoreLikePatternTests
{
    /// <summary>SQL Server プロバイダの構成（接続はしない）</summary>
    private static DbContextOptions<QuickErDbContext> SqlServerOptions() =>
        new DbContextOptionsBuilder<QuickErDbContext>()
            .UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True")
            .IgnoreServiceProviderCacheLimit()
            .Options;

    /// <summary>顧客の名前の列を Unicode でない列として構成した DbContext（利用者が OnModelCreating で足す形）</summary>
    private sealed class NonUnicodeNameContext(DbContextOptions<QuickErDbContext> options)
        : QuickErDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CustomerEntity>().Property(c => c.Name).IsUnicode(false);
        }
    }

    [Fact(
        DisplayName = "EF Core: Unicode でない列の LIKE は、定数のパターンとエスケープ文字を Unicode でない文字列で書く"
    )]
    public void NonUnicodeColumn_ConstantPattern_IsNotUnicode()
    {
        using var context = new NonUnicodeNameContext(SqlServerOptions());

        var sql = context.Set<CustomerEntity>().Where(c => c.Name.StartsWith("AB")).ToQueryString();

        sql.Should().Contain("LIKE 'AB%' ESCAPE '\\'");
        sql.Should().NotContain("N'");
    }

    [Fact(
        DisplayName = "EF Core: Unicode でない列の LIKE は、パラメータのパターンも Unicode でない文字列で送る"
    )]
    public void NonUnicodeColumn_ParameterPattern_IsNotUnicode()
    {
        using var context = new NonUnicodeNameContext(SqlServerOptions());
        var prefix = "AB";

        var sql = context
            .Set<CustomerEntity>()
            .Where(c => c.Name.StartsWith(prefix))
            .ToQueryString();

        sql.Should().Contain("DECLARE @prefix varchar(");
        sql.Should().NotContain("nvarchar");
        sql.Should().NotContain("N'");
    }

    [Fact(DisplayName = "EF Core: Unicode の列の LIKE は従来どおり Unicode の文字列で書く")]
    public void UnicodeColumn_Pattern_StaysUnicode()
    {
        using var context = new QuickErDbContext(SqlServerOptions());
        var prefix = "AB";

        context
            .Set<CustomerEntity>()
            .Where(c => c.Name.StartsWith("AB"))
            .ToQueryString()
            .Should()
            .Contain("LIKE N'AB%' ESCAPE N'\\'");
        context
            .Set<CustomerEntity>()
            .Where(c => c.Name.StartsWith(prefix))
            .ToQueryString()
            .Should()
            .Contain("DECLARE @prefix nvarchar(4000)");
    }

    [Theory(
        DisplayName = "EF Core: LIKE のエスケープの規則はプロバイダ名で決まり、判別できないプロバイダは ESCAPE を書く"
    )]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "\\", true, true, false)]
    [InlineData("Oracle.EntityFrameworkCore", "\\", false, true, true)]
    [InlineData("MySql.EntityFrameworkCore", "!", false, true, false)]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", "!", false, true, false)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", "\\", false, true, false)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", "\\", false, false, false)]
    [InlineData("FirebirdSql.EntityFrameworkCore.Firebird", "\\", false, true, false)]
    [InlineData("IBM.EntityFrameworkCore", "\\", false, true, false)]
    [InlineData(null, "\\", false, true, false)]
    public void LikeEscapeBehavior_FollowsTheProvider(
        string? providerName,
        string escapeChar,
        bool escapeBracket,
        bool emitEscapeClause,
        bool concatenationSkipsNull
    )
    {
        LikeEscapeBehavior
            .FromProviderName(providerName)
            .Should()
            .Be(
                new LikeEscapeBehavior(escapeChar, escapeBracket, emitEscapeClause)
                {
                    ConcatenationSkipsNull = concatenationSkipsNull,
                }
            );
    }
}
