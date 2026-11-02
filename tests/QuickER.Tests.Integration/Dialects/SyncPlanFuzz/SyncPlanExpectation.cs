using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>期待を照合する面（差分と計画を、決まった書式の行の並びへ写したもの＝<see cref="SyncPlanProjection"/>）</summary>
internal static class ExpectationFacet
{
    /// <summary>差分の項目（選び方を当てる前の既定の選択のまま・差分の並び）</summary>
    public const string Diff = "diff";

    /// <summary>計画のセクション（1 行＝1 セクション・実行順）</summary>
    public const string Sections = "sections";

    /// <summary>計画の項目（1 行＝1 項目・実行順・行の頭にセクション）</summary>
    public const string Items = "items";

    /// <summary>計画の警告（並びのまま）</summary>
    public const string Warnings = "warnings";

    /// <summary>計画から外した選んだ項目（並びのまま）</summary>
    public const string Excluded = "excluded";

    /// <summary>テーブル再構築（1 表につき複数行）</summary>
    public const string Rebuilds = "rebuilds";

    /// <summary>ネイティブの列順の移動（1 移動につき 1 行）</summary>
    public const string Reorders = "reorders";

    /// <summary>
    /// 再構築・列順の移動へ畳んだ元の項目（1 項目につき 1 行・<c>{表} rebuild|reorder &gt; {項目の行}</c>）。<see cref="Rebuilds"/> と
    /// <see cref="Reorders"/> の <c>source</c> 行は鍵だけなので、畳んだ項目の詳細（列ペア・参照アクション・構成列）はこの面で見る
    /// </summary>
    public const string Sources = "sources";

    /// <summary>差分か計画の組み立てが投げた例外（<c>型名: メッセージ</c>）</summary>
    public const string Exception = "exception";

    /// <summary>計画を作る面（ほかは差分の面か例外）</summary>
    public static IReadOnlyList<string> PlanFacets { get; } =
    [Sections, Items, Warnings, Excluded, Rebuilds, Reorders, Sources];

    /// <summary>実 DB の段：同期の実行の結果（<c>committed</c> か <c>failed: {エラーの 1 行目}</c>）</summary>
    public const string Outcome = "outcome";

    /// <summary>
    /// 実 DB の段：同期の実行の失敗のメッセージの全文（1 行＝メッセージの 1 行・行末の空白を除く・空行を除く。コミットしたら空）。
    /// <see cref="Outcome"/> は 1 行目だけなので、2 行目以降に乗る説明（Oracle の部分適用の説明など）はこの面で見る
    /// </summary>
    public const string Error = "error";

    /// <summary>実 DB の段：流した同期スクリプト（1 行＝スクリプトの 1 行・行末の空白を除く）</summary>
    public const string Script = "script";

    /// <summary>実 DB の段：同期の前に取り込んだ図（書式は <see cref="SyncPlanRealProjection.Schema"/>）</summary>
    public const string Before = "before";

    /// <summary>実 DB の段：同期の後（失敗なら失敗の後）に取り込み直した図（書式は <see cref="Before"/> と同じ）</summary>
    public const string After = "after";

    /// <summary>実 DB の段：同期の後に取り込み直した図と目標の図の差分（書式は <see cref="Diff"/> と同じ・コミットしたときだけ）</summary>
    public const string AfterDiff = "afterdiff";

    /// <summary>実 DB の段：同期の後のカタログの探針の面の接頭辞（<c>probe:{名前}</c>）</summary>
    public const string ProbePrefix = "probe:";

    /// <summary>実 DB の段：同期の前（取り込んだ直後）のカタログの探針の面の接頭辞（<c>probe-before:{名前}</c>）</summary>
    public const string ProbeBeforePrefix = "probe-before:";

    /// <summary>実 DB の段にだけある面か</summary>
    public static bool IsRealOnly(string facet) =>
        facet is Outcome or Error or Script or Before or After or AfterDiff
        || facet.StartsWith(ProbePrefix, StringComparison.Ordinal)
        || facet.StartsWith(ProbeBeforePrefix, StringComparison.Ordinal);
}

/// <summary>期待の強さ</summary>
internal enum ExpectationMatch
{
    /// <summary>行の数が同じで、i 番目の行が i 番目のパターンに合う（順序まで見る）</summary>
    Exact,

    /// <summary>行の数が同じで、行とパターンが 1 対 1 に組める（順序は見ない）</summary>
    Unordered,

