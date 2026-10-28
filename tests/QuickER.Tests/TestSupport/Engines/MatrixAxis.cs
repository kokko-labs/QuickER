using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>行列の軸の値ごとのアダプタ（軸の値を 1 つ受け持つ）</summary>
/// <remarks>
/// 軸の値による違いは、アダプタの抽象メンバーとして表す。アダプタの中で軸の値で分岐しない
/// （<c>if (value == "oracle")</c> のような分岐は、値を足したときに黙って既定の枝へ落ちる）。
/// </remarks>
public interface IMatrixAdapter
{
    /// <summary>受け持つ軸の値</summary>
    string AxisValue { get; }
}

/// <summary>
/// 正本の値の並びと、値ごとのアダプタ（または理由つきの除外）が一対一に対応する行列の軸
/// </summary>
/// <typeparam name="TAdapter">アダプタの型</typeparam>
/// <remarks>
/// <para>
/// 構築時に、正本の値の重複・正本に無い除外・理由の空の除外・正本に無い値のアダプタ・除外した値のアダプタ・
/// 同じ値のアダプタの重複・アダプタも除外も無い正本の値を、すべて並べて断る（正本に値を足したのにアダプタを書き忘れると赤になる）。
/// 比較は序数。
/// </para>
/// <para>
/// アダプタの中で軸の値で分岐しない。値による違いはアダプタの抽象メンバーとして表す（<see cref="IMatrixAdapter"/> を参照）。
/// </para>
/// </remarks>
public sealed class MatrixAxis<TAdapter>
    where TAdapter : IMatrixAdapter
{
    private readonly IReadOnlyList<string> _authoritative;
    private readonly Dictionary<string, TAdapter> _byValue;

    /// <summary>軸を作る（整っていなければ問題をすべて並べて断る）</summary>
    /// <param name="name">軸の名前</param>
    /// <param name="authoritativeValues">正本の値（例：方言の名前の正本）</param>
    /// <param name="adapters">値ごとのアダプタ</param>
    /// <param name="excluded">アダプタを持たない値と、その理由</param>
    public MatrixAxis(
        string name,
        IReadOnlyList<string> authoritativeValues,
        IReadOnlyList<TAdapter> adapters,
        IReadOnlyDictionary<string, string>? excluded = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(authoritativeValues);
        ArgumentNullException.ThrowIfNull(adapters);

        var excludedMap = excluded ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var authoritative = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in authoritativeValues)
        {
            if (!authoritative.Add(value))
            {
                problems.Add($"正本の値 {value} が重複している");
            }
        }

        foreach (var (value, reason) in excludedMap)
        {
            if (!authoritative.Contains(value))
            {
                problems.Add($"除外した値 {value} が正本に無い");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"除外した値 {value} の理由が空");
            }
        }

        var byValue = new Dictionary<string, TAdapter>(StringComparer.Ordinal);

        foreach (var adapter in adapters)
        {
            var value = adapter.AxisValue;

            if (value is null || !authoritative.Contains(value))
            {
                problems.Add($"アダプタ {adapter.GetType().Name} の値 {value} が正本に無い");
                continue;
            }

            if (excludedMap.ContainsKey(value))
            {
                problems.Add($"除外した値 {value} にアダプタ {adapter.GetType().Name} がある");
            }

            if (!byValue.TryAdd(value, adapter))
            {
                problems.Add($"値 {value} のアダプタが重複している");
            }
        }

        foreach (
            var value in authoritativeValues
                .Distinct(StringComparer.Ordinal)
                .Where(value => !byValue.ContainsKey(value) && !excludedMap.ContainsKey(value))
        )
        {
            problems.Add($"正本の値 {value} にアダプタも除外の理由も無い");
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"行列の軸 {name} が整っていない:\n  " + string.Join("\n  ", problems),
                nameof(adapters)
            );
        }

        Name = name;
        _authoritative = authoritativeValues.ToList();
        _byValue = byValue;
        Excluded = new Dictionary<string, string>(excludedMap, StringComparer.Ordinal);
        Adapters = _authoritative
            .Where(value => _byValue.ContainsKey(value))
            .Select(value => _byValue[value])
            .ToList();
    }

    /// <summary>軸の名前</summary>
    public string Name { get; }

    /// <summary>アダプタ（正本の並び）</summary>
    public IReadOnlyList<TAdapter> Adapters { get; }

    /// <summary>アダプタを持たない値と、その理由</summary>
    public IReadOnlyDictionary<string, string> Excluded { get; }

    /// <summary>値のアダプタを引く</summary>
    /// <param name="value">軸の値</param>
    /// <exception cref="InvalidOperationException">除外した値</exception>
    /// <exception cref="KeyNotFoundException">正本に無い値</exception>
    public TAdapter Get(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (_byValue.TryGetValue(value, out var adapter))
        {
            return adapter;
        }

        if (Excluded.TryGetValue(value, out var reason))
        {
            throw new InvalidOperationException(
                $"行列の軸 {Name} の値 {value} は除外している（{reason}）"
            );
        }

        throw new KeyNotFoundException($"行列の軸 {Name} に値 {value} が無い");
    }

    /// <summary>アダプタを持つ値を正本の並びで Theory のデータにする（除外した値は含まない）</summary>
    public TheoryData<string> Values()
    {
        var data = new TheoryData<string>();

        foreach (var value in _authoritative.Where(value => _byValue.ContainsKey(value)))
        {
            data.Add(value);
        }

        return data;
    }
}

