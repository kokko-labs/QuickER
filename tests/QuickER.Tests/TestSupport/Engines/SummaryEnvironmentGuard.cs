using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// 種の要約が、実行する機械の環境（言語・タイムゾーン）に依らないことを確かめるガード
/// </summary>
/// <remarks>
/// <para>
/// 指紋の記録は日本語の Windows・日本時間の機械で作り、CI は英語・UTC で動く。要約に resx の文や機械の時差が入ると、
/// 生成器を変えていないのに CI だけで指紋が食い違う。要約は指紋のドリフトテストと同じ経路
/// （<see cref="SeedSummary.UnderInvariantCulture"/>）で作る。
/// </para>
/// <para>
/// 種は設定の最初の種から <see cref="SeedCount"/> 個（普段の既定の種の数を超えない範囲）。
/// </para>
/// </remarks>
public static class SummaryEnvironmentGuard
{
    /// <summary>確かめる先頭の種の数</summary>
    public const int SeedCount = 10;

    /// <summary>外側に切り替えるカルチャ（記録を作る機械と CI の言語）</summary>
    public static IReadOnlyList<string> OuterCultures { get; } = ["ja-JP", "en-US"];

    /// <summary>時差の形（<c>+09:00</c> など）</summary>
    private static readonly Regex UtcOffset = new(
        @"[+-][0-9][0-9]:[0-9][0-9]",
        RegexOptions.CultureInvariant
    );

    /// <summary>確かめる先頭の種</summary>
    /// <param name="settings">エンジンの種の決め方</param>
    public static IReadOnlyList<int> FirstSeeds(SeedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Enumerable
            .Range(settings.FirstSeed, Math.Min(SeedCount, settings.RegularCount))
            .ToList();
    }

    /// <summary>外側のカルチャを ja-JP と en-US に切り替えて作った要約が一致することを表明する</summary>
    /// <param name="engineId">エンジンの名前（報告用）</param>
    /// <param name="seeds">確かめる種</param>
    /// <param name="summary">種から要約を作る処理（ドリフトテストと同じもの）</param>
    public static void AssertCultureIndependent(
        string engineId,
        IReadOnlyList<int> seeds,
        Func<int, string> summary
    )
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(summary);

        var byCulture = OuterCultures.ToDictionary(
            name => name,
            name => SummariesUnder(name, seeds, summary)
        );
        var first = byCulture[OuterCultures[0]];

        foreach (var name in OuterCultures.Skip(1))
        {
            var other = byCulture[name];

            foreach (var seed in seeds)
            {
                if (first[seed] == other[seed])
                {
                    continue;
                }

                throw new XunitException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{engineId}: 種 {seed} の要約が、外側のカルチャ {OuterCultures[0]} と {name} で食い違う（指紋が機械の言語で変わる）。"
                            + $"最初に食い違う行: {FirstDifference(first[seed], other[seed])}"
                    )
                );
            }
        }
    }

    /// <summary>要約に時差の形（<c>[+-]hh:mm</c>）が現れないことを表明する</summary>
    /// <remarks>
    /// Kind が Local の日時を往復の書式で書くと、機械の時差が入る。タイムゾーンはプロセスの中で切り替えられないので、
    /// 時差が 0 の機械（CI の UTC）でも <c>+00:00</c> として当たるが、時差の付かない書き方へ戻したかどうかを確かめられるのは、
    /// 記録を作る時差のある機械（日本時間など）でだけ（そこでは Local の時差が <c>+09:00</c> として現れる）。
    /// </remarks>
    /// <param name="engineId">エンジンの名前（報告用）</param>
    /// <param name="seeds">確かめる種</param>
    /// <param name="summary">種から要約を作る処理（ドリフトテストと同じもの）</param>
    public static void AssertNoUtcOffset(
        string engineId,
        IReadOnlyList<int> seeds,
        Func<int, string> summary
    )
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(summary);

        foreach (var seed in seeds)
        {
            var text = SeedSummary.UnderInvariantCulture(() => summary(seed));
            var match = UtcOffset.Match(text);

            if (match.Success)
            {
                var start = Math.Max(0, match.Index - 40);
                var length = Math.Min(text.Length - start, match.Length + 80);
                throw new XunitException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{engineId}: 種 {seed} の要約に時差の形 \"{match.Value}\" がある（指紋が機械のタイムゾーンで変わる）: …{text.Substring(start, length)}…"
                    )
                );
            }
        }
    }

    /// <summary>外側のカルチャを切り替えて、種ごとの要約を作る</summary>
    private static Dictionary<int, string> SummariesUnder(
        string cultureName,
        IReadOnlyList<int> seeds,
        Func<int, string> summary
    )
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var outer = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = outer;
            CultureInfo.CurrentUICulture = outer;

            // 切り替えたつもりで切り替わらない環境（ICU が無くインバリアントのグローバリゼーションで動くなど）で、
            // 比べる意味の無いまま緑にならないようにする
            if (
                CultureInfo.CurrentCulture.Name != cultureName
                || CultureInfo.CurrentUICulture.Name != cultureName
            )
            {
                throw new XunitException(
                    $"外側のカルチャを {cultureName} に切り替えられなかった（CurrentCulture={CultureInfo.CurrentCulture.Name}・"
                        + $"CurrentUICulture={CultureInfo.CurrentUICulture.Name}）。ICU が無い・InvariantGlobalization の環境では、このガードは意味を持たない"
                );
            }

            return seeds.ToDictionary(
                seed => seed,
                seed => SeedSummary.UnderInvariantCulture(() => summary(seed))
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    /// <summary>2 つの要約の最初に食い違う行</summary>
    private static string FirstDifference(string left, string right)
    {
        var leftLines = left.Split('\n');
        var rightLines = right.Split('\n');

        for (var i = 0; i < Math.Max(leftLines.Length, rightLines.Length); i++)
        {
            var l = i < leftLines.Length ? leftLines[i] : "(無し)";
            var r = i < rightLines.Length ? rightLines[i] : "(無し)";

            if (l != r)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{i + 1} 行目 「{l}」 / 「{r}」"
                );
            }
        }

        return "(行は同じ・改行の違い)";
    }
}
