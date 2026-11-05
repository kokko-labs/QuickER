using System.Collections.Concurrent;
using System.IO;
using Microsoft.CodeAnalysis;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 生成コードを Roslyn でコンパイルし、エラー 0・警告 0 を表明する
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>インライン（既定）：<see cref="GeneratedCodeCompiler.CompileProject"/>（参照集合は <see cref="RuntimeReferenceSet.Full"/>・XmlDoc の整合も診断する）。
///   サーバー実装（<c>*.RemoteServer.g.cs</c>）も含める（ASP.NET Core はテストホストの共有フレームワークから解決する）。</item>
///   <item>パッケージ参照モード：案内（<see cref="RuntimePackageReferenceGuidance.Compute"/>）の
///   パッケージだけを参照する集合（パッケージ用ソースをその場でコンパイルしたアセンブリ＋<see cref="RuntimeReferenceSet.GeneratedCode"/>
///   ／EF Core のときは <see cref="RuntimeReferenceSet.GeneratedCodeWithEfCore"/>）でコンパイルする。この経路は
///   <see cref="GeneratedCodeCompiler.CreateCompilation"/> の構文木（XmlDoc の診断なし）を使う（共有の部品を変えないため）。</item>
///   <item>層別：層ごとに別プロジェクトで、参照グラフは Infrastructure → Domain・Presentation → Domain・Server → Domain＋Infrastructure
///   （固定 infra の可視性など、別プロジェクトでしか出ない崩れを見る）。決まった行が 1 アセンブリを指定したとき（<c>CompileAsOneAssembly</c>）は、
///   層別でも全部を 1 アセンブリへまとめる（層をまたぐ同名の型など、1 アセンブリでしか出ない崩れを見る）。</item>
/// </list>
/// 手動実装のクエリ（<see cref="ShapeDiagram.ManualSources"/>）は利用者の partial と同じくインフラ層（層別でなければ同じプロジェクト）へ入れる。
/// </remarks>
internal static class CompileJudge
{
    /// <summary>CS1701 / CS1702（参照の版の統一の情報）は生成コードの品質と無関係なので数えない</summary>
    private static readonly HashSet<string> IgnoredWarnings = new(StringComparer.Ordinal)
    {
        "CS1701",
        "CS1702",
    };

