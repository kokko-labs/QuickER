using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Dialects;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <see cref="TranslatorParityEfCoreRuntimeTestsBase"/> の述語の表を、方言切替で PostgreSQL へ写した図から生成した EF Core 版
/// Repository で、実 PostgreSQL（Testcontainers）へ流す派生。
/// </summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
[RuntimeCell(RuntimeFeatures.TranslatorParity, RuntimeTarget.EfCorePostgreSql)]
public sealed class TranslatorParityEfCorePostgreSqlRuntimeTests(PostgreSqlContainerFixture fixture)
    : TranslatorParityEfCoreRuntimeTestsBase
{
    /// <summary>EF Core（PostgreSQL）の答えが C# と割れると分かっている行と、その理由</summary>
    /// <remarks>
    /// どれも docs のコード生成の EF Core の節「Known limitation」に書いた割り切り。PostgreSQL の <c>timestamp</c>・<c>time</c> は
    /// マイクロ秒までしか持たず、Npgsql はそれより細かい値をマイクロ秒へ切り詰めて送る。整数の列と decimal の列の比較は、EF Core が
    /// 整数の列を相手の列の型へ CAST し、その型に入らない行で桁あふれの例外になる。
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> KnownRows = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["datetime2-scaled-equals-finer"] = SubMicrosecond,
        ["datetime2-scaled-less-finer"] = SubMicrosecond,
        ["time-scaled-equals-finer"] = SubMicrosecond,
        ["time-scaled-less-finer"] = SubMicrosecond,
        ["int-column-greater-decimal-column"] = ColumnCast,
        ["int-column-equals-decimal-column"] = ColumnCast,
    };

    private const string SubMicrosecond =
        "PostgreSQL の timestamp・time はマイクロ秒までしか持たず、Npgsql が 1 マイクロ秒より細かい値を切り詰めて比べる（docs の EF Core の節の既知の割り切り）";

    private const string ColumnCast =
        "整数の列と decimal の列の比較は、EF Core が整数の列を相手の列の型（numeric(10,2)）へ CAST し、整数部の桁が入らない行で桁あふれの例外になる（docs の EF Core の節の既知の割り切り）";

    private static TranslatorParityEfCoreGenerated.Generated Generated =>
        TranslatorParityEfCoreGenerated.For(DialectNames.PostgreSql);

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> Known => KnownRows;

    /// <inheritdoc />
    protected override TranslatorParityEfCoreBinding Binding => Generated.Binding;

    /// <summary>
    /// Npgsql は <c>timestamp with time zone</c> の列へ UTC 以外のオフセットの DateTimeOffset を書けない（docs のコード生成の
    /// 「EF Core に教える列の型」の Npgsql の規則）ので、シードのオフセットつき日時を UTC へ直す
    /// </summary>
    protected override object? SeedValue(PropertyInfo target, object? value) =>
        value is DateTimeOffset offset ? offset.ToUniversalTime() : value;

    /// <inheritdoc />
    protected override async Task<ServiceProvider> CreateProviderAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(Generated.Ddl, Ct);

        return Binding.CreateProvider(options =>
            options.UseNpgsql(fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
        );
    }
}
