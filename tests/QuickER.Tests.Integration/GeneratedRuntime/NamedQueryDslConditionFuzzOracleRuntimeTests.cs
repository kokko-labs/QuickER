using Microsoft.EntityFrameworkCore;
using QuickER.Dialects;
using QuickER.Provider.Oracle;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 簡易 DSL の条件の総当たりの Oracle の派生: 実コンテナの EF Core 版 Repository（Oracle）で、生成→実コンパイル→実行し、
/// 読み戻した行へ docs の意味のオラクルを当てた結果と照合するテストクラス（手順は <see cref="DslConditionFuzzEfCore"/>）
/// </summary>
/// <remarks>
/// 文字列の大文字小文字と空文字列は <see cref="DslCaseRule.Oracle"/>（docs の表の EF Core（Oracle）の行と空文字列の段落）、NULL の
/// 並ぶ位置は昇順で末尾（docs のインメモリの節「実 DB との既知の乖離」）。失敗のキーの入口は <c>dsl-oracle-</c>。
/// </remarks>
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class NamedQueryDslConditionFuzzOracleRuntimeTests(OracleContainerFixture fixture)
{
    private const string Suite = "dsl-oracle";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "簡易 DSL の条件の総当たり（Oracle）: 生成→実コンパイル→EF Core の結果が docs の意味と一致する（既知の失敗の一覧と厳密に一致）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task DslConditions_MatchDocumentedSemanticsOnOracle()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        await DslConditionFuzzEfCore.RunAsync(
            DslConditionFuzzEfCore.Target(
                Suite,
                DialectNames.Oracle,
                new OracleProvider(),
                BackendAxis(fixture)
            ),
            DslFuzzDimensions.OracleScopes,
            DslFuzzUnits.Oracle,
            [
                DslFuzzUnits.Coverage(DialectNames.Oracle),
                DslFuzzUnits.PlainCoverage(DialectNames.Oracle),
            ],
            Ct
        );
    }

    /// <summary>
    /// Oracle の構成の実装先の軸（EF Core だけ）。コンテナには実装先を開くときにだけ触れるので、軸を作るだけなら Docker は要らない
    /// （<see cref="MatrixAxisGuardTests"/> が Docker なしで作れることを確かめる）
    /// </summary>
    /// <param name="fixture">Oracle のコンテナ（実装先を開くときにだけ使う）</param>
    internal static MatrixAxis<DslFuzzBackend> BackendAxis(OracleContainerFixture fixture) =>
        DslConditionFuzzEfCore.BackendAxis(
            DialectNames.Oracle,
            DslCaseRule.Oracle,
            DslNullOrder.LastWhenAscending,
            async (ddl, ct) =>
            {
                await fixture.ResetSchemaAsync(ct);
                await fixture.ExecuteAsync(ddl, ct);
            },
            options => options.UseOracle(fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
        );
}
