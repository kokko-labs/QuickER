using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// エンジンが次元表の宣言と食い違う割り当てを観測した（宣言していない水準・足りない因子・起こり得ない組み合わせ・固定した水準との食い違い）
/// </summary>
/// <remarks>
/// 宣言の誤りは、既知の失敗の照合に混ぜず、宣言の誤りとしてテストを赤にする。
/// </remarks>
public sealed class CoverageDeclarationException : Exception
{
    /// <summary>例外を作る</summary>
    public CoverageDeclarationException() { }

    /// <summary>例外を作る</summary>
    /// <param name="message">メッセージ</param>
    public CoverageDeclarationException(string message)
        : base(message) { }

    /// <summary>例外を作る</summary>
    /// <param name="message">メッセージ</param>
    /// <param name="innerException">元の例外</param>
    public CoverageDeclarationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// エンジンが流したケースの割り当てを数え、次元表の網羅すべき組のうち何が出たかをまとめる
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Observe"/> はスレッドセーフ（並列に流すエンジンが各ケースから呼ぶ）。
/// </para>
/// <para>
/// 割り当てが宣言と食い違えば <see cref="CoverageDeclarationException"/> を投げ、最初の 1 件を覚えておく。
/// <see cref="Report"/> はそれを投げ直す。エンジンが種ごとの例外を種の失敗として受け止める作りでも、宣言の誤りは
/// 既知の失敗の照合に混ざらず、宣言の誤りとして必ずテストを赤にする。
/// </para>
/// </remarks>
public sealed class CoverageCounter
{
    private readonly object _lock = new();
    private readonly DimensionTable _table;
    private readonly Dictionary<string, string> _fixedLevels;
    private readonly IReadOnlyList<CoverageTuple> _requiredPairs;
    private readonly IReadOnlyList<CoverageTuple> _requiredTriples;
    private readonly IReadOnlyList<CoverageTuple> _required;
    private readonly Dictionary<CoverageTuple, int> _tupleCounts = new();
    private readonly Dictionary<(string Factor, string Level), int> _levelCounts = new();
    private int _cases;
    private CoverageDeclarationException? _declarationError;

    /// <summary>数え始める</summary>
    /// <param name="table">次元表</param>
    /// <param name="fixedLevels">この実行で 1 つの水準に固定する因子（<see cref="DimensionTable.RequiredPairs"/> と同じ）</param>
    public CoverageCounter(
        DimensionTable table,
        IReadOnlyDictionary<string, string>? fixedLevels = null
    )
    {
        ArgumentNullException.ThrowIfNull(table);
        _table = table;
        _fixedLevels = table.NormalizeFixedLevels(fixedLevels);
        _requiredPairs = table.RequiredPairs(_fixedLevels);
        _requiredTriples = table.RequiredTriples(_fixedLevels);

        _required = _requiredPairs.Concat(_requiredTriples).Distinct().ToList();

        foreach (var tuple in _required)
        {
            _tupleCounts[tuple] = 0;
        }

        foreach (var factor in table.Factors)
        {
            foreach (var level in factor.Levels)
            {
                _levelCounts[(factor.Name, level.Name)] = 0;
            }
        }
    }

    /// <summary>観測したケースの数</summary>
    public int Cases
    {
        get
        {
            lock (_lock)
            {
                return _cases;
            }
        }
    }

