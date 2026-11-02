using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 複合外部キー（<see cref="Relationship.ColumnPairs"/> が 2 組以上）が、DDL 生成・スキーマ署名・同期スクリプトのどこでも
/// 構成列を失わずに扱われることを検証する（差分計算と同期計画は、同期計画の期待の行 <c>diff-composite-fk-*</c>・
/// <c>plan-composite-fk-*</c>＝SyncPlanExpectationRows.CompositeForeignKeys が見る）。
/// </summary>
/// <remarks>
/// 列ペアが外部キー定義の唯一の正本で、推測フォールバック（親の主キー先頭列・命名規約による子列）は無い。
/// そのため「ペア 0 件」「解決できない列を含む」外部キーは各層でスキップされる——その規則も併せて固定する。
/// </remarks>
public sealed class CompositeForeignKeyTests
{
    // ---------------- 図の組み立て ----------------

    /// <summary>複合主キー (a, b) の親と、複合外部キー (a_ref, b_ref) を持つ子からなる図を組み立てる</summary>
    /// <param name="dataType">両テーブルの列に使う方言別の型名</param>
    private static (
        ErDiagram Diagram,
        Entity Parent,
        Entity Child,
        Relationship Relationship
    ) BuildCompositeDiagram(string dataType = "int")
    {
        var parent = new Entity { TableName = "parent_t" };
        var parentA = new Column
        {
            Name = "a",
            DataType = dataType,
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parentB = new Column
        {
            Name = "b",
            DataType = dataType,
            IsPrimaryKey = true,
            IsNullable = false,
        };
        parent.Columns.AddRange([parentA, parentB]);

        var child = new Entity { TableName = "child_t" };
        var childId = new Column
        {
            Name = "id",
            DataType = dataType,
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childA = new Column
        {
            Name = "a_ref",
            DataType = dataType,
            IsNullable = false,
        };
        var childB = new Column
        {
            Name = "b_ref",
            DataType = dataType,
            IsNullable = false,
        };
        child.Columns.AddRange([childId, childA, childB]);

        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentA.Id, childA.Id), new(parentB.Id, childB.Id)],
            ConstraintName = "FK_child_t_parent_t",
        };

        var diagram = new ErDiagram
        {
            Entities = { parent, child },
            Relationships = { relationship },
        };

