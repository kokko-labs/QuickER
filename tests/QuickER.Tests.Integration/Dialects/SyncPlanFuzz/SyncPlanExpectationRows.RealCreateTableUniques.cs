using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;
using X = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.RealCrossDialectScenarios;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 実 DB の段の期待の行（表を作る文の中で、主キーか先に書いた一意制約と同じ列の並びの一意制約を書く表を、DDL で作って取り込み直す・
/// 同期の表の追加で作る）
/// </summary>
/// <remarks>
/// どちらの経路でも、作った表は図の一意制約をすべて持ち、取り込み直した差分は空になる。主キーと同じ列集合の一意制約を持てない方言
/// （Oracle・SQLite）は DDL・同期がその一意制約を省いて注記し、差分は比較から外す。同じ列の並びの一意制約を 2 つ持てない方言
/// （Oracle の ORA-02261）と、1 つの索引にまとめる方言（SQLite）は、2 つ目の一意制約の行を流さない。
/// </remarks>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>同じ列の並びの一意制約を 2 つ持てる方言</summary>
    private static string[] SameColumnUniqueDialects =>
        [DialectNames.SqlServer, DialectNames.PostgreSql, DialectNames.MySql];

    /// <summary>表を作る文の中の、ほかの制約と同じ列の並びの一意制約の行（実 DB の段）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealCreateTableUniqueRows() =>
        [
            Row(
                    "realdb-create-table-unique-on-primary-key-columns-survives-reimport",
                    "[Integration] DDL で作った表の主キーと同じ列の一意制約は、持てる方言では取り込み直しても残り、差分は空になる",
                    AllDialects
                )
                .LiveWith(w => CreateTableUniqueOnPrimaryKey(w.Live, w.Capability))
                .TargetWith(w => CreateTableUniqueOnPrimaryKey(w.Target, w.Capability))
                .Expect(e => e.Diff.Empty())
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.AfterDiff.Empty();

                        foreach (var dialect in AllDialects)
                        {
                            e.For(
                                dialect,
                                x =>
                                {
                                    if (
                                        SyncPlanExpectationCapabilities
                                            .Get(dialect)
                                            .SupportsUniqueConstraintOnPrimaryKey
                                    )
                                    {
                                        x.Before.Single("uq ct uq_ct_id (id)");
                                        return;
                                    }

                                    x.Before.Absent("uq *");
                                }
                            );
                        }
                    })
                ),
            Row(
                    "realdb-create-table-unique-on-primary-key-columns-added-by-sync",
                    "[Integration] 同期で足した表の主キーと同じ列の一意制約は、持てる方言では作られ、取り込み直した差分は空になる",
                    AllDialects
                )
                .LiveWith(w => CreateTableBase(w.Live, w.Capability))
                .TargetWith(w =>
                {
                    CreateTableBase(w.Target, w.Capability);
                    CreateTableUniqueOnPrimaryKey(w.Target, w.Capability);
                })
                .Expect(e => e.Diff.Single("AddTable *"))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.AfterDiff.Empty();

                        foreach (var dialect in AllDialects)
                        {
                            e.For(
                                dialect,
                                x =>
                                {
                                    if (
                                        SyncPlanExpectationCapabilities
                                            .Get(dialect)
                                            .SupportsUniqueConstraintOnPrimaryKey
                                    )
                                    {
                                        x.After.Single("uq ct uq_ct_id (id)");
                                        return;
                                    }

                                    x.After.Absent("uq *");
                                }
                            );
                        }
                    })
                ),
            Row(
                    "realdb-create-table-second-unique-on-same-columns-survives-reimport",
                    "[Integration] DDL で作った表の同じ列の 2 つ目の一意制約は、取り込み直しても残り、差分は空になる",
                    SameColumnUniqueDialects
                )
                .LiveWith(w => CreateTableSameColumnUniques(w.Live, w.Capability))
                .TargetWith(w => CreateTableSameColumnUniques(w.Target, w.Capability))
                .Expect(e => e.Diff.Empty())
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.Before.Count("uq *", 2);
                        e.Before.Single("uq ct2 uq_ct2_b (code)");
                        e.AfterDiff.Empty();
                    })
                ),
            Row(
                    "realdb-create-table-second-unique-on-same-columns-added-by-sync",
                    "[Integration] 同期で足した表の同じ列の 2 つ目の一意制約は作られ、取り込み直した差分は空になる",
                    SameColumnUniqueDialects
                )
                .LiveWith(w => CreateTableBase(w.Live, w.Capability))
                .TargetWith(w =>
                {
                    CreateTableBase(w.Target, w.Capability);
                    CreateTableSameColumnUniques(w.Target, w.Capability);
                })
                .Expect(e => e.Diff.Single("AddTable *"))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Count("uq *", 2);
                        e.After.Single("uq ct2 uq_ct2_b (code)");
                        e.AfterDiff.Empty();
                    })
                ),
        ];

    /// <summary>表 ct_base(id)（表の追加の行の DB 側の図）</summary>
    private static void CreateTableBase(ExpectationSchema s, string dialect) =>
        s.Table("ct_base", S.Pk("id", X.IntType(dialect)));

    /// <summary>表 ct(id, code) と、主キーと同じ列の一意制約 uq_ct_id(id)</summary>
    private static void CreateTableUniqueOnPrimaryKey(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("ct", S.Pk("id", type), S.NotNull("code", type));
        s.Unique("ct", "uq_ct_id", "id");
    }

    /// <summary>表 ct2(id, code) と、同じ列の一意制約 uq_ct2_a(code)・uq_ct2_b(code)</summary>
    private static void CreateTableSameColumnUniques(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("ct2", S.Pk("id", type), S.NotNull("code", type));
        s.Unique("ct2", "uq_ct2_a", "code");
        s.Unique("ct2", "uq_ct2_b", "code");
    }
}
