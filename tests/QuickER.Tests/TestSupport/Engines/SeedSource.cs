using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>種の数を決めるプロファイル（普段の CI か、夜間の深い実行か）</summary>
public enum EngineProfile
{
    /// <summary>普段の CI（既定）</summary>
    Regular,

    /// <summary>夜間の実行（<c>QUICKER_ENGINE_PROFILE=nightly</c>）</summary>
    Nightly,
}

/// <summary>エンジンが流す種の決め方（変数名・既定の数・開始番号）</summary>
/// <remarks>
/// 変数名は各エンジンのいまの名前をそのまま渡す（CLAUDE.md・docs が参照している）。夜間の種は普段の種を先頭に含む
/// （同じ開始番号から連番で、数が多いだけ）。この前提で、普段の種で記録した指紋を夜間の実行でも照合できる。
/// </remarks>
public sealed record SeedSettings
{
    /// <summary>決め方を作る（値を検査する）</summary>
    /// <param name="countVariable">種の数を指定する環境変数（例 <c>QUICKER_SYNC_FUZZ_SEEDS</c>）</param>
    /// <param name="regularCount">普段の CI の既定の種の数</param>
    /// <param name="nightlyCount">夜間の既定の種の数（普段の数以上）</param>
    /// <param name="firstSeed">最初の種の番号（0 以上）</param>
    /// <param name="singleVariable">1 つの種だけを流す環境変数（無ければ null）</param>
    public SeedSettings(
        string countVariable,
        int regularCount,
        int nightlyCount,
        int firstSeed = 1,
        string? singleVariable = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countVariable);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(regularCount, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(nightlyCount, regularCount);
        ArgumentOutOfRangeException.ThrowIfNegative(firstSeed);

        if (singleVariable is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(singleVariable);
        }

        CountVariable = countVariable;
        RegularCount = regularCount;
        NightlyCount = nightlyCount;
        FirstSeed = firstSeed;
        SingleVariable = singleVariable;
    }

    /// <summary>種の数を指定する環境変数</summary>
    public string CountVariable { get; }

    /// <summary>普段の CI の既定の種の数</summary>
    public int RegularCount { get; }

    /// <summary>夜間の既定の種の数</summary>
    public int NightlyCount { get; }

    /// <summary>最初の種の番号</summary>
    public int FirstSeed { get; }

    /// <summary>1 つの種だけを流す環境変数（無ければ null）</summary>
    public string? SingleVariable { get; }
}

/// <summary>このテストの実行で流す種</summary>
/// <param name="Seeds">流す種（昇順）</param>
/// <param name="IsSingle">単独の種の変数で 1 つだけを流すか</param>
/// <param name="IsDefaultCount">数をプロファイルの既定から決めたか（変数の指定も単独の種も無い）</param>
/// <param name="Profile">プロファイル</param>
/// <param name="Settings">決め方</param>
/// <param name="Warnings">読めなかった指定などの警告（既定へ倒した理由）</param>
public sealed record SeedPlan(
    IReadOnlyList<int> Seeds,
    bool IsSingle,
    bool IsDefaultCount,
    EngineProfile Profile,
    SeedSettings Settings,
    IReadOnlyList<string> Warnings
)
{
    /// <summary>種 1 つを再現する手順（報告に添える）</summary>
    /// <param name="seed">再現する種（単独の種の変数が無いエンジンでは開始番号以上）</param>
    public string ReproductionFor(int seed)
    {
        if (Settings.SingleVariable is { } single)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"`{single}={seed}` で 1 種だけ流す"
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(seed, Settings.FirstSeed);
        var count = seed - Settings.FirstSeed + 1;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"`{Settings.CountVariable}={count}` で種 {Settings.FirstSeed}〜{seed} を流す"
        );
    }

    /// <summary>警告をテストの出力へ書く</summary>
    /// <param name="output">出力先（null なら現在のテストの出力）</param>
    public void WriteWarnings(ITestOutputHelper? output = null)
    {
        output ??= TestContext.Current.TestOutputHelper;

        if (output is null)
        {
            return;
        }

        foreach (var warning in Warnings)
        {
            output.WriteLine($"[種の指定] {warning}");
        }
    }
}

/// <summary>環境変数から流す種を決める（解析の規則を全エンジンで 1 つにする）</summary>
/// <remarks>
/// <para>
/// 優先は「単独の種の変数」→「個別の数の変数」→「プロファイルの既定（普段・夜間）」の順。
/// </para>
/// <para>
/// 解析は <see cref="NumberStyles.None"/> と <see cref="CultureInfo.InvariantCulture"/>（符号・空白・桁区切りを受けない）。
/// 数の変数が読めない・0 以下なら既定へ倒し、単独の種が読めなければ無視する（どちらも警告に書く＝黙って別の種を流さない）。
/// 単独の種の 0 は有効（開始番号が 0 のエンジンがある）。プロファイルは <c>nightly</c>（前後の空白を除き大文字小文字を無視）
/// なら夜間、未設定・空なら普段、それ以外は普段へ倒して警告に書く。
/// </para>
/// </remarks>
public static class SeedSource
{
    /// <summary>プロファイルを切り替える環境変数</summary>
    public const string ProfileVariable = "QUICKER_ENGINE_PROFILE";

    /// <summary>夜間を表す値</summary>
    public const string NightlyValue = "nightly";

    /// <summary>流す種を決める</summary>
    /// <param name="settings">決め方</param>
    /// <param name="environment">環境変数の読み取り（null ならプロセスの環境変数）</param>
    public static SeedPlan Resolve(SeedSettings settings, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        environment ??= Environment.GetEnvironmentVariable;

        var warnings = new List<string>();
        var profile = ResolveProfile(environment(ProfileVariable), warnings);

        if (settings.SingleVariable is { } singleVariable)
        {
            var singleText = environment(singleVariable);

            if (singleText is { Length: > 0 })
            {
                if (TryParse(singleText, out var single))
                {
                    return new SeedPlan([single], true, false, profile, settings, warnings);
                }

                warnings.Add($"{singleVariable}=\"{singleText}\" を読めないので無視した");
            }
        }

        var defaultCount =
            profile == EngineProfile.Nightly ? settings.NightlyCount : settings.RegularCount;
        var count = defaultCount;
        var isDefaultCount = true;
        var countText = environment(settings.CountVariable);

        if (countText is { Length: > 0 })
        {
            if (TryParse(countText, out var parsed) && parsed > 0)
            {
                count = parsed;
                isDefaultCount = false;
            }
            else
            {
                warnings.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{settings.CountVariable}=\"{countText}\" は 1 以上の数として読めないので既定の {defaultCount} 種にした"
                    )
                );
            }
        }

        var seeds = Enumerable.Range(settings.FirstSeed, count).ToList();
        return new SeedPlan(seeds, false, isDefaultCount, profile, settings, warnings);
    }

    private static EngineProfile ResolveProfile(string? text, List<string> warnings)
    {
        var trimmed = text?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return EngineProfile.Regular;
        }

        if (string.Equals(trimmed, NightlyValue, StringComparison.OrdinalIgnoreCase))
        {
            return EngineProfile.Nightly;
        }

        warnings.Add($"{ProfileVariable}=\"{text}\" は知らない値なので普段の実行にした");
        return EngineProfile.Regular;
    }

    private static bool TryParse(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