    /// <summary>ケース 1 件の割り当てを数える</summary>
    /// <param name="assignment">表のすべての因子に宣言した水準を割り当てたもの</param>
    /// <exception cref="CoverageDeclarationException">割り当てが宣言と食い違う（最初の 1 件は <see cref="Report"/> も投げ直す）</exception>
    public void Observe(IReadOnlyDictionary<string, string> assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        var problems = Check(assignment);

        if (problems.Count > 0)
        {
            var error = new CoverageDeclarationException(
                $"{_table.EngineId}: 観測した割り当てが次元表の宣言と食い違う（{Describe(assignment)}）:\n  "
                    + string.Join("\n  ", problems)
            );

            lock (_lock)
            {
                _declarationError ??= error;
            }

            throw error;
        }

        var contained = _required.Where(tuple => tuple.IsContainedIn(assignment)).ToList();

        lock (_lock)
        {
            _cases++;

            foreach (var factor in _table.Factors)
            {
                _levelCounts[(factor.Name, assignment[factor.Name])]++;
            }

            foreach (var tuple in contained)
            {
                _tupleCounts[tuple]++;
            }
        }
    }

    /// <summary>数えた結果をまとめる</summary>
    /// <exception cref="CoverageDeclarationException"><see cref="Observe"/> が宣言の誤りを見た（呼び出し側が受け止めていても投げ直す）</exception>
    public CoverageReport Report()
    {
        lock (_lock)
        {
            if (_declarationError is { } error)
            {
                throw new CoverageDeclarationException(
                    $"{error.Message}\n（Observe で起きた宣言の誤り。呼び出し側が受け止めていても宣言の誤りとして赤にする）",
                    error
                );
            }

            var pairs = _requiredPairs.ToDictionary(tuple => tuple, tuple => _tupleCounts[tuple]);
            var triples = _requiredTriples.ToDictionary(
                tuple => tuple,
                tuple => _tupleCounts[tuple]
            );
            var missing = _required.Where(tuple => _tupleCounts[tuple] == 0).ToList();
            var levels = new Dictionary<(string Factor, string Level), int>(_levelCounts);
            return new CoverageReport(
                _table.EngineId,
                _table.Factors,
                _cases,
                pairs,
                triples,
                missing,
                levels
            );
        }
    }

    private List<string> Check(IReadOnlyDictionary<string, string> assignment)
    {
        var problems = new List<string>();

        foreach (var factor in _table.Factors)
        {
            if (!assignment.TryGetValue(factor.Name, out var level))
            {
                problems.Add($"因子 {factor.Name} が割り当てに無い");
                continue;
            }

            if (level is null || !DimensionTable.HasLevel(factor, level))
            {
                problems.Add($"水準 {factor.Name}={level} が宣言されていない");
                continue;
            }

            if (
                _fixedLevels.TryGetValue(factor.Name, out var fixedLevel)
                && !string.Equals(fixedLevel, level, StringComparison.Ordinal)
            )
            {
                problems.Add(
                    $"因子 {factor.Name} は {fixedLevel} に固定したのに {level} を観測した"
                );
            }
        }

        foreach (var key in assignment.Keys.Where(key => _table.IndexOf(key) < 0))
        {
            problems.Add($"因子 {key} が表に無い");
        }

        if (problems.Count == 0 && !_table.IsValid(assignment))
        {
            problems.Add("isValid が起こり得ない組み合わせとした");
        }

        return problems;
    }

