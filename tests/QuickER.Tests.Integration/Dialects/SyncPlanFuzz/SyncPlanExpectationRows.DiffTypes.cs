using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>差分の段の期待の行（列の型の比較＝方言の取込表記への正規化）</summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>綴りだけ違う同じ型（DB 側, 図）と行の形</summary>
    private static (string Shape, string Live, string Target)[] SameTypeSpellings =>
        [
            ("numeric18-vs-numeric18-0", "numeric(18)", "numeric(18,0)"),
            ("datetime2-vs-datetime2-7", "datetime2", "datetime2(7)"),
            ("decimal18-vs-decimal", "decimal(18)", "decimal"),
            ("timestamp-vs-rowversion", "timestamp", "rowversion"),
        ];

    /// <summary>正規化しても違う型（DB 側, 図）と行の形</summary>
    private static (string Shape, string Live, string Target)[] DifferentTypes =>
        [
            ("datetime2-vs-datetime2-3", "datetime2", "datetime2(3)"),
            ("numeric18-vs-numeric18-2", "numeric(18)", "numeric(18,2)"),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> DiffTypeRows() =>
        [
            .. SameTypeSpellings.Select(t =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"diff-type-normalized-per-dialect-{t.Shape}",
                            "型の比較は方言の取込表記へ寄せてから行う",
                            DialectNames.SqlServer,
                            C.Neutral
                        )
                        .Live(s => ExpectationScenarios.IdValueTable(s, "t", t.Live))
                        .Target(s => ExpectationScenarios.IdValueTable(s, "t", t.Target))
                        .Expect(e =>
                            e.For(DialectNames.SqlServer, d => d.Diff.Absent("AlterColumn *"))
                                .For(C.Neutral, d => d.Diff.Single("AlterColumn *"))
                        )
            ),
            .. DifferentTypes.Select(t =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"diff-type-different-after-normalization-is-alter-column-{t.Shape}",
                            "正規化しても違う型は AlterColumn になる",
                            DialectNames.SqlServer
                        )
                        .Live(s => ExpectationScenarios.IdValueTable(s, "t", t.Live))
                        .Target(s => ExpectationScenarios.IdValueTable(s, "t", t.Target))
                        .Expect(e => e.Diff.Single("AlterColumn *"))
            ),
        ];
}
