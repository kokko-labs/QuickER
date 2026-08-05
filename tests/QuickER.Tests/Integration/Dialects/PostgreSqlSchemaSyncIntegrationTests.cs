using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// C: <see cref="SchemaDiffService"/> + <see cref="PostgreSqlSyncScriptBuilder"/> +
/// <see cref="PostgreSqlSchemaSyncExecutor"/> によるスキーマ同期の実往復を検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlSchemaSyncIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SchemaDiffService _diff = new();
    private readonly PostgreSqlSyncScriptBuilder _builder = new();
    private readonly PostgreSqlSchemaSyncExecutor _executor = new();
    private readonly PostgreSqlSchemaImporter _importer = new();

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
        var childParentId = Col("parent_id", "integer", nullable: true);
        child.Columns.Add(childId);
        child.Columns.Add(childParentId);
        child.Columns.Add(Col("to_alter", "varchar(50)", nullable: true));
        child.Columns.Add(Col("to_drop", "integer", nullable: true));

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
        // 目標: child に added 列を追加、to_alter を varchar(100)＋NOT NULL に変更、to_drop を削除
        var child2 = new Entity { TableName = "child" };
        child2.Columns.Add(Pk("id"));
        var child2ParentId = Col("parent_id", "integer", nullable: true);
        child2.Columns.Add(child2ParentId);
        child2.Columns.Add(Col("to_alter", "varchar(100)", nullable: false)); // 型変更＋NULL→NOT NULL
        child2.Columns.Add(Col("added", "integer", nullable: true)); // 追加

        var parentLive = live1.Entities.Single(e => e.TableName == "parent");
        // parent は変更しないので live の状態を目標にも据える（差分を出さない）
        var parentTarget = CloneAsTarget(parentLive);

        // FK を維持するため rel を live 側 ID で組み直す
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
        // 目標から FK を外す → DropForeignKey が出る。live の rel には ConstraintName が入っている
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

        // ========== フェーズ4: DropForeignKey（制約名不明 → DO ブロック経路） ==========
        // まず制約名の分からない FK を DB に直接張る
        await fixture.ExecuteAsync(
            "ALTER TABLE \"child\" ADD CONSTRAINT \"fk_unknown_name\" "
                + "FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\");",
            Ct
        );
        // ConstraintName を伏せた DropForeignKey 項目を手組みして DO ブロック経路を通す
        var live4 = await ImportAsync();
        var parentE = live4.Entities.Single(e => e.TableName == "parent");
        var childE = live4.Entities.Single(e => e.TableName == "child");
        var dropFkUnknown = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "child",
            ParentEntity = parentE,
            ChildEntity = childE,
            ForeignKeyName = null, // 不明 → DO ブロックでカタログ逆引き
            IsSelected = true,
        };
        await ApplyAsync(settings, new[] { dropFkUnknown });

        var live5 = await ImportAsync();
        live5.Relationships.Should().BeEmpty();

        // ========== フェーズ5: DropTable ==========
        // parent を DB から削除する（child のみ残す）。FK は既に無い
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
    /// わざと不正な型で AlterColumn を流し、実行が失敗（Committed=false）してロールバックされ、
    /// スキーマが変化しないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] C: 不正な同期はロールバックされ Committed=false・スキーマ不変"
    )]
    public async Task SchemaSync_InvalidAlter_RollsBack()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        // 準備: 文字列列 'note' に非数値データを入れておくと integer への変換が失敗する
        await fixture.ExecuteAsync(
            "CREATE TABLE \"t\" (\"id\" integer NOT NULL, \"note\" varchar(50) NULL, "
                + "CONSTRAINT \"PK_t\" PRIMARY KEY (\"id\"));",
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO \"t\" (\"id\", \"note\") VALUES (1, 'abc');", Ct);

        // note を integer へ ALTER する不正な差分を手組みする（'abc' は integer にできず失敗）
        var invalidAlter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "t",
            ColumnName = "note",
            Column = Col("note", "integer", nullable: true),
            IsSelected = true,
        };

        var script = _builder.Build(
            new SyncPlanner().BuildPlan(new[] { invalidAlter }, new SyncDialectCapabilities())
        );
        var result = await _executor.ExecuteAsync(settings, script, Ct);

        result.Committed.Should().BeFalse("不正な型変換は失敗するはず");
        result.Error.Should().NotBeNullOrEmpty();

        // スキーマ不変であることを再取込で確認（note は varchar(50) のまま）
        var live = await ImportAsync();
        var t = live.Entities.Single(e => e.TableName == "t");
        t.Columns.Single(c => c.Name == "note").DataType.Should().Be("varchar(50)");
    }

    /// <summary>
    /// 主キーの付け替え（id → code）が実 DB へ適用され、行データが温存されることを検証する。
    /// </summary>
    /// <remarks>参照してくる FK が無いテーブルで、主キー変更単体の往復を確認する</remarks>
    [Fact(DisplayName = "[Integration] C: 主キーを id から code へ付け替えてもデータが温存される")]
    public async Task SchemaSync_AlterPrimaryKey_MovesPrimaryKeyAndKeepsRows()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        await fixture.ExecuteAsync(
            "CREATE TABLE \"pk_item\" (\"code\" varchar(20) NOT NULL, \"id\" integer NOT NULL, "
                + "CONSTRAINT \"PK_pk_item\" PRIMARY KEY (\"id\"));",
            Ct
        );
        await fixture.ExecuteAsync(
            "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-001', 1), ('A-002', 2);",
            Ct
        );

        var live = await ImportAsync();
        var liveItem = live.Entities.Single(e => e.TableName == "pk_item");

        // 目標: 列構成・型は変えず、主キーだけ id から code へ移す
        var target = CloneAsTarget(liveItem);
        target.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;

        var caps = new PostgreSqlProvider().SyncCapabilities;
        var diff = _diff.Compute(
            live.Entities,
            live.Relationships,
            new[] { target },
            new List<Relationship>(),
            caps
        );

        // 主キー変更は既定で未選択のため、対象項目だけを明示的に選択する
        var alterPk = diff
            .Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AlterPrimaryKey && i.TableName == "pk_item"
            )
            .Which;
        alterPk.IsSelected = true;

        var context = new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
        };
        var script = _builder.Build(new SyncPlanner().BuildPlan(diff.Items, caps, context));
        var result = await _executor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"主キー変更に失敗: {result.Error}\nSQL:\n{script}");

        // 再取込: 主キーが code へ移っている
        var live2 = await ImportAsync();
        var item2 = live2.Entities.Single(e => e.TableName == "pk_item");
        item2.Columns.Single(c => c.Name == "code").IsPrimaryKey.Should().BeTrue();
        item2.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeFalse();

        // 行データは失われない
        var rows = await QueryPkItemRowsAsync();
        rows.Should().Equal(("A-001", 1), ("A-002", 2));
    }

    // ---------------- ヘルパー ----------------

    /// <summary>主キー変更の検証用テーブルの行を取得する（データ温存の確認用）</summary>
    private async Task<List<(string Code, int Id)>> QueryPkItemRowsAsync()
    {
        var rows = new List<(string Code, int Id)>();

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"code\", \"id\" FROM \"pk_item\" ORDER BY \"id\";";
        await using var reader = await cmd.ExecuteReaderAsync(Ct).ConfigureAwait(false);

        while (await reader.ReadAsync(Ct).ConfigureAwait(false))
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return rows;
    }

    private static Column Pk(string name) =>
        new()
        {
            Name = name,
            DataType = "integer",
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

    /// <summary>取込済みエンティティを「目標図」として使うために ID ごと複製する</summary>
    private static Entity CloneAsTarget(Entity e) => e.Clone(preserveId: true);

    private async Task<SchemaImportResult> ImportAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var r = await _importer.ImportAsync(conn, Ct);
        return new SchemaImportResult { Entities = r.Entities, Relationships = r.Relationships };
    }

    private async Task ApplyAsync(DbConnectionSettings settings, IEnumerable<SchemaDiffItem> items)
    {
        var script = _builder.Build(
            new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities())
        );
        var result = await _executor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL:\n{script}");
    }
}
