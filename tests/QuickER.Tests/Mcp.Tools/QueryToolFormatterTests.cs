using System.Globalization;
using AwesomeAssertions;
using QuickER.Mcp.Tools;
using Xunit.Sdk;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>名前付きクエリのツールの結果の整形の表（<see cref="QueryToolFormatterRows"/>）を流す</summary>
/// <remarks>
/// UI の言語の切り替えはプロセス共有の静的 <c>Strings.Culture</c> を変えず、スレッドの
/// <see cref="CultureInfo.CurrentUICulture"/> を try/finally で一時的に変えて戻す（<c>Strings.Culture</c> が未設定なら
/// <c>GetString</c> が CurrentUICulture を使う）。英語の整形器の行は UI の言語を変えない。
/// </remarks>
public sealed class QueryToolFormatterTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Cases => new(QueryToolFormatterRows.All.Select(row => row.Id));

    [Theory(DisplayName = "名前付きクエリの結果の整形の行は、期待どおりで改行は LF だけ")]
    [MemberData(nameof(Cases))]
    public void Row_Matches(string id)
    {
        var row = QueryToolFormatterRows.All.Single(row => row.Id == id);
        var formatter =
            row.Formatter == QueryToolFormatterKind.English
                ? QueryToolFormatter.English
                : QueryToolFormatter.Localized;
        string Render() => WithUiLanguage(row.Formatter, () => row.Render(formatter));

        var text = Render();
        var observation = new QueryFormatterObservation(text, Render);
        var problems = new List<string>();

        if (text.Contains('\r'))
        {
            problems.Add("改行は LF だけ: CR を含む（1 つの応答の中で改行の書き方を混ぜない）");
        }

        foreach (var check in row.Checks)
        {
            if (check.Verify(observation) is { } problem)
            {
                problems.Add($"{check.Description}: {problem}");
            }
        }

        if (problems.Count > 0)
        {
            throw new XunitException(
                $"行 {row.Id}（{row.Description}）・{row.Formatter}"
                    + Environment.NewLine
                    + "  "
                    + string.Join(Environment.NewLine + "  ", problems)
                    + Environment.NewLine
                    + "  text: "
                    + text.Replace("\n", "\n        ")
            );
        }
    }

    [Fact(DisplayName = "整形の行の Id は重ならない")]
    public void Rows_HaveUniqueIds() =>
        QueryToolFormatterRows
            .All.GroupBy(row => row.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Should()
            .BeEmpty();

    [Fact(
        DisplayName = "整形の行は、結果の状態 × 整形器の全組を持つ（行の要らない組は理由つきの除外の一覧に載せる）"
    )]
    public void Rows_CoverEveryStatusAndFormatter()
    {
        var covered = QueryToolFormatterRows
            .All.Select(row => (row.Status, row.Formatter))
            .ToHashSet();
        var excluded = QueryToolFormatterRows
            .Excluded.Select(entry => (entry.Status, entry.Formatter))
            .ToHashSet();
        var problems = new List<string>();

        foreach (var status in Enum.GetValues<QueryToolStatus>())
        {
            foreach (var formatter in Enum.GetValues<QueryToolFormatterKind>())
            {
                var pair = (status, formatter);

                if (!covered.Contains(pair) && !excluded.Contains(pair))
                {
                    problems.Add(
                        $"{status} × {formatter}: 行が無い（行を足すか、除外の一覧に理由つきで載せる）"
                    );
                }

                if (covered.Contains(pair) && excluded.Contains(pair))
                {
                    problems.Add($"{status} × {formatter}: 除外の一覧に載っているのに行がある");
                }
            }
        }

        foreach (var entry in QueryToolFormatterRows.Excluded)
        {
            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                problems.Add($"{entry.Status} × {entry.Formatter}: 除外の理由が空");
            }
        }

        problems.Should().BeEmpty();
    }

    /// <summary>整形器の UI の言語で処理を評価し、必ず元へ戻す（英語の整形器は UI の言語を変えない）</summary>
    private static T WithUiLanguage<T>(QueryToolFormatterKind formatter, Func<T> body)
    {
        var culture = formatter switch
        {
            QueryToolFormatterKind.LocalizedEn => "en",
            QueryToolFormatterKind.LocalizedJa => "ja",
            _ => null,
        };

        if (culture is null)
        {
            return body();
        }

        var previousUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
