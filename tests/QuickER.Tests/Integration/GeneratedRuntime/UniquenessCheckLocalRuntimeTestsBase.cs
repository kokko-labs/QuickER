using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 重複事前チェックのランタイムスイートに、<c>Query()</c> を持つ実装先だけが流せる
/// <b>翻訳器の NULL 補償</b>の検証を足す共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 重複事前チェックの共有本体は「値が null の等値比較」を含むため、翻訳器が
/// <c>== null</c> を <c>IS NULL</c> へ補償することが前提になっている。ここではその前提そのものを、
/// 同じフィクスチャの <c>Query()</c> で直接観測する（QuickER 版 ADO の 2 方言・EF Core・インメモリで同一結果）。
/// </para>
/// <para>
/// <b>リモート面には <c>Query()</c> が無い</b>（式木はネットワーク境界を越えられない）ため、
/// リモート派生は親の <see cref="UniquenessCheckRuntimeTestsBase{TOrder}"/> に留まる＝条件スキップではなく
/// サブクラス階層で対象を分ける（スキップ 0 の原則）。
/// </para>
/// </remarks>
/// <typeparam name="TOrder">注文エンティティ型（フィクスチャごとに別型）</typeparam>
public abstract class UniquenessCheckLocalRuntimeTestsBase<TOrder>
    : UniquenessCheckRuntimeTestsBase<TOrder>
    where TOrder : class
{
    /// <summary>memo が「null の変数」と等しい行の注文 ID を返す（<c>Where(o =&gt; o.Memo == missing)</c>）</summary>
    protected abstract Task<IReadOnlyList<int>> OrderIdsWhereMemoEqualsNullVariableAsync();

    /// <summary>memo が「null の変数」と等しくない行の注文 ID を返す（<c>Where(o =&gt; o.Memo != missing)</c>）</summary>
    protected abstract Task<IReadOnlyList<int>> OrderIdsWhereMemoNotEqualsNullVariableAsync();

    /// <summary>memo が指定値と等しいことの否定に一致する行の注文 ID を返す（<c>Where(o =&gt; !(o.Memo == memo))</c>）</summary>
    protected abstract Task<IReadOnlyList<int>> OrderIdsWhereNotMemoEqualsAsync(string memo);

    /// <summary>memo が指定値と等しくない行の注文 ID を返す（<c>Where(o =&gt; o.Memo != memo)</c>）</summary>
    protected abstract Task<IReadOnlyList<int>> OrderIdsWhereMemoNotEqualsAsync(string memo);

    /// <summary>8. null 変数との等値比較は IS NULL 相当（C# / EF Core と同じ意味論）になる</summary>
    /// <remarks>翻訳器の null 補償そのものの検証。実装先で観測結果が一致することを固定する。</remarks>
    [Fact(DisplayName = "[Uniqueness] 8: null 変数との == / != が IS NULL / IS NOT NULL になる")]
    public async Task NullVariableComparison_MatchesNullRows()
    {
        await ResetAndSeedAsync();

        // memo が NULL の行（注文 11）だけに一致する
        (await OrderIdsWhereMemoEqualsNullVariableAsync())
            .Should()
            .Equal(11);

        // 反対に != null は NULL でない行（注文 10）だけに一致する
        (await OrderIdsWhereMemoNotEqualsNullVariableAsync())
            .Should()
            .Equal(10);
    }

    /// <summary>9. 等値の否定 <c>!(==)</c> は <c>!=</c> と同じ行集合を返す（NULL 行を含む）</summary>
    /// <remarks>
    /// 翻訳器が否定を <c>NOT (...)</c> で包むと列側の NULL 補償の外側に出てしまい、NULL 行が落ちて
    /// C#（インメモリ）・EF Core と結果が割れる。ここでは 2 つの書き方が同じ行集合になることを固定する。
    /// </remarks>
    [Fact(DisplayName = "[Uniqueness] 9: !(==) が != と同じ行集合（NULL 行を含む）を返す")]
    public async Task NegatedEqualComparison_MatchesNotEqual()
    {
        await ResetAndSeedAsync();

        // memo が "apple pie" でない行＝memo が NULL の注文 11（NULL 行が落ちれば空になる）
        var negated = await OrderIdsWhereNotMemoEqualsAsync("apple pie");
        negated.Should().Equal(11);

        var notEqual = await OrderIdsWhereMemoNotEqualsAsync("apple pie");
        negated.Should().Equal(notEqual, "!(==) と != は同義");
    }
}
