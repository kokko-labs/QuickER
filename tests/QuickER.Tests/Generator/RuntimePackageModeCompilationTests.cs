using System.IO;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.Generator;

/// <summary>
/// パッケージ参照モード（<see cref="CodeGenerationOptions.UseRuntimePackages"/>）の生成コードが、
/// 「案内ビルダー（<see cref="RuntimePackageReferenceGuidance"/>）が指示するパッケージ集合だけを参照して」
/// Roslyn でコンパイルできることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// ランタイム側は <see cref="RuntimePackageSourceRenderer"/> の出力（Core / SqlServer / Sqlite / EfCore）を
/// in-memory アセンブリへ順にコンパイル（Core → 方言/EF の参照順）して <see cref="MetadataReference"/> 化し、
/// 生成コードのコンパイルへ「案内が指示するパッケージだけ」を渡す。これにより案内の十分性（不足なくコンパイル可能）と
/// 依存最小性（余計なパッケージなしで成立）を同時に証明する。
/// </para>
/// <para>
/// 生成コードが直接参照する外部依存は BCL＋<c>Microsoft.Extensions.DependencyInjection</c>（DI 登録拡張）のみ。
/// 方言 ADO（SqlClient / Sqlite）・EF Core は<b>パッケージ側だけ</b>が参照するため、生成コードのコンパイル参照には含めない。
/// </para>
/// </remarks>
public class RuntimePackageModeCompilationTests
{
    private static readonly RuntimePackageSourceRenderer PackageRenderer = new();

    /// <summary>代表構成×分割の生成→案内どおりのパッケージ参照のみでコンパイル成功を検証する</summary>
    [Theory]
    [MemberData(nameof(PackageModeCases))]
    public void Generate_PackageMode_CompilesWithGuidedPackagesOnly(
        string caseName,
        CodeGenerationOptions baseOptions
    )
    {
        var options = WithPackageMode(baseOptions);
        var diagram = Diagram();

        var (primary, byDialect) = ResolveTypes(diagram, options);
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result
            .HasErrors.Should()
            .BeFalse(
                $"「{caseName}」の生成自体でエラー: "
                    + string.Join(
                        " / ",
                        result
                            .Diagnostics.Where(d =>
                                d.Severity == GenerationDiagnosticSeverity.Error
                            )
                            .Select(d => d.Message)
                    )
            );
        result.Files.Should().NotBeEmpty();

        // 生成コードには固定 infra の定義が一切出ない（パッケージが提供する）
        var allContent = string.Join(Environment.NewLine, result.Files.Select(f => f.Content));
        allContent.Should().NotContain("abstract partial class EntityBase");
        allContent.Should().NotContain("interface IRepository<TEntity");
        allContent.Should().NotContain("class SqlServerRepository<");
        allContent.Should().NotContain("class SqliteRepository<");

        // 案内が指示するパッケージ集合だけを参照集合として構築し、生成コードをコンパイルする
        var packages = RuntimePackageReferenceGuidance.Compute(options);
        var packageRefs = BuildPackageReferences(packages);

        var compilation = CompileGenerated(result, packages, packageRefs);

        compilation
            .Success.Should()
            .BeTrue(
                $"「{caseName}」の生成コードが案内どおりのパッケージ参照でコンパイル不能:{Environment.NewLine}{compilation.Describe()}"
            );
    }

