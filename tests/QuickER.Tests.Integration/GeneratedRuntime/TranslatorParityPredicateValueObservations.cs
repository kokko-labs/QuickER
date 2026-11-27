using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 式の翻訳の述語の表（<c>table:TranslatorParityPredicates</c>）が作る入力の値の観測：各行の述語の比べる値と、行が比べるシードの行の値
/// </summary>
/// <remarks>
/// <para>
/// 述語は <see cref="ValueExpressionObserver"/> で歩く（列は <c>[Column]</c> の列名から <see cref="TranslatorParityFixtureDefinition"/> の図の
/// 列の型を引く・列の側の値を変える明示の変換は相の変換先）。並び替えの行は述語だけを歩く（キーは値を持たない）。
/// </para>
/// <para>
/// シードは行の対象の表ごとに、基底（<see cref="TranslatorParityRuntimeTestsBase"/>）が入れる行：probes は <c>SeedProbes</c>、整数部の大きい
/// probes の行はそれに <c>WideProbes</c> を足したもの、small_child は <c>CrossTypeChildren</c>、big_parent は <c>WideBigParents</c>。
/// シードは表ごとに 1 回だけ観測する（行ごとに同じ値を数えない）。
/// </para>
/// </remarks>
internal static class TranslatorParityPredicateValueObservations
{
    /// <summary>図の表名 → 列名 → 列の相</summary>
    private static readonly Dictionary<string, Dictionary<string, ValueColumn>> Columns =
        TranslatorParityFixtureDefinition
            .Build()
            .Entities.ToDictionary(
                entity => entity.TableName,
                entity =>
                    entity.Columns.ToDictionary(
                        column => column.Name,
                        column => ValueColumn.FromSqlType(column.DataType),
                        StringComparer.Ordinal
                    ),
                StringComparer.Ordinal
            );

    /// <summary>エンティティのプロパティの列の相（<c>[Table]</c> と <c>[Column]</c> の名前で図を引く。列でなければ null）</summary>
    /// <param name="member">メンバー</param>
    internal static ValueColumn? ColumnOf(MemberInfo member)
    {
        var table = member.DeclaringType?.GetCustomAttribute<TableAttribute>()?.Name;
        var column = member.GetCustomAttribute<ColumnAttribute>()?.Name;

        return
            table is not null
            && column is not null
            && Columns.TryGetValue(table, out var columns)
            && columns.TryGetValue(column, out var facet)
            ? facet
            : null;
    }

    /// <summary>表の全行の比べる値と、比べるシードの行の値</summary>
    internal static IReadOnlyList<ValueObservation> Observe()
    {
        var observations = new List<ValueObservation>();
        var seeded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in TranslatorParityPredicateRows.Table)
        {
            var (predicate, seeds) = row.Input switch
            {
                ProbePredicate probe => (
                    (System.Linq.Expressions.LambdaExpression)probe.Predicate,
                    ("probes", (IEnumerable<object>)TranslatorParityRuntimeTestsBase.SeedProbes())
                ),
                OrderedProbePredicate ordered => (
                    ordered.Predicate,
                    ("probes", TranslatorParityRuntimeTestsBase.SeedProbes())
                ),
                SmallChildPredicate child => (
                    child.Predicate,
                    ("small_child", TranslatorParityRuntimeTestsBase.CrossTypeChildren())
                ),
                BigParentPredicate parent => (
                    parent.Predicate,
                    ("big_parent", TranslatorParityRuntimeTestsBase.WideBigParents())
                ),
                WideProbePredicate wide => (
                    wide.Predicate,
                    (
                        "wide-probes",
                        TranslatorParityRuntimeTestsBase
                            .SeedProbes()
                            .Concat(TranslatorParityRuntimeTestsBase.WideProbes())
                    )
                ),
                _ => throw new InvalidOperationException(
                    $"述語の表の行 {row.Id} の入力の型 {row.Input.GetType().Name} を観測できない"
                ),
            };

            observations.AddRange(
                ValueExpressionObserver.Observe(predicate, ColumnOf, $"行 {row.Id}")
            );

            if (seeded.Add(seeds.Item1))
            {
                observations.AddRange(ObserveSeeds(seeds.Item2, $"シード {seeds.Item1}"));
            }
        }

        return observations;
    }

    /// <summary>シードの行の列の値（<c>[Column]</c> のプロパティだけ）</summary>
    private static IEnumerable<ValueObservation> ObserveSeeds(
        IEnumerable<object> rows,
        string where
    )
    {
        var index = 0;

        foreach (var row in rows)
        {
            index++;

            foreach (
                var property in row.GetType()
                    .GetProperties()
                    .Where(property => property.GetCustomAttribute<ColumnAttribute>() is not null)
            )
            {
                yield return new ValueObservation(
                    property.GetValue(row),
                    ColumnOf(property),
                    $"{where} の {index} 行目の {property.Name}"
                );
            }
        }
    }
}
