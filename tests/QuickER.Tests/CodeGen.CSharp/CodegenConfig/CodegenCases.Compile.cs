using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：コンパイル（元の <c>GeneratedCodeCompilationTests</c> の行列と、<c>RuntimePackageModeCompilationTests</c> の
/// 案内どおりのパッケージ参照だけでのコンパイル）
/// </summary>
/// <remarks>
/// <para>
/// <c>GeneratedCodeCompilationTests</c> の行は図 <see cref="CompileDiagrams.FullCoverage"/>（GuidKey の単発オプションだけ
/// <see cref="CompileDiagrams.StringKey"/>）で、元は SQL Server の型マッパーで型を解決していたので図の方言を SQL Server にする。
/// 入口は direct（本番と同じ <c>DiagramCodeGenerator.Generate</c>＝方言の中立トークンの付加と、実効方言ごとの型マッパーを通る）。
/// </para>
/// <para>
/// 層別の行は 2 つの形を持つ：<c>compile-layered-one-assembly-*</c> は元の <c>Generate_LayeredOutputMatrix_ShouldProduceCompilableCode</c> と同じ
/// 1 アセンブリ（層をまたぐ同名の型などは 1 アセンブリでしか出ない）、<c>compile-layered-projects-*</c> は元の
/// <c>Generate_Layered_CompilesAsSeparateProjects</c> と同じ層ごとの別プロジェクト（固定 infra の可視性は別プロジェクトでしか出ない）。
/// </para>
/// </remarks>
internal static partial class CodegenCases
{
    /// <summary>コンパイルの行</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> CompileRows() =>
        [.. CompileMatrixRows(), .. CompileSingleOptionRows(), .. CompilePackageRows()];

    /// <summary>生成が 1 ファイル以上ある（元の <c>result.Files.Should().NotBeEmpty()</c>。層別でも数えるので置き場は見ない）</summary>
    private static FileRule CompileGenerated => new(".g.cs", NonEmpty: true, AnyDirectory: true);

    /// <summary>サーバー実装のファイルがちょうど 1 つ出る（非分割は <c>{ベース名}.RemoteServer.g.cs</c>・分割は <c>RemoteServer.g.cs</c>）</summary>
    private static FileRule CompileRemoteServerFile => new("RemoteServer.g.cs", Count: 1);

