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
}
