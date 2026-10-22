using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Platform;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ソースを走査するガードが、条件コンパイル（<c>#if</c> / <c>#elif</c> / <c>#else</c>）のどの分岐も読むことを固定する
/// （<see cref="ConditionalSourceParse"/> と、<see cref="SourceSymbolScan"/> の変種のコンパイル）
/// </summary>
/// <remarks>
/// 1 通りの記号でパースすると、外れる分岐の中身はトリビアになり、そこに書いた禁止の呼び出しはどのガードからも見えない。
/// ここでは、分岐の中の呼び出しが見えること・分岐の外の呼び出しを組み合わせの数だけ数えないこと・実ファイルの組み合わせの数を表明する。
/// </remarks>
public sealed class ConditionalSourceParseTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// 分岐の中に禁止の呼び出しを置いたプローブ（src の記号＝DEBUG と QUICKER_* がすべて定義された構成では、どちらも外れる分岐）
    /// </summary>
    private const string BranchProbe = """
        using System.Diagnostics;
        using System.IO;

        internal static class Probe
        {
            public static void Run(string path)
            {
                File.AppendAllText(path, "shared");
        #if !DEBUG
                File.WriteAllText(path, "release");
        #endif
        #if QUICKER_AI_MODULES
                File.AppendAllText(path, "with modules");
        #else
                Process.Start(path);
        #endif
            }
        }
        """;

    /// <summary>構文木の中の、名前で呼んだメソッドの呼び出しの名前（受け手を除く）</summary>
    private static List<string> CalledNames(IEnumerable<SyntaxNode> nodes) =>
        nodes
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax member
                    ? member.Name.Identifier.ValueText
                    : invocation.Expression.ToString()
            )
            .ToList();

    [Fact(
        DisplayName = "#if !DEBUG の中と QUICKER_* の #else の中の呼び出しは、1 通りのパースでは見えず、すべての組み合わせのパースで見える"
    )]
    public void BranchCalls_AreVisibleOnlyAcrossVariants()
    {
        var options = SourceSymbolScan.ParseOptions;

        // 対照：従来の 1 通りのパース（src の記号）は、外れる分岐の呼び出しを見落とす
        var single = CSharpSyntaxTree.ParseText(BranchProbe, options, cancellationToken: Ct);
        CalledNames(single.GetRoot(Ct).DescendantNodes())
            .Should()
            .Equal("AppendAllText", "AppendAllText");

        var variants = ConditionalSourceParse.Parse(BranchProbe, options, "Probe.cs", Ct);

        variants.Should().HaveCount(4, "記号は DEBUG と QUICKER_AI_MODULES の 2 つ＝4 通り");
        variants[0].Options.Should().BeSameAs(options, "先頭は基準の設定の構文木");
        CalledNames(ConditionalSourceParse.DescendantNodes(variants, Ct))
            .Should()
            .BeEquivalentTo(["AppendAllText", "WriteAllText", "AppendAllText", "Start"]);
    }

    [Fact(
        DisplayName = "記号なしの既定の設定でも、#if DEBUG の中の呼び出しは、すべての組み合わせのパースで見える"
    )]
    public void DebugBranchCalls_AreVisibleWithDefaultOptions()
    {
        const string source = """
            internal static class Probe
            {
                public static void Run()
                {
                    Shared();
            #if DEBUG
                    Forbidden();
            #endif
                }

                private static void Shared() { }

                private static void Forbidden() { }
            }
            """;

        // 対照：従来の既定の設定（記号なし）のパースは、#if DEBUG の中を見落とす
        CalledNames(
                CSharpSyntaxTree
                    .ParseText(source, cancellationToken: Ct)
                    .GetRoot(Ct)
                    .DescendantNodes()
            )
            .Should()
            .Equal("Shared");

        CalledNames(
                ConditionalSourceParse.DescendantNodes(
                    ConditionalSourceParse.Parse(source, cancellationToken: Ct),
                    Ct
                )
            )
            .Should()
            .Equal("Shared", "Forbidden");
    }

    [Fact(DisplayName = "無効な分岐の中の入れ子の #if の記号も、組み合わせに入る")]
    public void NestedConditionInInactiveBranch_IsCollected()
    {
        const string source = """
            internal static class Probe
            {
            #if OUTER
            #if INNER
                public static void Inner() { }
            #elif OTHER
                public static void Other() { }
            #endif
            #endif
            }
            """;
        var variants = ConditionalSourceParse.Parse(source, cancellationToken: Ct);

        ConditionalSourceParse
            .ConditionalSymbols(variants[0], Ct)
            .Should()
            .Equal("INNER", "OTHER", "OUTER");
        variants.Should().HaveCount(8);
        ConditionalSourceParse
            .DescendantNodes(variants, Ct)
            .OfType<MethodDeclarationSyntax>()
            .Select(method => method.Identifier.ValueText)
            .Should()
            .BeEquivalentTo(["Inner", "Other"]);
    }

    [Fact(
        DisplayName = "記号が上限の 6 個なら 64 通り、7 個ならファイル名を添えて例外にする（黙って間引かない）"
    )]
    public void SymbolCap_ThrowsWithFileName()
    {
        static string Source(int count) =>
            "internal static class Probe\n{\n"
            + string.Concat(
                Enumerable
                    .Range(0, count)
                    .Select(index =>
                        $"#if S{index}\n    public static void M{index}() {{ }}\n#endif\n"
                    )
            )
            + "}\n";

        ConditionalSourceParse
            .Parse(Source(6), path: "Six.cs", cancellationToken: Ct)
            .Should()
            .HaveCount(64);

        var parse = () =>
            ConditionalSourceParse.Parse(Source(7), path: "Seven.cs", cancellationToken: Ct);

        parse.Should().Throw<InvalidOperationException>().WithMessage("Seven.cs:*7 個*");
    }

    /// <summary>リポジトリのルートからの相対パスのソースを読む</summary>
    private static string ReadSource(string relative) =>
        File.ReadAllText(Path.Combine(NeutralResxFiles.FindRepositoryRoot(), relative));

    [Theory(
        DisplayName = "実ファイルの組み合わせの数：UpdateFeed.cs は 2・FeatureModuleCatalog.cs は 8・条件コンパイルの無いファイルは 1"
    )]
    [InlineData("src/QuickER.Gui/Services/UpdateFeed.cs", 2)]
    [InlineData("src/QuickER.Gui/FeatureModuleCatalog.cs", 8)]
    [InlineData("src/QuickER.Platform/AtomicFile.cs", 1)]
    public void RealFiles_HaveExpectedVariantCounts(string relative, int expected)
    {
        var text = ReadSource(relative);

        ConditionalSourceParse.Parse(text, cancellationToken: Ct).Should().HaveCount(expected);
        ConditionalSourceParse
            .Parse(text, SourceSymbolScan.ParseOptions, cancellationToken: Ct)
            .Should()
            .HaveCount(expected, "src の記号を基準にしても、組み合わせの数は同じ");
    }

    [Fact(
        DisplayName = "組み合わせをまたいで同じ節は 1 回だけ数える（UpdateFeed.cs の呼び出しは 5 つ・分岐の外の 2 つを重ねない）"
    )]
    public void SharedNodes_AreCountedOnce()
    {
        var variants = ConditionalSourceParse.Parse(
            ReadSource("src/QuickER.Gui/Services/UpdateFeed.cs"),
            cancellationToken: Ct
        );

        // 既定の設定（DEBUG なし）の構文木は分岐の外の 2 つ、DEBUG ありは 5 つ＝重ねると 7
        variants
            .Sum(tree =>
                tree.GetRoot(Ct).DescendantNodes().OfType<InvocationExpressionSyntax>().Count()
            )
            .Should()
            .Be(7);
        ConditionalSourceParse
            .DescendantNodes(variants, Ct)
            .OfType<InvocationExpressionSyntax>()
            .Should()
            .HaveCount(5);

        var catalog = ConditionalSourceParse.Parse(
            ReadSource("src/QuickER.Gui/FeatureModuleCatalog.cs"),
            cancellationToken: Ct
        );

        // new List<IFeatureModule>() は 8 通りすべてにあるが 1 つ、モジュールの生成は分岐ごとに 4 つ
        ConditionalSourceParse
            .DescendantNodes(catalog, Ct)
            .OfType<ObjectCreationExpressionSyntax>()
            .Should()
            .HaveCount(5);
        ConditionalSourceParse
            .DescendantTokens(catalog, Ct)
            .Count(token => token.ValueText == "modules")
            .Should()
            .Be(6, "宣言 1・return 1・Add 4（分岐の外のトークンも 1 回だけ）");
    }

    [Fact(
        DisplayName = "シンボルの走査も、分岐の中の禁止の書き込み・起動を変種のコンパイルで拾い、分岐の外を重ねない"
    )]
    public void SemanticScan_SeesBranchesAndCountsSharedOnce()
    {
        var project = SourceSymbolScan.CompileProbeProject(
            "QuickER.Tests.ConditionalProbe",
            [("src/Probe/Probe.cs", BranchProbe)]
        );

        project.Variants.Should().HaveCount(3, "4 通りのうち基準を除いた 3 つ");

        // 対照：基準のコンパイルだけの走査（従来の形）は、分岐の中を見落とす
        var file = project.Files.Single();
        var baseModel = project.Compilation.GetSemanticModel(file.Tree);
        file.Tree.GetRoot(Ct)
            .DescendantNodes()
            .Select(node =>
                FileWriteCalls.Describe(baseModel, node)
                ?? ProcessLaunchCalls.Describe(baseModel, node)
            )
            .OfType<string>()
            .Should()
            .Equal("File.AppendAllText", "File.AppendAllText");

        project
            .ScanNodes(Ct)
            .Select(item =>
                FileWriteCalls.Describe(item.Model, item.Node)
                ?? ProcessLaunchCalls.Describe(item.Model, item.Node)
            )
            .OfType<string>()
            .Should()
            .BeEquivalentTo([
                "File.AppendAllText",
                "File.WriteAllText",
                "File.AppendAllText",
                "Process.Start",
            ]);
    }

    [Fact(
        DisplayName = "src の QuickER.Gui は 4 つの記号の 15 の変種のコンパイルを持ち、条件コンパイルの無いプロジェクトは持たない"
    )]
    public void GuiProject_HasVariantCompilations()
    {
        var gui = SourceSymbolScan.SrcProjects.Single(project => project.Name == "QuickER.Gui");

        foreach (var variant in gui.Variants)
        {
            output.WriteLine($"[{variant.Symbols}] bcl={variant.ResolvedBclInvocationCount}");
        }

        gui.Variants.Should()
            .HaveCount(15, "DEBUG と QUICKER_* の 3 つ＝16 通りのうち基準を除いた数");
        gui.Variants.SelectMany(variant => variant.Files)
            .Select(file => file.RelativePath)
            .Distinct()
            .Should()
            .BeEquivalentTo(
                "src/QuickER.Gui/FeatureModuleCatalog.cs",
                "src/QuickER.Gui/Services/UpdateFeed.cs"
            );

        // 変種のコンパイルが壊れていない（差し替えた構文木にコンパイルエラーが無い＝参照も暗黙の using も効いている）
        gui.Variants.SelectMany(variant =>
                variant.Files.SelectMany(file =>
                    variant
                        .Compilation.GetSemanticModel(file.Tree)
                        .GetDiagnostics(cancellationToken: Ct)
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(diagnostic =>
                            $"[{variant.Symbols}] {file.RelativePath}: {diagnostic}"
                        )
                )
            )
            .Should()
            .BeEmpty();
        SourceSymbolScan
            .SrcProjects.Where(project => project.Name != "QuickER.Gui")
            .Where(project => project.Variants.Count > 0)
            .Select(project => project.Name)
            .Should()
            .BeEmpty();

        // UpdateFeed.cs の呼び出しは 15 の変種のうち DEBUG なしの 8 つにも現れるが、重ねずに 5 つ
        gui.ScanNodes(Ct)
            .Where(item =>
                item.File.RelativePath.EndsWith("/UpdateFeed.cs", StringComparison.Ordinal)
            )
            .Count(item => item.Node is InvocationExpressionSyntax)
            .Should()
            .Be(5);
    }
}
