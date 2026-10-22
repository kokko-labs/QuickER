using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// src のプロジェクトを、BCL とビルド出力の DLL を参照にした Roslyn のコンパイルとして組み、呼び出し・生成をシンボルで判定できるようにする
/// （走査のガードの土台）
/// </summary>
/// <remarks>
/// <para>
/// 構文の名前だけで判定すると、目的型の <c>new()</c>・<c>using static</c>・別名・<c>FileInfo</c> のような別の書き方を拾えない。
/// シンボルで判定すれば、書き方に依らず「どの API を呼んでいるか」で数えられる。
/// </para>
/// <para>
/// 組み方：<c>src/</c> の <c>.csproj</c> を持つフォルダを 1 プロジェクトとし、その下の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）に、
/// ビルドが作った暗黙の using（<c>obj/**/{.csproj の名前}.GlobalUsings.g.cs</c>）を足す。参照はテストの実行時の
/// TRUSTED_PLATFORM_ASSEMBLIES と、テストの出力フォルダの管理された DLL（自分自身のアセンブリを除く）。
/// アセンブリ名は <c>.csproj</c> の <c>AssemblyName</c>（無ければフォルダ名）にして、<c>InternalsVisibleTo</c> を効かせる。
/// プリプロセッサの記号は <c>DEBUG</c>・<c>TRACE</c> と、src の <c>.csproj</c> にある <c>QUICKER_*</c>（機能モジュールをすべて含む構成）。
/// </para>
/// <para>
/// ソース生成器（CommunityToolkit・<c>GeneratedRegex</c> など）と XAML が作るメンバーは含まないので、それを呼ぶ箇所は解決しない。
/// 呼び出し側は「解決しなかった呼び出し」を数えない側へ倒さず、別の欄として照合すること。参照の組み立てが壊れると
/// すべてが未解決になって緑のまま残るので、<see cref="SourceProject.ResolvedBclInvocationCount"/> が 0 でないことも確かめる。
/// </para>
/// <para>
/// 1 通りの記号でパースすると、外れる分岐（<c>#if !DEBUG</c> の中・<c>#if QUICKER_*</c> の <c>#else</c>）がトリビアになって走査から
/// 見えない。条件コンパイルを持つファイルがあるプロジェクトは、試す記号のすべての組み合わせの変種のコンパイル
/// （<see cref="SourceProject.Variants"/>）も作る。走査は <see cref="SourceProject.ScanNodes"/> を通し、組み合わせをまたいで
/// 同じ節を 1 回だけ数える（<see cref="ConditionalSourceParse"/>）。
/// </para>
/// </remarks>
internal static class SourceSymbolScan
{
    /// <summary>src のすべてのプロジェクト（初回の使用で 1 回だけ組む）</summary>
    public static IReadOnlyList<SourceProject> SrcProjects => LazyProjects.Value;

    /// <summary>テストの実行時に見える参照（プローブのソースをコンパイルするときにも使う）</summary>
    public static IReadOnlyList<MetadataReference> References => LazyReferences.Value;

    /// <summary>src のプロジェクトのパース設定（プリプロセッサの記号を含む）</summary>
    public static CSharpParseOptions ParseOptions => LazyParseOptions.Value;

    private static readonly Lazy<IReadOnlyList<SourceProject>> LazyProjects = new(LoadProjects);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> LazyReferences = new(
        LoadReferences
    );

    private static readonly Lazy<CSharpParseOptions> LazyParseOptions = new(LoadParseOptions);

