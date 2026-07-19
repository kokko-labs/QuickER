using System.IO;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// <see cref="RuntimePackageSourceRenderer"/> が書き出す 4 パッケージ（Core / SqlServer / Sqlite / EfCore）のソースが、
/// 「案内どおりの最小依存だけを参照して」Roslyn でコンパイルできることと、依存が排他であることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// Core は BCL のみ（Microsoft.Data.SqlClient / Microsoft.Data.Sqlite / EntityFrameworkCore を参照に含めない）で
/// コンパイル成立＝依存ゼロを証明する。方言／EF Core は Core ソース＋該当依存のみで成立し、他方言の ADO や EF Core を参照しない。
/// </para>
/// <para>
/// 参照集合は実行時の Trusted Platform Assemblies（BCL）をベースに、除外対象アセンブリ
/// （SqlClient / Sqlite / EF Core / DI）をファイル名で取り除いてから、各ケースが許す依存だけを明示的に戻して構築する。
/// </para>
/// </remarks>
public class RuntimePackageSourceRendererTests
{
    private readonly RuntimePackageSourceRenderer _renderer = new();

    /// <summary>Core は BCL のみ（ADO / EF Core / DI なし）でコンパイルでき、診断ゼロになる</summary>
    [Fact]
    public void RenderCore_CompilesWithFrameworkReferencesOnly()
    {
        var core = _renderer.RenderCore();

        var result = Compile([core], allowSqlClient: false, allowSqlite: false, allowEfCore: false);

        result
            .Success.Should()
            .BeTrue($"Core は BCL のみで成立するはず:{Environment.NewLine}{result.Describe()}");
    }

    /// <summary>SqlServer は Core＋SqlClient 参照でコンパイルでき、Sqlite / EF Core 参照なしで成立する</summary>
    [Fact]
    public void RenderSqlServer_CompilesWithCoreAndSqlClientOnly()
    {
        var core = _renderer.RenderCore();
        var sqlServer = _renderer.RenderSqlServer();

        var result = Compile(
            [core, sqlServer],
            allowSqlClient: true,
            allowSqlite: false,
            allowEfCore: false
        );

        result
            .Success.Should()
            .BeTrue(
                $"SqlServer は Core＋Microsoft.Data.SqlClient のみで成立するはず:{Environment.NewLine}{result.Describe()}"
            );
    }

    /// <summary>Sqlite は Core＋Microsoft.Data.Sqlite 参照でコンパイルでき、SqlClient / EF Core 参照なしで成立する</summary>
    [Fact]
    public void RenderSqlite_CompilesWithCoreAndSqliteOnly()
    {
        var core = _renderer.RenderCore();
        var sqlite = _renderer.RenderSqlite();

        var result = Compile(
            [core, sqlite],
            allowSqlClient: false,
            allowSqlite: true,
            allowEfCore: false
        );

        result
            .Success.Should()
            .BeTrue(
                $"Sqlite は Core＋Microsoft.Data.Sqlite のみで成立するはず:{Environment.NewLine}{result.Describe()}"
            );
    }

    /// <summary>EfCore は Core＋EF Core 参照でコンパイルでき、SqlClient / Sqlite 参照なしで成立する</summary>
    [Fact]
    public void RenderEfCore_CompilesWithCoreAndEfCoreOnly()
    {
        var core = _renderer.RenderCore();
        var efCore = _renderer.RenderEfCore();

        var result = Compile(
            [core, efCore],
            allowSqlClient: false,
            allowSqlite: false,
            allowEfCore: true
        );

        result
            .Success.Should()
            .BeTrue(
                $"EfCore は Core＋EF Core のみで成立するはず:{Environment.NewLine}{result.Describe()}"
            );
    }

    /// <summary>Core ソースには方言 ADO / EF Core の名前空間文字列が現れない（依存排他の文字列ガード）</summary>
    [Fact]
    public void RenderCore_DoesNotReferenceDialectOrEfNamespaces()
    {
        var core = _renderer.RenderCore();

        core.Should().NotContain("Microsoft.Data.SqlClient");
        core.Should().NotContain("Microsoft.Data.Sqlite");
        core.Should().NotContain("EntityFrameworkCore");
    }

    /// <summary>各パッケージソースは固定名前空間で出力され、方言／EF Core はコア契約を using する</summary>
    [Fact]
    public void RenderedSources_UseFixedNamespacesAndCoreUsing()
    {
        _renderer.RenderCore().Should().Contain($"namespace {RuntimePackages.Core};");

        var sqlServer = _renderer.RenderSqlServer();
        sqlServer.Should().Contain($"namespace {RuntimePackages.SqlServer};");
        sqlServer.Should().Contain($"using {RuntimePackages.Core};");

        var sqlite = _renderer.RenderSqlite();
        sqlite.Should().Contain($"namespace {RuntimePackages.Sqlite};");
        sqlite.Should().Contain($"using {RuntimePackages.Core};");

        var efCore = _renderer.RenderEfCore();
        efCore.Should().Contain($"namespace {RuntimePackages.EntityFrameworkCore};");
        efCore.Should().Contain($"using {RuntimePackages.Core};");
    }

