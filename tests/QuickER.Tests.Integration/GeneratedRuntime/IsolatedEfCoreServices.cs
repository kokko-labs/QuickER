using System;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// インターセプターを使う EF Core のテストに、EF Core の共有の内部サービスプロバイダのキャッシュを使わせない。
/// </summary>
/// <remarks>
/// <para>
/// EF Core はオプション（インターセプターのインスタンスを含む）が違うたびに内部のサービスプロバイダを作ってプロセス全体で
/// キャッシュし、20 を超えると、たまたま 21 個目を作った DbContext を ManyServiceProvidersCreatedWarning（既定でエラー）で
/// 失敗させる。テストごとにインターセプターを作るテストが増えるほど、関係の無いテストが順序しだいで落ちる。
/// </para>
/// <para>
/// ここで作る DbContext は <c>UseInternalServiceProvider</c> で自前の内部プロバイダを使い、インターセプターはそのプロバイダに
/// <see cref="IInterceptor"/> のサービスとして登録する。キャッシュには入らないので、上限の数に数えられない。内部プロバイダは
/// インターセプターのインスタンスごとに 1 つ作って使い回す。
/// </para>
/// </remarks>
public static class IsolatedEfCoreServices
{
    private static readonly ConcurrentDictionary<
        (string, IInterceptor),
        IServiceProvider
    > Providers = new();

    /// <summary>SQLite を、<paramref name="interceptor"/> を登録した自前の内部プロバイダで使う</summary>
    public static DbContextOptionsBuilder UseSqliteWith(
        this DbContextOptionsBuilder builder,
        string connectionString,
        IInterceptor interceptor
    ) =>
        builder
            .UseSqlite(connectionString)
            .IgnoreServiceProviderCacheLimit()
            .UseInternalServiceProvider(
                Providers.GetOrAdd(
                    ("sqlite", interceptor),
                    key =>
                        new ServiceCollection()
                            .AddEntityFrameworkSqlite()
                            .AddSingleton(key.Item2)
                            .BuildServiceProvider()
                )
            );

    /// <summary>SQL Server を、<paramref name="interceptor"/> を登録した自前の内部プロバイダで使う</summary>
    public static DbContextOptionsBuilder UseSqlServerWith(
        this DbContextOptionsBuilder builder,
        string connectionString,
        IInterceptor interceptor
    ) =>
        builder
            .UseSqlServer(connectionString)
            .IgnoreServiceProviderCacheLimit()
            .UseInternalServiceProvider(
                Providers.GetOrAdd(
                    ("sqlserver", interceptor),
                    key =>
                        new ServiceCollection()
                            .AddEntityFrameworkSqlServer()
                            .AddSingleton(key.Item2)
                            .BuildServiceProvider()
                )
            );
}
