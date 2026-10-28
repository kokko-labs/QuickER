using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// <see cref="CSharpCodeGenerationService"/> の生成結果を Roslyn で実際にコンパイルし、
/// 「生成 C# コードがコンパイル可能である」ことを検証するためのテストヘルパー
/// </summary>
/// <remarks>
/// <para>
/// 生成コードが依存する型（<c>Microsoft.Data.SqlClient.SqlConnection</c> など）を解決できるよう、
/// 実行時の Trusted Platform Assemblies に加えて明示的な参照アセンブリを合成した <see cref="RuntimeReferenceSet.Full"/> を使う。
/// 参照集合はプロセス内で不変なため一度だけ作り、全テストケースで使い回すことで実行時間を抑える。
/// </para>
/// <para>
/// 構文木のパースは <see cref="Parse"/> の 1 か所だけで行う（テストが Roslyn のパースを直接呼ぶ箇所は
/// <c>SourceParseEntryGuardTests</c> の一覧で数えている）。依存を絞った参照集合でコンパイルする検証は <see cref="Emit"/> を使う。
/// </para>
/// </remarks>
internal static class GeneratedCodeCompiler
{
    /// <summary>
    /// 生成コードのコンパイルに用いる参照アセンブリ一覧（TPA＋生成コードが直接依存し得るアセンブリ）
    /// </summary>
    private static IReadOnlyList<MetadataReference> MetadataReferences =>
        RuntimeReferenceSet.Full.Build();

    /// <summary>
    /// 生成結果の全ファイルを 1 コンパイル単位としてコンパイルし、診断（エラー・警告）を返す
    /// </summary>
    /// <param name="result">検証対象の生成結果。<see cref="CodeGenerationResult.Files"/> の全ファイルをまとめてコンパイルする</param>
    /// <param name="assemblyName">コンパイル対象アセンブリ名（診断メッセージの可読性のためテストケースごとに変える）</param>
    /// <returns>コンパイル結果（成功可否と診断一覧）</returns>
    public static GeneratedCodeCompilationResult Compile(
        CodeGenerationResult result,
        string assemblyName
    ) => CompileProject(result.Files, assemblyName, projectReferences: [], out _);

    /// <summary>
    /// ファイル群を 1 プロジェクト（1 アセンブリ）としてコンパイルし、成功時は他プロジェクトから参照できる
    /// メタデータ参照も返す（層別出力の「別アセンブリに分けても参照グラフだけでビルドが通る」検証用）
    /// </summary>
    /// <param name="files">このプロジェクトに属する生成ファイル群</param>
    /// <param name="assemblyName">アセンブリ名（internal 可視性の判定・診断の可読性に効く）</param>
    /// <param name="projectReferences">参照する兄弟プロジェクトのメタデータ参照（例: Infrastructure → Domain）</param>
    /// <param name="emittedReference">成功時に生成されるこのプロジェクトのメタデータ参照（失敗時は null）</param>
    public static GeneratedCodeCompilationResult CompileProject(
        IEnumerable<GeneratedFile> files,
        string assemblyName,
        IReadOnlyList<MetadataReference> projectReferences,
        out MetadataReference? emittedReference
    )
    {
        // DocumentationMode.Diagnose: XmlDoc の整合診断（CS1573=param 欠落・CS1574=cref 解決不能・
        // CS1591=公開メンバーの XmlDoc 欠落など）を警告として拾う＝GenerateDocumentationFile=true の
        // 利用者ビルドで初めて出る警告を、ここで先に検出する（テストが合成する利用例ソースは internal にして対象外へ）
        var compilation = CreateCompilationCore(
            files.Select(file => (file.FileName, file.Content)),
            assemblyName,
            MetadataReferences.Concat(projectReferences),
            DocumentationMode.Diagnose
        );

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        emittedReference = emitResult.Success
            ? MetadataReference.CreateFromImage(peStream.ToArray())
            : null;

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

    /// <summary>
    /// ソース群の Roslyn コンパイルを作って返す（emit はしない。意味モデルで型の参照を調べる検証用）
    /// </summary>
    /// <param name="sources">ファイル名と内容の組</param>
    /// <param name="assemblyName">アセンブリ名</param>
    public static CSharpCompilation CreateCompilation(
        IEnumerable<(string FileName, string Content)> sources,
        string assemblyName
    ) => CreateCompilationCore(sources, assemblyName, MetadataReferences, DocumentationMode.Parse);

    /// <summary>
    /// 生成結果をコンパイルし、成功時にロード済みアセンブリを返す（生成コードを実際に動かす検証用）
    /// </summary>
    /// <param name="result">検証対象の生成結果</param>
    /// <param name="assemblyName">コンパイル対象アセンブリ名（テストごとに一意にする）</param>
    /// <param name="extraSources">生成物と一緒にコンパイルする追加ソース（生成物の型を静的に使う検証コードなど）</param>
    /// <remarks>
    /// 参照集合は <see cref="Compile"/> と同一（TPA＋明示追加）。ロードしたアセンブリはテストホストに
    /// 既にロード済みの同一 ID のアセンブリ（EF Core 等）へ束縛されるため、生成コードをそのまま実行できる。
    /// </remarks>
    public static Assembly CompileAndLoad(
        CodeGenerationResult result,
        string assemblyName,
        params string[] extraSources
    )
    {
        var compilation = CreateCompilationCore(
            result
                .Files.Select(file => (file.FileName, file.Content))
                .Concat(extraSources.Select((source, index) => ($"Extra{index}.cs", source))),
            assemblyName,
            MetadataReferences,
            DocumentationMode.Parse
        );

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        if (!emitResult.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emitResult
                    .Diagnostics.Where(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error
                    )
                    .Select(diagnostic => diagnostic.ToString())
            );

            throw new InvalidOperationException(
                "生成コードのコンパイルに失敗:" + Environment.NewLine + errors
            );
        }

        return Assembly.Load(peStream.ToArray());
    }

