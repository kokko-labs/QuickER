using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQL Server の系統の乱数の保存（<see cref="BackendParitySqlServerSaveGenerator"/>）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、種 1 つの保存の行 1 つと部品 1 つ。観測は生成器が返した保存を読むだけで、乱数を引かない（種の指紋は変わらない）。
/// 因子 <see cref="OtherGone"/>（同じ保存のほかの行に、他者が消した行があるか）が、代替キーを書いた後に同じ保存のほかの行の理由で
/// 試行をやり直す経路の入力で、3 因子の組（役 × ほかの行が消えたか × 保存の引数）で網羅する。
/// </para>
/// <para>
/// 部品のケースは部品の因子（<see cref="PartAction"/> ほか）を持ち、kits の行だけの因子は None にする（kits の行のケースは部品の
/// 因子が None）。2 つのケースは別のケースなので、片方だけの因子の値ともう片方だけの因子の値の組は起こり得ない。
/// </para>
/// <para>
/// 普段の 8 種は数えて出力へ書くだけで、網羅しない組の照合は夜間の既定の種の数でだけ行う（8 種では 3 因子の組が埋まらない。
/// 代替キーの行と消えた行の組は、決まった形のケース <c>alternate-key-*-with-*</c> が普段の網で全組を流す）。
/// </para>
/// </remarks>
internal static class BackendParitySqlServerSaveDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string RandomScope = "random";

    /// <summary>因子：行の役</summary>
    public const string Role = "Role";

    /// <summary>因子：読んだ後に他者がしたこと</summary>
    public const string Condition = "Condition";

    /// <summary>因子：Before のフックが飛ばすか</summary>
    public const string Skipped = "Skipped";

    /// <summary>因子：同じ保存のほかの行に、他者が消した行があるか</summary>
    public const string OtherGone = "OtherGone";

    /// <summary>因子：保存の引数の組</summary>
    public const string Arguments = "Arguments";

    /// <summary>因子：手放す code を取る行が、その code を手放す行の前に渡されるか（取る役の行だけが持つ）</summary>
    public const string HandoffOrder = "HandoffOrder";

    /// <summary>因子：手放す code を取る行の、その code を手放す行に読んだ後に他者がしたこと（取る役の行だけが持つ）</summary>
    public const string ReleaserCondition = "ReleaserCondition";

    /// <summary>因子：部品の役（kits の行のケースは None）</summary>
    public const string PartAction = "PartAction";

    /// <summary>因子：部品を読んだ後に他者がしたこと（kits の行のケースは None）</summary>
    public const string PartCondition = "PartCondition";

    /// <summary>
    /// 因子：部品の初めの kit（New＝新しい部品・Kept＝code を変えない kit・HeldBack＝消す kit の code を取る後回しの kit・
    /// Deleted＝消す kit。kits の行のケースは None）
    /// </summary>
    public const string PartSource = "PartSource";

    /// <summary>因子：付け替える部品の、書く先の kit と初めの kit の渡す順（付け替えでない部品は Within・kits の行のケースは None）</summary>
    public const string PartOrder = "PartOrder";

    /// <summary>因子：新しい部品の外部キーの書き方（新しい部品でない部品と kits の行のケースは None）</summary>
    public const string PartForeignKey = "PartForeignKey";

    /// <summary>因子：部品を持たせる kit の役（kits の行のケースは None）</summary>
    public const string PartTarget = "PartTarget";

    /// <summary>因子：部品を持たせる kit に読んだ後に他者がしたこと（kits の行のケースは None）</summary>
    public const string PartTargetCondition = "PartTargetCondition";

    /// <summary>因子：部品を持たせる kit を Before のフックが飛ばすか（kits の行のケースは None）</summary>
    public const string PartTargetSkipped = "PartTargetSkipped";

    /// <summary>
    /// kits の行のケースだけが持つ因子（部品のケースは None）と、部品のケースだけが持つ因子（kits の行のケースは None）。
    /// 保存の引数は両方が持つ
    /// </summary>
    private static readonly string[] KitOnlyFactors = [Role, Condition, Skipped, OtherGone];

    private static readonly string[] PartOnlyFactors =
    [
        PartAction,
        PartCondition,
        PartSource,
        PartOrder,
        PartForeignKey,
        PartTarget,
        PartTargetCondition,
        PartTargetSkipped,
    ];

    /// <summary>ほかの側のケースの水準</summary>
    private const string None = "None";

    /// <summary>手放しと取りの因子の、取る役でない行の水準</summary>
    private const string NoHandoff = None;

    /// <summary>付け替えでない部品の渡す順の水準</summary>
    private const string Within = "Within";

    /// <summary>書く先の kit が初めの kit より先に渡される</summary>
    private const string TargetFirst = "TargetFirst";

    /// <summary>初めの kit が書く先の kit より先に渡される</summary>
    private const string SourceFirst = "SourceFirst";

    /// <summary>1 段目で code を手放す kit の役（部品を持たせない＝付け替えと単独の削除の初めの kit にならない）</summary>
    private static readonly string[] FirstStageReleasers =
    [
        nameof(SaveRowRole.ChangeCode),
        nameof(SaveRowRole.TakeReleasedCode),
        nameof(SaveRowRole.SwapCode),
    ];

    /// <summary>手放す行が先に渡される</summary>
    private const string ReleaserFirst = "ReleaserFirst";

    /// <summary>取る行が先に渡される</summary>
    private const string TakerFirst = "TakerFirst";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.GeneratedRuntime,
            [
                new Factor(Role, [new Level(None), .. Levels<SaveRowRole>()], "行の役"),
                new Factor(
                    Condition,
                    [new Level(None), .. Levels<SaveRowCondition>()],
                    "読んだ後に他者がしたこと"
                ),
                new Factor(
                    Skipped,
                    [new Level(None), new Level("False"), new Level("True")],
                    "Before のフックが飛ばすか"
                ),
                new Factor(
                    OtherGone,
                    [new Level(None), new Level("False"), new Level("True")],
                    "同じ保存のほかの行に、他者が消した行があるか"
                ),
                new Factor(Arguments, Levels<SaveArguments>(), "保存の引数の組"),
                new Factor(
                    HandoffOrder,
                    [new Level(NoHandoff), new Level(ReleaserFirst), new Level(TakerFirst)],
                    "手放す code を取る行が、手放す行の前に渡されるか"
                ),
                new Factor(
                    ReleaserCondition,
                    [new Level(NoHandoff), .. Levels<SaveRowCondition>()],
                    "手放す行に読んだ後に他者がしたこと"
                ),
                new Factor(PartAction, [new Level(None), .. Levels<SavePartRole>()], "部品の役"),
                new Factor(
                    PartCondition,
                    [
                        new Level(None),
                        new Level(nameof(SaveRowCondition.Fresh)),
                        new Level(nameof(SaveRowCondition.Gone)),
                    ],
                    "部品を読んだ後に他者がしたこと（部品は版を持たないので Stale は無い）"
                ),
                new Factor(
                    PartSource,
                    [
                        new Level(None),
                        new Level("New"),
                        new Level("Kept"),
                        new Level("HeldBack"),
                        new Level("Deleted"),
                    ],
                    "部品の初めの kit"
                ),
                new Factor(
                    PartOrder,
                    [
                        new Level(None),
                        new Level(Within),
                        new Level(TargetFirst),
                        new Level(SourceFirst),
                    ],
                    "付け替える部品の、書く先の kit と初めの kit の渡す順"
                ),
                new Factor(
                    PartForeignKey,
                    [new Level(None), .. Levels<SavePartForeignKey>()],
                    "新しい部品の外部キーの書き方"
                ),
                new Factor(
                    PartTarget,
                    [new Level(None), .. Levels<SaveRowRole>()],
                    "部品を持たせる kit の役"
                ),
                new Factor(
                    PartTargetCondition,
                    [new Level(None), .. Levels<SaveRowCondition>()],
                    "部品を持たせる kit に読んだ後に他者がしたこと"
                ),
                new Factor(
                    PartTargetSkipped,
                    [new Level(None), new Level("False"), new Level("True")],
                    "部品を持たせる kit を Before のフックが飛ばすか"
                ),
            ],
            [
                new FactorGroup(Role, OtherGone, Arguments),
                new FactorGroup(Role, ReleaserCondition, HandoffOrder),
                new FactorGroup(PartAction, PartTarget, Arguments),
                new FactorGroup(PartAction, PartSource, PartOrder),
                new FactorGroup(PartAction, PartCondition, Arguments),
            ],
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.GeneratedRuntime,
            [
                CoverageGap.AcceptedTuple(
                    RandomScope,
                    "F4",
                    "他者が消した行の削除は、0 行の削除を実装先ごとに違って扱うことを受け入れた差（行なし削除の非対称・F4 の決定で直さない）で、比べると結果が割れる",
                    (Role, nameof(SaveRowRole.Delete)),
                    (Condition, nameof(SaveRowCondition.Gone))
                ),
                .. new[] { SaveRowCondition.Fresh, SaveRowCondition.Stale }.Select(releaser =>
                    CoverageGap.AcceptedTuple(
                        RandomScope,
                        "G3-B",
                        "挿入する行が、同じ保存で後ろに渡した更新する行の手放す code を取る保存は、QuickER 版が挿入で断られ、EF Core 版が代替キーを先に書いて通す、受け入れた差（docs のグラフ保存の書く順と EF Core の代替キーの節）。生成器は新しい kit をまだ code を持つ手放す行の後へ回す（手放す行が消えていれば code は空いているので順を変えない）",
                        (Role, nameof(SaveRowRole.NewTakesReleasedCode)),
                        (HandoffOrder, TakerFirst),
                        (ReleaserCondition, releaser.ToString())
                    )
                ),
                // 他者が消した部品の削除（単独・カスケード）は、kits の行の F4 と同じ 0 行の削除の非対称
                .. PartDeleteGoneTuples()
                    .Select(items =>
                        CoverageGap.AcceptedTuple(RandomScope, "F4", PartDeleteGoneReason, items)
                    ),
                // 1 段目で code を手放す kit には部品を持たせない（単独の削除の持たせる kit にならない）
                .. FirstStageReleasers
                    .SelectMany(target =>
                        ArgumentLevels
                            .Select(arguments =>
                                new[]
                                {
                                    (Arguments, arguments),
                                    (PartAction, nameof(SavePartRole.Delete)),
                                    (PartTarget, target),
                                }
                            )
                            .Prepend([
                                (PartAction, nameof(SavePartRole.Delete)),
                                (PartTarget, target),
                            ])
                    )
                    .Select(items =>
                        CoverageGap.AcceptedTuple(RandomScope, "H2-D1", ReleaserPartsReason, items)
                    ),
                CoverageGap.AcceptedTuple(
                    RandomScope,
                    "H2-D2",
                    "code を入れ替える kit のある保存はどの実装先でも最初の更新で断られ、他者が消した部品を置くと、QuickER 版は前にある部品の競合を、EF Core 版は先に書く代替キーの断りを報告する（docs のグラフ保存と EF Core モードに書いた受け入れた差。決まった形のケース alternate-key-gone-part-then-taker-first が両方の値を固定する）。生成器はそうした保存に他者が消した部品を置かない",
                    (PartCondition, Gone),
                    (PartTarget, nameof(SaveRowRole.SwapCode))
                ),
                // 部品を持たせる kit を Before のフックが飛ばすと、その kit の code が書かれず、部品の外部キーがどの実装先でも断られる
                .. new[]
                {
                    nameof(SaveRowRole.ChangeCode),
                    nameof(SaveRowRole.TakeReleasedCode),
                    nameof(SaveRowRole.SwapCode),
                    nameof(SaveRowRole.New),
                    nameof(SaveRowRole.NewTakesReleasedCode),
                }.Select(target =>
                    CoverageGap.AcceptedTuple(
                        RandomScope,
                        "H2",
                        "Before のフックが飛ばした kit は書かれない（新しい kit は挿入されず、code を変える kit は元の code のまま）ので、その kit の新しい code を参照する部品の付け替え・新しい部品は、どの実装先でも外部キーに断られて保存の全体が止まり、ほかの行を比べなくなる（Save フックの Before の false はその 1 件の書き込みだけを飛ばす＝docs の Save フックの節）。code を変えない kit を飛ばす形と、後回しの kit の単独の削除の部品は作る",
                        (PartTarget, target),
                        (PartTargetSkipped, bool.TrueString)
                    )
                ),
            ]
        );

    private const string Gone = nameof(SaveRowCondition.Gone);

    /// <summary>保存の引数の水準（網羅しない組の一覧より前に初期化されるよう、計算するプロパティにする）</summary>
    private static IEnumerable<string> ArgumentLevels =>
        Enum.GetValues<SaveArguments>().Select(value => value.ToString());

    /// <summary>他者が消した部品の削除の組（2 因子の組と、保存の引数との 3 因子の組）</summary>
    private static IEnumerable<(string Factor, string Level)[]> PartDeleteGoneTuples()
    {
        yield return [(PartAction, nameof(SavePartRole.Cascade)), (PartCondition, Gone)];
        yield return [(PartAction, nameof(SavePartRole.Delete)), (PartCondition, Gone)];
        yield return [(PartCondition, Gone), (PartTarget, nameof(SaveRowRole.Delete))];

        foreach (var arguments in ArgumentLevels)
        {
            foreach (
                var action in new[] { nameof(SavePartRole.Cascade), nameof(SavePartRole.Delete) }
            )
            {
                yield return [(Arguments, arguments), (PartAction, action), (PartCondition, Gone)];
            }
        }
    }

    private const string PartDeleteGoneReason =
        "他者が消した部品の削除（単独の削除・消す kit のカスケード）は、0 行の削除を実装先ごとに違って扱うことを受け入れた差（行なし削除の非対称・kits の行の F4 と同じ）で、比べると結果が割れる";

    private const string ReleaserPartsReason =
        "1 段目で code を手放す kit（code を変える・手放す code を取る・入れ替える）の部品は、同じ保存で付け替えると EF Core 版が代替キーを先に書いて外部キーに断られ、QuickER 版は付け替え先を先に渡せば通る（docs のグラフ保存と EF Core モードに書いた受け入れた差。決まった形のケース alternate-key-part-moved-off-released-* が両方の値を固定する）。単独で消すか残すと、どの実装先でも参照された code の変更として断られる。生成器はそうした kit に部品を持たせない";

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.GeneratedRuntime,
            "ParitySqlServerSaves",
            "実装先どうしの一致（SQL Server の系統の乱数の保存）",
            Table,
            0,
            [
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerSaveComparison.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRuntimeTests.SqlServerSaveComparison.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerSaveDimensions.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerSaveGenerator.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerRuntimeTests.RandomSaves.cs",
            ],
            [
                "src/QuickER.Runtime",
                "src/QuickER.Runtime.SqlServer",
                "src/QuickER.Runtime.EntityFrameworkCore",
                "src/QuickER.Runtime.AspNetCore",
            ]
        );

    /// <summary>1 つの種の保存の kits の行と部品を数える（1 行・1 部品が 1 ケース）</summary>
    public static void Observe(CoverageCounter counter, SqlServerSaveScenario scenario)
    {
        foreach (var row in scenario.Rows)
        {
            var otherGone = scenario.Rows.Any(other =>
                !ReferenceEquals(other, row) && other.Condition == SaveRowCondition.Gone
            );
            var (order, releaserCondition) = Handoff(scenario, row);
            var assignment = new Dictionary<string, string>
            {
                [Role] = row.Role.ToString(),
                [Condition] = row.Condition.ToString(),
                [Skipped] = row.Skipped.ToString(),
                [OtherGone] = otherGone.ToString(),
                [Arguments] = scenario.Arguments.ToString(),
                [HandoffOrder] = order,
                [ReleaserCondition] = releaserCondition,
            };

            foreach (var factor in PartOnlyFactors)
            {
                assignment[factor] = None;
            }

            counter.Observe(assignment);
        }

        var rows = scenario.Rows.ToList();

        foreach (var part in scenario.Parts)
        {
            var target = rows.Single(row => row.KitId == part.TargetKitId);
            var owner = part.OwnerKitId is { } ownerId
                ? rows.Single(row => row.KitId == ownerId)
                : null;
            var assignment = new Dictionary<string, string>
            {
                [Arguments] = scenario.Arguments.ToString(),
                [HandoffOrder] = NoHandoff,
                [ReleaserCondition] = NoHandoff,
                [PartAction] = part.Role.ToString(),
                [PartCondition] = part.Condition.ToString(),
                [PartSource] = owner switch
                {
                    null => "New",
                    { Role: SaveRowRole.Delete } => "Deleted",
                    { Role: SaveRowRole.TakeDeletedCode } => "HeldBack",
                    { Role: SaveRowRole.Rename } => "Kept",
                    // 1 段目で code を手放す kit の部品は作らない（作れば宣言に無い水準で宣言の誤りになる）
                    _ => owner.Role.ToString(),
                },
                [PartOrder] =
                    part.Role != SavePartRole.Move ? Within
                    : rows.IndexOf(target) < rows.IndexOf(owner!) ? TargetFirst
                    : SourceFirst,
                [PartForeignKey] =
                    part.Role == SavePartRole.New ? part.ForeignKey.ToString() : None,
                [PartTarget] = target.Role.ToString(),
                [PartTargetCondition] = target.Condition.ToString(),
                [PartTargetSkipped] = target.Skipped.ToString(),
            };

            foreach (var factor in KitOnlyFactors)
            {
                assignment[factor] = None;
            }

            counter.Observe(assignment);
        }
    }

    /// <summary>
    /// 手放す code を取る役の行の、手放す行に対する渡す順と、手放す行に読んだ後に他者がしたこと（取る役でない行は両方
    /// <see cref="NoHandoff"/>）
    /// </summary>
    private static (string Order, string ReleaserCondition) Handoff(
        SqlServerSaveScenario scenario,
        SaveRowPlan row
    )
    {
        if (!row.TakesReleasedCode())
        {
            return (NoHandoff, NoHandoff);
        }

        var rows = scenario.Rows.ToList();
        var releaser = rows.Single(other =>
            !ReferenceEquals(other, row) && !other.IsNew() && other.InitialCode == row.TakenCode()
        );

        return (
            rows.IndexOf(row) < rows.IndexOf(releaser) ? TakerFirst : ReleaserFirst,
            releaser.Condition.ToString()
        );
    }

    /// <summary>原理的に起こり得ない組</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment) =>
        SidesAgree(assignment) && IsValidKitRow(assignment) && IsValidPart(assignment);

    /// <summary>
    /// kits の行のケースと部品のケースは別のケースなので、片方だけの因子の値と、もう片方だけの因子の値は同じケースに並ばない
    /// </summary>
    private static bool SidesAgree(IReadOnlyDictionary<string, string> assignment)
    {
        var kit = false;
        var part = false;

        foreach (var (factor, level) in assignment)
        {
            // 外部キーの書き方は新しい部品だけが持つ（ほかの部品も kits の行も None）
            if (factor == PartForeignKey)
            {
                part |= level != None;
            }
            else if (KitOnlyFactors.Contains(factor))
            {
                (level == None ? ref part : ref kit) = true;
            }
            else if (PartOnlyFactors.Contains(factor))
            {
                (level == None ? ref kit : ref part) = true;
            }
            else if (factor is HandoffOrder or ReleaserCondition && level != NoHandoff)
            {
                kit = true;
            }
        }

        return !(kit && part);
    }

    /// <summary>部品のケースの中で原理的に起こり得ない組</summary>
    private static bool IsValidPart(IReadOnlyDictionary<string, string> assignment)
    {
        var action = assignment.GetValueOrDefault(PartAction);
        var source = assignment.GetValueOrDefault(PartSource);
        var order = assignment.GetValueOrDefault(PartOrder);
        var foreignKey = assignment.GetValueOrDefault(PartForeignKey);
        var target = assignment.GetValueOrDefault(PartTarget);
        var condition = assignment.GetValueOrDefault(PartCondition);
        var targetCondition = assignment.GetValueOrDefault(PartTargetCondition);
        var isNew = action == nameof(SavePartRole.New);

        // 初めの kit が無いのは新しい部品だけ、外部キーの書き方を持つのも新しい部品だけ
        if (action is not null and not None)
        {
            if (source is not null and not None && isNew != (source == "New"))
            {
                return false;
            }

            if (foreignKey is not null && isNew == (foreignKey == None))
            {
                return false;
            }

            // 書く先と初めの kit の順を持つのは付け替えだけ
            if (order is not null && (action == nameof(SavePartRole.Move)) == (order == Within))
            {
                return false;
            }
        }

        // 新しい部品（初めの kit が無く、外部キーの書き方を持つ）は読んでいないので、他者が消すことは無く、付け替えでもない
        var newPart = isNew || source == "New" || foreignKey is not null and not None;

        if (newPart && condition == nameof(SaveRowCondition.Gone))
        {
            return false;
        }

        if (newPart && order is TargetFirst or SourceFirst)
        {
            return false;
        }

        // 外部キーの書き方を持つのは新しい部品だけ
        if (source is not null and not (None or "New") && foreignKey is not null and not None)
        {
            return false;
        }

        if (source == "New" && foreignKey == None)
        {
            return false;
        }

        // 消す kit に付け替えた部品はカスケードで消える（付け替えにならない）。code を変えない kit と後回しの kit の部品は、
        // 自分の kit のまま（更新・単独の削除）か付け替えなので、消す kit のコレクションに無い
        if (
            target == nameof(SaveRowRole.Delete)
            && (order is TargetFirst or SourceFirst || source is "Kept" or "HeldBack")
        )
        {
            return false;
        }

        // カスケードは消す kit のコレクションに残る部品の定義、消す kit に付け替えた部品はカスケードで消える（付け替えにならない）。
        // 消す kit のコレクションに持たせられるのは、カスケードで消す部品と新しい部品だけ
        if (
            target == nameof(SaveRowRole.Delete)
            && action is not null
            && action is not (None or nameof(SavePartRole.Cascade) or nameof(SavePartRole.New))
        )
        {
            return false;
        }

        if (
            action == nameof(SavePartRole.Cascade)
            && (
                target is not null and not nameof(SaveRowRole.Delete)
                || source is not null and not "Deleted"
            )
        )
        {
            return false;
        }

        // 消す kit の部品は、カスケードで消えるか付け替える
        if (
            source == "Deleted"
            && action is not null
            && action is not (nameof(SavePartRole.Cascade) or nameof(SavePartRole.Move))
        )
        {
            return false;
        }

        // 自分の kit のまま更新する部品は外部キーが親の code と同じでなければならない（違えば整合の検査が書く前に断る）ので、
        // 持たせる kit は code を変えない kit だけ
        if (
            action == nameof(SavePartRole.Update)
            && (
                target is not null and not nameof(SaveRowRole.Rename)
                || source is not null and not "Kept"
            )
        )
        {
            return false;
        }

        // 既存の部品を自分の kit に持たせる役（更新・単独の削除・カスケード）では、持たせる kit は新しい kit でなく、他者が消した
        // kit でもない（外部キーが NO ACTION なので、部品を持つ kit を他者は消せない）。初めの kit と持たせる kit は同じ
        if (
            action
            is nameof(SavePartRole.Update)
                or nameof(SavePartRole.Delete)
                or nameof(SavePartRole.Cascade)
        )
        {
            if (target is nameof(SaveRowRole.New) or nameof(SaveRowRole.NewTakesReleasedCode))
            {
                return false;
            }

            if (targetCondition == nameof(SaveRowCondition.Gone))
            {
                return false;
            }

            if (source == "Kept" && target is not null and not nameof(SaveRowRole.Rename))
            {
                return false;
            }

            if (
                source == "HeldBack"
                && target is not null and not nameof(SaveRowRole.TakeDeletedCode)
            )
            {
                return false;
            }
        }

        // 新しい kit は読んでいないので、読んだ後に他者が変えることは無い
        if (
            target is nameof(SaveRowRole.New) or nameof(SaveRowRole.NewTakesReleasedCode)
            && targetCondition is not null and not nameof(SaveRowCondition.Fresh)
        )
        {
            return false;
        }

        // 消す kit は他者に消されない（行なし削除の非対称で作らない kits の行の役と同じ定義）
        if (
            target == nameof(SaveRowRole.Delete)
            && targetCondition == nameof(SaveRowCondition.Gone)
        )
        {
            return false;
        }

        return true;
    }

    /// <summary>kits の行のケースの中で原理的に起こり得ない組</summary>
    private static bool IsValidKitRow(IReadOnlyDictionary<string, string> assignment)
    {
        var hasRole = assignment.TryGetValue(Role, out var role);

        // 新しい行は読んでいないので、読んだ後に他者が変えることは無い
        if (
            hasRole
            && role is nameof(SaveRowRole.New) or nameof(SaveRowRole.NewTakesReleasedCode)
            && assignment.TryGetValue(Condition, out var condition)
            && condition != nameof(SaveRowCondition.Fresh)
        )
        {
            return false;
        }

        // 手放しと取りの因子は、取る役の行だけが持ち、取る役の行は必ず持つ
        var taker =
            role
            is nameof(SaveRowRole.TakeReleasedCode)
                or nameof(SaveRowRole.SwapCode)
                or nameof(SaveRowRole.NewTakesReleasedCode);

        foreach (var factor in new[] { HandoffOrder, ReleaserCondition })
        {
            if (hasRole && assignment.TryGetValue(factor, out var level))
            {
                if (taker == (level == NoHandoff))
                {
                    return false;
                }
            }
        }

        var hasOrder = assignment.TryGetValue(HandoffOrder, out var order);
        var hasReleaser = assignment.TryGetValue(ReleaserCondition, out var releaser);

        // 2 つの因子は同じ行（取る役の行）だけが持つ
        if (hasOrder && hasReleaser && (order == NoHandoff) != (releaser == NoHandoff))
        {
            return false;
        }

        // 手放す行を他者が消したなら、取る行から見て同じ保存のほかの行が消えている
        if (
            hasReleaser
            && releaser == nameof(SaveRowCondition.Gone)
            && assignment.TryGetValue(OtherGone, out var otherGone)
            && otherGone == bool.FalseString
        )
        {
            return false;
        }

        return true;
    }

    private static List<Level> Levels<TEnum>()
        where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new Level(value.ToString()))];
}
