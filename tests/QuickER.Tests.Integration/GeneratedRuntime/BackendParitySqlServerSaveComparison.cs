using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using QuickER.Tests.GeneratedConcurrencyFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQL Server の系統の乱数の保存で、実装先の行を基準の QuickER 版の行と比べる（比べ方の例外は 1 つ）
/// </summary>
/// <remarks>
/// <para>
/// 例外：結果の行（先頭）の競合の理由とキーが、基準も実装先も「単独でも競合になる行」（候補）のどれかとその行の役に合う理由の組
/// なら同じとみなす。候補は生成器が引いた行の役から決め（<see cref="IndependentConflicts"/>）、観測した結果からは決めない。
/// 違う組が両方とも候補に入るのは候補が 2 つ以上ある保存だけ（候補が 1 つなら両方がその組で、行はもともと同じ）なので、
/// 例外が効くのは「単独でも競合になる行が 2 つ以上ある保存」に限られる。どの行の競合を報告するかは文の順で決まり、QuickER 版は
/// グラフ保存の書く順（削除しない書き込み → 削除）、EF Core は 1 回の SaveChanges の中で自分で決めた順で、最初に当たらなかった
/// 行を報告するため（docs の EF Core の節）。
/// </para>
/// <para>
/// 結果の行の後ろ（行ごとの版・保存先）は例外のときも等しさを求める。候補の外のキーや、役に合わない理由（消えた行の Modified・
/// 版の古い行の NotFound）は違いのまま。
/// </para>
/// </remarks>
internal static partial class BackendParitySqlServerSaveComparison
{
    /// <summary>
    /// 単独でも競合になる行（Before のフックが飛ばさない行のうち、insertWhenUpdateMissing なしで他者が消した行＝NotFound と、
    /// Optimistic で他者が版を進めた行＝Modified）と、その行が報告されるときの理由・キー
    /// </summary>
    public static IReadOnlyList<(SaveConflictReason Reason, string Key)> IndependentConflicts(
        SqlServerSaveScenario scenario
    ) =>
        [
            .. scenario
                .Rows.Where(row => !row.Skipped && !row.IsNew())
                .Select(row =>
                    (
                        Row: row,
                        Reason: row.Condition switch
                        {
                            SaveRowCondition.Gone when !scenario.InsertWhenUpdateMissing =>
                                (SaveConflictReason?)SaveConflictReason.NotFound,
                            SaveRowCondition.Stale
                                when scenario.Mode == ConcurrencyMode.Optimistic =>
                                SaveConflictReason.Modified,
                            _ => null,
                        }
                    )
                )
                .Where(candidate => candidate.Reason is not null)
                .Select(candidate =>
                    (
                        candidate.Reason!.Value,
                        candidate.Row.KitId.ToString(CultureInfo.InvariantCulture)
                    )
                ),
        ];

    /// <summary>
    /// 実装先の行が基準の行と違えば違いの説明を、同じ（例外の条件で同じとみなすときも含む）なら null を返す
    /// </summary>
    public static string? Difference(
        SqlServerSaveScenario scenario,
        IReadOnlyList<string> baseline,
        IReadOnlyList<string> actual
    )
    {
        if (baseline.SequenceEqual(actual, StringComparer.Ordinal))
        {
            return null;
        }

        var candidates = IndependentConflicts(scenario);

        if (
            baseline.Count > 0
            && actual.Count == baseline.Count
            && ParseConflict(baseline[0]) is { } reportedByBaseline
            && ParseConflict(actual[0]) is { } reportedByActual
            && candidates.Contains(reportedByBaseline)
            && candidates.Contains(reportedByActual)
            && baseline.Skip(1).SequenceEqual(actual.Skip(1), StringComparer.Ordinal)
        )
        {
            return null;
        }

        return "differs";
    }

    /// <summary>結果の行の書き方（競合はキーまで書く）</summary>
    public static string ConflictLine(SaveConflictException conflict) =>
        $"throws SaveConflictException reason={conflict.Reason} key={conflict.Key}";

    /// <summary>競合の結果の行から理由とキーを読む（競合の行でなければ null）</summary>
    private static (SaveConflictReason Reason, string Key)? ParseConflict(string line) =>
        ConflictPattern().Match(line) is { Success: true } match
        && Enum.TryParse<SaveConflictReason>(match.Groups[1].Value, out var reason)
            ? (reason, match.Groups[2].Value)
            : null;

    [GeneratedRegex(
        "^throws SaveConflictException reason=([A-Za-z]+) key=(.*)$",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex ConflictPattern();
}
