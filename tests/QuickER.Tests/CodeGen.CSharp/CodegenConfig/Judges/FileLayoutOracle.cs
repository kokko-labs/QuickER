using System.IO;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>生成ファイルの系統（名前空間の規則と組む）</summary>
internal enum FileKind
{
    /// <summary>非分割の本体</summary>
    SingleMain,

    /// <summary>非分割のサーバー実装</summary>
    SingleServer,

    /// <summary>Entities.g.cs</summary>
    Entities,

    /// <summary>EditModels.g.cs</summary>
    EditModels,

    /// <summary>Mappers.g.cs</summary>
    Mappers,

    /// <summary>ValueObjects.g.cs</summary>
    ValueObjects,

    /// <summary>Repositories.g.cs（契約）</summary>
    Contracts,

    /// <summary>Repositories.{方言}.g.cs</summary>
    DialectRepositories,

    /// <summary>Repositories.EntityFrameworkCore.g.cs</summary>
    EfCoreRepositories,

    /// <summary>Repositories.InMemory.g.cs</summary>
    InMemoryRepositories,

    /// <summary>Repositories.Sync.g.cs</summary>
    SyncRepositories,

    /// <summary>Repositories.Http.g.cs</summary>
    HttpRepositories,

    /// <summary>RemoteServer.g.cs</summary>
    RemoteServer,

    /// <summary>Runtime.g.cs</summary>
    Runtime,

    /// <summary>Runtime.{方言}.g.cs</summary>
    RuntimeDialect,

    /// <summary>Runtime.EntityFrameworkCore.g.cs</summary>
    RuntimeEfCore,

    /// <summary>Runtime.InMemory.g.cs</summary>
    RuntimeInMemory,

    /// <summary>Runtime.Sync.g.cs</summary>
    RuntimeSync,

    /// <summary>Runtime.AspNetCore.g.cs</summary>
    RuntimeAspNetCore,

    /// <summary>API リファレンス（.g.md）</summary>
    ApiDoc,
}

/// <summary>期待する生成ファイル 1 つ</summary>
/// <param name="Directory">出力フォルダからの相対フォルダ（区切りは <c>/</c>。直下は null）</param>
/// <param name="FileName">ファイル名</param>
/// <param name="Kind">系統</param>
/// <param name="Dialect">方言の系統なら方言名</param>
internal sealed record ExpectedFile(
    string? Directory,
    string FileName,
    FileKind Kind,
    string? Dialect = null
)
{
    /// <summary>相対パス（区切りは <c>/</c>）</summary>
    public string Path => Directory is null ? FileName : $"{Directory}/{FileName}";
}

