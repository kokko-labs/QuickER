using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たりの表明 1b（<see cref="SyncPlanFuzzRunner.FailedSyncConstraintLosses"/>）の規則を、DB を使わずに 1 規則 1 行の
/// 表で固定する（エンジンの部品のガード）。
/// </summary>
/// <remarks>
/// 規則 3（主キーの変更は旧か新の形で残る）は普段の種では出ず、規則 4（表・列の削除は実際に消えたときだけ消えてよい）は
/// 夜間の種でも出ないので、総当たりだけでは黙って退行できる。ここで同期の前の DB・目標の図・選んだ項目・計画の警告・
/// 失敗の後の DB・実行の結果を手で組み、規則ごとに「許される形」と「違反の形」を表明する。方言の能力は MySQL
/// （外部キーの名前を保つ・DDL を暗黙コミットする＝1b が意味を持つ方言）。
/// </remarks>
public sealed class SyncPlanFuzzOracleTests
{
    /// <summary>1 行の期待（違反の規則の番号の並び・注）</summary>
    public sealed record Case(
        string Name,
        Shape Target,
        SchemaDiffKind[] Select,
        Shape Failed,
        int[] Rules,
        string[] Notes,
        SyncPlanWarning? Warning = null,
        string Error = "statement failed"
    )
    {
        public override string ToString() => Name;
    }

    /// <summary>図の形（同期の前の DB は既定の形）</summary>
    public sealed record Shape(
        ForeignKeyReferentialAction CpOnDelete = ForeignKeyReferentialAction.NoAction,
        bool CpPresent = true,
        string[]? UniqueColumns = null,
        bool UniquePresent = true,
        string[]? PrimaryKey = null,
        bool DPresent = true,
        bool DcPresent = true
    );

    private static readonly Shape Live = new();

    private static readonly SyncPlanWarning OwnWarning = new(
        SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey,
        "c",
        "fk_c_p"
    );

    public static TheoryData<Case> Cases() =>
        new(
            // 規則 1：その制約の削除を選んだ制約は、消えていてよい
            new Case(
                "rule1-dropped-foreign-key-may-vanish",
                Live with
                {
                    CpPresent = false,
                },
                [SchemaDiffKind.DropForeignKey],
                Live with
                {
                    CpPresent = false,
                },
                [],
                []
            ),
            // 規則 2：作り直しを選んだ制約は、旧か新の形で残る
            new Case(
                "rule2-rebuilt-foreign-key-in-new-form",
                Live with
                {
                    CpOnDelete = ForeignKeyReferentialAction.Cascade,
                },
                [SchemaDiffKind.AlterForeignKey],
                Live with
                {
                    CpOnDelete = ForeignKeyReferentialAction.Cascade,
                },
                [],
                []
            ),
            new Case(
                "rule2-rebuilt-foreign-key-lost",
                Live with
                {
                    CpOnDelete = ForeignKeyReferentialAction.Cascade,
                },
                [SchemaDiffKind.AlterForeignKey],
                Live with
                {
                    CpPresent = false,
                },
                [2],
                []
            ),
            new Case(
                "rule2-rebuilt-unique-in-new-form",
                Live with
                {
                    UniqueColumns = ["id", "code"],
                },
                [SchemaDiffKind.AlterUniqueConstraint],
                Live with
                {
                    UniqueColumns = ["id", "code"],
                },
                [],
                []
            ),
            new Case(
                "rule2-rebuilt-unique-lost",
                Live with
                {
                    UniqueColumns = ["id", "code"],
                },
                [SchemaDiffKind.AlterUniqueConstraint],
                Live with
                {
                    UniquePresent = false,
                },
                [2],
                []
            ),
            // 規則 3：主キーの変更を選んだ表の主キーは、旧か新の形で残る（主キーを外す変更なら、無いのも新の形）
            new Case(
                "rule3-primary-key-in-new-form",
                Live with
                {
                    PrimaryKey = ["code"],
                },
                [SchemaDiffKind.AlterPrimaryKey],
                Live with
                {
                    PrimaryKey = ["code"],
                },
                [],
                []
            ),
            new Case(
                "rule3-primary-key-lost",
                Live with
                {
                    PrimaryKey = ["code"],
                },
                [SchemaDiffKind.AlterPrimaryKey],
                Live with
                {
                    PrimaryKey = [],
                },
                [3],
                []
            ),
            new Case(
                "rule3-primary-key-removed-and-absent",
                Live with
                {
                    PrimaryKey = [],
                },
                [SchemaDiffKind.AlterPrimaryKey],
                Live with
                {
                    PrimaryKey = [],
                },
                [],
                []
            ),
            // 規則 4：表・列の削除でしか消えてよい理由の無い制約は、その表・列が実際に消えたときだけ消えてよい
            new Case(
                "rule4-table-dropped-with-its-foreign-key",
                Live with
                {
                    DPresent = false,
                },
                [SchemaDiffKind.DropTable],
                Live with
                {
                    DPresent = false,
                },
                [],
                []
            ),
            new Case(
                "rule4-table-remains-but-foreign-key-lost",
                Live with
                {
                    DPresent = false,
                },
                [SchemaDiffKind.DropTable],
                Live with
                {
                    DcPresent = false,
                },
                [4],
                []
            ),
            // 規則 5：それ以外の制約は残る
            new Case(
                "rule5-unselected-foreign-key-lost",
                Live with
                {
                    UniqueColumns = ["id", "code"],
                },
                [SchemaDiffKind.AlterUniqueConstraint],
                Live with
                {
                    UniqueColumns = ["id", "code"],
                    CpPresent = false,
                },
                [5],
                []
            ),
            // 例外：候補キーを証明できないと警告した外部キー自身の消失は受け入れる（作り直しに重なれば注として数える）
            new Case(
                "warned-rebuild-own-failure-is-a-note",
                Live with
                {
                    CpOnDelete = ForeignKeyReferentialAction.Cascade,
                },
                [SchemaDiffKind.AlterForeignKey],
                Live with
                {
                    CpPresent = false,
                },
                [],
                [$"{SyncPlanFuzzRunner.WarnedAlterForeignKeyNote}:own-failure"],
                OwnWarning,
                "Missing unique key for constraint 'fk_c_p'"
            ),
            new Case(
                "warned-rebuild-collateral-is-a-note",
                Live with
                {
                    CpOnDelete = ForeignKeyReferentialAction.Cascade,
                },
                [SchemaDiffKind.AlterForeignKey],
                Live with
                {
                    CpPresent = false,
                },
                [],
                [$"{SyncPlanFuzzRunner.WarnedAlterForeignKeyNote}:collateral"],
                OwnWarning,
                "Missing unique key for constraint 'fk_other'"
            )
        );