    /// <summary>与えたソースを 1 つのコンパイルとして組む（ガードが拾う形を確かめる自己検査のプローブ用）</summary>
    public static (CSharpCompilation Compilation, SyntaxTree Tree) CompileProbe(
        string source,
        string assemblyName = "QuickER.Tests.Probe"
    )
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        return (compilation, tree);
    }

    /// <summary>呼び出し・生成の節が指すメソッド（解決できなければ、候補が 1 つのときだけその候補）</summary>
    public static IMethodSymbol? ResolveMethod(SemanticModel model, SyntaxNode node)
    {
        var info = model.GetSymbolInfo(node);

        if (info.Symbol is IMethodSymbol method)
        {
            return method;
        }

        return info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] as IMethodSymbol : null;
    }

    /// <summary>メソッドを宣言している型の完全名（<c>System.IO.File</c> など）</summary>
    public static string? ContainingTypeName(IMethodSymbol method) =>
        method.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static IReadOnlyList<SourceProject> LoadProjects()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var projects = Directory
            .EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(csproj => !IsBuildOutput(csproj))
            .AsParallel()
            .Select(csproj => LoadProject(root, csproj))
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();
        LoadElapsed = watch.Elapsed;
        return projects;
    }

    /// <summary>src のプロジェクトを組み終えるまでにかかった時間（走査の費用の報告用）</summary>
    public static TimeSpan LoadElapsed { get; private set; }

    private static SourceProject LoadProject(string root, string csproj)
    {
        var folder = Path.GetDirectoryName(csproj)!;
        var assemblyName = ReadAssemblyName(csproj) ?? Path.GetFileName(folder);
        var files = Directory
            .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path =>
                (
                    RelativePath: Path.GetRelativePath(root, path).Replace('\\', '/'),
                    Text: File.ReadAllText(path),
                    FullPath: path
                )
            )
            .ToList();

        // ビルドが作った暗黙の using（名前は AssemblyName でなく .csproj の名前で付く＝QuickER.Gui は AssemblyName が QuickER。
        // 条件コンパイルを持たない生成物なので、組み合わせは 1 通りになる）
        var globalUsings = Directory
            .EnumerateFiles(
                folder,
                Path.GetFileNameWithoutExtension(csproj) + ".GlobalUsings.g.cs",
                SearchOption.AllDirectories
            )
            .Where(path =>
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Take(1)
            .Select(path =>
                ConditionalSourceParse.Parse(File.ReadAllText(path), ParseOptions, path).Single()
            )
            .ToList();
        var ownAssemblyFile = assemblyName + ".dll";
        var references = References
            .Where(reference =>
                !string.Equals(
                    Path.GetFileName((reference as PortableExecutableReference)?.FilePath),
                    ownAssemblyFile,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();

        return BuildProject(
            Path.GetFileName(folder),
            assemblyName,
            files,
            globalUsings,
            references
        );
    }

    /// <summary>
    /// 与えたソースを 1 プロジェクトとして組む（条件コンパイルの変種のコンパイルも作る。自己検査のプローブ用）
    /// </summary>
    /// <param name="name">プロジェクト名（アセンブリ名にも使う）</param>
    /// <param name="files">走査の対象のソース（相対パスと本文）</param>
    public static SourceProject CompileProbeProject(
        string name,
        IReadOnlyList<(string RelativePath, string Text)> files
    ) =>
        BuildProject(
            name,
            name,
            files.Select(file => (file.RelativePath, file.Text, file.RelativePath)).ToList(),
            [],
            References
        );

    /// <summary>
    /// ソースをコンパイルとして組む。基準のコンパイルは <see cref="ParseOptions"/> の記号でパースした全ファイル。
    /// 条件コンパイルを持つファイルがあれば、それらのファイルが試す記号のすべての組み合わせ（基準と同じものを除く）ごとに、
    /// そのファイルの構文木だけを差し替えた変種のコンパイルを作る（ほかのファイルの構文木は基準と共有する）
    /// </summary>
    private static SourceProject BuildProject(
        string name,
        string assemblyName,
        IReadOnlyList<(string RelativePath, string Text, string FullPath)> sources,
        IReadOnlyList<SyntaxTree> extraTrees,
        IReadOnlyList<MetadataReference> references
    )
    {
        // ソースは必ず ConditionalSourceParse でパースする（先頭が基準の設定の構文木・残りが記号の組み合わせごとの構文木）
        var parsed = sources
            .Select(source =>
            {
                var trees = ConditionalSourceParse.Parse(
                    source.Text,
                    ParseOptions,
                    source.FullPath
                );

                return new ParsedSource(
                    new SourceFile(source.RelativePath, trees[0]),
                    trees,
                    ConditionalSourceParse.ConditionalSymbols(trees[0])
                );
            })
            .ToList();
        var files = parsed.Select(item => item.File).ToList();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            files.Select(file => file.Tree).Concat(extraTrees),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        return new SourceProject(
            name,
            compilation,
            files,
            CountResolvedBclInvocations(compilation, files),
            BuildVariants(name, compilation, [.. parsed.Where(item => item.Trees.Count > 1)])
        );
    }

    /// <summary>
    /// 条件コンパイルを持つファイルについて、記号の組み合わせ（基準と同じものを除く）ごとの変種のコンパイルを作る
    /// </summary>
    /// <remarks>
    /// 記号は、プロジェクトの条件コンパイルを持つファイルが試すものの和集合で、上限は <see cref="ConditionalSourceParse.MaxSymbols"/>。
    /// 各ファイルの構文木は <see cref="ConditionalSourceParse.Parse"/> が作ったもの（そのファイルが試す記号の定義が同じ組み合わせ
    /// どうしで 1 本を共有し、基準と同じなら基準の構文木）を使う。
    /// </remarks>
    private static IReadOnlyList<SourceVariant> BuildVariants(
        string name,
        CSharpCompilation baseCompilation,
        IReadOnlyList<ParsedSource> conditional
    )
    {
        if (conditional.Count == 0)
        {
            return [];
        }

        var symbols = conditional
            .SelectMany(item => item.Symbols)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var baseDefined = ParseOptions.PreprocessorSymbolNames.ToHashSet(StringComparer.Ordinal);
        var variants = new List<SourceVariant>();

        foreach (
            var options in ConditionalSourceParse
                .VariantOptions(ParseOptions, symbols, name)
                .Skip(1)
        )
        {
            var defined = options.PreprocessorSymbolNames.ToHashSet(StringComparer.Ordinal);
            var compilation = baseCompilation;
            var files = new List<SourceFile>();

            foreach (var item in conditional)
            {
                // ファイルの構文木の並びは「基準から定義を反転した記号のビット」の順（VariantOptions と同じ）
                var flip = Enumerable
                    .Range(0, item.Symbols.Count)
                    .Where(index =>
                        defined.Contains(item.Symbols[index])
                        != baseDefined.Contains(item.Symbols[index])
                    )
                    .Sum(index => 1 << index);
                var tree = item.Trees[flip];

                if (!ReferenceEquals(tree, item.File.Tree))
                {
                    compilation = compilation.ReplaceSyntaxTree(item.File.Tree, tree);
                }

                files.Add(new SourceFile(item.File.RelativePath, tree));
            }

            variants.Add(
                new SourceVariant(
                    string.Join(
                        " ",
                        symbols.Select(symbol => defined.Contains(symbol) ? symbol : "!" + symbol)
                    ),
                    compilation,
                    files,
                    CountResolvedBclInvocations(compilation, files)
                )
            );
        }

        return variants;
    }

    /// <summary>パースしたソース（基準の構文木と、記号の組み合わせごとの構文木）</summary>
    private sealed record ParsedSource(
        SourceFile File,
        IReadOnlyList<SyntaxTree> Trees,
        IReadOnlyList<string> Symbols
    );

    private static int CountResolvedBclInvocations(
        CSharpCompilation compilation,
        IReadOnlyList<SourceFile> files
    ) =>
        files.Sum(file =>
        {
            var model = compilation.GetSemanticModel(file.Tree);

            return file
                .Tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Count(invocation =>
                    ResolveMethod(model, invocation) is { } method
                    && IsSystemNamespace(method.ContainingType?.ContainingNamespace)
                );
        });

    /// <summary>
    /// 名前空間が <c>System</c> かその下か（型の表記でなく名前空間で見る＝<c>string</c> のようにキーワードで表記される型も BCL に数える）
    /// </summary>
    private static bool IsSystemNamespace(INamespaceSymbol? ns) =>
        ns?.ToDisplayString() is { } name
        && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));

    private static IReadOnlyList<MetadataReference> LoadReferences()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (
            var path in Directory.EnumerateFiles(
                AppContext.BaseDirectory,
                "*.dll",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            paths.TryAdd(Path.GetFileName(path), path);
        }

        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        foreach (
            var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            paths.TryAdd(Path.GetFileName(path), path);
        }

        return paths
            .Values.Where(IsManagedAssembly)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException)
        {
            return false;
        }
    }

    private static CSharpParseOptions LoadParseOptions()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var symbols = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "QUICKER_[A-Z_]+"))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Concat(["DEBUG", "TRACE"])
            .ToArray();

        return new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: symbols);
    }

    private static string? ReadAssemblyName(string csproj)
    {
        var match = Regex.Match(File.ReadAllText(csproj), "<AssemblyName>([^<]+)</AssemblyName>");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");
}