    /// <summary>どのパターンにも合う行が 1 つ以上ある（包含）</summary>
    Contains,

    /// <summary>どのパターンにも合う行が無い</summary>
    Absent,

    /// <summary>パターンに合う行がちょうど <see cref="SyncPlanExpectation.Count"/> 個</summary>
    Count,

    /// <summary>
    /// 面の行が、別の面（<see cref="SyncPlanExpectation.Patterns"/> の先頭）の行と同じ（2 つ目以降のパターンに合う行は両方から除いて比べる。
    /// 実 DB の段の「DB が変わらない」＝同期の前と後の比較）
    /// </summary>
    SameAs,
}

/// <summary>期待 1 つ（面・強さ・パターン。能力を指定すればその能力で流したときだけ照合する）</summary>
/// <param name="Facet">照合する面（<see cref="ExpectationFacet"/>）</param>
/// <param name="Match">強さ</param>
/// <param name="Patterns">行のパターン（<c>*</c> は任意の文字列。ほかの文字はそのまま）</param>
/// <param name="Count">強さが <see cref="ExpectationMatch.Count"/> のときの個数</param>
/// <param name="Capability">照合する能力の鍵（null なら行のすべての能力）</param>
internal sealed record SyncPlanExpectation(
    string Facet,
    ExpectationMatch Match,
    IReadOnlyList<string> Patterns,
    int Count = 0,
    string? Capability = null
)
{
    /// <summary>行の並びを照合し、食い違いを問題の文にする（合えば null）</summary>
    /// <param name="lines">面の行</param>
    public string? Check(IReadOnlyList<string> lines)
    {
        if (Match == ExpectationMatch.SameAs)
        {
            throw new InvalidOperationException("SameAs は 2 つの面を比べる（CheckSame を使う）");
        }

        var globs = Patterns.Select(Glob.Of).ToList();
        string? problem = Match switch
        {
            ExpectationMatch.Exact => CheckExact(lines, globs),
            ExpectationMatch.Unordered => CheckUnordered(lines, globs),
            ExpectationMatch.Contains => CheckContains(lines, globs),
            ExpectationMatch.Absent => CheckAbsent(lines, globs),
            ExpectationMatch.Count => CheckCount(lines, globs.Single(), Count),
            _ => throw new ArgumentOutOfRangeException(nameof(Match), Match, null),
        };

        return problem is null
            ? null
            : $"{Facet} {Describe()}: {problem}\n      actual:\n        "
                + (lines.Count == 0 ? "(none)" : string.Join("\n        ", lines));
    }

    /// <summary>
    /// <see cref="ExpectationMatch.SameAs"/> の期待を照合する（除くパターンに合う行を両方から除き、残りが同じ並びか）
    /// </summary>
    /// <param name="lines">この面の行</param>
    /// <param name="other">比べる面（<see cref="Patterns"/> の先頭）の行</param>
    public string? CheckSame(IReadOnlyList<string> lines, IReadOnlyList<string> other)
    {
        var excluded = Patterns.Skip(1).Select(Glob.Of).ToList();
        var mine = lines.Where(l => !excluded.Any(g => g.IsMatch(l))).ToList();
        var theirs = other.Where(l => !excluded.Any(g => g.IsMatch(l))).ToList();

        if (mine.SequenceEqual(theirs, StringComparer.Ordinal))
        {
            return null;
        }

        return $"{Facet} {Describe()}: the lines differ\n      {Patterns[0]}:\n        "
            + (theirs.Count == 0 ? "(none)" : string.Join("\n        ", theirs))
            + $"\n      {Facet}:\n        "
            + (mine.Count == 0 ? "(none)" : string.Join("\n        ", mine));
    }

    /// <summary>期待を 1 行で表す（報告用）</summary>
    public string Describe() =>
        Match switch
        {
            ExpectationMatch.Count => $"Count({Count}) of [{Patterns[0]}]",
            ExpectationMatch.SameAs => $"SameAs {Patterns[0]}"
                + (Patterns.Count > 1 ? $" except [{string.Join(" | ", Patterns.Skip(1))}]" : ""),
            _ => $"{Match} [{string.Join(" | ", Patterns)}]",
        };

    private static string? CheckExact(IReadOnlyList<string> lines, List<Glob> globs)
    {
        if (lines.Count != globs.Count)
        {
            return $"expected {globs.Count} lines, got {lines.Count}";
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (!globs[i].IsMatch(lines[i]))
            {
                return $"line {i + 1} does not match [{globs[i].Pattern}]";
            }
        }

        return null;
    }

    private static string? CheckUnordered(IReadOnlyList<string> lines, List<Glob> globs)
    {
        if (lines.Count != globs.Count)
        {
            return $"expected {globs.Count} lines, got {lines.Count}";
        }

        var used = new bool[lines.Count];
        return Assign(0) ? null : "the lines cannot be paired with the patterns";

        // パターンを並びの順に、まだ使っていない行へ当てる（合わなければ戻ってほかの行を試す）
        bool Assign(int index)
        {
            if (index == globs.Count)
            {
                return true;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                if (used[i] || !globs[index].IsMatch(lines[i]))
                {
                    continue;
                }

                used[i] = true;

                if (Assign(index + 1))
                {
                    return true;
                }

                used[i] = false;
            }

            return false;
        }
    }

    private static string? CheckContains(IReadOnlyList<string> lines, List<Glob> globs)
    {
        var missing = globs.Where(g => !lines.Any(g.IsMatch)).Select(g => g.Pattern).ToList();
        return missing.Count == 0 ? null : $"no line matches [{string.Join(" | ", missing)}]";
    }

    private static string? CheckAbsent(IReadOnlyList<string> lines, List<Glob> globs)
    {
        var present = lines.Where(line => globs.Any(g => g.IsMatch(line))).ToList();
        return present.Count == 0 ? null : $"unexpected lines [{string.Join(" | ", present)}]";
    }

    private static string? CheckCount(IReadOnlyList<string> lines, Glob glob, int count)
    {
        var actual = lines.Count(glob.IsMatch);
        return actual == count ? null : $"expected {count} matching lines, got {actual}";
    }

    /// <summary><c>*</c> だけを特別に扱うパターン</summary>
    private sealed class Glob
    {
        private readonly Regex _regex;

        private Glob(string pattern)
        {
            Pattern = pattern;
            _regex = new Regex(
                "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$",
                RegexOptions.Singleline | RegexOptions.CultureInvariant
            );
        }

        public string Pattern { get; }

        public static Glob Of(string pattern) => new(pattern);

        public bool IsMatch(string line) => _regex.IsMatch(line);
    }
}