    [Theory(
        DisplayName = "[Integration] 同期計画の総当たりの表明 1b: 規則ごとに、許される形は違反にならず、違反の形は規則の番号で名指しされる"
    )]
    [MemberData(nameof(Cases))]
    public void FailedSyncConstraintLosses_FollowsRules(Case c)
    {
        var capabilities = new MySqlProvider().SyncCapabilities;
        var before = Build(Live);
        var target = Build(c.Target);
        var diff = new SchemaDiffService().Compute(
            before.Entities,
            before.Relationships,
            target.Entities,
            target.Relationships,
            capabilities
        );
        var selected = diff.Items.Where(i => c.Select.Contains(i.Kind)).ToList();
        selected.Select(i => i.Kind).Should().BeEquivalentTo(c.Select, c.Name);

        var result = new SchemaSyncResult { Committed = false, Error = c.Error };
        result.Batches.Add(
            new SchemaSyncBatchResult(0, "ALTER TABLE `c` DROP FOREIGN KEY `fk_c_p`;", true, null)
        );
        result.Batches.Add(
            new SchemaSyncBatchResult(1, "ALTER TABLE `c` ADD CONSTRAINT …;", false, c.Error)
        );

        var (violations, notes) = SyncPlanFuzzRunner.FailedSyncConstraintLosses(
            before,
            Build(c.Failed),
            selected,
            new SyncPlan { Warnings = c.Warning is null ? [] : [c.Warning] },
            capabilities,
            result
        );

        violations
            .Select(v => int.Parse(v[(v.IndexOf("(rule ", StringComparison.Ordinal) + 6)..][..1]))
            .Should()
            .Equal(c.Rules, string.Join("\n", violations));
        violations
            .Should()
            .AllSatisfy(v => v.Should().StartWith(SyncPlanFuzzRunner.ConstraintLostPrefix));
        notes.Should().Equal(c.Notes);
    }

    /// <summary>
    /// 表 p（id・code・主キーと一意制約は形しだい）・c（id・p_id＝fk_c_p で p.id を参照）・d（id・c_id＝fk_d_c で c.id を参照）を
    /// 組む（呼ぶたびに新しい Guid の空間＝GUI と同じく live と図は別の ID）
    /// </summary>
    private static SchemaImportResult Build(Shape shape)
    {
        var primaryKey = shape.PrimaryKey ?? ["id"];
        var p = Table("p", ["id", "code"], primaryKey);
        var c = Table("c", ["id", "p_id"], ["id"]);
        var entities = new List<Entity> { p, c };
        var relationships = new List<Relationship>();

        if (shape.UniquePresent)
        {
            p.UniqueConstraints.Add(
                new UniqueConstraint
                {
                    Name = "uq_p",
                    ColumnIds =
                    [
                        .. (shape.UniqueColumns ?? ["code"]).Select(n =>
                            p.Columns.Single(col => col.Name == n).Id
                        ),
                    ],
                }
            );
        }

        if (shape.CpPresent)
        {
            relationships.Add(Reference("fk_c_p", p, "id", c, "p_id", shape.CpOnDelete));
        }

        if (shape.DPresent)
        {
            var d = Table("d", ["id", "c_id"], ["id"]);
            entities.Add(d);

            if (shape.DcPresent)
            {
                relationships.Add(
                    Reference("fk_d_c", c, "id", d, "c_id", ForeignKeyReferentialAction.NoAction)
                );
            }
        }

        return new SchemaImportResult { Entities = entities, Relationships = relationships };
    }

    private static Entity Table(string name, string[] columns, string[] primaryKey)
    {
        var entity = new Entity { TableName = name };

        foreach (var column in columns)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = column,
                    DataType = column == "code" ? "varchar(20)" : "int",
                    IsNullable = false,
                    IsPrimaryKey = primaryKey.Contains(column),
                }
            );
        }

        entity.PrimaryKeyColumnIds =
        [
            .. primaryKey.Select(n => entity.Columns.Single(col => col.Name == n).Id),
        ];

        return entity;
    }

    private static Relationship Reference(
        string name,
        Entity parent,
        string parentColumn,
        Entity child,
        string childColumn,
        ForeignKeyReferentialAction onDelete
    ) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ConstraintName = name,
            OnDelete = onDelete,
            ColumnPairs =
            [
                new RelationshipColumnPair(
                    parent.Columns.Single(col => col.Name == parentColumn).Id,
                    child.Columns.Single(col => col.Name == childColumn).Id
                ),
            ],
        };
}
