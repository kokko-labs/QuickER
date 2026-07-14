using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// パリティスイートを<b>QuickER の SQL Server 実装</b>ランタイム（<see cref="SqlServerRepository{TEntity, TKey}"/> /
/// <see cref="SqlExecutor"/>）で実行する派生。リポジトリ・エグゼキュータは <see cref="ISqlConnectionFactory"/>
/// を渡して直接 new する。
/// </summary>
public sealed class GeneratedRuntimeAdoParityTests(SqlServerContainerFixture fixture)
    : GeneratedRuntimeParityTestsBase(fixture)
{
    /// <summary>接続ファクトリを生成する（コンテナの接続文字列を使う）</summary>
    private SqlConnectionFactory Factory() => new(Fixture.ConnectionString);

    protected override ICustomerRepository CreateCustomerRepository() =>
        new CustomerRepository(Factory());

    protected override IOrderRepository CreateOrderRepository() => new OrderRepository(Factory());

    protected override ISqlExecutor CreateSqlExecutor() => new SqlExecutor(Factory());
}
