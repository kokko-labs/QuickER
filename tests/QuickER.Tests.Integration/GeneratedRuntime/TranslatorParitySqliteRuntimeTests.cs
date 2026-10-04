using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.Sqlite;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <see cref="TranslatorParityRuntimeTestsBase"/> の共通シナリオを、実ファイルの SQLite の
/// QuickER 版 Repository へ流す派生（Docker 不要＝CI で常時実行）。
/// </summary>
[Trait("Category", "Integration")]
public sealed class TranslatorParitySqliteRuntimeTests
    : TranslatorParityRuntimeTestsBase,
        IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    protected override async Task<ServiceProvider> CreateProviderAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(TranslatorParityFixtureDefinition.Build(), Ct);

        return new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
            .BuildServiceProvider();
    }

    public void Dispose() => _db.Dispose();
}
