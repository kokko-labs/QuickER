using System.Text;
using System.Text.Json;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>ツールの行の入力（方言・前提の組み立て・確かめる呼び出しの列）</summary>
/// <param name="Dialect">図の方言（両ホストとも同じ方言の図で開く＝<see cref="TwoHostToolHarness.OpenAligned"/>）</param>
/// <param name="Arrange">前提の組み立て（成功を表明するツールの呼び出しか、ツールでは作れない形の直接の書き込み）</param>
/// <param name="Calls">確かめる呼び出し（呼び出しごとに期待を持つ）</param>
internal sealed record ToolScenario(
    string Dialect,
    IReadOnlyList<ToolArrangeStep> Arrange,
    IReadOnlyList<ToolScenarioCall> Calls
);

/// <summary>前提の組み立ての 1 手</summary>
/// <param name="Description">何をするか（失敗の文に出す）</param>
/// <param name="Apply">セッションへ当てる</param>
internal sealed record ToolArrangeStep(string Description, Action<ToolHostSession> Apply);

/// <summary>確かめる呼び出し 1 つ</summary>
/// <param name="Tool">ツール名</param>
/// <param name="Args">引数</param>
/// <param name="ExpectSuccess">宣言した成否（<see cref="ToolExpect.Ok"/> / <see cref="ToolExpect.Fails"/>・網羅の計数が読む）</param>
/// <param name="Checks">期待（<see cref="ToolExpect"/> が組む）</param>
/// <param name="CoreChecks">
/// 名前付きクエリのツールの共有コア（<see cref="QueryToolCore"/>）が返す構造の結果への期待（<see cref="ToolExpect.Core"/>）。
/// 空なら照合しない
/// </param>
internal sealed record ToolScenarioCall(
    string Tool,
    JsonElement Args,
    bool ExpectSuccess,
    IReadOnlyList<ToolCheck> Checks,
    IReadOnlyList<QueryCoreCheck> CoreChecks
);

/// <summary>共有コアの結果への期待 1 つ</summary>
/// <param name="Description">何を見るか（失敗の文に出す）</param>
/// <param name="Verify">合っていれば null・違えば理由</param>
internal sealed record QueryCoreCheck(string Description, Func<QueryToolOutcome, string?> Verify);

/// <summary>呼び出し 1 つの観測</summary>
/// <param name="Host">流したホスト</param>
/// <param name="Result">応答の文</param>
/// <param name="Success">成否</param>
/// <param name="Wrote">図を書いたか（<see cref="ToolHostSession.WroteDiagram"/>）</param>
/// <param name="Before">呼び出しの前の図の写し（<see cref="ToolSchemaProjection"/>）</param>
/// <param name="After">呼び出しの後の図の写し</param>
internal sealed record ToolObservation(
    ToolHostKind Host,
    string Result,
    bool Success,
    bool Wrote,
    IReadOnlyList<string> Before,
    IReadOnlyList<string> After
);

/// <summary>期待 1 つ（<paramref name="Host"/> が null なら両ホスト）</summary>
/// <param name="Description">何を見るか（失敗の文に出す）</param>
/// <param name="Host">このホストでだけ見る（ホストの値の分岐）</param>
/// <param name="Verify">合っていれば null・違えば理由</param>
internal sealed record ToolCheck(
    string Description,
    ToolHostKind? Host,
    Func<ToolObservation, string?> Verify
);

/// <summary>ツールの行を組み立てる（<see cref="ToolScenarioRows"/> の 1 行）</summary>
/// <param name="id">行の Id（「{主題}-{守っていること}-{形}」の英小文字とハイフン）</param>
/// <param name="description">何を確かめるか（元のテストの DisplayName）</param>
internal sealed class ToolRow(string id, string description)
{
    private readonly List<ToolArrangeStep> _arrange = [];
    private readonly List<ToolScenarioCall> _calls = [];
    private string _dialect = "sqlserver";

    /// <summary>図の方言（既定 sqlserver）</summary>
    public ToolRow Dialect(string dialect)
    {
        _dialect = dialect;
        return this;
    }

    /// <summary>前提の組み立て（ツールの呼び出しの列・どれも成功を表明する）</summary>
    public ToolRow Base(IEnumerable<(string Tool, object Args)> steps)
    {
        foreach (var (tool, args) in steps)
        {
            Arrange(tool, args);
        }

        return this;
    }

