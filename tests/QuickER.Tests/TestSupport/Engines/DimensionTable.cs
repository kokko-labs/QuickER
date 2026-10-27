using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>製品の仕様の軸の 1 項目（列挙の値かプロパティ）の身元</summary>
/// <param name="DeclaringType">宣言している型</param>
/// <param name="Member">メンバーの名前（列挙の値の名前かプロパティの名前。序数で比べる）</param>
public sealed record AxisMember(Type DeclaringType, string Member)
{
    /// <summary>列挙の値を軸の項目にする（定義されていない値は断る）</summary>
    /// <typeparam name="TEnum">列挙の型</typeparam>
    /// <param name="value">列挙の値</param>
    public static AxisMember Of<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"{typeof(TEnum).Name} に定義されていない値"
            );
        }

        return new AxisMember(typeof(TEnum), value.ToString());
    }

    /// <summary>型のプロパティを軸の項目にする（存在しないプロパティは断る）</summary>
    /// <param name="type">プロパティを持つ型</param>
    /// <param name="name">プロパティの名前</param>
    public static AxisMember Property(Type type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;

        if (type.GetProperty(name, flags) is null)
        {
            throw new ArgumentException($"{type.Name} にプロパティ {name} が無い", nameof(name));
        }

        return new AxisMember(type, name);
    }
}

/// <summary>因子の水準</summary>
/// <param name="Name">水準の名前（因子の中で一意）</param>
/// <param name="Source">この水準が表す製品の仕様の軸の項目（無ければ null）</param>
public sealed record Level(string Name, AxisMember? Source = null);

/// <summary>次元表の因子（エンジンが組み合わせて流す入力の 1 つの観点）</summary>
/// <param name="Name">因子の名前（表の中で一意）</param>
/// <param name="Levels">水準（1 つ以上・宣言の順）</param>
/// <param name="Description">説明（無ければ null）</param>
/// <param name="Source">因子全体が表す製品の仕様の軸（例：あるオプションのプロパティ。無ければ null）</param>
public sealed record Factor(
    string Name,
    IReadOnlyList<Level> Levels,
    string? Description = null,
    AxisMember? Source = null
);

/// <summary>3 因子の組まで網羅する因子の集まり（集まりの因子から選んだ 3 つの組をすべて網羅する）</summary>
/// <param name="Factors">因子の名前（相異なる 3 つ以上）</param>
public sealed record FactorGroup(IReadOnlyList<string> Factors)
{
    /// <summary>因子の名前を並べて作る</summary>
    /// <param name="factors">因子の名前</param>
    public FactorGroup(params string[] factors)
        : this((IReadOnlyList<string>)factors) { }
}

/// <summary>網羅すべき組（2 つか 3 つの因子と、その水準）</summary>
/// <remarks>
/// 等しさは <see cref="Key"/> の序数の一致で決める。<see cref="DimensionTable"/> が作る組は、因子を表の宣言の順に並べる。
/// </remarks>
public sealed class CoverageTuple : IEquatable<CoverageTuple>
{
    /// <summary>因子の区切り</summary>
    public const string Separator = " × ";

    /// <summary>組を作る</summary>
    /// <param name="items">因子と水準（並びがそのまま <see cref="Key"/> の並びになる）</param>
    public CoverageTuple(IReadOnlyList<(string Factor, string Level)> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            throw new ArgumentException("組に因子が無い", nameof(items));
        }

