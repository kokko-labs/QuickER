using System.IO;
using AwesomeAssertions;
using QuickER.Tests.Docs;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 生成器の作らない形の宣言（<see cref="NotGeneratedDeclaration"/>）の照合：宣言の構造・種を使うエンジンの部分の網羅・受け持つ守りの実在
/// </summary>
/// <remarks>
/// <para>
/// 宣言はこのアセンブリから反射で集める（<see cref="NotGeneratedGuard.InAssembly"/>）。Integration の宣言は Compile のリンクでこのアセンブリにも
/// 入るので、受け持つ守りの照合と、宣言のエンジンの部分が影響の地図に実在することは、ここで両方の宣言について見る。網羅（種を使う部分が宣言を
/// 持つ）は、このアセンブリの台帳の部分だけを見る（Integration の台帳は Integration のガードが見る）。
/// </para>
/// <para>
/// 受け持つ守りは CLAUDE.md の「ガード：」と同じ書き方で、読み方も CLAUDE.md の照合と同じ関数（<see cref="ClaudeMdReferenceTests"/> の
/// <c>ReferencesIn</c>・<c>FindMissingTestNames</c>・<c>FindMissingMapNames</c>・<c>FindForeignRowIds</c>・<c>FindMissingRowIds</c>）を使う。
/// 宣言のファイルは行 Id の照合の本文から外してあるので、宣言に書いた行 Id が宣言そのものに当たって実在に見えることは無い。
/// </para>
/// </remarks>
public sealed class NotGeneratedGuardTests
{
    /// <summary>このアセンブリの宣言（Integration の宣言をリンクで含む）</summary>
    internal static IReadOnlyList<(
        Type Owner,
        NotGeneratedDeclaration Declaration
    )> Found { get; } = NotGeneratedGuard.InAssembly(typeof(NotGeneratedGuardTests).Assembly);

    private static IReadOnlyList<NotGeneratedDeclaration> Declarations { get; } =
    [.. Found.Select(found => found.Declaration)];

    [Fact(DisplayName = "[作らない形] 宣言は 1 つ以上あり、構造の問題が無い")]
    public void Declarations_AreWellFormed()
    {
        Declarations.Should().NotBeEmpty("宣言を反射で見つけること");
        NotGeneratedGuard.Verify(
            "作らない形の宣言が整っていない",
            NotGeneratedGuard.Problems(Declarations)
        );
    }

    [Fact(
        DisplayName = "[作らない形] このアセンブリの台帳の種を使うエンジンの部分は、どれも宣言を持つ"
    )]
    public void SeedUsingParts_HaveDeclarations()
    {
        var seedUsing = NotGeneratedGuard.SeedUsingParts(
            EngineRegistry.All,
            NotGeneratedGuard.SourceReader(EngineRepository.Root())
        );

        seedUsing.Should().Contain(ProtectionNames.EnginePart(EngineIds.Dsl, "SqliteMySql"));
        NotGeneratedGuard
            .UndeclaredParts(Declarations, seedUsing)
            .Should()
            .BeEmpty(
                "種を使うエンジンの部分は生成器の作らない形の宣言を持つ（{生成器}NotGenerated.cs を足す）"
            );
    }

