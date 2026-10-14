using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// 識別子の長さの上限の宣言が 5 方言で 1 つの正本（<see cref="IdentifierLengthLimits"/>）を指していることを検証するテストクラス
/// </summary>
/// <remarks>
/// 同期のケーパビリティの既定は上限なしなので、プロバイダーが宣言を忘れると、その方言だけ黙って検査が外れる。
/// プロバイダーのケーパビリティ・識別子クラスの <c>Limit</c>・方言名からの引き当ての 3 つが同じ値であることを固定する。
/// </remarks>
public class IdentifierLengthLimitDeclarationTests
{
    public static TheoryData<string> Dialects =>
        ["sqlserver", "postgresql", "mysql", "oracle", "sqlite"];

    [Theory(DisplayName = "5 方言のケーパビリティ・識別子クラス・方言名の表が同じ上限を指す")]
    [MemberData(nameof(Dialects))]
    public void Declarations_PointToSingleSource(string dbms)
    {
        var (provider, identifierLimit) = dbms switch
        {
            "sqlserver" => ((IDatabaseProvider)new SqlServerProvider(), SqlIdentifier.Limit),
            "postgresql" => (new PostgreSqlProvider(), PgIdentifier.Limit),
            "mysql" => (new MySqlProvider(), MySqlIdentifier.Limit),
            "oracle" => (new OracleProvider(), OracleIdentifier.Limit),
            "sqlite" => (new SqliteProvider(), SqliteIdentifier.Limit),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms)),
        };

        var expected = IdentifierLengthLimits.ForDbms(provider.Name);

        provider.Name.Should().Be(dbms);
        provider.SyncCapabilities.IdentifierLengthLimit.Should().BeSameAs(expected);
        identifierLimit.Should().BeSameAs(expected);
    }
}
