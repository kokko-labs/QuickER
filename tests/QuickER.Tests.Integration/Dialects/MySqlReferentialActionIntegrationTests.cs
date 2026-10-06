using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using MySqlConnector;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// MySQL（InnoDB）の参照アクション <c>SET DEFAULT</c> の扱いを実 DB で検証する統合テスト。
/// </summary>
/// <remarks>
/// InnoDB は <c>SET DEFAULT</c> を宣言として保存するが効かせない（親を消すと外部キーエラー＝<c>NO ACTION</c> と同じ）。
/// QuickER は DDL・同期で句を省き、差分の比較でも <c>NO ACTION</c> へ畳む（<see cref="MySqlReferentialAction"/>）。
/// Docker 必須（<see cref="MySqlContainerFixture"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlReferentialActionIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly MySqlProvider _provider = new();

    /// <summary>
    /// 宣言した <c>SET DEFAULT</c> は実際には <c>NO ACTION</c> として動くこと（前提の実測）と、
    /// SET DEFAULT の図から作った DDL を適用して取り込み直しても外部キーの差分が残らないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL: SET DEFAULT は効かないので句を省き、適用後に外部キーの差分が残らない"
    )]
    public async Task SetDefault_IsOmitted_AndLeavesNoForeignKeyDiff()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // 前提: SET DEFAULT を宣言した外部キーでも、親を消すと外部キーエラーになる（RESTRICT と同じ）
        await fixture.ExecuteAsync(
            """
            CREATE TABLE ra_raw_parent (id int NOT NULL, PRIMARY KEY (id));
            CREATE TABLE ra_raw_child (id int NOT NULL, parent_id int NULL DEFAULT 0, PRIMARY KEY (id),
                CONSTRAINT fk_ra_raw FOREIGN KEY (parent_id) REFERENCES ra_raw_parent (id)
                ON DELETE SET DEFAULT ON UPDATE SET DEFAULT);
            INSERT INTO ra_raw_parent VALUES (1);
            INSERT INTO ra_raw_child VALUES (1, 1);
            """,
            Ct
        );
        var delete = async () => await fixture.ExecuteAsync("DELETE FROM ra_raw_parent;", Ct);
        await delete.Should().ThrowAsync<MySqlException>();

        // SET DEFAULT の図から DDL を作ると、句は出ずに注意コメントが付く
        var (diagram, relationship) = BuildDiagram();
        var ddl = _provider.DdlGenerator.Build(diagram);
        ddl.Should().Contain(MySqlReferentialAction.SetDefaultNote);
        ddl.Should().NotContain("SET DEFAULT ");
        await fixture.ExecuteAsync(ddl, Ct);

        // 取り込み直して同じ図と比べても、宣言を持つ既存の外部キー（ra_raw）と比べても差分は出ない
        var live = await _provider.SchemaImporter.ImportAsync(fixture.ConnectionString, 120, Ct);
        var rawChild = live.Entities.Single(e => e.TableName == "ra_raw_child");
        var rawParent = live.Entities.Single(e => e.TableName == "ra_raw_parent");
        var rawLive = live.Relationships.Single(r => r.ConstraintName == "fk_ra_raw");
        rawLive.OnDelete.Should().Be(ForeignKeyReferentialAction.SetDefault, "DB は宣言を保存する");
        var rawTarget = new Relationship
        {
            SourceEntityId = rawLive.SourceEntityId,
            TargetEntityId = rawLive.TargetEntityId,
            Type = rawLive.Type,
            ColumnPairs = rawLive.ColumnPairs.ToList(),
            ConstraintName = rawLive.ConstraintName,
            OnDelete = ForeignKeyReferentialAction.NoAction,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };

        var targets = diagram.Entities.Concat([rawParent, rawChild]).ToList();
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            targets,
            [relationship, rawTarget],
            _provider.SyncCapabilities
        );

        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.AddForeignKey
                || i.Kind == SchemaDiffKind.DropForeignKey
                || i.Kind == SchemaDiffKind.AlterForeignKey
            );
    }

    /// <summary>親 ra_parent ← 子 ra_child の外部キーを ON DELETE / ON UPDATE とも SET DEFAULT で持つ図を作る</summary>
    private static (ErDiagram Diagram, Relationship Relationship) BuildDiagram()
    {
        var parent = new Entity { TableName = "ra_parent" };
        parent.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        var child = new Entity { TableName = "ra_child" };
        child.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        child.Columns.Add(
            new Column
            {
                Name = "parent_id",
                DataType = "int",
                IsNullable = true,
            }
        );
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
            ConstraintName = "fk_ra",
            OnDelete = ForeignKeyReferentialAction.SetDefault,
            OnUpdate = ForeignKeyReferentialAction.SetDefault,
        };

        return (
            new ErDiagram { Entities = [parent, child], Relationships = [relationship] },
            relationship
        );
    }
}
