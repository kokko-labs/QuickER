using System.Collections;
using System.Reflection;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 2 つのツールホストの軸（<see cref="TwoHostToolHarness.Axis"/>）が正本と一対一に対応し、ホストを引数に取るテストが
/// 全ホストの行を持つことを確かめる
/// </summary>
/// <remarks>
/// ホストを引数に取る Theory は、行を <c>[InlineData(ToolHostKind.Gui, …)]</c> / <c>[InlineData(ToolHostKind.File, …)]</c> と
/// ホストごとに書いている（ほかの引数との組ごとに 1 行）。ホストを足したのに行を足し忘れる・片方の行だけを消すと、そのホストで
/// その組が流れなくなるので、ほかの引数の組ごとに軸の全ホストの行があることを反射で照合する。行を <c>[MemberData]</c> /
/// <c>[ClassData]</c> で作る Theory も、その行を取り出して同じ照合に掛ける。
/// </remarks>
public sealed class ToolHostAxisGuardTests
{
    [Fact(DisplayName = "2 つのツールホストの軸は作れ、正本の全ホストのアダプタを持つ")]
    public void Axis_IsComplete()
    {
        var axis = TwoHostToolHarness.Axis;

        axis.Adapters.Select(adapter => adapter.AxisValue)
            .Should()
            .Equal(Enum.GetNames<ToolHostKind>());
        axis.Excluded.Should().BeEmpty();
    }

    [Fact(DisplayName = "ホストを引数に取る Theory は、ほかの引数の組ごとに軸の全ホストの行を持つ")]
    public void HostTheories_CoverEveryHostForEachArgumentCombination()
    {
        var hosts = TwoHostToolHarness.Axis.Adapters.Select(adapter => adapter.AxisValue).ToList();
        var theories = HostTheories().ToList();
        var problems = new List<string>();
        var rowCount = 0;

        foreach (var (method, hostIndex) in theories)
        {
            var rows = Rows(method);
            rowCount += rows.Count;

            foreach (
                var group in rows.GroupBy(
                    row =>
                        string.Join(
                            " | ",
                            row.Where((_, index) => index != hostIndex).Select(Describe)
                        ),
                    StringComparer.Ordinal
                )
            )
            {
                var present = group.Select(row => row[hostIndex]?.ToString()).ToHashSet();
                var missing = hosts.Where(host => !present.Contains(host)).ToList();

                if (missing.Count > 0)
                {
                    problems.Add(
                        $"{method.DeclaringType!.FullName}.{method.Name}（{group.Key}）: "
                            + $"{string.Join(", ", missing)} の行が無い"
                    );
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"ホストを引数に取る Theory: {theories.Count} 個（行 {rowCount} 行）"
        );
        CoverageGuard.AtLeast("ホストを引数に取る Theory", theories.Count, 1);
        problems
            .Should()
            .BeEmpty(
                "ホストの軸の値ごとに同じ組の行を書く（書き忘れたホストではその組が流れない）"
            );
    }

    /// <summary>このアセンブリの、<see cref="ToolHostKind"/> を引数に取る Theory（とその引数の位置）</summary>
    private static IEnumerable<(MethodInfo Method, int HostIndex)> HostTheories() =>
        typeof(ToolHostAxisGuardTests)
            .Assembly.GetTypes()
            .SelectMany(type =>
                type.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.Static
                        | BindingFlags.DeclaredOnly
                )
            )
            .Where(method => method.GetCustomAttribute<TheoryAttribute>() is not null)
            .Select(method =>
                (
                    Method: method,
                    HostIndex: Array.FindIndex(
                        method.GetParameters(),
                        parameter => parameter.ParameterType == typeof(ToolHostKind)
                    )
                )
            )
            .Where(theory => theory.HostIndex >= 0);

    /// <summary>Theory の全行（InlineData・MemberData・ClassData）</summary>
    private static List<object?[]> Rows(MethodInfo method)
    {
        var rows = new List<object?[]>();

        foreach (var inline in method.GetCustomAttributes<InlineDataAttribute>())
        {
            rows.Add(inline.Data);
        }

        foreach (var member in method.GetCustomAttributes<MemberDataAttribute>())
        {
            rows.AddRange(MemberRows(method, member));
        }

        foreach (var data in method.GetCustomAttributes<ClassDataAttribute>())
        {
            rows.AddRange(RowsOf(Activator.CreateInstance(data.Class)));
        }

        return rows;
    }

    /// <summary>MemberData が指す静的メンバーの行</summary>
    private static IEnumerable<object?[]> MemberRows(MethodInfo method, MemberDataAttribute member)
    {
        var type = member.MemberType ?? method.DeclaringType!;
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.FlattenHierarchy;

        var value =
            type.GetProperty(member.MemberName, flags)?.GetValue(null)
            ?? type.GetField(member.MemberName, flags)?.GetValue(null)
            ?? type.GetMethod(member.MemberName, flags)!.Invoke(null, member.Arguments);

        return RowsOf(value);
    }

    /// <summary>行の集まり（<c>object[]</c> か <see cref="ITheoryDataRow"/> の並び）を行の配列にする</summary>
    private static IEnumerable<object?[]> RowsOf(object? value)
    {
        foreach (var row in (IEnumerable)value!)
        {
            yield return row switch
            {
                ITheoryDataRow theoryRow => theoryRow.GetData(),
                object?[] array => array,
                _ => throw new InvalidOperationException($"行の形を読めない: {row?.GetType()}"),
            };
        }
    }

    /// <summary>引数の値の表記（グループ化のキー）</summary>
    private static string Describe(object? value) =>
        value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            IEnumerable items => $"[{string.Join(", ", items.Cast<object?>().Select(Describe))}]",
            _ => $"{value.GetType().Name}:{value}",
        };
}
