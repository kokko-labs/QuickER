using System;
using System.Linq.Expressions;
using AwesomeAssertions;
using Xunit;
using QueryFixtureCache = QuickER.Tests.GeneratedQueryFixture.QuerySelectorCache;
using QueryFixtureCustomerId = QuickER.Tests.GeneratedQueryFixture.CustomerIdValue;
using QueryFixtureOrder = QuickER.Tests.GeneratedQueryFixture.OrderEntity;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>QuerySelectorCache</c> が「式ツリーの参照同一性キーで compile-once」になっていることを
/// 検証する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// 射影終端（<c>ToProjectionListAsync</c>）は選択式を実行するためにデリゲートへコンパイルする必要があるが、
/// <c>Expression.Compile()</c> は毎回新しい動的メソッドを生成するため、呼び出しごとに実行すると小さな射影
/// クエリの反復では固定費が支配的になる。キャッシュはこれを辞書引きへ置き換える。
/// </para>
/// <para>
/// 前提は「同じ式ツリーインスタンスが毎回渡ってくること」で、それを満たすのが生成側の
/// <c>private static readonly Expression&lt;...&gt; _{メソッド名}Selector</c> への巻き上げ（QueryGenerationTests が固定）。
/// C# は式ツリーをキャッシュしないため、メソッド本体に直書きしたラムダは呼び出しのたびに別インスタンスになり、
/// 参照同一性のキーは一度もヒットしない。ここでは「同一インスタンス＝同一デリゲート／別インスタンス＝別デリゲート」
/// というキャッシュ側の契約だけを固定する。
/// </para>
/// </remarks>
public sealed class QuerySelectorCacheTests
{
    /// <summary>同じ式ツリーインスタンスを渡す限り、コンパイル済みデリゲートは 1 つだけ作られる</summary>
    [Fact(DisplayName = "同一の式ツリーインスタンスには同一のデリゲートを返す")]
    public void GetOrCompile_SameInstance_ReturnsSameDelegate()
    {
        Expression<Func<QueryFixtureOrder, string>> selector = e => e.CustomerId.Value.ToString();

        var first = QueryFixtureCache.GetOrCompile(selector);
        var second = QueryFixtureCache.GetOrCompile(selector);

        second.Should().BeSameAs(first, "参照同一性キーのキャッシュがヒットしなければ意味がない");
    }

    /// <summary>別インスタンスは（内容が同じでも）別のデリゲートになる＝参照同一性キーであることの裏返し</summary>
    [Fact(DisplayName = "別の式ツリーインスタンスには別のデリゲートを返す")]
    public void GetOrCompile_DifferentInstances_ReturnDifferentDelegates()
    {
        Expression<Func<QueryFixtureOrder, string>> first = e => e.CustomerId.Value.ToString();
        Expression<Func<QueryFixtureOrder, string>> second = e => e.CustomerId.Value.ToString();

        // 同一内容でも C# は式ツリーをキャッシュしないため、2 つは別インスタンスでなければならない
        // （この前提が崩れるとテスト自体が無意味になるので明示する）
        ReferenceEquals(first, second).Should().BeFalse();

        QueryFixtureCache
            .GetOrCompile(first)
            .Should()
            .NotBeSameAs(QueryFixtureCache.GetOrCompile(second));
    }

    /// <summary>キャッシュから返したデリゲートは、その場でコンパイルしたものと同じ結果を返す</summary>
    [Fact(DisplayName = "キャッシュ済みデリゲートは選択式どおりに評価する")]
    public void GetOrCompile_ReturnedDelegate_EvaluatesSelector()
    {
        Expression<Func<QueryFixtureOrder, int>> selector = e => e.CustomerId.Value;
        var order = new QueryFixtureOrder { CustomerId = QueryFixtureCustomerId.Create(7) };

        var project = QueryFixtureCache.GetOrCompile(selector);

        project(order).Should().Be(7);
        // 2 回目（キャッシュヒット側）も同じ結果になること
        QueryFixtureCache.GetOrCompile(selector)(order).Should().Be(7);
    }
}