    /// <summary>Entity のみ（Repository/EF なし）は Core だけで成立し、参照集合に ADO/EF が含まれない</summary>
    [Fact]
    public void EntityOnly_GuidanceIsCoreOnly_NoAdoOrEf()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateEditModels = true,
                GenerateMappers = true,
                GenerateRepositories = false,
            }
        );

        var packages = RuntimePackageReferenceGuidance.Compute(options);

        packages.Should().Equal(RuntimePackages.Core);
        packages.Should().NotContain(RuntimePackages.SqlServer);
        packages.Should().NotContain(RuntimePackages.Sqlite);
        packages.Should().NotContain(RuntimePackages.EntityFrameworkCore);
    }

    /// <summary>SQLite 単独の生成物に "Microsoft.Data.SqlClient" が現れない（依存排他の文字列ガード）</summary>
    [Fact]
    public void SqliteOnly_GeneratedCode_DoesNotReferenceSqlClient()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialect = "sqlite",
            }
        );
        var diagram = Diagram();
        var (primary, byDialect) = ResolveTypes(diagram, options);

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();

        var allContent = string.Join(Environment.NewLine, result.Files.Select(f => f.Content));
        allContent.Should().NotContain("Microsoft.Data.SqlClient");

        // 案内は Core＋Sqlite のみ（SqlServer / EF を含まない）
        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.Sqlite);
    }

    /// <summary>マルチターゲット（sqlserver+sqlite）の案内は Core＋両方言（EF なし）になる</summary>
    [Fact]
    public void MultiTarget_Guidance_IsCoreAndBothDialects()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialects = ["sqlserver", "sqlite"],
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer, RuntimePackages.Sqlite);
    }

    /// <summary>EF 単独×パッケージの案内は Core＋EF のみ（ADO を含まない）になる</summary>
    [Fact]
    public void EfCoreOnly_Guidance_IsCoreAndEfCore_NoAdo()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCore = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.EntityFrameworkCore);
    }

    /// <summary>自作 sqlserver＋EF 併存×パッケージの案内は Core＋SqlServer＋EF になる</summary>
    [Fact]
    public void RepositoryPlusEfCore_Guidance_IsCoreSqlServerAndEfCore()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = true,
                GenerateEfCore = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(
                RuntimePackages.Core,
                RuntimePackages.SqlServer,
                RuntimePackages.EntityFrameworkCore
            );
    }

    /// <summary>PackageReference 行の案内が指定バージョンで組み立てられる</summary>
    [Fact]
    public void BuildPackageReferenceLines_ProducesVersionedLines()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions { NamespaceName = "Sample.Domain" }
        );

        var lines = RuntimePackageReferenceGuidance.BuildPackageReferenceLines(options, "9.9.9");

        lines
            .Should()
            .Contain("<PackageReference Include=\"QuickER.Runtime\" Version=\"9.9.9\" />");
        lines
            .Should()
            .Contain(
                "<PackageReference Include=\"QuickER.Runtime.SqlServer\" Version=\"9.9.9\" />"
            );
    }

    // ---- テストデータ ----

    /// <summary>代表構成×分割: (a) Entity+EditModel+Mapper+VO のみ (b) 自作 sqlserver (c) 自作 sqlite (d) マルチターゲット</summary>
    public static TheoryData<string, CodeGenerationOptions> PackageModeCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();

        foreach (var split in new[] { false, true })
        {
            data.Add(
                $"Entity+EditModel+Mapper+VO のみ Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateValueObjects = true,
                    GenerateRepositories = false,
                }
            );
            data.Add(
                $"自作 sqlserver 単独 Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                }
            );
            data.Add(
                $"自作 sqlite 単独 Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    RepositoryDialect = "sqlite",
                }
            );
            data.Add(
                $"マルチターゲット sqlserver+sqlite Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    RepositoryDialects = ["sqlserver", "sqlite"],
                }
            );
            data.Add(
                $"EF 単独 Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = false,
                    GenerateEfCore = true,
                }
            );
            data.Add(
                $"自作 sqlserver＋EF 併存 Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = true,
                    GenerateEfCore = true,
                }
            );
        }

        // リモート契約生成×パッケージ参照モード（IRemoteRepository は Core パッケージが提供する）
        data.Add(
            "remote 自作 sqlserver",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRemoteContracts = true,
            }
        );
        data.Add(
            "remote マルチターゲット sqlserver+sqlite",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialects = ["sqlserver", "sqlite"],
                GenerateRemoteContracts = true,
            }
        );
        data.Add(
            "remote 自作 sqlite＋EF 併存",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialect = "sqlite",
                GenerateEfCore = true,
                GenerateRemoteContracts = true,
            }
        );

        return data;
    }

    private static CodeGenerationOptions WithPackageMode(CodeGenerationOptions options) =>
        new()
        {
            NamespaceName = options.NamespaceName,
            OutputFileName = options.OutputFileName,
            GenerateEntityClasses = options.GenerateEntityClasses,
            GenerateEditModels = options.GenerateEditModels,
            GenerateMappers = options.GenerateMappers,
            GenerateRepositories = options.GenerateRepositories,
            RepositoryDialect = options.RepositoryDialect,
            RepositoryDialects = options.RepositoryDialects,
            GenerateEfCore = options.GenerateEfCore,
            IncludeDataAnnotations = options.IncludeDataAnnotations,
            IncludeJsonIgnoreOnParentNavigation = options.IncludeJsonIgnoreOnParentNavigation,
            GenerateValueObjects = options.GenerateValueObjects,
            UseGuidKeyForStringPrimaryKey = options.UseGuidKeyForStringPrimaryKey,
            SplitFilesByCategory = options.SplitFilesByCategory,
            RuntimeNamespace = options.RuntimeNamespace,
            EntityNamespace = options.EntityNamespace,
            EditModelNamespace = options.EditModelNamespace,
            MapperNamespace = options.MapperNamespace,
            RepositoryNamespace = options.RepositoryNamespace,
            ValueObjectNamespace = options.ValueObjectNamespace,
            EfCoreNamespace = options.EfCoreNamespace,
            GenerateRemoteContracts = options.GenerateRemoteContracts,
            UseRuntimePackages = true,
        };

    /// <summary>実効方言に応じ、主辞書＋方言別辞書を用意する（マルチターゲットは byDialect 必須）</summary>
    private static (
        IReadOnlyDictionary<Guid, CSharpTypeInfo> Primary,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> ByDialect
    ) ResolveTypes(ErDiagram diagram, CodeGenerationOptions options)
    {
        var sqlServer = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var sqlite = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram);

        IReadOnlyList<string> dialects;

        try
        {
            dialects = options.EffectiveRepositoryDialects;
        }
        catch (ArgumentException)
        {
            dialects = ["sqlserver"];
        }

        var primaryDialect = dialects[0];
        var primary = string.Equals(primaryDialect, "sqlite", StringComparison.OrdinalIgnoreCase)
            ? sqlite
            : sqlServer;

        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = sqlServer,
            ["sqlite"] = sqlite,
        };

        return (primary, byDialect);
    }

    /// <summary>2 エンティティ・1対多・int/string/decimal（方言可搬）の小さな ER 図</summary>
    private static ErDiagram Diagram()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var order = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();

        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        // 文字列列は Unicode で統一（Ansi/Unicode 差で canonical トークンが割れるため。lessons.md 参照）
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = orderPk,
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "amount",
                            DataType = "decimal(10,2)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    SourceColumnId = customerPk,
                    TargetColumnId = orderFk,
                },
            ],
        };
    }

    // ---- ランタイムパッケージのアセンブリ化と参照集合の構築 ----

    /// <summary>
    /// 案内パッケージ集合を、実際の in-memory アセンブリ参照へ変換する。
    /// </summary>
    /// <remarks>
    /// Core を先にコンパイルし、方言/EF は Core アセンブリを参照してコンパイルする（参照順を守る）。
    /// 各パッケージのコンパイルは、そのパッケージが正当に必要とする外部依存だけを許して行い（依存最小性の担保）、
    /// 生成コードのコンパイルには「案内された各パッケージ＋その参照」を渡す。
    /// </remarks>
    private static IReadOnlyList<MetadataReference> BuildPackageReferences(
        IReadOnlyList<string> packages
    )
    {
        var references = new List<MetadataReference>();

        // Core は必ず先に組む（他パッケージが参照する）
        var coreImage = CompilePackageAssembly(
            RuntimePackages.Core,
            [PackageRenderer.RenderCore()],
            RuntimeReferenceSet.CoreOnly,
            corePeImage: null
        );
        references.Add(MetadataReference.CreateFromImage(coreImage));

        if (packages.Contains(RuntimePackages.SqlServer))
        {
            var image = CompilePackageAssembly(
                RuntimePackages.SqlServer,
                [PackageRenderer.RenderCore(), PackageRenderer.RenderSqlServer()],
                RuntimeReferenceSet.SqlServer,
                corePeImage: coreImage
            );
            references.Add(MetadataReference.CreateFromImage(image));
        }

        if (packages.Contains(RuntimePackages.Sqlite))
        {
            var image = CompilePackageAssembly(
                RuntimePackages.Sqlite,
                [PackageRenderer.RenderCore(), PackageRenderer.RenderSqlite()],
                RuntimeReferenceSet.Sqlite,
                corePeImage: coreImage
            );
            references.Add(MetadataReference.CreateFromImage(image));
        }

        if (packages.Contains(RuntimePackages.EntityFrameworkCore))
        {
            var image = CompilePackageAssembly(
                RuntimePackages.EntityFrameworkCore,
                [PackageRenderer.RenderCore(), PackageRenderer.RenderEfCore()],
                RuntimeReferenceSet.EfCore,
                corePeImage: coreImage
            );
            references.Add(MetadataReference.CreateFromImage(image));
        }

        return references;
    }

    /// <summary>
    /// パッケージソースを 1 アセンブリへコンパイルし、PE イメージ（バイト列）を返す。
    /// </summary>
    /// <remarks>
    /// 方言/EF パッケージは Core の型（EntityBase・IRepository 等）を参照するため、Core の PE イメージを
    /// メタデータ参照として渡す。ソースにも Core を含めているのは、Core を単独アセンブリにするための便宜
    /// （Core アセンブリを渡す構成では方言/EF ソースだけを渡すと二重定義になるため、方言/EF は Core を参照に持ち
    /// 方言/EF ソースのみをコンパイルする）。
    /// </remarks>
    private static byte[] CompilePackageAssembly(
        string assemblyName,
        IReadOnlyList<string> allSources,
        RuntimeReferenceSet referenceSet,
        byte[]? corePeImage
    )
    {
        // Core アセンブリを参照する構成では、方言/EF ソースだけをコンパイルする（Core は参照から解決）。
        var sources = corePeImage is null ? allSources : allSources.Skip(1).ToArray();

        var references = referenceSet.Build();

        if (corePeImage is not null)
        {
            references = [.. references, MetadataReference.CreateFromImage(corePeImage)];
        }

        var syntaxTrees = sources
            .Select(
                (source, index) =>
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.Latest),
                        path: $"{assemblyName}.{index}.g.cs"
                    )
            )
            .ToArray();

        var compilation = CSharpCompilation.Create(
            $"{assemblyName}.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);

        emit.Success.Should()
            .BeTrue(
                $"ランタイムパッケージ '{assemblyName}' のアセンブリ化に失敗:{Environment.NewLine}"
                    + string.Join(
                        Environment.NewLine,
                        emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => d.GetMessage())
                    )
            );

        return peStream.ToArray();
    }

    /// <summary>生成コードを「案内されたパッケージ参照＋BCL＋DI（＋EF 生成時のみ EF Core）」だけでコンパイルする</summary>
    /// <remarks>
    /// 生成コードが直接使うのは BCL＋DI（登録拡張）＋案内パッケージ。方言 ADO（SqlClient / Sqlite）はパッケージ側だけが
    /// 参照するため含めない。ただし EF 生成時は、生成側の QuickErDbContext（<c>: DbContext</c>）や
    /// AddGeneratedEfCoreRepositories（<c>DbContextOptionsBuilder</c> / <c>AddDbContextFactory</c>）が EF Core の型を
    /// 直接参照するため、EF パッケージが案内に含まれるときは EF Core 参照も生成コードのコンパイルへ渡す。
    /// </remarks>
    private static GeneratedCompileResult CompileGenerated(
        CodeGenerationResult result,
        IReadOnlyList<string> packages,
        IReadOnlyList<MetadataReference> packageReferences
    )
    {
        var referenceSet = packages.Contains(RuntimePackages.EntityFrameworkCore)
            ? RuntimeReferenceSet.GeneratedCodeWithEfCore
            : RuntimeReferenceSet.GeneratedCode;
        var references = new List<MetadataReference>(referenceSet.Build());
        references.AddRange(packageReferences);

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
            $"QuickER.PackageMode.Tests.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);

        var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        return new GeneratedCompileResult
        {
            Success = emit.Success && errors.Length == 0,
            Errors = errors,
        };
    }

    private sealed class GeneratedCompileResult
    {
        public required bool Success { get; init; }

        public required IReadOnlyList<Diagnostic> Errors { get; init; }

        public string Describe() =>
            string.Join(
                Environment.NewLine,
                Errors.Select(d =>
                {
                    var span = d.Location.GetLineSpan();
                    return $"[{d.Id}] {span.Path}:{span.StartLinePosition.Line + 1} {d.GetMessage()}";
                })
            );
    }
}