    /// <summary>コンパイルの行を組む（直接の入口・コンパイルする）</summary>
    private static ScenarioRow<CodegenCase> CompileRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        string typeDialect = DialectNames.SqlServer,
        DiagnosticExpectation? diagnostics = null,
        IReadOnlyList<FileRule>? fileRules = null,
        bool fileLayout = false,
        bool oneAssembly = false,
        CodeGenerationOptions? comparisonOptions = null
    ) =>
        new(
            id,
            null,
            description,
            new CodegenCase(
                diagram,
                options,
                typeDialect,
                diagnostics ?? DiagnosticExpectation.Contains(),
                FileRules: [CompileGenerated, .. fileRules ?? []],
                FileLayout: fileLayout,
                Compile: true,
                Relations: comparisonOptions is null ? null : [CodegenRelation.OptionEffect],
                ComparisonOptions: comparisonOptions,
                CompileAsOneAssembly: oneAssembly
            )
        );

    /// <summary>分割 × 値オブジェクトの 4 象限の行（Id は <c>{接頭辞}-{inline|split}[-vo]</c>、説明の括弧は元のケース名）</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> CompileSplitVoRows(
        string idPrefix,
        string method,
        string caseNamePrefix,
        Func<bool, bool, CodeGenerationOptions> options,
        IReadOnlyList<FileRule>? fileRules = null
    )
    {
        foreach (var split in new[] { false, true })
        {
            foreach (var vo in new[] { false, true })
            {
                yield return CompileRow(
                    $"{idPrefix}-{(split ? "split" : "inline")}{(vo ? "-vo" : string.Empty)}",
                    $"{method}（{caseNamePrefix} Split={split} VO={vo}）",
                    CompileDiagrams.FullCoverage,
                    options(split, vo),
                    fileRules: fileRules
                );
            }
        }
    }

    /// <summary>元の <c>GeneratedCodeCompilationTests</c> のケース名の接頭辞</summary>
    private const string CompileSource = "GeneratedCodeCompilationTests.";

    /// <summary>行列（単発オプション以外）の行</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> CompileMatrixRows() =>
        [
            // ===== 全カテゴリ（FullMatrixCases）。ReducedCategoryCases の「Entity+EditModel+Mapper」もここ =====
            .. CompileSplitVoRows(
                "compile-full",
                CompileSource
                    + "Generate_FullMatrix_ShouldProduceCompilableCode・Generate_ReducedCategories_ShouldProduceCompilableCode（VO=False は「Entity+EditModel+Mapper」と同じオプション）",
                "全カテゴリ",
                (split, vo) =>
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = split,
                        GenerateValueObjects = vo,
                    }
            ),
            // ===== EF Core（EfCoreMatrixCases・EfCoreOnlyMatrixCases は GenerateRepositories の既定が false なので同じオプション） =====
            .. CompileSplitVoRows(
                "compile-efcore",
                CompileSource
                    + "Generate_EfCoreMatrix_ShouldProduceCompilableCode・Generate_EfCoreOnlyMatrix_ShouldProduceCompilableCode（「EfCore 単独」は同じオプション）",
                "EfCore",
                (split, vo) =>
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = split,
                        GenerateValueObjects = vo,
                        GenerateEfCoreRepositories = true,
                    }
            ),
            CompileRow(
                "compile-efcore-exclude-unbounded-binary",
                CompileSource
                    + "Generate_EfCoreMatrix_ShouldProduceCompilableCode（EfCore + QuickER 版 Repository + ExcludeUnboundedBinaryColumns）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateEfCoreRepositories = true,
                    ExcludeUnboundedBinaryColumns = true,
                }
            ),
            // ===== SQLite 方言（SqliteRepositoryMatrixCases） =====
            .. CompileSplitVoRows(
                "compile-sqlite-dialect",
                CompileSource + "Generate_SqliteRepositoryMatrix_ShouldProduceCompilableCode",
                "SQLite Repository",
                (split, vo) =>
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = split,
                        GenerateValueObjects = vo,
                        RepositoryDialects = [DialectNames.Sqlite],
                    }
            ),
            CompileRow(
                "compile-sqlite-dialect-efcore",
                CompileSource
                    + "Generate_SqliteRepositoryMatrix_ShouldProduceCompilableCode（SQLite Repository + EF Core）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    RepositoryDialects = [DialectNames.Sqlite],
                    GenerateEfCoreRepositories = true,
                }
            ),
            // ===== インメモリ（InMemoryRepositoryMatrixCases） =====
            .. CompileSplitVoRows(
                "compile-inmemory",
                CompileSource + "Generate_InMemoryRepositoryMatrix_ShouldProduceCompilableCode",
                "InMemory 単独",
                (split, vo) =>
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = split,
                        GenerateValueObjects = vo,
                        GenerateRepositories = false,
                        GenerateEfCoreRepositories = false,
                        GenerateInMemoryRepositories = true,
                    }
            ),
            CompileRow(
                "compile-inmemory-efcore",
                CompileSource
                    + "Generate_InMemoryRepositoryMatrix_ShouldProduceCompilableCode（InMemory + EF Core）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                    GenerateInMemoryRepositories = true,
                }
            ),
            CompileRow(
                "compile-inmemory-sqlserver-repository",
                CompileSource
                    + "Generate_InMemoryRepositoryMatrix_ShouldProduceCompilableCode（InMemory + QuickER sqlserver Repository）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateInMemoryRepositories = true,
                }
            ),
            .. new[] { DialectNames.SqlServer, DialectNames.Sqlite }.Select(dialect =>
                CompileRow(
                    $"compile-inmemory-{dialect}-repository-split",
                    CompileSource
                        + $"Generate_InMemoryRepositoryMatrix_ShouldProduceCompilableCode（InMemory + QuickER {dialect} Repository Split）",
                    CompileDiagrams.FullCoverage,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = true,
                        GenerateRepositories = true,
                        RepositoryDialects = [dialect],
                        GenerateInMemoryRepositories = true,
                    }
                )
            ),
            CompileRow(
                "compile-inmemory-multi-target",
                CompileSource
                    + "Generate_InMemoryRepositoryMatrix_ShouldProduceCompilableCode（InMemory + QuickER マルチターゲット(sqlserver/sqlite)）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    GenerateInMemoryRepositories = true,
                }
            ),
            // ===== カテゴリ削減（ReducedCategoryCases。「Entity+EditModel+Mapper」は compile-full-inline / compile-full-split と同じオプション） =====
            .. new[] { false, true }.SelectMany(split =>
                new[]
                {
                    CompileRow(
                        $"compile-reduced-entity-only-{(split ? "split" : "inline")}",
                        CompileSource
                            + $"Generate_ReducedCategories_ShouldProduceCompilableCode（Entity のみ Split={split}）",
                        CompileDiagrams.FullCoverage,
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateEditModels = false,
                            GenerateMappers = false,
                            GenerateRepositories = false,
                        }
                    ),
                    CompileRow(
                        $"compile-reduced-entity-repository-{(split ? "split" : "inline")}",
                        CompileSource
                            + $"Generate_ReducedCategories_ShouldProduceCompilableCode（Entity+Repository（EditModel/Mapper 抜き） Split={split}）",
                        CompileDiagrams.FullCoverage,
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateEditModels = false,
                            GenerateMappers = false,
                            GenerateRepositories = true,
                        }
                    ),
                }
            ),
            // ===== リモート契約（RemoteContractMatrixCases） =====
            .. CompileSplitVoRows(
                "compile-remote-contracts-sqlserver",
                CompileSource + "Generate_RemoteContractMatrix_ShouldProduceCompilableCode",
                "remote QuickER sqlserver",
                (split, vo) =>
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = split,
                        GenerateValueObjects = vo,
                        GenerateRepositories = true,
                        GenerateRemoteContracts = true,
                    }
            ),
            CompileRow(
                "compile-remote-contracts-efcore-only",
                CompileSource
                    + "Generate_RemoteContractMatrix_ShouldProduceCompilableCode（remote EF Core 単独）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                    GenerateRemoteContracts = true,
                }
            ),
            CompileRow(
                "compile-remote-contracts-sqlite-efcore",
                CompileSource
                    + "Generate_RemoteContractMatrix_ShouldProduceCompilableCode（remote SQLite + EF Core）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    RepositoryDialects = [DialectNames.Sqlite],
                    GenerateEfCoreRepositories = true,
                    GenerateRemoteContracts = true,
                }
            ),
            CompileRow(
                "compile-remote-contracts-inmemory-only",
                CompileSource
                    + "Generate_RemoteContractMatrix_ShouldProduceCompilableCode（remote InMemory 単独）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = false,
                    GenerateInMemoryRepositories = true,
                    GenerateRemoteContracts = true,
                }
            ),
            CompileRow(
                "compile-remote-contracts-multi-target",
                CompileSource
                    + "Generate_RemoteContractMatrix_ShouldProduceCompilableCode（remote マルチターゲット(sqlserver/sqlite)）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    GenerateRepositories = true,
                    GenerateRemoteContracts = true,
                }
            ),
            // ===== 双方向同期（SyncSupportMatrixCases） =====
            .. CompileSplitVoRows(
                "compile-sync-support",
                CompileSource + "Generate_SyncSupportMatrix_ShouldProduceCompilableCode",
                "同期支援",
                (split, vo) =>
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = split,
                        GenerateValueObjects = vo,
                        GenerateRepositories = true,
                        RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                        GenerateSyncSupport = true,
                    }
            ),
            // ===== リモートサービス（RemoteServiceMatrixCases。元は本体だけをコンパイルしていた。エンジンはサーバーファイルもコンパイルする） =====
            .. CompileSplitVoRows(
                "compile-remote-services-sqlserver",
                CompileSource + "Generate_RemoteServiceMatrix_MainOutputShouldCompile",
                "remote-services QuickER sqlserver",
                (split, vo) =>
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = split,
                        GenerateValueObjects = vo,
                        GenerateRepositories = true,
                        GenerateRemoteServices = true,
                    },
                [CompileRemoteServerFile]
            ),
            .. new[] { false, true }.SelectMany(split =>
                new[]
                {
                    CompileRow(
                        $"compile-remote-services-sqlite-efcore-{(split ? "split" : "inline")}",
                        CompileSource
                            + $"Generate_RemoteServiceMatrix_MainOutputShouldCompile（remote-services SQLite + EF Core Split={split}）",
                        CompileDiagrams.FullCoverage,
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            RepositoryDialects = [DialectNames.Sqlite],
                            GenerateEfCoreRepositories = true,
                            GenerateRemoteServices = true,
                        },
                        fileRules: [CompileRemoteServerFile]
                    ),
                    CompileRow(
                        $"compile-remote-services-efcore-only-{(split ? "split" : "inline")}",
                        CompileSource
                            + $"Generate_RemoteServiceMatrix_MainOutputShouldCompile（remote-services EF Core 単独 Split={split}）",
                        CompileDiagrams.FullCoverage,
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = false,
                            GenerateEfCoreRepositories = true,
                            GenerateRemoteServices = true,
                        },
                        fileRules: [CompileRemoteServerFile]
                    ),
                }
            ),
            // ===== 層別・1 アセンブリ（LayeredOutputMatrixCases） =====
            CompileRow(
                "compile-layered-one-assembly-remote-services-vo",
                CompileSource
                    + "Generate_LayeredOutputMatrix_ShouldProduceCompilableCode（layered QuickER + remote-services + VO）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    LayeredOutput = true,
                    GenerateValueObjects = true,
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                },
                oneAssembly: true
            ),
            CompileRow(
                "compile-layered-one-assembly-efcore-only",
                CompileSource
                    + "Generate_LayeredOutputMatrix_ShouldProduceCompilableCode（layered EF Core 単独）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    LayeredOutput = true,
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                },
                oneAssembly: true
            ),
            CompileRow(
                "compile-layered-one-assembly-multi-target-sync-inmemory-remote",
                CompileSource
                    + "Generate_LayeredOutputMatrix_ShouldProduceCompilableCode（layered multi-target + sync + in-memory + remote）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    GenerateSyncSupport = true,
                    GenerateInMemoryRepositories = true,
                    GenerateRemoteServices = true,
                },
                oneAssembly: true
            ),
            CompileRow(
                "compile-layered-one-assembly-custom-folders-explicit-entity-namespace",
                CompileSource
                    + "Generate_LayeredOutputMatrix_ShouldProduceCompilableCode（layered custom folders + explicit entity namespace）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    DomainLayerDirectory = "MyApp.Domain/Generated",
                    InfrastructureLayerDirectory = "MyApp.Infrastructure",
                    EntityNamespace = "MyApp.Domain.Model",
                },
                oneAssembly: true
            ),
            // ===== 層別・層ごとの別プロジェクト（LayeredProjectCases。元の「層のファイルがある」はファイル構成の規則で見る） =====
            CompileRow(
                "compile-layered-projects-remote-inmemory-vo",
                CompileSource
                    + "Generate_Layered_CompilesAsSeparateProjects（layered 4-projects QuickER + remote + in-memory + VO）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    LayeredOutput = true,
                    GenerateValueObjects = true,
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                    GenerateInMemoryRepositories = true,
                },
                fileLayout: true
            ),
            CompileRow(
                "compile-layered-projects-multi-target-sync-remote",
                CompileSource
                    + "Generate_Layered_CompilesAsSeparateProjects（layered 4-projects multi-target + sync + remote）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    GenerateSyncSupport = true,
                    GenerateRemoteServices = true,
                },
                fileLayout: true
            ),
        ];

    /// <summary>
    /// 単発オプション（SingleOptionCases）の行：コンパイルと、関係 OptionEffect（比較用＝そのオプションを既定へ戻したオプションの生成物と違い、
    /// 同じオプションで生成し直した生成物とは同じ）
    /// </summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> CompileSingleOptionRows() =>
        [
            CompileRow(
                "compile-single-option-no-data-annotations",
                CompileSource
                    + "Generate_SingleOptions_ShouldProduceCompilableCode・Generate_SingleOptions_ChangeTheOutput（IncludeDataAnnotations=false（Repository 生成不可のため除外））",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    IncludeDataAnnotations = false,
                    GenerateRepositories = false,
                },
                comparisonOptions: new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = false,
                }
            ),
            CompileRow(
                "compile-single-option-no-json-ignore-parent-navigation",
                CompileSource
                    + "Generate_SingleOptions_ShouldProduceCompilableCode・Generate_SingleOptions_ChangeTheOutput（IncludeJsonIgnoreOnParentNavigation=false）",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    IncludeJsonIgnoreOnParentNavigation = false,
                },
                comparisonOptions: new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
            ),
            CompileRow(
                "compile-single-option-guid-key-string-key",
                CompileSource
                    + "Generate_SingleOptions_ShouldProduceCompilableCode・Generate_SingleOptions_ChangeTheOutput（UseGuidKeyForStringPrimaryKey=true（VO 有効時のみ適用・名前付きクエリのため Repository も生成））・GuidKeyCase_EmitsGuidKeyValueObjectSharedByForeignKey の警告（部分一致の 2 本は警告で飛ばす）",
                CompileDiagrams.StringKey,
                CompileDiagrams.GuidKeyOptions(),
                diagnostics: DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                        "CodeContains"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                        "OwnerCodeStartsWith"
                    )
                ),
                comparisonOptions: CompileDiagrams.GuidKeyOptions() with
                {
                    UseGuidKeyForStringPrimaryKey = false,
                }
            ),
            CompileRow(
                "compile-single-option-exclude-unbounded-binary",
                CompileSource
                    + "Generate_SingleOptions_ShouldProduceCompilableCode・Generate_SingleOptions_ChangeTheOutput（ExcludeUnboundedBinaryColumns=true（varbinary(max) の photo 列にマーカー付与））",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    ExcludeUnboundedBinaryColumns = true,
                },
                comparisonOptions: new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
            ),
            CompileRow(
                "compile-single-option-root-namespace-inline",
                CompileSource
                    + "Generate_SingleOptions_ShouldProduceCompilableCode・Generate_SingleOptions_ChangeTheOutput（Namespace 上書き（Split off・単一 RootNamespace））",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions { RootNamespace = "Acme.Custom.Domain" },
                comparisonOptions: new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
            ),
            CompileRow(
                "compile-single-option-category-namespaces-split",
                CompileSource
                    + "Generate_SingleOptions_ShouldProduceCompilableCode・Generate_SingleOptions_ChangeTheOutput（Namespace 上書き（Split on・カテゴリ別名前空間））",
                CompileDiagrams.FullCoverage,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = true,
                    RuntimeNamespace = "Acme.Shared.Runtime",
                    EntityNamespace = "Acme.Domain.Entities",
                    EditModelNamespace = "Acme.Domain.EditModels",
                    MapperNamespace = "Acme.Domain.Mappers",
                    RepositoryNamespace = "Acme.Domain.Repositories",
                },
                comparisonOptions: new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = true,
                }
            ),
        ];

    // ===== パッケージ参照（RuntimePackageModeCompilationTests.Generate_PackageMode_CompilesWithGuidedPackagesOnly） =====

    /// <summary>元のケース名の接頭辞</summary>
    private const string CompilePackageSource =
        "RuntimePackageModeCompilationTests.Generate_PackageMode_CompilesWithGuidedPackagesOnly";

    /// <summary>
    /// パッケージ参照の行を組む（UseRuntimePackages=true を足す。コンパイルは案内のパッケージだけを参照する集合＝<see cref="CompileJudge"/>）
    /// </summary>
    /// <remarks>
    /// 図の方言は元の型の解決と同じ（主辞書は実効方言の先頭が sqlite なら SQLite、ほかは SQL Server）。
    /// </remarks>
    private static ScenarioRow<CodegenCase> CompilePackageRow(
        string id,
        string caseName,
        CodeGenerationOptions options,
        bool remoteServer = false,
        bool oneAssembly = false
    )
    {
        var packageOptions = options with { UseRuntimePackages = true };
        var primaryDialect = packageOptions.ResolveRepositoryDialects()[0];

        return CompileRow(
            id,
            $"{CompilePackageSource}（{caseName}）",
            Compile_PackageModeDiagram,
            packageOptions,
            string.Equals(primaryDialect, DialectNames.Sqlite, StringComparison.OrdinalIgnoreCase)
                ? DialectNames.Sqlite
                : DialectNames.SqlServer,
            fileRules: remoteServer ? [CompileRemoteServerFile] : null,
            oneAssembly: oneAssembly
        );
    }

    /// <summary>パッケージ参照の行（元の PackageModeCases の 28 通り）</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> CompilePackageRows() =>
        [
            .. new[] { false, true }.SelectMany(split =>
            {
                var form = split ? "split" : "inline";

                return new[]
                {
                    CompilePackageRow(
                        $"compile-package-entity-editmodel-mapper-vo-{form}",
                        $"Entity+EditModel+Mapper+VO のみ Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateValueObjects = true,
                            GenerateRepositories = false,
                        }
                    ),
                    CompilePackageRow(
                        $"compile-package-sqlserver-{form}",
                        $"QuickER sqlserver 単独 Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = true,
                        }
                    ),
                    CompilePackageRow(
                        $"compile-package-sqlite-{form}",
                        $"QuickER sqlite 単独 Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = true,
                            RepositoryDialects = [DialectNames.Sqlite],
                        }
                    ),
                    CompilePackageRow(
                        $"compile-package-multi-target-{form}",
                        $"マルチターゲット sqlserver+sqlite Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = true,
                            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                        }
                    ),
                    CompilePackageRow(
                        $"compile-package-efcore-only-{form}",
                        $"EF Core 単独 Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = false,
                            GenerateEfCoreRepositories = true,
                        }
                    ),
                    CompilePackageRow(
                        $"compile-package-sqlserver-efcore-{form}",
                        $"QuickER sqlserver＋EF Core 併存 Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = true,
                            GenerateEfCoreRepositories = true,
                        }
                    ),
                    CompilePackageRow(
                        $"compile-package-inmemory-only-{form}",
                        $"インメモリ単独 Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = false,
                            GenerateInMemoryRepositories = true,
                        }
                    ),
                    CompilePackageRow(
                        $"compile-package-sqlserver-inmemory-{form}",
                        $"QuickER sqlserver＋インメモリ併存 Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = true,
                            GenerateInMemoryRepositories = true,
                        }
                    ),
                    CompilePackageRow(
                        $"compile-package-inmemory-vo-exclude-unbounded-binary-{form}",
                        $"インメモリ＋VO＋バイナリ除外 Split={split}",
                        new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.Domain",
                            SplitFilesByCategory = split,
                            GenerateRepositories = true,
                            GenerateInMemoryRepositories = true,
                            GenerateValueObjects = true,
                            ExcludeUnboundedBinaryColumns = true,
                        }
                    ),
                };
            }),
            CompilePackageRow(
                "compile-package-remote-contracts-sqlserver",
                "remote QuickER sqlserver",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    GenerateRemoteContracts = true,
                }
            ),
            CompilePackageRow(
                "compile-package-remote-contracts-multi-target",
                "remote マルチターゲット sqlserver+sqlite",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    GenerateRemoteContracts = true,
                }
            ),
            CompilePackageRow(
                "compile-package-remote-contracts-sqlite-efcore",
                "remote QuickER sqlite＋EF Core 併存",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.Sqlite],
                    GenerateEfCoreRepositories = true,
                    GenerateRemoteContracts = true,
                }
            ),
            CompilePackageRow(
                "compile-package-remote-services-sqlserver-inline",
                "remote-services QuickER sqlserver",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                },
                remoteServer: true
            ),
            CompilePackageRow(
                "compile-package-remote-services-sqlserver-split",
                "remote-services QuickER sqlserver Split=true",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                },
                remoteServer: true
            ),
            CompilePackageRow(
                "compile-package-remote-services-sqlserver-exclude-unbounded-binary",
                "remote-services QuickER sqlserver＋バイナリ除外",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                    ExcludeUnboundedBinaryColumns = true,
                },
                remoteServer: true
            ),
            .. new[] { false, true }.Select(split =>
                CompilePackageRow(
                    $"compile-package-sync-support-{(split ? "split" : "inline")}",
                    $"同期支援 sqlserver+sqlite Split={split}",
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        SplitFilesByCategory = split,
                        GenerateRepositories = true,
                        RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                        GenerateSyncSupport = true,
                    }
                )
            ),
            // 層別×パッケージ参照（元は 1 アセンブリへの一括コンパイル）
            CompilePackageRow(
                "compile-package-layered-sqlserver",
                "層別 QuickER sqlserver",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                },
                oneAssembly: true
            ),
            CompilePackageRow(
                "compile-package-layered-sync-support",
                "層別×同期 sqlserver+sqlite",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    GenerateSyncSupport = true,
                },
                oneAssembly: true
            ),
        ];

    /// <summary>RuntimePackageModeCompilationTests.Diagram（2 エンティティ・1対多・int/string/decimal（方言可搬）＋無制限バイナリ列）</summary>
    private static ErDiagram Compile_PackageModeDiagram()
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
                        // 文字列列は Unicode で統一（Ansi/Unicode 差で canonical トークンが割れるため）
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
                        // 無制限バイナリ列（ExcludeUnboundedBinaryColumns のケースで Stream アクセサ・
                        // 除外ガードの固定 infra 参照までコンパイル検証させる）
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "attachment",
                            DataType = "varbinary(max)",
                            IsNullable = true,
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
                    ColumnPairs = [new(customerPk, orderFk)],
                },
            ],
        };
    }
}
