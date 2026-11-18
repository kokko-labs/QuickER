using System.IO;
using QuickER.Tests.Docs;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：同期の計画（選んだ項目を外す規則の宣言と規則の表・外し直しの宣言と宣言の表）</summary>
internal static partial class ParityRows
{
    private static IEnumerable<ParityRow> Provider() =>
        [
            new(
                "sync-selection-rules-table-complete",
                "同期の計画の選んだ項目を外す規則（SelectionRules の規則の形の Exclude*）は、どれも規則の表（BeforeRounds・EachRound の和）に"
                    + "ちょうど 1 回載り、表の要素はどれも規則の形の関数にする（表から抜けた規則は黙って一度も流れず、計画に載せられない項目が"
                    + "外されずに DB で断られる。2 回載せた規則は周の中で 2 回流れる）",
                ParityCompare.Multiset,
                () => SyncSelectionRuleTable.DeclaredRules(SelectionSources()),
                () => SyncSelectionRuleTable.ListedRules(SelectionSources())
            )
            {
                // いまの規則は 9 つ（周の前の 1 つと周ごとの 8 つ）。拾い方が外れて正本が減ったら赤にする
                MinimumCanon = 9,
            },
            new(
                "sync-selection-rules-declarations-probe",
                "自己検査の行：照合の行 sync-selection-rules-table-complete が規則の形の関数の宣言を集める関数そのものを、合成のソースで"
                    + "確かめる（正本は合成のソースで集めるべき宣言）。ファイルをまたぐ partial の宣言と条件コンパイルの外れる分岐の宣言を拾い、"
                    + "引数の数や型が違う関数（規則が共有する本体）・Exclude で始まらない関数・ほかのクラスの規則の形の関数は拾わない",
                ParityCompare.Sequence,
                () => ["ExcludeFirst", "ExcludeInBranch", "ExcludeSecond"],
                () => SyncSelectionRuleTable.DeclaredRules(ProbeSelectionSources)
            ),
            new(
                "sync-selection-rules-entries-probe",
                "自己検査の行：照合の行 sync-selection-rules-table-complete が規則の表の要素を集める関数そのものを、合成のソースで確かめる"
                    + "（正本は合成のソースで集めるべき要素）。2 つの表の要素を重なりごと数え、メソッドの名前でない要素は綴りのまま数え、"
                    + "表でないフィールド・ほかのクラスの同じ名前のフィールドの要素は拾わない",
                ParityCompare.Sequence,
                () => ["ExcludeFirst", "ExcludeSecond", "ExcludeSecond", "Other.ExcludeElsewhere"],
                () => SyncSelectionRuleTable.ListedRules(ProbeSelectionSources)
            ),
            new(
                "sync-rebuild-declarations-table-complete",
                "同期の計画の新しい組み立ての外し直しの宣言（RebuildDeclarations の宣言の形の Require*）は、どれも宣言の表（All）に"
                    + "ちょうど 1 回載り、表の要素はどれも宣言の形の関数にする（表から抜けた宣言は黙って一度も流れず、外れていなければならない"
                    + "制約を外さない計画が DB で断られる）",
                ParityCompare.Multiset,
                () =>
                    SyncSelectionRuleTable.DeclaredRules(
                        SchedulingSources(),
                        SyncSelectionRuleTable.Rebuild
                    ),
                () =>
                    SyncSelectionRuleTable.ListedRules(
                        SchedulingSources(),
                        SyncSelectionRuleTable.Rebuild
                    )
            )
            {
                // いまの宣言は 7 つ（外部キーの 5 つと、一意制約・主キーの 1 つずつ）。拾い方が外れて正本が減ったら赤にする
                MinimumCanon = 7,
            },
            new(
                "sync-rebuild-declarations-declarations-probe",
                "自己検査の行：照合の行 sync-rebuild-declarations-table-complete が宣言の形の関数の宣言を集める形を、合成のソースで確かめる"
                    + "（正本は合成のソースで集めるべき宣言）。ファイルをまたぐ partial の宣言と条件コンパイルの外れる分岐の宣言を拾い、"
                    + "引数の数や型・戻り値の型が違う関数・Require で始まらない関数・ほかのクラスの宣言の形の関数は拾わない",
                ParityCompare.Sequence,
                () => ["RequireFirst", "RequireInBranch", "RequireSecond"],
                () =>
                    SyncSelectionRuleTable.DeclaredRules(
                        ProbeRebuildSources,
                        SyncSelectionRuleTable.Rebuild
                    )
            ),
            new(
                "sync-rebuild-declarations-entries-probe",
                "自己検査の行：照合の行 sync-rebuild-declarations-table-complete が宣言の表の要素を集める形を、合成のソースで確かめる"
                    + "（正本は合成のソースで集めるべき要素）。表の要素を重なりごと数え、メソッドの名前でない要素は綴りのまま数え、"
                    + "表でないフィールド・ほかのクラスの同じ名前のフィールドの要素は拾わない",
                ParityCompare.Sequence,
                () => ["Other.RequireElsewhere", "RequireFirst", "RequireSecond", "RequireSecond"],
                () =>
                    SyncSelectionRuleTable.ListedRules(
                        ProbeRebuildSources,
                        SyncSelectionRuleTable.Rebuild
                    )
            ),
            new(
                "sync-assessment-rules-table-complete",
                "同期の計画の評価の規則（AssessmentRules の規則の形の Warn*）は、どれも規則の表（All）にちょうど 1 回載り、表の要素は"
                    + "どれも規則の形の関数にする（表から抜けた規則は新しい計画器で一度も流れず、その警告が黙って消える）",
                ParityCompare.Multiset,
                () =>
                    SyncSelectionRuleTable.DeclaredRules(
                        AssessmentSources(),
                        SyncSelectionRuleTable.Assessment
                    ),
                () =>
                    SyncSelectionRuleTable.ListedRules(
                        AssessmentSources(),
                        SyncSelectionRuleTable.Assessment
                    )
            )
            {
                // いまの規則は 12（計画の事実だけを読む規則 9 つと、組み立ての結果も読む規則 3 つ）。拾い方が外れて正本が減ったら赤にする
                MinimumCanon = 12,
            },
            new(
                "sync-assessment-rules-declarations-probe",
                "自己検査の行：照合の行 sync-assessment-rules-table-complete が規則の形の関数の宣言を集める形を、合成のソースで確かめる"
                    + "（正本は合成のソースで集めるべき宣言）。引数が計画の事実の関数と組み立ての結果つきの関数の両方・ファイルをまたぐ"
                    + " partial の宣言・条件コンパイルの外れる分岐の宣言を拾い、引数の数や型・戻り値の型が違う関数・Warn で始まらない関数・"
                    + "ほかのクラスの規則の形の関数は拾わない",
                ParityCompare.Sequence,
                () => ["WarnByFacts", "WarnByPlan", "WarnInBranch"],
                () =>
                    SyncSelectionRuleTable.DeclaredRules(
                        ProbeAssessmentSources,
                        SyncSelectionRuleTable.Assessment
                    )
            ),
            new(
                "sync-assessment-rules-entries-probe",
                "自己検査の行：照合の行 sync-assessment-rules-table-complete が規則の表の要素を集める形を、合成のソースで確かめる"
                    + "（正本は合成のソースで集めるべき要素）。表の要素を重なりごと数え、メソッドの名前でない要素は綴りのまま数え、"
                    + "表でないフィールド・ほかのクラスの同じ名前のフィールドの要素は拾わない",
                ParityCompare.Sequence,
                () => ["Other.WarnElsewhere", "WarnByFacts", "WarnByPlan", "WarnByPlan"],
                () =>
                    SyncSelectionRuleTable.ListedRules(
                        ProbeAssessmentSources,
                        SyncSelectionRuleTable.Assessment
                    )
            ),
        ];