/// <summary>派生クラスが受け持つ行列の軸の値（<see cref="MatrixAxisGuard"/> が全数を照合する）</summary>
/// <param name="axis">軸の名前</param>
/// <param name="value">軸の値</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MatrixAxisValueAttribute(string axis, string value) : Attribute
{
    /// <summary>軸の名前</summary>
    public string Axis { get; } = axis;

    /// <summary>軸の値</summary>
    public string Value { get; } = value;
}

/// <summary>
/// 「共通基底 1＋値ごとの派生 N」の形のテストで、派生クラスが軸の正本の値と一対一に対応することを確かめる
/// </summary>
/// <remarks>
/// 派生クラスの中で軸の値で分岐しない（<c>if (value == "oracle")</c>）。値による違いは基底の抽象メンバーとして表し、
/// 派生がそれを実装する（値を足したときに、実装し忘れがビルドで分かる）。
/// </remarks>
public static class MatrixAxisGuard
{
    /// <summary>派生クラスと軸の値の対応の問題をすべて並べる（無ければ空）</summary>
    /// <param name="baseOrMarker">基底のクラスか目印のインターフェイス（ジェネリックの定義も可）。同じアセンブリの具象クラスを調べる</param>
    /// <param name="axis">軸の名前</param>
    /// <param name="authoritative">正本の値</param>
    /// <param name="excluded">派生を持たない値と、その理由</param>
    public static IReadOnlyList<string> DerivedClassesProblems(
        Type baseOrMarker,
        string axis,
        IReadOnlyList<string> authoritative,
        IReadOnlyDictionary<string, string>? excluded = null
    )
    {
        ArgumentNullException.ThrowIfNull(baseOrMarker);
        ArgumentException.ThrowIfNullOrWhiteSpace(axis);
        ArgumentNullException.ThrowIfNull(authoritative);

        var excludedMap = excluded ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var authoritativeSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in authoritative)
        {
            if (!authoritativeSet.Add(value))
            {
                problems.Add($"正本の値 {value} が重複している");
            }
        }