/// <summary>行の期待を組み立てる</summary>
internal sealed class ExpectationBuilder
{
    private readonly List<SyncPlanExpectation> _expectations = [];
    private string? _capability;

    /// <summary>組み立てた期待</summary>
    public IReadOnlyList<SyncPlanExpectation> Expectations => _expectations;

    /// <summary>行のすべての能力で計画が同じ（全部の面の行が一致する）ことを期待する</summary>
    public bool SamePlanAcrossCapabilities { get; private set; }

    /// <summary>差分の項目</summary>
    public FacetExpectations Diff => new(this, ExpectationFacet.Diff);

    /// <summary>計画のセクション</summary>
    public FacetExpectations Sections => new(this, ExpectationFacet.Sections);

    /// <summary>計画の項目</summary>
    public FacetExpectations Items => new(this, ExpectationFacet.Items);

    /// <summary>計画の警告</summary>
    public FacetExpectations Warnings => new(this, ExpectationFacet.Warnings);

    /// <summary>計画から外した選んだ項目</summary>
    public FacetExpectations Excluded => new(this, ExpectationFacet.Excluded);

    /// <summary>テーブル再構築</summary>
    public FacetExpectations Rebuilds => new(this, ExpectationFacet.Rebuilds);

    /// <summary>ネイティブの列順の移動</summary>
    public FacetExpectations Reorders => new(this, ExpectationFacet.Reorders);

    /// <summary>再構築・列順の移動へ畳んだ元の項目</summary>
    public FacetExpectations Sources => new(this, ExpectationFacet.Sources);

    /// <summary>例外</summary>
    public FacetExpectations Exception => new(this, ExpectationFacet.Exception);

    /// <summary>実 DB の段：同期の実行の結果</summary>
    public FacetExpectations Outcome => new(this, ExpectationFacet.Outcome);

    /// <summary>実 DB の段：同期の実行の失敗のメッセージの全文（1 行＝メッセージの 1 行）</summary>
    public FacetExpectations Error => new(this, ExpectationFacet.Error);

