using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EF Core 版の生 SQL（リポジトリの <c>ExecuteSqlAsync</c>）と、Save フックの <c>ISaveHookContext.ExecuteSqlAsync</c> が、
/// DbContext に設定したコマンドタイムアウトに従うことを検証する（SQL Server・Docker）。
/// </summary>
/// <remarks>
/// どちらも DbContext の接続から自分でコマンドを作るため、設定を写さないと接続の既定（30 秒）のまま走る。
/// タイムアウトを 1 秒にした DbContext で 3 秒待つ文を流し、タイムアウトで失敗することを確かめる。
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class EfCoreCommandTimeoutRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>タイムアウト（1 秒）より長く待つ文</summary>
    private const string SlowSql = "WAITFOR DELAY '00:00:03'";

    private readonly List<ServiceProvider> _providers = [];

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        await fixture.ResetSchemaAsync(Ct);
        await fixture.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>コマンドタイムアウトを 1 秒にした EF Core 版リポジトリの DI を組む</summary>
    private ServiceProvider BuildProvider(object? hook = null)
    {
        var services = new ServiceCollection().AddGeneratedEfCoreRepositories(options =>
            options.UseSqlServer(fixture.ConnectionString, sql => sql.CommandTimeout(1))
        );

        if (hook is not null)
        {
            services.AddSaveHook(hook);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    [Fact(DisplayName = "EF Core: 生 SQL は DbContext のコマンドタイムアウトで打ち切られる")]
    public async Task RawSql_FollowsDbContextCommandTimeout()
    {
        var documents = BuildProvider().GetRequiredService<IDocumentRepository>();

        var act = () => documents.ExecuteSqlAsync(SlowSql, cancellationToken: Ct);

        (await act.Should().ThrowAsync<SqlException>())
            .Which.Number.Should()
            .Be(-2, "タイムアウト");
    }

    [Fact(
        DisplayName = "EF Core: Save フックの ExecuteSqlAsync は DbContext のコマンドタイムアウトで打ち切られる"
    )]
    public async Task SaveHookSql_FollowsDbContextCommandTimeout()
    {
        var hook = new RecordingHook<DocumentEntity>([], document => document.DocumentId)
        {
            AfterAction = (_, _, context) =>
                context.ExecuteSqlAsync(SlowSql, cancellationToken: Ct),
        };
        var documents = BuildProvider(hook).GetRequiredService<IDocumentRepository>();
        var document = new DocumentEntity
        {
            DocumentId = 1,
            Title = "alpha",
            Thumb = [],
        };
        document.MarkAdded();

        var act = () => documents.SaveAsync(document, cancellationToken: Ct);

        (await act.Should().ThrowAsync<SqlException>())
            .Which.Number.Should()
            .Be(-2, "タイムアウト");
        (await documents.GetByIdAsync(1, Ct))
            .Should()
            .BeNull("フックの失敗で保存はロールバックされる");
    }
}
