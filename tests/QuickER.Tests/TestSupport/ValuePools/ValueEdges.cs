using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>
/// 値の端の水準の名簿と分類：値（と列の相）が、どの水準に当たるかを決める
/// </summary>
/// <remarks>
/// <para>
/// 系統は値の実行時の型で決まる（リスト・配列は要素ごとに分類した和＝IN のパラメータ）。列の相の要る水準（<see cref="ValueEdge.NeedsColumn"/>）は、
/// 相が無いか、水準が読む相（桁・長さ・範囲）が欠けていれば判定しない。値を変える明示の変換の水準（系統 <see cref="ValueFamily.Narrowing"/>）は、
/// 相に変換先（<see cref="ValueColumn.CastTarget"/>）のある数値の観測だけで判定する。
/// </para>
/// <para>
/// 水準の判定式は系統ごとの部分ファイル（<c>ValueEdges.Temporal.cs</c>・<c>ValueEdges.Numeric.cs</c>・<c>ValueEdges.Text.cs</c>）に置く。
/// 名簿は値を受けるエンジン・表の宣言（<see cref="ValuePoolUseDeclarations"/>）と行列の照合（<see cref="ValuePoolUseChecks"/>）が読む。
/// </para>
/// </remarks>
public static partial class ValueEdges
{
    /// <summary>水準 1 つの判定（値は系統の型の値。相の要る水準は相を読む）</summary>
    /// <param name="Edge">水準</param>
    /// <param name="Facet">水準が読む相があるか（相の要らない水準は null）</param>
    /// <param name="Test">値と相がこの水準に当たるか（相の要る水準は <paramref name="Facet"/> が真の相だけを受ける）</param>
    private sealed record EdgeRule(
        ValueEdge Edge,
        Func<ValueColumn, bool>? Facet,
        Func<object, ValueColumn?, bool> Test
    );

    /// <summary>全部の水準の判定（系統の宣言の順）</summary>
    private static readonly EdgeRule[] Rules =
    [
        .. DateTimeRules,
        .. DateTimeOffsetRules,
        .. TimeSpanRules,
        .. DecimalRules,
        .. IntegerRules,
        .. FloatingRules,
        .. StringRules,
        .. BinaryRules,
        .. GuidRules,
        .. BooleanRules,
        .. NarrowingRules,
    ];

    /// <summary>名簿（Id は一意）</summary>
    public static IReadOnlyList<ValueEdge> All { get; } = [.. Rules.Select(rule => rule.Edge)];

    /// <summary>Id で引く水準（名簿に無い Id は null）</summary>
    /// <param name="id">Id</param>
    public static ValueEdge? Find(string id) =>
        All.FirstOrDefault(edge => string.Equals(edge.Id, id, StringComparison.Ordinal));

    /// <summary>値と列の相が当たる水準の Id の集合（null は空）</summary>
    /// <param name="value">値（リスト・配列は要素ごとに分類した和）</param>
    /// <param name="column">列の相（無ければ null）</param>
    public static IReadOnlySet<string> Classify(object? value, ValueColumn? column)
    {
        var hits = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scalar in Scalars(value))
        {
            var family = FamilyOf(scalar);

            if (family is not null)
            {
                AddHits(hits, family.Value, scalar, column);
            }

            if (column?.CastTarget is not null && IsNumeric(scalar))
            {
                AddHits(hits, ValueFamily.Narrowing, scalar, column);
            }
        }

        return hits;
    }

    /// <summary>値の系統の集合（リスト・配列は要素の系統の和。相に変換先のある数値は <see cref="ValueFamily.Narrowing"/> も含む）</summary>
    /// <param name="value">値</param>
    /// <param name="column">列の相（無ければ null）</param>
    public static IReadOnlySet<ValueFamily> FamiliesOf(object? value, ValueColumn? column)
    {
        var families = new HashSet<ValueFamily>();

        foreach (var scalar in Scalars(value))
        {
            if (FamilyOf(scalar) is { } family)
            {
                families.Add(family);
            }

            if (column?.CastTarget is not null && IsNumeric(scalar))
            {
                families.Add(ValueFamily.Narrowing);
            }
        }

        return families;
    }

    /// <summary>値（リスト・配列は要素）が名簿のどの系統にも当たらないか（null と空のリストは当たらないと数えない）</summary>
    /// <param name="value">値</param>
    public static bool IsUnclassified(object? value) =>
        Scalars(value).Any(scalar => FamilyOf(scalar) is null);

    /// <summary>水準が読む相を列の相が持つか（相の要らない水準は相があれば真）</summary>
    /// <param name="edgeId">水準の Id</param>
    /// <param name="column">列の相</param>
    public static bool HasFacet(string edgeId, ValueColumn? column)
    {
        if (column is null)
        {
            return false;
        }

        var rule = Rules.FirstOrDefault(rule => rule.Edge.Id == edgeId);

        return rule is not null && (rule.Facet is null || rule.Facet(column));
    }

    /// <summary>1 つの値（リスト・配列でない）の系統（名簿に無い型は null）</summary>
    /// <param name="scalar">値</param>
    public static ValueFamily? FamilyOf(object scalar) =>
        scalar switch
        {
            DateTime => ValueFamily.DateTime,
            DateTimeOffset => ValueFamily.DateTimeOffset,
            TimeSpan => ValueFamily.TimeSpan,
            decimal => ValueFamily.Decimal,
            sbyte or byte or short or ushort or int or uint or long or ulong => ValueFamily.Integer,
            float or double => ValueFamily.Floating,
            string => ValueFamily.String,
            byte[] => ValueFamily.Binary,
            Guid => ValueFamily.Guid,
            bool => ValueFamily.Boolean,
            _ => null,
        };

    /// <summary>値を 1 つずつの値へ広げる（null は除く・リスト・配列は要素・文字列とバイト列はそのまま）</summary>
    private static IEnumerable<object> Scalars(object? value)
    {
        switch (value)
        {
            case null:
                yield break;

            case string
            or byte[]:
                yield return value;
                yield break;

            case IEnumerable items:
                foreach (var item in items)
                {
                    foreach (var scalar in Scalars(item))
                    {
                        yield return scalar;
                    }
                }

                yield break;

            default:
                yield return value;
                yield break;
        }
    }

    private static bool IsNumeric(object scalar) =>
        FamilyOf(scalar) is ValueFamily.Integer or ValueFamily.Decimal or ValueFamily.Floating;

    private static void AddHits(
        HashSet<string> hits,
        ValueFamily family,
        object scalar,
        ValueColumn? column
    )
    {
        foreach (var rule in Rules.Where(rule => rule.Edge.Family == family))
        {
            if (rule.Facet is not null && (column is null || !rule.Facet(column)))
            {
                continue;
            }

            if (rule.Test(scalar, column))
            {
                hits.Add(rule.Edge.Id);
            }
        }
    }

    /// <summary>相の要らない水準</summary>
    private static EdgeRule Edge<T>(
        string id,
        ValueFamily family,
        string branch,
        Func<T, bool> test
    ) => new(new ValueEdge(id, family, branch, false), null, (value, _) => test((T)value));

    /// <summary>相の要る水準</summary>
    private static EdgeRule ColumnEdge<T>(
        string id,
        ValueFamily family,
        string branch,
        Func<ValueColumn, bool> facet,
        Func<T, ValueColumn, bool> test
    ) =>
        new(
            new ValueEdge(id, family, branch, true),
            facet,
            (value, column) => test((T)value, column!)
        );
}
