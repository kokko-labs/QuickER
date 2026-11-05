using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：出力ファイルの名前・置き場・名前空間（API リファレンス・層別出力・サブフォルダ・ファイル計画・出力ファイル名）で判定していたテスト
/// </summary>
/// <remarks>
/// 元は <c>ApiReferenceDocTests</c> / <c>LayeredOutputTests</c> / <c>CodeSubdirectoryTests</c> / <c>GeneratedFilePlannerTests</c> /
/// <c>OutputFileNameValidationTests</c>。計画（<c>GeneratedFilePlanner.Plan</c>）を直接呼んでいたテストは、同じオプションで生成して
/// 出力のファイル名・置き場・名前空間を表明する。図の無かった層別出力の計画のテストは、全バケットを出す図として
/// 同期の固定フィクスチャの図（<see cref="SyncFixtureDefinition.Build"/>）を使う（同期は sqlserver + sqlite の 2 方言を要するので、
/// 計画のときの構成に sqlite の方言を足す。足した 2 ファイル <c>Repositories.Sqlite.g.cs</c> / <c>Runtime.Sqlite.g.cs</c> も表明する）。
/// </remarks>
internal static partial class CodegenCases
{
    /// <summary>1 行を作る（SQL Server の型解決・direct の入口）</summary>
    /// <param name="id">行の Id</param>
    /// <param name="source">元のテスト（クラス.メソッド）</param>
    /// <param name="display">元の DisplayName（無ければメソッド名）</param>
    /// <param name="diagram">図</param>
    /// <param name="options">生成オプション</param>
    /// <param name="diagnostics">診断の期待</param>
    /// <param name="fileRules">ファイルの期待</param>
    /// <param name="fileLayout">ファイル構成の規則を掛けるか</param>
    /// <param name="namespaces">名前空間の規則を掛けるか</param>
    /// <param name="fileNamespaces">ファイル名 → 名前空間の完全一致</param>
    private static ScenarioRow<CodegenCase> FilesRow(
        string id,
        string source,
        string display,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        DiagnosticExpectation diagnostics,
        IReadOnlyList<FileRule>? fileRules = null,
        bool fileLayout = false,
        bool namespaces = false,
        IReadOnlyDictionary<string, string>? fileNamespaces = null
    ) =>
        new(
            id,
            null,
            $"{display}（{source}）",
            new CodegenCase(
                diagram,
                options,
                DialectNames.SqlServer,
                diagnostics,
                FileRules: fileRules,
                FileLayout: fileLayout,
                Namespaces: namespaces,
                FileNamespaces: fileNamespaces
            )
        );

    /// <summary>生成が成り立ち、.g.cs が直下に 1 つ以上ある（成功の期待の足場。エラーなら段の食い違いで赤になる）</summary>
    private static IReadOnlyList<FileRule> FilesGenerated =>
        [new FileRule(".g.cs", NonEmpty: true)];

    /// <summary>生成が成り立ち、.g.cs が（置き場を問わず）1 つ以上ある</summary>
    private static IReadOnlyList<FileRule> FilesGeneratedAnywhere =>
        [new FileRule(".g.cs", NonEmpty: true, AnyDirectory: true)];

    /// <summary>.g.md の名前の集合（拡張子ごとの数も合わせて見る）</summary>
    private static IReadOnlyList<FileRule> FilesMarkdown(params string[] names) =>
        [new FileRule(".g.md", Count: names.Length, Names: names), .. FilesGenerated];

    /// <summary>ファイルごとの置き場の期待（<paramref name="closed"/> なら .g.cs の名前の集合がこれだけであることも見る）</summary>
    private static IReadOnlyList<FileRule> FilesDirectories(
        bool closed,
        params (string File, string Directory)[] expected
    ) =>
        [
            .. expected.Select(entry => new FileRule(
                entry.File,
                Count: 1,
                Directory: entry.Directory
            )),
            .. closed
                ?
                [
                    new FileRule(
                        ".g.cs",
                        Names: [.. expected.Select(entry => entry.File)],
                        AnyDirectory: true
                    ),
                ]
                : Array.Empty<FileRule>(),
        ];

    /// <summary>層別出力で全層が埋まる構成（元の <c>LayeredOutputTests.FullLayeredOptions</c> に sqlite の方言を足したもの）</summary>
    private static CodeGenerationOptions FilesFullLayered() =>
        new()
        {
            RootNamespace = "Acme.App",
            LayeredOutput = true,
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
            GenerateRemoteServices = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateValueObjects = true,
            GenerateInMemoryRepositories = true,
            GenerateSyncSupport = true,
        };

    /// <summary>層別出力の全層の置き場（バケット → 層の固定対応）</summary>
    private static (string File, string Directory)[] FilesFullLayeredDirectories() =>
        [
            ("Entities.g.cs", "Domain"),
            ("ValueObjects.g.cs", "Domain"),
            ("Repositories.g.cs", "Domain"),
            ("Runtime.g.cs", "Domain"),
            ("EditModels.g.cs", "Presentation"),
            ("Mappers.g.cs", "Presentation"),
            ("Repositories.SqlServer.g.cs", "Infrastructure"),
            ("Repositories.Sqlite.g.cs", "Infrastructure"),
            ("Repositories.Http.g.cs", "Infrastructure"),
            ("Repositories.InMemory.g.cs", "Infrastructure"),
            ("Repositories.Sync.g.cs", "Infrastructure"),
            ("Runtime.SqlServer.g.cs", "Infrastructure"),
            ("Runtime.Sqlite.g.cs", "Infrastructure"),
            ("Runtime.InMemory.g.cs", "Infrastructure"),
            ("Runtime.Sync.g.cs", "Infrastructure"),
            ("RemoteServer.g.cs", "Server"),
            ("Runtime.AspNetCore.g.cs", "Server"),
        ];

