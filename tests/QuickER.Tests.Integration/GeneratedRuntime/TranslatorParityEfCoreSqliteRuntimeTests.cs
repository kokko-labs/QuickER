using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedTranslatorParityEfCoreFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <see cref="TranslatorParityEfCoreRuntimeTestsBase"/> の述語の表を、実ファイルの SQLite の EF Core 版 Repository へ流す派生
/// （Docker 不要＝CI で常時実行）。
/// </summary>
/// <remarks>
/// フィクスチャは SQL Server 方言で生成したものをそのまま使う（生成の EF Core のコードはプロバイダに依らず、
/// <c>UseSqlite</c> で流せる）。スキーマは同じ図から SQLite の DDL で作る。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class TranslatorParityEfCoreSqliteRuntimeTests
    : TranslatorParityEfCoreRuntimeTestsBase,
        IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>EF Core（SQLite）の答えが C# と割れると分かっている行と、その理由</summary>
    /// <remarks>
    /// EF Core の SQLite プロバイダの文書（Microsoft Learn の「SQLite Database Provider - Limitations」の Query limitations）は、
    /// SQLite が持たない型（TimeSpan など）について、等値の比較は扱えるが、大小の比較と並び替えはクライアントの評価が要ると書く。
    /// TimeSpan の列の順序比較は、翻訳できない LINQ の式として <see cref="InvalidOperationException"/> で断られる（プロバイダの事実）。
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> KnownRows = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["time-scaled-less-finer"] =
            "EF Core の SQLite プロバイダは TimeSpan の列の大小の比較を翻訳できず、InvalidOperationException で断る（プロバイダの文書の Query limitations：SQLite が持たない型の比較と並び替えはクライアントの評価が要る）",
    };

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> Known => KnownRows;

    /// <inheritdoc />
    protected override async Task<ServiceProvider> CreateProviderAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(TranslatorParityEfCoreFixtureDefinition.Build(), Ct);

        return new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options
                    .UseSqlite(_db.ReadWriteCreateConnectionString)
                    .IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();
    }

    /// <summary>一時のデータベースを片付ける</summary>
    public void Dispose() => _db.Dispose();
}