        return (diagram, parent, child, relationship);
    }

    // ---------------- DDL 生成（5 方言） ----------------

    /// <summary>5 方言の DDL が複合外部キーを 1 本の複数列 <c>FOREIGN KEY</c> 句として出力することを検証する</summary>
    /// <remarks>
    /// 期待句は方言のクォート記号だけが違い、構成列の並び（宣言順）は共通。1 列ずつの外部キーへ分裂したり
    /// 先頭列だけへ縮んだりしないことを固定する。
    /// </remarks>
    [Theory(DisplayName = "5 方言の DDL が複合 FK を複数列の FOREIGN KEY 句として出力する")]
    [InlineData("SqlServer", "FOREIGN KEY ([a_ref], [b_ref]) REFERENCES [parent_t] ([a], [b])")]
    [InlineData(
        "PostgreSql",
        "FOREIGN KEY (\"a_ref\", \"b_ref\") REFERENCES \"parent_t\" (\"a\", \"b\")"
    )]
    [InlineData("MySql", "FOREIGN KEY (`a_ref`, `b_ref`) REFERENCES `parent_t` (`a`, `b`)")]
    [InlineData(
        "Oracle",
        "FOREIGN KEY (\"a_ref\", \"b_ref\") REFERENCES \"parent_t\" (\"a\", \"b\")"
    )]
    [InlineData(
        "Sqlite",
        "FOREIGN KEY (\"a_ref\", \"b_ref\") REFERENCES \"parent_t\" (\"a\", \"b\")"
    )]
    public void DdlGenerators_EmitMultiColumnForeignKey(string dialect, string expectedClause)
    {
        var (diagram, _, _, _) = BuildCompositeDiagram(DataTypeFor(dialect));

        BuildDdl(dialect, diagram).Should().Contain(expectedClause);
    }

    /// <summary>列ペアを持たないリレーションは 5 方言とも <c>FOREIGN KEY</c> 句を出力しないことを検証する</summary>
    /// <remarks>
    /// 推測フォールバック（親の主キー先頭列・<c>{親表}_{PK列}</c> の命名規約）を廃止したため、
    /// 列ペア未設定は「外部キーを作れない」＝黙ってスキップになる。
    /// </remarks>
    [Theory(DisplayName = "列ペアなしのリレーションは 5 方言とも FOREIGN KEY を出力しない")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("Oracle")]
    [InlineData("Sqlite")]
    public void DdlGenerators_SkipRelationshipWithoutColumnPairs(string dialect)
    {
        var (diagram, _, _, relationship) = BuildCompositeDiagram(DataTypeFor(dialect));
        relationship.ColumnPairs.Clear();

        BuildDdl(dialect, diagram).Should().NotContain("FOREIGN KEY");
    }

    /// <summary>方言名から DDL を生成する</summary>
    private static string BuildDdl(string dialect, ErDiagram diagram) =>
        dialect switch
        {
            "SqlServer" => new SqlServerDdlGenerator().Build(diagram),
            "PostgreSql" => new PostgreSqlDdlGenerator().Build(diagram),
            "MySql" => new MySqlDdlGenerator().Build(diagram),
            "Oracle" => new OracleDdlGenerator().Build(diagram),
            _ => new SqliteDdlGenerator().Build(diagram),
        };

    /// <summary>方言ごとの整数型名（DDL は型名をそのまま透過するため、句の比較には影響しない）</summary>
    private static string DataTypeFor(string dialect) =>
        dialect switch
        {
            "PostgreSql" => "integer",
            "Oracle" => "NUMBER(10)",
            _ => "int",
        };

    // ---------------- スキーマ署名 ----------------

    /// <summary>構成列ペアの並びが違えばスキーマ署名も変わることを検証する</summary>
    /// <remarks>
    /// <c>(a, b) → (a_ref, b_ref)</c> と <c>(b, a) → (a_ref, b_ref)</c> は別の外部キー定義なので、
    /// 「同じ列集合だから同じ」とは畳まない（署名は宣言順を含める）。
    /// </remarks>
    [Fact(DisplayName = "スキーマ署名は複合 FK の列ペアの順序を反映する")]
    public void SchemaSignature_ReflectsColumnPairOrder()
    {
        var (diagram, _, _, relationship) = BuildCompositeDiagram();
        var original = SchemaSignature.Compute(diagram.Entities, diagram.Relationships);

        relationship.ColumnPairs.Reverse();

        SchemaSignature.Compute(diagram.Entities, diagram.Relationships).Should().NotBe(original);
    }

    // ---------------- 同期スクリプト ----------------

    /// <summary>同期スクリプトが複合外部キーを複数列の <c>ADD CONSTRAINT</c> として出力することを検証する</summary>
    [Fact(DisplayName = "同期スクリプトの AddForeignKey が複数列で出力される")]
    public void SyncScript_AddForeignKey_EmitsMultipleColumns()
    {
        var (live, _, _, _) = BuildCompositeDiagram();
        var (target, _, _, _) = CloneDiagram(live);

        // live 側に外部キーが無い状態から追加する
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            [],
            target.Entities,
            target.Relationships,
            TestDialectCapabilities.Neutral
        );
        var addItem = diff.Items.Single(i => i.Kind == SchemaDiffKind.AddForeignKey);
        addItem.IsSelected = true;

        var plan = new SyncPlanner().BuildPlan([addItem], TestDialectCapabilities.Neutral);
        var sql = new SqlServerSyncScriptBuilder().Build(plan);

        sql.Should().Contain("FOREIGN KEY ([a_ref], [b_ref]) REFERENCES [parent_t] ([a], [b])");
    }

    /// <summary>図を深く複製する（列・リレーションの Id は維持する）</summary>
    private static (
        ErDiagram Diagram,
        Entity Parent,
        Entity Child,
        Relationship Relationship
    ) CloneDiagram(ErDiagram source)
    {
        var entities = source.Entities.Select(e => e.Clone(preserveId: true)).ToList();
        var relationships = source
            .Relationships.Select(r => new Relationship
            {
                Id = r.Id,
                SourceEntityId = r.SourceEntityId,
                TargetEntityId = r.TargetEntityId,
                Type = r.Type,
                ColumnPairs = [.. r.ColumnPairs.Select(p => p.Clone())],
                ConstraintName = r.ConstraintName,
                OnDelete = r.OnDelete,
                OnUpdate = r.OnUpdate,
            })
            .ToList();

        var diagram = new ErDiagram();
        diagram.Entities.AddRange(entities);
        diagram.Relationships.AddRange(relationships);

        return (
            diagram,
            entities.Single(e => e.TableName == "parent_t"),
            entities.Single(e => e.TableName == "child_t"),
            relationships.Single()
        );
    }
}