    /// <summary>前提のツールの呼び出し 1 つ（成功を表明する）</summary>
    public ToolRow Arrange(string tool, object args)
    {
        var element = JsonSerializer.SerializeToElement(args);
        _arrange.Add(
            new ToolArrangeStep(
                $"{tool} {element.GetRawText()}",
                session =>
                {
                    var (result, success) = session.Execute(tool, element);

                    if (!success)
                    {
                        throw new XunitException(
                            $"前提の {tool} {element.GetRawText()} が失敗した: {result}"
                        );
                    }
                }
            )
        );
        return this;
    }

    /// <summary>ツールでは作れない前提を直接書き込む（名前付きクエリ・制約名の消去・手で付けた外部キーの印など）</summary>
    public ToolRow Seed(string what, Action<ToolHostSession> seed)
    {
        _arrange.Add(new ToolArrangeStep(what, seed));
        return this;
    }

    /// <summary>確かめる呼び出し（期待は <paramref name="expect"/> で組む）</summary>
    public ToolRow Call(string tool, object args, Action<ToolExpect> expect)
    {
        var checks = new ToolExpect(null);
        expect(checks);

        if (checks.DeclaredSuccess is not { } expectSuccess)
        {
            throw new InvalidOperationException(
                $"行 {id} の {tool} に成否の期待（Ok か Fails）が無い"
            );
        }

        if (checks.CoreChecks.Count > 0 && !QueryToolScenarioCore.IsQueryTool(tool))
        {
            throw new InvalidOperationException(
                $"行 {id} の {tool} は名前付きクエリのツールでないので Core の期待を書けない"
            );
        }

        _calls.Add(
            new ToolScenarioCall(
                tool,
                JsonSerializer.SerializeToElement(args),
                expectSuccess,
                checks.Checks,
                checks.CoreChecks
            )
        );
        return this;
    }

    /// <summary>表の行にする</summary>
    public ScenarioRow<ToolScenario> Build()
    {
        if (_calls.Count == 0)
        {
            throw new InvalidOperationException($"行 {id} に確かめる呼び出しが無い");
        }

        return new ScenarioRow<ToolScenario>(
            id,
            null,
            description,
            new ToolScenario(_dialect, [.. _arrange], [.. _calls])
        );
    }
}

/// <summary>呼び出し 1 つへの期待を組む</summary>
/// <remarks>
/// 面は 3 つ：成否（<see cref="Ok"/> / <see cref="Fails"/>）・応答の文（<see cref="Result"/>）・図の写し（<see cref="Schema"/>）。
/// 「何も変えない」は <see cref="Unchanged"/>（写しが同じ＋図を書いていない）。ホストで値が違う面は
/// <see cref="ForHost"/>（理由が必須）で書き分ける。照合のパターンは <c>*</c> だけが任意の文字列。
/// </remarks>
internal sealed class ToolExpect
{
    private readonly ToolHostKind? _host;

    internal ToolExpect(ToolHostKind? host)
    {
        _host = host;
        Result = new ToolTextFacet(this);
        Schema = new ToolLinesFacet(this, "schema", observation => observation.After);
        Core = new QueryCoreFacet(this);
    }

    internal List<ToolCheck> Checks { get; } = [];

    internal List<QueryCoreCheck> CoreChecks { get; } = [];

    /// <summary>
    /// 名前付きクエリのツールの共有コアの結果（両ホストに共通・<see cref="ToolScenarioTests.Row_MatchesCore"/> が照合する）
    /// </summary>
    public QueryCoreFacet Core { get; }

    /// <summary>両ホストに共通の成否の宣言（無ければ null）</summary>
    internal bool? DeclaredSuccess { get; private set; }

    /// <summary>応答の文</summary>
    public ToolTextFacet Result { get; }

    /// <summary>呼び出しの後の図の写し（<see cref="ToolSchemaProjection"/>）</summary>
    public ToolLinesFacet Schema { get; }

    /// <summary>成功する（呼び出しごとに Ok か Fails のどちらかを両ホストに共通で書く）</summary>
    public ToolExpect Ok() =>
        Declare(true)
            .Add(
                "success",
                observation => observation.Success ? null : $"失敗した: {observation.Result}"
            );

    /// <summary>失敗する</summary>
    public ToolExpect Fails() =>
        Declare(false)
            .Add(
                "failure",
                observation => observation.Success ? $"成功した: {observation.Result}" : null
            );

