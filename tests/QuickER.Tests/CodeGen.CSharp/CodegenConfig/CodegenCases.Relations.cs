using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture;
using QuickER.Tests.GeneratedPortableFixture;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：関係（同じ行の変種を生成して比べる）。層別と分割・サブフォルダ・パッケージ参照のオン／オフ・Runtime 系の型集合・
/// 決定的・オプションが効く・方言の綴りの大小・方言の指定順
/// </summary>
internal static partial class CodegenCases
{
    /// <summary>関係の行</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> RelationRows() =>
        [
            // ===== 層別と分割（LayeredOutputTests） =====
            RelationRow(
                "relation-layered-vs-split-order",
                "LayeredOutputTests.Generate_Layered_KeepsContentIdenticalExceptNamespaces の比較（層分け ON / OFF で namespace・using 以外の生成テキストは一致し、Runtime 系は可視性だけが違う）",
                OrderOnlyDiagram,
                FilesLayeredBase() with
                {
                    LayeredOutput = true,
                },
                DialectNames.SqlServer,
                [CodegenRelation.LayeredVsSplit]
            ),
            // ===== サブフォルダ（CodeSubdirectoryTests） =====
            RelationRow(
                "relation-code-subdirectory-layered",
                "CodeSubdirectoryTests.Generate_Subdirectory_ChangesPlacementOnly（サブフォルダは名前空間・using に一切現れない＝配置だけが変わる）",
                OrderOnlyDiagram,
                FilesCodeSubdirectoryLayered(subdirectory: null),
                DialectNames.SqlServer,
                [CodegenRelation.CodeSubdirectory]
            ),
            // ===== パッケージ参照・Runtime 系の型集合（SplitRuntimeSymmetryTests） =====
            RelationRow(
                "relation-runtime-packages-split-multi-dialect",
                "SplitRuntimeSymmetryTests.SchemaDependentFiles_ShouldBeIdenticalAcrossRuntimePackageModes",
                RelationSymmetryDiagram,
                RelationMultiDialectOptions() with
                {
                    UseRuntimePackages = true,
                },
                DialectNames.SqlServer,
                [CodegenRelation.RuntimePackages]
            ),
            RelationRow(
                "relation-runtime-type-set-split-multi-dialect",
                "SplitRuntimeSymmetryTests.SplitRuntimeFiles_ShouldDeclareSameTypesAsRuntimePackages・SplitAspNetCoreRuntimeFile_ShouldDeclareSameTypesAsAspNetCorePackage",
                RelationSymmetryDiagram,
                RelationMultiDialectOptions(),
                DialectNames.SqlServer,
                [CodegenRelation.RuntimeTypeSet],
                exactRuntimeTypeSets:
                [
                    "Runtime.g.cs",
                    "Runtime.SqlServer.g.cs",
                    "Runtime.Sqlite.g.cs",
                    "Runtime.AspNetCore.g.cs",
                ]
            ),
            RelationRow(
                "relation-runtime-type-set-split-efcore-only",
                "SplitRuntimeSymmetryTests.SplitEfCoreRuntimeFile_ShouldDeclareSameTypesAsEfCorePackage",
                RelationSymmetryDiagram,
                RelationEfCoreOnlyOptions(),
                DialectNames.SqlServer,
                [CodegenRelation.RuntimeTypeSet],
                exactRuntimeTypeSets: ["Runtime.EntityFrameworkCore.g.cs"]
            ),
            RelationRow(
                "relation-runtime-type-set-split-inmemory",
                "SplitRuntimeSymmetryTests.SplitInMemoryRuntimeFile_ShouldDeclareSameTypesAsInMemoryPackage",
                RelationSymmetryDiagram,
                RelationInMemoryOptions(),
                DialectNames.SqlServer,
                [CodegenRelation.RuntimeTypeSet],
                exactRuntimeTypeSets: ["Runtime.InMemory.g.cs"]
            ),
            RelationRow(
                "relation-runtime-type-set-split-sync",
                "SplitRuntimeSymmetryTests.SplitSyncRuntimeFile_ShouldDeclareSameTypesAsSyncPackage",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    SplitFilesByCategory = true,
                },
                DialectNames.SqlServer,
                [CodegenRelation.RuntimeTypeSet],
                exactRuntimeTypeSets: ["Runtime.Sync.g.cs"]
            ),
            // ===== 決定的（ApiReferenceDocTests） =====
            RelationRow(
                "relation-deterministic-api-docs",
                "ApiReferenceDocTests.Deterministic_SameInputProducesIdenticalMarkdown（.g.md だけでなく全ファイル・診断を比べる）",
                CustomerOrderDiagram,
                new CodeGenerationOptions { GenerateApiDocs = true },
                DialectNames.SqlServer,
                [CodegenRelation.Deterministic],
                [new FileRule(".g.md", Count: 1)]
            ),
            // ===== オプションが効く：行は単発オプションのコンパイルの行 compile-single-option-*（CodegenCases.Compile.cs）が持つ =====
            // ===== 方言の綴りの大小（MultiTargetRepositoryGenerationTests） =====
            RelationRow(
                "relation-dialect-casing-sqlite",
                "MultiTargetRepositoryGenerationTests.Generate_CaseInsensitiveDialectSpelling_MatchesCanonicalOutput（図は同じ形の CustomerOrderDiagram）",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.Sqlite],
                },
                DialectNames.Sqlite,
                [CodegenRelation.DialectCasing]
            ),
            // ===== 方言の指定順（DialectOrderInvarianceTests） =====
            RelationRow(
                "relation-dialect-order-without-rowversion",
                "DialectOrderInvarianceTests.SwappedDialectOrder_ShouldProduceIdenticalOutput_WithoutRowVersion",
                () => PortableFixtureDefinition.Build(PortableDialect.SqlServer),
                RelationDialectOrderOptions(generateInMemory: false),
                DialectNames.SqlServer,
                [CodegenRelation.DialectOrder],
                RelationNamedFiles(RelationDialectOrderFiles())
            ),
            RelationRow(
                "relation-dialect-order-with-rowversion",
                "DialectOrderInvarianceTests.SwappedDialectOrder_ShouldProduceIdenticalOutput_WithRowVersion",
                MultiTargetRowVersionFixtureDefinition.Build,
                RelationDialectOrderOptions(generateInMemory: false),
                DialectNames.SqlServer,
                [CodegenRelation.DialectOrder],
                RelationNamedFiles(RelationDialectOrderFiles())
            ),
            RelationRow(
                "relation-dialect-order-with-inmemory",
                "DialectOrderInvarianceTests.SwappedDialectOrder_ShouldProduceIdenticalOutput_WithInMemory",
                MultiTargetRowVersionFixtureDefinition.Build,
                RelationDialectOrderOptions(generateInMemory: true),
                DialectNames.SqlServer,
                [CodegenRelation.DialectOrder],
                RelationNamedFiles([
                    .. RelationDialectOrderFiles(),
                    "Repositories.InMemory.g.cs",
                    "Runtime.InMemory.g.cs",
                ])
            ),
        ];

    /// <summary>関係の行を組む（直接の入口・診断はエラーなしの包含）</summary>
    private static ScenarioRow<CodegenCase> RelationRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        string typeDialect,
        IReadOnlyList<CodegenRelation> relations,
        IReadOnlyList<FileRule>? fileRules = null,
        IReadOnlyList<string>? exactRuntimeTypeSets = null
    ) =>
        new(
            id,
            null,
            description,
            new CodegenCase(
                diagram,
                options,
                typeDialect,
                DiagnosticExpectation.Contains(),
                FileRules: fileRules,
                Relations: relations,
                ExactRuntimeTypeSets: exactRuntimeTypeSets
            )
        );

    /// <summary>
    /// 名指しのファイルがちょうど 1 つずつ出る（関係が比べる相手のファイルが命名の変更で消えて、比較が空振りしないように。
    /// 元の DialectOrderInvarianceTests は名指しのファイルの実在を確かめていた）
    /// </summary>
    private static IReadOnlyList<FileRule> RelationNamedFiles(params string[] fileNames) =>
        fileNames.Select(name => new FileRule(name, Count: 1, Prefix: name)).ToList();

    /// <summary>DialectOrderInvarianceTests.OrderInvariantFileNames（片方の順序でしか出ないと困る、名指しの不変ファイル）</summary>
    private static string[] RelationDialectOrderFiles() =>
        [
            "Entities.g.cs",
            "Repositories.g.cs",
            "Repositories.SqlServer.g.cs",
            "Repositories.Sqlite.g.cs",
            "Runtime.g.cs",
            "Runtime.SqlServer.g.cs",
            "Runtime.Sqlite.g.cs",
        ];

    /// <summary>DialectOrderInvarianceTests.Generate のオプション（分割・マルチターゲット。方言の順は関係が入れ替える）</summary>
    private static CodeGenerationOptions RelationDialectOrderOptions(bool generateInMemory) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
            GenerateRepositories = true,
            GenerateEfCoreRepositories = false,
            GenerateInMemoryRepositories = generateInMemory,
            SplitFilesByCategory = true,
        };

    /// <summary>SplitRuntimeSymmetryTests.MultiDialectOptions（全機能 ON のマルチ方言分割）</summary>
    private static CodeGenerationOptions RelationMultiDialectOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            SplitFilesByCategory = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
            GenerateRemoteServices = true,
            GenerateValueObjects = true,
            IncludeDataAnnotations = true,
            IncludeJsonIgnoreOnParentNavigation = true,
            ExcludeUnboundedBinaryColumns = true,
        };

    /// <summary>SplitRuntimeSymmetryTests.EfCoreOnlyOptions（EF Core 単独の分割）</summary>
    private static CodeGenerationOptions RelationEfCoreOnlyOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            SplitFilesByCategory = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = false,
            GenerateEfCoreRepositories = true,
            GenerateRemoteServices = true,
            GenerateValueObjects = true,
            IncludeDataAnnotations = true,
            IncludeJsonIgnoreOnParentNavigation = true,
            ExcludeUnboundedBinaryColumns = true,
        };

    /// <summary>SplitRuntimeSymmetryTests.InMemoryOptions（単一方言＋インメモリの分割）</summary>
    private static CodeGenerationOptions RelationInMemoryOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            SplitFilesByCategory = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateInMemoryRepositories = true,
            GenerateRemoteServices = true,
            GenerateValueObjects = true,
            IncludeDataAnnotations = true,
            IncludeJsonIgnoreOnParentNavigation = true,
            ExcludeUnboundedBinaryColumns = true,
        };

    /// <summary>
    /// SplitRuntimeSymmetryTests.Diagram（説明・UNIQUE 制約・無制限バイナリ列を含む図。属性型の出力条件をすべて満たし、
    /// パッケージ書き出しと同じ固定 infra の集合になる）
    /// </summary>
    private static ErDiagram RelationSymmetryDiagram()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var customerCode = Guid.NewGuid();
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
                    Description = "Customer master",
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
                        new Column
                        {
                            Id = customerCode,
                            Name = "code",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                            Description = "Business code",
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                    ],
                    UniqueConstraints =
                    [
                        new UniqueConstraint
                        {
                            Id = Guid.NewGuid(),
                            Name = "UQ_customers_code",
                            ColumnIds = [customerCode],
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