    /// <summary>
    /// 指定した許可依存だけを参照へ含めて、パッケージソース群をコンパイルする。
    /// </summary>
    /// <remarks>
    /// BCL（TPA）から SqlClient / Sqlite / EF Core / DI のアセンブリをファイル名で除外してから、
    /// <paramref name="allowSqlClient"/> 等が true のものだけを明示的に戻す。DI（<c>Microsoft.Extensions.DependencyInjection</c>）は
    /// EF Core 部品（AddGeneratedEfCoreRepositories）だけが必要とする。方言パッケージの DI 登録拡張はスキーマ依存物として
    /// 生成側に出力される（パッケージ書き出しでは抑止）ため、方言許可では DI を戻さない＝DI 非依存をコンパイルで証明する。
    /// </remarks>
    private static CompileResult Compile(
        IReadOnlyList<string> sources,
        bool allowSqlClient,
        bool allowSqlite,
        bool allowEfCore
    )
    {
        var syntaxTrees = sources
            .Select(
                (source, index) =>
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.Latest),
                        path: $"Package{index}.g.cs"
                    )
            )
            .ToArray();

        var references = BuildReferences(allowSqlClient, allowSqlite, allowEfCore);

        var compilation = CSharpCompilation.Create(
            $"QuickER.RuntimePackage.Tests.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        var errors = emitResult
            .Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        return new CompileResult
        {
            Success = emitResult.Success && errors.Length == 0,
            Errors = errors,
        };
    }

    /// <summary>除外対象アセンブリのファイル名（拡張子なし・小文字比較）。許可されない限り参照へ含めない</summary>
    private static readonly IReadOnlyList<string> ExclusiveAssemblyFileNames =
    [
        "Microsoft.Data.SqlClient",
        "Microsoft.Data.Sqlite",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    ];

    /// <summary>許可依存だけを含む参照集合を構築する</summary>
    private static IReadOnlyList<MetadataReference> BuildReferences(
        bool allowSqlClient,
        bool allowSqlite,
        bool allowEfCore
    )
    {
        var referencesByPath = new Dictionary<string, MetadataReference>(
            StringComparer.OrdinalIgnoreCase
        );

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            referencesByPath.TryAdd(path, MetadataReference.CreateFromFile(path));
        }

        // TPA（BCL 全体）をベースにするが、排他対象アセンブリはファイル名で除外する。
        var trustedAssembliesPaths = (
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
        )?.Split(Path.PathSeparator);

        if (trustedAssembliesPaths is not null)
        {
            foreach (var path in trustedAssembliesPaths)
            {
                var fileName = Path.GetFileNameWithoutExtension(path);

                if (ExclusiveAssemblyFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddPath(path);
            }
        }

        // DI（登録拡張）を必要とするのは EF Core 部品（AddGeneratedEfCoreRepositories）だけ。
        // 方言パッケージは DI 非依存（DI 登録拡張はスキーマ依存物として生成側に出力される）のため戻さない。
        if (allowEfCore)
        {
            AddPath(
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection)
                    .Assembly
                    .Location
            );
            AddPath(
                typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)
                    .Assembly
                    .Location
            );
        }

        if (allowSqlClient)
        {
            AddPath(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location);
        }

        if (allowSqlite)
        {
            AddPath(typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.Location);
        }

        if (allowEfCore)
        {
            AddPath(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location);
            AddPath(
                typeof(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions)
                    .Assembly
                    .Location
            );
            AddPath(typeof(Microsoft.EntityFrameworkCore.DeleteBehavior).Assembly.Location);
        }

        return referencesByPath.Values.ToArray();
    }

    /// <summary>コンパイル結果（成功可否とエラー診断）</summary>
    private sealed class CompileResult
    {
        public required bool Success { get; init; }

        public required IReadOnlyList<Diagnostic> Errors { get; init; }

        /// <summary>エラー診断を読みやすい文字列にまとめる（テスト失敗メッセージ用）</summary>
        public string Describe() =>
            string.Join(
                Environment.NewLine,
                Errors.Select(diagnostic =>
                {
                    var lineSpan = diagnostic.Location.GetLineSpan();
                    return $"[{diagnostic.Id}] {lineSpan.Path}:{lineSpan.StartLinePosition.Line + 1} {diagnostic.GetMessage()}";
                })
            );
    }
}