    /// <summary>図を変えない（写しが呼び出しの前と同じで、図を書いていない）</summary>
    public ToolExpect Unchanged() =>
        Add(
            "unchanged",
            observation =>
            {
                if (observation.Wrote)
                {
                    return "図を書いた（GUI＝履歴の世代か未保存の状態が変わった・File＝ファイルのバイトか更新日時が変わった）";
                }

                return observation.Before.SequenceEqual(observation.After, StringComparer.Ordinal)
                    ? null
                    : "写しが変わった";
            }
        );

    /// <summary>図を書く（GUI＝履歴の世代が進む・File＝ファイルが変わる）</summary>
    public ToolExpect Wrote() =>
        Add("wrote", observation => observation.Wrote ? null : "図を書いていない");

    /// <summary>
    /// 名前付きクエリ（<paramref name="tableAndName"/>＝<c>表.名前</c>）が呼び出しの前後どちらにもあり、Id が同じ
    /// （同名の再定義が既存の Id を温存する）
    /// </summary>
    public ToolExpect QueryIdKept(string tableAndName) =>
        Add(
            $"query id kept {tableAndName}",
            observation =>
            {
                var before = QueryId(observation.Before, tableAndName);
                var after = QueryId(observation.After, tableAndName);

                if (before is null || after is null)
                {
                    return $"前後のどちらかにクエリが無い（前 {before ?? "なし"}・後 {after ?? "なし"}）";
                }

                return before == after ? null : $"Id が変わった（{before} → {after}）";
            }
        );

    /// <summary>写しの qdef の行から Id を読む（無ければ null）</summary>
    private static string? QueryId(IReadOnlyList<string> lines, string tableAndName)
    {
        var prefix = $"qdef {tableAndName} ";
        var line = lines.SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

        if (line is null)
        {
            return null;
        }

        var start = line.IndexOf(" id=", StringComparison.Ordinal) + 4;

        return line[start..line.IndexOf(' ', start)];
    }

    /// <summary>このホストでだけ見る期待（ホストの値の分岐。理由は必須）</summary>
    /// <param name="host">ホスト</param>
    /// <param name="reason">なぜホストで値が違うか</param>
    /// <param name="expect">そのホストでの期待</param>
    public ToolExpect ForHost(ToolHostKind host, string reason, Action<ToolExpect> expect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_host is not null)
        {
            throw new InvalidOperationException("ForHost は入れ子にしない");
        }

        var inner = new ToolExpect(host);
        expect(inner);

        if (inner.Checks.Count == 0)
        {
            throw new InvalidOperationException($"ForHost({host}) に期待が無い");
        }

        Checks.AddRange(inner.Checks);
        return this;
    }

    /// <summary>面で書けない期待（使うときは理由を行のコメントに書く）</summary>
    public ToolExpect Check(string description, Func<ToolObservation, string?> verify) =>
        Add(description, verify);

    private ToolExpect Declare(bool success)
    {
        if (_host is not null)
        {
            throw new InvalidOperationException(
                "成否は ForHost の中に書かない（ホストで成否が割れる行は止めて報告する）"
            );
        }

        if (DeclaredSuccess is not null)
        {
            throw new InvalidOperationException("成否は 1 回だけ書く");
        }

        DeclaredSuccess = success;
        return this;
    }

    internal ToolExpect Add(string description, Func<ToolObservation, string?> verify)
    {
        Checks.Add(new ToolCheck(description, _host, verify));
        return this;
    }

    internal ToolExpect AddCore(string description, Func<QueryToolOutcome, string?> verify)
    {
        if (_host is not null)
        {
            throw new InvalidOperationException(
                "Core の期待は ForHost の中に書かない（コアはホストに依らない）"
            );
        }

        CoreChecks.Add(new QueryCoreCheck(description, verify));
        return this;
    }
}

/// <summary>名前付きクエリのツールの共有コア（<see cref="QueryToolCore"/>）の結果の期待</summary>
internal sealed class QueryCoreFacet(ToolExpect owner)
{
    /// <summary>結果の状態</summary>
    public ToolExpect Status(QueryToolStatus status) =>
        owner.AddCore(
            $"core status == {status}",
            outcome => outcome.Status == status ? null : $"状態が {outcome.Status}"
        );

