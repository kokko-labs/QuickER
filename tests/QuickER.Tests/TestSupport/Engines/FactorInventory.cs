using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>製品の仕様の軸（列挙の全値・型のプロパティ）を、棚卸しの対象として反射で並べたもの</summary>
/// <param name="Name">軸の名前（<see cref="FactorInventoryAssignment"/> の名前と同じ）</param>
/// <param name="Type">軸の型（項目の <see cref="AxisMember.DeclaringType"/> はこの型そのもの）</param>
/// <param name="Members">軸の項目</param>
public sealed record InventoryAxis(string Name, Type Type, IReadOnlyList<AxisMember> Members)
{
    /// <summary>列挙の全値を軸にする（値を足すと項目も増える＝棚卸しが赤になる）</summary>
    /// <typeparam name="TEnum">列挙の型</typeparam>
    /// <param name="name">軸の名前</param>
    public static InventoryAxis Enum<TEnum>(string name)
        where TEnum : struct, System.Enum
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new InventoryAxis(
            name,
            typeof(TEnum),
            System
                .Enum.GetNames<TEnum>()
                .Select(member => new AxisMember(typeof(TEnum), member))
                .ToList()
        );
    }

    /// <summary>型の公開のインスタンスのプロパティ（インデクサーを除く・宣言の順）を軸にする</summary>
    /// <param name="name">軸の名前</param>
    /// <param name="type">プロパティを持つ型</param>
    /// <param name="filter">軸に含めるプロパティ（例：真偽で設定できるものだけ。null ならすべて）</param>
    public static InventoryAxis Properties(
        string name,
        Type type,
        Func<PropertyInfo, bool>? filter = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        return new InventoryAxis(
            name,
            type,
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0)
                .Where(property => filter?.Invoke(property) ?? true)
                .OrderBy(property => property.MetadataToken)
                .Select(property => new AxisMember(type, property.Name))
                .ToList()
        );
    }
}

/// <summary>
/// 棚卸しの除外（どのエンジンの次元表にもまだ水準として載っていない軸の項目と、受け持つエンジン・載せる区切り・理由）
/// </summary>
/// <param name="Axis">軸の名前</param>
/// <param name="Member">項目の名前（列挙の値かプロパティの名前。軸から生成せず文字列で書く＝項目を足すと棚卸しが赤になる）</param>
/// <param name="OwnerEngineId">載せるエンジンの ID（<see cref="EngineIds"/> のどれか）</param>
/// <param name="Stage">載せる区切り（<see cref="FactorInventoryGuard.Stages"/> のどれか）</param>
/// <param name="Reason">まだ載っていない理由（具体的に書く。仮置きの文は断る）</param>
public sealed record InventoryExclusion(
    string Axis,
    string Member,
    string OwnerEngineId,
    string Stage,
    string Reason
);

/// <summary>
/// 因子の棚卸しの検査：製品の仕様の軸のすべての項目が、どれかのエンジンの次元表の Source（<see cref="Level.Source"/>・
/// <see cref="Factor.Source"/>）に載っているか、理由つきで除外されているかの、ちょうど一方であること
/// </summary>
/// <remarks>
/// <para>
/// 項目は（軸の型、名前）で照合する。<see cref="AxisMember.DeclaringType"/> が軸の型と同じ <see cref="Type"/> でなければ載ったとみなさない
/// （生成フィクスチャの写しと製品の型のように、名前が同じでも別の型は別の項目）。
/// </para>
/// <para>
/// 赤にする問題：載っても除外されてもいない項目・載ったのに除外にも残る項目・知らない軸や軸に無い項目を指す除外・
/// 重複した除外・整っていない除外（知らないエンジン・知らない区切り・空や仮置きの理由）・同じ名前の軸の重複・
/// 軸の型と違う型の項目。
/// </para>
/// <para>
/// 列挙を全数で流すエンジン（水準もテストのループも同じ <c>Enum.GetValues</c> から作る）では、列挙に足した値は自動で水準になって
/// 実際に流れるので、この検査は緑のままで、その値の組が出たかは網羅の計数（<see cref="CoverageCounter"/>）が見る。
/// 水準を全数から作ってよいのは、テストのループや生成器の選び方も同じ列挙から作るときに限る。
/// </para>
/// </remarks>
public static class FactorInventoryGuard
{
    /// <summary>
    /// 除外に書ける区切り（P2＝エンジンの次元表へ因子・水準を足す・P3＝単体のテストをエンジンへ移す・P4＝その後の区切り）
    /// </summary>
    public static IReadOnlyList<string> Stages { get; } = ["P2", "P3", "P4"];