    /// <summary>
    /// ソース群を、指定した参照集合だけでコンパイルしてエラー診断と PE イメージを返す
    /// （依存を絞った参照集合でコンパイルが通ることを確かめる検証用。<see cref="RuntimeReferenceSet"/> と組み合わせる）
    /// </summary>
    /// <param name="sources">ファイル名と内容の組</param>
    /// <param name="assemblyName">アセンブリ名</param>
    /// <param name="references">参照集合（ここに無いアセンブリの型は解決できない）</param>
    public static GeneratedCodeEmitResult Emit(
        IEnumerable<(string FileName, string Content)> sources,
        string assemblyName,
        IEnumerable<MetadataReference> references
    )
    {
        var compilation = CreateCompilationCore(
            sources,
            assemblyName,
            references,
            DocumentationMode.Parse
        );

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        var errors = emitResult
            .Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        return new GeneratedCodeEmitResult
        {
            Success = emitResult.Success && errors.Length == 0,
            Errors = errors,
            Image = emitResult.Success ? peStream.ToArray() : null,
        };
    }

    /// <summary>ソース群を構文木にしてコンパイルを作る（オプションは全入口で共通）</summary>
    private static CSharpCompilation CreateCompilationCore(
        IEnumerable<(string FileName, string Content)> sources,
        string assemblyName,
        IEnumerable<MetadataReference> references,
        DocumentationMode documentationMode
    ) =>
        CSharpCompilation.Create(
            assemblyName,
            sources.Select(source => Parse(source.Content, source.FileName, documentationMode)),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

    /// <summary>ソース 1 つを最新の言語版でパースする（テストが生成物をパースする唯一の場所）</summary>
    private static SyntaxTree Parse(
        string content,
        string path,
        DocumentationMode documentationMode
    ) =>
        CSharpSyntaxTree.ParseText(
            content,
            new CSharpParseOptions(LanguageVersion.Latest, documentationMode),
            path: path
        );
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
    public string DescribeErrors() =>
        string.Join(Environment.NewLine, Errors.Select(GeneratedCodeDiagnostics.Describe));

    /// <summary>診断一覧を改行区切りの読みやすい文字列にまとめる（テスト失敗メッセージ用）</summary>
    public string DescribeWarnings() =>
        string.Join(Environment.NewLine, Warnings.Select(GeneratedCodeDiagnostics.Describe));
}

/// <summary>
/// <see cref="GeneratedCodeCompiler.Emit"/> の結果
/// </summary>
internal sealed class GeneratedCodeEmitResult
{
    /// <summary>コンパイルエラーが 0 件かどうか</summary>
    public required bool Success { get; init; }

    /// <summary>コンパイルエラー診断の一覧</summary>
    public required IReadOnlyList<Diagnostic> Errors { get; init; }

    /// <summary>成功時の PE イメージ（失敗時は null）</summary>
    public required byte[]? Image { get; init; }

    /// <summary>エラー診断を改行区切りの読みやすい文字列にまとめる（テスト失敗メッセージ用）</summary>
    public string DescribeErrors() =>
        string.Join(Environment.NewLine, Errors.Select(GeneratedCodeDiagnostics.Describe));
}

/// <summary>診断の表記（<c>[ID] パス:行 メッセージ</c>）</summary>
internal static class GeneratedCodeDiagnostics
{
    /// <summary>診断を 1 行の表記にする</summary>
    public static string Describe(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        return $"[{diagnostic.Id}] {lineSpan.Path}:{lineSpan.StartLinePosition.Line + 1} {diagnostic.GetMessage()}";
    }
}