/// <summary>src の 1 プロジェクト（コンパイルと、走査の対象の自分のソース）</summary>
/// <param name="Name">フォルダ名（<c>QuickER.Gui</c> など）</param>
/// <param name="Compilation">シンボルの解決に使うコンパイル（基準の記号でパースした全ファイル）</param>
/// <param name="Files">自分のソース（暗黙の using のファイルを含まない）</param>
/// <param name="ResolvedBclInvocationCount">BCL（<c>System.*</c>）のメソッドへ解決できた呼び出しの数</param>
/// <param name="Variants">条件コンパイルの記号の組み合わせごとの変種のコンパイル（条件コンパイルが無ければ空）</param>
internal sealed record SourceProject(
    string Name,
    CSharpCompilation Compilation,
    IReadOnlyList<SourceFile> Files,
    int ResolvedBclInvocationCount,
    IReadOnlyList<SourceVariant> Variants
)
{
    /// <summary>
    /// 自分のソースの全節を、その節の構文木の意味モデルと組にして返す。条件コンパイルを持つファイルは、変種のコンパイルの
    /// 構文木の節も返し、組み合わせをまたいで同じ節は 1 回だけにする（分岐の外の呼び出しを組み合わせの数だけ数えない）
    /// </summary>
    public IEnumerable<(SourceFile File, SemanticModel Model, SyntaxNode Node)> ScanNodes(
        CancellationToken cancellationToken = default
    )
    {
        var variantUnits = Variants
            .SelectMany(variant => variant.Files.Select(file => (file, variant.Compilation)))
            .ToLookup(unit => unit.file.RelativePath, StringComparer.Ordinal);

        foreach (var file in Files)
        {
            IEnumerable<(SourceFile File, CSharpCompilation Compilation)> units =
            [
                (file, Compilation),
                .. variantUnits[file.RelativePath],
            ];
            var nodes = units.SelectMany(unit =>
            {
                var model = unit.Compilation.GetSemanticModel(unit.File.Tree);

                return unit
                    .File.Tree.GetRoot(cancellationToken)
                    .DescendantNodes()
                    .Select(node => (unit.File, Model: model, Node: node));
            });

            var distinct = variantUnits.Contains(file.RelativePath)
                ? ConditionalSourceParse.DistinctAcrossVariants(nodes, item => item.Node)
                : nodes;

            foreach (var item in distinct)
            {
                yield return item;
            }
        }
    }
}

/// <summary>条件コンパイルの記号の 1 つの組み合わせの変種のコンパイル</summary>
/// <param name="Symbols">組み合わせの表記（未定義の記号は <c>!</c> を付ける）</param>
/// <param name="Compilation">条件コンパイルを持つファイルの構文木だけを差し替えたコンパイル</param>
/// <param name="Files">差し替えた構文木（条件コンパイルを持つファイルだけ）</param>
/// <param name="ResolvedBclInvocationCount">差し替えた構文木で BCL のメソッドへ解決できた呼び出しの数</param>
internal sealed record SourceVariant(
    string Symbols,
    CSharpCompilation Compilation,
    IReadOnlyList<SourceFile> Files,
    int ResolvedBclInvocationCount
);

/// <summary>走査の対象のソース 1 つ</summary>
/// <param name="RelativePath">リポジトリのルートからの相対パス（区切りは <c>/</c>）</param>
/// <param name="Tree">構文木</param>
internal sealed record SourceFile(string RelativePath, SyntaxTree Tree);
