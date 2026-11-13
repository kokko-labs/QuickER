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
/// <see cref="TranslatorParityEfCoreRuntimeTestsBase"/> の述語の表を、方言切替で MySQL へ写した図から生成した EF Core 版
/// Repository で、実 MySQL（Testcontainers）へ流す派生。
/// </summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
[RuntimeCell(RuntimeFeatures.TranslatorParity, RuntimeTarget.EfCoreMySql)]
public sealed class TranslatorParityEfCoreMySqlRuntimeTests(MySqlContainerFixture fixture)
    : TranslatorParityEfCoreRuntimeTestsBase
{
    /// <summary>EF Core（MySQL）の答えが C# と割れると分かっている行と、その理由</summary>
    /// <remarks>
    /// MySQL の <c>datetime</c>・<c>time</c> はマイクロ秒までしか持たず、ドライバはそれより細かい値をマイクロ秒へ切り詰めて送る
    /// （docs のコード生成の EF Core の節「Known limitation」の割り切り）。
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
    };

    private const string SubMicrosecond =
        "MySQL の datetime・time はマイクロ秒までしか持たず、ドライバが 1 マイクロ秒より細かい値を切り詰めて比べる（docs の EF Core の節の既知の割り切り）";

    private static TranslatorParityEfCoreGenerated.Generated Generated =>
        TranslatorParityEfCoreGenerated.For(DialectNames.MySql);

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> Known => KnownRows;

    /// <inheritdoc />
    protected override TranslatorParityEfCoreBinding Binding => Generated.Binding;

    /// <summary>
    /// MySQL はオフセットつき日時の型を持たず、方言切替が <c>at_offset</c> を <c>timestamp</c>（DateTime）へ写すので、
    /// シードのオフセットつき日時はその日時の部分を入れる（この列を参照する行は表に無い）
    /// </summary>
    protected override object? SeedValue(PropertyInfo target, object? value) =>
        value is DateTimeOffset offset && target.PropertyType != typeof(DateTimeOffset?)
            ? offset.DateTime
            : value;

    /// <inheritdoc />
    protected override async Task<ServiceProvider> CreateProviderAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(Generated.Ddl, Ct);

        return Binding.CreateProvider(options =>
            options.UseMySQL(fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
        );
    }
}
