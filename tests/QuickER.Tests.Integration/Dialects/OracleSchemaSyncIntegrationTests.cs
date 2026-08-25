using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Oracle;
using QuickER.Provider;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// C: <see cref="SchemaDiffService"/> + <see cref="OracleSyncScriptBuilder"/> +
/// <see cref="OracleSchemaSyncExecutor"/> によるスキーマ同期の実往復を検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleSchemaSyncIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SchemaDiffService _diff = new();
    private readonly OracleSyncScriptBuilder _builder = new();
    private readonly OracleSchemaSyncExecutor _executor = new();
    private readonly OracleSchemaImporter _importer = new();

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
        parent.Columns.Add(Col("name", "VARCHAR2(50)", nullable: true, desc: "名称"));

        var child = new Entity { TableName = "child" };
        var childId = Pk("id");
        var childParentId = Col("parent_id", "NUMBER(10)", nullable: true);
        child.Columns.Add(childId);
        child.Columns.Add(childParentId);
        child.Columns.Add(Col("to_alter", "VARCHAR2(50)", nullable: true));
        child.Columns.Add(Col("to_drop", "NUMBER(10)", nullable: true));

        var rel = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentId.Id, childParentId.Id)],
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
        // 目標: child に added 列を追加、to_alter を VARCHAR2(100)＋NOT NULL に変更、to_drop を削除
        var child2 = new Entity { TableName = "child" };
        child2.Columns.Add(Pk("id"));
        var child2ParentId = Col("parent_id", "NUMBER(10)", nullable: true);
        child2.Columns.Add(child2ParentId);
        child2.Columns.Add(Col("to_alter", "VARCHAR2(100)", nullable: false)); // 型変更＋NULL→NOT NULL
        child2.Columns.Add(Col("added", "NUMBER(10)", nullable: true)); // 追加

        var parentLive = live1.Entities.Single(e => e.TableName == "parent");
        // parent は変更しないので live の状態を目標にも据える（差分を出さない）
        var parentTarget = CloneAsTarget(parentLive);

        // FK を維持するため rel を live 側 ID で組み直す
        var relKeep = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = child2.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                new(parentTarget.Columns.Single(c => c.IsPrimaryKey).Id, child2ParentId.Id),
            ],
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
        alteredCol.DataType.Should().Be("VARCHAR2(100)");
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

        // ========== フェーズ4: DropForeignKey（制約名不明 → PL/SQL ブロック経路） ==========
        // まず制約名の分からない FK を DB に直接張る
        await fixture.ExecuteAsync(
            "ALTER TABLE \"child\" ADD CONSTRAINT \"fk_unknown_name\" "
                + "FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\");",
            Ct
        );
        // ConstraintName を伏せた DropForeignKey 項目を手組みして PL/SQL ブロック経路を通す
        var live4 = await ImportAsync();
        var parentE = live4.Entities.Single(e => e.TableName == "parent");
        var childE = live4.Entities.Single(e => e.TableName == "child");
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
    /// わざと不正な型で AlterColumn を流し、実行が失敗（Committed=false）することを検証する。
    /// </summary>
    /// <remarks>
    /// Oracle の DDL は暗黙コミットされるため、PostgreSQL 版と異なり「スキーマ不変」までは保証されない
    /// （部分適用があり得る）。ここでは Committed=false とエラー報告のみを検証する。
    /// </remarks>
    [Fact(DisplayName = "[Integration] C: 不正な同期は Committed=false・エラーが報告される")]
    public async Task SchemaSync_InvalidAlter_ReportsFailure()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        // 準備: 文字列列 'note' に非数値データを入れておくと NUMBER への変換が失敗する
        await fixture.ExecuteAsync(
            "CREATE TABLE \"t\" (\"id\" NUMBER(10) NOT NULL, \"note\" VARCHAR2(50) NULL, "
                + "CONSTRAINT \"PK_t\" PRIMARY KEY (\"id\"));",
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO \"t\" (\"id\", \"note\") VALUES (1, 'abc');", Ct);

        // note を NUMBER へ ALTER する不正な差分を手組みする（'abc' は NUMBER にできず失敗）
        var invalidAlter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "t",
            ColumnName = "note",
            Column = Col("note", "NUMBER(10)", nullable: true),
            IsSelected = true,
        };

        var script = _builder.Build(
            new SyncPlanner().BuildPlan(new[] { invalidAlter }, new SyncDialectCapabilities())
        );
        var result = await _executor.ExecuteAsync(settings, script, Ct);

        result.Committed.Should().BeFalse("不正な型変換は失敗するはず");
        result.Error.Should().NotBeNullOrEmpty();
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
            "CREATE TABLE \"pk_item\" (\"code\" VARCHAR2(20) NOT NULL, \"id\" NUMBER(10) NOT NULL, "
                + "CONSTRAINT \"PK_pk_item\" PRIMARY KEY (\"id\"));",
            Ct
        );
        await fixture.ExecuteAsync(
            "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-001', 1);",
            Ct
        );
        await fixture.ExecuteAsync(
            "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-002', 2);",
            Ct
        );

        var live = await ImportAsync();
        var liveItem = live.Entities.Single(e => e.TableName == "pk_item");

        // 目標: 列構成・型は変えず、主キーだけ id から code へ移す
        var target = CloneAsTarget(liveItem);
        target.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;

        var caps = new OracleProvider().SyncCapabilities;
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

    /// <summary>
    /// 被参照列が候補キーでなくなる主キー変更は、自動再 ADD される FK が実行時に失敗し、
    /// DDL の暗黙コミットにより<b>部分適用</b>（FK が消えたまま主キーだけ変わる）で残ることを検証する。
    /// </summary>
    /// <remarks>
    /// 既知の限界の現状固定。SQL Server / PostgreSQL は単一トランザクションのためロールバックされる
    /// （<c>SqlServerSchemaSyncIntegrationTests.PrimaryKeyChange_BreakingDependentForeignKey_RollsBack</c>）が、
    /// Oracle は DDL が暗黙コミットされるため元に戻らない。計画側はこのリスクを警告として積む
    /// （<see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/>）。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] C: AlterPrimaryKey: 被参照列が候補キーでなくなる変更は失敗し部分適用で残る"
    )]
    public async Task PrimaryKeyChange_BreakingDependentForeignKey_PartiallyApplies()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        await fixture.ExecuteAsync(
            "CREATE TABLE \"pk_parent\" (\"id\" NUMBER(10) NOT NULL, \"code\" VARCHAR2(20) NOT NULL, "
                + "CONSTRAINT \"PK_pk_parent\" PRIMARY KEY (\"id\"))",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE \"pk_child\" (\"id\" NUMBER(10) NOT NULL, \"parent_id\" NUMBER(10) NULL, "
                + "CONSTRAINT \"PK_pk_child\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"FK_pk_child_pk_parent\" FOREIGN KEY (\"parent_id\") "
                + "REFERENCES \"pk_parent\" (\"id\"))",
            Ct
        );
        await fixture.ExecuteAsync(
            "INSERT INTO \"pk_parent\" (\"id\", \"code\") VALUES (1, 'P-1')",
            Ct
        );
        await fixture.ExecuteAsync(
            "INSERT INTO \"pk_child\" (\"id\", \"parent_id\") VALUES (1, 1)",
            Ct
        );

        var live = await ImportAsync();
        var liveParent = live.Entities.Single(e => e.TableName == "pk_parent");
        var liveChild = live.Entities.Single(e => e.TableName == "pk_child");
        var liveFk = live.Relationships.Single(r => r.TargetEntityId == liveChild.Id);

        // 目標: 親の主キーを id から code へ移す（子の FK は id を参照したまま維持）
        var parentTarget = CloneAsTarget(liveParent);
        parentTarget.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        parentTarget.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
        var childTarget = CloneAsTarget(liveChild);
        var relKeep = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                new(
                    parentTarget.Columns.Single(c => c.Name == "id").Id,
                    childTarget.Columns.Single(c => c.Name == "parent_id").Id
                ),
            ],
            ConstraintName = liveFk.ConstraintName,
        };

        var caps = new OracleProvider().SyncCapabilities;
        var diff = _diff.Compute(
            live.Entities,
            live.Relationships,
            new[] { parentTarget, childTarget },
            new[] { relKeep },
            caps
        );
        var alterPk = diff
            .Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AlterPrimaryKey && i.TableName == "pk_parent"
            )
            .Which;
        alterPk.IsSelected = true;

        var context = new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
        };
        var plan = new SyncPlanner().BuildPlan(diff.Items, caps, context);

        // 計画側は候補キー喪失の恐れを警告として積む（一意制約は取り込んでいないため実行はブロックしない）
        plan.Warnings.Should()
            .Contain(w => w.Kind == SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);

        var script = _builder.Build(plan);
        var result = await _executor.ExecuteAsync(settings, script, Ct);

        // 再 ADD できない FK があるため実行は失敗する（ORA-02270: 一致する一意キー・主キーが無い）
        result
            .Committed.Should()
            .BeFalse($"再 ADD できない FK があるため失敗するはず\nSQL:\n{script}");
        result.Error.Should().NotBeNullOrEmpty();
        // 部分適用があり得る旨の説明が付く（文言は表示言語依存のため culture 安定トークンで確認する）
        result.Error.Should().Contain("Oracle");

        // ---------- Oracle の DDL は暗黙コミット＝部分適用が残る ----------
        var live2 = await ImportAsync();
        var parent2 = live2.Entities.Single(e => e.TableName == "pk_parent");
        parent2
            .Columns.Single(c => c.Name == "code")
            .IsPrimaryKey.Should()
            .BeTrue("主キーの付け替えは適用済みのまま残る");
        parent2.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeFalse();
        live2.Relationships.Should().BeEmpty("自動で外した FK は戻らない（部分適用）");
    }

    // ---------------- ヘルパー ----------------

    /// <summary>主キー変更の検証用テーブルの行を取得する（データ温存の確認用）</summary>
    private async Task<List<(string Code, int Id)>> QueryPkItemRowsAsync()
    {
        var rows = new List<(string Code, int Id)>();

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"code\", \"id\" FROM \"pk_item\" ORDER BY \"id\"";
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
            DataType = "NUMBER(10)",
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

    /// <summary>図に足した一意制約が実 DB へ追加され、外した一意制約が実 DB から消えることを検証する</summary>
    [Fact(DisplayName = "[Integration] C: 一意制約の追加・削除が実 DB へ反映される")]
    public async Task UniqueConstraintSync_AddsAndDrops()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        await fixture.ExecuteAsync(
            "CREATE TABLE \"uq_item\" (\"id\" NUMBER(10) NOT NULL, \"code\" VARCHAR2(20) NOT NULL, "
                + "CONSTRAINT \"PK_uq_item\" PRIMARY KEY (\"id\"));",
            Ct
        );

        // ========== 追加 ==========
        var live = await ImportAsync();
        var target = WithUniqueOnCode(live);
        var addDiff = _diff.Compute(
            live.Entities,
            live.Relationships,
            new[] { target },
            new List<Relationship>()
        );
        // 一意制約の追加は既定で選択される（制約を増やすだけで既存定義を壊さないため）
        addDiff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddUniqueConstraint)
            .Which.IsSelected.Should()
            .BeTrue();

        await ApplyAsync(settings, addDiff.Items);

        var live2 = await ImportAsync();
        var item2 = SingleUqItem(live2);
        var added = item2.UniqueConstraints.Should().ContainSingle().Which;
        added
            .ColumnIds.Select(id => item2.Columns.Single(c => c.Id == id).Name)
            .Should()
            .Equal("code");

        // ========== 削除 ==========
        var target2 = CloneAsTarget(item2);
        target2.UniqueConstraints.Clear();
        var dropDiff = _diff.Compute(
            live2.Entities,
            live2.Relationships,
            new[] { target2 },
            new List<Relationship>()
        );
        var drop = dropDiff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Which;
        // 削除は破壊的のため既定では未選択＝明示的に選ぶ
        drop.IsSelected.Should().BeFalse();
        drop.IsSelected = true;

        await ApplyAsync(settings, dropDiff.Items);

        SingleUqItem(await ImportAsync()).UniqueConstraints.Should().BeEmpty();
    }

    /// <summary>検証用テーブルの取込エンティティを取り出す</summary>
    private static Entity SingleUqItem(SchemaImportResult result) =>
        result.Entities.Single(e => e.TableName == "uq_item");

    /// <summary>取込結果の検証用テーブルへ「code 列の一意制約」を足した目標図を作る</summary>
    private static Entity WithUniqueOnCode(SchemaImportResult live)
    {
        var target = CloneAsTarget(SingleUqItem(live));
        target.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [target.Columns.Single(c => c.Name == "code").Id] }
        );
        return target;
    }

    // ---------------- 複合外部キー ----------------

    /// <summary>複合外部キーの追加・削除が、構成列を保ったまま実 DB へ反映されることを検証する</summary>
    [Fact(DisplayName = "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される")]
    public async Task CompositeForeignKeySync_AddsAndDrops()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        await fixture.ExecuteAsync(
            "CREATE TABLE \"cfk_parent\" (\"a\" NUMBER(10) NOT NULL, \"b\" NUMBER(10) NOT NULL, "
                + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"a\", \"b\"));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE \"cfk_child\" (\"id\" NUMBER(10) NOT NULL, \"a_ref\" NUMBER(10) NOT NULL, "
                + "\"b_ref\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\"));",
            Ct
        );

        // ========== 追加 ==========
        var live = await ImportAsync();
        var (parentTarget, childTarget, targetRel) = BuildCompositeForeignKeyTarget(live);

        var addDiff = _diff.Compute(
            live.Entities,
            live.Relationships,
            new[] { parentTarget, childTarget },
            new[] { targetRel }
        );
        var add = addDiff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddForeignKey)
            .Which;
        add.ForeignKeyColumnPairs.Select(p => (p.ParentColumn, p.ChildColumn))
            .Should()
            .Equal(("a", "a_ref"), ("b", "b_ref"));

        await ApplyAsync(settings, addDiff.Items);

        // 再取込: 構成列を 2 組とも保った 1 本の外部キーとして戻る
        var live2 = await ImportAsync();
        AssertCompositeForeignKey(live2);

        // ========== 削除 ==========
        var dropDiff = _diff.Compute(
            live2.Entities,
            live2.Relationships,
            live2.Entities.Select(CloneAsTarget).ToArray(),
            new List<Relationship>()
        );
        var drop = dropDiff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropForeignKey)
            .Which;
        // 削除は破壊的のため既定では未選択＝明示的に選ぶ
        drop.IsSelected = true;

        await ApplyAsync(settings, dropDiff.Items);

        (await ImportAsync()).Relationships.Should().BeEmpty();
    }

    /// <summary>
    /// 複合主キーの変更に巻き込まれる複合外部キーが、全構成列のまま自動で外して張り直されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] C: 複合主キー変更に伴う複合外部キーの張り直しで構成列が失われない"
    )]
    public async Task CompositeForeignKey_SurvivesPrimaryKeyChange()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        await fixture.ExecuteAsync(
            "CREATE TABLE \"cfk_parent\" (\"id\" NUMBER(10) NOT NULL, \"code\" NUMBER(10) NOT NULL, "
                + "\"a\" NUMBER(10) NOT NULL, \"b\" NUMBER(10) NOT NULL, "
                + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"id\", \"code\"), "
                + "CONSTRAINT \"UQ_cfk_parent_a_b\" UNIQUE (\"a\", \"b\"));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE \"cfk_child\" (\"id\" NUMBER(10) NOT NULL, \"a_ref\" NUMBER(10) NOT NULL, "
                + "\"b_ref\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"FK_cfk_child_cfk_parent\" FOREIGN KEY (\"a_ref\", \"b_ref\") "
                + "REFERENCES \"cfk_parent\" (\"a\", \"b\"));",
            Ct
        );

        var live = await ImportAsync();

        // 親の複合主キー (id, code) を (id) へ縮める（被参照列 (a, b) の一意制約はそのまま）
        var parentTarget = CloneAsTarget(live.Entities.Single(e => e.TableName == "cfk_parent"));
        parentTarget.Columns.Single(c => c.Name == "code").IsPrimaryKey = false;
        var childTarget = CloneAsTarget(live.Entities.Single(e => e.TableName == "cfk_child"));

        // 外部キー自体は図にも残す（FK 差分を出さず、主キー変更だけを見るため）
        var relKeep = BuildCompositeRelationship(parentTarget, childTarget);

        var caps = new OracleProvider().SyncCapabilities;
        var diff = _diff.Compute(
            live.Entities,
            live.Relationships,
            new[] { parentTarget, childTarget },
            new[] { relKeep },
            caps
        );
        // 主キー変更は既定で未選択のため明示的に選択する
        diff.Items.Single(i => i.Kind == SchemaDiffKind.AlterPrimaryKey).IsSelected = true;

        var context = new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
        };
        var plan = new SyncPlanner().BuildPlan(diff.Items, caps, context);

        // 被参照列 (a, b) は同期後も一意制約として残るため、候補キー喪失の警告は出ない
        plan.Warnings.Should().BeEmpty();

        var script = _builder.Build(plan);
        var result = await _executor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"主キー変更に失敗: {result.Error}\nSQL:\n{script}");

        // 主キーは縮み、複合外部キーは構成列を保ったまま残る
        var live2 = await ImportAsync();
        live2
            .Entities.Single(e => e.TableName == "cfk_parent")
            .Columns.Where(c => c.IsPrimaryKey)
            .Select(c => c.Name)
            .Should()
            .Equal("id");
        AssertCompositeForeignKey(live2);
    }

    /// <summary>取込結果へ複合外部キーを足した目標図（親・子・リレーション）を作る</summary>
    private static (
        Entity Parent,
        Entity Child,
        Relationship Relationship
    ) BuildCompositeForeignKeyTarget(SchemaImportResult live)
    {
        var parent = CloneAsTarget(live.Entities.Single(e => e.TableName == "cfk_parent"));
        var child = CloneAsTarget(live.Entities.Single(e => e.TableName == "cfk_child"));
        return (parent, child, BuildCompositeRelationship(parent, child));
    }

    /// <summary>(a, b) → (a_ref, b_ref) の複合外部キーを表すリレーションを組み立てる</summary>
    private static Relationship BuildCompositeRelationship(Entity parent, Entity child) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                new(
                    parent.Columns.Single(c => c.Name == "a").Id,
                    child.Columns.Single(c => c.Name == "a_ref").Id
                ),
                new(
                    parent.Columns.Single(c => c.Name == "b").Id,
                    child.Columns.Single(c => c.Name == "b_ref").Id
                ),
            ],
            ConstraintName = "FK_cfk_child_cfk_parent",
        };

    /// <summary>取込結果に、構成列を 2 組とも保った複合外部キーが 1 本だけあることを検証する</summary>
    private static void AssertCompositeForeignKey(SchemaImportResult live)
    {
        var parent = live.Entities.Single(e => e.TableName == "cfk_parent");
        var child = live.Entities.Single(e => e.TableName == "cfk_child");
        var rel = live.Relationships.Should().ContainSingle().Which;

        rel.ColumnPairs.Select(p =>
                (
                    parent.Columns.Single(c => c.Id == p.SourceColumnId).Name,
                    child.Columns.Single(c => c.Id == p.TargetColumnId).Name
                )
            )
            .Should()
            .Equal(("a", "a_ref"), ("b", "b_ref"));
    }
}
