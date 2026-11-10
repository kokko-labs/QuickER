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
            ],
            [new FactorGroup(Role, OtherGone, Arguments)],
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
            counter.Observe(
                new Dictionary<string, string>
                {
                    [Role] = row.Role.ToString(),
                    [Condition] = row.Condition.ToString(),
                    [Skipped] = row.Skipped.ToString(),
                    [OtherGone] = otherGone.ToString(),
                    [Arguments] = scenario.Arguments.ToString(),
                }
            );
        }
    }

    /// <summary>原理的に起こり得ない組</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        // 新しい行は読んでいないので、読んだ後に他者が変えることは無い
        if (
            assignment.TryGetValue(Role, out var role)
            && role == nameof(SaveRowRole.New)
            && assignment.TryGetValue(Condition, out var condition)
            && condition != nameof(SaveRowCondition.Fresh)
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