    /// <summary>set_query が既存を置き換えたか</summary>
    public ToolExpect WasUpdate(bool wasUpdate) =>
        owner.AddCore(
            $"core wasUpdate == {wasUpdate}",
            outcome => outcome.WasUpdate == wasUpdate ? null : $"WasUpdate が {outcome.WasUpdate}"
        );

    /// <summary>エラーの診断の種類が、この順でちょうどこれだけ（空なら無い）</summary>
    public ToolExpect Errors(params QueryToolDiagnosticCode[] codes) =>
        owner.AddCore(
            $"core errors [{string.Join(", ", codes)}]",
            outcome => SameCodes(outcome.Errors, codes)
        );

    /// <summary>警告の診断の種類が、この順でちょうどこれだけ（空なら無い）</summary>
    public ToolExpect Warnings(params QueryToolDiagnosticCode[] codes) =>
        owner.AddCore(
            $"core warnings [{string.Join(", ", codes)}]",
            outcome => SameCodes(outcome.Warnings, codes)
        );

    /// <summary>面で書けない期待（使うときは理由を行のコメントに書く）</summary>
    public ToolExpect Check(string description, Func<QueryToolOutcome, string?> verify) =>
        owner.AddCore(description, verify);

    private static string? SameCodes(
        IReadOnlyList<QueryToolDiagnostic> actual,
        QueryToolDiagnosticCode[] expected
    ) =>
        actual.Select(diagnostic => diagnostic.Code).SequenceEqual(expected)
            ? null
            : $"診断が [{string.Join(", ", actual.Select(diagnostic => diagnostic.Code))}]";
}

/// <summary>応答の文の期待</summary>
internal sealed class ToolTextFacet(ToolExpect owner)
{
    /// <summary>全文が一致する</summary>
    public ToolExpect Exact(string expected) =>
        owner.Add(
            $"result == \"{expected}\"",
            observation =>
                string.Equals(observation.Result, expected, StringComparison.Ordinal)
                    ? null
                    : "全文が違う"
        );

    /// <summary>全文がパターンに合う（<c>*</c> だけが任意の文字列）</summary>
    public ToolExpect Like(string pattern) =>
        owner.Add(
            $"result like \"{pattern}\"",
            observation =>
                ToolPattern.IsMatch(pattern, observation.Result) ? null : "パターンに合わない"
        );

    /// <summary>断片をすべて含む（序数で比べる）</summary>
    public ToolExpect Contains(params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            owner.Add(
                $"result contains \"{fragment}\"",
                observation =>
                    observation.Result.Contains(fragment, StringComparison.Ordinal)
                        ? null
                        : "含まない"
            );
        }

        return owner;
    }

    /// <summary>断片をどれも含まない</summary>
    public ToolExpect Absent(params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            owner.Add(
                $"result absent \"{fragment}\"",
                observation =>
                    observation.Result.Contains(fragment, StringComparison.Ordinal) ? "含む" : null
            );
        }

        return owner;
    }

    /// <summary>空でない</summary>
    public ToolExpect NotEmpty() =>
        owner.Add(
            "result not empty",
            observation => string.IsNullOrWhiteSpace(observation.Result) ? "空" : null
        );
}

