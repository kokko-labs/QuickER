using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 複合外部キー（<see cref="Relationship.ColumnPairs"/> が 2 組以上）が、DDL 生成・スキーマ署名・差分計算・
/// 同期計画のどこでも構成列を失わずに扱われることを検証する。
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

    // ---------------- 差分計算 ----------------

    /// <summary>live と target の複合外部キーが同一なら外部キー差分が出ないことを検証する</summary>
    [Fact(DisplayName = "同一の複合 FK では FK 差分が出ない")]
    public void SchemaDiff_IdenticalCompositeForeignKey_ProducesNoDiff()
    {
        var (live, liveParent, liveChild, liveRel) = BuildCompositeDiagram();
        var (target, _, _, _) = CloneDiagram(live);

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            target.Entities,
            target.Relationships
        );

        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.AddForeignKey || i.Kind == SchemaDiffKind.DropForeignKey
            );

        // 使わない変数の警告を避けつつ、組み立てた図の前提（複合 2 列）を明示する
        liveRel.ColumnPairs.Should().HaveCount(2);
        liveParent.Columns.Should().HaveCount(2);
        liveChild.Columns.Should().HaveCount(3);
    }

    /// <summary>構成列が 1 組減ると Drop ＋ Add の差分になり、Add 側が全構成列を運ぶことを検証する</summary>
    [Fact(DisplayName = "複合 FK が単列へ変わると Drop と Add の両方が出て構成列を運ぶ")]
    public void SchemaDiff_CompositeToSingle_EmitsDropAndAddWithColumnPairs()
    {
        var (live, _, _, _) = BuildCompositeDiagram();
        var (target, _, _, targetRel) = CloneDiagram(live);

        // 目標は先頭ペアだけの単列外部キー
        targetRel.ColumnPairs.RemoveAt(1);

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            target.Entities,
            target.Relationships
        );

        var drop = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropForeignKey)
            .Which;
        drop.ForeignKeyColumnPairs.Select(p => (p.ParentColumn, p.ChildColumn))
            .Should()
            .Equal(("a", "a_ref"), ("b", "b_ref"));

        var add = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddForeignKey)
            .Which;
        add.ForeignKeyColumnPairs.Select(p => (p.ParentColumn, p.ChildColumn))
            .Should()
            .Equal(("a", "a_ref"));
        // 表示・照合の互換のため ColumnName には先頭ペアの子列名が入る
        add.ColumnName.Should().Be("a_ref");
    }

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
            target.Relationships
        );
        var addItem = diff.Items.Single(i => i.Kind == SchemaDiffKind.AddForeignKey);
        addItem.IsSelected = true;

        var plan = new SyncPlanner().BuildPlan([addItem], new SyncDialectCapabilities());
        var sql = new SqlServerSyncScriptBuilder().Build(plan);

        sql.Should().Contain("FOREIGN KEY ([a_ref], [b_ref]) REFERENCES [parent_t] ([a], [b])");
    }

    // ---------------- 同期計画（暗黙の作り直しと候補キーの証明） ----------------

    /// <summary>
    /// 複合外部キーの参照先テーブルの主キーを変えると、暗黙の DROP → 再 ADD が全構成列で注入されることを検証する。
    /// </summary>
    /// <remarks>
    /// 同期後の主キーが被参照列集合 (a, b) とちょうど一致する（順序だけ入れ替わる）ため、候補キーは失われず警告も出ない。
    /// </remarks>
    [Fact(DisplayName = "複合 FK: 参照先の主キー変更で全構成列の暗黙 DROP→再 ADD が注入される")]
    public void SyncPlanner_ParentPrimaryKeyChange_RebuildsCompositeForeignKey()
    {
        var (live, parent, child, relationship) = BuildCompositeDiagram();

        // 主キーの構成列順を入れ替える（列集合は (a, b) のまま＝候補キーは維持される）
        var targetParent = parent.Clone(preserveId: true);
        targetParent.Columns.Reverse();

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterPrimaryKey,
                    TableName = "parent_t",
                    Entity = targetParent,
                    IsSelected = true,
                },
            ],
            new SyncDialectCapabilities(),
            new SyncPlanContext
            {
                LiveEntities = [parent, child],
                LiveRelationships = [relationship],
            }
        );

        var drop = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which;
        drop.ForeignKeyColumnPairs.Should().HaveCount(2);

        var add = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which;
        add.ForeignKeyColumnPairs.Select(p => (p.ParentColumn, p.ChildColumn))
            .Should()
            .Equal(("a", "a_ref"), ("b", "b_ref"));

        // 被参照列集合が同期後の主キーとちょうど一致するため候補キー喪失の警告は出ない
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 主キーが被参照列集合を包含するだけ（拡張される）のときは、候補キーを証明できず警告が積まれることを検証する。
    /// </summary>
    [Fact(DisplayName = "複合 FK: 主キーが被参照列集合を超えて拡張されると候補キー喪失を警告する")]
    public void SyncPlanner_PrimaryKeyExpandedBeyondReferencedColumns_Warns()
    {
        var (live, parent, child, relationship) = BuildCompositeDiagram();

        // 主キーへ 3 列目を足す＝(a, b) は候補キーでなくなりうる
        var targetParent = parent.Clone(preserveId: true);
        targetParent.Columns.Add(
            new Column
            {
                Name = "c",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterPrimaryKey,
                    TableName = "parent_t",
                    Entity = targetParent,
                    IsSelected = true,
                },
            ],
            new SyncDialectCapabilities(),
            new SyncPlanContext
            {
                LiveEntities = [parent, child],
                LiveRelationships = [relationship],
            }
        );

        plan.Warnings.Should()
            .ContainSingle()
            .Which.Kind.Should()
            .Be(SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);
    }

    /// <summary>
    /// 被参照列集合と同じ構成の一意制約が同期後にも在れば、主キーが拡張されても警告しないことを検証する。
    /// </summary>
    /// <remarks>候補キーの証明は「同期後の主キーと完全一致」または「同期後の一意制約と完全一致」の集合版。</remarks>
    [Fact(DisplayName = "複合 FK: 被参照列集合と一致する複合 UNIQUE があれば警告しない")]
    public void SyncPlanner_CompositeUniqueConstraintCoversReferencedColumns_DoesNotWarn()
    {
        var (live, parent, child, relationship) = BuildCompositeDiagram();

        // live の親に (a, b) の複合一意制約がある＝主キーを変えても候補キーは残る
        parent.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_parent_t_a_b",
                ColumnIds = [parent.Columns[0].Id, parent.Columns[1].Id],
            }
        );

        var targetParent = parent.Clone(preserveId: true);
        targetParent.Columns.Add(
            new Column
            {
                Name = "c",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterPrimaryKey,
                    TableName = "parent_t",
                    Entity = targetParent,
                    IsSelected = true,
                },
            ],
            new SyncDialectCapabilities(),
            new SyncPlanContext
            {
                LiveEntities = [parent, child],
                LiveRelationships = [relationship],
            }
        );

        plan.Warnings.Should().BeEmpty();
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
