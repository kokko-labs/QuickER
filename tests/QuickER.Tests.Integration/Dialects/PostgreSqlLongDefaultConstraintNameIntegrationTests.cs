using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// PostgreSQL：旧版の QuickER が書いた DDL（縮めない長い既定名）で作った外部キーを、差分が吸収しないと別の外部キーに見えること
/// （吸収の対照）を実 DB で確かめる
/// </summary>
/// <remarks>
/// 吸収そのもの（DB の外部キー名が縮める前の既定名を 63 バイトで切った名前で、差分が出ない）は、同期計画の期待の行
/// <c>realdb-long-default-pg-legacy-truncated-fk-name-is-absorbed</c>（<see cref="SyncPlanExpectationRows"/>）が確かめる。
/// この対照は能力（<see cref="SyncDialectCapabilities.TruncatesLongIdentifiers"/>）を差し替えて差分を取るので、方言の能力で
/// 流す期待の行の実 DB の段では書けない。既定名を上限に収める図の往復は期待の行 <c>realdb-long-default-names-apply-and-round-trip-no-diff</c>。
/// 単体の対応は <c>LongDefaultConstraintNameTests</c>。
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class PostgreSqlLongDefaultConstraintNameIntegrationTests(
    PostgreSqlContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "[Integration] PostgreSQL: 旧版の DDL で作った長い既定名の外部キーは、吸収しないと別の外部キーに見える（対照）"
    )]
    public async Task LegacyTruncatedForeignKeyName_IsNotAbsorbedWithoutTruncation()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        var provider = new PostgreSqlProvider();
        var diagram = LongNameDiagrams.BuildSingleForeignKey(40, provider.Name);

        // 旧版の DDL と同じ形（外部キーの既定名を縮めずに書く）
        await fixture.ExecuteAsync(RealCrossDialectScenarios.LegacyPostgreSqlLongNameSql, Ct);
        var live = await provider.SchemaImporter.ImportAsync(
            fixture.ConnectionString,
            DbCommands.DefaultTimeoutSeconds,
            Ct
        );

        var notAbsorbed = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            provider.SyncCapabilities with
            {
                TruncatesLongIdentifiers = false,
            }
        );

        notAbsorbed
            .Items.Select(item => item.Kind)
            .Should()
            .Contain(SchemaDiffKind.AddForeignKey, "吸収しないと別の外部キーに見える（対照）");
        await fixture.ResetSchemaAsync(Ct);
    }
}