/// <summary>
/// ファイル構成の期待（宣言の表：出力の形 × 有効な機能 → ファイル名と層）。<c>GeneratedFilePlanner</c> の関数は期待に使わない
/// </summary>
/// <remarks>
/// <para>出どころ：</para>
/// <list type="bullet">
///   <item>非分割は本体 1 ファイル（<c>OutputFileName</c>。<c>.g.cs</c> で終わらなければ拡張子を付け替える）と、リモートサービスのときだけ
///   <c>{ベース名}.RemoteServer.g.cs</c>（docs/cli.md の OutputFileName・docs「Remote services」）。</item>
///   <item>分割は docs「How generated files map to the packages」の表：<c>Runtime.g.cs</c> は常に（パッケージ参照モードでは
///   <c>Runtime*.g.cs</c> を 1 本も出さない）、方言の 2 本は QuickER 版 Repository、EF Core・インメモリ・ASP.NET Core・同期は各オプション。
///   スキーマ依存は <c>Entities</c> / <c>EditModels</c> / <c>Mappers</c> / <c>ValueObjects</c> と <c>Repositories*</c> / <c>RemoteServer</c>
///   （src/QuickER.CodeGen.CSharp/CLAUDE.md「分割生成のファイル構成」「HTTP クライアントのバケット分離」）。</item>
///   <item>リモート・同期は Repository 契約が無ければ出ない（契約が無い指定は生成時エラー＝docs/cli.md）。</item>
///   <item>層別は docs「Layered folder output」の層の表（Domain / Presentation / Infrastructure / Server）。サブフォルダは
///   <c>.g.cs</c> だけをもう 1 段下へ（docs「The generated-code subfolder」）。</item>
///   <item>API リファレンスは docs「API reference」：英語 <c>{ベース名}.g.md</c>・日本語 <c>{ベース名}.ja.g.md</c>、分割は固定名 <c>ApiDocs</c>、
///   明示のファイル名が勝つ、置き場は <c>ApiDocsSubdirectory</c> だけ。</item>
/// </list>
/// </remarks>
internal static class FileLayoutOracle
{
    /// <summary>オプションから期待するファイルの一覧を作る</summary>
    /// <param name="options">実効の生成オプション</param>
    public static IReadOnlyList<ExpectedFile> Expected(CodeGenerationOptions options)
    {
        var files = new List<ExpectedFile>();
        var namespaces = new CodegenNamespaces(options);
        var contract = CodegenOptionFacts.GeneratesRepositoryContract(options);
        var services = options.GenerateRemoteServices && contract;
        var sync = options.GenerateSyncSupport && contract;
        var dialects = CodegenOptionFacts.Dialects(options);
        var mainFile = NormalizeOutputFileName(options.OutputFileName);
        var codeSubdirectory = string.IsNullOrWhiteSpace(options.CodeSubdirectory)
            ? null
            : options.CodeSubdirectory.Trim().Replace('\\', '/').Trim('/');

        string? Place(string? layer) =>
            (layer, codeSubdirectory) switch
            {
                (null, null) => null,
                (null, _) => codeSubdirectory,
                (_, null) => layer,
                _ => $"{layer}/{codeSubdirectory}",
            };

        void Add(string? layer, string fileName, FileKind kind, string? dialect = null) =>
            files.Add(new ExpectedFile(Place(layer), fileName, kind, dialect));

        if (!CodegenOptionFacts.Split(options))
        {
            Add(null, mainFile, FileKind.SingleMain);

            if (services)
            {
                Add(null, $"{BaseName(mainFile)}.RemoteServer.g.cs", FileKind.SingleServer);
            }
        }
        else
        {
            var layered = options.LayeredOutput;
            var domain = layered ? namespaces.DomainDirectory : null;
            var presentation = layered ? namespaces.PresentationDirectory : null;
            var infrastructure = layered ? namespaces.InfraDirectory : null;
            var server = layered ? namespaces.ServerDirectory : null;
            var inline = !options.UseRuntimePackages;

            Add(domain, "Entities.g.cs", FileKind.Entities);

            if (options.GenerateEditModels)
            {
                Add(presentation, "EditModels.g.cs", FileKind.EditModels);
            }

            if (options.GenerateMappers)
            {
                Add(presentation, "Mappers.g.cs", FileKind.Mappers);
            }

            if (options.GenerateValueObjects)
            {
                Add(domain, "ValueObjects.g.cs", FileKind.ValueObjects);
            }

            if (contract)
            {
                Add(domain, "Repositories.g.cs", FileKind.Contracts);
            }

            if (inline)
            {
                Add(domain, "Runtime.g.cs", FileKind.Runtime);
            }

            if (options.GenerateRepositories)
            {
                foreach (var dialect in dialects)
                {
                    var suffix = CodegenOptionFacts.DialectSuffix(dialect);
                    Add(
                        infrastructure,
                        $"Repositories.{suffix}.g.cs",
                        FileKind.DialectRepositories,
                        dialect
                    );

                    if (inline)
                    {
                        Add(
                            infrastructure,
                            $"Runtime.{suffix}.g.cs",
                            FileKind.RuntimeDialect,
                            dialect
                        );
                    }
                }
            }

            if (options.GenerateEfCoreRepositories)
            {
                Add(
                    infrastructure,
                    "Repositories.EntityFrameworkCore.g.cs",
                    FileKind.EfCoreRepositories
                );

                if (inline)
                {
                    Add(infrastructure, "Runtime.EntityFrameworkCore.g.cs", FileKind.RuntimeEfCore);
                }
            }

            if (options.GenerateInMemoryRepositories)
            {
                Add(infrastructure, "Repositories.InMemory.g.cs", FileKind.InMemoryRepositories);

                if (inline)
                {
                    Add(infrastructure, "Runtime.InMemory.g.cs", FileKind.RuntimeInMemory);
                }
            }

            if (sync)
            {
                Add(infrastructure, "Repositories.Sync.g.cs", FileKind.SyncRepositories);

                if (inline)
                {
                    Add(infrastructure, "Runtime.Sync.g.cs", FileKind.RuntimeSync);
                }
            }

            if (services)
            {
                Add(infrastructure, "Repositories.Http.g.cs", FileKind.HttpRepositories);
                Add(server, "RemoteServer.g.cs", FileKind.RemoteServer);

                if (inline)
                {
                    Add(server, "Runtime.AspNetCore.g.cs", FileKind.RuntimeAspNetCore);
                }
            }
        }

        if (options.GenerateApiDocs)
        {
            var baseName =
                !string.IsNullOrWhiteSpace(options.ApiDocsFileName)
                    ? StripMarkdownExtension(options.ApiDocsFileName.Trim())
                : CodegenOptionFacts.Split(options) ? "ApiDocs"
                : BaseName(mainFile);
            var directory = string.IsNullOrWhiteSpace(options.ApiDocsSubdirectory)
                ? null
                : options.ApiDocsSubdirectory.Trim().Replace('\\', '/').Trim('/');

            if (options.ApiDocsLanguage is ApiDocsLanguage.English or ApiDocsLanguage.Both)
            {
                files.Add(new ExpectedFile(directory, $"{baseName}.g.md", FileKind.ApiDoc));
            }

            if (options.ApiDocsLanguage is ApiDocsLanguage.Japanese or ApiDocsLanguage.Both)
            {
                files.Add(new ExpectedFile(directory, $"{baseName}.ja.g.md", FileKind.ApiDoc));
            }
        }

        return files;
    }