    private static string Describe(IReadOnlyDictionary<string, string> assignment) =>
        string.Join(CoverageTuple.Separator, assignment.Select(pair => $"{pair.Key}={pair.Value}"));
}

/// <summary>網羅しない組の一覧の 1 項目が、観測と照らしてどれだけ使われたか</summary>
/// <param name="Entry">網羅しない組の項目</param>
/// <param name="Matched">この項目に当たった、網羅しなかった組の数</param>
/// <param name="RequiredInFactorGroup">項目と同じ因子の組を持つ、網羅すべき組の数</param>
/// <param name="CoversWholeFactorGroup">その因子の組が 1 つも観測されていないか（網羅すべき組が 1 つ以上あり、すべてがこの項目に当たった）</param>
public sealed record CoverageGapUsage(
    CoverageGapEntry Entry,
    int Matched,
    int RequiredInFactorGroup,
    bool CoversWholeFactorGroup
);

/// <summary>網羅の数え上げの結果</summary>
/// <param name="EngineId">エンジンの ID</param>
/// <param name="Factors">因子（宣言の順。要約の並びに使う）</param>
/// <param name="Cases">観測したケースの数</param>
/// <param name="PairCounts">網羅すべき 2 因子の組ごとの観測の数（0 を含む・網羅すべき順）</param>
/// <param name="TripleCounts">網羅すべき 3 因子の組ごとの観測の数（0 を含む・網羅すべき順）</param>
/// <param name="Missing">観測しなかった網羅すべき組（2 因子の組→3 因子の組の順）</param>
/// <param name="LevelCounts">因子と水準ごとの観測の数（0 を含む）</param>
public sealed record CoverageReport(
    string EngineId,
    IReadOnlyList<Factor> Factors,
    int Cases,
    IReadOnlyDictionary<CoverageTuple, int> PairCounts,
    IReadOnlyDictionary<CoverageTuple, int> TripleCounts,
    IReadOnlyList<CoverageTuple> Missing,
    IReadOnlyDictionary<(string Factor, string Level), int> LevelCounts
)
{
    /// <summary>網羅すべき組（2 因子の組→3 因子の組の順・重複なし）</summary>
    public IReadOnlyList<CoverageTuple> Required =>
        PairCounts.Keys.Concat(TripleCounts.Keys).Distinct().ToList();

    /// <summary>因子と水準ごとの数と、網羅すべき組の網羅の具合をテストの出力へ書く</summary>
    /// <param name="output">出力先（null なら現在のテストの出力）</param>
    public void WriteSummary(ITestOutputHelper? output = null)
    {
        output ??= TestContext.Current.TestOutputHelper;

        if (output is null)
        {
            return;
        }

        output.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"[{EngineId}] ケース {Cases} 件")
        );

