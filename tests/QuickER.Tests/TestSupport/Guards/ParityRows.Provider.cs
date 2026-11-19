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
                "同期の計画の組み立ての外し直しの宣言（RebuildDeclarations の宣言の形の Require*）は、どれも宣言の表（All）に"
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
                    + "どれも規則の形の関数にする（表から抜けた規則は一度も流れず、その警告が黙って消える）",
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
            new(
                "sync-step-switches-cover-every-step-type",
                "同期の計画の文の型（SyncStep.cs の SyncStep の派生）のうちセクションに入る型は、文の型で振り分ける所（StepSwitchSites）の"
                    + "どれにもちょうど 1 回現れ、セクションに入らない型（StepsOutsideSections）はどこにも現れない（文の型を足して振り分けに"
                    + "足し忘れると、その文がセクションの種類を持たないか、同期スクリプトに書かれない。振り分けの既定の腕は例外にするが、"
                    + "例外は計画を流したときにしか出ない）",
                ParityCompare.Multiset,
                () =>
                    [
                        .. StepSwitchSites.SelectMany(site =>
                            SyncStepSwitches
                                .DeclaredSteps([ReadRepoFile(StepTypesPath)!])
                                .Where(step => !StepsOutsideSections.ContainsKey(step))
                                .Select(step => $"{site.Label}: {step}")
                        ),
                    ],
                () =>
                    [
                        .. StepSwitchSites.SelectMany(site =>
                            SyncStepSwitches
                                .SwitchedSteps(ReadRepoFile(site.RelativePath), site.Method)
                                .Select(step => $"{site.Label}: {step}")
                        ),
                    ]
            )
            {
                // いまの文の型は 15（セクションに入るのは 13）× 振り分ける所 4。拾い方が外れて正本が減ったら赤にする
                MinimumCanon = 52,
            },
            new(
                "sync-step-switches-declarations-probe",
                "自己検査の行：照合の行 sync-step-switches-cover-every-step-type が文の型の宣言を集める形を、合成のソースで確かめる"
                    + "（正本は合成のソースで集めるべき宣言）。主コンストラクターの引数つきの基底・引数の無い基底・条件コンパイルの外れる分岐の"
                    + "宣言を拾い、SyncStep 自身・ほかの型を基底にした宣言・2 つ目以降の基底に SyncStep を書いた宣言は拾わない",
                ParityCompare.Sequence,
                () => ["FirstStep", "InBranchStep", "SecondStep"],
                () => SyncStepSwitches.DeclaredSteps(ProbeStepSources)
            ),
            new(
                "sync-step-switches-entries-probe",
                "自己検査の行：照合の行 sync-step-switches-cover-every-step-type が振り分けの腕の型を集める形を、合成のソースで確かめる"
                    + "（正本は合成のソースで集めるべき腕）。switch 式の型・定数・プロパティのパターンの腕、switch 文の型と変数・定数の case、"
                    + "条件コンパイルの外れる分岐の腕を拾い、_・default・プロパティのパターンの中の型・ほかのメソッドの腕は拾わない",
                ParityCompare.Sequence,
                () =>
                    [
                        "FirstStep",
                        "FirstStep",
                        "InBranchStep",
                        "QualifiedStep",
                        "SecondStep",
                        "SecondStep",
                    ],
                () => SyncStepSwitches.SwitchedSteps(ProbeStepSources[1], "Route")
            ),
            new(
                "sync-step-name-members-declared",
                "同期の計画の文の型（SyncStep.cs の SyncStep の派生）の、名前を運びうる型の位置指定の引数は、どれも同期の名前の入口"
                    + "（SqlNameText.PlanNames）が拾う出どころとして方言の DDL の表の名前の出どころの宣言（経路の StepMember）にあるか、"
                    + "理由つきの一覧（StepMembersNotNamed）にあるかの、ちょうど一方にある（文の型に名前のメンバーを足して入口が拾い忘れると、"
                    + "その名前だけが検証されずに同期スクリプトへ書かれる。宣言の経路が無ければ、出どころだけへ表せない名前を入れた行も無い）",
                ParityCompare.Multiset,
                () => SyncStepSwitches.NameMembers([ReadRepoFile(StepTypesPath)!]),
                () => [.. DeclaredStepMembers(), .. StepMembersNotNamed.Keys]
            )
            {
                // いまの名前を運びうる引数は 37（宣言の経路 30・理由つきの一覧 7）。拾い方が外れて正本が減ったら赤にする
                MinimumCanon = 37,
            },
            new(
                "sync-step-name-members-probe",
                "自己検査の行：照合の行 sync-step-name-members-declared が文の型の名前を運びうる引数を集める形を、合成のソースで確かめる"
                    + "（正本は合成のソースで集めるべき引数）。名前を運びうる型の引数（null 許容・総称の型・空白を含む型の綴りを含む）と"
                    + "条件コンパイルの外れる分岐の宣言の引数を拾い、決めていない型の引数は型を添えて拾い、文の由来と参照アクションの引数・"
                    + "SyncStep 自身・ほかの型を基底にした宣言の引数は拾わない",
                ParityCompare.Sequence,
                () =>
                    [
                        "FirstStep.Columns",
                        "FirstStep.Definition",
                        "FirstStep.Name",
                        "FirstStep.Names",
                        "FirstStep.TableName",
                        "FirstStep.When（名前を運ぶかを決めていない型 DateTime）",
                        "InBranchStep.Pairs",
                    ],
                () => SyncStepSwitches.NameMembers(ProbeNameMemberSources)
            ),
        ];

    /// <summary>
    /// 方言の DDL の表の名前の出どころの宣言が、経路の <c>StepMember</c> で名乗る文の型のメンバー（重なりを除く）
    /// </summary>
    private static IEnumerable<string> DeclaredStepMembers() =>
        QuickER
            .Tests.Provider.DialectDdlRows.NameSources.SelectMany(source => source.Routes)
            .Select(route => route.StepMember)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// 文の型の名前を運びうる引数のうち、同期の名前の入口（SqlNameText.PlanNames の文の型の振り分け）が拾わないものと、その理由
    /// （照合の行 sync-step-name-members-declared の写しの一方）
    /// </summary>
    private static readonly Dictionary<string, string> StepMembersNotNamed = new(
        StringComparer.Ordinal
    )
    {
        ["SetTableDescriptionStep.NewDescription"] =
            "説明の本文で名前でない（改行が正当で、コメントの側の SqlComment.Sanitize が受け持つ）",
        ["SetColumnDescriptionStep.NewDescription"] =
            "説明の本文で名前でない（改行が正当で、コメントの側の SqlComment.Sanitize が受け持つ）",
        ["CreateTableStep.UniqueConstraintNames"] =
            "表の定義の一意制約（入口が CreateTableStep.Definition として検証する名前）から差分が解決した名前で、入口は読まない（今より検証を広げない）",
        ["RebuildTableStep.Rebuild"] =
            "表の作り直しは SyncPlan.Rebuilds のループが拾う（宣言の rebuild-* と rebuild-definition-* の経路）",
        ["MoveColumnStep.TableName"] =
            "列を動かす文は SyncPlan.Reorders のループが拾う（宣言の reorder-table の経路）",
        ["MoveColumnStep.Column"] =
            "列を動かす文は SyncPlan.Reorders のループが拾う（宣言の reorder-move-column の経路）",
        ["MoveColumnStep.AfterColumn"] =
            "列を動かす文は SyncPlan.Reorders のループが拾う（宣言の reorder-move-after-column の経路）",
    };

    /// <summary>名前を運びうる引数の自己検査の行の合成のソース</summary>
    private static readonly string[] ProbeNameMemberSources =
    [
        """
            internal abstract record SyncStep(StepOrigin Origin);

            internal sealed record FirstStep(
                StepOrigin Origin,
                string TableName,
                string? Name,
                IReadOnlyList<string> Columns,
                Entity Definition,
                IReadOnlyDictionary<UniqueConstraint,  string>? Names,
                ForeignKeyReferentialAction OnDelete,
                DateTime When
            ) : SyncStep(Origin);

            internal sealed record OtherBased(StepOrigin Origin, string TableName) : OtherBase(Origin);

            #if QUICKER_PROBE_BRANCH
            internal sealed record InBranchStep(StepOrigin Origin, IReadOnlyList<ForeignKeyColumnNamePair> Pairs)
                : SyncStep(Origin);
            #endif
            """,
    ];

    /// <summary>文の型を宣言するファイル（リポジトリの根からの相対パス）</summary>
    private const string StepTypesPath = "src/QuickER.Provider/Sync/Steps/SyncStep.cs";

    /// <summary>
    /// 文の型で振り分ける所（照合の行 sync-step-switches-cover-every-step-type の写し）。振り分ける所を足すときは、ここに 1 行足す
    /// </summary>
    private static readonly SyncStepSwitches.Site[] StepSwitchSites =
    [
        new("section-of", "src/QuickER.Provider/Sync/Steps/StepSections.cs", "SectionOf"),
        new("builder-base", "src/QuickER.Provider/SyncScriptBuilderBase.cs", "AppendStep"),
        new(
            "builder-oracle",
            "src/QuickER.Provider.Oracle/OracleSyncScriptBuilder.cs",
            "AppendStep"
        ),
        new("name-text", "src/QuickER.Provider/SqlNameText.cs", "PlanNames"),
    ];

    /// <summary>セクションに入らない文の型と、その理由（振り分ける所のどこにも現れない）</summary>
    private static readonly Dictionary<string, string> StepsOutsideSections = new(
        StringComparer.Ordinal
    )
    {
        ["RebuildTableStep"] =
            "表の作り直しは SyncPlan.Rebuilds として読み、SQLite のビルダーが再構築の文を織り交ぜる（セクションの文として書かない）",
        ["MoveColumnStep"] =
            "列を動かす文は SyncPlan.Reorders として読み、MySQL のビルダーが列順の節で書く（セクションの文として書かない）",
    };

    /// <summary>リポジトリの根からの相対パスのファイルの本文（無ければ null）</summary>
    private static string? ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(
            WorkflowGuardTests.Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)
        );

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>文の型の自己検査の行の合成のソース（宣言のファイルと、振り分けのファイル）</summary>
    private static readonly string[] ProbeStepSources =
    [
        """
            internal abstract record SyncStep(StepOrigin Origin);

            internal sealed record FirstStep(StepOrigin Origin, string TableName) : SyncStep(Origin);

            internal sealed record SecondStep : SyncStep
            {
            }

            internal sealed record OtherBased(StepOrigin Origin) : OtherBase(Origin);

            internal sealed record LaterBase(StepOrigin Origin) : IStep, SyncStep;

            #if QUICKER_PROBE_BRANCH
            internal sealed record InBranchStep(StepOrigin Origin) : SyncStep(Origin);
            #endif
            """,
        """
            internal static class Router
            {
                private static int Route(SyncStep step)
                {
                    var kind = step switch
                    {
                        FirstStep => 1,
                        SecondStep { Pair: OtherStep } => 2,
                        Steps.QualifiedStep => 3,
            #if QUICKER_PROBE_BRANCH
                        InBranchStep => 4,
            #endif
                        _ => 0,
                    };

                    switch (step)
                    {
                        case FirstStep first:
                            return first.Length;

                        case SecondStep:
                            return 2;

                        default:
                            return kind;
                    }
                }

                private static int Other(SyncStep step) =>
                    step switch
                    {
                        OutsideStep => 1,
                        _ => 0,
                    };
            }
            """,
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

    /// <summary>組み立てのフォルダ（src/QuickER.Provider/Sync/Scheduling）のソースの本文</summary>
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