    [Fact(
        DisplayName = "[作らない形] 宣言のエンジンの部分は、影響の地図の守り（両アセンブリの台帳）に実在する"
    )]
    public void EngineParts_ExistInImpactMap()
    {
        var names = ImpactMapSources
            .Protections(EngineRepository.Root())
            .Select(protection => protection.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missing = Declarations
            .SelectMany(declaration =>
                declaration
                    .EngineParts.Where(part => !names.Contains(part))
                    .Select(part => $"{declaration.Generator}: {part}")
            )
            .ToList();

        missing.Should().BeEmpty("宣言のエンジンの部分は台帳の部分の名前で書く");
    }

    [Fact(
        DisplayName = "[作らない形] 宣言を持つ型は {生成器}NotGenerated の名前で、同じ名前のファイルにだけ宣言される"
    )]
    public void DeclarationOwners_LiveInDeclarationFiles()
    {
        var sources = ClaudeMdReferenceTests.Sources.Value;
        var problems = new List<string>();

        foreach (var owner in Found.Select(found => found.Owner).Distinct())
        {
            if (!NotGeneratedGuard.IsDeclarationFileName(owner.Name + ".cs"))
            {
                problems.Add($"{owner.FullName}: 型の名前が NotGenerated で終わらない");
                continue;
            }

            if (!sources.DeclarationFiles.TryGetValue(owner.Name, out var files))
            {
                problems.Add($"{owner.FullName}: 宣言するファイルが tests/ に見つからない");
                continue;
            }

            problems.AddRange(
                files
                    .Where(path =>
                        !string.Equals(
                            Path.GetFileName(path),
                            owner.Name + ".cs",
                            StringComparison.Ordinal
                        )
                    )
                    .Select(path =>
                        $"{owner.FullName}: {owner.Name}.cs でないファイル {path} に宣言がある"
                    )
            );
        }

        problems
            .Should()
            .BeEmpty(
                "宣言のファイルは CLAUDE.md の照合の行 Id の本文から名前で外すので、宣言は {生成器}NotGenerated.cs に置く"
            );
    }

    [Fact(
        DisplayName = "[作らない形] tests/ のすべての宣言のファイルの宣言は、このアセンブリに入っている（Integration の宣言の逆向きのリンクを含む）"
    )]
    public void EveryDeclarationFile_IsCompiledHere()
    {
        var owners = Found.Select(found => found.Owner.Name).ToHashSet(StringComparer.Ordinal);
        var files = DeclarationFilePaths();

        files
            .Should()
            .Contain(path =>
                path.StartsWith("tests/QuickER.Tests.Integration/", StringComparison.Ordinal)
            );
        files
            .Where(path => !owners.Contains(Path.GetFileNameWithoutExtension(path)))
            .Should()
            .BeEmpty(
                "宣言のファイルは QuickER.Tests でコンパイルされ、受け持つ守りが照合される（Integration の宣言は csproj の逆向きのリンクで入る）"
            );
    }

    [Fact(DisplayName = "[作らない形] 宣言のファイルは、CLAUDE.md の照合の行 Id の本文に入らない")]
    public void DeclarationFiles_AreLeftOutOfRowIdText()
    {
        var root = EngineRepository.Root();
        var allText = ClaudeMdReferenceTests.Sources.Value.AllText;

        DeclarationFilePaths()
            .Where(path =>
                allText.Contains(
                    File.ReadAllText(Path.Combine(root, path)),
                    StringComparison.Ordinal
                )
            )
            .Should()
            .BeEmpty("宣言に書いた行 Id が宣言そのものに当たって実在に見えないよう、名前で外す");
    }

    /// <summary>
    /// tests/ の宣言のファイル（リポジトリの根からの相対パス。型を定義する Engines/NotGenerated.cs と bin・obj は除く）
    /// </summary>
    private static List<string> DeclarationFilePaths() =>
        [
            .. SourceFiles
                .Enumerate("tests")
                .Select(entry => entry.RelativePath)
                .Where(path => NotGeneratedGuard.IsDeclarationFileName(Path.GetFileName(path)))
                .Where(path => path != "tests/QuickER.Tests/TestSupport/Engines/NotGenerated.cs"),
        ];

    [Fact(
        DisplayName = "[作らない形] 受け持つ守りに書いた名前と行 Id は実在し、行 Id はその持ち主のソースにある"
    )]
    public void Guards_ReferToExistingProtections()
    {
        var check = GuardChecker();
        NotGeneratedGuard.Verify(
            "作らない形の受け持つ守りが実在しない（CLAUDE.md の「ガード：」と同じ書き方で書く。どこでも見ていなければ NotGenerated.Unguarded）",
            NotGeneratedGuard.ReferenceProblems(Declarations, check)
        );
    }

    /// <summary>受け持つ守りの本文 → 照合の問題（CLAUDE.md の照合と同じ関数で読む）</summary>
    internal static Func<string, IReadOnlyList<string>> GuardChecker()
    {
        var root = EngineRepository.Root();
        var sources = ClaudeMdReferenceTests.Sources.Value;
        var protections = ImpactMapSources.Protections(root);
        var names = protections
            .Select(protection => protection.Name)
            .ToHashSet(StringComparer.Ordinal);
        var owner = ClaudeMdReferenceTests.FollowingRowOwner(protections, sources);

        return guard =>
        {
            var problems = new List<string>();
            var (found, _, unreadable) = ClaudeMdReferenceTests.ReferencesIn(
                guard,
                sources.RowIdPrefixes
            );

            if (found.Count == 0)
            {
                problems.Add("地図の名前もテストの名前も無い");
            }

            problems.AddRange(unreadable.Select(word => $"名前とも行 Id とも読めない語 `{word}`"));
            problems.AddRange(
                ClaudeMdReferenceTests
                    .FindMissingTestNames(guard, sources)
                    .Select(name => $"tests/ に無いテストの名前 {name}")
            );
            problems.AddRange(
                ClaudeMdReferenceTests
                    .FindMissingMapNames(guard, names)
                    .Select(name => $"地図に無い名前 {name}")
            );
            problems.AddRange(
                ClaudeMdReferenceTests
                    .FindForeignRowIds(guard, owner, sources.RowIdPrefixes)
                    .Select(entry => $"持ち主のソースに無い行 {entry}")
            );
            problems.AddRange(
                ClaudeMdReferenceTests
                    .FindMissingRowIds(guard, sources)
                    .Select(id => $"tests/ に無い行 {id}")
            );

            return problems;
        };
    }
}
