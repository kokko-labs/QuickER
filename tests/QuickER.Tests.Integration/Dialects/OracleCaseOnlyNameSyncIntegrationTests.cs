using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 引用符なしで作られた（大文字で格納された）表へ、大文字小文字だけ綴りの違う図を同期できることを実 Oracle で検証する。
/// </summary>
/// <remarks>
/// 差分は表・列を大文字小文字を無視して組むので、スクリプトが図の綴り（<c>"Orders"</c>）を書くと表 <c>ORDERS</c> に届かない。
/// 差分の入口で組になった表・列を DB の綴りへ揃える（<see cref="SchemaDiffService"/>）ので、列の型変更・説明・
/// 主キーの付け替え（実行時に制約名を逆引きするブロック）がすべて実在する表を指す。Docker 必須。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleCaseOnlyNameSyncIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>大文字で格納された表へ、混在した綴りの図から型変更・説明・主キーの付け替えを同期できる</summary>
    [Fact(
        DisplayName = "[Integration] Oracle: 大文字で格納された表へ大文字小文字だけ違う図を同期できる"
    )]
    public async Task Sync_ToUppercaseTable_FromMixedCaseDiagram_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            """
            CREATE TABLE ORDERS (
                ID NUMBER(10) NOT NULL,
                NAME VARCHAR2(50) NULL,
                CODE VARCHAR2(10) NOT NULL,
                CONSTRAINT PK_ORDERS PRIMARY KEY (ID)
            );
            """,
            Ct
        );

        var target = new Entity { TableName = "Orders", Description = "orders" };
        target.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "NUMBER(10)",
                IsNullable = false,
            }
        );
        target.Columns.Add(
            new Column
            {
                Name = "Name",
                DataType = "VARCHAR2(100)",
                IsNullable = true,
                Description = "order name",
            }
        );
        target.Columns.Add(
            new Column
            {
                Name = "Code",
                DataType = "VARCHAR2(10)",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );

        var caps = new OracleProvider().SyncCapabilities;
        var live = await ImportAsync();
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            [target],
            [],
            caps
        );

        foreach (var item in diff.Items.Where(i => i.IsSelectable))
        {
            item.IsSelected = true;
        }

        diff.Items.Select(i => i.Kind)
            .Should()
            .Contain([
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.AlterPrimaryKey,
                SchemaDiffKind.SetTableDescription,
                SchemaDiffKind.SetColumnDescription,
            ]);

        var script = new OracleSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(
                diff.Items,
                caps,
                new SyncPlanContext
                {
                    LiveEntities = live.Entities,
                    LiveRelationships = live.Relationships,
                }
            )
        );
        var result = await new OracleSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        result.Committed.Should().BeTrue($"{result.Error}\n{script}");

        var after = (await ImportAsync()).Entities.Should().ContainSingle().Subject;
        after.TableName.Should().Be("ORDERS");
        after.Description.Should().Be("orders");
        after.Columns.Single(c => c.Name == "NAME").DataType.Should().Be("VARCHAR2(100)");
        after.Columns.Single(c => c.Name == "NAME").Description.Should().Be("order name");
        after.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("CODE");
    }

    private async Task<SchemaImportResult> ImportAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        return await new OracleSchemaImporter().ImportAsync(conn, Ct);
    }
}