        foreach (var factor in Factors)
        {
            var levels = factor.Levels.Select(level =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{level.Name}={LevelCounts.GetValueOrDefault((factor.Name, level.Name))}"
                )
            );
            output.WriteLine($"[{EngineId}] {factor.Name}: {string.Join(", ", levels)}");
        }

        var required = Required;
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[{EngineId}] 網羅すべき組 {required.Count}（2 因子 {PairCounts.Count}・3 因子 {TripleCounts.Count}）・網羅 {required.Count - Missing.Count}・欠け {Missing.Count}"
            )
        );

        if (Missing.Count == 0)
        {
            return;
        }

        var byFactors = Missing
            .GroupBy(tuple => tuple.FactorKey, StringComparer.Ordinal)
            .Select(group =>
                string.Create(CultureInfo.InvariantCulture, $"{group.Key} {group.Count()} 件")
            );
        output.WriteLine($"[{EngineId}] 欠けの内訳: {string.Join("・", byFactors)}");
    }

    /// <summary>観測しなかった網羅すべき組を、既知の失敗の照合に渡す失敗にする</summary>
    /// <param name="scope">範囲（入口など）</param>
    public IReadOnlyList<EngineFailure> MissingAsFailures(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return Missing
            .Select(tuple => new EngineFailure(scope, tuple.Key, tuple.Key, Source: tuple))
            .ToList();
    }

    /// <summary>
    /// 観測しなかった網羅すべき組が、網羅しない組の一覧とちょうど一致することを表明する
    /// （一覧に無い欠けも、一覧にあるのに網羅した組も赤）
    /// </summary>
    /// <param name="gaps">網羅しない組の一覧</param>
    /// <param name="scope">範囲（この範囲の項目だけで照合する）</param>
    /// <param name="plan">
    /// 流した種（null なら常に照合する）。既定の種の数で流したときだけ照合する
    /// （種を絞ると欠けが増え、増やすと欠けが減るので、一覧は既定の種の数での網羅に合わせて書く）
    /// </param>
    /// <param name="output">夜間に埋まった組を書く出力先（null なら現在のテストの出力）</param>
    /// <remarks>
    /// 夜間の既定の種の数（<see cref="EngineProfile.Nightly"/> かつ <see cref="SeedPlan.IsDefaultCount"/>）では、
    /// 「一覧に無い欠け」と理由の食い違いだけを赤にし、「一覧にあるのに網羅した」項目は赤にせず「夜間なら埋まる組」として
    /// 出力へ書く。一覧は普段の既定の種の数での網羅に合わせて書いてあり、夜間の種は普段の種を先頭に含むので、夜間に新しい欠けは
    /// 出ず、埋まる向きにだけずれる。
    /// </remarks>
    /// <exception cref="CoverageDeclarationException">一覧の項目が、この次元表の網羅すべき組を指していない（種の数に依らず検査する）</exception>
    public void AssertGaps(
        CoverageGapSet gaps,
        string scope,
        SeedPlan? plan = null,
        ITestOutputHelper? output = null
    )
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        ValidateGapDeclarations(gaps, scope);

        if (plan is not null && !plan.IsDefaultCount)
        {
            return;
        }

        var known = gaps.Known.Classify(
            MissingAsFailures(scope),
            ownsScope: s => string.Equals(s, scope, StringComparison.Ordinal)
        );
        var title = $"{EngineId} の網羅しない組（{scope}）";

        if (plan?.Profile == EngineProfile.Nightly)
        {
            WriteFilledAtNightly(known.NotObserved, scope, output);
            known = known with { NotObserved = [] };
        }

        known.AssertClean(title);
    }

    /// <summary>夜間の種の数で網羅した、一覧に載っている組をテストの出力へ書く</summary>
    private void WriteFilledAtNightly(
        IReadOnlyList<KnownEntry> filled,
        string scope,
        ITestOutputHelper? output
    )
    {
        output ??= TestContext.Current.TestOutputHelper;

        if (output is null || filled.Count == 0)
        {
            return;
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[{EngineId}] 夜間なら埋まる組（{scope}）: {filled.Count} 項目（普段の既定の種の数では欠けるので一覧に残す）"
            )
        );

        foreach (var entry in filled)
        {
            output.WriteLine($"[{EngineId}]   {entry.Describe()}");
        }
    }

    /// <summary>網羅しない組の一覧の、範囲の項目ごとの当たり方（報告に使う）</summary>
    /// <param name="gaps">網羅しない組の一覧</param>
    /// <param name="scope">範囲</param>
    public IReadOnlyList<CoverageGapUsage> GapUsage(CoverageGapSet gaps, string scope)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var owned = gaps
            .Entries.Where(entry =>
                string.Equals(entry.Entry.Scope, scope, StringComparison.Ordinal)
            )
            .ToList();
        var known = gaps.Known.Classify(
            MissingAsFailures(scope),
            ownsScope: s => string.Equals(s, scope, StringComparison.Ordinal)
        );
        var required = Required;

        return owned
            .Select(entry =>
            {
                var matched = known.Matched.Count(match =>
                    ReferenceEquals(match.Entry, entry.Entry)
                );
                var inGroup = required.Count(tuple => tuple.HasFactors(entry.Factors));
                return new CoverageGapUsage(
                    entry,
                    matched,
                    inGroup,
                    inGroup > 0 && matched == inGroup
                );
            })
            .ToList();
    }

    /// <summary>
    /// 網羅しない組の一覧の、範囲の項目ごとの当たり方（当たった欠けの数 / 同じ因子の組の網羅すべき組の数）をテストの出力へ書く。
    /// 因子の組を丸ごと覆う項目（その因子の組が 1 つも観測されていない）には印を付ける
    /// </summary>
    /// <param name="gaps">網羅しない組の一覧</param>
    /// <param name="scope">範囲</param>
    /// <param name="output">出力先（null なら現在のテストの出力）</param>
    public void WriteGapUsage(CoverageGapSet gaps, string scope, ITestOutputHelper? output = null)
    {
        var usage = GapUsage(gaps, scope);
        output ??= TestContext.Current.TestOutputHelper;

        if (output is null)
        {
            return;
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[{EngineId}] 網羅しない組の一覧（{scope}）: {usage.Count} 項目"
            )
        );

        foreach (var item in usage)
        {
            var whole = item.CoversWholeFactorGroup ? "・因子の組を丸ごと覆う" : "";
            var kind =
                item.Entry.Entry.Kind == KnownKind.Accepted
                    ? "Accepted"
                    : $"Pending {item.Entry.Entry.FixedIn}";
            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{EngineId}]   {item.Entry.Entry.Finding}（{kind}） {item.Entry.FactorKey}: 当たり {item.Matched} / {item.RequiredInFactorGroup}{whole}（理由：{item.Entry.Entry.Reason}）"
                )
            );
        }
    }

    /// <summary>範囲の項目が、この次元表の網羅すべき組を指しているかを検査する</summary>
    private void ValidateGapDeclarations(CoverageGapSet gaps, string scope)
    {
        var problems = new List<string>();

        if (!string.Equals(gaps.Known.EngineId, EngineId, StringComparison.Ordinal))
        {
            problems.Add(
                $"一覧のエンジン {gaps.Known.EngineId} が次元表のエンジン {EngineId} と違う"
            );
        }

        var required = Required;
        var keys = required.Select(tuple => tuple.Key).ToHashSet(StringComparer.Ordinal);

        foreach (
            var entry in gaps.Entries.Where(entry =>
                string.Equals(entry.Entry.Scope, scope, StringComparison.Ordinal)
            )
        )
        {
            if (!required.Any(tuple => tuple.HasFactors(entry.Factors)))
            {
                problems.Add(
                    $"{entry.Entry.Finding}: 因子の組 {entry.FactorKey} に網羅すべき組が無い（因子の綴り・isValid・固定した水準を確かめる）"
                );
                continue;
            }

            if (
                entry.Entry.Match == KnownMatch.ExactSignature
                && !keys.Contains(entry.Entry.Signature!)
            )
            {
                problems.Add(
                    $"{entry.Entry.Finding}: 組 {entry.Entry.Signature} は網羅すべき組でない（因子を表の宣言の順に並べたか・水準の綴り・isValid・固定した水準を確かめる）"
                );
            }
        }

        if (problems.Count > 0)
        {
            throw new CoverageDeclarationException(
                $"{EngineId} の網羅しない組の一覧（{scope}）が次元表と食い違う:\n  "
                    + string.Join("\n  ", problems)
            );
        }
    }
}

