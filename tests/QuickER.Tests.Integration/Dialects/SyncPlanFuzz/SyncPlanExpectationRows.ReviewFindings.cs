using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;
using X = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.RealCrossDialectScenarios;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 低の指摘の受け入れの行（3 回目の効果測定の PB1・PC1・PC6・PD1・PE4。分岐の軸の台帳に載せない低の指摘を、指摘の形で固定する）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>低の指摘の札の記録の名前</summary>
    private const string Measure3Record = "review-measure3";

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> ReviewFindingRows() =>
        [
            .. PrimaryKeyReplacedUniqueNameRows(),
            ForeignKeyPairSetDelimiterRow(),
            UniqueDropBreakingSameNamedForeignKeysRow(),
            CompensatingViewChangeAcrossTablesRow(),
            DroppedParentOfTwoProtectedChildrenRow(),
            DroppedParentColumnOfTwoProtectedChildrenRow(),
        ];

    /// <summary>
    /// PB1：主キーを一意制約の列へ移し、その一意制約と同じ名前の一意制約を別の表へ足す。主キーと同じ列集合の一意制約を持てない
    /// 方言（Oracle）は、主キーの変更がその一意制約を暗黙に外すので、残る制約に数えず、追加を外さない
    /// </summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > PrimaryKeyReplacedUniqueNameRows()
    {
        static void Live(ExpectationSchema s, string dialect)
        {
            var type = X.IntType(dialect);
            s.Table("pb1_t", S.Pk("id", type), S.NotNull("code", type));
            s.Unique("pb1_t", "uq_pb1", "code");
            s.Table("pb1_s", S.Pk("id", type), S.NotNull("v", type));
        }

        static void Target(ExpectationSchema s, string dialect)
        {
            var type = X.IntType(dialect);
            s.Table("pb1_t", S.NotNull("id", type), S.Pk("code", type));
            s.Table("pb1_s", S.Pk("id", type), S.NotNull("v", type));
            s.Unique("pb1_s", "uq_pb1", "v");
        }

        var tag = RowTag.Reason(
            RowTagKind.ReviewFinding,
            $"PB1（{Measure3Record} v4）：主キーが暗黙に外す一意制約を残る制約に数えない。軸（方言の宣言 SupportsUniqueConstraintOnPrimaryKey・名前の範囲）は行が全 5 方言の能力で受け、水準の表は名前の重なりの規則を持たない"
        );

        yield return Row(
                "plan-name-collision-unique-named-like-unique-replaced-by-pk-pk-change-and-add",
                "同期: 主キーを一意制約の列へ移し、同じ名前の一意制約を別の表へ足す（主キーの変更と追加だけを選ぶ）。主キーと同じ列集合の一意制約を持てない方言は、主キーが置き換える一意制約を残る制約に数えず、追加を外さない",
                AllDialects
            )
            .LiveWith(w => Live(w.Live, w.Capability))
            .TargetWith(w => Target(w.Target, w.Capability))
            .Select(items =>
                RealScenarios.SelectKinds(
                    items,
                    SchemaDiffKind.AlterPrimaryKey,
                    SchemaDiffKind.AddUniqueConstraint
                )
            )
            .Expect(e =>
                e.For(
                        DialectNames.Oracle,
                        d =>
                            d.Excluded.Empty()
                                .Warnings.Absent("SelectedItemRequiresUnselectedItem*")
                                .Warnings.Single("UniqueConstraintReplacedByPrimaryKey(pb1_t): *")
                    )
                    .For(
                        DialectNames.SqlServer,
                        d =>
                            d.Excluded.Exact("AddUniqueConstraint *")
                                .Warnings.Single(
                                    "SelectedItemRequiresUnselectedItem(pb1_s): uq_pb1 (pb1_t.uq_pb1)"
                                )
                    )
                    .For(
                        DialectNames.PostgreSql,
                        d =>
                            d.Excluded.Exact("AddUniqueConstraint *")
                                .Warnings.Single(
                                    "SelectedItemRequiresUnselectedItem(pb1_s): uq_pb1 (pb1_t.uq_pb1)"
                                )
                    )
                    .For(DialectNames.MySql, d => d.Excluded.Empty())
                    .For(DialectNames.Sqlite, d => d.Excluded.Empty())
            )
            .Tagged(tag);

        yield return Row(
                "plan-name-collision-unique-named-like-unique-replaced-by-pk-all-selected",
                "同期: 同じ形で選べる項目を全部選ぶと、どの方言も追加を外さない",
                AllDialects
            )
            .LiveWith(w => Live(w.Live, w.Capability))
            .TargetWith(w => Target(w.Target, w.Capability))
            .Select(RealScenarios.SelectAll)
            .Expect(e => e.Excluded.Empty().Warnings.Absent("SelectedItemRequiresUnselectedItem*"))
            .Tagged(tag);
    }

    /// <summary>
    /// PD1：列名に区切り文字（<c>&gt;</c>）を含む外部キーの列の組を変える作り直しは、外す外部キーと列の組の集合が違うので「定義を変えて
    /// 付け直す」に数え、主キーの並べ替えで外して付け直す別の外部キー（そのまま付け直す）の後ろへ回す（暗黙コミットの方言）。区切り
    /// 文字だけでつなぐと、(b&gt;c → a) と (c → a&gt;b) が同じ集合に見えて「そのまま付け直す」になり、作り直しが前に残る
    /// </summary>
    private static ScenarioRow<SyncPlanExpectationInput> ForeignKeyPairSetDelimiterRow() =>
        Row(
                "plan-arrange-fk-pair-set-with-delimiter-in-column-names-is-a-changed-definition",
                "同期: 区切り文字を含む列名の外部キーの列の組を変える作り直しは、主キーの並べ替えでそのまま付け直す別の外部キーより後ろに並ぶ（暗黙コミットの方言）",
                AllDialects
            )
            .LiveWith(w =>
            {
                var type = X.IntType(w.Capability);
                w.Live.Table(
                    "pd1_p",
                    S.Pk("id", type),
                    S.NotNull("a", type),
                    S.NotNull("a>b", type)
                );
                w.Live.Unique("pd1_p", "uq_pd1_p_a", "a");
                w.Live.Unique("pd1_p", "uq_pd1_p_ab", "a>b");
                w.Live.Table(
                    "pd1_c",
                    S.Pk("id", type),
                    S.Nullable("b>c", type),
                    S.Nullable("c", type)
                );
                w.Live.Fk("pd1_p", "pd1_c", "fk_pd1_c", ("a", "b>c"));
                w.Live.Table("pd1_q", S.Pk("k1", type), S.Pk("k2", type));
                w.Live.Table(
                    "pd1_r",
                    S.Pk("id", type),
                    S.Nullable("r1", type),
                    S.Nullable("r2", type)
                );
                w.Live.Fk("pd1_q", "pd1_r", "fk_pd1_r", ("k1", "r1"), ("k2", "r2"));
            })
            .TargetWith(w =>
            {
                var type = X.IntType(w.Capability);
                w.Target.Table(
                    "pd1_p",
                    S.Pk("id", type),
                    S.NotNull("a", type),
                    S.NotNull("a>b", type)
                );
                w.Target.Unique("pd1_p", "uq_pd1_p_a", "a");
                w.Target.Unique("pd1_p", "uq_pd1_p_ab", "a>b");
                w.Target.Table(
                    "pd1_c",
                    S.Pk("id", type),
                    S.Nullable("b>c", type),
                    S.Nullable("c", type)
                );
                w.Target.Fk("pd1_p", "pd1_c", "fk_pd1_c", ("a>b", "c"));
                w.Target.Table("pd1_q", S.Pk("k1", type), S.Pk("k2", type));
                w.Target.PrimaryKeyOrder("pd1_q", "k2", "k1");
                w.Target.Table(
                    "pd1_r",
                    S.Pk("id", type),
                    S.Nullable("r1", type),
                    S.Nullable("r2", type)
                );
                w.Target.Fk("pd1_q", "pd1_r", "fk_pd1_r", ("k1", "r1"), ("k2", "r2"));
            })
            .Select(RealScenarios.SelectAll)
            .Expect(e =>
                e.For(
                        DialectNames.MySql,
                        d => d.Sections.Exact(PairSetDelimiterNonTransactionalSections)
                    )
                    .For(
                        DialectNames.Oracle,
                        d => d.Sections.Exact(PairSetDelimiterNonTransactionalSections)
                    )
            )
            .Tagged(
                RowTag.Reason(
                    RowTagKind.ReviewFinding,
                    $"PD1（{Measure3Record} v5）：外部キーの列の組の集合のキーのつなぎ方。軸（方言の宣言 TransactionalDdl）は行が全 5 方言の能力で受け、区切り文字を含む名前は名前の総当たりの領域で水準の表に無い"
                )
            );

    /// <summary>PD1 の行の暗黙コミットの方言のセクション（外部キーの付け直しが先・列の組を変えた作り直しが後ろ）</summary>
    private static string[] PairSetDelimiterNonTransactionalSections =>
        [
            "DropForeignKey: *",
            "AlterPrimaryKey/Drop: AlterPrimaryKey pd1_q",
            "AlterPrimaryKey/Add: AlterPrimaryKey pd1_q",
            "AddForeignKey: AddForeignKey pd1_r.r1 fk=fk_pd1_r; AddForeignKey pd1_c.c fk=fk_pd1_c",
        ];

    /// <summary>
    /// PE4：一意制約の削除が、別の子の表の同じ名前の外部キー 2 本を壊しうるとき、警告を子の表ごとに 1 件ずつ出す（外部キーの名前が表ごとの
    /// 方言）。Detail が名前だけだと、同じ値の警告として 1 件に畳まれる
    /// </summary>
    private static ScenarioRow<SyncPlanExpectationInput> UniqueDropBreakingSameNamedForeignKeysRow() =>
        Row(
                "plan-warning-unique-drop-may-break-fk-same-name-on-two-child-tables-named-per-child",
                "同期: 一意制約の削除が、別の子の表の同じ名前の外部キー 2 本を壊しうるとき、子の表ごとに名指しする",
                [DialectNames.PostgreSql, DialectNames.Sqlite]
            )
            .LiveWith(w =>
            {
                var type = X.IntType(w.Capability);
                w.Live.Table("pe4_p", S.Pk("id", type), S.NotNull("u", type));
                w.Live.Unique("pe4_p", "uq_pe4_p_u", "u");
                w.Live.Table("pe4_c1", S.Pk("id", type), S.Nullable("pu", type));
                w.Live.Fk("pe4_p", "pe4_c1", "fk_pe4", ("u", "pu"));
                w.Live.Table("pe4_c2", S.Pk("id", type), S.Nullable("pu", type));
                w.Live.Fk("pe4_p", "pe4_c2", "fk_pe4", ("u", "pu"));
            })
            .TargetWith(w =>
            {
                var type = X.IntType(w.Capability);
                w.Target.Table("pe4_p", S.Pk("id", type), S.NotNull("u", type));
                w.Target.Table("pe4_c1", S.Pk("id", type), S.Nullable("pu", type));
                w.Target.Fk("pe4_p", "pe4_c1", "fk_pe4", ("u", "pu"));
                w.Target.Table("pe4_c2", S.Pk("id", type), S.Nullable("pu", type));
                w.Target.Fk("pe4_p", "pe4_c2", "fk_pe4", ("u", "pu"));
            })
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropUniqueConstraint))
            .Expect(e =>
                e.Warnings.Single(
                    "UniqueConstraintDropMayBreakForeignKey(pe4_p): pe4_c1.fk_pe4",
                    "UniqueConstraintDropMayBreakForeignKey(pe4_p): pe4_c2.fk_pe4"
                )
            )
            .Tagged(
                RowTag
                    .Reason(
                        RowTagKind.ReviewFinding,
                        $"PE4（{Measure3Record} v5）：警告の Detail の形。軸（名前の範囲＝方言の宣言 ConstraintNameScope）は行の方言の組で受け、水準の表は同じ名前の外部キーを作らない"
                    )
                    .Except(
                        DialectNames.SqlServer,
                        "外部キーの名前がスキーマで一意で、別の表に同じ名前の外部キーを持てない（ConstraintNameScope）"
                    )
                    .Except(
                        DialectNames.MySql,
                        "外部キーの名前がスキーマで一意で、別の表に同じ名前の外部キーを持てない（ConstraintNameScope）"
                    )
                    .Except(
                        DialectNames.Oracle,
                        "制約の名前がスキーマで一意で、別の表に同じ名前の外部キーを持てない（ConstraintNameScope）"
                    )
            );

    /// <summary>
    /// PC1：列を pc1_a から pc1_b へ移す（計画全体では pc1_v1 は 5 列のまま読める）。無関係な pc1_c.w の削除が pc1_v2 を壊す。計画全体で
    /// 壊れる物だけを原因に数え、pc1_a・pc1_b の変更は外さずに流す
    /// </summary>
    private static ScenarioRow<SyncPlanExpectationInput> CompensatingViewChangeAcrossTablesRow() =>
        Row(
                "realdb-view-compensating-change-across-tables-is-not-named-for-unrelated-break",
                "[Integration] SQLite 同期: 表をまたいで打ち消し合う変更（計画全体では壊れないビュー）は、無関係な壊れるビューがあっても外さない",
                DialectNames.Sqlite
            )
            .TargetFromLive(s =>
            {
                s["pc1_a"].Columns.RemoveAll(c => c.Name == "y");
                s["pc1_b"].Columns.Add(S.Nullable("q", "TEXT"));
                s["pc1_c"].Columns.RemoveAll(c => c.Name == "w");
            })
            .Select(RealScenarios.SelectAll)
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE \"pc1_a\" (\"id\" INTEGER NOT NULL, \"x\" TEXT, \"y\" TEXT, CONSTRAINT \"PK_pc1_a\" PRIMARY KEY (\"id\"));"
                            + "CREATE TABLE \"pc1_b\" (\"id\" INTEGER NOT NULL, \"z\" TEXT, CONSTRAINT \"PK_pc1_b\" PRIMARY KEY (\"id\"));"
                            + "CREATE TABLE \"pc1_c\" (\"id\" INTEGER NOT NULL, \"w\" TEXT, \"v\" TEXT, CONSTRAINT \"PK_pc1_c\" PRIMARY KEY (\"id\"));"
                            + "CREATE VIEW \"pc1_v1\" (\"c1\", \"c2\", \"c3\", \"c4\", \"c5\") AS SELECT * FROM \"pc1_a\", \"pc1_b\";"
                            + "CREATE VIEW \"pc1_v2\" AS SELECT \"w\" FROM \"pc1_c\";"
                    )
                    .Query("view", "SELECT COUNT(*) FROM \"pc1_v1\";")
                    .Expect(e =>
                    {
                        e.Warnings.Absent(
                            "TableChangeBreaksDependentView(pc1_a)*",
                            "TableChangeBreaksDependentView(pc1_b)*"
                        );
                        e.Warnings.Single("TableChangeBreaksDependentView(pc1_c): pc1_v2");
                        e.Outcome.Exact("committed");
                        e.After.Absent("col pc1_a.y *");
                        e.After.Contains("col pc1_b.q *", "col pc1_c.w *");
                        e.Probe("view").Exact("0");
                    })
            )
            .Tagged(SqliteOnlyReviewFinding("PC1", "表ごとの判定を計画全体の結果と突き合わせる"));

    /// <summary>
    /// PC6：取込が捨てた物のある表 2 つが参照する親の表の削除は、守る表を両方とも名指しして外す（守る表ごとに 1 件）
    /// </summary>
    private static ScenarioRow<SyncPlanExpectationInput> DroppedParentOfTwoProtectedChildrenRow() =>
        Row(
                "realdb-rebuild-skipped-when-import-discarded-drop-parent-of-two-children-names-both",
                "[Integration] SQLite 同期: 取込が捨てた物のある表 2 つが参照する親の表の削除は、守る表を両方とも名指しして外し、何も変えない",
                DialectNames.Sqlite
            )
            .TargetFromLive(s =>
            {
                s.Relationships.RemoveAll(rel =>
                    rel.ConstraintName is "fk_pc6_c1_p" or "fk_pc6_c2_p"
                );
                s.Entities.Remove(s["pc6_p"]);
            })
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropTable))
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE \"pc6_p\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_pc6_p\" PRIMARY KEY (\"id\"));"
                            + "CREATE TABLE \"pc6_c1\" (\"id\" INTEGER NOT NULL, \"pid\" INTEGER NULL, \"zid\" INTEGER NULL, CONSTRAINT \"PK_pc6_c1\" PRIMARY KEY (\"id\"), "
                            + "CONSTRAINT \"fk_pc6_c1_p\" FOREIGN KEY (\"pid\") REFERENCES \"pc6_p\" (\"id\"), "
                            + "CONSTRAINT \"fk_pc6_c1_zz\" FOREIGN KEY (\"zid\") REFERENCES \"zz\" (\"id\"));"
                            + "CREATE TABLE \"pc6_c2\" (\"id\" INTEGER NOT NULL, \"pid\" INTEGER NULL, \"zid\" INTEGER NULL, CONSTRAINT \"PK_pc6_c2\" PRIMARY KEY (\"id\"), "
                            + "CONSTRAINT \"fk_pc6_c2_p\" FOREIGN KEY (\"pid\") REFERENCES \"pc6_p\" (\"id\"), "
                            + "CONSTRAINT \"fk_pc6_c2_zz\" FOREIGN KEY (\"zid\") REFERENCES \"zz\" (\"id\"));"
                    )
                    .Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.Warnings.Single(
                            "TableRebuildLosesImportDiscardedContent(pc6_c1): *",
                            "TableRebuildLosesImportDiscardedContent(pc6_c2): *"
                        );
                        e.Unchanged();
                    })
            )
            .Tagged(
                SqliteOnlyReviewFinding(
                    "PC6",
                    "端を消す守る表を全部名指しする（守る表ごとに 1 件）"
                )
            );

    /// <summary>
    /// PC6：取込が捨てた物のある表 2 つが参照する親の列の削除も、守る表を両方とも名指しして外す（列の削除の腕）
    /// </summary>
    private static ScenarioRow<SyncPlanExpectationInput> DroppedParentColumnOfTwoProtectedChildrenRow() =>
        Row(
                "realdb-rebuild-skipped-when-import-discarded-drop-parent-column-of-two-children-names-both",
                "[Integration] SQLite 同期: 取込が捨てた物のある表 2 つが参照する親の列の削除は、守る表を両方とも名指しして外し、何も変えない",
                DialectNames.Sqlite
            )
            .TargetFromLive(s =>
            {
                s.Relationships.RemoveAll(rel =>
                    rel.ConstraintName is "fk_pc6k_c1_p" or "fk_pc6k_c2_p"
                );
                s["pc6k_p"].UniqueConstraints.Clear();
                s["pc6k_p"].Columns.RemoveAll(c => c.Name == "k");
            })
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropColumn))
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE \"pc6k_p\" (\"id\" INTEGER NOT NULL, \"k\" INTEGER NOT NULL, CONSTRAINT \"PK_pc6k_p\" PRIMARY KEY (\"id\"), CONSTRAINT \"uq_pc6k_p_k\" UNIQUE (\"k\"));"
                            + "CREATE TABLE \"pc6k_c1\" (\"id\" INTEGER NOT NULL, \"pk\" INTEGER NULL, \"zid\" INTEGER NULL, CONSTRAINT \"PK_pc6k_c1\" PRIMARY KEY (\"id\"), "
                            + "CONSTRAINT \"fk_pc6k_c1_p\" FOREIGN KEY (\"pk\") REFERENCES \"pc6k_p\" (\"k\"), "
                            + "CONSTRAINT \"fk_pc6k_c1_zz\" FOREIGN KEY (\"zid\") REFERENCES \"zz\" (\"id\"));"
                            + "CREATE TABLE \"pc6k_c2\" (\"id\" INTEGER NOT NULL, \"pk\" INTEGER NULL, \"zid\" INTEGER NULL, CONSTRAINT \"PK_pc6k_c2\" PRIMARY KEY (\"id\"), "
                            + "CONSTRAINT \"fk_pc6k_c2_p\" FOREIGN KEY (\"pk\") REFERENCES \"pc6k_p\" (\"k\"), "
                            + "CONSTRAINT \"fk_pc6k_c2_zz\" FOREIGN KEY (\"zid\") REFERENCES \"zz\" (\"id\"));"
                    )
                    .Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.Warnings.Single(
                            "TableRebuildLosesImportDiscardedContent(pc6k_c1): *",
                            "TableRebuildLosesImportDiscardedContent(pc6k_c2): *"
                        );
                        e.Unchanged();
                    })
            )
            .Tagged(
                SqliteOnlyReviewFinding("PC6", "端の列を消す守る表を全部名指しする（列の削除の腕）")
            );

    /// <summary>SQLite だけの判定を直した低の指摘の札（ほかの 4 方言は判定を持たないので外す）</summary>
    private static RowTag SqliteOnlyReviewFinding(string finding, string what) =>
        RowTag
            .Reason(
                RowTagKind.ReviewFinding,
                $"{finding}（{Measure3Record} v5）：{what}。軸（方言）は SQLite だけが持つ判定で、ほかの方言の行は作れない"
            )
            .Except(
                DialectNames.SqlServer,
                "判定（ビュー・トリガー・索引の判定器と作り直さない表）はテーブル再構築の方言（SQLite）だけが持つ"
            )
            .Except(
                DialectNames.PostgreSql,
                "判定（ビュー・トリガー・索引の判定器と作り直さない表）はテーブル再構築の方言（SQLite）だけが持つ"
            )
            .Except(
                DialectNames.MySql,
                "判定（ビュー・トリガー・索引の判定器と作り直さない表）はテーブル再構築の方言（SQLite）だけが持つ"
            )
            .Except(
                DialectNames.Oracle,
                "判定（ビュー・トリガー・索引の判定器と作り直さない表）はテーブル再構築の方言（SQLite）だけが持つ"
            );
}
