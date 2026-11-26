using QuickER.Dialects;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;
using QuickER.Tests.Integration.GeneratedRuntime;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// Integration の札の対象の表の一覧（減るだけの写し <c>row-baseline.json</c> と照合する。tests/QuickER.Tests/TestSupport/CLAUDE.md
/// 「分岐の軸の台帳と新しい行の札」）
/// </summary>
/// <remarks>
/// <para>
/// 手で書く表は行 Id に、生成の表（形の行）は因子の水準と必ず含む行に札を付ける。表を足すときは、写しの JSON にその表の項目
/// （今の Id の全部）を手で足す（書き直しは写しに無い表を書かない）。
/// </para>
/// <para>一覧に入れない表（理由）：</para>
/// <list type="bullet">
/// <item>網羅の行の種の行（<see cref="SyncPlanCoverageRows"/> の Id に種の番号を含む行）：Id が種の番号を含み、生成器を変えるたびに選び直すので Id が安定しない。</item>
/// <item>来歴の行（<see cref="SyncPlanHistoryRows"/>・<see cref="SyncPlanScenarios"/> の <c>history-</c> の行）：全積の生成の行で、水準を足すと行が増える形。札は付けない（BX の対象外）。</item>
/// <item>形の行の方言ごとの行（<c>d4-</c>）：生成の表なので行 Id でなく水準と必ず含む行に札を付ける。</item>
/// <item>方言の型の実 DB の表：5 方言で共有する行を基底の 1 つの Fact が回し、札を付ける行の単位が無い。</item>
/// </list>
/// </remarks>
internal static class RowBaselineTables
{
    /// <summary>写しのファイル（リポジトリの根からの相対パス）</summary>
    public const string BaselinePath = "tests/QuickER.Tests.Integration/row-baseline.json";

    /// <summary>同期の期待の行の表の名前</summary>
    public const string ExpectationRows = "SyncPlanExpectationRows";

    /// <summary>札の対象の表（今の Id と札）</summary>
    public static IReadOnlyList<BaselineTable> All() =>
        [
            BaselineTable.Of(ExpectationRows, SyncPlanExpectationRows.Table),
            BaselineTable.Of(
                "SyncPlanScenarios",
                SyncPlanScenarios.Table.Where(row =>
                    !row.Id.StartsWith(SyncPlanShapeRows.IdPrefix, StringComparison.Ordinal)
                    && !row.Id.StartsWith(SyncPlanHistoryRows.IdPrefix, StringComparison.Ordinal)
                )
            ),
            // 手で書いた行は網でも流す行（種の行は実 DB でだけ流す＝Net が偽）
            BaselineTable.Of(
                "SyncPlanCoverageRows",
                SyncPlanCoverageRows.Table.Where(row => row.Input.Net)
            ),
            BaselineTable.Of("TranslatorParityPredicateRows", TranslatorParityPredicateRows.Table),
            BaselineTable.LevelsOf("SyncPlanShapeRows.Levels", SyncPlanShapeRows.Table),
            BaselineTable.RequiredRowsOf(
                "SyncPlanShapeRows.RequiredRows",
                SyncPlanShapeRows.RequiredRows
            ),
            BaselineTable.LevelsOf(
                "TranslatorParityBindingRows.Levels",
                TranslatorParityBindingRows.Table
            ),
            BaselineTable.RequiredRowsOf(
                "TranslatorParityBindingRows.RequiredRows",
                TranslatorParityBindingRows.RequiredRows
            ),
        ];

    /// <summary>
    /// 同期の期待の行で写しに無い行の方言の問題：実方言を 1 つでも持つ行は、能力の鍵の実方言（実 DB の段を持てば実 DB の段の方言も）と
    /// 札の外す方言を合わせて全実方言にする
    /// </summary>
    /// <param name="newRowIds">写しに無い期待の行の Id</param>
    public static IEnumerable<string> NewExpectationRowDialectProblems(
        IReadOnlyCollection<string> newRowIds
    )
    {
        foreach (var row in SyncPlanExpectationRows.Table.Where(row => newRowIds.Contains(row.Id)))
        {
            var excluded = (row.Tag?.Excluded ?? [])
                .Select(exclusion => exclusion.Target)
                .ToHashSet(StringComparer.Ordinal);

            foreach (
                var unknown in excluded.Where(target =>
                    !DialectNames.All.Contains(target, StringComparer.Ordinal)
                )
            )
            {
                yield return $"{ExpectationRows}: {row.Id}: 外す方言 {unknown} は実方言でない";
            }

            var netDialects = row
                .Input.Capabilities.Where(SyncPlanExpectationCapabilities.IsDialect)
                .ToHashSet(StringComparer.Ordinal);

            if (netDialects.Count == 0)
            {
                // 実方言を持たない行（合成の能力の鍵だけの網の行）は規則の外
                continue;
            }

            foreach (
                var missing in DialectNames.All.Where(dialect =>
                    !netDialects.Contains(dialect) && !excluded.Contains(dialect)
                )
            )
            {
                yield return $"{ExpectationRows}: {row.Id}: 新しい行は全実方言に流す（能力の鍵に {missing} が無い。外すなら札の Except に理由を書く）";
            }

            if (row.Input.Real is null)
            {
                continue;
            }

            var realDialects = row.Input.RealDialects.ToHashSet(StringComparer.Ordinal);

            foreach (
                var missing in DialectNames.All.Where(dialect =>
                    !realDialects.Contains(dialect) && !excluded.Contains(dialect)
                )
            )
            {
                yield return $"{ExpectationRows}: {row.Id}: 新しい行の実 DB の段は全実方言に流す（{missing} が無い。外すなら札の Except に理由を書く）";
            }
        }
    }
}