/// <summary>
/// パッケージ／生成コードのコンパイルに渡す参照集合を、許可依存だけを含めて構築するヘルパー。
/// </summary>
/// <remarks>
/// TPA（BCL 全体）をベースに、排他対象アセンブリ（SqlClient / Sqlite / EF Core / DI）をファイル名で除外し、
/// 各構成が許すものだけを明示的に戻す（<see cref="RuntimePackageSourceRendererTests"/> と同じ流儀）。
/// </remarks>
internal sealed class RuntimeReferenceSet
{
    private readonly bool _sqlClient;
    private readonly bool _sqlite;
    private readonly bool _efCore;
    private readonly bool _di;

    private RuntimeReferenceSet(bool sqlClient, bool sqlite, bool efCore, bool di)
    {
        _sqlClient = sqlClient;
        _sqlite = sqlite;
        _efCore = efCore;
        _di = di;
    }

    /// <summary>Core パッケージ: BCL のみ（ADO / EF / DI なし）</summary>
    public static RuntimeReferenceSet CoreOnly { get; } = new(false, false, false, false);

    /// <summary>SqlServer パッケージ: BCL＋SqlClient＋DI</summary>
    public static RuntimeReferenceSet SqlServer { get; } = new(true, false, false, true);

    /// <summary>Sqlite パッケージ: BCL＋Microsoft.Data.Sqlite＋DI</summary>
    public static RuntimeReferenceSet Sqlite { get; } = new(false, true, false, true);

    /// <summary>EfCore パッケージ: BCL＋EF Core＋DI</summary>
    public static RuntimeReferenceSet EfCore { get; } = new(false, false, true, true);

    /// <summary>生成コード: BCL＋DI（登録拡張）のみ。方言 ADO / EF はパッケージ側だけが参照する</summary>
    public static RuntimeReferenceSet GeneratedCode { get; } = new(false, false, false, true);

    /// <summary>生成コード（EF 生成時）: BCL＋DI＋EF Core。生成側の QuickErDbContext / DI 拡張が EF Core 型を直接参照する</summary>
    public static RuntimeReferenceSet GeneratedCodeWithEfCore { get; } =
        new(false, false, true, true);

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

    public IReadOnlyList<MetadataReference> Build()
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

        if (_di)
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

        if (_sqlClient)
        {
            AddPath(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location);
        }

        if (_sqlite)
        {
            AddPath(typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.Location);
        }

        if (_efCore)
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
}