/// <summary>網羅しない組の一覧の 1 項目（既知の失敗の項目と、それが指す因子の組）</summary>
/// <param name="Entry">既知の失敗の項目</param>
/// <param name="Factors">項目が指す因子（並びは問わない）</param>
public sealed record CoverageGapEntry(KnownEntry Entry, IReadOnlyList<string> Factors)
{
    /// <summary>因子の組の表記（項目を作ったときの並び）</summary>
    public string FactorKey => string.Join(CoverageTuple.Separator, Factors);
}

/// <summary>網羅しない組の一覧（照合は <see cref="KnownFailureSet"/> の規則のまま）</summary>
public sealed class CoverageGapSet
{
    /// <summary>一覧を作る（項目の整合は <see cref="KnownFailureSet"/> が構築時に検査する）</summary>
    /// <param name="engineId">エンジンの ID</param>
    /// <param name="entries">項目（<see cref="CoverageGap.Tuple"/> か <see cref="CoverageGap.Matching"/> で作る）</param>
    public CoverageGapSet(string engineId, IEnumerable<CoverageGapEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries.ToList();
        Known = new KnownFailureSet(engineId, Entries.Select(entry => entry.Entry).ToList());
    }

    /// <summary>項目</summary>
    public IReadOnlyList<CoverageGapEntry> Entries { get; }

