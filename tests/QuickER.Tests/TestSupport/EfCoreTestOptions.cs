using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストで DbContext を構成するときの共通の設定（QuickER.Tests と QuickER.Tests.Integration の両方がこのファイルを使う）。
/// </summary>
/// <remarks>
/// <para>
/// EF Core は、構成（プロバイダ・オプション拡張・インターセプターのインスタンスなど）が違うたびに内部のサービスプロバイダを
/// 作ってプロセス全体でキャッシュし、20 を超えると ManyServiceProvidersCreatedWarning を出す。この警告は既定でエラーなので、
/// たまたま 21 個目を作った DbContext が、そのテストと関係なく失敗する。テストはフィクスチャごと・プロバイダごとに構成が違い
/// （フィクスチャごとに値オブジェクトの翻訳の拡張の型が違う）、インターセプターを作るテストもあるので、全テストを流すと
/// 実行順しだいで 20 を超える。
/// </para>
/// <para>
/// 製品の振る舞いは変えず、テストの構成でこの警告だけを無視する。プロバイダを指定する呼び出し（<c>UseSqlite</c> など）の
/// 直後に必ずこれを続ける。<c>EfCoreServiceProviderLimitGuardTests</c> が、テストのソースのすべての呼び出しを検査する。
/// </para>
/// </remarks>
public static class EfCoreTestOptions
{
    /// <summary>内部のサービスプロバイダの数の警告を無視する</summary>
    public static DbContextOptionsBuilder IgnoreServiceProviderCacheLimit(
        this DbContextOptionsBuilder builder
    ) =>
        builder.ConfigureWarnings(warnings =>
            warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
        );

    /// <summary>内部のサービスプロバイダの数の警告を無視する（型つきの構成）</summary>
    public static DbContextOptionsBuilder<TContext> IgnoreServiceProviderCacheLimit<TContext>(
        this DbContextOptionsBuilder<TContext> builder
    )
        where TContext : DbContext =>
        builder.ConfigureWarnings(warnings =>
            warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
        );
}
