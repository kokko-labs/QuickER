using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成されたインメモリのシーダー（<c>InMemorySampleData.Seed</c>）が投入したストアについて、
/// 主キーの重複が無い・一意制約の重複が無い・外部キーが親に実在する、の 3 つを反射で確かめるヘルパー。
/// </summary>
/// <remarks>
/// 生成物の型はフィクスチャ（生成物の名前空間）ごとに別物なので、型名・属性名で引く。
/// 外部キーは親への参照ナビゲーションに付く <c>[NavigationReference]</c>（単一列）から読む。
/// </remarks>
internal static class SeededSampleDataInvariants
{
    /// <summary>名前空間の生成物でストアをシードし、違反の一覧とエンティティごとの行数を返す</summary>
    /// <param name="assembly">生成物を含むアセンブリ</param>
    /// <param name="namespaceName">生成物の名前空間（<c>InMemorySampleData</c> と同じ）</param>
    public static (
        IReadOnlyList<string> Violations,
        IReadOnlyDictionary<string, int> RowCounts
    ) Check(Assembly assembly, string namespaceName)
    {
        var types = assembly.GetTypes().Where(t => t.Namespace == namespaceName).ToList();
        var storeType = types.Single(t => t.Name == "InMemoryDataStore");
        var seederType = types.Single(t => t.Name == "InMemorySampleData");

        var store = Activator.CreateInstance(storeType)!;
        seederType.GetMethod("Seed")!.Invoke(null, [store]);

        var snapshot = storeType.GetMethod("Snapshot")!;
        var entityTypes = types
            .Where(t =>
                t.IsClass && !t.IsAbstract && t.GetCustomAttribute<TableAttribute>() is not null
            )
            .ToList();

        var rowsByType = entityTypes.ToDictionary(
            t => t,
            t =>
                ((IEnumerable)snapshot.MakeGenericMethod(t).Invoke(store, null)!)
                    .Cast<object>()
                    .ToList()
        );

        var violations = new List<string>();

        foreach (var (type, rows) in rowsByType)
        {
            var keyProperties = type.GetProperties()
                .Where(p => p.GetCustomAttribute<KeyAttribute>() is not null)
                .ToArray();

            if (keyProperties.Length > 0)
            {
                ReportDuplicates(violations, type, "primary key", rows, keyProperties);
            }

            foreach (
                var constraint in type.GetCustomAttributes()
                    .Where(a => a.GetType().Name == "UniqueConstraintAttribute")
            )
            {
                var names = (string[])
                    constraint.GetType().GetProperty("PropertyNames")!.GetValue(constraint)!;
                ReportDuplicates(
                    violations,
                    type,
                    $"UNIQUE ({string.Join(", ", names)})",
                    rows,
                    names.Select(name => type.GetProperty(name)!).ToArray()
                );
            }

            foreach (var navigation in type.GetProperties())
            {
                var reference = navigation
                    .GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "NavigationReferenceAttribute");

                if (reference is null || !(bool)Read(reference, "IsParentReference")!)
                {
                    continue;
                }

                var parentType = entityTypes.Single(t =>
                    t.GetCustomAttribute<TableAttribute>()!.Name
                    == (string)Read(reference, "PrincipalTable")!
                );
                var parentColumn = ColumnProperty(
                    parentType,
                    (string)Read(reference, "PrincipalColumn")!
                );
                var childColumn = ColumnProperty(type, (string)Read(reference, "DependentColumn")!);
                var parentValues = rowsByType[parentType]
                    .Select(row => Normalize(parentColumn.GetValue(row)))
                    .Where(value => value is not null)
                    .ToHashSet();

                foreach (var row in rows)
                {
                    var value = Normalize(childColumn.GetValue(row));

                    if (value is not null && !parentValues.Contains(value))
                    {
                        violations.Add(
                            $"{type.Name}.{childColumn.Name} = {value} refers to no {parentType.Name}.{parentColumn.Name}"
                        );
                    }
                }
            }
        }

        return (
            violations,
            rowsByType.ToDictionary(pair => pair.Key.Name, pair => pair.Value.Count)
        );
    }

    /// <summary>値の組が NULL を含まない行どうしで重複していれば違反へ足す</summary>
    private static void ReportDuplicates(
        List<string> violations,
        Type type,
        string what,
        List<object> rows,
        PropertyInfo[] properties
    )
    {
        var seen = new HashSet<string>();

        foreach (var row in rows)
        {
            var values = properties.Select(p => Normalize(p.GetValue(row))).ToArray();

            if (values.Any(value => value is null))
            {
                continue;
            }

            var tuple = string.Join("\u001f", values);

            if (!seen.Add(tuple))
            {
                violations.Add($"{type.Name} {what} repeats ({string.Join(", ", values)})");
            }
        }
    }

    /// <summary>列名から [Column] のプロパティを引く</summary>
    private static PropertyInfo ColumnProperty(Type type, string columnName) =>
        type.GetProperties()
            .Single(p => p.GetCustomAttribute<ColumnAttribute>()?.Name == columnName);

    /// <summary>属性のプロパティを読む</summary>
    private static object? Read(Attribute attribute, string name) =>
        attribute.GetType().GetProperty(name)!.GetValue(attribute);

    /// <summary>比較の形へ写す（値オブジェクトは中身・バイト列は 16 進・数値は 10 進の文字列）</summary>
    private static string? Normalize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.GetType().GetInterfaces().Any(i => i.Name == "IValueObjectCore"))
        {
            value = value.GetType().GetProperty("Value")!.GetValue(value);
        }

        return value switch
        {
            null => null,
            byte[] bytes => Convert.ToHexString(bytes),
            IFormattable formattable => formattable.ToString(
                null,
                System.Globalization.CultureInfo.InvariantCulture
            ),
            _ => value.ToString(),
        };
    }
}
