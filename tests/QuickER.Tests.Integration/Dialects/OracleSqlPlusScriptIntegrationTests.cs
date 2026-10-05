using System.Collections.Generic;
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
/// 出力した DDL・同期スクリプトを実 SQL*Plus で手で流したとき、各文がちょうど 1 回だけ実行されることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// SQL*Plus は行末の <c>;</c> で通常の文を実行し、<c>/</c> だけの行で<b>バッファの文をもう一度</b>実行する。
/// 通常の文の後ろにも <c>/</c> 行を置くと、CREATE / ALTER が 2 回走って ORA-00955 / ORA-01430 で止まる。
/// そのため <c>/</c> 行は PL/SQL ブロックの後ろだけに置く（<see cref="OracleSyncScriptBuilder"/> の規約）。
/// </para>
/// <para>
/// 流すのはコンテナ内の <c>sqlplus</c>（<see cref="OracleContainerFixture.RunSqlPlusAsync"/>）。
/// <c>WHENEVER SQLERROR EXIT FAILURE</c> つきなので、どれか 1 文でもエラーになれば終了コードが 0 でなくなる。
/// 通常の文（CREATE TABLE・ALTER TABLE ADD / MODIFY）と PL/SQL ブロック（主キーの付け替え・改行を含む説明）を混ぜる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleSqlPlusScriptIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>改行を含む説明（PL/SQL ブロックで出力される）</summary>
    private const string MultilineDescription = "line 1\nline 2";

    /// <summary>同期スクリプトを SQL*Plus で流すと、通常文もブロックも 1 回だけ実行される</summary>
    [Fact(
        DisplayName = "[Integration] 同期スクリプトを SQL*Plus で流すと各文が 1 回だけ実行される"
    )]
    public async Task SyncScript_RunsEachStatementOnceInSqlPlus()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        var caps = new OracleProvider().SyncCapabilities;

        // ---- 1 回目: 空の DB へ表 2 つ・外部キー・説明（1 行と改行入り）を作る ----
        var parent = new Entity { TableName = "parent", Description = MultilineDescription };
        var parentId = Column("id", "NUMBER(10)", primaryKey: true);
        parent.Columns.Add(parentId);
        parent.Columns.Add(Column("name", "VARCHAR2(50)", description: "one line"));
        var child = new Entity { TableName = "child" };
        var childParentId = Column("parent_id", "NUMBER(10)");
        child.Columns.Add(Column("id", "NUMBER(10)", primaryKey: true));
        child.Columns.Add(Column("code", "VARCHAR2(20)", nullable: false));
        child.Columns.Add(childParentId);
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentId.Id, childParentId.Id)],
            ConstraintName = "FK_child_parent",
        };

        var script1 = await BuildSyncScriptAsync([parent, child], [relationship], caps);
        script1
            .Should()
            .Contain("BEGIN", "改行入りの説明がブロックで出ていること（ブロックの経路も通す）");
        await RunInSqlPlusAsync(script1);

        var live1 = await ImportAsync();
        live1.Entities.Select(e => e.TableName).Should().BeEquivalentTo("parent", "child");
        live1
            .Entities.Single(e => e.TableName == "parent")
            .Description.Should()
            .Be(MultilineDescription);
        live1.Relationships.Should().ContainSingle();

        // ---- 2 回目: 列の追加・型の変更（通常文）と主キーの付け替え（ブロック）----
        var parent2 = live1.Entities.Single(e => e.TableName == "parent").Clone(preserveId: true);
        var child2 = live1.Entities.Single(e => e.TableName == "child").Clone(preserveId: true);
        child2.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        child2.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
        child2.Columns.Add(Column("added", "NUMBER(10)"));
        parent2.Columns.Single(c => c.Name == "name").DataType = "VARCHAR2(100)";
        var relationship2 = new Relationship
        {
            SourceEntityId = parent2.Id,
            TargetEntityId = child2.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                new(
                    parent2.Columns.Single(c => c.Name == "id").Id,
                    child2.Columns.Single(c => c.Name == "parent_id").Id
                ),
            ],
            ConstraintName = "FK_child_parent",
        };

        var script2 = await BuildSyncScriptAsync(
            [parent2, child2],
            [relationship2],
            caps,
            selectAll: true
        );
        script2.Should().Contain("DECLARE", "主キーの付け替えはブロックで出ること");
        await RunInSqlPlusAsync(script2);

        var live2 = await ImportAsync();
        var liveChild = live2.Entities.Single(e => e.TableName == "child");
        liveChild.Columns.Single(c => c.Name == "code").IsPrimaryKey.Should().BeTrue();
        liveChild.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeFalse();
        liveChild.Columns.Should().Contain(c => c.Name == "added");
        live2
            .Entities.Single(e => e.TableName == "parent")
            .Columns.Single(c => c.Name == "name")
            .DataType.Should()
            .Be("VARCHAR2(100)");
    }

    /// <summary>図から生成した DDL を SQL*Plus で流すと、通常文もブロックも 1 回だけ実行される</summary>
    [Fact(DisplayName = "[Integration] DDL を SQL*Plus で流すと各文が 1 回だけ実行される")]
    public async Task Ddl_RunsEachStatementOnceInSqlPlus()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var entity = new Entity { TableName = "ddl_item", Description = MultilineDescription };
        entity.Columns.Add(Column("id", "NUMBER(10)", primaryKey: true));
        entity.Columns.Add(Column("name", "VARCHAR2(50)", description: "one line"));
        var diagram = new ErDiagram();
        diagram.Entities.Add(entity);
        var ddl = new OracleDdlGenerator().Build(diagram);
        ddl.Should().Contain("BEGIN");

        await RunInSqlPlusAsync(ddl);

        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "ddl_item")
            .Description.Should()
            .Be(MultilineDescription);
    }

    /// <summary>live を取り込んで差分を取り、選んだ項目から同期スクリプトを組み立てる</summary>
    private async Task<string> BuildSyncScriptAsync(
        IReadOnlyList<Entity> targets,
        IReadOnlyList<Relationship> relationships,
        SyncDialectCapabilities caps,
        bool selectAll = false
    )
    {
        var live = await ImportAsync();
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            targets,
            relationships,
            caps
        );

        if (selectAll)
        {
            foreach (var item in diff.Items.Where(i => i.IsSelectable))
            {
                item.IsSelected = true;
            }
        }

        var context = new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
        };
        return new OracleSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(diff.Items, caps, context)
        );
    }

    /// <summary>SQL*Plus で流し、終了コード 0 を表明する</summary>
    private async Task RunInSqlPlusAsync(string script)
    {
        var (exitCode, output) = await fixture.RunSqlPlusAsync(script, Ct);
        exitCode.Should().Be(0, $"SQL*Plus の出力:\n{output}\nスクリプト:\n{script}");
    }

    private async Task<SchemaImportResult> ImportAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        return await new OracleSchemaImporter().ImportAsync(conn, Ct);
    }

    private static Column Column(
        string name,
        string dataType,
        bool primaryKey = false,
        bool nullable = true,
        string description = ""
    ) =>
        new()
        {
            Name = name,
            DataType = dataType,
            IsPrimaryKey = primaryKey,
            IsNullable = !primaryKey && nullable,
            Description = description,
        };
}