    /// <summary>理由として断る仮置きの文</summary>
    private static readonly string[] PlaceholderReasons = ["後で", "TODO", "TBD"];

    /// <summary>理由の最短の長さ（これより短い理由は具体的でないとみなす）</summary>
    private const int MinimumReasonLength = 10;

    /// <summary>棚卸しの問題をすべて並べる（無ければ空）</summary>
    /// <param name="axes">製品の仕様の軸</param>
    /// <param name="registry">エンジンの台帳（このアセンブリのもの）</param>
    /// <param name="exclusions">除外</param>
    public static IReadOnlyList<string> Problems(
        IReadOnlyList<InventoryAxis> axes,
        IReadOnlyList<EngineDescriptor> registry,
        IReadOnlyList<InventoryExclusion> exclusions
    )
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(exclusions);

        var problems = new List<string>();
        var sources = Sources(registry);
        var axisByName = new Dictionary<string, InventoryAxis>(StringComparer.Ordinal);

        foreach (var axis in axes)
        {
            if (!axisByName.TryAdd(axis.Name, axis))
            {
                problems.Add($"軸 {axis.Name} が重複している");
                continue;
            }

            foreach (var member in axis.Members.Where(member => member.DeclaringType != axis.Type))
            {
                problems.Add(
                    $"{axis.Name}.{member.Member}: 項目の型 {member.DeclaringType.FullName} が軸の型 {axis.Type.FullName} と違う"
                );
            }
        }

        var excluded = new Dictionary<(string Axis, string Member), InventoryExclusion>();

        foreach (var exclusion in exclusions)
        {
            var name = $"除外 {exclusion.Axis}.{exclusion.Member}";
            problems.AddRange(Validate(exclusion).Select(problem => $"{name}: {problem}"));

            if (!excluded.TryAdd((exclusion.Axis, exclusion.Member), exclusion))
            {
                problems.Add($"{name}: 同じ項目の除外が重複している");
            }

            if (!axisByName.TryGetValue(exclusion.Axis, out var axis))
            {
                problems.Add($"{name}: 知らない軸 {exclusion.Axis}");
                continue;
            }

            if (
                !axis.Members.Any(member =>
                    string.Equals(member.Member, exclusion.Member, StringComparison.Ordinal)
                )
            )
            {
                problems.Add($"{name}: 軸 {exclusion.Axis} に項目 {exclusion.Member} が無い");
            }
        }

        foreach (var axis in axisByName.Values)
        {
            foreach (var member in axis.Members.Where(member => member.DeclaringType == axis.Type))
            {
                var sourced = sources.Contains((axis.Type, member.Member));
                var isExcluded = excluded.ContainsKey((axis.Name, member.Member));

                if (!sourced && !isExcluded)
                {
                    problems.Add(
                        $"{axis.Name}.{member.Member}: どのエンジンの次元表の Source にも無く、除外の一覧にも無い（次元表の水準・因子の Source に載せるか、受け持つエンジン・区切り・理由つきで除外する）"
                    );
                }
                else if (sourced && isExcluded)
                {
                    problems.Add(
                        $"{axis.Name}.{member.Member}: エンジンの次元表の Source に載ったので除外の一覧から消す"
                    );
                }
            }
        }