    /// <summary>
    /// 元の API リファレンス・出力ファイル名のテストの図（<c>ItemsDiagram</c>＝id 列だけの items 表。
    /// <c>OutputFileNameValidationTests.Generate</c> の図）
    /// </summary>
    private static ErDiagram FilesItemsDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>層別出力・サブフォルダの代表構成（元の <c>CodeSubdirectoryTests.LayeredOptions</c>）</summary>
    private static CodeGenerationOptions FilesCodeSubdirectoryLayered(string? subdirectory) =>
        new()
        {
            RootNamespace = "Acme.App",
            LayeredOutput = true,
            GenerateRepositories = true,
            GenerateRemoteServices = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            CodeSubdirectory = subdirectory,
        };

    /// <summary>
    /// API リファレンスの言語と分割から、期待するファイル名の並びを作る（<c>ResolveApiDocsFileNames</c> と同じ並び）
    /// </summary>
    /// <remarks>
    /// 元のテストは「関数と実出力の一致」という関係なので、関数から作った並びとも照合する。関数と出力が同時に壊れても気付けるよう、
    /// 書き下ろした名前の並び（<paramref name="literalNames"/>）とも照合する。
    /// </remarks>
    private static ScenarioRow<CodegenCase> FilesResolvedNamesRow(
        string id,
        ApiDocsLanguage language,
        bool split,
        params string[] literalNames
    )
    {
        var options = new CodeGenerationOptions
        {
            OutputFileName = "EcOrder.g.cs",
            GenerateApiDocs = true,
            ApiDocsLanguage = language,
            SplitFilesByCategory = split,
        };

        return FilesRow(
            id,
            "ApiReferenceDocTests.ResolveApiDocsFileNames_MatchesEmittedNames",
            "ResolveApiDocsFileNames は実出力のファイル名の並びと一致する（GUI プレースホルダの正）",
            CustomerOrderDiagram,
            options,
            DiagnosticExpectation.Contains(),
            [
                new FileRule(
                    ".g.md",
                    Names: CSharpCodeGenerationService.ResolveApiDocsFileNames(options),
                    Ordered: true
                ),
                new FileRule(".g.md", Names: literalNames, Ordered: true),
                .. FilesGenerated,
            ]
        );
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> FileRows() =>
        [
            // ===== API リファレンス（ApiReferenceDocTests） =====
            FilesRow(
                "files-apidocs-default-no-markdown",
                "ApiReferenceDocTests.Default_DoesNotEmitMarkdown",
                "既定（GenerateApiDocs=false）では .g.md を出力しない",
                CustomerOrderDiagram,
                new CodeGenerationOptions(),
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.md", Count: 0), .. FilesGenerated]
            ),
            FilesRow(
                "files-apidocs-single-markdown-symmetric-name",
                "ApiReferenceDocTests.On_EmitsSingleMarkdown_WithSymmetricFileName",
                "ON で .g.md が 1 つ出力され、ファイル名が OutputFileName のベース名と対称になる",
                CustomerOrderDiagram,
                new CodeGenerationOptions { OutputFileName = "Foo.g.cs", GenerateApiDocs = true },
                DiagnosticExpectation.Contains(),
                FilesMarkdown("Foo.g.md")
            ),
            FilesRow(
                "files-apidocs-split-fixed-names",
                "ApiReferenceDocTests.SplitFiles_EmitsSingleMarkdown_WithFixedName",
                "分割生成では Markdown はカテゴリ別固定名と同じ流儀の固定名 ApiDocs.g.md / ApiDocs.ja.g.md になる",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = "EcOrder.g.cs",
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                    SplitFilesByCategory = true,
                },
                DiagnosticExpectation.Contains(),
                FilesMarkdown("ApiDocs.g.md", "ApiDocs.ja.g.md")
            ),
            FilesRow(
                "diag-apidocs-validation-error-no-files",
                "ApiReferenceDocTests.ValidationError_DoesNotEmitMarkdown",
                "検証エラー時（エンティティ 0 件）は .g.md も出力しない",
                () => new ErDiagram(),
                new CodeGenerationOptions { GenerateApiDocs = true },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(nameof(Strings.CodeGen_Error_NoEntities))
                )
            ),
            FilesRow(
                "files-apidocs-language-english",
                "ApiReferenceDocTests.ApiDocsLanguage_EmitsOnlySelectedLanguages(English)",
                "ApiDocsLanguage が指す言語の Markdown だけが英語→日本語の順で出る",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = "EcOrder.g.cs",
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.English,
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.md", Names: ["EcOrder.g.md"], Ordered: true), .. FilesGenerated]
            ),
            FilesRow(
                "files-apidocs-language-japanese",
                "ApiReferenceDocTests.ApiDocsLanguage_EmitsOnlySelectedLanguages(Japanese)",
                "ApiDocsLanguage が指す言語の Markdown だけが英語→日本語の順で出る",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = "EcOrder.g.cs",
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Japanese,
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(".g.md", Names: ["EcOrder.ja.g.md"], Ordered: true),
                    .. FilesGenerated,
                ]
            ),
            FilesRow(
                "files-apidocs-language-both",
                "ApiReferenceDocTests.ApiDocsLanguage_EmitsOnlySelectedLanguages(Both)",
                "ApiDocsLanguage が指す言語の Markdown だけが英語→日本語の順で出る",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = "EcOrder.g.cs",
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(
                        ".g.md",
                        Names: ["EcOrder.g.md", "EcOrder.ja.g.md"],
                        Ordered: true
                    ),
                    .. FilesGenerated,
                ]
            ),
            FilesRow(
                "files-apidocs-language-default-english",
                "ApiReferenceDocTests.ApiDocsLanguage_DefaultsToEnglish",
                "ApiDocsLanguage の既定は English（英語版だけ。オプションの既定値は、言語を指定しない生成が英語版だけを出すことで見る）",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = "EcOrder.g.cs",
                    GenerateApiDocs = true,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.md", Names: ["EcOrder.g.md"], Ordered: true), .. FilesGenerated]
            ),
            FilesRow(
                "files-apidocs-japanese-only-no-english",
                "ApiReferenceDocTests.JapaneseOnly_ContentMatchesJapaneseOfBoth の構造の表明",
                "日本語だけのとき、英語版は出ない（.ja.g.md だけが 1 つ出る）",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Japanese,
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(".g.md", Count: 1),
                    new FileRule(".ja.g.md", Count: 1),
                    .. FilesGenerated,
                ]
            ),
            FilesRow(
                "diag-apidocs-language-undefined",
                "ApiReferenceDocTests.ApiDocsLanguage_Undefined_IsDiagnosticError",
                "未定義の ApiDocsLanguage は生成時診断エラーで止まり、Markdown もコードも出ない",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    ApiDocsLanguage = (ApiDocsLanguage)99,
                },
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_InvalidApiDocsLanguage),
                        "99"
                    ),
                    1
                )
            ),
            FilesRow(
                "diag-apidocs-language-undefined-apidocs-off-ignored",
                "ApiReferenceDocTests.ApiDocsLanguage_Undefined_WithoutApiDocs_IsIgnored",
                "GenerateApiDocs=false なら未定義の ApiDocsLanguage でもエラーにしない（意味を持たないため）",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = false,
                    ApiDocsLanguage = (ApiDocsLanguage)99,
                },
                DiagnosticExpectation.Contains(),
                FilesGenerated
            ),
            FilesRow(
                "files-apidocs-language-apidocs-off-no-markdown",
                "ApiReferenceDocTests.ApiDocsLanguage_WithoutApiDocs_EmitsNoMarkdown",
                "GenerateApiDocs=false のとき ApiDocsLanguage=Both でも Markdown は一切出ない",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = false,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.md", Count: 0), .. FilesGenerated]
            ),
            FilesRow(
                "files-apidocs-subdirectory-default-root",
                "ApiReferenceDocTests.ApiDocsSubdirectory_Default_KeepsRootPlacement",
                "ApiDocsSubdirectory 未指定（既定）は出力ディレクトリ直下",
                CustomerOrderDiagram,
                new CodeGenerationOptions { GenerateApiDocs = true },
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.md", Count: 1), .. FilesGenerated]
            ),
            FilesRow(
                "diag-apidocs-subdirectory-invalid",
                "ApiReferenceDocTests.ApiDocsSubdirectory_Invalid_ReportsError",
                "ApiDocsSubdirectory の不正パス（.. / 絶対パス）は生成時エラーになる",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    ApiDocsSubdirectory = @"..\docs",
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_InvalidOutputSubdirectory),
                        "ApiDocsSubdirectory"
                    )
                )
            ),
            FilesRow(
                "diag-apidocs-subdirectory-invalid-apidocs-off",
                "ApiReferenceDocTests.ApiDocsSubdirectory_Invalid_WithApiDocsOff_ReportsNoError",
                "ApiDocs OFF なら不正な ApiDocsSubdirectory でも検証しない",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = false,
                    ApiDocsSubdirectory = @"..\docs",
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Error_InvalidOutputSubdirectory),
                            "ApiDocsSubdirectory"
                        )
                    ),
                FilesGenerated
            ),
            FilesNormalizedFileNameRow(
                "files-apidocs-filename-normalizes-extension-plain",
                "Api",
                "Api.g.md",
                "Api.ja.g.md"
            ),
            FilesNormalizedFileNameRow(
                "files-apidocs-filename-normalizes-extension-md",
                "Api.md",
                "Api.g.md",
                "Api.ja.g.md"
            ),
            FilesNormalizedFileNameRow(
                "files-apidocs-filename-normalizes-extension-gmd",
                "Api.g.md",
                "Api.g.md",
                "Api.ja.g.md"
            ),
            FilesNormalizedFileNameRow(
                "files-apidocs-filename-normalizes-extension-gcs",
                "Api.g.cs",
                "Api.g.md",
                "Api.ja.g.md"
            ),
            FilesNormalizedFileNameRow(
                "files-apidocs-filename-normalizes-extension-padded",
                "  Api  ",
                "Api.g.md",
                "Api.ja.g.md"
            ),
            FilesRow(
                "files-apidocs-filename-wins-over-split-fixed-name",
                "ApiReferenceDocTests.ApiDocsFileName_WinsOverSplitFixedName",
                "ApiDocsFileName は分割生成でも固定名 ApiDocs.g.md より優先される",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = "EcOrder.g.cs",
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                    SplitFilesByCategory = true,
                    ApiDocsFileName = "Reference",
                },
                DiagnosticExpectation.Contains(),
                FilesMarkdown("Reference.g.md", "Reference.ja.g.md")
            ),
            FilesRow(
                "files-apidocs-filename-with-subdirectory",
                "ApiReferenceDocTests.ApiDocsFileName_CombinesWithDirectory",
                "ApiDocsFileName は出力先サブフォルダ（ApiDocsSubdirectory）と併用できる",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    ApiDocsSubdirectory = "docs",
                    ApiDocsFileName = "Reference",
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(".g.md", Count: 1, Directory: "docs", Names: ["Reference.g.md"]),
                    .. FilesGenerated,
                ]
            ),
            FilesInvalidFileNameRow("diag-apidocs-filename-invalid-backslash", @"docs\Api.g.md"),
            FilesInvalidFileNameRow("diag-apidocs-filename-invalid-slash", "docs/Api.g.md"),
            FilesInvalidFileNameRow("diag-apidocs-filename-invalid-drive", @"C:\Api.g.md"),
            FilesInvalidFileNameRow("diag-apidocs-filename-invalid-no-base-name", ".g.md"),
            FilesRow(
                "diag-apidocs-filename-invalid-apidocs-off",
                "ApiReferenceDocTests.ApiDocsFileName_Invalid_WithApiDocsOff_ReportsNoError",
                "ApiDocs OFF なら不正な ApiDocsFileName でも検証しない",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = false,
                    ApiDocsFileName = @"docs\Api.g.md",
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_InvalidApiDocsFileName)
                        )
                    ),
                FilesGenerated
            ),
            FilesRow(
                "files-apidocs-filename-default-derived",
                "ApiReferenceDocTests.ApiDocsFileName_Default_KeepsDerivedName",
                "ApiDocsFileName 未指定（既定）は OutputFileName のベース名から導出する",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = "EcOrder.g.cs",
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                },
                DiagnosticExpectation.Contains(),
                FilesMarkdown("EcOrder.g.md", "EcOrder.ja.g.md")
            ),
            FilesResolvedNamesRow(
                "files-apidocs-resolved-names-english-single",
                ApiDocsLanguage.English,
                split: false,
                "EcOrder.g.md"
            ),
            FilesResolvedNamesRow(
                "files-apidocs-resolved-names-japanese-single",
                ApiDocsLanguage.Japanese,
                split: false,
                "EcOrder.ja.g.md"
            ),
            FilesResolvedNamesRow(
                "files-apidocs-resolved-names-both-single",
                ApiDocsLanguage.Both,
                split: false,
                "EcOrder.g.md",
                "EcOrder.ja.g.md"
            ),
            FilesResolvedNamesRow(
                "files-apidocs-resolved-names-english-split",
                ApiDocsLanguage.English,
                split: true,
                "ApiDocs.g.md"
            ),
            FilesResolvedNamesRow(
                "files-apidocs-resolved-names-japanese-split",
                ApiDocsLanguage.Japanese,
                split: true,
                "ApiDocs.ja.g.md"
            ),
            FilesResolvedNamesRow(
                "files-apidocs-resolved-names-both-split",
                ApiDocsLanguage.Both,
                split: true,
                "ApiDocs.g.md",
                "ApiDocs.ja.g.md"
            ),
            // ===== 層別出力（LayeredOutputTests） =====
            FilesRow(
                "files-layered-fixed-layer-directories",
                "LayeredOutputTests.Plan_Layered_AssignsFixedLayerDirectories",
                "計画: バケット→層の固定対応で全ファイルへ層フォルダが付く（同じオプションで生成して置き場を見る）",
                SyncFixtureDefinition.Build,
                FilesFullLayered(),
                DiagnosticExpectation.Contains(),
                FilesDirectories(closed: true, FilesFullLayeredDirectories()),
                fileLayout: true
            ),
            FilesRow(
                "files-layered-efcore-infrastructure",
                "LayeredOutputTests.Plan_Layered_EfCore_MapsToInfrastructure",
                "計画: EF Core 構成では DbContext 系がインフラ層・契約はドメイン層に付く",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = true,
                    GenerateEfCoreRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                FilesDirectories(
                    closed: false,
                    ("Repositories.EntityFrameworkCore.g.cs", "Infrastructure"),
                    ("Runtime.EntityFrameworkCore.g.cs", "Infrastructure"),
                    ("Repositories.g.cs", "Domain")
                )
            ),
            FilesRow(
                "files-layered-implies-split",
                "LayeredOutputTests.Plan_Layered_ImpliesSplit",
                "計画: LayeredOutput は SplitFilesByCategory を自動含意する（分割レイアウトのファイルが出て、単一ファイルにならない）",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = true,
                    SplitFilesByCategory = false,
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule("Entities.g.cs", Count: 1, AnyDirectory: true),
                    new FileRule("QuickEREntities.g.cs", Count: 0, AnyDirectory: true),
                ],
                fileLayout: true
            ),
            FilesRow(
                "files-layered-overrides-and-blank-fallback",
                "LayeredOutputTests.Plan_Layered_UsesOverridesAndFallsBackWhenBlank",
                "計画: フォルダ上書きは複数階層可・空白は既定へフォールバック",
                SyncFixtureDefinition.Build,
                FilesFullLayered() with
                {
                    DomainLayerDirectory = "MyApp.Domain/Generated",
                    PresentationLayerDirectory = "MyApp.Wpf",
                    InfrastructureLayerDirectory = "   ",
                },
                DiagnosticExpectation.Contains(),
                FilesDirectories(
                    closed: false,
                    ("Entities.g.cs", "MyApp.Domain/Generated"),
                    ("Repositories.SqlServer.g.cs", "Infrastructure"),
                    ("EditModels.g.cs", "MyApp.Wpf"),
                    ("RemoteServer.g.cs", "Server")
                ),
                fileLayout: true
            ),
            FilesRow(
                "files-layered-off-leaves-directory-root",
                "LayeredOutputTests.Plan_NonLayered_LeavesRelativeDirectoryNull",
                "計画: 層別出力でなければ RelativeDirectory は全て null（直下）",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation.Contains(),
                FilesGenerated
            ),
            FilesRow(
                "files-layered-namespaces-follow-layer-folders",
                "LayeredOutputTests.Plan_Layered_NamespacesFollowLayerFolders",
                "計画: 名前空間の既定は層フォルダに追従する（固定部は per-entity と同一名前空間へ統合）",
                SyncFixtureDefinition.Build,
                FilesFullLayered(),
                DiagnosticExpectation.Contains(),
                FilesGeneratedAnywhere,
                namespaces: true,
                fileNamespaces: new Dictionary<string, string>
                {
                    // ドメイン層: {Domain}.{種別サフィックス}
                    ["Entities.g.cs"] = "Domain.Entities",
                    ["ValueObjects.g.cs"] = "Domain.ValueObjects",
                    ["Repositories.g.cs"] = "Domain.Repositories",
                    ["Runtime.g.cs"] = "Domain.Runtime",
                    // プレゼンテーション層
                    ["EditModels.g.cs"] = "Presentation.EditModels",
                    ["Mappers.g.cs"] = "Presentation.Mappers",
                    // インフラ層: 固定部（Runtime.{X}）と per-entity（Repositories.{X}）は同一サブ名前空間へ統合
                    ["Repositories.SqlServer.g.cs"] = "Infrastructure.SqlServer",
                    ["Runtime.SqlServer.g.cs"] = "Infrastructure.SqlServer",
                    ["Repositories.Sqlite.g.cs"] = "Infrastructure.Sqlite",
                    ["Runtime.Sqlite.g.cs"] = "Infrastructure.Sqlite",
                    ["Repositories.Http.g.cs"] = "Infrastructure.Http",
                    ["Repositories.InMemory.g.cs"] = "Infrastructure.InMemory",
                    ["Runtime.InMemory.g.cs"] = "Infrastructure.InMemory",
                    ["Repositories.Sync.g.cs"] = "Infrastructure.Sync",
                    ["Runtime.Sync.g.cs"] = "Infrastructure.Sync",
                    // サーバー層
                    ["RemoteServer.g.cs"] = "Server.RemoteServer",
                    ["Runtime.AspNetCore.g.cs"] = "Server.AspNetCore",
                }
            ),
            FilesRow(
                "files-layered-namespace-follows-folder-explicit-wins",
                "LayeredOutputTests.Plan_Layered_NamespaceFollowsFolderAndExplicitWins",
                "計画: フォルダ変更で名前空間も追従し、明示指定はそれより優先される",
                SyncFixtureDefinition.Build,
                FilesFullLayered() with
                {
                    DomainLayerDirectory = "MyApp.Domain/Generated",
                    InfrastructureLayerDirectory = "MyApp.Infrastructure",
                    EntityNamespace = "MyApp.Model",
                },
                DiagnosticExpectation.Contains(),
                FilesGeneratedAnywhere,
                fileNamespaces: new Dictionary<string, string>
                {
                    // 明示指定が最優先
                    ["Entities.g.cs"] = "MyApp.Model",
                    // 複数階層フォルダは区切りを . に変換して名前空間ルートになる
                    ["ValueObjects.g.cs"] = "MyApp.Domain.Generated.ValueObjects",
                    // 方言実装は契約でなくインフラ層ルートの下に導出される（固定部も同一）
                    ["Repositories.SqlServer.g.cs"] = "MyApp.Infrastructure.SqlServer",
                    ["Runtime.SqlServer.g.cs"] = "MyApp.Infrastructure.SqlServer",
                }
            ),
            FilesRow(
                "diag-layered-folder-not-namespace",
                "LayeredOutputTests.Generate_LayerFolderNotNamespace_ReportsError",
                "診断: 名前空間になれない層フォルダ（ハイフン等）は導出が使われる層でエラーになる",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    DomainLayerDirectory = "my-domain",
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_LayerDirectoryNotNamespace),
                        "DomainLayerDirectory",
                        "my-domain"
                    )
                )
            ),
            FilesRow(
                "diag-layered-folder-not-namespace-allowed-when-all-explicit",
                "LayeredOutputTests.Generate_LayerFolderNotNamespace_AllowedWhenAllNamespacesExplicit",
                "診断: 層の全バケットを明示名前空間で賄えば、名前空間になれないフォルダでも許容する",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    GenerateEditModels = false,
                    GenerateMappers = false,
                    DomainLayerDirectory = "my-domain",
                    EntityNamespace = "Acme.Domain.Model",
                    RepositoryNamespace = "Acme.Domain.Repositories",
                    RuntimeNamespace = "Acme.Domain.Runtime",
                },
                DiagnosticExpectation.Contains(),
                FilesGeneratedAnywhere
            ),
            FilesRow(
                "diag-layered-invalid-layer-directory",
                "LayeredOutputTests.Generate_InvalidLayerDirectory_ReportsError",
                "診断: 不正な層フォルダ（.. / 絶対パス）は生成時エラーになる",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    DomainLayerDirectory = @"..\evil",
                    InfrastructureLayerDirectory = @"C:\abs",
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_InvalidOutputSubdirectory),
                        "DomainLayerDirectory"
                    ),
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_InvalidOutputSubdirectory),
                        "InfrastructureLayerDirectory"
                    )
                )
            ),
            FilesRow(
                "diag-layered-invalid-layer-directory-layered-off",
                "LayeredOutputTests.Generate_InvalidLayerDirectory_WithoutLayeredOutput_ReportsNoError",
                "診断: 層別出力 OFF なら不正な層フォルダでも検証しない",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = false,
                    GenerateRepositories = true,
                    DomainLayerDirectory = @"..\evil",
                    InfrastructureLayerDirectory = @"C:\abs",
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Error_InvalidOutputSubdirectory),
                            "DomainLayerDirectory"
                        ),
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Error_InvalidOutputSubdirectory),
                            "InfrastructureLayerDirectory"
                        ),
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Error_LayerDirectoryNotNamespace),
                            "DomainLayerDirectory"
                        ),
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Error_LayerDirectoryNotNamespace),
                            "InfrastructureLayerDirectory"
                        )
                    ),
                FilesGenerated
            ),
            FilesRow(
                "diag-layered-unused-server-layer-directory",
                "LayeredOutputTests.Generate_UnusedServerLayerDirectory_IsNotValidated",
                "診断: 使われない層（リモートなしのサーバー層）は不正値でも検証しない",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    ServerLayerDirectory = @"..\unused",
                },
                DiagnosticExpectation.Contains(),
                FilesGeneratedAnywhere
            ),
            FilesRow(
                "files-layered-baseline-split-root-placement",
                "LayeredOutputTests.Generate_Layered_KeepsContentIdenticalExceptNamespaces の構造の表明（分割のみ）",
                "分割のみでは配置なし（.g.cs も .g.md も出力ディレクトリ直下）",
                OrderOnlyDiagram,
                FilesLayeredBase() with
                {
                    SplitFilesByCategory = true,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.md", NonEmpty: true), .. FilesGenerated]
            ),
            FilesRow(
                "files-layered-placement-code-in-layers-markdown-in-root",
                "LayeredOutputTests.Generate_Layered_KeepsContentIdenticalExceptNamespaces の構造の表明（層別）",
                "層別では .g.cs に層フォルダ・.g.md は出力ディレクトリ直下のまま",
                OrderOnlyDiagram,
                FilesLayeredBase() with
                {
                    LayeredOutput = true,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.md", NonEmpty: true), .. FilesGeneratedAnywhere],
                fileLayout: true
            ),
            // ===== ファイル計画（GeneratedFilePlannerTests） =====
            FilesRow(
                "files-split-remote-server-right-after-repositories",
                "GeneratedFilePlannerTests.Plan_Split_RemoteServer_ComesRightAfterRepositories",
                "分割＋リモートサービス生成時、RemoteServer.g.cs が Repositories 系の直後（Runtime 系より前）に並ぶ（出力の順）",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(
                        ".g.cs",
                        Names:
                        [
                            "Entities.g.cs",
                            "EditModels.g.cs",
                            "Mappers.g.cs",
                            "Repositories.g.cs",
                            "Repositories.SqlServer.g.cs",
                            "Repositories.Http.g.cs",
                            "RemoteServer.g.cs",
                            "Runtime.g.cs",
                            "Runtime.SqlServer.g.cs",
                            "Runtime.AspNetCore.g.cs",
                        ],
                        Ordered: true
                    ),
                ]
            ),
            // ===== コード出力先サブフォルダ（CodeSubdirectoryTests） =====
            FilesRow(
                "files-codesubdir-layered-below-layer-directory",
                "CodeSubdirectoryTests.Plan_Layered_AppendsSubdirectoryBelowLayerDirectory",
                "計画: 層別出力ではサブフォルダが層フォルダの 1 段下に付く",
                OrderOnlyDiagram,
                FilesCodeSubdirectoryLayered("Generated"),
                DiagnosticExpectation.Contains(),
                FilesDirectories(
                    closed: false,
                    ("Entities.g.cs", "Domain/Generated"),
                    ("EditModels.g.cs", "Presentation/Generated"),
                    ("Repositories.SqlServer.g.cs", "Infrastructure/Generated"),
                    ("RemoteServer.g.cs", "Server/Generated")
                ),
                fileLayout: true
            ),
            FilesRow(
                "files-codesubdir-layered-custom-layer-directory",
                "CodeSubdirectoryTests.Plan_Layered_AppendsSubdirectoryBelowLayerDirectory",
                "計画: 層フォルダを明示指定した場合も同じ規則（複数階層の層フォルダの下へ 1 段）",
                OrderOnlyDiagram,
                FilesCodeSubdirectoryLayered("Generated") with
                {
                    DomainLayerDirectory = "MyApp.Domain/src",
                },
                DiagnosticExpectation.Contains(),
                FilesDirectories(closed: false, ("Entities.g.cs", "MyApp.Domain/src/Generated")),
                fileLayout: true
            ),
            FilesRow(
                "files-codesubdir-split-only",
                "CodeSubdirectoryTests.Plan_SplitWithoutLayeredOutput_UsesSubdirectoryOnly",
                "計画: 層別出力 OFF（分割）ではサブフォルダだけが付く",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                    CodeSubdirectory = "Generated",
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.cs", NonEmpty: true, Directory: "Generated")]
            ),
            FilesRow(
                "files-codesubdir-single-file",
                "CodeSubdirectoryTests.Plan_SingleFile_AppliesSubdirectory",
                "計画: 非分割（単一ファイル）でもサブフォルダが効く（別ファイルのリモートサーバー実装も同じサブフォルダへ入る）",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Acme.g.cs",
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                    CodeSubdirectory = "Generated",
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(".g.cs", NonEmpty: true, Directory: "Generated"),
                    new FileRule(".RemoteServer.g.cs", NonEmpty: true, Directory: "Generated"),
                ]
            ),
            FilesRow(
                "files-codesubdir-absent-layered-layers-only",
                "CodeSubdirectoryTests.Plan_WithoutSubdirectory_KeepsExistingPlacement",
                "計画: サブフォルダ未指定なら配置は従来どおり（層別は層のみ）",
                OrderOnlyDiagram,
                FilesCodeSubdirectoryLayered(null),
                DiagnosticExpectation.Contains(),
                FilesGeneratedAnywhere,
                fileLayout: true
            ),
            FilesRow(
                "files-codesubdir-absent-split-root",
                "CodeSubdirectoryTests.Plan_WithoutSubdirectory_KeepsExistingPlacement",
                "計画: サブフォルダ未指定なら配置は従来どおり（分割は直下）",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                FilesGenerated
            ),
            FilesRow(
                "files-codesubdir-blank-is-absent",
                "CodeSubdirectoryTests.Plan_WithoutSubdirectory_KeepsExistingPlacement",
                "計画: 空白は未指定と同じ扱い（サブフォルダなしへフォールバック）",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                    CodeSubdirectory = "   ",
                },
                DiagnosticExpectation.Contains(),
                FilesGenerated
            ),
            FilesInvalidCodeSubdirectoryRow("diag-codesubdir-invalid-parent", @"..\evil"),
            FilesInvalidCodeSubdirectoryRow("diag-codesubdir-invalid-absolute", @"C:\abs"),
            FilesRow(
                "files-codesubdir-non-identifier-allowed",
                "CodeSubdirectoryTests.Generate_NonIdentifierSubdirectory_IsAllowed",
                "診断: 名前空間になれない名前（ハイフン等）もサブフォルダなら許容する（全ファイルが層フォルダ/generated-code に入る）",
                OrderOnlyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    CodeSubdirectory = "generated-code",
                },
                DiagnosticExpectation.Contains(),
                FilesGeneratedAnywhere,
                fileLayout: true
            ),
            // ===== 出力ファイル名（OutputFileNameValidationTests） =====
            FilesInvalidOutputFileNameRow("diag-outputfilename-invalid-slash", "sub/Foo.g.cs"),
            FilesInvalidOutputFileNameRow("diag-outputfilename-invalid-backslash", @"sub\Foo.g.cs"),
            FilesInvalidOutputFileNameRow("diag-outputfilename-invalid-reserved-name", "CON"),
            FilesInvalidOutputFileNameRow("diag-outputfilename-invalid-short-alias", "a~1"),
            FilesInvalidOutputFileNameRow("diag-outputfilename-invalid-no-base-gcs", ".g.cs"),
            FilesInvalidOutputFileNameRow("diag-outputfilename-invalid-no-base-cs", ".cs"),
            FilesInvalidOutputFileNameRow("diag-outputfilename-invalid-trailing-dot", "Foo."),
            FilesRow(
                "files-outputfilename-long-name-without-remote-succeeds",
                "OutputFileNameValidationTests.DerivedFileNameTooLong_IsReported（前半）",
                "付け足すと 1 区間の上限を超える名前でも、サーバー実装を付けない構成なら断らない",
                FilesItemsDiagram,
                new CodeGenerationOptions { OutputFileName = new string('a', 245) + ".g.cs" },
                DiagnosticExpectation.Contains(),
                FilesGenerated
            ),
            FilesRow(
                "diag-outputfilename-derived-name-too-long",
                "OutputFileNameValidationTests.DerivedFileNameTooLong_IsReported（後半）",
                "付け足すと 1 区間の上限を超える名前（サーバー実装の .RemoteServer.g.cs）も断る",
                FilesItemsDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = new string('a', 245) + ".g.cs",
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_InvalidOutputFileName),
                        new string('a', 245) + ".g.cs"
                    )
                )
            ),
            FilesRow(
                "files-outputfilename-split-ignores-name",
                "OutputFileNameValidationTests.SplitOutput_IgnoresOutputFileName",
                "分割出力では出力ファイル名を使わないので、断る名前でも生成を止めない",
                FilesItemsDiagram,
                new CodeGenerationOptions
                {
                    OutputFileName = "sub/Foo.g.cs",
                    SplitFilesByCategory = true,
                },
                DiagnosticExpectation.Contains(),
                FilesGenerated
            ),
            FilesValidOutputFileNameRow(
                "files-outputfilename-valid-gcs",
                "MyApp.g.cs",
                "MyApp.g.cs"
            ),
            FilesValidOutputFileNameRow("files-outputfilename-valid-cs", "MyApp.cs", "MyApp.g.cs"),
            FilesValidOutputFileNameRow(
                "files-outputfilename-valid-padded",
                " MyApp ",
                "MyApp.g.cs"
            ),
        ];

    /// <summary>層別出力の ON / OFF を比べた元のテストの構成（<c>Generate_Layered_KeepsContentIdenticalExceptNamespaces</c> の baseOptions）</summary>
    private static CodeGenerationOptions FilesLayeredBase() =>
        new()
        {
            RootNamespace = "Acme.App",
            GenerateRepositories = true,
            GenerateRemoteServices = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateValueObjects = true,
            GenerateApiDocs = true,
        };

    /// <summary>ApiDocsFileName の拡張子の正規化（英語版と日本語版の名前）</summary>
    private static ScenarioRow<CodegenCase> FilesNormalizedFileNameRow(
        string id,
        string fileName,
        string expectedEnglish,
        string expectedJapanese
    ) =>
        FilesRow(
            id,
            "ApiReferenceDocTests.ApiDocsFileName_NormalizesExtension",
            "ApiDocsFileName 指定でファイル名が決まり、拡張子は .g.md へ正規化される",
            CustomerOrderDiagram,
            new CodeGenerationOptions
            {
                OutputFileName = "EcOrder.g.cs",
                GenerateApiDocs = true,
                ApiDocsLanguage = ApiDocsLanguage.Both,
                ApiDocsFileName = fileName,
            },
            DiagnosticExpectation.Contains(),
            FilesMarkdown(expectedEnglish, expectedJapanese)
        );

    /// <summary>ApiDocsFileName の不正指定</summary>
    private static ScenarioRow<CodegenCase> FilesInvalidFileNameRow(string id, string fileName) =>
        FilesRow(
            id,
            "ApiReferenceDocTests.ApiDocsFileName_Invalid_ReportsError",
            "ApiDocsFileName の不正指定（パス区切り・ドライブ・ベース名なし）は生成時エラーになる",
            CustomerOrderDiagram,
            new CodeGenerationOptions { GenerateApiDocs = true, ApiDocsFileName = fileName },
            DiagnosticExpectation.Contains(
                ExpectedDiagnostic.ErrorAnyItems(
                    nameof(Strings.CodeGen_Error_InvalidApiDocsFileName)
                )
            )
        );

    /// <summary>CodeSubdirectory の不正指定</summary>
    private static ScenarioRow<CodegenCase> FilesInvalidCodeSubdirectoryRow(
        string id,
        string subdirectory
    ) =>
        FilesRow(
            id,
            "CodeSubdirectoryTests.Generate_InvalidSubdirectory_ReportsError",
            "診断: 不正なサブフォルダ（.. / 絶対パス）は出力モードに依らずエラーになる",
            OrderOnlyDiagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                GenerateRepositories = true,
                CodeSubdirectory = subdirectory,
            },
            DiagnosticExpectation.Contains(
                ExpectedDiagnostic.ErrorMentioning(
                    nameof(Strings.CodeGen_Error_InvalidOutputSubdirectory),
                    nameof(CodeGenerationOptions.CodeSubdirectory)
                )
            )
        );

    /// <summary>OutputFileName の不正指定</summary>
    private static ScenarioRow<CodegenCase> FilesInvalidOutputFileNameRow(
        string id,
        string outputFileName
    ) =>
        FilesRow(
            id,
            "OutputFileNameValidationTests.InvalidOutputFileName_IsReportedBeforeGeneration",
            "ファイル名として断る出力ファイル名は、生成前の診断でエラーになり何も出力しない",
            FilesItemsDiagram,
            new CodeGenerationOptions { OutputFileName = outputFileName },
            DiagnosticExpectation.Contains(
                ExpectedDiagnostic.ErrorMentioning(
                    nameof(Strings.CodeGen_Error_InvalidOutputFileName),
                    outputFileName.Trim()
                )
            )
        );

    /// <summary>OutputFileName の有効指定（拡張子は .g.cs へ正規化）</summary>
    private static ScenarioRow<CodegenCase> FilesValidOutputFileNameRow(
        string id,
        string outputFileName,
        string expected
    ) =>
        FilesRow(
            id,
            "OutputFileNameValidationTests.ValidOutputFileName_IsAccepted",
            "ファイル名だけの指定は受け付ける（拡張子は .g.cs へ正規化）",
            FilesItemsDiagram,
            new CodeGenerationOptions { OutputFileName = outputFileName },
            DiagnosticExpectation.Contains(),
            [new FileRule(".g.cs", Count: 1, Names: [expected])]
        );
}
