using System.Text;
using AwesomeAssertions;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 説明テキストの折返し（<see cref="DiagramMetricsService.WrapDescription(string?, double)"/>）が、
/// 長い説明でも計測回数を抑えることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 折返しは 1 行ごとに「幅に収まる最長の文字数」を探す。上限を段落の残り全体のまま二分探索すると、
/// 1 行決めるたびに残り全体の長さの文字列を計測することになり（計測コストは文字数に比例）、
/// 段落が長いほど二乗で効く（実測: 幅 200px で 10,000 文字 65ms・50,000 文字 2.3 秒）。
/// 長さを倍々に広げて括ってから二分探索することで、計測する文字列は 1 行分の数倍に収まる。
/// </para>
/// <para>
/// <b>括りは結果を変えない</b>ので、出力を比べるテストでは壊れても赤にならない。
/// ここでは<b>計測の回数と長さの合計</b>を表明する（括りを外すと跳ね上がって赤になる）。
/// 折返しの意味そのもの（どこで折るか）は <see cref="DiagramMetricsServiceTests"/> が持つ。
/// </para>
/// </remarks>
public class WrapDescriptionPerformanceTests
{
    /// <summary>折返し幅（カードの説明欄で使われる程度の値）</summary>
    private const double MaxWidth = 200;

    /// <summary>指定長の説明を作る（空白の有無で単語境界の処理が変わる）</summary>
    private static string BuildText(int length, bool withSpaces)
    {
        var text = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            text.Append(withSpaces && i % 7 == 6 ? ' ' : (char)('a' + i % 26));
        }

        return text.ToString();
    }

    /// <summary>計測の回数と、計測した文字列の長さの合計を数えながら折り返す</summary>
    private static (IReadOnlyList<string> Lines, int Calls, long TotalChars) WrapCounting(
        string text
    )
    {
        var calls = 0;
        long totalChars = 0;

        var lines = DiagramMetricsService.WrapDescription(
            text,
            MaxWidth,
            candidate =>
            {
                calls++;
                totalChars += candidate.Length;

                return DiagramMetricsService.MeasureBodyTextWidth(candidate);
            }
        );

        return (lines, calls, totalChars);
    }

    /// <summary>計測する文字列の総量が、説明の長さに対しておよそ比例に収まることを検証する</summary>
    /// <remarks>
    /// 上限は実測（50,000 文字で約 40 万文字）から余裕を見て「長さ × 20」に置く。
    /// 括りを外すと段落長の二乗で効くため、50,000 文字では 2 桁以上超える。
    /// </remarks>
    [Theory(DisplayName = "折返し: 計測する文字数の合計が説明の長さに比例する")]
    [InlineData(10_000, false)]
    [InlineData(10_000, true)]
    [InlineData(50_000, false)]
    [InlineData(50_000, true)]
    public void Wrap_MeasuresProportionallyToLength(int length, bool withSpaces)
    {
        var (lines, calls, totalChars) = WrapCounting(BuildText(length, withSpaces));

        lines.Should().NotBeEmpty();
        totalChars.Should().BeLessThan(length * 20L, "計測する文字列は 1 行分の数倍に収まる");
        calls.Should().BeLessThan(lines.Count * 40, "1 行あたりの計測回数は行長の対数どまり");
    }

    /// <summary>括りを入れても折返しの結果が変わらないことを検証する</summary>
    /// <remarks>
    /// 探索範囲を狭めただけで判定式は変えていないため、どこで折るかは同じでなければならない。
    /// 同じ計測手段を渡して、括りを入れる前の探索（上限＝段落の残り全体の二分探索）と突き合わせる。
    /// </remarks>
    [Theory(DisplayName = "折返し: 括りを入れても折る位置は変わらない")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("the quick brown fox jumps over the lazy dog and keeps running")]
    [InlineData("日本語の説明文はふつう空白を含まないため文字単位で折り返されます")]
    [InlineData("mixed 日本語 and english words 混在 テキスト 12345 67890")]
    [InlineData("line one\r\nline two\n\nline four")]
    [InlineData("supercalifragilisticexpialidocious")]
    public void Wrap_MatchesPlainBinarySearch(string text)
    {
        DiagramMetricsService
            .WrapDescription(text, MaxWidth, Measure)
            .Should()
            .Equal(WrapPlain(text, MaxWidth));
    }

    /// <summary>参照実装と本実装で共有する計測手段</summary>
    private static double Measure(string candidate) =>
        DiagramMetricsService.MeasureBodyTextWidth(candidate);

    /// <summary>括りを入れる前の探索（上限＝段落の残り全体の二分探索）で折り返す参照実装</summary>
    private static List<string> WrapPlain(string text, double maxWidth)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var start = 0;

            while (start < paragraph.Length)
            {
                var remaining = paragraph.Length - start;
                var low = 1;
                var high = remaining;
                var fit = 1;

                while (low <= high)
                {
                    var mid = (low + high) / 2;

                    if (Measure(paragraph.Substring(start, mid)) <= maxWidth)
                    {
                        fit = mid;
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }

                if (fit >= remaining)
                {
                    lines.Add(paragraph[start..]);
                    break;
                }

                var cut = fit;

                if (
                    char.IsAsciiLetterOrDigit(paragraph[start + fit - 1])
                    && char.IsAsciiLetterOrDigit(paragraph[start + fit])
                )
                {
                    var lastSpace = paragraph.LastIndexOf(' ', start + fit - 1, fit);

                    if (lastSpace > start)
                    {
                        cut = lastSpace - start;
                    }
                }

                lines.Add(paragraph.Substring(start, cut).TrimEnd());
                start += cut;

                while (start < paragraph.Length && paragraph[start] == ' ')
                {
                    start++;
                }
            }
        }

        return lines;
    }
}
