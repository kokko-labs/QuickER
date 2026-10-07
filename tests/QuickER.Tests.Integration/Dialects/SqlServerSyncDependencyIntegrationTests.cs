using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// SQL Server の同期で、外部キーの差分の照合（名前の無いリレーション・外部キーの作り直し）が
/// 実 DB で成り立つことを検証する統合テスト。
/// </summary>
/// <remarks>
/// 図は GUI と同じく live と別の Guid 空間に作る（<see cref="SyncAsync"/>）。
/// Docker 必須（<see cref="SqlServerContainerFixture"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerSyncDependencyIntegrationTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SqlServerProvider _provider = new();

    /// <summary>
    /// 名前の無いリレーションの図を QuickER の DDL で適用したあと、同じ図と差分を取っても外部キーの差分が出ず、
    /// 既定の選択のまま同期を実行しても失敗しないことを検証する。
    /// </summary>
    /// <remarks>
    /// DDL は名前の無い外部キーに既定名 <c>FK_{子}_{親}</c> を付ける。差分が素の名前（null）で比べると
    /// 別物に見え、何も変えていなくても追加（既定で選択）と削除が出て、既定のまま実行すると
    /// 同じ名前の外部キーを足そうとして失敗していた。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 名前の無いリレーションの図は DDL 適用後に外部キーの差分を出さない"
    )]
    public async Task UnnamedRelationship_ProducesNoForeignKeyDiffAfterDdl()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var diagram = BuildParentChildDiagram(constraintName: null);
        await fixture.ApplyDdlAsync(diagram, Ct);

        var live = await ImportAsync();
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            _provider.SyncCapabilities
        );

        diff.Items.Should().NotContain(i => IsForeignKeyDiff(i));

        // 既定の選択のまま実行しても失敗しない（外部キーは 1 本のまま）
        var result = await ExecuteAsync(diff.Items, live);
        result.Committed.Should().BeTrue(result.Error);
        (await ImportAsync()).Relationships.Should().ContainSingle();
    }

    /// <summary>
    /// 同じテーブルの組に名前の無いリレーションが 2 本ある図で、DDL が適用でき、適用後の差分に外部キーが出ないことを検証する。
    /// </summary>
    /// <remarks>
    /// 既定名だけでは 2 本が同じ名前 <c>FK_{子}_{親}</c> になり、DDL の 2 本目が名前の重複で失敗していた。
    /// 2 本目は子の列名を足した名前になり、差分も同じ規則で名前を求めるので、何も変えていなければ外部キーの差分は出ない。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 名前の無い同じ組の外部キー 2 本の図は DDL が通り、適用後に外部キーの差分を出さない"
    )]
    public async Task UnnamedSamePairRelationships_DdlAppliesAndProducesNoDiff()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var diagram = BuildParentChildDiagram(constraintName: null);
        var parent = diagram.Entities[0];
        var child = diagram.Entities[1];
        var backup = new Column
        {
            Name = "backup_parent_id",
            DataType = "int",
            IsNullable = true,
        };
        child.Columns.Add(backup);
        diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                ColumnPairs = [new(parent.Columns[0].Id, backup.Id)],
            }
        );

        await fixture.ApplyDdlAsync(diagram, Ct);

        var live = await ImportAsync();
        live.Relationships.Select(r => r.ConstraintName)
            .Should()
            .BeEquivalentTo("FK_sd_child_sd_parent", "FK_sd_child_sd_parent_backup_parent_id");

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            _provider.SyncCapabilities
        );

        diff.Items.Should().NotContain(i => IsForeignKeyDiff(i));
    }

    /// <summary>
    /// 名前の無い多対多が同じ組の外部キーより前に並ぶ図でも、外部キーは既定名で作られ、
    /// 既定名の外部キーを持つ DB に対して外部キーの差分を出さないことを検証する。
    /// </summary>
    /// <remarks>
    /// 多対多は DDL が外部キーを作らないので既定名を取らない。取らせると本物の外部キーが
    /// <c>FK_{子}_{親}_{列}</c> へずれ、既定名で外部キーを作った DB（それまでの版の DDL）との同期で、
    /// 外部キーの追加と削除が出ていた。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 名前の無い多対多が先に並んでも外部キーは既定名で作られ、既定名の DB と差分を出さない"
    )]
    public async Task UnnamedManyToManyFirst_ForeignKeyKeepsDefaultName()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // 既定名の外部キーを持つ DB（多対多の無い図の DDL）
        await fixture.ApplyDdlAsync(BuildParentChildDiagram(constraintName: null), Ct);

        var diagram = BuildParentChildDiagram(constraintName: null);
        diagram.Relationships.Insert(
            0,
            new Relationship
            {
                SourceEntityId = diagram.Entities[0].Id,
                TargetEntityId = diagram.Entities[1].Id,
                Type = RelationshipType.ManyToMany,
            }
        );

        var live = await ImportAsync();
        live.Relationships.Select(r => r.ConstraintName).Should().Equal("FK_sd_child_sd_parent");

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            _provider.SyncCapabilities
        );

        diff.Items.Should().NotContain(i => IsForeignKeyDiff(i));

        // 多対多が先に並ぶ図の DDL も同じ既定名で外部キーを作る
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ApplyDdlAsync(diagram, Ct);

        (await ImportAsync())
            .Relationships.Select(r => r.ConstraintName)
            .Should()
            .Equal("FK_sd_child_sd_parent");
    }

    /// <summary>
    /// 参照アクションだけを変えた外部キーは 1 つの外部キー変更になり、既定の選択（未選択）のまま実行しても失敗せず、
    /// 選んで実行すると新しい参照アクションで作り直されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 参照アクションの変更は外部キー変更として作り直され、既定のままでも失敗しない"
    )]
    public async Task ReferentialActionChange_AlterForeignKey_Applies()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ApplyDdlAsync(BuildParentChildDiagram(constraintName: null), Ct);

        // 既定の選択のまま（外部キー変更は未選択）実行する
        var (defaultResult, _, defaultDiff, _) = await SyncAsync(
            (_, relationships) => relationships[0].OnDelete = ForeignKeyReferentialAction.Cascade,
            select: null
        );

        var alter = defaultDiff.Items.Should().ContainSingle(i => IsForeignKeyDiff(i)).Which;
        alter.Kind.Should().Be(SchemaDiffKind.AlterForeignKey);
        defaultResult.Committed.Should().BeTrue(defaultResult.Error);
        (await ImportAsync())
            .Relationships.Should()
            .ContainSingle()
            .Which.OnDelete.Should()
            .Be(ForeignKeyReferentialAction.NoAction);

        // 外部キー変更を選んで実行すると作り直される
        var (result, script, _, _) = await SyncAsync(
            (_, relationships) => relationships[0].OnDelete = ForeignKeyReferentialAction.Cascade,
            select: item => item.Kind == SchemaDiffKind.AlterForeignKey
        );

        result.Committed.Should().BeTrue($"{result.Error}\n{script}");
        var fk = (await ImportAsync()).Relationships.Should().ContainSingle().Which;
        fk.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        fk.ConstraintName.Should().Be("FK_sd_child_sd_parent");
    }

    /// <summary>
    /// 主キーの構成列の型を変える同期（両端を同じ型へ）が、主キー変更を選ばなくても主キー・外部キーを
    /// 外して付け直して成功することを検証する（SQL Server の Msg 5074 対策）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 主キー列と参照する列を同じ型へ変える同期が主キー・外部キーを保って成功する"
    )]
    public async Task PrimaryKeyColumnTypeChange_BothEnds_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ApplyDdlAsync(BuildParentChildDiagram(constraintName: null), Ct);
        await fixture.ExecuteAsync("INSERT INTO [sd_parent] ([id]) VALUES (1);", Ct);
        await fixture.ExecuteAsync("INSERT INTO [sd_child] ([id], [parent_id]) VALUES (1, 1);", Ct);

        var (result, script, _, plan) = await SyncAsync(
            (entities, _) =>
            {
                entities.Single(e => e.TableName == "sd_parent").Columns[0].DataType = "bigint";
                entities.Single(e => e.TableName == "sd_child").Columns[1].DataType = "bigint";
            },
            select: item => item.Kind == SchemaDiffKind.AlterColumn
        );

        plan.Warnings.Should().BeEmpty();
        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");

        var live = await ImportAsync();
        var parent = live.Entities.Single(e => e.TableName == "sd_parent");
        parent.Columns.Single(c => c.Name == "id").DataType.Should().Be("bigint");
        parent.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("id");
        live.Entities.Single(e => e.TableName == "sd_child")
            .Columns.Single(c => c.Name == "parent_id")
            .DataType.Should()
            .Be("bigint");
        live.Relationships.Should().ContainSingle();
    }

    /// <summary>
    /// 外部キーの片側の列だけ型を変える同期は、外部キーを外し直さずに警告し、実行は列の変更で失敗して
    /// 何も変わらない（外部キーも型も元のまま）ことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 外部キーの片側だけ型を変えると警告し、実行しても外部キーと型は元のまま"
    )]
    public async Task ForeignKeyColumnTypeChange_OneEnd_WarnsAndChangesNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ApplyDdlAsync(BuildParentChildDiagram(constraintName: null), Ct);

        var (result, _, _, plan) = await SyncAsync(
            (entities, _) =>
                entities.Single(e => e.TableName == "sd_parent").Columns[0].DataType = "bigint",
            select: item => item.Kind == SchemaDiffKind.AlterColumn
        );

        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .Which.Detail.Should()
            .Be("FK_sd_child_sd_parent");
        result.Committed.Should().BeFalse();

        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "sd_parent")
            .Columns.Single(c => c.Name == "id")
            .DataType.Should()
            .Be("int");
        live.Relationships.Should().ContainSingle();
    }

    /// <summary>
    /// 参照される主キーの変更と、参照する子表の削除を同時に選んでも成功することを検証する
    /// （外部キーは外すだけで、消える子表へ付け直さない）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 参照される主キーの変更と子表の削除を同時に選んでも成功する"
    )]
    public async Task AlterPrimaryKeyWithChildDropTable_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var diagram = BuildParentChildDiagram(constraintName: null);
        diagram
            .Entities[0]
            .Columns.Add(
                new Column
                {
                    Name = "code",
                    DataType = "int",
                    IsNullable = false,
                }
            );
        await fixture.ApplyDdlAsync(diagram, Ct);

        var (result, script, _, _) = await SyncAsync(
            (entities, relationships) =>
            {
                var parent = entities.Single(e => e.TableName == "sd_parent");
                parent.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
                parent.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
                entities.RemoveAll(e => e.TableName == "sd_child");
                relationships.Clear();
            },
            select: item => item.Kind is SchemaDiffKind.AlterPrimaryKey or SchemaDiffKind.DropTable
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var live = await ImportAsync();
        live.Entities.Select(e => e.TableName).Should().Equal("sd_parent");
        live.Entities[0].GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("code");
    }

    // ---------------- ヘルパー ----------------

    /// <summary>親 sd_parent(id) と子 sd_child(id, parent_id) と、parent_id → id の外部キーを持つ図を作る</summary>
    private static ErDiagram BuildParentChildDiagram(string? constraintName)
    {
        var parent = new Entity
        {
            TableName = "sd_parent",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = "sd_child",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "parent_id",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                    ConstraintName = constraintName,
                },
            ],
        };
    }

    /// <summary>
    /// live を取り込み、GUI と同じく別 Guid 空間の図を作って <paramref name="editTarget"/> で変え、
    /// 差分 → 計画 → スクリプト → 実行まで通す。
    /// </summary>
    /// <param name="select">選ぶ差分の述語。<c>null</c> なら差分計算が付けた既定の選択のまま実行する</param>
    private async Task<(
        SchemaSyncResult Result,
        string Script,
        SchemaDiff Diff,
        SyncPlan Plan
    )> SyncAsync(
        System.Action<List<Entity>, List<Relationship>> editTarget,
        System.Func<SchemaDiffItem, bool>? select
    )
    {
        var live = await ImportAsync();

        var entityIdMap = new Dictionary<System.Guid, System.Guid>();
        var columnIdMap = new Dictionary<System.Guid, System.Guid>();
        var targetEntities = new List<Entity>();

        foreach (var entity in live.Entities)
        {
            var clone = entity.Clone(preserveId: false);
            entityIdMap[entity.Id] = clone.Id;

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                columnIdMap[entity.Columns[i].Id] = clone.Columns[i].Id;
            }

            targetEntities.Add(clone);
        }

        var targetRelationships = live
            .Relationships.Select(r => new Relationship
            {
                SourceEntityId = entityIdMap[r.SourceEntityId],
                TargetEntityId = entityIdMap[r.TargetEntityId],
                Type = r.Type,
                ColumnPairs = [.. r.ColumnPairs.Select(p => p.Clone(columnIdMap))],
                ConstraintName = r.ConstraintName,
                OnDelete = r.OnDelete,
                OnUpdate = r.OnUpdate,
            })
            .ToList();

        editTarget(targetEntities, targetRelationships);

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            targetEntities,
            targetRelationships,
            _provider.SyncCapabilities
        );

        if (select is not null)
        {
            foreach (var item in diff.Items)
            {
                item.IsSelected = select(item);
            }
        }

        var (result, script, plan) = await ExecuteWithScriptAsync(diff.Items, live);
        return (result, script, diff, plan);
    }

    /// <summary>差分項目を live の文脈つきで計画 → スクリプト → 実行する</summary>
    private async Task<SchemaSyncResult> ExecuteAsync(
        IEnumerable<SchemaDiffItem> items,
        SchemaImportResult live
    ) => (await ExecuteWithScriptAsync(items, live)).Result;

    /// <summary>差分項目を live の文脈つきで計画 → スクリプト → 実行し、スクリプトと計画も返す</summary>
    private async Task<(
        SchemaSyncResult Result,
        string Script,
        SyncPlan Plan
    )> ExecuteWithScriptAsync(IEnumerable<SchemaDiffItem> items, SchemaImportResult live)
    {
        var plan = new SyncPlanner().BuildPlan(
            items,
            _provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
            }
        );
        var script = _provider.SyncScriptBuilder.Build(plan);
        var result = await _provider.SyncExecutor.ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );

        return (result, script, plan);
    }

    /// <summary>コンテナの DB からスキーマを取り込む</summary>
    private Task<SchemaImportResult> ImportAsync() =>
        _provider.SchemaImporter.ImportAsync(fixture.ConnectionString, 120, Ct);

    /// <summary>外部キーの差分（追加・削除・作り直し）か</summary>
    private static bool IsForeignKeyDiff(SchemaDiffItem item) =>
        item.Kind
            is SchemaDiffKind.AddForeignKey
                or SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.AlterForeignKey;
}
