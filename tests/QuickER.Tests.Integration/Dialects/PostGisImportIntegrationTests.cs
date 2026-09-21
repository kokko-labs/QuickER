using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// PostGIS を入れた実 PostgreSQL からの取込が、そのまま DDL 生成まで通ることを検証する統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// PostGIS の型は<b>括弧引数に語を取る</b>（<c>geometry(Point,4326)</c>）。型は識別子でも文字列リテラルでも
/// ないため <see cref="SqlTypeText"/> が構造的ホワイトリストで絞っており、そこに数値しか通さないと
/// <b>この 1 列のせいで図全体の DDL 生成・同期生成が止まる</b>（入口の検証はテーブル単位でなく図単位）。
/// 実際に PostGIS の DB を取り込んで DDL を作るところまでを通しで固定する。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostGisContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostGisImportIntegrationTests(PostGisContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>(列名, 作成する型, 取込で期待される表記)</summary>
    private static readonly (string Column, string CreateType, string Expected)[] GeometryCases =
    [
        new("g_plain", "geometry", "geometry"),
        new("g_point", "geometry(Point,4326)", "geometry(Point,4326)"),
        new("g_mpoly", "geometry(MultiPolygon)", "geometry(MultiPolygon)"),
        new("g_pointz", "geometry(PointZ,4326)", "geometry(PointZ,4326)"),
        new("gg_point", "geography(Point,4326)", "geography(Point,4326)"),
    ];

    /// <summary>
    /// PostGIS の型を取り込み、表記が宣言どおりで、かつ DDL 生成が図全体として成功することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] PostGIS: 型修飾つきの空間型を取り込み、図全体の DDL 生成が成功する"
    )]
    public async Task Import_PostGisTypes_AreCarriedThroughAndDdlCanBeGenerated()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var geometryColumns = string.Join(
            ",\n",
            GeometryCases.Select(c => $"    \"{c.Column}\" {c.CreateType}")
        );

        // 空間列を持たない普通のテーブルも置く＝「1 列の型のせいで図全体が止まる」ことの検出用
        await fixture.ExecuteAsync(
            $"""
            CREATE TABLE "site" (
                "id" integer NOT NULL,
                "name" varchar(50) NOT NULL,
                CONSTRAINT "PK_site" PRIMARY KEY ("id")
            );
            CREATE TABLE "shape" (
                "id" integer NOT NULL,
            {geometryColumns},
                CONSTRAINT "PK_shape" PRIMARY KEY ("id")
            );
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        // PostGIS 拡張が所有するテーブル（spatial_ref_sys 等）は従来どおり取込対象外
        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("site", "shape");

        var shape = result.Entities.Single(e => e.TableName == "shape");
        var catalog = new PostgreSqlTypeCatalog();

        foreach (var (columnName, createType, expected) in GeometryCases)
        {
            var column = shape.Columns.Single(c => c.Name == columnName);
            column.DataType.Should().Be(expected, $"列 {columnName}（{createType}）の取込型表記");

            SqlTypeText
                .IsSafe(column.DataType)
                .Should()
                .BeTrue($"取込型 '{column.DataType}' は DDL へ出せる表記であること");

            // 正規型には空間型に対応する概念が無いので解析できないのが正しい（verbatim 併記に委ねる）
            catalog.TryParse(column.DataType, out _).Should().BeFalse();
        }

        // 取込表記が 1 つでも弾かれると、この Build が図全体を止める（回帰の本体はここ）
        var ddl = new PostgreSqlDdlGenerator().Build(
            new ErDiagram
            {
                Entities = result.Entities.ToList(),
                Relationships = result.Relationships.ToList(),
            }
        );

        ddl.Should().Contain("geometry(Point,4326)").And.Contain("geography(Point,4326)");

        // 空間型を持たないテーブルも巻き添えで落ちていないこと
        ddl.Should().Contain("\"site\"");
    }

    /// <summary>生成した DDL を実 PostGIS へ再適用でき、再取込が 1 回目と一致することを検証する</summary>
    [Fact(
        DisplayName = "[Integration] PostGIS: 取込→DDL 再生成→再適用が成功し、再取込が 1 回目と一致する"
    )]
    public async Task ImportedPostGisSchema_RegeneratedDdl_CanBeReapplied()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var geometryColumns = string.Join(
            ",\n",
            GeometryCases.Select(c => $"    \"{c.Column}\" {c.CreateType}")
        );

        await fixture.ExecuteAsync(
            $"""
            CREATE TABLE "shape" (
                "id" integer NOT NULL,
            {geometryColumns},
                CONSTRAINT "PK_shape" PRIMARY KEY ("id")
            );
            """,
            Ct
        );

        List<Entity> firstEntities;
        List<Relationship> firstRelationships;

        await using (var conn = await fixture.OpenConnectionAsync(Ct))
        {
            var imported = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);
            firstEntities = imported.Entities.ToList();
            firstRelationships = imported.Relationships.ToList();
        }

        var regenerated = new PostgreSqlDdlGenerator().Build(
            new ErDiagram { Entities = firstEntities, Relationships = firstRelationships }
        );

        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(regenerated, Ct);

        await using var reconn = await fixture.OpenConnectionAsync(Ct);
        var second = await new PostgreSqlSchemaImporter().ImportAsync(reconn, Ct);

        SchemaReapplyAssertions.ShouldRoundTrip(
            firstEntities,
            firstRelationships,
            second.Entities,
            second.Relationships
        );
    }
}
