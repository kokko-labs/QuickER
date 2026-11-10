using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>生成器が作らない形の 1 行</summary>
/// <param name="Id">宣言の中で一意の短い Id（小文字・数字・ハイフン）</param>
/// <param name="Shape">作らない形の短い説明</param>
/// <param name="Kind">
/// 種類（<see cref="KnownKind.Accepted"/>＝製品・DB・docs の事実で作らない／<see cref="KnownKind.Pending"/>＝水準を足す区切りがある）
/// </param>
/// <param name="FixedIn">Pending の直す区切り（Accepted は null）</param>
/// <param name="Reason">作らない理由（具体的に書く）</param>
/// <param name="Guard">
/// 受け持つ守り（CLAUDE.md の「ガード：」と同じ書き方。地図の名前・テストのクラスとメソッド・名前に続けた行 Id）。
/// どこでも見ていない形は <see cref="NotGenerated.Unguarded"/>
/// </param>
public sealed record NotGeneratedShape(
    string Id,
    string Shape,
    KnownKind Kind,
    string? FixedIn,
    string Reason,
    string Guard
);

/// <summary>生成器 1 つの作らない形の宣言</summary>
/// <param name="Generator">生成器の名前（型の名前）</param>
/// <param name="EngineParts">
/// 生成器の入力を受け取るエンジンの部分（<see cref="ProtectionNames.EnginePart"/> の形。どちらのアセンブリの台帳の部分でもよい）
/// </param>
/// <param name="Shapes">作らない形</param>
/// <param name="EmptyReason">作らない形が無い理由（<paramref name="Shapes"/> が空のときだけ書く）</param>
public sealed record NotGeneratedDeclaration(
    string Generator,
    IReadOnlyList<string> EngineParts,
    IReadOnlyList<NotGeneratedShape> Shapes,
    string? EmptyReason = null
);

/// <summary>作らない形の行を作る</summary>
public static class NotGenerated
{
    /// <summary>受け持つ守りが無い（どのテストもその形を見ていない）ことの明示の印</summary>
    public const string Unguarded = "(no guard)";

    /// <summary>製品・DB・docs の事実で作らない形（直す区切りを持たない）</summary>
    /// <param name="id">Id</param>
    /// <param name="shape">形</param>
    /// <param name="reason">作らない理由（事実と根拠）</param>
    /// <param name="guard">受け持つ守り（無ければ <see cref="Unguarded"/>）</param>
    public static NotGeneratedShape Accepted(
        string id,
        string shape,
        string reason,
        string guard
    ) => new(id, shape, KnownKind.Accepted, null, reason, guard);

    /// <summary>水準を足す区切りがある形</summary>
    /// <param name="id">Id</param>
    /// <param name="shape">形</param>
    /// <param name="fixedIn">水準を足す区切り</param>
    /// <param name="reason">まだ作らない理由</param>
    /// <param name="guard">受け持つ守り（無ければ <see cref="Unguarded"/>）</param>
    public static NotGeneratedShape Pending(
        string id,
        string shape,
        string fixedIn,
        string reason,
        string guard
    ) => new(id, shape, KnownKind.Pending, fixedIn, reason, guard);
}

/// <summary>
/// 作らない形の宣言の検査：宣言の構造（理由・種類・区切り・Id・受け持つ守りの有無）と、種を使うエンジンの部分がどれも宣言を持つこと
/// </summary>
/// <remarks>
/// <para>
/// 宣言は生成器の隣の <c>{生成器}NotGenerated.cs</c> に置く純粋なデータ（この名前空間の型と BCL だけを参照する）。Integration の宣言は
/// QuickER.Tests にも Compile のリンクで入り、受け持つ守りの照合（地図の名前・テストの名前・行 Id）は QuickER.Tests が両方の宣言について行う。
/// </para>
/// <para>
/// 受け持つ守りの照合の関数は呼び出し側が渡す（<see cref="ReferenceProblems"/>）。照合は CLAUDE.md の照合と同じ関数で、
/// この名前空間を BCL と xUnit だけに保つため。
/// </para>
/// </remarks>
public static class NotGeneratedGuard
{
    /// <summary>宣言のファイルの名前の末尾</summary>
    public const string DeclarationFileSuffix = "NotGenerated.cs";

    /// <summary>種を使うエンジンの目印（テストのソースの本文にこの呼び出しがあれば種を使う）</summary>
    public const string SeedUseMarker = "SeedSource.Resolve(";

