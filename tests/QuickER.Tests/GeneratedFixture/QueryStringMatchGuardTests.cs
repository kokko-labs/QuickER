using System;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using QueryFixtureConnectionFactory = QuickER.Tests.GeneratedQueryFixture.SqlConnectionFactory;
using QueryFixtureOrderRepository = QuickER.Tests.GeneratedQueryFixture.OrderRepository;
using SqliteEfCoreCustomerRepository = QuickER.Tests.GeneratedSqliteFixture.EfCoreCustomerRepository;
using SqliteQuickErDbContext = QuickER.Tests.GeneratedSqliteFixture.QuickErDbContext;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの共有ガード <c>QueryStringMatchGuard</c>（<c>SqlQuery.Where</c> から呼ばれる）が、
/// 文字列一致（Contains / StartsWith / EndsWith）の null 引数を<b>全バックエンド共通で</b>
/// <see cref="ArgumentNullException"/> として即座に弾くことを検証する（DB 接続不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 修正前は同じ式が経路ごとに 6 通りに割れていた（QuickER 版 ADO＝パターン <c>"%%"</c> で全件一致・
/// EF Core＝素の string で 0 件／VO 列で全件一致・インメモリ＝素の string で例外／VO 列で 0 件）。
/// 全バックエンドと DSL 名前付きクエリが共有の <c>SqlQuery.Where</c> を必ず通ることを利用し、
/// そこ 1 箇所で fail-fast へ統一した。
/// </para>
/// <para>
/// 終端メソッド（ToListAsync 等）を待たず <c>Where</c> の時点で同期的に throw するため、いずれのケースも
/// 実 DB を必要としない。EF Core 経路は「ガードが弾くまで DbContext が 1 度も生成されないこと」を、
/// 生成すると例外になるファクトリ（<see cref="ThrowingContextFactory"/>）で同時に証明する。
/// </para>
/// </remarks>
public sealed class QueryStringMatchGuardTests
{
    /// <summary>接続は張られない前提のダミー接続文字列（Where は同期 throw のため実 DB は不要）</summary>
    private const string DummySqlServerConnectionString =
        "Server=(local);Database=QuickErGuard;Trusted_Connection=True;";

    private const string DummySqliteConnectionString = "Data Source=:memory:";

    /// <summary>ガードが弾くまで DbContext が生成されないことを証明するファクトリ（生成されたら失敗）</summary>
    private sealed class ThrowingContextFactory : IDbContextFactory<SqliteQuickErDbContext>
    {
        public SqliteQuickErDbContext CreateDbContext() =>
            throw new InvalidOperationException(
                "ガードが述語を拒否する前に DbContext が生成された"
            );
    }

    // ===== (1) QuickER 版 ADO（SQL Server 方言）＋ VO 列: string / VO 両オーバーロード =====

    [Fact(
        DisplayName = "VO 列の Contains/StartsWith/EndsWith は null 引数を Where 時点で拒否する（string オーバーロード）"
    )]
    public void ValueObjectColumn_StringOverloadNull_ThrowsAtWhere()
    {
        var customers = new CustomerRepository(
            new SqlConnectionFactory(DummySqlServerConnectionString)
        );

        var contains = () => customers.Query().Where(c => c.Name.Contains((string)null!));
        contains.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");

        var startsWith = () => customers.Query().Where(c => c.Name.StartsWith((string)null!));
        startsWith.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");

        var endsWith = () => customers.Query().Where(c => c.Name.EndsWith((string)null!));
        endsWith.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
    }

    [Fact(
        DisplayName = "VO 列の Contains/StartsWith/EndsWith は null 引数を Where 時点で拒否する（VO オーバーロード）"
    )]
    public void ValueObjectColumn_ValueObjectOverloadNull_ThrowsAtWhere()
    {
        var customers = new CustomerRepository(
            new SqlConnectionFactory(DummySqlServerConnectionString)
        );

        var contains = () => customers.Query().Where(c => c.Name.Contains((NameValue)null!));
        contains.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");

        var startsWith = () => customers.Query().Where(c => c.Name.StartsWith((NameValue)null!));
        startsWith.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");

        var endsWith = () => customers.Query().Where(c => c.Name.EndsWith((NameValue)null!));
        endsWith.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
    }

    [Fact(
        DisplayName = "非 null の文字列一致は従来どおり Where を通る（ガードは null のみを弾く）"
    )]
    public void NonNullPattern_PassesThrough()
    {
        var customers = new CustomerRepository(
            new SqlConnectionFactory(DummySqlServerConnectionString)
        );

        var literal = () => customers.Query().Where(c => c.Name.Contains("A"));
        literal.Should().NotThrow();

        // クロージャ捕捉の値引数（式ツリーでは MemberExpression）も同じく通る
        var keyword = "A";
        var captured = () => customers.Query().Where(c => c.Name.StartsWith(keyword));
        captured.Should().NotThrow();

        var valueObject = () =>
            customers.Query().Where(c => c.Name.EndsWith(NameValue.Create("A")));
        valueObject.Should().NotThrow();
    }

    // ===== (2) EF Core 経路 =====

    [Fact(
        DisplayName = "EF Core 版リポジトリでも null 引数は Where 時点で拒否する（DbContext は生成されない）"
    )]
    public void EfCoreRepository_NullPattern_ThrowsAtWhere()
    {
        var customers = new SqliteEfCoreCustomerRepository(new ThrowingContextFactory());

        var contains = () => customers.Query().Where(c => c.Name.Contains((string)null!));
        contains.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
    }

    // ===== (3) DSL 名前付きクエリ経路 =====

    [Fact(
        DisplayName = "DSL の CONTAINS(@param) を使う生成メソッドも共有ガードで null 引数を拒否する"
    )]
    public void GeneratedDslQueryMethod_NullPattern_Throws()
    {
        var orders = new QueryFixtureOrderRepository(
            new QueryFixtureConnectionFactory(DummySqliteConnectionString)
        );

        // SearchMemoAsync は Query().Where(e => e.Memo!.Contains(keyword)) へ展開されるため、
        // 終端の ToListAsync へ到達する前に Where が同期的に throw する（Task は返らない）
        Action search = () => orders.SearchMemoAsync(null!);
        search.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
    }
}
