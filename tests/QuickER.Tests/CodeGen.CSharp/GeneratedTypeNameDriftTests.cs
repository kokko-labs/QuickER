using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成コードの型名の名簿（<see cref="GeneratedFixedTypeNames"/> と、図から決まる <c>GeneratedTypeNames</c>）が、
/// 実際の生成出力と一致し続けることを表明するドリフトガード。
/// </summary>
/// <remarks>
/// <para>
/// 名簿は、射影 DTO の型名が生成コードの型名と衝突する定義を生成前に止める入力。名簿が実際の出力より狭いと、
/// その名前の DTO は診断ゼロでコンパイルできない出力になる（<c>Task</c> のように、生成コードが単純名で使う型を
/// 隠す場合は、無関係なメソッドまで壊れる）。ビルドも型検査も検出できないので、次の 3 つを実コンパイルの結果で表明する。
/// </para>
/// <list type="number">
///   <item>ランタイムのパッケージ用ソース 7 本が宣言する型名は <see cref="GeneratedFixedTypeNames.Runtime"/> と完全一致する。</item>
///   <item>
///     コミット済みの全フィクスチャ（全機能を網羅する生成物）が宣言する型名は、ランタイム・固定の生成物・
///     テーブルと列から決まる名前（射影 DTO を除く）のどれかに収まり、固定の生成物の名簿に載せた名前はどれかの
///     フィクスチャに実在する。
///   </item>
///   <item>
///     フィクスチャの意味モデルで、単純名として参照された外部（ソース外）の型名は
///     <see cref="GeneratedFixedTypeNames.External"/> に収まり、載せた名前はどれかで実際に参照されている。
///   </item>
/// </list>
/// </remarks>
public sealed class GeneratedTypeNameDriftTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly string[] RuntimePackageSources =
    [
        "src/QuickER.Runtime/Runtime.g.cs",
        "src/QuickER.Runtime.SqlServer/Runtime.SqlServer.g.cs",
        "src/QuickER.Runtime.Sqlite/Runtime.Sqlite.g.cs",
        "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
        "src/QuickER.Runtime.InMemory/Runtime.InMemory.g.cs",
        "src/QuickER.Runtime.AspNetCore/Runtime.AspNetCore.g.cs",
        "src/QuickER.Runtime.Sync/Runtime.Sync.g.cs",
    ];

    /// <summary>リポジトリ直下（QuickER.slnx のある場所）を、テストアセンブリの位置から遡って解決する</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("リポジトリ直下（QuickER.slnx）が見つかりません。");
    }

    /// <summary>コミット済みフィクスチャを、フィクスチャごと（本体と RemoteServer をまとめて）に読む</summary>
    private static IEnumerable<(
        string Name,
        (string FileName, string Content)[] Files
    )> Fixtures() =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot(), "tests", "QuickER.Tests", "GeneratedFixture"),
                "*.g.cs"
            )
            .GroupBy(path => Path.GetFileName(path).Split('.')[0])
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
                (
                    group.Key,
                    group.Select(path => (Path.GetFileName(path), File.ReadAllText(path))).ToArray()
                )
            );

    /// <summary>名前空間直下に宣言された型の名前（入れ子の型は含めない）</summary>
    private static IEnumerable<(string Name, bool IsProjectionDto)> TopLevelTypes(
        SyntaxNode root
    ) =>
        root.DescendantNodes()
            .Where(node =>
                node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
                && node.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax
            )
            .Select(node =>
                node switch
                {
                    BaseTypeDeclarationSyntax type => (
                        type.Identifier.Text,
                        type.GetLeadingTrivia()
                            .ToFullString()
                            .Contains("Projection DTO for the named query")
                    ),
                    DelegateDeclarationSyntax @delegate => (@delegate.Identifier.Text, false),
                    _ => throw new InvalidOperationException(),
                }
            );

    [Fact(
        DisplayName = "ランタイムのパッケージ用ソースが宣言する型名は GeneratedFixedTypeNames.Runtime と完全一致する"
    )]
    public void Runtime_MatchesPackageSources()
    {
        var declared = RuntimePackageSources
            .SelectMany(relative =>
                TopLevelTypes(
                        CSharpSyntaxTree
                            .ParseText(
                                File.ReadAllText(Path.Combine(RepoRoot(), relative)),
                                cancellationToken: Ct
                            )
                            .GetRoot(Ct)
                    )
                    .Select(type => type.Name)
            )
            .ToHashSet(StringComparer.Ordinal);

        Report("Runtime", declared, GeneratedFixedTypeNames.Runtime);
    }

    [Fact(
        DisplayName = "全フィクスチャが宣言する型名は、ランタイム・固定の生成物・テーブルと列から決まる名前に収まる"
    )]
    public void DeclaredTypes_AreCoveredByTheRegistries()
    {
        var uncovered = new SortedSet<string>(StringComparer.Ordinal);
        var seenFixed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, files) in Fixtures())
        {
            var compilation = GeneratedCodeCompiler.CreateCompilation(files, "Survey." + name);
            var tables = new HashSet<string>(StringComparer.Ordinal);
            var columns = new HashSet<string>(StringComparer.Ordinal);

            foreach (var tree in compilation.SyntaxTrees)
            {
                var text = tree.GetText(Ct).ToString();

                foreach (Match match in Regex.Matches(text, @"\[Table\(""([^""]+)""\)\]"))
                {
                    tables.Add(match.Groups[1].Value);
                }

                foreach (Match match in Regex.Matches(text, @"\[Column\(""([^""]+)""\)\]"))
                {
                    columns.Add(match.Groups[1].Value);
                }
            }

            // テーブルと列は、生成物の [Table] / [Column] 属性から復元する（図の定義は使わない）
            var derived = GeneratedTypeNames.Collect(
                tables.Select(table => new Entity
                {
                    TableName = table,
                    Columns = columns.Select(column => new Column { Name = column }).ToList(),
                })
            );

            foreach (var tree in compilation.SyntaxTrees)
            {
                foreach (var (typeName, isProjectionDto) in TopLevelTypes(tree.GetRoot(Ct)))
                {
                    if (isProjectionDto)
                    {
                        continue;
                    }

                    if (GeneratedFixedTypeNames.Runtime.Contains(typeName))
                    {
                        continue;
                    }

                    if (GeneratedFixedTypeNames.FixedGenerated.Contains(typeName))
                    {
                        seenFixed.Add(typeName);

                        continue;
                    }

                    if (!derived.Contains(typeName))
                    {
                        uncovered.Add(typeName);
                    }
                }
            }
        }

        Report("FixedGenerated（不足）", uncovered, new HashSet<string>());
        Report(
            "FixedGenerated（実在しない）",
            new HashSet<string>(),
            GeneratedFixedTypeNames.FixedGenerated.Except(seenFixed).ToHashSet()
        );
    }

    [Fact(
        DisplayName = "フィクスチャが単純名で参照する外部の型名は GeneratedFixedTypeNames.External に収まる"
    )]
    public void ReferencedExternalTypes_AreCoveredByExternal()
    {
        var referenced = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (name, files) in Fixtures())
        {
            var compilation = GeneratedCodeCompiler.CreateCompilation(files, "Survey." + name);

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);

                foreach (var node in tree.GetRoot(Ct).DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var info = model.GetSymbolInfo(node, Ct);
                    var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                    var type = symbol switch
                    {
                        INamedTypeSymbol named => named,
                        // 属性の使用（[Table] など）は構築子を指す。型は Attribute を省いた綴りでも参照される
                        IMethodSymbol { MethodKind: MethodKind.Constructor } ctor =>
                            ctor.ContainingType,
                        _ => null,
                    };

                    if (type is null || type.Locations.Any(location => location.IsInSource))
                    {
                        continue;
                    }

                    referenced.Add(node.Identifier.Text);
                    referenced.Add(type.Name);
                }
            }
        }

        Report("External", referenced, GeneratedFixedTypeNames.External);
    }

    /// <summary>実際の集合と名簿の差を、足りない名前・余分な名前として一覧で示して落とす</summary>
    private static void Report(
        string label,
        IEnumerable<string> actual,
        IEnumerable<string> registry
    )
    {
        var missing = actual.Except(registry).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var extra = registry.Except(actual).OrderBy(n => n, StringComparer.Ordinal).ToList();

        if (missing.Count == 0 && extra.Count == 0)
        {
            return;
        }

        throw new Xunit.Sdk.XunitException(
            $"[{label}] 名簿が実際の生成出力と一致しない。"
                + $"{Environment.NewLine}名簿に足りない({missing.Count}): {string.Join(", ", missing)}"
                + $"{Environment.NewLine}名簿にあるが実在しない({extra.Count}): {string.Join(", ", extra)}"
        );
    }
}
