using System;
using AwesomeAssertions;
using Xunit;
using QueryFixtureConnectionFactory = QuickER.Tests.GeneratedQueryFixture.SqlConnectionFactory;
using QueryFixtureOrderRepository = QuickER.Tests.GeneratedQueryFixture.OrderRepository;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの共有ガード <c>QueryStringMatchGuard</c>（<c>SqlQuery.Where</c> から呼ばれる）を、
/// DSL 名前付きクエリの生成メソッドも通ることを検証する（DB 接続不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 文字列一致（Contains / StartsWith / EndsWith）の null 引数を全バックエンド共通で <see cref="ArgumentNullException"/>
/// として弾くこと（値オブジェクトの string / 値オブジェクトの多重定義・EF Core 版を含む）と、null でないパターンが通ることは、
/// 実装先の一致（Integration の <c>BackendParityRuntimeTests</c> のシナリオ <c>query-where-expression-and-paging</c>）が
/// QuickER 版・EF Core・インメモリで見る。
/// </para>
/// <para>
/// ここに残すのは、DSL の生成メソッドが Task を返す前に <c>Where</c> で同期的に投げる形（終端の ToListAsync へ届かない）。
/// 実装先の一致は名前付きクエリの生成メソッドを流さない。
/// </para>
/// </remarks>
public sealed class QueryStringMatchGuardTests
{
    /// <summary>接続は張られない前提のダミー接続文字列（Where は同期 throw のため実 DB は不要）</summary>
    private const string DummySqliteConnectionString = "Data Source=:memory:";

    [Fact(
        DisplayName = "DSL の CONTAINS(@param) を使う生成メソッドも共有ガードで null 引数を拒否する"
    )]
    public void GeneratedDslQueryMethod_NullPattern_Throws()
    {
        var orders = new QueryFixtureOrderRepository(
            new QueryFixtureConnectionFactory(DummySqliteConnectionString)
        );

        // SearchMemoAsync は Query().Where(e => (e.Memo != null && e.Memo!.Contains(keyword))) へ展開されるため、
        // 終端の ToListAsync へ到達する前に Where が同期的に throw する（Task は返らない）
        Action search = () => orders.SearchMemoAsync(null!);
        search.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
    }
}