        foreach (var (value, reason) in excludedMap)
        {
            if (!authoritativeSet.Contains(value))
            {
                problems.Add($"除外した値 {value} が正本に無い");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"除外した値 {value} の理由が空");
            }
        }

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (
            var type in LoadableTypes(baseOrMarker.Assembly)
                .Where(type => IsConcreteDerived(type, baseOrMarker))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        )
        {
            var values = type.GetCustomAttributes<MatrixAxisValueAttribute>(inherit: false)
                .Where(attribute => string.Equals(attribute.Axis, axis, StringComparison.Ordinal))
                .Select(attribute => attribute.Value)
                .ToList();
            var typeName = type.FullName ?? type.Name;

            if (values.Count == 0)
            {
                problems.Add($"{typeName} に軸 {axis} の値の属性が無い");
                continue;
            }

            if (values.Count > 1)
            {
                problems.Add($"{typeName} に軸 {axis} の値の属性が {values.Count} 個ある");
                continue;
            }

            var value = values[0];

            if (value is null || !authoritativeSet.Contains(value))
            {
                problems.Add($"{typeName} の値 {value} が正本に無い");
                continue;
            }

            if (excludedMap.ContainsKey(value))
            {
                problems.Add($"{typeName} の値 {value} は除外している");
                continue;
            }

            if (!owners.TryAdd(value, typeName))
            {
                problems.Add(
                    $"値 {value} の派生クラスが重複している（{owners[value]}・{typeName}）"
                );
            }
        }

        foreach (
            var value in authoritative
                .Distinct(StringComparer.Ordinal)
                .Where(value => !owners.ContainsKey(value) && !excludedMap.ContainsKey(value))
        )
        {
            problems.Add($"正本の値 {value} に派生クラスも除外の理由も無い");
        }

        return problems;
    }

    /// <summary>派生クラスと軸の値の対応に問題があれば、すべて並べて赤にする</summary>
    /// <param name="baseOrMarker">基底のクラスか目印のインターフェイス</param>
    /// <param name="axis">軸の名前</param>
    /// <param name="authoritative">正本の値</param>
    /// <param name="excluded">派生を持たない値と、その理由</param>
    public static void DerivedClassesCover(
        Type baseOrMarker,
        string axis,
        IReadOnlyList<string> authoritative,
        IReadOnlyDictionary<string, string>? excluded = null
    )
    {
        var problems = DerivedClassesProblems(baseOrMarker, axis, authoritative, excluded);

        if (problems.Count > 0)
        {
            throw new XunitException(
                $"{baseOrMarker.Name} の派生クラスが軸 {axis} の正本と対応しない:\n  "
                    + string.Join("\n  ", problems)
            );
        }
    }

    /// <summary>
    /// 派生クラスが属性で名乗る軸の値と、そのクラスが実際に使う値の食い違いをすべて並べる（無ければ空）。
    /// 属性が 1 つだけのクラスを調べる（属性の欠け・重複は <see cref="DerivedClassesProblems"/> が名指しする）
    /// </summary>
    /// <param name="baseOrMarker">基底のクラスか目印のインターフェイス</param>
    /// <param name="axis">軸の名前</param>
    /// <param name="actualValue">クラスが実際に使う軸の値を求める（DB・コンテナに触れずに求める。求められなければ null）</param>
    public static IReadOnlyList<string> DeclaredValueMismatches(
        Type baseOrMarker,
        string axis,
        Func<Type, string?> actualValue
    )
    {
        ArgumentNullException.ThrowIfNull(baseOrMarker);
        ArgumentException.ThrowIfNullOrWhiteSpace(axis);
        ArgumentNullException.ThrowIfNull(actualValue);

        var problems = new List<string>();

        foreach (
            var type in LoadableTypes(baseOrMarker.Assembly)
                .Where(type => IsConcreteDerived(type, baseOrMarker))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        )
        {
            var values = type.GetCustomAttributes<MatrixAxisValueAttribute>(inherit: false)
                .Where(attribute => string.Equals(attribute.Axis, axis, StringComparison.Ordinal))
                .Select(attribute => attribute.Value)
                .ToList();

            if (values.Count != 1)
            {
                continue;
            }

            var actual = actualValue(type);

            if (!string.Equals(values[0], actual, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{type.FullName ?? type.Name} は軸 {axis} の値 {values[0]} を名乗るが、実際は {actual ?? "(求められない)"}"
                );
            }
        }

        return problems;
    }

    /// <summary>派生クラスが名乗る軸の値と実際の値が食い違えば、すべて並べて赤にする</summary>
    /// <param name="baseOrMarker">基底のクラスか目印のインターフェイス</param>
    /// <param name="axis">軸の名前</param>
    /// <param name="actualValue">クラスが実際に使う軸の値を求める（DB・コンテナに触れずに求める）</param>
    public static void DeclaredValuesMatch(
        Type baseOrMarker,
        string axis,
        Func<Type, string?> actualValue
    )
    {
        var problems = DeclaredValueMismatches(baseOrMarker, axis, actualValue);

        if (problems.Count > 0)
        {
            throw new XunitException(
                $"{baseOrMarker.Name} の派生クラスが名乗る軸 {axis} の値が実際と食い違う:\n  "
                    + string.Join("\n  ", problems)
            );
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static bool IsConcreteDerived(Type type, Type baseOrMarker)
    {
        if (
            !type.IsClass
            || type.IsAbstract
            || type.IsGenericTypeDefinition
            || type == baseOrMarker
        )
        {
            return false;
        }

        if (!baseOrMarker.IsGenericTypeDefinition)
        {
            return baseOrMarker.IsAssignableFrom(type);
        }

        if (baseOrMarker.IsInterface)
        {
            return type.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == baseOrMarker);
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == baseOrMarker)
            {
                return true;
            }
        }

        return false;
    }
}
