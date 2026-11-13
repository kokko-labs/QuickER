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
/// <see cref="TranslatorParityEfCoreRuntimeTestsBase"/> の述語の表を、実 SQL Server（Testcontainers）の EF Core 版 Repository へ流す派生。
/// </summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
[RuntimeCell(RuntimeFeatures.TranslatorParity, RuntimeTarget.EfCoreSqlServer)]
public sealed class TranslatorParityEfCoreSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : TranslatorParityEfCoreRuntimeTestsBase
{
    /// <summary>EF Core（SQL Server）の答えが C# と割れると分かっている行と、その理由</summary>
    /// <remarks>
    /// Unicode でない列と ASCII の外の文字の定数の比較は、EF Core が定数を列の型の非 Unicode のリテラル（<c>N</c> の無い
    /// <c>'日本'</c>）として SQL へ書き込み、文字が <c>'??'</c> に化けて化けた行に一致する。パラメータでないので
    /// <c>SqlServerQueryParameterInterceptor</c> では直せない（docs の EF Core の節の既知の割り切り。簡易 DSL の条件は
    /// 文字列のリテラルを変数にして生成するので、この割り切りに当たらない）。
    /// 整数の列と decimal の列の比較は、EF Core が整数の列を相手の列の型へ CAST し、その型の整数部に入らない行で
    /// 桁あふれの例外になる（docs の既知の割り切り。既定の decimal の型を広げても、列どうしの CAST は相手の列の型を取るので直らない）。
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> KnownRows = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["varchar-equals-non-ascii"] =
            "手書きの LINQ の定数は非 Unicode のリテラルになり、ASCII の外の文字が化ける（docs の既知の割り切り）",
        ["varchar-not-equals-non-ascii"] =
            "手書きの LINQ の定数は非 Unicode のリテラルになり、ASCII の外の文字が化ける（docs の既知の割り切り）",
        ["int-column-greater-decimal-column"] =
            "整数の列と decimal の列の比較は、EF Core が整数の列を相手の列の型（decimal(10,2)）へ CAST し、整数部の桁が入らない行で桁あふれの例外になる（docs の EF Core の節の既知の割り切り）",
        ["int-column-equals-decimal-column"] =
            "整数の列と decimal の列の比較は、EF Core が整数の列を相手の列の型（decimal(10,2)）へ CAST し、整数部の桁が入らない行で桁あふれの例外になる（docs の EF Core の節の既知の割り切り）",
        ["binary-equals-value"] = BinaryPadding,
        ["binary-not-equals-value"] = BinaryPadding,
        ["binary-equals-trailing-zero-value"] = BinaryPadding,
        ["binary-in-list"] = BinaryPadding,
        ["binary-not-in-list"] = BinaryPadding,
        ["binary-in-list-with-null"] = BinaryPadding,
        ["binary-column-equals-column"] = BinaryPadding,
        ["binary-column-not-equals-column"] = BinaryPadding,
        ["time-out-of-day-equals"] = TimeOutOfDay,
        ["time-out-of-day-not-equals-negative"] = TimeOutOfDay,
        ["time-out-of-day-less"] = TimeOutOfDay,
        ["time-out-of-day-greater"] = TimeOutOfDay,
        ["time-out-of-day-greater-or-equal-negative"] = TimeOutOfDay,
        ["time-out-of-day-less-or-equal-negative"] = TimeOutOfDay,
        ["time-out-of-day-value-on-left"] = TimeOutOfDay,
        ["time-out-of-day-negated-equals"] = TimeOutOfDay,
        ["time-out-of-day-in-mixed"] = TimeOutOfDay,
        ["time-out-of-day-not-in-only"] = TimeOutOfDay,
    };

    /// <inheritdoc />
    /// <remarks>time の列と範囲の外の TimeSpan の行（SqlClient が SqlDbType.Time の範囲の外として断る）</remarks>
    protected override IReadOnlyDictionary<string, Type> KnownParameterRefusals { get; } =
        KnownRows
            .Where(pair => pair.Value == TimeOutOfDay)
            .ToDictionary(pair => pair.Key, _ => typeof(OverflowException), StringComparer.Ordinal);

    /// <summary>time の列と範囲の外の TimeSpan の割り切りの理由</summary>
    private const string TimeOutOfDay =
        "EF Core 版は time の列と比べる値を列の型（time）で送り、SqlClient が 0〜24 時間未満の外の TimeSpan を範囲の外として断る（docs の TimeSpan の既知の割り切り。SQL Server に TimeSpan をそのまま比べられる型は無く、パラメータの型を変えても直らない）";

    /// <summary>バイナリの比較の割り切りの理由</summary>
    private const string BinaryPadding =
        "EF Core 版は SQL Server の比べ方のままで、末尾の 0x00 だけが違うバイナリを等しいとみなす（docs の「大文字小文字・空白・日付部品」の節のバイナリの段落の既知の割り切り）";

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> Known => KnownRows;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, int>? FixedBinaryLengths =>
        TranslatorParityRuntimeTestsBase.SqlServerFixedBinaryLengths;

    /// <inheritdoc />
    protected override async Task<ServiceProvider> CreateProviderAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ApplyDdlAsync(TranslatorParityEfCoreFixtureDefinition.Build(), Ct);

        return new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();
    }
}