        Items = items.ToList();
        Key = string.Join(Separator, Items.Select(item => $"{item.Factor}={item.Level}"));
        FactorKey = string.Join(Separator, Items.Select(item => item.Factor));
    }

    /// <summary>因子と水準</summary>
    public IReadOnlyList<(string Factor, string Level)> Items { get; }

    /// <summary>組の鍵（<c>A=a × B=b</c>）</summary>
    public string Key { get; }

    /// <summary>因子の組の鍵（<c>A × B</c>）</summary>
    public string FactorKey { get; }

    /// <summary>割り当てがこの組の水準をすべて含むか</summary>
    /// <param name="assignment">因子から水準への割り当て</param>
    public bool IsContainedIn(IReadOnlyDictionary<string, string> assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        foreach (var (factor, level) in Items)
        {
            if (
                !assignment.TryGetValue(factor, out var assigned)
                || !string.Equals(assigned, level, StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>因子の集まりが同じか（並びは問わない）</summary>
    /// <param name="factors">因子の名前</param>
    public bool HasFactors(IReadOnlyCollection<string> factors)
    {
        ArgumentNullException.ThrowIfNull(factors);
        return Items.Count == factors.Count
            && Items.All(item => factors.Contains(item.Factor, StringComparer.Ordinal));
    }

    /// <inheritdoc/>
    public bool Equals(CoverageTuple? other) =>
        other is not null && string.Equals(Key, other.Key, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CoverageTuple);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    /// <inheritdoc/>
    public override string ToString() => Key;
}

/// <summary>
/// エンジンの次元表（因子・水準・3 因子まで網羅する集まり・原理的に起こり得ない組の規則）
/// </summary>
/// <remarks>
/// <para>
/// 網羅すべき組は、因子の 2 つ組の全水準（<see cref="RequiredPairs"/>）と、3 因子の集まりの 3 つ組の全水準
/// （<see cref="RequiredTriples"/>）のうち、<c>isValid</c> が偽を返さないもの。
/// </para>
/// <para>
/// <c>isValid</c> は<b>一部の因子だけ</b>を含む割り当て（2 つ組・3 つ組・組み立て途中の行）を受け取り、
/// その割り当てが<b>原理的に決して起こり得ない</b>組み合わせのときだけ偽を返す。判断に要る因子が割り当てに無ければ真を返す
/// （無い因子を仮定して偽にすると、網羅すべき組が黙って減る）。
/// </para>
/// <para>
/// ここに書くのは原理的に起こり得ない組み合わせだけで、規則ごとに理由のコメントを付ける。
/// エンジンがまだ作れないだけの組み合わせは <c>isValid</c> に書かず、網羅しない組の一覧（<see cref="CoverageGap"/>）に
/// 直す区切りと理由つきで載せる（ここに混ぜると、網羅しない組の一覧が実態より短く見える）。
/// </para>
/// </remarks>
public sealed class DimensionTable
{
    private readonly IReadOnlyList<Factor> _factors;
    private readonly Dictionary<string, int> _factorIndex;
    private readonly Func<IReadOnlyDictionary<string, string>, bool>? _isValid;

    /// <summary>次元表を作る（整っていなければ問題をすべて並べて断る）</summary>
    /// <param name="engineId">エンジンの ID</param>
    /// <param name="factors">因子（2 つ以上・宣言の順）</param>
    /// <param name="strength3Groups">3 因子の組まで網羅する集まり（無ければ 2 因子の組だけ）</param>
    /// <param name="isValid">原理的に起こり得ない組み合わせのときだけ偽を返す規則（<see cref="DimensionTable"/> の説明を参照）</param>
    public DimensionTable(
        string engineId,
        IReadOnlyList<Factor> factors,
        IReadOnlyList<FactorGroup>? strength3Groups = null,
        Func<IReadOnlyDictionary<string, string>, bool>? isValid = null
    )
    {
        ArgumentNullException.ThrowIfNull(factors);

        var groups = strength3Groups ?? [];
        var problems = Validate(engineId, factors, groups);

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"{(string.IsNullOrWhiteSpace(engineId) ? "(エンジンの ID なし)" : engineId)} の次元表が整っていない:\n  "
                    + string.Join("\n  ", problems),
                nameof(factors)
            );
        }

        EngineId = engineId;
        _factors = factors.ToList();
        _factorIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < _factors.Count; i++)
        {
            _factorIndex[_factors[i].Name] = i;
        }

        Strength3Groups = groups.ToList();
        _isValid = isValid;
    }

    /// <summary>エンジンの ID</summary>
    public string EngineId { get; }

    /// <summary>因子（宣言の順）</summary>
    public IReadOnlyList<Factor> Factors => _factors;

    /// <summary>3 因子の組まで網羅する集まり</summary>
    public IReadOnlyList<FactorGroup> Strength3Groups { get; }

    /// <summary>割り当て（一部の因子でもよい）が原理的に起こり得るか</summary>
    /// <param name="assignment">因子から水準への割り当て</param>
    public bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return _isValid?.Invoke(assignment) ?? true;
    }

    /// <summary>名前で因子を引く</summary>
    /// <param name="name">因子の名前</param>
    /// <exception cref="KeyNotFoundException">表に無い因子</exception>
    public Factor Factor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_factorIndex.TryGetValue(name, out var index))
        {
            throw new KeyNotFoundException($"{EngineId} の次元表に因子 {name} が無い");
        }

        return _factors[index];
    }

    /// <summary>因子の宣言の位置（無ければ -1）</summary>
    /// <param name="name">因子の名前</param>
    public int IndexOf(string name) => _factorIndex.TryGetValue(name, out var index) ? index : -1;

    /// <summary>網羅すべき 2 因子の組（因子の宣言の順・水準の宣言の順）</summary>
    /// <param name="fixedLevels">
    /// この実行で 1 つの水準に固定する因子（例：入口ごとに固定する名前の種類）。固定した因子を含む組は固定した水準のときだけ要り、
    /// <c>isValid</c> には固定した水準を合わせた割り当てを渡す
    /// </param>
    public IReadOnlyList<CoverageTuple> RequiredPairs(
        IReadOnlyDictionary<string, string>? fixedLevels = null
    )
    {
        var fixedMap = NormalizeFixedLevels(fixedLevels);
        var indexes = new List<int[]>();

        for (var i = 0; i < _factors.Count; i++)
        {
            for (var j = i + 1; j < _factors.Count; j++)
            {
                indexes.Add([i, j]);
            }
        }

        return Enumerate(indexes, fixedMap);
    }

    /// <summary>網羅すべき 3 因子の組（集まりごとの 3 つ組・集まりをまたいで重複を除く）</summary>
    /// <param name="fixedLevels">この実行で 1 つの水準に固定する因子（<see cref="RequiredPairs"/> と同じ）</param>
    public IReadOnlyList<CoverageTuple> RequiredTriples(
        IReadOnlyDictionary<string, string>? fixedLevels = null
    )
    {
        var fixedMap = NormalizeFixedLevels(fixedLevels);
        var indexes = new List<int[]>();
        var seen = new HashSet<(int, int, int)>();

        foreach (var group in Strength3Groups)
        {
            var members = group
                .Factors.Distinct(StringComparer.Ordinal)
                .Select(name => _factorIndex[name])
                .Order()
                .ToList();

            for (var a = 0; a < members.Count; a++)
            {
                for (var b = a + 1; b < members.Count; b++)
                {
                    for (var c = b + 1; c < members.Count; c++)
                    {
                        if (seen.Add((members[a], members[b], members[c])))
                        {
                            indexes.Add([members[a], members[b], members[c]]);
                        }
                    }
                }
            }
        }

        return Enumerate(indexes, fixedMap);
    }

    /// <summary>固定する水準を検査して、序数の辞書にする</summary>
    /// <param name="fixedLevels">固定する水準（null なら空）</param>
    /// <exception cref="ArgumentException">表に無い因子・宣言していない水準</exception>
    public Dictionary<string, string> NormalizeFixedLevels(
        IReadOnlyDictionary<string, string>? fixedLevels
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (fixedLevels is null)
        {
            return result;
        }

        var problems = new List<string>();

        foreach (var (factor, level) in fixedLevels)
        {
            if (!_factorIndex.TryGetValue(factor, out var index))
            {
                problems.Add($"固定する因子 {factor} が表に無い");
                continue;
            }

            if (!HasLevel(_factors[index], level))
            {
                problems.Add($"固定する水準 {factor}={level} が宣言されていない");
                continue;
            }

            result[factor] = level;
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"{EngineId} の固定する水準が整っていない:\n  " + string.Join("\n  ", problems),
                nameof(fixedLevels)
            );
        }

        return result;
    }

    /// <summary>因子が水準を宣言しているか</summary>
    internal static bool HasLevel(Factor factor, string level) =>
        factor.Levels.Any(l => string.Equals(l.Name, level, StringComparison.Ordinal));

    /// <summary>因子の位置の組ごとに水準を総当たりし、起こり得る組だけを返す</summary>
    private List<CoverageTuple> Enumerate(
        IReadOnlyList<int[]> combinations,
        Dictionary<string, string> fixedMap
    )
    {
        var result = new List<CoverageTuple>();

        foreach (var combination in combinations)
        {
            var levelChoices = combination
                .Select(index =>
                {
                    var factor = _factors[index];
                    return fixedMap.TryGetValue(factor.Name, out var fixedLevel)
                        ? new List<string> { fixedLevel }
                        : factor.Levels.Select(l => l.Name).ToList();
                })
                .ToList();

            foreach (var levels in Product(levelChoices))
            {
                var items = combination
                    .Select((index, position) => (_factors[index].Name, levels[position]))
                    .ToList();
                var partial = new Dictionary<string, string>(fixedMap, StringComparer.Ordinal);

                foreach (var (factor, level) in items)
                {
                    partial[factor] = level;
                }

                if (IsValid(partial))
                {
                    result.Add(new CoverageTuple(items));
                }
            }
        }

        return result;
    }

    /// <summary>水準の選び方の直積（先頭の因子が外側）</summary>
    private static IEnumerable<string[]> Product(IReadOnlyList<List<string>> choices)
    {
        var current = new string[choices.Count];
        return Recurse(0);

        IEnumerable<string[]> Recurse(int depth)
        {
            if (depth == choices.Count)
            {
                yield return (string[])current.Clone();
                yield break;
            }

            foreach (var level in choices[depth])
            {
                current[depth] = level;

                foreach (var row in Recurse(depth + 1))
                {
                    yield return row;
                }
            }
        }
    }

    private static List<string> Validate(
        string engineId,
        IReadOnlyList<Factor> factors,
        IReadOnlyList<FactorGroup> groups
    )
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(engineId))
        {
            problems.Add("エンジンの ID が空");
        }

        if (factors.Count < 2)
        {
            problems.Add($"因子が {factors.Count} 個しかない（2 個以上）");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var factor in factors)
        {
            if (factor is null)
            {
                problems.Add("null の因子がある");
                continue;
            }

            if (string.IsNullOrWhiteSpace(factor.Name))
            {
                problems.Add("因子の名前が空");
                continue;
            }

            if (!names.Add(factor.Name))
            {
                problems.Add($"因子 {factor.Name} が重複している");
            }

            if (HasReservedCharacter(factor.Name))
            {
                problems.Add($"因子 {factor.Name} の名前に × か = がある");
            }

            if (factor.Levels is null || factor.Levels.Count == 0)
            {
                problems.Add($"因子 {factor.Name} に水準が無い");
                continue;
            }

            var levels = new HashSet<string>(StringComparer.Ordinal);

            foreach (var level in factor.Levels)
            {
                if (level is null || string.IsNullOrWhiteSpace(level.Name))
                {
                    problems.Add($"因子 {factor.Name} に名前の空の水準がある");
                    continue;
                }

                if (!levels.Add(level.Name))
                {
                    problems.Add($"因子 {factor.Name} の水準 {level.Name} が重複している");
                }

                if (HasReservedCharacter(level.Name))
                {
                    problems.Add($"因子 {factor.Name} の水準 {level.Name} の名前に × か = がある");
                }
            }
        }

        foreach (var group in groups)
        {
            if (group?.Factors is null)
            {
                problems.Add("null の 3 因子の集まりがある");
                continue;
            }

            var label = string.Join(", ", group.Factors);
            var distinct = group.Factors.Distinct(StringComparer.Ordinal).ToList();

            if (distinct.Count != group.Factors.Count)
            {
                problems.Add($"3 因子の集まり [{label}] に同じ因子が重複している");
            }

            if (distinct.Count < 3)
            {
                problems.Add($"3 因子の集まり [{label}] の因子が 3 個未満");
            }

            foreach (var name in distinct.Where(name => name is null || !names.Contains(name)))
            {
                problems.Add($"3 因子の集まり [{label}] の因子 {name} が表に無い");
            }
        }

        return problems;
    }

    private static bool HasReservedCharacter(string name) =>
        name.Contains('×', StringComparison.Ordinal)
        || name.Contains('=', StringComparison.Ordinal);
}
