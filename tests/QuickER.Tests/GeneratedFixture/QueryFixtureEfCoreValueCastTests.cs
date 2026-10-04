using System.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// EF Core で値オブジェクトの <c>.Value</c> を比べる条件が、列自身のストア型（桁・長さ）のまま SQL になることを検証するテストクラス。
/// </summary>
/// <remarks>
/// 値オブジェクトの列は変換（<c>HasConversion</c>）で生の値の列へ写るため、<c>.Value</c> の参照は変換を経由しないよう
/// 生の値の型へ CAST される。CAST 先が列のストア型でなく CLR 型の既定のマッピング（<c>decimal(18,2)</c>・
/// <c>nvarchar(max)</c>）だと、桁の大きい列は丸められ、長さのある列もインデックスの効かない型で比べられる。
/// SQL の形だけを見るので DB には接続しない（SQL Server プロバイダの <c>ToQueryString</c>）。
/// </remarks>
public class QueryFixtureEfCoreValueCastTests
{
    /// <summary>SQL Server プロバイダで構成したクエリフィクスチャの DbContext（接続はしない）</summary>
    private static QuickErDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<QuickErDbContext>()
                .UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True")
                .Options
        );

    [Fact(
        DisplayName = "EF Core: decimal の値オブジェクトの .Value は列の桁 decimal(10,2) で CAST される"
    )]
    public void DecimalValueObjectValue_IsCastToColumnStoreType()
    {
        using var context = CreateContext();

        var sql = context.Set<OrderEntity>().Where(o => o.Amount.Value > 100.5m).ToQueryString();

        sql.Should().Contain("CAST([o].[amount] AS decimal(10,2))");
        sql.Should().NotContain("decimal(18,2)");
    }

    [Fact(
        DisplayName = "EF Core: 文字列の値オブジェクトの .Value は列の長さ nvarchar(50) で CAST される"
    )]
    public void StringValueObjectValue_IsCastToColumnStoreType()
    {
        using var context = CreateContext();

        var sql = context.Set<CustomerEntity>().Where(c => c.Name.Value == "Bob").ToQueryString();

        sql.Should().Contain("CAST([c].[name] AS nvarchar(50))");
        sql.Should().NotContain("nvarchar(max)");
    }
}
