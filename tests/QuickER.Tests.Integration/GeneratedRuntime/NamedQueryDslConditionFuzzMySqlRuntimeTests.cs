using Microsoft.EntityFrameworkCore;
using QuickER.Dialects;
using QuickER.Provider.MySql;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 簡易 DSL の条件の総当たりの MySQL の派生: 実コンテナの EF Core 版 Repository（MySQL）で、生成→実コンパイル→実行し、
/// 読み戻した行へ docs の意味のオラクルを当てた結果と照合するテストクラス（手順は <see cref="DslConditionFuzzEfCore"/>）
/// </summary>
/// <remarks>
/// 単位は QuickER.Tests の MySQL の構成（インメモリ）と同じ（符号なし整数・sbyte の列と、網羅の単位の固定長の文字列・decimal・日時・
/// Guid の列）。文字列の大文字小文字・空白は <see cref="DslCaseRule.MySql"/>（docs の表の EF Core（MySQL）の行）、NULL の並ぶ位置は
/// 昇順で先頭。失敗のキーの入口は <c>dsl-mysql-</c>。
/// </remarks>
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class NamedQueryDslConditionFuzzMySqlRuntimeTests(MySqlContainerFixture fixture)
{
    private const string Suite = "dsl-mysql";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "簡易 DSL の条件の総当たり（MySQL）: 生成→実コンパイル→EF Core の結果が docs の意味と一致する（既知の失敗の一覧と厳密に一致）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task DslConditions_MatchDocumentedSemanticsOnMySql()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        await DslConditionFuzzEfCore.RunAsync(
            DslConditionFuzzEfCore.Target(
                Suite,
                DialectNames.MySql,
                new MySqlProvider(),
                BackendAxis(fixture)
            ),
            DslFuzzDimensions.MySqlScopes,
            DslFuzzUnits.MySql,
            [
                DslFuzzUnits.Coverage(DialectNames.MySql),
                DslFuzzUnits.PlainCoverage(DialectNames.MySql),
                .. DslFuzzUnits.FamiliesCoverage(DialectNames.MySql),
                .. DslFuzzUnits.TimeSpanCoverage(DialectNames.MySql),
                .. DslFuzzUnits.FloatingCoverage(DialectNames.MySql),
            ],
            Ct
        );
    }

    /// <summary>
    /// MySQL の構成の実装先の軸（EF Core だけ）。コンテナには実装先を開くときにだけ触れるので、軸を作るだけなら Docker は要らない
    /// （<see cref="MatrixAxisGuardTests"/> が Docker なしで作れることを確かめる）
    /// </summary>
    /// <param name="fixture">MySQL のコンテナ（実装先を開くときにだけ使う）</param>
    internal static MatrixAxis<DslFuzzBackend> BackendAxis(MySqlContainerFixture fixture) =>
        DslConditionFuzzEfCore.BackendAxis(
            DialectNames.MySql,
            DslCaseRule.MySql,
            DslNullOrder.FirstWhenAscending,
            async (ddl, ct) =>
            {
                await fixture.ResetSchemaAsync(ct);
                await fixture.ExecuteAsync(ddl, ct);
            },
            options => options.UseMySQL(fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
        );
}
