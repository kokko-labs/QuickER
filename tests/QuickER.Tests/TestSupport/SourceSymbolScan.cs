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
/// ビルドが作った暗黙の using（<c>obj/**/{アセンブリ名}.GlobalUsings.g.cs</c>）を足す。参照はテストの実行時の
/// TRUSTED_PLATFORM_ASSEMBLIES と、テストの出力フォルダの管理された DLL（自分自身のアセンブリを除く）。
/// アセンブリ名は <c>.csproj</c> の <c>AssemblyName</c>（無ければフォルダ名）にして、<c>InternalsVisibleTo</c> を効かせる。
/// プリプロセッサの記号は <c>DEBUG</c>・<c>TRACE</c> と、src の <c>.csproj</c> にある <c>QUICKER_*</c>（機能モジュールをすべて含む構成）。
/// </para>
/// <para>
/// ソース生成器（CommunityToolkit・<c>GeneratedRegex</c> など）と XAML が作るメンバーは含まないので、それを呼ぶ箇所は解決しない。
/// 呼び出し側は「解決しなかった呼び出し」を数えない側へ倒さず、別の欄として照合すること。参照の組み立てが壊れると
/// すべてが未解決になって緑のまま残るので、<see cref="SourceProject.ResolvedBclInvocationCount"/> が 0 でないことも確かめる。
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
        {
            var folder = Path.GetDirectoryName(csproj)!;
            var assemblyName = ReadAssemblyName(csproj) ?? Path.GetFileName(folder);
            var files = Directory
                .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Select(path => new SourceFile(
                    Path.GetRelativePath(root, path).Replace('\\', '/'),
                    CSharpSyntaxTree.ParseText(File.ReadAllText(path), ParseOptions, path)
                ))
                .ToList();
            var globalUsings = Directory
                .EnumerateFiles(
                    folder,
                    assemblyName + ".GlobalUsings.g.cs",
                    SearchOption.AllDirectories
                )
                .Where(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                )
                .Take(1)
                .Select(path =>
                    CSharpSyntaxTree.ParseText(File.ReadAllText(path), ParseOptions, path)
                );
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
            var compilation = CSharpCompilation.Create(
                assemblyName,
                files.Select(file => file.Tree).Concat(globalUsings),
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Enable
                )
            );

            return new SourceProject(
                Path.GetFileName(folder),
                compilation,
                files,
                CountResolvedBclInvocations(compilation, files)
            );
        }
    }

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
                    && ContainingTypeName(method)?.StartsWith("System.", StringComparison.Ordinal)
                        == true
                );
        });

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
/// <param name="Compilation">シンボルの解決に使うコンパイル</param>
/// <param name="Files">自分のソース（暗黙の using のファイルを含まない）</param>
/// <param name="ResolvedBclInvocationCount">BCL（<c>System.*</c>）のメソッドへ解決できた呼び出しの数</param>
internal sealed record SourceProject(
    string Name,
    CSharpCompilation Compilation,
    IReadOnlyList<SourceFile> Files,
    int ResolvedBclInvocationCount
);

/// <summary>走査の対象のソース 1 つ</summary>
/// <param name="RelativePath">リポジトリのルートからの相対パス（区切りは <c>/</c>）</param>
/// <param name="Tree">構文木</param>
internal sealed record SourceFile(string RelativePath, SyntaxTree Tree);
