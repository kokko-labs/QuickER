using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedMultiTargetFixture;
using QuickER.Tests.GeneratedMultiTargetFixture.Repositories.Sqlite;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// edge-skip の実行時観測を<b>QuickER の <c>SqliteRepository</c> 版</b>で実ファイル SQLite に流す派生
/// （Docker 不要＝CI 常時実行）。
/// </summary>
public sealed class IncludeGraphSelfReferenceAdoRuntimeTests
    : IncludeGraphSelfReferenceMultiTargetRuntimeTestsBase,
        IDisposable
{
    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
            .BuildServiceProvider();

    protected override INodeRepository Nodes() => Provider().GetRequiredService<INodeRepository>();

    protected override async Task ResetSchemaAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(MultiTargetPortableFixtureDefinition.Build(), Ct);
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }
}
