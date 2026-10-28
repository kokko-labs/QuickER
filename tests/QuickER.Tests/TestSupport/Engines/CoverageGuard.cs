using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// エンジンが黙って空振りしていないことを確かめるガード（流したケースの数・必ず出るべき形）
/// </summary>
/// <remarks>
/// 生成器や絞り込みの誤りでケースが 0 件になっても、照合する失敗が無ければテストは緑になる。
/// 最低件数と、必ず出るべき値の集まりを表明して、それを赤にする。
/// </remarks>
public static class CoverageGuard
{
    /// <summary>件数が最低件数以上であることを表明する</summary>
    /// <param name="what">何の件数か</param>
    /// <param name="actual">実際の件数</param>
    /// <param name="minimum">最低件数（1 以上）</param>
    public static void AtLeast(string what, int actual, int minimum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimum, 0);

        if (actual < minimum)
        {
            throw new XunitException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{what}: {actual} 件しか流れていない（最低 {minimum} 件）。生成器や絞り込みが空振りしていないか確かめる"
                )
            );
        }
    }

    /// <summary>必ず出るべき値がすべて観測されたことを表明する</summary>
    /// <typeparam name="T">値の型</typeparam>
    /// <param name="what">何の値か</param>
    /// <param name="observed">観測した値</param>
    /// <param name="required">必ず出るべき値（空は断る＝何も守らない）</param>
    public static void ContainsAll<T>(string what, IEnumerable<T> observed, IEnumerable<T> required)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(required);

        var requiredList = required.Distinct().ToList();

        if (requiredList.Count == 0)
        {
            throw new ArgumentException("必ず出るべき値が空（何も守らない）", nameof(required));
        }

        var seen = observed.ToHashSet();
        var missing = requiredList.Where(value => !seen.Contains(value)).ToList();

        if (missing.Count > 0)
        {
            throw new XunitException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{what}: 必ず出るべき {requiredList.Count} 個のうち {missing.Count} 個が出ていない: {string.Join(", ", missing)}"
                )
            );
        }
    }
}
