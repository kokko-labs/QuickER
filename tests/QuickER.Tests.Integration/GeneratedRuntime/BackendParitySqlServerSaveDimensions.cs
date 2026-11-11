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
/// ケースは、種 1 つの保存の行 1 つ。観測は生成器が返した保存を読むだけで、乱数を引かない（種の指紋は変わらない）。
/// 因子 <see cref="OtherGone"/>（同じ保存のほかの行に、他者が消した行があるか）が、代替キーを書いた後に同じ保存のほかの行の理由で
/// 試行をやり直す経路の入力で、3 因子の組（役 × ほかの行が消えたか × 保存の引数）で網羅する。
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

    /// <summary>手放しと取りの因子の、取る役でない行の水準</summary>
    private const string NoHandoff = "None";

    /// <summary>手放す行が先に渡される</summary>
    private const string ReleaserFirst = "ReleaserFirst";

    /// <summary>取る行が先に渡される</summary>
    private const string TakerFirst = "TakerFirst";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.GeneratedRuntime,
            [
                new Factor(Role, Levels<SaveRowRole>(), "行の役"),
                new Factor(Condition, Levels<SaveRowCondition>(), "読んだ後に他者がしたこと"),
                new Factor(
                    Skipped,
                    [new Level("False"), new Level("True")],
                    "Before のフックが飛ばすか"
                ),
                new Factor(
                    OtherGone,
                    [new Level("False"), new Level("True")],
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
            ],
            [
                new FactorGroup(Role, OtherGone, Arguments),
                new FactorGroup(Role, ReleaserCondition, HandoffOrder),
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
            ]
        );

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

    /// <summary>1 つの種の保存の行を数える</summary>
    public static void Observe(CoverageCounter counter, SqlServerSaveScenario scenario)
    {
        foreach (var row in scenario.Rows)
        {
            var otherGone = scenario.Rows.Any(other =>
                !ReferenceEquals(other, row) && other.Condition == SaveRowCondition.Gone
            );
            var (order, releaserCondition) = Handoff(scenario, row);
            counter.Observe(
                new Dictionary<string, string>
                {
                    [Role] = row.Role.ToString(),
                    [Condition] = row.Condition.ToString(),
                    [Skipped] = row.Skipped.ToString(),
                    [OtherGone] = otherGone.ToString(),
                    [Arguments] = scenario.Arguments.ToString(),
                    [HandoffOrder] = order,
                    [ReleaserCondition] = releaserCondition,
                }
            );
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
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
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
