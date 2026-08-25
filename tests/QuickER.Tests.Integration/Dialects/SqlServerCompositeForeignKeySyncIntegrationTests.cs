using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// C: SQL Server の複合外部キー同期（追加・削除・主キー変更に伴う張り直し）を実 DB で検証する統合テスト。
/// </summary>
/// <remarks>
/// 同方言のスキーマ同期テストには localhost の実 SQL Server へ接続する
/// <see cref="SqlServerSchemaSyncIntegrationTests"/> もあるが、そちらは Docker を使わない別機構のため、
/// 環境非依存で回る Testcontainers 側（<see cref="SqlServerContainerFixture"/>）にこのクラスを置く。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerCompositeForeignKeySyncIntegrationTests(
    SqlServerContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SchemaDiffService _diff = new();
    private readonly SqlServerSyncScriptBuilder _builder = new();
    private readonly SqlServerSchemaSyncExecutor _executor = new();
    private readonly SqlServerSchemaImporter _importer = new();

    /// <summary>複合外部キーの追加・削除が、構成列を保ったまま実 DB へ反映されることを検証する</summary>
    [Fact(DisplayName = "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される")]
    public async Task CompositeForeignKeySync_AddsAndDrops()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var settings = fixture.ToDbConnectionSettings();

        await fixture.ExecuteAsync(
            "CREATE TABLE [cfk_parent] ([a] int NOT NULL, [b] int NOT NULL, "
                + "CONSTRAINT [PK_cfk_parent] PRIMARY KEY ([a], [b]));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE [cfk_child] ([id] int NOT NULL, [a_ref] int NOT NULL, "
                + "[b_ref] int NOT NULL, CONSTRAINT [PK_cfk_child] PRIMARY KEY ([id]));",
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
            "CREATE TABLE [cfk_parent] ([id] int NOT NULL, [code] int NOT NULL, "
                + "[a] int NOT NULL, [b] int NOT NULL, "
                + "CONSTRAINT [PK_cfk_parent] PRIMARY KEY ([id], [code]), "
                + "CONSTRAINT [UQ_cfk_parent_a_b] UNIQUE ([a], [b]));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE [cfk_child] ([id] int NOT NULL, [a_ref] int NOT NULL, "
                + "[b_ref] int NOT NULL, CONSTRAINT [PK_cfk_child] PRIMARY KEY ([id]), "
                + "CONSTRAINT [FK_cfk_child_cfk_parent] FOREIGN KEY ([a_ref], [b_ref]) "
                + "REFERENCES [cfk_parent] ([a], [b]));",
            Ct
        );

        var live = await ImportAsync();

        // 親の複合主キー (id, code) を (id) へ縮める（被参照列 (a, b) の一意制約はそのまま）
        var parentTarget = CloneAsTarget(live.Entities.Single(e => e.TableName == "cfk_parent"));
        parentTarget.Columns.Single(c => c.Name == "code").IsPrimaryKey = false;
        var childTarget = CloneAsTarget(live.Entities.Single(e => e.TableName == "cfk_child"));

        // 外部キー自体は図にも残す（FK 差分を出さず、主キー変更だけを見るため）
        var relKeep = BuildCompositeRelationship(parentTarget, childTarget);

        var caps = new SqlServerProvider().SyncCapabilities;
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

    // ---------------- ヘルパー ----------------

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
            new SyncPlanner().BuildPlan(items, new SqlServerProvider().SyncCapabilities)
        );
        var result = await _executor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL:\n{script}");
    }
}
