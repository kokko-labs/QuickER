using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedMultiTargetFixture;
using QuickER.Tests.GeneratedMultiTargetFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedMultiTargetFixture.Repositories.SqlServer;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// マルチターゲット構成の keyed DI 解決を、DB 接続を伴わずに検証する（Docker 不要・CI でも常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// keyed 登録（<see cref="ServiceCollectionExtensions"/> 相当の方言別 DI 拡張）で同一契約型
/// <see cref="ICustomerRepository"/> / <see cref="ISqlExecutor"/> を server=SQL Server・local=SQLite に登録し、
/// <c>GetRequiredKeyedService</c> が「別インスタンス・別方言実装」を返すことを確認する。DI コンテナの解決自体は
/// 接続を開かない（接続ファクトリは接続文字列を保持するだけ）ため、Testcontainers を起動できない CI でも実行できる。
/// </para>
/// <para>
/// 実 DB への書き分け・読み分け（相互汚染なし・式木・Include・生 SQL・エンティティ受け渡し）は
/// <see cref="MultiTargetRepositoryRuntimeTests"/>（Docker 依存・SQL Server=Testcontainers）で検証する。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class MultiTargetRepositorySqliteKeyedResolutionTests
{
    /// <summary>接続を開かないダミー接続文字列（解決のみ検証するため実 DB は不要）</summary>
    private const string ServerConnectionString =
        "Server=.;Database=multitarget;Trusted_Connection=True;";
    private const string LocalConnectionString = "Data Source=:memory:";

    /// <summary>両方言を keyed 登録した ServiceProvider を組む</summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqlServerRepositories("server", ServerConnectionString);
        services.AddGeneratedSqliteRepositories("local", LocalConnectionString);

        return services.BuildServiceProvider();
    }

    [Fact(
        DisplayName = "[MultiTarget/CI] keyed 解決で ICustomerRepository が別インスタンス・別方言実装になる"
    )]
    public void KeyedResolution_CustomerRepository_DistinctDialectImplementations()
    {
        using var provider = BuildProvider();

        var server = provider.GetRequiredKeyedService<ICustomerRepository>("server");
        var local = provider.GetRequiredKeyedService<ICustomerRepository>("local");

        server.Should().NotBeSameAs(local);
        server
            .GetType()
            .FullName.Should()
            .Be(
                "QuickER.Tests.GeneratedMultiTargetFixture.Repositories.SqlServer.CustomerRepository"
            );
        local
            .GetType()
            .FullName.Should()
            .Be("QuickER.Tests.GeneratedMultiTargetFixture.Repositories.Sqlite.CustomerRepository");
    }

    [Fact(
        DisplayName = "[MultiTarget/CI] keyed 解決で ISqlExecutor / IOrderRepository も方言別実装になる"
    )]
    public void KeyedResolution_ExecutorAndOrderRepository_DistinctDialectImplementations()
    {
        using var provider = BuildProvider();

        var serverExec = provider.GetRequiredKeyedService<ISqlExecutor>("server");
        var localExec = provider.GetRequiredKeyedService<ISqlExecutor>("local");
        serverExec.Should().NotBeSameAs(localExec);
        serverExec
            .GetType()
            .FullName.Should()
            .Be("QuickER.Tests.GeneratedMultiTargetFixture.Repositories.SqlServer.SqlExecutor");
        localExec
            .GetType()
            .FullName.Should()
            .Be("QuickER.Tests.GeneratedMultiTargetFixture.Repositories.Sqlite.SqlExecutor");

        var serverOrders = provider.GetRequiredKeyedService<IOrderRepository>("server");
        var localOrders = provider.GetRequiredKeyedService<IOrderRepository>("local");
        serverOrders
            .GetType()
            .FullName.Should()
            .Be("QuickER.Tests.GeneratedMultiTargetFixture.Repositories.SqlServer.OrderRepository");
        localOrders
            .GetType()
            .FullName.Should()
            .Be("QuickER.Tests.GeneratedMultiTargetFixture.Repositories.Sqlite.OrderRepository");
    }

    /// <summary>
    /// keyed 登録でも Save フックのレジストリ（<see cref="ISaveHookRegistry"/>）が解決され、登録した
    /// <see cref="ISaveHook{TEntity}"/> が呼び出し面（<see cref="ISaveHookInvoker"/>）として効くことを検証する。
    /// </summary>
    /// <remarks>
    /// レジストリは方言別の keyed 拡張が <c>TryAddScoped</c> で非 keyed に既定登録し、生成リポジトリのファクトリが
    /// <c>provider.GetService&lt;ISaveHookRegistry&gt;()</c> でコンストラクタへ配線する（生成コード参照）。実 DB を開かずに
    /// レジストリ解決とフック発火（Before 短絡・After 呼び出し）だけを確認する＝Docker 不要で CI 常時実行。
    /// </remarks>
    [Fact(DisplayName = "[MultiTarget/CI] keyed 登録でも ISaveHookRegistry が解決されフックが効く")]
    public async Task KeyedResolution_SaveHookRegistry_ResolvesAndFiresHook()
    {
        var services = new ServiceCollection();
        services.AddGeneratedSqlServerRepositories("server", ServerConnectionString);
        services.AddGeneratedSqliteRepositories("local", LocalConnectionString);

        // フックは非 keyed の ISaveHook<TEntity> として登録する（レジストリは IServiceProvider から解決する）
        var hook = new RecordingCustomerHook();
        services.AddSingleton<ISaveHook<CustomerEntity>>(hook);

        using var provider = services.BuildServiceProvider();

        // レジストリは Scoped のためスコープ内で解決する（keyed 拡張が非 keyed に既定登録している）
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetService<ISaveHookRegistry>();
        registry.Should().NotBeNull("keyed 拡張でも ISaveHookRegistry が既定登録される");

        // 登録した ISaveHook<CustomerEntity> が呼び出し面として解決され、実際に発火する
        var invoker = registry!.GetInvoker(typeof(CustomerEntity));
        invoker.Should().NotBeNull("登録済みフックがあれば呼び出し面が解決される");

        var entity = new CustomerEntity();
        var proceed = await invoker!.InvokeBeforeAsync(
            entity,
            SaveOperation.Insert,
            CancellationToken.None
        );

        proceed.Should().BeTrue();
        hook.BeforeCalls.Should().Be(1, "Before フックが発火した");

        // フック未登録の型は完全 no-op（呼び出し面は null）
        registry.GetInvoker(typeof(OrderEntity)).Should().BeNull("未登録の型は no-op");
    }

    /// <summary>Before の発火回数を数えるテスト用フック</summary>
    private sealed class RecordingCustomerHook : ISaveHook<CustomerEntity>
    {
        public int BeforeCalls { get; private set; }

        public Task<bool> BeforeSaveAsync(
            CustomerEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            BeforeCalls++;
            return Task.FromResult(true);
        }
    }
}
