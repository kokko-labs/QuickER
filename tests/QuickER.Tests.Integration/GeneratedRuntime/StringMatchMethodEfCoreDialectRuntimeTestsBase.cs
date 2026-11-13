using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値オブジェクトの文字列一致のシナリオを、EF Core 版 Repository で実コンテナの PostgreSQL・MySQL・Oracle へ流す派生の共通基底
/// （Testcontainers・Docker 依存）。
/// </summary>
/// <remarks>
/// クエリフィクスチャの図の列は int・nvarchar(50)・decimal(10,2) だけで、生成の Fluent（<c>HasMaxLength</c>・<c>HasPrecision</c>・
/// 値オブジェクトの変換）は方言に依らない。チェックイン済みの生成物をそのまま使い、スキーマは図を方言切替
/// （<see cref="DiagramTypeConverter"/>）でその方言へ写した DDL で作る（図に写せない列は無い。あれば例外にする）。
/// </remarks>
public abstract class StringMatchMethodEfCoreDialectRuntimeTestsBase
    : StringMatchMethodQueryFixtureRuntimeTestsBase,
        IDisposable
{
    private ServiceProvider? _provider;

    /// <summary>写す先の方言のプロバイダ</summary>
    protected abstract IDatabaseProvider Provider { get; }

    /// <summary>コンテナが使えなければスキップする</summary>
    protected abstract void SkipUnlessAvailable();

    /// <summary>スキーマを空にして DDL を当てる</summary>
    protected abstract Task ResetAndApplyDdlAsync(string ddl);

    /// <summary>EF Core のプロバイダを指定する</summary>
    protected abstract void Configure(DbContextOptionsBuilder options);

    /// <inheritdoc />
    protected override async Task ResetStorageAsync()
    {
        SkipUnlessAvailable();
        var diagram = QueryFixtureDefinition.Build();
        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerProvider().TypeCatalog,
            Provider.TypeCatalog
        );

        if (plan.Unconverted.Count > 0)
        {
            throw new InvalidOperationException(
                "クエリフィクスチャの図に写せない列がある: "
                    + string.Join(
                        ", ",
                        plan.Unconverted.Select(c => $"{c.TableName}.{c.ColumnName}")
                    )
            );
        }

        DiagramTypeConverter.Apply(diagram, plan);
        await ResetAndApplyDdlAsync(Provider.DdlGenerator.Build(diagram));
    }

    /// <inheritdoc />
    protected override ICustomerRepository CreateCustomerRepository() =>
        (
            _provider ??= new ServiceCollection()
                .AddGeneratedEfCoreRepositories(options =>
                {
                    Configure(options);
                    options.IgnoreServiceProviderCacheLimit();
                })
                .BuildServiceProvider()
        ).GetRequiredService<ICustomerRepository>();

    /// <summary>DI コンテナを破棄する</summary>
    public void Dispose()
    {
        _provider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
