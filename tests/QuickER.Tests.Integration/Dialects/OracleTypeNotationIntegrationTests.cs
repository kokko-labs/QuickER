using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// Oracle で「同期しても消えない差分」が出ないこと（型表記の閉包・Oracle が持たない参照アクション）を
/// 実 DB で確かめる統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleTypeNotationIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 選択肢と TryFormat の全表記で表を作って取り込み直すと、正規化した図の表記と取込の表記が一致し、
    /// 差分に AlterColumn が残らないことを検証する（判定の本体は <see cref="TypeNotationClosure"/>）。
    /// </summary>
    [Fact(DisplayName = "[Integration] Oracle: 選択肢と TryFormat の全表記で同期→再取込→差分 0")]
    public async Task TypeNotation_IsClosedUnderSyncAndImport()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var provider = new OracleProvider();
        // 選択肢と TryFormat の表記に加え、指摘で実際に食い違った綴り（図に手で書かれ得る別表記）も確かめる
        var types = TypeNotationClosure
            .CandidateTypes(provider.TypeCatalog, TypeNotationClosure.DefaultSamples)
            .Concat([
                "TIMESTAMP",
                "TIMESTAMP WITH TIME ZONE",
                "TIMESTAMP WITH LOCAL TIME ZONE",
                "INTEGER",
                "SMALLINT",
                "NUMBER(10,0)",
                "NUMBER(*)",
                "FLOAT",
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await TypeNotationClosure.AssertClosedAsync(
            provider,
            fixture.ToDbConnectionSettings(),
            types,
            "tn_",
            Ct
        );
    }

    /// <summary>
    /// ON UPDATE と ON DELETE SET DEFAULT を持つ図を同期しても、取り込み直した後の差分に外部キーの項目が
    /// 残らないことを検証する。
    /// </summary>
    /// <remarks>
    /// Oracle はどちらも持たないので DDL・同期は句を省いて外部キーを作る。比べるときに図のそれらを
    /// NO ACTION へ畳まないと、DB は NO ACTION のまま差分が出続け、既定どおり追加だけを選ぶと同じ名前の
    /// 外部キーで ORA-02275 になる。表せる ON DELETE CASCADE は畳まずに比べる（同期で DB に入るので差分 0）。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] Oracle: ON UPDATE・ON DELETE SET DEFAULT の図を同期しても外部キーの差分が残らない"
    )]
    public async Task UnsupportedReferentialActions_LeaveNoForeignKeyDiff()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var parent = new Entity
        {
            TableName = "FKA_PARENT",
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "NUMBER(10)",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = "FKA_CHILD",
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "NUMBER(10)",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column { Name = "P1", DataType = "NUMBER(10)" },
                new Column { Name = "P2", DataType = "NUMBER(10)" },
            },
        };
        List<Relationship> relationships =
        [
            new()
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                ConstraintName = "FK_FKA_UPDATE",
                OnDelete = ForeignKeyReferentialAction.Cascade,
                OnUpdate = ForeignKeyReferentialAction.Cascade,
            },
            new()
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[2].Id)],
                ConstraintName = "FK_FKA_SETDEFAULT",
                OnDelete = ForeignKeyReferentialAction.SetDefault,
                OnUpdate = ForeignKeyReferentialAction.NoAction,
            },
        ];

        var provider = new OracleProvider();
        var settings = fixture.ToDbConnectionSettings();
        var connectionString = provider.BuildConnectionString(settings);
        var live = await provider.SchemaImporter.ImportAsync(connectionString, 60, Ct);
        var create = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            [parent, child],
            relationships,
            provider.SyncCapabilities
        );

        foreach (var item in create.Items)
        {
            item.IsSelected = true;
        }

        var script = provider.SyncScriptBuilder.Build(
            new SyncPlanner().BuildPlan(
                create.Items,
                provider.SyncCapabilities,
                new SyncPlanContext
                {
                    LiveEntities = live.Entities,
                    LiveRelationships = live.Relationships,
                }
            )
        );
        var result = await provider.SyncExecutor.ExecuteAsync(settings, script, Ct);
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\n{script}");

        var reimported = await provider.SchemaImporter.ImportAsync(connectionString, 60, Ct);
        reimported
            .Relationships.Single(r => r.ConstraintName == "FK_FKA_UPDATE")
            .OnDelete.Should()
            .Be(ForeignKeyReferentialAction.Cascade, "表せる ON DELETE CASCADE は DB に入ること");

        var after = new SchemaDiffService().Compute(
            reimported.Entities,
            reimported.Relationships,
            [parent, child],
            relationships,
            provider.SyncCapabilities
        );

        after
            .Items.Where(i =>
                i.Kind
                    is SchemaDiffKind.AddForeignKey
                        or SchemaDiffKind.DropForeignKey
                        or SchemaDiffKind.AlterForeignKey
            )
            .Select(i => i.Description)
            .Should()
            .BeEmpty("Oracle が持たない参照アクションは、同期した後の差分に残らないこと");
    }
}
