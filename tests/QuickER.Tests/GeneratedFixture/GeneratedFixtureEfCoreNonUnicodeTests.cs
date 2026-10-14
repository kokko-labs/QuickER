using System.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成物のままの EF Core の DbContext で、SQL Server の varchar の列への比較と LIKE が、Unicode でない文字列で
/// 送られることを検証するテストクラス。
/// </summary>
/// <remarks>
/// GeneratedFixture の <c>customers.name</c> は <c>varchar(50)</c> の値オブジェクトの列。生成される Fluent が
/// <c>IsUnicode(false)</c> を宣言するので、パラメータは <c>varchar</c>、定数は <c>N</c> の付かない文字列になる
/// （Unicode で送ると SQL Server は列の側を nvarchar へ変換してから比べ、索引が効きにくい）。
/// SQL の形だけを見るので DB には接続しない（SQL Server プロバイダの <c>ToQueryString</c>）。
/// </remarks>
public class GeneratedFixtureEfCoreNonUnicodeTests
{
    /// <summary>SQL Server プロバイダで構成した生成物のままの DbContext（接続はしない）</summary>
    private static QuickErDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<QuickErDbContext>()
                .UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True")
                .IgnoreServiceProviderCacheLimit()
                .Options
        );

    [Fact(
        DisplayName = "EF Core（生成物のまま）: varchar の値オブジェクトの列の等値比較は varchar のパラメータで送る"
    )]
    public void Equality_OnVarcharColumn_UsesNonUnicodeParameter()
    {
        using var context = CreateContext();
        var name = NameValue.Create("Alice");

        var sql = context.Set<CustomerEntity>().Where(c => c.Name == name).ToQueryString();

        sql.Should().Contain("DECLARE @name varchar(50)");
        sql.Should().NotContain("nvarchar");
    }

    [Fact(
        DisplayName = "EF Core（生成物のまま）: varchar の値オブジェクトの列の LIKE は、定数もパラメータも Unicode にしない"
    )]
    public void Like_OnVarcharColumn_IsNotUnicode()
    {
        using var context = CreateContext();
        var prefix = "AB";

        var constant = context
            .Set<CustomerEntity>()
            .Where(c => c.Name.StartsWith("AB"))
            .ToQueryString();
        constant.Should().Contain("LIKE 'AB%' ESCAPE '\\'");
        constant.Should().NotContain("N'");

        var parameter = context
            .Set<CustomerEntity>()
            .Where(c => c.Name.StartsWith(prefix))
            .ToQueryString();
        parameter.Should().Contain("DECLARE @prefix varchar(");
        parameter.Should().NotContain("nvarchar");
        parameter.Should().NotContain("N'");
    }
}
