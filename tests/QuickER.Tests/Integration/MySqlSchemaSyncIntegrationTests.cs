using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Provider;

namespace QuickER.Tests.Integration;

/// <summary>
/// C: <see cref="SchemaDiffService"/> + <see cref="MySqlSyncScriptBuilder"/> +
/// <see cref="MySqlSchemaSyncExecutor"/> によるスキーマ同期の実往復を検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlSchemaSyncIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SchemaDiffService _diff = new();
    private readonly MySqlSyncScriptBuilder _builder = new();
    private readonly MySqlSchemaSyncExecutor _executor = new();
    private readonly MySqlSchemaImporter _importer = new();

    /// <summary>
    /// AddTable（説明付き）/ AddColumn / AlterColumn / DropColumn / DropTable /
    /// AddForeignKey / DropForeignKey（制約名既知・不明の両方）が実 DB へ適用され、往復で検証できることを確認する。
    /// </summary>
    [Fact(DisplayName = "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される")]
    public async Task SchemaSync_AppliesAllKinds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        // ========== フェーズ1: 空 DB → 初期スキーマ（AddTable 説明付き・AddColumn・AddForeignKey） ==========
        var parent = new Entity { TableName = "parent", Description = "親テーブルの説明" };
        var parentId = Pk("id");
        parent.Columns.Add(parentId);
        parent.Columns.Add(Col("name", "varchar(50)", nullable: true, desc: "名称"));

        var child = new Entity { TableName = "child" };
        var childId = Pk("id");
        var childParentId = Col("parent_id", "int", nullable: true);
        child.Columns.Add(childId);
        child.Columns.Add(childParentId);
        child.Columns.Add(Col("to_alter", "varchar(50)", nullable: true));
        child.Columns.Add(Col("to_drop", "int", nullable: true));

        var rel = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parentId.Id,
            TargetColumnId = childParentId.Id,
            ConstraintName = "FK_child_parent",
            OnDelete = ForeignKeyReferentialAction.Cascade,
        };

        var live0 = await ImportAsync();
        var diff1 = _diff.Compute(
            live0.Entities,
            live0.Relationships,
            new[] { parent, child },
            new[] { rel }
        );
        diff1
            .Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == "parent");
        diff1
            .Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == "child");
        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddForeignKey);
        diff1
            .Items.Should()
            .Contain(i =>
                i.Kind == SchemaDiffKind.SetTableDescription
                && i.NewDescription == "親テーブルの説明"
            );

        await ApplyAsync(settings, diff1.Items);

        // 再取込で往復確認
        var live1 = await ImportAsync();
        live1.Entities.Select(e => e.TableName).Should().BeEquivalentTo("parent", "child");
        live1
            .Entities.Single(e => e.TableName == "parent")
            .Description.Should()
            .Be("親テーブルの説明");
        live1
            .Entities.Single(e => e.TableName == "parent")
            .Columns.Single(c => c.Name == "name")
            .Description.Should()
            .Be("名称");
        live1.Relationships.Should().ContainSingle();
        live1.Relationships[0].ConstraintName.Should().Be("FK_child_parent");
        live1.Relationships[0].OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);

        // ========== フェーズ2: AddColumn / AlterColumn（型変更・NULL→NOT NULL） / DropColumn ==========
        var child2 = new Entity { TableName = "child" };
        child2.Columns.Add(Pk("id"));
        var child2ParentId = Col("parent_id", "int", nullable: true);
        child2.Columns.Add(child2ParentId);
        child2.Columns.Add(Col("to_alter", "varchar(100)", nullable: false)); // 型変更＋NULL→NOT NULL
        child2.Columns.Add(Col("added", "int", nullable: true)); // 追加

        var parentLive = live1.Entities.Single(e => e.TableName == "parent");
        var parentTarget = CloneAsTarget(parentLive);

        var relKeep = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = child2.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parentTarget.Columns.Single(c => c.IsPrimaryKey).Id,
            TargetColumnId = child2ParentId.Id,
            ConstraintName = "FK_child_parent",
            OnDelete = ForeignKeyReferentialAction.Cascade,
        };

        var diff2 = _diff.Compute(
            live1.Entities,
            live1.Relationships,
            new[] { parentTarget, child2 },
            new[] { relKeep }
        );
        diff2
            .Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.AddColumn && i.ColumnName == "added");
        diff2
            .Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName == "to_alter");
        diff2
            .Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName == "to_drop");

        // 破壊的差分は既定で未選択のため、全項目を選択して実行する
        foreach (var item in diff2.Items)
        {
            item.IsSelected = true;
        }

        await ApplyAsync(settings, diff2.Items);

        var live2 = await ImportAsync();
        var childLive2 = live2.Entities.Single(e => e.TableName == "child");
        childLive2.Columns.Select(c => c.Name).Should().Contain("added");
        childLive2.Columns.Select(c => c.Name).Should().NotContain("to_drop");
        var alteredCol = childLive2.Columns.Single(c => c.Name == "to_alter");
        alteredCol.DataType.Should().Be("varchar(100)");
        alteredCol.IsNullable.Should().BeFalse();

        // ========== フェーズ3: DropForeignKey（制約名既知） ==========
        var parentTarget3 = CloneAsTarget(live2.Entities.Single(e => e.TableName == "parent"));
        var childTarget3 = CloneAsTarget(childLive2);
        var diff3 = _diff.Compute(
            live2.Entities,
            live2.Relationships,
            new[] { parentTarget3, childTarget3 },
            new List<Relationship>() // FK 無し
        );
        var dropFk = diff3.Items.Single(i => i.Kind == SchemaDiffKind.DropForeignKey);
        dropFk.ForeignKeyName.Should().Be("FK_child_parent"); // 制約名既知
        dropFk.IsSelected = true;
        await ApplyAsync(settings, new[] { dropFk });

        var live3 = await ImportAsync();
        live3.Relationships.Should().BeEmpty();

        // ========== フェーズ4: DropForeignKey（制約名不明 → プリペアド動的 SQL 経路） ==========
        // MySQL の FK は参照インデックスを要求するため parent.id（PK）を参照する FK を直接張る
        await fixture.ExecuteAsync(
            "ALTER TABLE `child` ADD CONSTRAINT `fk_unknown_name` "
                + "FOREIGN KEY (`parent_id`) REFERENCES `parent` (`id`);",
            Ct
        );
        var live4 = await ImportAsync();
        var parentE = live4.Entities.Single(e => e.TableName == "parent");
        var childE = live4.Entities.Single(e => e.TableName == "child");
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

        var live5 = await ImportAsync();
        live5.Relationships.Should().BeEmpty();

        // ========== フェーズ5: DropTable ==========
        var childTarget5 = CloneAsTarget(live5.Entities.Single(e => e.TableName == "child"));
        var diff5 = _diff.Compute(
            live5.Entities,
            live5.Relationships,
            new[] { childTarget5 },
            new List<Relationship>()
        );
        var dropTable = diff5.Items.Single(i =>
            i.Kind == SchemaDiffKind.DropTable && i.TableName == "parent"
        );
        dropTable.IsSelected = true;
        await ApplyAsync(settings, new[] { dropTable });

        var live6 = await ImportAsync();
        live6.Entities.Select(e => e.TableName).Should().BeEquivalentTo("child");
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

        var script = _builder.Build(new[] { invalidAlter });
        var result = await _executor.ExecuteAsync(settings, script, Ct);

        result.Committed.Should().BeFalse("不正な DDL は失敗するはず");
        result.Error.Should().NotBeNullOrEmpty();
        // MySQL は暗黙コミットのため部分適用があり得る旨がメッセージに含まれること
        result.Error.Should().Contain("暗黙コミット");
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

        var script = _builder.Build(items);
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

    private static Column Col(string name, string type, bool nullable, string desc = "") =>
        new()
        {
            Name = name,
            DataType = type,
            IsNullable = nullable,
            Description = desc,
        };

    private static Entity CloneAsTarget(Entity e) => e.Clone(preserveId: true);

    private async Task<SchemaImportResult> ImportAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var r = await _importer.ImportAsync(conn, Ct);
        return new SchemaImportResult { Entities = r.Entities, Relationships = r.Relationships };
    }

    private async Task ApplyAsync(DbConnectionSettings settings, IEnumerable<SchemaDiffItem> items)
    {
        var script = _builder.Build(items);
        var result = await _executor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL:\n{script}");
    }
}
