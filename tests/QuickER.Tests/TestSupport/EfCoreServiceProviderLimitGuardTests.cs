using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストの DbContext が、内部のサービスプロバイダの数の警告で失敗しないことを固定するテストクラス
/// （<see cref="EfCoreTestOptions"/>）。
/// </summary>
/// <remarks>
/// テストのソースのプロバイダの指定の直後に共通の設定が続くことは、<c>SourceRuleTests</c> の規則
/// <c>ef-core-provider-cache-limit</c> へ移した。ここに残すのは、共通の設定が実際に効くことの実行の確認。
/// </remarks>
public class EfCoreServiceProviderLimitGuardTests
{
    /// <summary>
    /// 何もしないインターセプター。具体化（materialization）のインターセプターはシングルトンのインターセプターで、
    /// インスタンスごとに内部のサービスプロバイダが別になる（コマンドのインターセプターは別にならない）。
    /// </summary>
    private sealed class NoopInterceptor : IMaterializationInterceptor;

    [Fact(
        DisplayName = "EF Core: 内部のサービスプロバイダを 21 個以上作っても、共通の設定を通した DbContext は失敗しない"
    )]
    public void ManyServiceProviders_DoNotFailTheContext()
    {
        for (var i = 0; i < 25; i++)
        {
            var options = new DbContextOptionsBuilder()
                .UseSqlite("Data Source=:memory:")
                .IgnoreServiceProviderCacheLimit()
                .AddInterceptors(new NoopInterceptor())
                .Options;
            using var context = new DbContext(options);

            var act = () => context.Model;

            act.Should().NotThrow($"{i + 1} 個目の内部のサービスプロバイダ");
        }
    }
}
