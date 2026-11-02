using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// C: <see cref="OracleSyncScriptBuilder"/> + <see cref="OracleSchemaSyncExecutor"/> による同期のうち、差分からは作れない項目
/// （名前不明の外部キーの削除）を実 DB で検証する統合テスト。
/// </summary>
/// <remarks>
/// 差分から作れる同期は、同期計画の期待の行の実 DB の段（<c>SyncPlanExpectationRows.RealOracle.cs</c>）が持つ。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleSchemaSyncIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly OracleSyncScriptBuilder _builder = new();
    private readonly OracleSchemaSyncExecutor _executor = new();
    private readonly OracleSchemaImporter _importer = new();

    /// <summary>
    /// 制約名を渡さない外部キーの削除（名前不明＝PL/SQL ブロックでカタログを逆引きする経路）が、実 DB の外部キーを外すことを検証する。
    /// Oracle は外部キーの名前を保存するので差分の外部キーの削除は必ず名前を持ち、この経路は手で組んだ項目でしか通らない。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] C: 名前不明の外部キーの削除がカタログの逆引きで実 DB に適用される"
    )]
    public async Task SchemaSync_DropForeignKeyWithoutName_UsesCatalogLookup()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        // 前提：説明つきの parent と、列を変えた後の child（to_alter VARCHAR2(100) NOT NULL・added）。外部キーは無い
        await fixture.ExecuteAsync(
            "CREATE TABLE \"parent\" (\"id\" NUMBER(10) NOT NULL, \"name\" VARCHAR2(50) NULL, "
                + "CONSTRAINT \"PK_parent\" PRIMARY KEY (\"id\"));\n"
                + "COMMENT ON TABLE \"parent\" IS '親テーブルの説明';\n"
                + "COMMENT ON COLUMN \"parent\".\"name\" IS '名称';\n"
                + "CREATE TABLE \"child\" (\"id\" NUMBER(10) NOT NULL, \"parent_id\" NUMBER(10) NULL, "
                + "\"to_alter\" VARCHAR2(100) NOT NULL, \"added\" NUMBER(10) NULL, "
                + "CONSTRAINT \"PK_child\" PRIMARY KEY (\"id\"));",
            Ct
        );

        // 制約名の分からない FK を DB に直接張る
        await fixture.ExecuteAsync(
            "ALTER TABLE \"child\" ADD CONSTRAINT \"fk_unknown_name\" "
                + "FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\");",
            Ct
        );

        // ConstraintName を伏せた DropForeignKey 項目を手組みして PL/SQL ブロック経路を通す
        var live = await ImportAsync();
        var parentE = live.Entities.Single(e => e.TableName == "parent");
        var childE = live.Entities.Single(e => e.TableName == "child");
        var dropFkUnknown = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "child",
            ParentEntity = parentE,
            ChildEntity = childE,
            ForeignKeyName = null, // 不明 → PL/SQL ブロックでカタログ逆引き
            IsSelected = true,
        };
        await ApplyAsync(settings, new[] { dropFkUnknown });

        (await ImportAsync()).Relationships.Should().BeEmpty();
    }

    // ---------------- ヘルパー ----------------

    private async Task<SchemaImportResult> ImportAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var r = await _importer.ImportAsync(conn, Ct);
        return new SchemaImportResult { Entities = r.Entities, Relationships = r.Relationships };
    }

    private async Task ApplyAsync(DbConnectionSettings settings, IEnumerable<SchemaDiffItem> items)
    {
        var script = _builder.Build(
            new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral)
        );
        var result = await _executor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL:\n{script}");
    }
}