/// <summary>写し（1 要素 1 行）の期待</summary>
internal sealed class ToolLinesFacet(
    ToolExpect owner,
    string name,
    Func<ToolObservation, IReadOnlyList<string>> lines
)
{
    /// <summary>すべての行が、この順でパターンに合う（行の数も同じ）</summary>
    public ToolExpect Exact(params string[] patterns) =>
        owner.Add(
            $"{name} exact [{string.Join(" | ", patterns)}]",
            observation =>
            {
                var actual = lines(observation);

                if (actual.Count != patterns.Length)
                {
                    return $"行の数が {actual.Count}（期待 {patterns.Length}）";
                }

                for (var i = 0; i < patterns.Length; i++)
                {
                    if (!ToolPattern.IsMatch(patterns[i], actual[i]))
                    {
                        return $"{i + 1} 行目 \"{actual[i]}\" がパターン \"{patterns[i]}\" に合わない";
                    }
                }

                return null;
            }
        );

    /// <summary>種類（先頭の語）が同じ行だけを取り出し、この順でパターンに合う（行の数も同じ）</summary>
    /// <param name="kind">行の先頭の語（<c>table</c>・<c>col</c>・<c>uq</c>・<c>rel</c> など）</param>
    /// <param name="patterns">パターン（並びのとおり）</param>
    public ToolExpect Kind(string kind, params string[] patterns) =>
        owner.Add(
            $"{name} {kind} [{string.Join(" | ", patterns)}]",
            observation =>
            {
                var actual = lines(observation)
                    .Where(line => line.StartsWith(kind + " ", StringComparison.Ordinal))
                    .ToList();

                if (actual.Count != patterns.Length)
                {
                    return $"{kind} の行が {actual.Count}（期待 {patterns.Length}）";
                }

                for (var i = 0; i < patterns.Length; i++)
                {
                    if (!ToolPattern.IsMatch(patterns[i], actual[i]))
                    {
                        return $"{kind} の {i + 1} 行目 \"{actual[i]}\" がパターン \"{patterns[i]}\" に合わない";
                    }
                }

                return null;
            }
        );

    /// <summary>パターンごとに、合う行が 1 つ以上ある</summary>
    public ToolExpect Contains(params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            owner.Add(
                $"{name} contains \"{pattern}\"",
                observation =>
                    lines(observation).Any(line => ToolPattern.IsMatch(pattern, line))
                        ? null
                        : "合う行が無い"
            );
        }

        return owner;
    }

    /// <summary>パターンごとに、合う行がちょうど 1 つある</summary>
    public ToolExpect Single(params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            owner.Add(
                $"{name} single \"{pattern}\"",
                observation =>
                {
                    var count = lines(observation)
                        .Count(line => ToolPattern.IsMatch(pattern, line));
                    return count == 1 ? null : $"合う行が {count}";
                }
            );
        }

        return owner;
    }

    /// <summary>パターンごとに、合う行が無い</summary>
    public ToolExpect Absent(params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            owner.Add(
                $"{name} absent \"{pattern}\"",
                observation =>
                {
                    var count = lines(observation)
                        .Count(line => ToolPattern.IsMatch(pattern, line));
                    return count == 0 ? null : $"合う行が {count}";
                }
            );
        }

        return owner;
    }

    /// <summary>パターンに合う行がちょうど <paramref name="count"/> 個ある</summary>
    public ToolExpect Count(string pattern, int count) =>
        owner.Add(
            $"{name} count \"{pattern}\" == {count}",
            observation =>
            {
                var actual = lines(observation).Count(line => ToolPattern.IsMatch(pattern, line));
                return actual == count ? null : $"合う行が {actual}";
            }
        );
}

/// <summary><c>*</c> だけが任意の文字列（空を含む）を表すパターン（ほかの文字はそのまま序数で比べる）</summary>
internal static class ToolPattern
{
    /// <summary>全体が合うか</summary>
    public static bool IsMatch(string pattern, string text)
    {
        var parts = pattern.Split('*');

        if (parts.Length == 1)
        {
            return string.Equals(pattern, text, StringComparison.Ordinal);
        }

        if (
            !text.StartsWith(parts[0], StringComparison.Ordinal)
            || !text.EndsWith(parts[^1], StringComparison.Ordinal)
            || text.Length < parts[0].Length + parts[^1].Length
        )
        {
            return false;
        }

        var position = parts[0].Length;
        var end = text.Length - parts[^1].Length;

        for (var i = 1; i < parts.Length - 1; i++)
        {
            var found = text.IndexOf(parts[i], position, end - position, StringComparison.Ordinal);

            if (found < 0)
            {
                return false;
            }

            position = found + parts[i].Length;
        }

        return true;
    }
}

/// <summary>名前付きクエリのツールの共有コアを、行の呼び出しの直前の図へ当てる</summary>
internal static class QueryToolScenarioCore
{
    /// <summary>名前付きクエリのツールか</summary>
    public static bool IsQueryTool(string tool) =>
        tool
            is QueryToolCore.SetQueryToolName
                or QueryToolCore.ListQueriesToolName
                or QueryToolCore.RemoveQueryToolName;

    /// <summary>コアを呼ぶ（図は呼び出しの直前の写しで、コアが変えてもホストの図には残らない）</summary>
    public static QueryToolOutcome Invoke(string tool, ErDiagram diagram, JsonElement args) =>
        tool switch
        {
            QueryToolCore.SetQueryToolName => QueryToolCore.SetQuery(diagram, args),
            QueryToolCore.ListQueriesToolName => QueryToolCore.ListQueries(diagram),
            QueryToolCore.RemoveQueryToolName => QueryToolCore.RemoveQuery(diagram, args),
            _ => throw new ArgumentOutOfRangeException(
                nameof(tool),
                tool,
                "名前付きクエリのツールでない"
            ),
        };

