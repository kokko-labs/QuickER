using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// C: <see cref="MySqlSyncScriptBuilder"/> + <see cref="MySqlSchemaSyncExecutor"/> による同期のうち、差分からは作れない項目
/// （名前不明の外部キーの削除）と、計画が警告しない失敗（存在しない型）を実 DB で検証する統合テスト。
/// </summary>
/// <remarks>
/// 差分から作れる同期は、同期計画の期待の行の実 DB の段（<c>SyncPlanExpectationRows.RealMySql.cs</c>）が持つ。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlSchemaSyncIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly MySqlSyncScriptBuilder _builder = new();
    private readonly MySqlSchemaSyncExecutor _executor = new();
    private readonly MySqlSchemaImporter _importer = new();

    /// <summary>
    /// 制約名を渡さない外部キーの削除（名前不明＝プリペアド動的 SQL でカタログを逆引きする経路）が、実 DB の外部キーを外すことを
    /// 検証する。MySQL は外部キーの名前を保存するので差分の外部キーの削除は必ず名前を持ち、この経路は手で組んだ項目でしか通らない。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] C: 名前不明の外部キーの削除がカタログの逆引きで実 DB に適用される"
    )]
    public async Task SchemaSync_DropForeignKeyWithoutName_UsesCatalogLookup()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        // MySQL の FK は参照インデックスを要求するため parent.id（PK）を参照する FK を直接張る
        await fixture.ExecuteAsync(
            "CREATE TABLE `parent` (`id` int NOT NULL, `name` varchar(50) NULL, "
                + "CONSTRAINT `PK_parent` PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `child` (`id` int NOT NULL, `parent_id` int NULL, "
                + "`to_alter` varchar(100) NOT NULL, `added` int NULL, "
                + "CONSTRAINT `PK_child` PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "ALTER TABLE `child` ADD CONSTRAINT `fk_unknown_name` "
                + "FOREIGN KEY (`parent_id`) REFERENCES `parent` (`id`);",
            Ct
        );
        var live = await ImportAsync();
        var parentE = live.Entities.Single(e => e.TableName == "parent");
        var childE = live.Entities.Single(e => e.TableName == "child");
        var dropFkUnknown = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "child",
            ParentEntity = parentE,
            ChildEntity = childE,
            ForeignKeyName = null, // 不明 → プリペアド動的 SQL でカタログ逆引き
            IsSelected = true,
        };
        await ApplyAsync(settings, new[] { dropFkUnknown });

        (await ImportAsync()).Relationships.Should().BeEmpty();
    }

    /// <summary>
    /// わざと不正な DDL（存在しない型への ALTER）を流し、実行が失敗（Committed=false）し、
    /// エラー内容が報告されることを検証する。MySQL の DDL は暗黙コミットされ部分適用があり得るため、
    /// スキーマ不変のアサートは行わない。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] C: 不正な同期は Committed=false・エラー報告（部分適用の可能性・スキーマ不変は検証しない）"
    )]
    public async Task SchemaSync_InvalidAlter_ReportsFailure()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        await fixture.ExecuteAsync(
            "CREATE TABLE `t` (`id` int NOT NULL, `note` varchar(50) NULL, "
                + "CONSTRAINT `PK_t` PRIMARY KEY (`id`));",
            Ct
        );

        // note を存在しない型 'notatype' へ ALTER する不正な差分を手組みする（構文エラーで失敗）
        var invalidAlter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "t",
            ColumnName = "note",
            Column = Col("note", "notatype", nullable: true),
            IsSelected = true,
        };

        var script = _builder.Build(
            new SyncPlanner().BuildPlan(new[] { invalidAlter }, TestDialectCapabilities.Neutral)
        );
        var result = await _executor.ExecuteAsync(settings, script, Ct);

        result.Committed.Should().BeFalse("不正な DDL は失敗するはず");
        result.Error.Should().NotBeNullOrEmpty();
        // 部分適用があり得る旨のメッセージが表示言語（resx）で構築されること。
        // 文言は表示言語依存のため、方言に紐づく culture 安定トークン "MySQL" で確認する。
        result.Error.Should().Contain("MySQL");
    }

    /// <summary>
    /// 複数文のうち後半でエラーが起き、前半の DDL は暗黙コミットにより適用済みとなること（部分適用）を検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] C: 複数文の途中失敗で前半は部分適用される")]
    public async Task SchemaSync_PartialApply_OnMidScriptFailure()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        // 1 文目: 正当な AddTable。2 文目: 不正な型の AddTable（失敗）
        var ok = new Entity { TableName = "ok_table" };
        ok.Columns.Add(Pk("id"));
        var bad = new Entity { TableName = "bad_table" };
        var badPk = Pk("id");
        bad.Columns.Add(badPk);
        bad.Columns.Add(Col("c", "notatype", nullable: true));

        var items = new[]
        {
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "ok_table",
                Entity = ok,
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "bad_table",
                Entity = bad,
                IsSelected = true,
            },
        };

        var script = _builder.Build(
            new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral)
        );
        var result = await _executor.ExecuteAsync(settings, script, Ct);

        result.Committed.Should().BeFalse();

        // 1 文目（ok_table）は暗黙コミットで残っているはず（部分適用）
        var live = await ImportAsync();
        live.Entities.Select(e => e.TableName).Should().Contain("ok_table");
        live.Entities.Select(e => e.TableName).Should().NotContain("bad_table");
    }

    // ---------------- ヘルパー ----------------

    private static Column Pk(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    private static Column Col(string name, string type, bool nullable) =>
        new()
        {
            Name = name,
            DataType = type,
            IsNullable = nullable,
        };

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
