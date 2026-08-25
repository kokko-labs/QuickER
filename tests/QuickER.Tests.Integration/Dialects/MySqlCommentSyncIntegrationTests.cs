using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Provider;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// B: <see cref="MySqlSyncScriptBuilder"/> + <see cref="MySqlSchemaSyncExecutor"/> による
/// 説明（テーブル COMMENT / 列 MODIFY）の同期往復を検証する統合テスト。設定・更新・削除の 3 パターンを実 DB で確認する。
/// </summary>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlCommentSyncIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// SetTableDescription / SetColumnDescription の差分から DDL を生成・実行し、
    /// 再取込で Description が反映されること（設定→更新→削除）を、日本語・シングルクォート含む文字列で検証する。
    /// 列の COMMENT 設定は MODIFY による完全再指定で行われ、既存の型・NULL 制約が保持されることも確認する。
    /// </summary>
    [Fact(DisplayName = "[Integration] B: COMMENT 同期で説明が設定→更新→削除まで往復する")]
    public async Task CommentSync_SetUpdateDelete_RoundTrips()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // ---------- 準備: テーブルを作成しておく ----------
        await fixture.ExecuteAsync(
            "CREATE TABLE `items` (`id` int NOT NULL, `name` varchar(50) NULL, "
                + "CONSTRAINT `PK_items` PRIMARY KEY (`id`));",
            Ct
        );

        var settings = fixture.ToDbConnectionSettings();
        var builder = new MySqlSyncScriptBuilder();
        var executor = new MySqlSchemaSyncExecutor();
        var importer = new MySqlSchemaImporter();

        const string TableDesc = "商品テーブル 'マスタ'"; // 日本語＋シングルクォート
        const string ColumnDesc = "商品名 'name' 列";

        // 列の説明設定に用いる Entity（型・NULL 制約の復元元）
        Entity ItemsEntity() =>
            new()
            {
                TableName = "items",
                Columns =
                {
                    new Column
                    {
                        Name = "name",
                        DataType = "varchar(50)",
                        IsNullable = true,
                    },
                },
            };

        // ---------- 1) 設定 ----------
        await ApplyAsync(
            builder,
            executor,
            settings,
            new[]
            {
                MakeTableDesc("items", TableDesc, oldDesc: null),
                MakeColumnDesc("items", "name", ItemsEntity(), ColumnDesc, oldDesc: null),
            }
        );

        var afterSet = await ImportSingleAsync(fixture, importer, "items");
        afterSet.Description.Should().Be(TableDesc);
        afterSet.Columns.Single(c => c.Name == "name").Description.Should().Be(ColumnDesc);
        // MODIFY による再指定で型・NULL 制約が保持されていること
        var nameCol = afterSet.Columns.Single(c => c.Name == "name");
        nameCol.DataType.Should().Be("varchar(50)");
        nameCol.IsNullable.Should().BeTrue();

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
                MakeColumnDesc("items", "name", ItemsEntity(), ColumnDesc2, oldDesc: ColumnDesc),
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
                MakeColumnDesc("items", "name", ItemsEntity(), string.Empty, oldDesc: ColumnDesc2),
            }
        );

        var afterDelete = await ImportSingleAsync(fixture, importer, "items");
        afterDelete.Description.Should().BeEmpty();
        afterDelete.Columns.Single(c => c.Name == "name").Description.Should().BeEmpty();
    }

    /// <summary>
    /// 列を MODIFY で ALTER する際、対象列に既存コメントがあれば COMMENT を含め、
    /// 型変更後もコメントが消えないことを検証する（MySQL の MODIFY 完全再指定に対する回帰テスト）。
    /// </summary>
    [Fact(DisplayName = "[Integration] B: AlterColumn（MODIFY）で既存コメントが消えない")]
    public async Task AlterColumn_PreservesExistingComment()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // 既存コメント付きの列を作っておく
        await fixture.ExecuteAsync(
            "CREATE TABLE `t` (`id` int NOT NULL, "
                + "`memo` varchar(50) NULL COMMENT '既存コメント', "
                + "CONSTRAINT `PK_t` PRIMARY KEY (`id`));",
            Ct
        );

        var settings = fixture.ToDbConnectionSettings();
        var builder = new MySqlSyncScriptBuilder();
        var executor = new MySqlSchemaSyncExecutor();
        var importer = new MySqlSchemaImporter();

        // memo を varchar(100) へ ALTER する。Description に既存コメントを載せて COMMENT を含めさせる
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "t",
            ColumnName = "memo",
            Column = new Column
            {
                Name = "memo",
                DataType = "varchar(100)",
                IsNullable = true,
                Description = "既存コメント",
            },
            IsSelected = true,
        };

        await ApplyAsync(builder, executor, settings, new[] { alter });

        var after = await ImportSingleAsync(fixture, importer, "t");
        var memo = after.Columns.Single(c => c.Name == "memo");
        memo.DataType.Should().Be("varchar(100)");
        memo.Description.Should().Be("既存コメント");
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
        Entity entity,
        string newDesc,
        string? oldDesc
    ) =>
        new()
        {
            Kind = SchemaDiffKind.SetColumnDescription,
            TableName = table,
            ColumnName = column,
            Entity = entity,
            NewDescription = newDesc,
            OldDescription = oldDesc,
        };

    private static async Task ApplyAsync(
        MySqlSyncScriptBuilder builder,
        MySqlSchemaSyncExecutor executor,
        DbConnectionSettings settings,
        IEnumerable<SchemaDiffItem> items
    )
    {
        var script = builder.Build(
            new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities())
        );
        var result = await executor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"COMMENT 同期に失敗: {result.Error}\nSQL:\n{script}");
    }

    private static async Task<Entity> ImportSingleAsync(
        MySqlContainerFixture fixture,
        MySqlSchemaImporter importer,
        string table
    )
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await importer.ImportAsync(conn, Ct);
        return result.Entities.Single(e => e.TableName == table);
    }
}
