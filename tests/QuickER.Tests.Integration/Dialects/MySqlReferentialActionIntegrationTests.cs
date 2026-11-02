using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
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
    /// SET DEFAULT の図から作った DDL が句を出さずに注意コメントを付け、そのまま適用できることを検証する。
    /// 宣言した <c>SET DEFAULT</c> が NO ACTION として動くこと（前提の実測）と、適用後に外部キーの差分が残らないことは、
    /// 同期計画の期待の行 realdb-mysql-set-default-is-omitted-and-leaves-no-fk-diff が固定する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL: SET DEFAULT の図の DDL は句を省いて注意コメントを付け、適用できる"
    )]
    public async Task SetDefault_DdlOmitsClauseWithNoteAndApplies()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // SET DEFAULT の図から DDL を作ると、句は出ずに注意コメントが付く
        var (diagram, _) = BuildDiagram();
        var ddl = _provider.DdlGenerator.Build(diagram);
        ddl.Should().Contain(MySqlReferentialAction.SetDefaultNote);
        ddl.Should().NotContain("SET DEFAULT ");
        await fixture.ExecuteAsync(ddl, Ct);
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
