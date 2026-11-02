using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 差分と計画の期待の行（複合外部キー＝列ペアが 2 組以上の外部キーが、差分と同期計画で構成列を失わずに扱われる：差分の作り直しは
/// 新旧の構成列を運び、参照先の主キーの変更は全構成列で外して付け直し、候補キーの証明は列の集合で行う）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> CompositeForeignKeyRows() =>
        [
            Row(
                    "diff-composite-fk-identical-produces-no-fk-diff",
                    "同一の複合 FK では FK 差分が出ない",
                    DialectNames.SqlServer
                )
                .Live(CompositeParentChild)
                .TargetWith(w => w.Target.CopyFrom(w.Live, preserveIds: true))
                .Expect(e => e.Diff.Absent("AddForeignKey *", "DropForeignKey *")),
            Row(
                    "diff-composite-fk-to-single-is-alter-carrying-both-pairs",
                    "複合 FK が単列へ変わると AlterForeignKey になり新旧の構成列を運ぶ",
                    DialectNames.SqlServer
                )
                .Live(CompositeParentChild)
                .TargetWith(w =>
                {
                    w.Target.CopyFrom(w.Live, preserveIds: true);

                    // 目標は先頭ペアだけの単列外部キー
                    w.Target.Relationships[0].ColumnPairs.RemoveAt(1);
                })
                // 表示・照合の互換のため ColumnName には先頭ペアの子列名が入る（鍵の列 a_ref）
                .Expect(e =>
                    e.Diff.Single(
                        "AlterForeignKey *",
                        "AlterForeignKey child_t.a_ref * pairs=(a>a_ref) * replaced-fk=[* pairs=(a>a_ref,b>b_ref) *] *"
                    )
                ),
            Row(
                    "plan-composite-fk-reordered-parent-pk-rebuilds-all-pairs",
                    "複合 FK: 参照先の主キー変更で全構成列の暗黙 DROP→再 ADD が注入される",
                    DialectNames.PostgreSql,
                    DialectNames.SqlServer
                )
                .Live(CompositeParentChild)
                // 主キーの構成列順を入れ替える（列集合は (a, b) のまま。並びを問わない方言では候補キーが維持される）
                .Items(w =>
                {
                    var targetParent = w.Live["parent_t"].Clone(preserveId: true);
                    targetParent.Columns.Reverse();
                    return [I.AlterPk("parent_t", targetParent)];
                })
                // 被参照列集合が同期後の主キーとちょうど一致するため候補キー喪失の警告は出ない
                .Expect(e =>
                    e.Warnings.Empty()
                        .For(
                            DialectNames.PostgreSql,
                            d =>
                                d.Sections.Count("DropForeignKey: *", 1)
                                    .Items.Count("DropForeignKey > *", 1)
                                    .Items.Single("DropForeignKey > * pairs=(a>a_ref,b>b_ref) *")
                                    .Sections.Count("AddForeignKey: *", 1)
                                    .Items.Count("AddForeignKey > *", 1)
                                    .Items.Single("AddForeignKey > * pairs=(a>a_ref,b>b_ref) *")
                        )
                        // 参照する列の並びを問う方言（SQL Server・MySQL）では、付け直しを新しい主キーの並び (b, a) で出す
                        .For(
                            DialectNames.SqlServer,
                            d =>
                                d.Sections.Count("AddForeignKey: *", 1)
                                    .Items.Count("AddForeignKey > *", 1)
                                    .Items.Single("AddForeignKey > * pairs=(b>b_ref,a>a_ref) *")
                        )
                ),
            .. new[] { false, true }.Select(coveredByUnique =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            coveredByUnique
                                ? "plan-composite-fk-pk-expanded-with-covering-unique-does-not-warn"
                                : "plan-composite-fk-pk-expanded-beyond-referenced-columns-warns",
                            coveredByUnique
                                ? "複合 FK: 被参照列集合と一致する複合 UNIQUE があれば警告しない"
                                : "複合 FK: 主キーが被参照列集合を超えて拡張されると候補キー喪失を警告する",
                            C.Neutral
                        )
                        .Live(s =>
                        {
                            CompositeParentChild(s);

                            // live の親に (a, b) の複合一意制約がある＝主キーを変えても候補キーは残る
                            if (coveredByUnique)
                            {
                                s.Unique("parent_t", "UQ_parent_t_a_b", "a", "b");
                            }

                            // 新しい主キーの列 c は live に在る（無ければ主キーの変更は「選んでいない列の追加」に依存して外れる）
                            s["parent_t"].Columns.Add(S.NotNull("c", "int"));
                        })
                        // 主キーへ 3 列目を足す＝(a, b) は候補キーでなくなりうる（c は live の列と別の Id）
                        .Items(w =>
                        {
                            var targetParent = w.Live["parent_t"].Clone(preserveId: true);
                            targetParent.Columns.RemoveAll(c => c.Name == "c");
                            targetParent.Columns.Add(S.Pk("c", "int"));
                            return [I.AlterPk("parent_t", targetParent)];
                        })
                        .Expect(e =>
                        {
                            if (coveredByUnique)
                            {
                                e.Warnings.Empty();
                            }
                            else
                            {
                                e.Warnings.Exact("ForeignKeyRebuildMayLoseCandidateKey(*");
                            }
                        })
            ),
        ];

    /// <summary>複合主キー (a, b) の親 parent_t と、複合外部キー FK_child_t_parent_t (a_ref, b_ref) を持つ子 child_t を足す</summary>
    private static void CompositeParentChild(S s)
    {
        s.Table("parent_t", S.Pk("a", "int"), S.Pk("b", "int"));
        s.Table("child_t", S.Pk("id", "int"), S.NotNull("a_ref", "int"), S.NotNull("b_ref", "int"));
        s.Fk("parent_t", "child_t", "FK_child_t_parent_t", ("a", "a_ref"), ("b", "b_ref"));
    }
}