    /// <summary>Id の形</summary>
    private static readonly Regex IdPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant
    );

    /// <summary>エンジンの部分の形（ID はエンジンの ID のどれか・部分は大文字で始まる英数字）</summary>
    private static readonly Regex EnginePartPattern = new(
        "^(?<id>[A-Za-z]+)/[A-Z][A-Za-z0-9]*$",
        RegexOptions.CultureInvariant
    );

    /// <summary>ファイルの名前が宣言のファイルか（CLAUDE.md の照合が、行 Id の照合の本文から外す）</summary>
    /// <param name="fileName">ファイルの名前（パスを含まない）</param>
    public static bool IsDeclarationFileName(string fileName) =>
        fileName.EndsWith(DeclarationFileSuffix, StringComparison.Ordinal);

    /// <summary>アセンブリの宣言を集める（静的なプロパティ・フィールドのうち型が <see cref="NotGeneratedDeclaration"/> のもの）</summary>
    /// <param name="assembly">アセンブリ</param>
    /// <returns>宣言を持つ型と宣言（型の完全な名前の序数順）</returns>
    public static IReadOnlyList<(Type Owner, NotGeneratedDeclaration Declaration)> InAssembly(
        Assembly assembly
    )
    {
        ArgumentNullException.ThrowIfNull(assembly);
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        var found = new List<(Type, NotGeneratedDeclaration)>();

        foreach (
            var type in assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal)
        )
        {
            var values = type.GetProperties(flags)
                .Where(property =>
                    property.PropertyType == typeof(NotGeneratedDeclaration)
                    && property.GetIndexParameters().Length == 0
                )
                .Select(property => property.GetValue(null))
                .Concat(
                    // 自動実装のプロパティの裏の field は数えない（プロパティと同じ宣言を 2 回数えてしまう）
                    type.GetFields(flags)
                        .Where(field =>
                            field.FieldType == typeof(NotGeneratedDeclaration)
                            && !field.IsDefined(typeof(CompilerGeneratedAttribute))
                        )
                        .Select(field => field.GetValue(null))
                );

            foreach (var value in values)
            {
                found.Add((type, (NotGeneratedDeclaration)value!));
            }
        }

        return found;
    }

    /// <summary>宣言の構造の問題をすべて並べる（無ければ空）</summary>
    /// <param name="declarations">宣言</param>
    public static IReadOnlyList<string> Problems(
        IReadOnlyList<NotGeneratedDeclaration> declarations
    )
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var problems = new List<string>();
        var generators = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declaration in declarations)
        {
            if (declaration is null)
            {
                problems.Add("null の宣言がある");
                continue;
            }

            var generator = string.IsNullOrWhiteSpace(declaration.Generator)
                ? "(生成器なし)"
                : declaration.Generator;

            if (string.IsNullOrWhiteSpace(declaration.Generator))
            {
                problems.Add($"{generator}: 生成器の名前が空");
            }
            else if (!generators.Add(declaration.Generator))
            {
                problems.Add($"{generator}: 同じ生成器の宣言が 2 つある");
            }

            CheckEngineParts(generator, declaration.EngineParts, problems);
            CheckShapes(generator, declaration, problems);
        }

        return problems;
    }

    /// <summary>
    /// 種を使うエンジンの部分（台帳の部分のうち、テストのソースの本文に <see cref="SeedUseMarker"/> があるもの）を並べる
    /// </summary>
    /// <param name="registry">エンジンの台帳</param>
    /// <param name="readSource">テストのソースの相対パス → 本文</param>
    public static IReadOnlyList<string> SeedUsingParts(
        IReadOnlyList<EngineDescriptor> registry,
        Func<string, string> readSource
    )
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(readSource);

        return
        [
            .. registry
                .Where(descriptor =>
                    descriptor.SourceFiles.Any(path =>
                        readSource(path).Contains(SeedUseMarker, StringComparison.Ordinal)
                    )
                )
                .Select(descriptor => ProtectionNames.EnginePart(descriptor.Id, descriptor.Part)),
        ];
    }

    /// <summary>
    /// 台帳のテストのソースの読み方（<see cref="SeedUsingParts"/> に渡す）：ファイルはその本文、フォルダはその下の C# のソースの本文をつないだもの
    /// （台帳はテストのソースにフォルダも書く）
    /// </summary>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static Func<string, string> SourceReader(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        return path =>
        {
            var full = Path.Combine(repositoryRoot, path);

            return Directory.Exists(full)
                ? string.Join(
                    '\n',
                    Directory
                        .EnumerateFiles(full, "*.cs", SearchOption.AllDirectories)
                        .Order(StringComparer.Ordinal)
                        .Select(File.ReadAllText)
                )
                : File.ReadAllText(full);
        };
    }

    /// <summary>種を使うエンジンの部分のうち、どの宣言の <see cref="NotGeneratedDeclaration.EngineParts"/> にも無いものを並べる</summary>
    /// <param name="declarations">宣言</param>
    /// <param name="seedUsingParts">種を使うエンジンの部分（<see cref="SeedUsingParts"/>）</param>
    public static IReadOnlyList<string> UndeclaredParts(
        IReadOnlyList<NotGeneratedDeclaration> declarations,
        IReadOnlyList<string> seedUsingParts
    )
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(seedUsingParts);
        var declared = declarations
            .Where(declaration => declaration?.EngineParts is not null)
            .SelectMany(declaration => declaration.EngineParts)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. seedUsingParts.Where(part => !declared.Contains(part)).Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>受け持つ守りの照合の問題をすべて並べる（<see cref="NotGenerated.Unguarded"/> の行は照合しない）</summary>
    /// <param name="declarations">宣言</param>
    /// <param name="guardProblems">受け持つ守りの本文 → 照合の問題（CLAUDE.md の照合と同じ関数で作る）</param>
    public static IReadOnlyList<string> ReferenceProblems(
        IReadOnlyList<NotGeneratedDeclaration> declarations,
        Func<string, IReadOnlyList<string>> guardProblems
    )
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(guardProblems);

        return
        [
            .. declarations
                .Where(declaration => declaration?.Shapes is not null)
                .SelectMany(declaration =>
                    declaration
                        .Shapes.Where(shape =>
                            shape is not null
                            && !string.IsNullOrWhiteSpace(shape.Guard)
                            && shape.Guard != NotGenerated.Unguarded
                        )
                        .SelectMany(shape =>
                            guardProblems(shape.Guard)
                                .Select(problem => $"{declaration.Generator}/{shape.Id}: {problem}")
                        )
                ),
        ];
    }

    /// <summary>問題があれば、すべて並べて赤にする</summary>
    /// <param name="title">見出し</param>
    /// <param name="problems">問題</param>
    public static void Verify(string title, IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (problems.Count > 0)
        {
            throw new XunitException($"{title}:\n  " + string.Join("\n  ", problems));
        }
    }

    private static void CheckEngineParts(
        string generator,
        IReadOnlyList<string>? parts,
        List<string> problems
    )
    {
        if (parts is null || parts.Count == 0)
        {
            problems.Add($"{generator}: 入力を受け取るエンジンの部分が無い");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in parts)
        {
            var match = EnginePartPattern.Match(part ?? "");

            if (
                !match.Success
                || !EngineIds.All.Contains(match.Groups["id"].Value, StringComparer.Ordinal)
            )
            {
                problems.Add($"{generator}: エンジンの部分の形でない {part}");
            }
            else if (!seen.Add(part!))
            {
                problems.Add($"{generator}: エンジンの部分 {part} が重複している");
            }
        }
    }

    private static void CheckShapes(
        string generator,
        NotGeneratedDeclaration declaration,
        List<string> problems
    )
    {
        var shapes = declaration.Shapes ?? [];

        if (shapes.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(declaration.EmptyReason))
            {
                problems.Add($"{generator}: 作らない形が無いのに、無い理由が無い");
            }
            else if (FactorInventoryGuard.IsVagueReason(declaration.EmptyReason.Trim()))
            {
                problems.Add(
                    $"{generator}: 作らない形が無い理由が具体的でない（{declaration.EmptyReason}）"
                );
            }
        }
        else if (declaration.EmptyReason is not null)
        {
            problems.Add($"{generator}: 作らない形があるのに、無い理由を持つ");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var shape in shapes)
        {
            if (shape is null)
            {
                problems.Add($"{generator}: null の行がある");
                continue;
            }

            var name = $"{generator}/{shape.Id}";

            if (shape.Id is null || !IdPattern.IsMatch(shape.Id))
            {
                problems.Add($"{name}: Id の形でない（小文字・数字・ハイフン）");
            }
            else if (!ids.Add(shape.Id))
            {
                problems.Add($"{name}: Id が重複している");
            }

            if (string.IsNullOrWhiteSpace(shape.Shape))
            {
                problems.Add($"{name}: 形の説明が空");
            }

            if (
                KnownFailureSet.KindProblem(shape.Kind, shape.FixedIn, shape.Reason) is
                { } kindProblem
            )
            {
                problems.Add($"{name}: {kindProblem}");
            }

            if (string.IsNullOrWhiteSpace(shape.Reason))
            {
                if (shape.Kind != KnownKind.Accepted)
                {
                    problems.Add($"{name}: 理由が空");
                }
            }
            else if (FactorInventoryGuard.IsVagueReason(shape.Reason.Trim()))
            {
                problems.Add($"{name}: 理由が具体的でない（{shape.Reason}）");
            }

            if (string.IsNullOrWhiteSpace(shape.Guard))
            {
                problems.Add(
                    $"{name}: 受け持つ守りが空（どこでも見ていない形は NotGenerated.Unguarded を書く）"
                );
            }
        }
    }
}
