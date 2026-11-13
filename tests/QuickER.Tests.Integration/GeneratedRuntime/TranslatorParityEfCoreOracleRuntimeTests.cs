using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Dialects;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <see cref="TranslatorParityEfCoreRuntimeTestsBase"/> の述語の表を、方言切替で Oracle へ写した図から生成した EF Core 版
/// Repository で、実 Oracle（Testcontainers）へ流す派生。
/// </summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
[RuntimeCell(RuntimeFeatures.TranslatorParity, RuntimeTarget.EfCoreOracle)]
public sealed class TranslatorParityEfCoreOracleRuntimeTests(OracleContainerFixture fixture)
    : TranslatorParityEfCoreRuntimeTestsBase
{
    /// <summary>EF Core（Oracle）の答えが C# と割れると分かっている行と、その理由</summary>
    /// <remarks>
    /// 整数の列と decimal の列の比較は、EF Core が整数の列を相手の列の型へ CAST し、その型に入らない行で桁あふれの例外になる
    /// （docs のコード生成の EF Core の節「Known limitation」の割り切り）。
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> KnownRows = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["int-column-greater-decimal-column"] = ColumnCast,
        ["int-column-equals-decimal-column"] = ColumnCast,
    };

    private const string ColumnCast =
        "整数の列と decimal の列の比較は、EF Core が整数の列を相手の列の型（DECIMAL(10,2)）へ CAST し、整数部の桁が入らない行で ORA-01438 になる（docs の EF Core の節の既知の割り切り）";

    /// <summary>生成物に列が無いので流せない行と、その理由</summary>
    private static readonly IReadOnlyDictionary<string, string> NotApplicableRows = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["time-scaled-equals-finer"] = NoTimeSpanType,
        ["time-scaled-less-finer"] = NoTimeSpanType,
    };

    private const string NoTimeSpanType =
        "Oracle の型マッパーに TimeSpan に写す型が無く、方言切替が time(2) の列 clock を写せない（図から外す）";

    private static TranslatorParityEfCoreGenerated.Generated Generated =>
        TranslatorParityEfCoreGenerated.For(DialectNames.Oracle);

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> Known => KnownRows;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> NotApplicable => NotApplicableRows;

    /// <inheritdoc />
    protected override TranslatorParityEfCoreBinding Binding => Generated.Binding;

    /// <inheritdoc />
    protected override async Task<ServiceProvider> CreateProviderAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(Generated.Ddl, Ct);

        return Binding.CreateProvider(options =>
            options.UseOracle(fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
        );
    }
}
