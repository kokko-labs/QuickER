using System.IO;
using QuickER.Tests.Docs;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：同期の計画（選んだ項目を外す規則の宣言と規則の表）</summary>
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
