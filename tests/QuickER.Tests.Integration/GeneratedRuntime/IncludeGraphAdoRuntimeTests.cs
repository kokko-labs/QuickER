using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ取得糖衣のランタイムスイートを<b>QuickER の <c>SqliteRepository</c> 版</b>で実行する派生。
/// </summary>
/// <remarks>
/// SQLite 方言の Include は「親の SELECT を実行してから、その主キーで子テーブルを引く」マルチクエリ
/// （<c>IncludeLoader</c>）で解決する。ツリーが 3 階層になるとこの引き当てが「孫の親キー」まで連なるため、
/// 2 階層で緑でも 3 階層目が空になり得る——ここはその段を通す唯一の経路。
/// </remarks>
public sealed class IncludeGraphAdoRuntimeTests : IncludeGraphSqliteFileRuntimeTestsBase
{
    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ（接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedSqliteRepositories → QuickER の SqliteRepository の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(ConnectionString)
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    protected override IOrderLineRepository CreateOrderLineRepository() =>
        Provider().GetRequiredService<IOrderLineRepository>();

    /// <summary>10. 兄弟分岐の Include（ルートのテーブルへ戻る枝を含む＝EF Core には無い面）</summary>
    [Fact(DisplayName = "[IncludeGraph] 10: 兄弟分岐の Include で両方の ThenInclude が載る (ado)")]
    public Task BranchedInclude_KeepsEveryBranch() => AssertBranchedIncludeKeepsEveryBranchAsync();

    /// <summary>11. IncludeGraph の共有ツリーが後続の ThenInclude で汚れない</summary>
    [Fact(
        DisplayName = "[IncludeGraph] 11: IncludeGraph の共有ツリーは後続の ThenInclude で汚れない (ado)"
    )]
    public Task IncludeGraph_SharedTreeIsNotMutatedByLaterThenInclude() =>
        AssertIncludeGraphSharedTreeIsNotMutatedAsync();

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
