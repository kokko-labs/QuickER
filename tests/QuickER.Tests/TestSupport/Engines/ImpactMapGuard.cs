using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// 影響の地図の照合：src のすべての単位が、どれかの守りの範囲にあるか、理由つきの守りの無い領域にあるかの、ちょうど一方であること
/// </summary>
/// <remarks>
/// <para>
/// 因子の棚卸し（<see cref="FactorInventoryGuard"/>）と同じ形。単位は <see cref="ImpactUnits.Enumerate"/> がファイルシステムから
/// 数えるので、src に新しいプロジェクト・直下のフォルダを足すと、守りの範囲に入れるか理由を書くまで赤になる。
/// </para>
/// <para>
/// 赤にする問題：守りにも守りの無い領域にも入らない単位・守りがあるのに守りの無い領域にも載っている単位（プロジェクトごとの宣言は、
/// その下の単位に守りがあればプロジェクト自身が「一部」になるので、ここで捕まる）・存在しないパスや単位でないパスの宣言・宣言の重複・
/// プロジェクトごと載せた単位の下のフォルダの重ね書き・空か具体的でない理由・知らない分類・守りの名前の重複・存在しない範囲・
/// src のプロジェクトの中を指さない範囲。
/// </para>
/// </remarks>
public static class ImpactMapGuard
{
    /// <summary>照合の問題をすべて並べる（無ければ空）</summary>
    /// <param name="map">地図</param>
    /// <param name="repositoryRoot">リポジトリの根（宣言と範囲のパスの存在を確かめる）</param>
    public static IReadOnlyList<string> Problems(ImpactMap map, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var problems = new List<string>();
        var units = map.Units.ToHashSet(StringComparer.Ordinal);

        foreach (var row in map.Rows)
        {
            var declaredHere = map.Unprotected.Any(area => area.Unit == row.Unit);

            if (row.Coverage == UnitCoverage.None && row.Unprotected is null)
            {
                problems.Add(
                    $"{row.Unit}: どの守りの範囲にも入らず、守りの無い領域にも無い（守りの範囲に入れるか、理由つきで守りの無い領域に書く）"
                );
            }
            else if (row.Coverage != UnitCoverage.None && declaredHere)
            {
                problems.Add(
                    $"{row.Unit}: 守り（{string.Join("・", row.Protections)}）があるのに、守りの無い領域にも載っている"
                );
            }
        }

        var seenAreas = new HashSet<string>(StringComparer.Ordinal);

        foreach (var area in map.Unprotected)
        {
            var unit = area.Unit ?? "";

            if (!seenAreas.Add(unit))
            {
                problems.Add($"{unit}: 守りの無い領域が重複している");
            }

            if (!units.Contains(unit))
            {
                problems.Add(
                    File.Exists(Path.Combine(repositoryRoot, unit))
                    || Directory.Exists(Path.Combine(repositoryRoot, unit))
                        ? $"{unit}: 守りの無い領域が単位（プロジェクトか、その直下のフォルダ）でない"
                        : $"{unit}: 守りの無い領域のパスが存在しない"
                );
            }
            else
            {
                var project = ImpactMap.ProjectOf(unit);

                if (project != unit && map.Unprotected.Any(other => other.Unit == project))
                {
                    problems.Add(
                        $"{unit}: プロジェクト {project} ごと守りの無い領域に載っているのに、その下のフォルダを重ねて書いている"
                    );
                }
            }

            if (!Enum.IsDefined(area.Kind))
            {
                problems.Add(
                    $"{unit}: 守りの無い領域の分類 {(int)area.Kind} が Pending / Accepted でない"
                );
            }

            var reason = area.Reason?.Trim() ?? "";

            if (reason.Length == 0)
            {
                problems.Add($"{unit}: 守りの無い領域の理由が空");
            }
            else if (FactorInventoryGuard.IsVagueReason(reason))
            {
                problems.Add(
                    $"{unit}: 守りの無い領域の理由「{reason}」が具体的でない（{FactorInventoryGuard.MinimumReasonLength} 文字以上で、守りが無い事情を書く）"
                );
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var protection in map.Protections)
        {
            if (!names.Add(protection.Name))
            {
                problems.Add($"{protection.Name}: 守りの名前が重複している");
            }

            foreach (var cover in protection.Covers)
            {
                if (!cover.StartsWith(ImpactUnits.SourceRoot + "/", StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{protection.Name}: 範囲 {cover} が src のプロジェクトの中を指さない（横断の守りなら範囲を空にする）"
                    );
                }
                else if (
                    !File.Exists(Path.Combine(repositoryRoot, cover))
                    && !Directory.Exists(Path.Combine(repositoryRoot, cover))
                )
                {
                    problems.Add($"{protection.Name}: 範囲 {cover} が存在しない");
                }
            }
        }

        return problems;
    }

    /// <summary>照合の問題があれば、すべて並べて赤にする</summary>
    /// <param name="map">地図</param>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static void Verify(ImpactMap map, string repositoryRoot)
    {
        var problems = Problems(map, repositoryRoot);

        if (problems.Count > 0)
        {
            throw new XunitException(
                "影響の地図の照合に問題がある:\n  " + string.Join("\n  ", problems)
            );
        }
    }
}