        return problems;
    }

    /// <summary>棚卸しに問題があれば、すべて並べて赤にする</summary>
    /// <param name="axes">製品の仕様の軸</param>
    /// <param name="registry">エンジンの台帳（このアセンブリのもの）</param>
    /// <param name="exclusions">除外</param>
    public static void Verify(
        IReadOnlyList<InventoryAxis> axes,
        IReadOnlyList<EngineDescriptor> registry,
        IReadOnlyList<InventoryExclusion> exclusions
    )
    {
        var problems = Problems(axes, registry, exclusions);

        if (problems.Count > 0)
        {
            throw new XunitException(
                $"因子の棚卸しが整っていない（{problems.Count} 件）:\n  "
                    + string.Join("\n  ", problems)
            );
        }
    }

    /// <summary>台帳の次元表の Source（水準と因子）を（型、名前）の集まりにする</summary>
    private static HashSet<(Type Type, string Member)> Sources(
        IReadOnlyList<EngineDescriptor> registry
    )
    {
        var sources = new HashSet<(Type, string)>();

        foreach (var factor in registry.SelectMany(descriptor => descriptor.Dimensions.Factors))
        {
            if (factor.Source is { } factorSource)
            {
                sources.Add((factorSource.DeclaringType, factorSource.Member));
            }

            foreach (var level in factor.Levels)
            {
                if (level.Source is { } levelSource)
                {
                    sources.Add((levelSource.DeclaringType, levelSource.Member));
                }
            }
        }

        return sources;
    }

    /// <summary>除外 1 件の整合（軸と項目の照合は呼び出し側）</summary>
    private static IEnumerable<string> Validate(InventoryExclusion exclusion)
    {
        if (string.IsNullOrWhiteSpace(exclusion.Axis))
        {
            yield return "軸の名前が空";
        }

        if (string.IsNullOrWhiteSpace(exclusion.Member))
        {
            yield return "項目の名前が空";
        }

        if (!EngineIds.All.Contains(exclusion.OwnerEngineId, StringComparer.Ordinal))
        {
            yield return $"知らないエンジンの ID {exclusion.OwnerEngineId}";
        }

        if (string.IsNullOrWhiteSpace(exclusion.Stage))
        {
            yield return "区切りが空";
        }
        else if (!Stages.Contains(exclusion.Stage, StringComparer.Ordinal))
        {
            yield return $"知らない区切り {exclusion.Stage}（{string.Join("・", Stages)} のどれか）";
        }

        var reason = exclusion.Reason?.Trim() ?? "";

        if (reason.Length == 0)
        {
            yield return "理由が空";
        }
        else if (
            PlaceholderReasons.Contains(reason, StringComparer.OrdinalIgnoreCase)
            || reason.Length < MinimumReasonLength
        )
        {
            yield return $"理由「{reason}」が具体的でない（{MinimumReasonLength} 文字以上で、まだ載っていない事情を書く）";
        }
    }
}

/// <summary>
/// 製品の仕様の軸（設計 2.9 の 13 軸）を、どのアセンブリの棚卸しが検査するかの表
/// （各アセンブリの棚卸しのテストは、自分の軸の名前がこの表とちょうど一致することを表明する＝2 回も 0 回も検査しない）
/// </summary>
/// <remarks>
/// 設計 2.9 の「ConflictPolicy」は実在の列挙の名前 <c>SyncConflictPolicy</c> で書く。
/// 同期の軸（SyncConflictReason など）を Integration が持つのは、QuickER.Tests.Integration が QuickER.Runtime.Sync を参照せず、
/// エンジンの次元表が生成フィクスチャの写し（<c>QuickER.Tests.GeneratedSyncModelFixture</c>）の型を Source にしているため。
/// </remarks>
public static class FactorInventoryAssignment
{
    /// <summary>QuickER.Tests のアセンブリの名前</summary>
    public const string Tests = "QuickER.Tests";

    /// <summary>QuickER.Tests.Integration のアセンブリの名前</summary>
    public const string Integration = "QuickER.Tests.Integration";

    /// <summary>軸の名前と、検査するアセンブリ（設計 2.9 の並び）</summary>
    public static IReadOnlyList<(string Axis, string Assembly)> Assignments { get; } =
    [
        ("SyncDialectCapabilities", Integration),
        ("SyncConflictReason", Integration),
        ("SyncMode", Integration),
        ("SyncConflictPolicy", Integration),
        ("SyncLocalRowChangeReason", Integration),
        ("SyncOptions", Integration),
        ("ConcurrencyMode", Integration),
        ("SaveOperation", Integration),
        ("ForeignKeyReferentialAction", Tests),
        ("RelationshipType", Tests),
        ("CodeGenerationOptions", Tests),
        ("CanonicalTypeKind", Tests),
        ("SchemaImportWarningKind", Integration),
    ];

    /// <summary>アセンブリが検査する軸の名前（表の並び）</summary>
    /// <param name="assembly"><see cref="Tests"/> か <see cref="Integration"/></param>
    /// <exception cref="ArgumentException">知らないアセンブリ</exception>
    public static IReadOnlyList<string> AxesFor(string assembly)
    {
        if (assembly is not (Tests or Integration))
        {
            throw new ArgumentException(
                $"知らないアセンブリ {assembly}（{Tests} か {Integration}）",
                nameof(assembly)
            );
        }

        return Assignments
            .Where(pair => string.Equals(pair.Assembly, assembly, StringComparison.Ordinal))
            .Select(pair => pair.Axis)
            .ToList();
    }
}
