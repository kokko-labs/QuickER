using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>bool の値の池：真と偽（NULL 許容の bool の列の行に当たる値と、当たらない値）</summary>
public static class BooleanPool
{
    /// <summary>真</summary>
    public const string True = "bool-true";

    /// <summary>偽</summary>
    public const string False = "bool-false";

    /// <summary>池の値（宣言の順）</summary>
    public static IReadOnlyList<PoolValue<bool>> Values { get; } =
    [
        new(True, true, "真の行に当たる値（SQL では 1）"),
        new(False, false, "偽の行に当たる値（SQL では 0。NULL の行はどちらにも当たらない）"),
    ];

    /// <summary>池の値の名前（宣言の順）</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Values.Select(value => value.Name)];

    /// <summary>池の値を名前で引く</summary>
    /// <param name="name">名前</param>
    /// <exception cref="ArgumentException">池に無い名前</exception>
    public static PoolValue<bool> Named(string name) =>
        Values.FirstOrDefault(value => value.Name == name)
        ?? throw new ArgumentException($"bool の値の池に {name} が無い", nameof(name));
}