    /// <summary>実 DB の段：流した同期スクリプト</summary>
    public FacetExpectations Script => new(this, ExpectationFacet.Script);

    /// <summary>実 DB の段：同期の前に取り込んだ図</summary>
    public FacetExpectations Before => new(this, ExpectationFacet.Before);

    /// <summary>実 DB の段：同期の後に取り込み直した図</summary>
    public FacetExpectations After => new(this, ExpectationFacet.After);

    /// <summary>実 DB の段：同期の後に取り込み直した図と目標の図の差分</summary>
    public FacetExpectations AfterDiff => new(this, ExpectationFacet.AfterDiff);

    /// <summary>実 DB の段：同期の後のカタログの探針（行の <c>Probe</c> で宣言した名前）</summary>
    public FacetExpectations Probe(string name) => new(this, ExpectationFacet.ProbePrefix + name);

    /// <summary>実 DB の段：同期の前（取り込んだ直後）のカタログの探針</summary>
    public FacetExpectations ProbeBefore(string name) =>
        new(this, ExpectationFacet.ProbeBeforePrefix + name);

    /// <summary>
    /// 実 DB の段：DB が変わらない（同期の後に取り込み直した図が、同期の前に取り込んだ図と同じ。<paramref name="except"/> に合う行は比べない）
    /// </summary>
    public ExpectationBuilder Unchanged(params string[] except) =>
        Add(ExpectationFacet.After, ExpectationMatch.SameAs, [ExpectationFacet.Before, .. except]);

    /// <summary>
    /// 実 DB の段：カタログの探針の行が同期の前と後で同じ（<paramref name="except"/> に合う行は比べない）
    /// </summary>
    public ExpectationBuilder ProbeUnchanged(string name, params string[] except) =>
        Add(
            ExpectationFacet.ProbePrefix + name,
            ExpectationMatch.SameAs,
            [ExpectationFacet.ProbeBeforePrefix + name, .. except]
        );

    /// <summary>能力 1 つで流したときだけ照合する期待を書く</summary>
    /// <param name="capability">能力の鍵</param>
    /// <param name="body">期待</param>
    public ExpectationBuilder For(string capability, Action<ExpectationBuilder> body)
    {
        var outer = _capability;
        _capability = capability;
        body(this);
        _capability = outer;
        return this;
    }

    /// <summary>行のすべての能力で計画が同じことを期待する</summary>
    public ExpectationBuilder SamePlan()
    {
        SamePlanAcrossCapabilities = true;
        return this;
    }

    internal ExpectationBuilder Add(
        string facet,
        ExpectationMatch match,
        IReadOnlyList<string> patterns,
        int count = 0
    )
    {
        _expectations.Add(new SyncPlanExpectation(facet, match, patterns, count, _capability));
        return this;
    }
}

/// <summary>面 1 つの期待を書く</summary>
internal readonly struct FacetExpectations(ExpectationBuilder owner, string facet)
{
    /// <summary>行がパターンと同じ数・同じ順で合う</summary>
    public ExpectationBuilder Exact(params string[] patterns) =>
        owner.Add(facet, ExpectationMatch.Exact, patterns);

    /// <summary>行が 1 つも無い</summary>
    public ExpectationBuilder Empty() => owner.Add(facet, ExpectationMatch.Exact, []);

    /// <summary>行がパターンと同じ数で、順序を問わず 1 対 1 に合う</summary>
    public ExpectationBuilder Unordered(params string[] patterns) =>
        owner.Add(facet, ExpectationMatch.Unordered, patterns);

    /// <summary>どのパターンにも合う行がある</summary>
    public ExpectationBuilder Contains(params string[] patterns) =>
        owner.Add(facet, ExpectationMatch.Contains, patterns);

    /// <summary>どのパターンにも合う行が無い</summary>
    public ExpectationBuilder Absent(params string[] patterns) =>
        owner.Add(facet, ExpectationMatch.Absent, patterns);

    /// <summary>パターンに合う行がちょうど 1 つ（パターンごとに）</summary>
    public ExpectationBuilder Single(params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            owner.Add(facet, ExpectationMatch.Count, [pattern], 1);
        }

        return owner;
    }

    /// <summary>パターンに合う行がちょうど <paramref name="count"/> 個</summary>
    public ExpectationBuilder Count(string pattern, int count) =>
        owner.Add(facet, ExpectationMatch.Count, [pattern], count);
}
