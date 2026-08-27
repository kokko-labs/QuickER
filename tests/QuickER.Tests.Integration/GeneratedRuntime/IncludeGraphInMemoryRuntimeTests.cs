using System.Threading.Tasks;
using QuickER.Tests.GeneratedQueryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ取得糖衣のランタイムスイートを<b>インメモリ Repository</b>（<see cref="InMemoryDataStore"/> 共有）で
/// 実行する派生。実 DB を使わないため Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// インメモリ実行器は SQL を持たず、ストアの行を外部キーで突き合わせてナビゲーションを組み立てる
/// （＝3 実装先の中でもっとも別物）。ここが同じ結果グラフを返すことで、Include ツリーの意味論が
/// 「SQL の書き方」ではなく仕様として揃っていることが分かる。
/// </remarks>
public sealed class IncludeGraphInMemoryRuntimeTests : IncludeGraphQueryFixtureRuntimeTestsBase
{
    /// <summary>全リポジトリで共有するインメモリストア（実 DB のファイルに相当する永続点）</summary>
    private readonly InMemoryDataStore _store = new();

    protected override Task ResetStorageAsync()
    {
        _store.Clear();

        return Task.CompletedTask;
    }

    protected override ICustomerRepository CreateCustomerRepository() =>
        new InMemoryCustomerRepository(_store);

    protected override IOrderRepository CreateOrderRepository() =>
        new InMemoryOrderRepository(_store);

    protected override IOrderLineRepository CreateOrderLineRepository() =>
        new InMemoryOrderLineRepository(_store);

    /// <summary>10. 兄弟分岐の Include（ルートのテーブルへ戻る枝を含む＝EF Core には無い面）</summary>
    [Fact(
        DisplayName = "[IncludeGraph] 10: 兄弟分岐の Include で両方の ThenInclude が載る (inmemory)"
    )]
    public Task BranchedInclude_KeepsEveryBranch() => AssertBranchedIncludeKeepsEveryBranchAsync();

    /// <summary>11. IncludeGraph の共有ツリーが後続の ThenInclude で汚れない</summary>
    [Fact(
        DisplayName = "[IncludeGraph] 11: IncludeGraph の共有ツリーは後続の ThenInclude で汚れない (inmemory)"
    )]
    public Task IncludeGraph_SharedTreeIsNotMutatedByLaterThenInclude() =>
        AssertIncludeGraphSharedTreeIsNotMutatedAsync();
}