    /// <summary>実際のファイル構成を期待と照合する（過不足を名指しする）</summary>
    /// <param name="options">実効の生成オプション</param>
    /// <param name="result">生成結果</param>
    public static IReadOnlyList<string> Check(
        CodeGenerationOptions options,
        CodeGenerationResult result
    )
    {
        var expected = Expected(options).Select(file => file.Path).ToList();
        var actual = result.Files.Select(PathOf).ToList();
        var problems = new List<string>();

        foreach (var missing in expected.Except(actual).Order(StringComparer.Ordinal))
        {
            problems.Add($"ファイル構成: 期待したファイル {missing} が無い");
        }

        foreach (var extra in actual.Except(expected).Order(StringComparer.Ordinal))
        {
            problems.Add($"ファイル構成: 期待していないファイル {extra} がある");
        }

        if (actual.Count != actual.Distinct(StringComparer.Ordinal).Count())
        {
            problems.Add("ファイル構成: 同じパスのファイルが 2 つ以上ある");
        }

        return problems;
    }

    /// <summary>生成ファイルの相対パス（区切りは <c>/</c>）</summary>
    public static string PathOf(GeneratedFile file) =>
        string.IsNullOrEmpty(file.RelativeDirectory)
            ? file.FileName
            : $"{file.RelativeDirectory.Replace('\\', '/').TrimEnd('/')}/{file.FileName}";

    /// <summary>非分割の出力ファイル名（空白は既定、<c>.g.cs</c> で終わらなければ拡張子を 1 つ外して付ける）</summary>
    private static string NormalizeOutputFileName(string? fileName)
    {
        var value = string.IsNullOrWhiteSpace(fileName) ? "QuickEREntities.g.cs" : fileName.Trim();
        return value.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            ? value
            : Path.GetFileNameWithoutExtension(value) + ".g.cs";
    }

    private static string BaseName(string generatedFileName) =>
        generatedFileName[..^".g.cs".Length];

    private static string StripMarkdownExtension(string fileName)
    {
        foreach (var suffix in new[] { ".g.md", ".md" })
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^suffix.Length];
            }
        }

        return fileName;
    }
}