    /// <summary>評価の規則のフォルダ（src/QuickER.Provider/Sync/Assessment）のソースの本文</summary>
    private static IEnumerable<string> AssessmentSources() =>
        Directory
            .EnumerateFiles(
                Path.Combine(
                    WorkflowGuardTests.Root,
                    "src",
                    "QuickER.Provider",
                    "Sync",
                    "Assessment"
                ),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(path => !SourceFiles.IsBuildOutput(path))
            .Select(File.ReadAllText);

    /// <summary>評価の規則の自己検査の行の合成のソース（表のファイルと、規則のファイル 2 つ）</summary>
    private static readonly string[] ProbeAssessmentSources =
    [
        """
            internal static partial class AssessmentRules
            {
                internal static readonly AssessmentRule[] All =
                [
                    WarnByFacts,
                    WarnByPlan,
                    WarnByPlan,
                    Other.WarnElsewhere,
                ];

                internal static readonly AssessmentRule[] Extra = [WarnUnlisted];
            }

            internal static class Other
            {
                internal static readonly AssessmentRule[] All = [WarnOutside];

                public static IEnumerable<SyncPlanWarning> WarnOutside(PlanAssessment assessment) => [];
            }
            """,
        """
            internal static partial class AssessmentRules
            {
                public static IEnumerable<SyncPlanWarning> WarnByFacts(AssessmentFacts facts) => [];

                public static IEnumerable<SyncPlanWarning> WarnShared(
                    AssessmentFacts facts,
                    bool flag
                ) => [];

                public static List<SyncPlanWarning> WarnOtherReturn(AssessmentFacts facts) => [];

                public static IEnumerable<SyncPlanWarning> WarnOtherInput(SelectionState state) => [];

                public static IEnumerable<SyncPlanWarning> CollectSomething(AssessmentFacts facts) => [];

                public static void Add(List<SyncPlanWarning> warnings, IEnumerable<SyncPlanWarning> found) { }
            }
            """,
        """
            internal static partial class AssessmentRules
            {
                public static IEnumerable<SyncPlanWarning> WarnByPlan(PlanAssessment assessment) =>
                    WarnShared(assessment, flag: true);

            #if QUICKER_PROBE_BRANCH
                public static IEnumerable<SyncPlanWarning> WarnInBranch(AssessmentFacts facts) => [];
            #endif
            }
            """,
    ];

    /// <summary>新しい組み立てのフォルダ（src/QuickER.Provider/Sync/Scheduling）のソースの本文</summary>
    private static IEnumerable<string> SchedulingSources() =>
        Directory
            .EnumerateFiles(
                Path.Combine(
                    WorkflowGuardTests.Root,
                    "src",
                    "QuickER.Provider",
                    "Sync",
                    "Scheduling"
                ),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(path => !SourceFiles.IsBuildOutput(path))
            .Select(File.ReadAllText);

    /// <summary>外し直しの宣言の自己検査の行の合成のソース（表のファイルと、宣言のファイル 2 つ）</summary>
    private static readonly string[] ProbeRebuildSources =
    [
        """
            internal static partial class RebuildDeclarations
            {
                internal static readonly RebuildDeclaration[] All =
                [
                    RequireFirst,
                    RequireSecond,
                    RequireSecond,
                    Other.RequireElsewhere,
                ];

                private static readonly RebuildDeclaration[] Unused = [RequireFirst];
            }

            internal static class Other
            {
                internal static readonly RebuildDeclaration[] All = [RequireElsewhere];

                internal static IEnumerable<LiveConstraint> RequireElsewhere(SchedulingInput input) => [];
            }
            """,
        """
            internal static partial class RebuildDeclarations
            {
                private static IEnumerable<LiveConstraint> RequireFirst(SchedulingInput input) => [];

                private static IEnumerable<LiveConstraint> RequireShared(
                    SchedulingInput input,
                    bool leading
                ) => [];

                private static IEnumerable<LiveConstraint> CollectSomething(SchedulingInput input) => [];
            }
            """,
        """
            internal static partial class RebuildDeclarations
            {
                private static IEnumerable<LiveConstraint> RequireSecond(SchedulingInput input) =>
                    RequireShared(input, leading: true);

            #if QUICKER_PROBE_BRANCH
                private static IEnumerable<LiveConstraint> RequireInBranch(SchedulingInput input) => [];
            #endif

                private static List<LiveConstraint> RequireOtherReturn(SchedulingInput input) => [];

                private static IEnumerable<LiveConstraint> RequireOtherInput(SelectionState state) => [];
            }
            """,
    ];

    /// <summary>選んだ項目を外す規則のフォルダ（src/QuickER.Provider/Sync/Selection）のソースの本文</summary>
    private static IEnumerable<string> SelectionSources() =>
        Directory
            .EnumerateFiles(
                Path.Combine(
                    WorkflowGuardTests.Root,
                    "src",
                    "QuickER.Provider",
                    "Sync",
                    "Selection"
                ),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(path => !SourceFiles.IsBuildOutput(path))
            .Select(File.ReadAllText);

    /// <summary>自己検査の行の合成のソース（表のファイルと、規則のファイル 2 つ）</summary>
    private static readonly string[] ProbeSelectionSources =
    [
        """
            internal static partial class SelectionRules
            {
                internal static readonly SelectionRule[] BeforeRounds = [ExcludeFirst];

                internal static readonly SelectionRule[] EachRound =
                [
                    ExcludeSecond,
                    ExcludeSecond,
                    Other.ExcludeElsewhere,
                ];

                private static readonly SelectionRule[] Unused = [ExcludeFirst];
            }

            internal static class Other
            {
                internal static readonly SelectionRule[] EachRound = [ExcludeElsewhere];

                internal static IReadOnlyList<Withdrawal> ExcludeElsewhere(
                    SelectionState state,
                    SelectionContext context
                ) => [];
            }
            """,
        """
            internal static partial class SelectionRules
            {
                private static IReadOnlyList<Withdrawal> ExcludeFirst(
                    SelectionState state,
                    SelectionContext context
                ) => [];

                private static IReadOnlyList<Withdrawal> ExcludeShared(
                    IReadOnlyList<SchemaDiffItem> selected,
                    IReadOnlyList<string> tables,
                    SelectionContext context
                ) => [];

                private static IReadOnlyList<Withdrawal> FindSomething(
                    SelectionState state,
                    SelectionContext context
                ) => [];
            }
            """,
        """
            internal static partial class SelectionRules
            {
                private static IReadOnlyList<Withdrawal> ExcludeSecond(
                    SelectionState state,
                    SelectionContext context
                ) => ExcludeShared(state.Items, [], context);

            #if QUICKER_PROBE_BRANCH
                private static IReadOnlyList<Withdrawal> ExcludeInBranch(
                    SelectionState state,
                    SelectionContext context
                ) => [];
            #endif

                private static List<Withdrawal> ExcludeOtherReturn(
                    SelectionState state,
                    SelectionContext context
                ) => [];
            }
            """,
    ];
}