    /// <summary>照合に使う既知の失敗の一覧</summary>
    public KnownFailureSet Known { get; }
}

/// <summary>
/// 網羅しない組の項目を作る。まだ作れないだけの組（<see cref="KnownKind.Pending"/>）は直す区切りと理由が、
/// 作れないことを受け入れた組（<see cref="KnownKind.Accepted"/>）は理由が必須
/// </summary>
/// <remarks>
/// <para>
/// 原理的に起こり得ない組み合わせはここに載せず、次元表の <c>isValid</c> に書く（<see cref="DimensionTable"/> を参照）。
/// </para>
/// <para>
/// <see cref="AcceptedTuple"/>・<see cref="AcceptedMatching"/> は、<b>エンジンを直しても埋められない</b>組だけに使う
/// （製品・プロバイダ・ツールのスキーマの事実で、その組をエンジンが作る相手が無いもの。例：方言の型の対応に無い列の種類・
/// その型の引数を持つツールが無い）。理由にはその事実と根拠（ファイルと対応）を書く。エンジンがまだ作っていないだけの組は、
/// 直す区切りつきの <see cref="Tuple"/>・<see cref="Matching"/> に残す。
/// </para>
/// <para>
/// 乱数の種で欠けが決まる組（既定の種で偶然出ない組）の一覧は、固定した既定の種の並びに合わせて書く。
/// 生成器を変えて種の指紋が変わったら、指紋と一緒に一覧も書き直す。
/// </para>
/// </remarks>
public static class CoverageGap
{
    /// <summary>一覧を作る</summary>
    /// <param name="engineId">エンジンの ID（次元表と同じ）</param>
    /// <param name="entries">項目</param>
    public static CoverageGapSet Set(string engineId, IEnumerable<CoverageGapEntry> entries) =>
        new(engineId, entries);

