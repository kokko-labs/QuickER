using AwesomeAssertions;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ソースを走査するガードの「理由つきの既知の一覧」と、実際に見つかった出現を厳密に照合する
/// </summary>
/// <remarks>
/// 照合は件数まで厳密で、2 つの向きの両方を赤にする：一覧に無い出現（件数が一覧と違うものを含む）と、一覧にあるのに
/// 無くなった出現（件数が違うものを含む）。何を数えるか（規則）と一覧の中身はガードごとの責務で、ここは照合だけを持つ。
/// </remarks>
internal static class GuardKnownList
{
    /// <summary>無くなった出現の既定の説明</summary>
    public const string DefaultStaleBecause = "無くなった出現は一覧から消す";

    /// <summary>見つかった出現（キーごとの件数）と既知の一覧（キーと件数）を照合する</summary>
    /// <param name="found">見つかった出現のキーごとの件数</param>
    /// <param name="known">既知の一覧（キーと件数。同じキーを 2 度書かない）</param>
    /// <param name="describe">食い違いの 1 行の表記（キーと件数から）</param>
    /// <param name="unexpectedBecause">一覧に無い出現があったときの説明</param>
    /// <param name="staleBecause">無くなった出現があったときの説明</param>
    /// <param name="describeUnexpected">一覧に無い出現の表記（省略時は <paramref name="describe"/>。行と文面を添えたいとき）</param>
    /// <param name="sortLines">食い違いの行を序数順に並べるか（既定は見つかった順・一覧の順）</param>
    public static void AssertMatches<TKey>(
        IReadOnlyDictionary<TKey, int> found,
        IEnumerable<(TKey Key, int Count)> known,
        Func<TKey, int, string> describe,
        string unexpectedBecause,
        string staleBecause = DefaultStaleBecause,
        Func<TKey, int, string>? describeUnexpected = null,
        bool sortLines = false
    )
        where TKey : notnull
    {
        var expected = known.ToDictionary(entry => entry.Key, entry => entry.Count);
        describeUnexpected ??= describe;

        var unexpected = found
            .Where(pair => !expected.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => describeUnexpected(pair.Key, pair.Value));
        var stale = expected
            .Where(pair => !found.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => describe(pair.Key, pair.Value));

        if (sortLines)
        {
            unexpected = unexpected.OrderBy(line => line, StringComparer.Ordinal);
            stale = stale.OrderBy(line => line, StringComparer.Ordinal);
        }

        string.Join(Environment.NewLine, unexpected).Should().BeEmpty(unexpectedBecause);
        string.Join(Environment.NewLine, stale).Should().BeEmpty(staleBecause);
    }
}
