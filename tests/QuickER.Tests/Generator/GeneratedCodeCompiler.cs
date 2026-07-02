using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.Generator;

namespace QuickER.Tests.Generator;

/// <summary>
/// <see cref="CSharpCodeGenerationService"/> の生成結果を Roslyn で実際にコンパイルし、
/// 「生成 C# コードがコンパイル可能である」ことを検証するためのテストヘルパー
/// </summary>
/// <remarks>
/// 生成コードが依存する型（<c>Microsoft.Data.SqlClient.SqlConnection</c> など）を解決できるよう、
/// 実行時の Trusted Platform Assemblies に加えて明示的な参照アセンブリを合成する。
/// 参照アセンブリの収集はプロセス内で不変なため <see cref="MetadataReferences"/> に一度だけキャッシュし、
/// 全テストケースで使い回すことで実行時間を抑える。
/// </remarks>
internal static class GeneratedCodeCompiler
{
    /// <summary>
    /// 生成コードのコンパイルに用いる参照アセンブリ一覧。
    /// TPA（Trusted Platform Assemblies）に加え、生成コードが利用するがテストホストでは
    /// 遅延ロードされ得るアセンブリ（Microsoft.Data.SqlClient 等）を明示的に追加する。
    /// </summary>
    private static readonly Lazy<IReadOnlyList<MetadataReference>> MetadataReferences = new(
        BuildMetadataReferences
    );

    /// <summary>
    /// 生成結果の全ファイルを 1 コンパイル単位としてコンパイルし、診断（エラー・警告）を返す
    /// </summary>
    /// <param name="result">検証対象の生成結果。<see cref="CodeGenerationResult.Files"/> の全ファイルをまとめてコンパイルする</param>
    /// <param name="assemblyName">コンパイル対象アセンブリ名（診断メッセージの可読性のためテストケースごとに変える）</param>
    /// <returns>コンパイル結果（成功可否と診断一覧）</returns>
    public static GeneratedCodeCompilationResult Compile(
        CodeGenerationResult result,
        string assemblyName
    )
    {
        var syntaxTrees = result
            .Files.Select(file =>
                CSharpSyntaxTree.ParseText(
                    file.Content,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    path: file.FileName
                )
            )
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            MetadataReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        var diagnostics = emitResult
            .Diagnostics.Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToArray();

        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        // CS1701/CS1702: アセンブリ参照バージョン統一に関する情報的警告。
        // テストホストが合成する参照セットに起因し、生成コード自体の品質とは無関係のため報告対象から除外する。
        var warnings = diagnostics
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Id != "CS1701"
                && diagnostic.Id != "CS1702"
            )
            .ToArray();

        return new GeneratedCodeCompilationResult
        {
            Success = emitResult.Success && errors.Length == 0,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        var referencesByPath = new Dictionary<string, MetadataReference>(
            StringComparer.OrdinalIgnoreCase
        );

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!File.Exists(path))
            {
                return;
            }

            referencesByPath.TryAdd(path, MetadataReference.CreateFromFile(path));
        }

        // 実行時の Trusted Platform Assemblies（BCL 全体）をベースにする
        var trustedAssembliesPaths = (
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
        )?.Split(Path.PathSeparator);
        if (trustedAssembliesPaths is not null)
        {
            foreach (var path in trustedAssembliesPaths)
            {
                AddPath(path);
            }
        }

        // TPA に含まれない可能性がある、生成コードが直接依存するアセンブリを明示的に追加する
        // （テストホストでは遅延ロードのため未ロード＝TPA 未列挙のことがある）
        AddPath(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location);
        AddPath(
            typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location
        );
        AddPath(
            typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)
                .Assembly
                .Location
        );

        return referencesByPath.Values.ToArray();
    }
}

/// <summary>
/// <see cref="GeneratedCodeCompiler.Compile"/> の結果
/// </summary>
internal sealed class GeneratedCodeCompilationResult
{
    /// <summary>コンパイルエラーが 0 件かどうか</summary>
    public required bool Success { get; init; }

    /// <summary>コンパイルエラー診断の一覧</summary>
    public required IReadOnlyList<Diagnostic> Errors { get; init; }

    /// <summary>生成コード起因の警告診断の一覧（CS1701/CS1702 などアセンブリ統一系は除外済み）</summary>
    public required IReadOnlyList<Diagnostic> Warnings { get; init; }

    /// <summary>診断一覧を改行区切りの読みやすい文字列にまとめる（テスト失敗メッセージ用）</summary>
    public string DescribeErrors() => string.Join(Environment.NewLine, Errors.Select(Describe));

    /// <summary>診断一覧を改行区切りの読みやすい文字列にまとめる（テスト失敗メッセージ用）</summary>
    public string DescribeWarnings() => string.Join(Environment.NewLine, Warnings.Select(Describe));

    private static string Describe(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        return $"[{diagnostic.Id}] {lineSpan.Path}:{lineSpan.StartLinePosition.Line + 1} {diagnostic.GetMessage()}";
    }
}