    /// <summary>コンパイルを確かめる（問題を返す）</summary>
    /// <param name="options">実効の生成オプション</param>
    /// <param name="result">生成結果</param>
    /// <param name="manualSources">手動実装のソース</param>
    /// <param name="oneAssembly">層別出力も 1 アセンブリへまとめてコンパイルする（層別でなければ意味を持たない）</param>
    public static IReadOnlyList<string> Check(
        CodeGenerationOptions options,
        CodeGenerationResult result,
        IReadOnlyList<GeneratedFile> manualSources,
        bool oneAssembly = false
    )
    {
        var files = result
            .Files.Where(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            .Concat(manualSources)
            .ToList();
        var references = options.UseRuntimePackages ? PackageReferences(options) : null;

        if (!options.LayeredOutput || oneAssembly)
        {
            return Compile("All", files, references, []).Problems;
        }

        var namespaces = new CodegenNamespaces(options);
        var problems = new List<string>();
        var byLayer = files
            .GroupBy(file => LayerOf(file, namespaces))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        MetadataReference? Layer(string layer, IReadOnlyList<MetadataReference?> projectReferences)
        {
            if (!byLayer.TryGetValue(layer, out var layerFiles))
            {
                return null;
            }

            var (layerProblems, emitted) = Compile(
                layer,
                layerFiles,
                references,
                projectReferences.OfType<MetadataReference>().ToList()
            );
            problems.AddRange(layerProblems);
            return emitted;
        }

        var domain = Layer(namespaces.DomainDirectory, []);
        var infrastructure = Layer(namespaces.InfraDirectory, [domain]);
        Layer(namespaces.PresentationDirectory, [domain]);
        Layer(namespaces.ServerDirectory, [domain, infrastructure]);

        foreach (
            var unknown in byLayer.Keys.Except(
                [
                    namespaces.DomainDirectory,
                    namespaces.InfraDirectory,
                    namespaces.PresentationDirectory,
                    namespaces.ServerDirectory,
                ],
                StringComparer.Ordinal
            )
        )
        {
            problems.Add($"コンパイル: どの層にも属さないフォルダ {unknown} のファイルがある");
        }

        return problems;
    }

    /// <summary>ファイルの層（RelativeDirectory の先頭の層フォルダ）</summary>
    private static string LayerOf(GeneratedFile file, CodegenNamespaces namespaces)
    {
        var directory = (file.RelativeDirectory ?? string.Empty).Replace('\\', '/');

        foreach (
            var layer in new[]
            {
                namespaces.DomainDirectory,
                namespaces.PresentationDirectory,
                namespaces.InfraDirectory,
                namespaces.ServerDirectory,
            }
        )
        {
            if (directory == layer || directory.StartsWith(layer + "/", StringComparison.Ordinal))
            {
                return layer;
            }
        }

        return directory;
    }

    /// <summary>1 つのプロジェクトをコンパイルする</summary>
    private static (IReadOnlyList<string> Problems, MetadataReference? Emitted) Compile(
        string project,
        IReadOnlyList<GeneratedFile> files,
        IReadOnlyList<MetadataReference>? packageReferences,
        IReadOnlyList<MetadataReference> projectReferences
    )
    {
        var assemblyName = $"QuickER.CodegenConfig.{project}.{Guid.NewGuid():N}";
        var problems = new List<string>();

        if (packageReferences is null)
        {
            var compiled = GeneratedCodeCompiler.CompileProject(
                files,
                assemblyName,
                projectReferences,
                out var emitted
            );

            problems.AddRange(
                compiled.Errors.Select(d =>
                    $"コンパイル（{project}）: エラー {GeneratedCodeDiagnostics.Describe(d)}"
                )
            );
            problems.AddRange(
                compiled.Warnings.Select(d =>
                    $"コンパイル（{project}）: 警告 {GeneratedCodeDiagnostics.Describe(d)}"
                )
            );
            return (problems, emitted);
        }

        var compilation = GeneratedCodeCompiler
            .CreateCompilation(
                files.Select(file => (FileLayoutOracle.PathOf(file), file.Content)),
                assemblyName
            )
            .WithReferences(packageReferences.Concat(projectReferences));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        var diagnostics = emit
            .Diagnostics.Where(d =>
                d.Severity >= DiagnosticSeverity.Warning && !IgnoredWarnings.Contains(d.Id)
            )
            .ToList();
        problems.AddRange(
            diagnostics.Select(d =>
                $"コンパイル（{project}・パッケージ参照）: {d.Severity} {GeneratedCodeDiagnostics.Describe(d)}"
            )
        );

        if (!emit.Success && problems.Count == 0)
        {
            problems.Add($"コンパイル（{project}・パッケージ参照）: emit が失敗した");
        }

        return (
            problems,
            emit.Success ? MetadataReference.CreateFromImage(stream.ToArray()) : null
        );
    }

    // ---- パッケージ参照モードの参照集合（パッケージ用ソースを Core → 各パッケージの順にアセンブリ化して参照する） ----

    private static readonly RuntimePackageSourceRenderer PackageRenderer = new();

    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> PackageImages = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// 案内のパッケージだけを参照する集合（生成コードが直接使う BCL＋DI〔EF Core のときは EF Core も〕＋案内のパッケージ）
    /// </summary>
    private static IReadOnlyList<MetadataReference> PackageReferences(CodeGenerationOptions options)
    {
        var packages = RuntimePackageReferenceGuidance.Compute(options);
        var baseSet = packages.Contains(RuntimePackages.EntityFrameworkCore)
            ? RuntimeReferenceSet.GeneratedCodeWithEfCore
            : RuntimeReferenceSet.GeneratedCode;
        var references = new List<MetadataReference>(baseSet.Build())
        {
            MetadataReference.CreateFromImage(Image(RuntimePackages.Core)),
        };

        foreach (var package in packages.Where(p => p != RuntimePackages.Core))
        {
            references.Add(MetadataReference.CreateFromImage(Image(package)));
        }

        return references;
    }

    /// <summary>パッケージ用ソースを 1 アセンブリへコンパイルした PE イメージ（パッケージごとに 1 回だけ作る）</summary>
    private static byte[] Image(string package) =>
        PackageImages.GetOrAdd(package, name => new Lazy<byte[]>(() => CompilePackage(name))).Value;

    private static byte[] CompilePackage(string package)
    {
        var (source, referenceSet) = package switch
        {
            RuntimePackages.Core => (PackageRenderer.RenderCore(), RuntimeReferenceSet.CoreOnly),
            RuntimePackages.SqlServer => (
                PackageRenderer.RenderSqlServer(),
                RuntimeReferenceSet.SqlServer
            ),
            RuntimePackages.Sqlite => (PackageRenderer.RenderSqlite(), RuntimeReferenceSet.Sqlite),
            RuntimePackages.EntityFrameworkCore => (
                PackageRenderer.RenderEfCore(),
                RuntimeReferenceSet.EfCore
            ),
            RuntimePackages.InMemory => (
                PackageRenderer.RenderInMemory(),
                RuntimeReferenceSet.InMemory
            ),
            RuntimePackages.Sync => (PackageRenderer.RenderSync(), RuntimeReferenceSet.Sync),
            RuntimePackages.AspNetCore => (
                PackageRenderer.RenderAspNetCore(),
                RuntimeReferenceSet.AspNetCore
            ),
            _ => throw new InvalidOperationException($"知らないパッケージ {package}"),
        };
        var references =
            package == RuntimePackages.Core
                ? referenceSet.Build()
                :
                [
                    .. referenceSet.Build(),
                    MetadataReference.CreateFromImage(Image(RuntimePackages.Core)),
                ];
        var emit = GeneratedCodeCompiler.Emit(
            [($"{package}.g.cs", source)],
            $"{package}.{Guid.NewGuid():N}",
            references
        );

        if (!emit.Success)
        {
            throw new InvalidOperationException(
                $"ランタイムパッケージ {package} のアセンブリ化に失敗:{Environment.NewLine}{emit.DescribeErrors()}"
            );
        }

        return emit.Image!;
    }
}
