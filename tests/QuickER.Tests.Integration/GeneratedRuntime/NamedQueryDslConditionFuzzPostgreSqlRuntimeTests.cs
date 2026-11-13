using Microsoft.EntityFrameworkCore;
using QuickER.Dialects;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 簡易 DSL の条件の総当たりの PostgreSQL の派生: 実コンテナの EF Core 版 Repository（PostgreSQL）で、生成→実コンパイル→実行し、
/// 読み戻した行へ docs の意味のオラクルを当てた結果と照合するテストクラス（手順は <see cref="DslConditionFuzzEfCore"/>）
/// </summary>
/// <remarks>
/// 文字列の大文字小文字・空白は <see cref="DslCaseRule.PostgreSql"/>（docs の表の EF Core（PostgreSQL）の行と固定長の列の段落）、
/// NULL の並ぶ位置は昇順で末尾（docs のインメモリの節「実 DB との既知の乖離」）。失敗のキーの入口は <c>dsl-postgresql-</c>。
/// </remarks>
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class NamedQueryDslConditionFuzzPostgreSqlRuntimeTests(
    PostgreSqlContainerFixture fixture
)
{
    private const string Suite = "dsl-postgresql";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "簡易 DSL の条件の総当たり（PostgreSQL）: 生成→実コンパイル→EF Core の結果が docs の意味と一致する（既知の失敗の一覧と厳密に一致）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task DslConditions_MatchDocumentedSemanticsOnPostgreSql()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        await DslConditionFuzzEfCore.RunAsync(
            DslConditionFuzzEfCore.Target(
                Suite,
                DialectNames.PostgreSql,
                new PostgreSqlProvider(),
                BackendAxis(fixture)
            ),
            DslFuzzDimensions.PostgreSqlScopes,
            DslFuzzUnits.PostgreSql,
            [
                DslFuzzUnits.Coverage(DialectNames.PostgreSql),
                DslFuzzUnits.PlainCoverage(DialectNames.PostgreSql),
                .. DslFuzzUnits.FamiliesCoverage(DialectNames.PostgreSql),
                .. DslFuzzUnits.TimeSpanCoverage(DialectNames.PostgreSql),
                .. DslFuzzUnits.DateTimeOffsetCoverage(DialectNames.PostgreSql),
            ],
            Ct
        );
    }

    /// <summary>
    /// PostgreSQL の構成の実装先の軸（EF Core だけ）。コンテナには実装先を開くときにだけ触れるので、軸を作るだけなら Docker は要らない
    /// （<see cref="MatrixAxisGuardTests"/> が Docker なしで作れることを確かめる）
    /// </summary>
    /// <param name="fixture">PostgreSQL のコンテナ（実装先を開くときにだけ使う）</param>
    internal static MatrixAxis<DslFuzzBackend> BackendAxis(PostgreSqlContainerFixture fixture) =>
        DslConditionFuzzEfCore.BackendAxis(
            DialectNames.PostgreSql,
            DslCaseRule.PostgreSql,
            DslNullOrder.LastWhenAscending,
            async (ddl, ct) =>
            {
                await fixture.ResetSchemaAsync(ct);
                await fixture.ExecuteAsync(ddl, ct);
            },
            options => options.UseNpgsql(fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
        );
}