    /// <summary>
    /// 行のうち Core の期待を持つ呼び出しを、コアへ直接当てて照合する（ホストは File で前提を作り、呼び出しの直前の図を読む）
    /// </summary>
    /// <remarks>
    /// 共通の表明：コアが失敗を返したら、渡した図を変えていない（全か無か＝ホストは成功のときだけ書き戻す）。
    /// 照合した後に同じ呼び出しを File ホストで実行し、次の呼び出しの前提を進める。
    /// </remarks>
    public static void Run(ScenarioRow<ToolScenario> row)
    {
        var scenario = row.Input;
        using var session = TwoHostToolHarness.OpenAligned(ToolHostKind.File, scenario.Dialect);

        foreach (var step in scenario.Arrange)
        {
            step.Apply(session);
        }

        var problems = new List<string>();

        for (var i = 0; i < scenario.Calls.Count; i++)
        {
            var call = scenario.Calls[i];

            if (call.CoreChecks.Count > 0)
            {
                var diagram = session.Schema;
                var before = ToolSchemaProjection.Lines(diagram);
                var outcome = Invoke(call.Tool, diagram, call.Args);

                foreach (var check in call.CoreChecks)
                {
                    if (check.Verify(outcome) is { } problem)
                    {
                        problems.Add(
                            $"呼び出し {i + 1}（{call.Tool}）: {check.Description}: {problem}"
                        );
                    }
                }

                if (
                    !outcome.Success
                    && !before.SequenceEqual(
                        ToolSchemaProjection.Lines(diagram),
                        StringComparer.Ordinal
                    )
                )
                {
                    problems.Add($"呼び出し {i + 1}（{call.Tool}）: 失敗したコアが図を変えた");
                }
            }

            session.Execute(call.Tool, call.Args);
        }

        if (problems.Count > 0)
        {
            throw new XunitException(
                $"行 {row.Id}（{row.Description}）・共有コア・方言 {scenario.Dialect}"
                    + Environment.NewLine
                    + "  "
                    + string.Join(Environment.NewLine + "  ", problems)
            );
        }
    }
}

/// <summary>ツールの行を 1 つのホストで流す</summary>
internal static class ToolScenarioRunner
{
    /// <summary>行を流し、合わない期待をすべて並べて赤にする</summary>
    public static void Run(ScenarioRow<ToolScenario> row, ToolHostKind host)
    {
        var scenario = row.Input;
        using var session = TwoHostToolHarness.OpenAligned(host, scenario.Dialect);

        foreach (var step in scenario.Arrange)
        {
            step.Apply(session);
        }

        var problems = new List<string>();
        var observations = new List<ToolObservation>();

        for (var i = 0; i < scenario.Calls.Count; i++)
        {
            var call = scenario.Calls[i];
            var before = ToolSchemaProjection.Lines(session.Schema);
            var (result, success) = session.Execute(call.Tool, call.Args);
            var observation = new ToolObservation(
                host,
                result,
                success,
                session.WroteDiagram,
                before,
                ToolSchemaProjection.Lines(session.Schema)
            );
            observations.Add(observation);

            foreach (
                var check in call.Checks.Where(check => check.Host is null || check.Host == host)
            )
            {
                if (check.Verify(observation) is { } problem)
                {
                    problems.Add(
                        $"呼び出し {i + 1}（{call.Tool}）: {check.Description}: {problem}"
                    );
                }
            }
        }

        if (problems.Count == 0)
        {
            return;
        }

        var message = new StringBuilder();
        message.AppendLine(
            $"行 {row.Id}（{row.Description}）・ホスト {host}・方言 {scenario.Dialect}"
        );

        foreach (var problem in problems)
        {
            message.AppendLine("  " + problem);
        }

        for (var i = 0; i < observations.Count; i++)
        {
            var observation = observations[i];
            message.AppendLine(
                $"--- 呼び出し {i + 1}（{scenario.Calls[i].Tool} {scenario.Calls[i].Args.GetRawText()}）: success={observation.Success} wrote={observation.Wrote}"
            );
            message.AppendLine("  result: " + observation.Result.Replace("\n", "\n          "));
            message.AppendLine("  schema:");

            foreach (var line in observation.After)
            {
                message.AppendLine("    " + line);
            }
        }

        throw new XunitException(message.ToString());
    }
}