    /// <summary>1 つの組を名指しする項目（まだ作れない組）</summary>
    /// <param name="scope">範囲</param>
    /// <param name="finding">指摘の番号（例 <c>P1-3</c>）</param>
    /// <param name="fixedIn">直す区切り</param>
    /// <param name="reason">まだ作れない理由</param>
    /// <param name="items">因子と水準（<b>表の宣言の順</b>に並べる。違う並びは網羅すべき組でないとして宣言の誤りになる）</param>
    public static CoverageGapEntry Tuple(
        string scope,
        string finding,
        string fixedIn,
        string reason,
        params (string Factor, string Level)[] items
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixedIn);
        return TupleEntry(scope, finding, KnownKind.Pending, fixedIn, reason, items);
    }

    /// <summary>
    /// 1 つの組を名指しする項目（製品・プロバイダ・ツールのスキーマの事実で作れないことを受け入れた組。直す区切りは持たない）
    /// </summary>
    /// <param name="scope">範囲</param>
    /// <param name="finding">指摘の番号（例 <c>P1-3</c>）</param>
    /// <param name="reason">エンジンを直しても埋められない理由（製品の事実と根拠）</param>
    /// <param name="items">因子と水準（<b>表の宣言の順</b>に並べる。違う並びは網羅すべき組でないとして宣言の誤りになる）</param>
    public static CoverageGapEntry AcceptedTuple(
        string scope,
        string finding,
        string reason,
        params (string Factor, string Level)[] items
    ) => TupleEntry(scope, finding, KnownKind.Accepted, null, reason, items);

    /// <summary>1 つの因子の組（2 因子）のうち、水準が述語に当たる組をまとめて指す項目（まだ作れない組）</summary>
    /// <param name="scope">範囲</param>
    /// <param name="finding">指摘の番号（例 <c>P1-3</c>）</param>
    /// <param name="fixedIn">直す区切り</param>
    /// <param name="reason">まだ作れない理由</param>
    /// <param name="factorA">因子 A</param>
    /// <param name="factorB">因子 B</param>
    /// <param name="predicate">A の水準と B の水準を受けて当たるかを返す（因子の組がちょうど {A, B} の組だけに掛ける）</param>
    public static CoverageGapEntry Matching(
        string scope,
        string finding,
        string fixedIn,
        string reason,
        string factorA,
        string factorB,
        Func<string, string, bool> predicate
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixedIn);
        return MatchingEntry(
            scope,
            finding,
            KnownKind.Pending,
            fixedIn,
            reason,
            factorA,
            factorB,
            predicate
        );
    }

    /// <summary>
    /// 1 つの因子の組（2 因子）のうち、水準が述語に当たる組をまとめて指す項目
    /// （製品・プロバイダ・ツールのスキーマの事実で作れないことを受け入れた組。直す区切りは持たない）
    /// </summary>
    /// <param name="scope">範囲</param>
    /// <param name="finding">指摘の番号（例 <c>P1-3</c>）</param>
    /// <param name="reason">エンジンを直しても埋められない理由（製品の事実と根拠）</param>
    /// <param name="factorA">因子 A</param>
    /// <param name="factorB">因子 B</param>
    /// <param name="predicate">A の水準と B の水準を受けて当たるかを返す（因子の組がちょうど {A, B} の組だけに掛ける）</param>
    public static CoverageGapEntry AcceptedMatching(
        string scope,
        string finding,
        string reason,
        string factorA,
        string factorB,
        Func<string, string, bool> predicate
    ) =>
        MatchingEntry(
            scope,
            finding,
            KnownKind.Accepted,
            null,
            reason,
            factorA,
            factorB,
            predicate
        );

    /// <summary>1 つの組を名指しする項目の本体（種類と直す区切りだけが違う 2 つの入口が共有する）</summary>
    private static CoverageGapEntry TupleEntry(
        string scope,
        string finding,
        KnownKind kind,
        string? fixedIn,
        string reason,
        (string Factor, string Level)[] items
    )
    {
        RequireText(scope, finding, reason);
        ArgumentNullException.ThrowIfNull(items);

        var tuple = new CoverageTuple(items);
        var entry = new KnownEntry(
            finding,
            kind,
            fixedIn,
            reason,
            scope,
            KnownMatch.ExactSignature,
            Signature: tuple.Key
        );
        return new CoverageGapEntry(entry, items.Select(item => item.Factor).ToList());
    }

    /// <summary>述語で組をまとめて指す項目の本体（種類と直す区切りだけが違う 2 つの入口が共有する）</summary>
    private static CoverageGapEntry MatchingEntry(
        string scope,
        string finding,
        KnownKind kind,
        string? fixedIn,
        string reason,
        string factorA,
        string factorB,
        Func<string, string, bool> predicate
    )
    {
        RequireText(scope, finding, reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(factorA);
        ArgumentException.ThrowIfNullOrWhiteSpace(factorB);
        ArgumentNullException.ThrowIfNull(predicate);

        if (string.Equals(factorA, factorB, StringComparison.Ordinal))
        {
            throw new ArgumentException($"因子 A と B が同じ（{factorA}）", nameof(factorB));
        }

        var entry = new KnownEntry(
            finding,
            kind,
            fixedIn,
            reason,
            scope,
            KnownMatch.Predicate,
            Predicate: failure =>
                failure.Source is CoverageTuple tuple
                && tuple.HasFactors([factorA, factorB])
                && predicate(LevelOf(tuple, factorA), LevelOf(tuple, factorB))
        );
        return new CoverageGapEntry(entry, [factorA, factorB]);
    }

    private static string LevelOf(CoverageTuple tuple, string factor) =>
        tuple
            .Items.First(item => string.Equals(item.Factor, factor, StringComparison.Ordinal))
            .Level;

    private static void RequireText(string scope, string finding, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(finding);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
    }
}
