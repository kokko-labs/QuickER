using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.Model;
using QuickER.PostgreSql;
using QuickER.Provider;

namespace QuickER.Tests.Integration;

/// <summary>
/// B: <see cref="PostgreSqlSyncScriptBuilder"/> + <see cref="PostgreSqlSchemaSyncExecutor"/> による
/// 説明（COMMENT ON）の同期往復を検証する統合テスト。設定・更新・削除の 3 パターンを実 DB で確認する。
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class PostgreSqlCommentSyncIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// SetTableDescription / SetColumnDescription の差分から COMMENT ON を生成・実行し、
    /// 再取込で Description が反映されること（設定→更新→削除）を、日本語・シングルクォート含む文字列で検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] B: COMMENT 同期で説明が設定→更新→削除まで往復する")]
    public async Task CommentSync_SetUpdateDelete_RoundTrips()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // ---------- 準備: テーブルを作成しておく ----------
        await fixture.ExecuteAsync(
            "CREATE TABLE \"items\" (\"id\" integer NOT NULL, \"name\" varchar(50) NULL, "
                + "CONSTRAINT \"PK_items\" PRIMARY KEY (\"id\"));",
            Ct
        );

        var settings = fixture.ToDbConnectionSettings();
        var builder = new PostgreSqlSyncScriptBuilder();
        var executor = new PostgreSqlSchemaSyncExecutor();
        var importer = new PostgreSqlSchemaImporter();

        const string TableDesc = "商品テーブル 'マスタ'"; // 日本語＋シングルクォート
        const string ColumnDesc = "商品名 'name' 列";

        // ---------- 1) 設定 ----------
        await ApplyAsync(
            builder,
            executor,
            settings,
            new[]
            {
                MakeTableDesc("items", TableDesc, oldDesc: null),
                MakeColumnDesc("items", "name", ColumnDesc, oldDesc: null),
            }
        );

        var afterSet = await ImportSingleAsync(fixture, importer, "items");
        afterSet.Description.Should().Be(TableDesc);
        afterSet.Columns.Single(c => c.Name == "name").Description.Should().Be(ColumnDesc);

        // ---------- 2) 更新 ----------
        const string TableDesc2 = "商品テーブル（更新後）";
        const string ColumnDesc2 = "商品名（更新後）";
        await ApplyAsync(
            builder,
            executor,
            settings,
            new[]
            {
                MakeTableDesc("items", TableDesc2, oldDesc: TableDesc),
                MakeColumnDesc("items", "name", ColumnDesc2, oldDesc: ColumnDesc),
            }
        );

        var afterUpdate = await ImportSingleAsync(fixture, importer, "items");
        afterUpdate.Description.Should().Be(TableDesc2);
        afterUpdate.Columns.Single(c => c.Name == "name").Description.Should().Be(ColumnDesc2);

        // ---------- 3) 削除（空文字） ----------
        await ApplyAsync(
            builder,
            executor,
            settings,
            new[]
            {
                MakeTableDesc("items", string.Empty, oldDesc: TableDesc2),
                MakeColumnDesc("items", "name", string.Empty, oldDesc: ColumnDesc2),
            }
        );

        var afterDelete = await ImportSingleAsync(fixture, importer, "items");
        afterDelete.Description.Should().BeEmpty();
        afterDelete.Columns.Single(c => c.Name == "name").Description.Should().BeEmpty();
    }

    private static SchemaDiffItem MakeTableDesc(string table, string newDesc, string? oldDesc) =>
        new()
        {
            Kind = SchemaDiffKind.SetTableDescription,
            TableName = table,
            NewDescription = newDesc,
            OldDescription = oldDesc,
        };

    private static SchemaDiffItem MakeColumnDesc(
        string table,
        string column,
        string newDesc,
        string? oldDesc
    ) =>
        new()
        {
            Kind = SchemaDiffKind.SetColumnDescription,
            TableName = table,
            ColumnName = column,
            NewDescription = newDesc,
            OldDescription = oldDesc,
        };

    private static async Task ApplyAsync(
        PostgreSqlSyncScriptBuilder builder,
        PostgreSqlSchemaSyncExecutor executor,
        DbConnectionSettings settings,
        IEnumerable<SchemaDiffItem> items
    )
    {
        var script = builder.Build(items);
        var result = await executor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"COMMENT 同期に失敗: {result.Error}\nSQL:\n{script}");
    }

    private static async Task<Entity> ImportSingleAsync(
        PostgreSqlContainerFixture fixture,
        PostgreSqlSchemaImporter importer,
        string table
    )
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await importer.ImportAsync(conn, Ct);
        return result.Entities.Single(e => e.TableName == table);
    }
}
