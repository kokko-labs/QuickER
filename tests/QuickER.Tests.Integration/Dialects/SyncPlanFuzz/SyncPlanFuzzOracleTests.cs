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
        string Error = "statement failed",
        Shape? Before = null
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
        bool DcPresent = true,
        CompositeForeignKey Composite = CompositeForeignKey.None
    );

    /// <summary>
    /// fk_c_p を複合外部キー（p の主キー (id, code) を c の (p_id, p_code) が参照する）にするときの列ペアの書き方
    /// </summary>
    public enum CompositeForeignKey
    {
        /// <summary>単一列（p.id ← c.p_id）</summary>
        None,

        /// <summary>id&gt;p_id, code&gt;p_code の並び</summary>
        Straight,

        /// <summary>同じ対応を逆の並びで（code&gt;p_code, id&gt;p_id）</summary>
        Reordered,

        /// <summary>対応を入れ替える（id&gt;p_code, code&gt;p_id）</summary>
        Swapped,
    }

    /// <summary>複合外部キーの形の同期の前の DB</summary>
    private static readonly Shape CompositeLive = new()
    {
        PrimaryKey = ["id", "code"],
        UniqueColumns = ["id"],
        Composite = CompositeForeignKey.Straight,
    };

    private static readonly Shape Live = new();

    private static readonly SyncPlanWarning OwnWarning = new(
        SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey,
        "c",
        "fk_c_p"
    );

    /// <summary>主キーが一意制約 uq_p（code）を置き換えると計画が名指しした警告（製品の文の形＝「名前 (列, …)」）</summary>
    private static readonly SyncPlanWarning ReplacedByPrimaryKey = new(
        SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey,
        "p",
        "uq_p (code)"
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
            // 規則 2（主キーが置き換える一意制約）：計画の警告が名指しした一意制約は、同じ列集合の主キーが残っていれば消えてよい
            new Case(
                "rule2-unique-replaced-by-primary-key-in-new-form",
                Live with
                {
                    PrimaryKey = ["code"],
                },
                [SchemaDiffKind.AlterPrimaryKey],
                Live with
                {
                    PrimaryKey = ["code"],
                    UniquePresent = false,
                },
                [],
                [],
                ReplacedByPrimaryKey
            ),
            // 主キーを付ける前（旧の主キーのまま）に失敗して、一意制約だけが消えた
            new Case(
                "rule2-unique-replaced-by-primary-key-lost-before-primary-key",
                Live with
                {
                    PrimaryKey = ["code"],
                },
                [SchemaDiffKind.AlterPrimaryKey],
                Live with
                {
                    UniquePresent = false,
                },
                [2],
                [],
                ReplacedByPrimaryKey
            ),
            // 列集合の違う主キーは、置き換えた形とみなさない
            new Case(
                "rule2-unique-replaced-by-primary-key-on-other-columns",
                Live with
                {
                    PrimaryKey = ["id", "code"],
                },
                [SchemaDiffKind.AlterPrimaryKey],
                Live with
                {
                    PrimaryKey = ["id", "code"],
                    UniquePresent = false,
                },
                [2],
                [],
                ReplacedByPrimaryKey
            ),
            // 警告が名指ししていない一意制約は、同じ列集合の主キーが残っていても消えてはいけない（規則 5）
            new Case(
                "rule5-unique-without-replacement-warning-lost",
                Live with
                {
                    PrimaryKey = ["code"],
                },
                [SchemaDiffKind.AlterPrimaryKey],
                Live with
                {
                    PrimaryKey = ["code"],
                    UniquePresent = false,
                },
                [5],
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
            // 列ペアの対応の集合で照合する：同じ対応を並びだけ変えて付け直した複合外部キーは消えていない（参照先のキーの並びを要する
            // 方言の付け直しはキーの並びになる＝製品の仕様）。対応を入れ替えた外部キーは別の外部キーで、元の外部キーは消えた（規則 5）
            new Case(
                "rule5-composite-foreign-key-reordered-pairs-remain",
                CompositeLive with
                {
                    UniqueColumns = ["code"],
                },
                [SchemaDiffKind.AlterUniqueConstraint],
                CompositeLive with
                {
                    UniqueColumns = ["code"],
                    Composite = CompositeForeignKey.Reordered,
                },
                [],
                [],
                Before: CompositeLive
            ),
            new Case(
                "rule5-composite-foreign-key-swapped-pairs-lost",
                CompositeLive with
                {
                    UniqueColumns = ["code"],
                },
                [SchemaDiffKind.AlterUniqueConstraint],
                CompositeLive with
                {
                    UniqueColumns = ["code"],
                    Composite = CompositeForeignKey.Swapped,
                },
                [5],
                [],
                Before: CompositeLive
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
            ),
            // 例外：列が空でないと通らない変更の前後で外して付け直すと名指しした制約の消失は受け入れる（注として数える）。名指しして
            // いない制約の消失は今どおり違反
            new Case(
                "empty-column-warned-foreign-key-is-a-note",
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
                [],
                [SyncPlanFuzzRunner.WarnedEmptyColumnChangeNote],
                EmptyColumnWarning("c", "fk_c_p")
            ),
            new Case(
                "empty-column-warning-on-other-foreign-key-is-a-violation",
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
                [],
                EmptyColumnWarning("c", "fk_other")
            ),
            new Case(
                "empty-column-warned-primary-key-is-a-note",
                Live with
                {
                    PrimaryKey = ["code"],
                },
                [SchemaDiffKind.AlterPrimaryKey],
                Live with
                {
                    PrimaryKey = [],
                },
                [],
                [SyncPlanFuzzRunner.WarnedEmptyColumnChangeNote],
                EmptyColumnWarning("p", "PRIMARY KEY")
            ),
            new Case(
                "empty-column-warning-on-other-table-primary-key-is-a-violation",
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
                [],
                EmptyColumnWarning("c", "PRIMARY KEY")
            )
        );

    /// <summary>列が空でないと通らない変更の前後で外して付け直す制約の警告（製品の文の形＝「制約名 (表.列: 旧 → 新)」）</summary>
    private static SyncPlanWarning EmptyColumnWarning(string table, string constraint) =>
        new(
            SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange,
            table,
            $"{constraint} (p.id: NUMBER(19) → NUMBER(10))"
        );

    /// <summary>既存の行で断られる文の見分けの 1 行（断られた文・エラー文・宣言・期待）</summary>
    public sealed record DataFailureCase(
        string Name,
        string FailedStatement,
        string Error,
        string[] Declared,
        bool Expected,
        bool Committed = false
    )
    {
        public override string ToString() => Name;
    }

    /// <summary>宣言（「種類|表|名前」。<see cref="FuzzDataFailure"/> は内部の型なので、行は文字列で書く）</summary>
    private const string OrphanAdd = "AddForeignKey|e|fk_e_p";

    private const string PrecisionDecrease = "AlterColumn|p|id";

    public static TheoryData<DataFailureCase> DataFailureCases() =>
        new(
            // 文ごとに流す方言（MySQL・Oracle）：失敗したバッチの文で見る
            new DataFailureCase(
                "declared-foreign-key-add-statement-matches",
                "ALTER TABLE `e` ADD CONSTRAINT `fk_e_p` FOREIGN KEY (`x`) REFERENCES `p` (`id`);",
                "Cannot add or update a child row",
                [OrphanAdd],
                true
            ),
            new DataFailureCase(
                "other-foreign-key-re-add-does-not-match",
                "ALTER TABLE `c` ADD CONSTRAINT `fk_c_p` FOREIGN KEY (`p_id`) REFERENCES `p` (`id`);",
                "Missing unique key for constraint 'fk_c_p'",
                [OrphanAdd],
                false
            ),
            new DataFailureCase(
                "longer-name-containing-declared-name-does-not-match",
                "ALTER TABLE `e` ADD CONSTRAINT `fk_e_p2` FOREIGN KEY (`x`) REFERENCES `p` (`id`);",
                "Cannot add or update a child row",
                [OrphanAdd],
                false
            ),
            new DataFailureCase(
                "primary-key-drop-does-not-match-declared-foreign-key",
                "ALTER TABLE `p` DROP PRIMARY KEY;",
                "Cannot drop index 'PRIMARY': needed in a foreign key constraint 'fk_e_p'",
                [OrphanAdd],
                false
            ),
            // スクリプトを 1 回で流す方言（PostgreSQL・SQL Server）：エラー文で見る
            new DataFailureCase(
                "declared-foreign-key-name-in-error-matches",
                "",
                "23503: insert or update on table \"e\" violates foreign key constraint \"fk_e_p\"",
                [OrphanAdd],
                true
            ),
            new DataFailureCase(
                "other-name-in-error-does-not-match",
                "",
                "2BP01: cannot drop constraint pk_p on table p because other objects depend on it",
                [OrphanAdd],
                false
            ),
            // SQLite：再構築の後の外部キー違反の表がすべて宣言した追加の子の表
            new DataFailureCase(
                "sqlite-violation-only-in-declared-child-matches",
                "",
                "Foreign key constraint violations were detected after the table rebuild, so the transaction was rolled back. Affected table(s): e",
                [OrphanAdd],
                true
            ),
            new DataFailureCase(
                "sqlite-violation-also-in-other-table-does-not-match",
                "",
                "Foreign key constraint violations were detected after the table rebuild, so the transaction was rolled back. Affected table(s): e, c",
                [OrphanAdd],
                false
            ),
            // 列の変更は文の中でだけ見分ける（Oracle の ORA-01440 は列を名指ししない）
            new DataFailureCase(
                "declared-column-change-statement-matches",
                "ALTER TABLE \"P\" MODIFY (\"ID\" NUMBER(10))",
                "ORA-01440: column to be modified must be empty to decrease precision or scale",
                [PrecisionDecrease],
                true
            ),
            new DataFailureCase(
                "other-column-change-does-not-match",
                "ALTER TABLE \"P\" MODIFY (\"CODE\" NVARCHAR2(10))",
                "ORA-01439: column to be modified must be empty to change datatype",
                [PrecisionDecrease],
                false
            ),
            new DataFailureCase(
                "foreign-key-statement-naming-the-column-does-not-match",
                "ALTER TABLE \"P\" ADD CONSTRAINT \"FK_P_Q\" FOREIGN KEY (\"ID\") REFERENCES \"Q\" (\"ID\")",
                "ORA-02298: cannot validate (APP.FK_P_Q) - parent keys not found",
                [PrecisionDecrease],
                false
            ),
            new DataFailureCase(
                "column-change-without-statement-does-not-match",
                "",
                "ORA-01440: column to be modified must be empty to decrease precision or scale",
                [PrecisionDecrease],
                false
            ),
            new DataFailureCase(
                "nothing-declared-does-not-match",
                "ALTER TABLE `e` ADD CONSTRAINT `fk_e_p` FOREIGN KEY (`x`) REFERENCES `p` (`id`);",
                "Cannot add or update a child row",
                [],
                false
            ),
            new DataFailureCase(
                "committed-does-not-match",
                "ALTER TABLE `e` ADD CONSTRAINT `fk_e_p` FOREIGN KEY (`x`) REFERENCES `p` (`id`);",
                "",
                [OrphanAdd],
                false,
                Committed: true
            )
        );

    [Theory(
        DisplayName = "[Integration] 同期計画の総当たりの表明 1: 宣言した既存の行で断られる文そのものの失敗だけを見分ける"
    )]
    [MemberData(nameof(DataFailureCases))]
    public void IsDeclaredDataFailure_MatchesOnlyTheDeclaredStatement(DataFailureCase c)
    {
        var result = new SchemaSyncResult
        {
            Committed = c.Committed,
            Error = c.Committed ? null : c.Error,
        };
        result.Batches.Add(
            new SchemaSyncBatchResult(0, "ALTER TABLE `x` ADD `y` int;", true, null)
        );

        if (!c.Committed)
        {
            result.Batches.Add(new SchemaSyncBatchResult(1, c.FailedStatement, false, c.Error));
        }

        var declared = c
            .Declared.Select(d => d.Split('|'))
            .Select(p => new FuzzDataFailure(Enum.Parse<FuzzDataFailureKind>(p[0]), p[1], p[2]))
            .ToList();
        SyncPlanFuzzRunner.IsDeclaredDataFailure(result, declared).Should().Be(c.Expected, c.Name);
    }

    [Theory(
        DisplayName = "[Integration] 同期計画の総当たりの表明 5: 取込が捨てた物の警告は、その表か名指しした列の削除を選んだときだけ消えてよい"
    )]
    [InlineData("collided-column-lost-by-unrelated-change", "AlterColumn", "note", true)]
    [InlineData("collided-column-lost-with-its-own-drop", "DropColumn", "name ", false)]
    [InlineData("collided-column-lost-with-table-drop", "DropTable", null, false)]
    [InlineData(
        "attribute-lost-by-change-on-the-table",
        "AlterColumn",
        "note",
        false,
        "ColumnDefaultLost"
    )]
    public void LostImportWarnings_AreExplainedOnlyBySelectedRemovals(
        string name,
        string kind,
        string? column,
        bool violation,
        string warningKind = "ColumnNameCollision"
    )
    {
        var warning = new SchemaImportWarning(
            Enum.Parse<SchemaImportWarningKind>(warningKind),
            "t",
            warningKind == "ColumnNameCollision" ? "name " : "note"
        );
        var before = new SchemaImportResult { Warnings = [warning] };
        var after = new SchemaImportResult();
        var selected = new List<SchemaDiffItem>
        {
            new()
            {
                Kind = Enum.Parse<SchemaDiffKind>(kind),
                TableName = "t",
                ColumnName = column,
            },
        };

        SyncPlanFuzzRunner
            .LostImportWarnings(before, after, selected, [])
            .Should()
            .HaveCount(violation ? 1 : 0, name);
    }

    [Theory(
        DisplayName = "[Integration] 同期計画の総当たりの表明 1b: 規則ごとに、許される形は違反にならず、違反の形は規則の番号で名指しされる"
    )]
    [MemberData(nameof(Cases))]
    public void FailedSyncConstraintLosses_FollowsRules(Case c)
    {
        var capabilities = new MySqlProvider().SyncCapabilities;
        var before = Build(c.Before ?? Live);
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
        var c = Table(
            "c",
            shape.Composite == CompositeForeignKey.None ? ["id", "p_id"] : ["id", "p_id", "p_code"],
            ["id"]
        );
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
            var reference = Reference("fk_c_p", p, "id", c, "p_id", shape.CpOnDelete);

            if (shape.Composite != CompositeForeignKey.None)
            {
                RelationshipColumnPair Pair(string parent, string child) =>
                    new(
                        p.Columns.Single(col => col.Name == parent).Id,
                        c.Columns.Single(col => col.Name == child).Id
                    );

                reference.ColumnPairs = shape.Composite switch
                {
                    CompositeForeignKey.Straight => [Pair("id", "p_id"), Pair("code", "p_code")],
                    CompositeForeignKey.Reordered => [Pair("code", "p_code"), Pair("id", "p_id")],
                    _ => [Pair("id", "p_code"), Pair("code", "p_id")],
                };
            }

            relationships.Add(reference);
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
                    DataType = column is "code" or "p_code" ? "varchar(20)" : "int",
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
