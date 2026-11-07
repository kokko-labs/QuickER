using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ取得糖衣のランタイムスイートを<b>EF Core Sqlite 版</b>で実行する派生。
/// </summary>
/// <remarks>
/// EF Core 実装は同じ <c>IncludeNode</c> ツリーを EF Core の <c>Include</c>/<c>ThenInclude</c> 呼び出しへ
/// 組み替えて実行する（QuickER 版の <see cref="IncludeGraphAdoRuntimeTests"/> とは別実装）。
/// 同一のアサーション集合を緑にすることで、同じツリーが両実行器で同じ結果グラフを返すことを固定する。
/// </remarks>
public sealed class IncludeGraphEfCoreRuntimeTests : IncludeGraphSqliteFileRuntimeTestsBase
{
    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlite・接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedEfCoreRepositories → UseSqlite の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlite(ConnectionString).IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    protected override IOrderLineRepository CreateOrderLineRepository() =>
        Provider().GetRequiredService<IOrderLineRepository>();

    /// <summary>
    /// 13. 直前のナビゲーションの逆向きを辿る Include（<c>Include(c =&gt; c.Orders).ThenInclude(o =&gt; o.Customer)</c>）は、EF Core 版が循環として断る。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>共通の基底に置かない理由</b>：この面は EF Core 版にだけある。QuickER 版とインメモリ版は読んだ行どうしを自動でつながないので、
    /// 親への参照はこの形で Include したときだけ埋まり、10 が 3 実装先でその結果を固定している（EF Core は 10 を呼ばない）。
    /// EF Core 版は追跡しないクエリ（<c>AsNoTracking</c>）で問い合わせ、EF Core が読んだ行を自分でつなぐので、この形は要らず、
    /// 書くと実行時に <see cref="InvalidOperationException"/>（"results in a cycle"）で断られる。
    /// </para>
    /// <para>
    /// docs（code-generation の Include の節と IncludeGraph の節）は「直前のナビゲーションの逆向きを辿る枝は QuickER 版とインメモリ版でだけ
    /// 使える」と書いているので、EF Core が将来この形を受けるようになったら、この Fact が赤になって docs の更新を促す。
    /// 断られるのは逆向きだけで、自己参照を同じ向きに辿る多段（<c>Include(n =&gt; n.Nodes).ThenInclude(n =&gt; n.Nodes)</c>）は EF Core でも通る（実測）。
    /// </para>
    /// </remarks>
    [Fact(
        DisplayName = "[IncludeGraph] 13: 直前のナビゲーションの逆向きを辿る Include は循環として断られる (efcore)"
    )]
    public async Task IncludeOfInverseNavigation_IsRejectedAsCycle()
    {
        await ResetAndSeedAsync();

        var key = CustomerIdValue.Create(1);

        Func<Task> read = () =>
            CreateCustomerRepository()
                .Query()
                .Include(customer => customer.Orders)
                    .ThenInclude(order => order.OrderLines)
                .Include(customer => customer.Orders)
                    .ThenInclude(order => order.Customer)
                .GetByIdAsync(key, Ct);

        (await read.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("cycle");
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
