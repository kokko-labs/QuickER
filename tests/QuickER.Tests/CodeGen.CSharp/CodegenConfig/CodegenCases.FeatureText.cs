using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges.CodeRule;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：リモート・IncludeGraph・API リファレンス・VO の外部キー・拡張シム・XmlDoc・DB 定義メタ（RemoteServiceGenerationTests ほか） の、生成テキストの文字列で判定していたテストを、宣言の期待・実行の探針・解析の判定へ言い換えたもの
/// </summary>
internal static partial class CodegenCases
{
    /// <summary>リモート・IncludeGraph ほかのテストクラスの行を作る（説明は <paramref name="testClass"/> ＋ "." ＋ 元のメソッド名）</summary>
    private static ScenarioRow<CodegenCase> FeatureTextRow(
        string id,
        string testClass,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        IReadOnlyList<CodeRule>? code = null,
        IReadOnlyList<EfCoreModelFact>? model = null,
        IReadOnlyList<ExecCheck>? exec = null,
        IReadOnlyList<ParsedCheck>? parsed = null,
        IReadOnlyList<MarkdownRule>? markdown = null,
        IReadOnlyDictionary<string, string>? fileNamespaces = null,
        bool compile = false,
        string typeDialect = DialectNames.SqlServer,
        IReadOnlyList<GeneratedFile>? extraSources = null
    ) =>
        new(
            id,
            null,
            testClass + "." + description,
            new CodegenCase(
                diagram,
                options,
                typeDialect,
                DiagnosticExpectation.Contains(),
                FileNamespaces: fileNamespaces,
                ExtraSources: extraSources,
                Compile: compile,
                Probes: model is null ? null : [CodegenProbe.EfCoreModel],
                ProbeFacts: model is null ? null : new CodegenProbeFacts(ModelFacts: model),
                Code: code,
                Exec: exec,
                Parsed: parsed,
                Markdown: markdown
            )
        );

    /// <summary>名前に <paramref name="fragment"/> を含む型も、そういう名前のメンバーを持つ型も無い（元の「出力に語が現れない」の言い換え）</summary>
    private static CodeRule FeatureText_NoNameContaining(string fragment) =>
        NoTypes(
            $"名前（型・メンバー）に {fragment} を含む",
            type =>
                type.Name.Contains(fragment, StringComparison.Ordinal)
                || type.GetMembers()
                    .Any(member =>
                        !member.IsImplicitlyDeclared
                        && member.Name.Contains(fragment, StringComparison.Ordinal)
                    )
        );

    /// <summary>ファイル <paramref name="file"/> に宣言された型に、名前（型・メンバー）に <paramref name="fragment"/> を含むものが無い</summary>
    private static CodeRule FeatureText_NoNameContainingInFile(string file, string fragment) =>
        NoTypes(
            $"{file} に宣言され、名前（型・メンバー）に {fragment} を含む",
            type =>
                type.DeclaringSyntaxReferences.Any(reference =>
                    CodeRuleContext.FileName(reference.SyntaxTree) == file
                )
                && (
                    type.Name.Contains(fragment, StringComparison.Ordinal)
                    || type.GetMembers()
                        .Any(member =>
                            !member.IsImplicitlyDeclared
                            && member.Name.Contains(fragment, StringComparison.Ordinal)
                        )
                )
        );

    /// <summary>型 <paramref name="name"/> が宣言するメンバー（暗黙のものを除く）が 1 つも無い（元の空ボディ <c>{ }</c> の言い換え）</summary>
    private static CodeRule FeatureText_EmptyType(string name, int arity = 0) =>
        NoTypes(
            $"{name}（型パラメータ {arity} 個）でメンバーを宣言する",
            type =>
                type.Name == name
                && type.Arity == arity
                && type.GetMembers().Any(member => !member.IsImplicitlyDeclared)
        );

    // ===== RemoteServiceGenerationTests =====

    private const string RemoteClass = "RemoteServiceGenerationTests";

    /// <summary>
    /// 出力ファイル名が <c>.g.cs</c> で終わらなくても、API リファレンスの生成ファイル構成表が実際に書き出す名前（本体＋サーバー）と一致する
    /// （元は表の文字列に両方の名前があり、正規化前の名前 <c>Shop.cs</c> が無いこと）
    /// </summary>
    private static ScenarioRow<CodegenCase> FeatureText_RemoteOutputNameRow(
        string id,
        string outputFileName,
        string mainName,
        bool multiDialect
    ) =>
        FeatureTextRow(
            id,
            RemoteClass,
            $"Generate_NonGeneratedSuffixOutputFileName_ServerFileNameFollowsNormalizedMain(\"{outputFileName}\", {(multiDialect ? "true" : "false")})",
            Columns_RemoteServiceDiagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                OutputFileName = outputFileName,
                GenerateRepositories = true,
                GenerateRemoteServices = true,
                RepositoryDialects = multiDialect
                    ? [DialectNames.SqlServer, DialectNames.Sqlite]
                    : [DialectNames.SqlServer],
                GenerateApiDocs = true,
            },
            markdown:
            [
                MarkdownRule.ColumnEquals(
                    "Shop.g.md",
                    "File",
                    [mainName, "Shop.RemoteServer.g.cs"]
                ),
            ]
        );

    /// <summary>RemoteServiceGenerationTests.Generate_RemoteServices_RequestRecordNamesDoNotCollideAcrossEntities の図（Order と OrderLine に Count のクエリ）</summary>
    private static ErDiagram FeatureText_RemoteCollidingRequestNamesDiagram()
    {
        var diagram = Columns_RemoteServiceDiagram();
        diagram.Queries.Clear();

        var order = diagram.Entities[0];
        var orderLine = new Entity { TableName = "OrderLine" };
        orderLine.Columns.Add(
            new Column
            {
                Name = "OrderLineId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        orderLine.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = false,
            }
        );
        diagram.Entities.Add(orderLine);
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "LineSummary",
                Returns = QueryReturnShape.Count,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
            }
        );
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = orderLine.Id,
                Name = "Summary",
                Returns = QueryReturnShape.Count,
                Parameters =
                {
                    new QueryParameter { Name = "quantity", Type = "int32" },
                },
                Condition = "Quantity = @quantity",
            }
        );

        return diagram;
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextRemoteRows()
    {
        var remote = new CodeGenerationOptions
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateRemoteServices = true,
        };
        var split = new CodeGenerationOptions
        {
            RootNamespace = "Acme",
            GenerateRepositories = true,
            GenerateRemoteServices = true,
            SplitFilesByCategory = true,
        };

        return
        [
            FeatureText_RemoteOutputNameRow(
                "markdown-remote-service-file-table-shop-cs",
                "Shop.cs",
                "Shop.g.cs",
                false
            ),
            FeatureText_RemoteOutputNameRow(
                "markdown-remote-service-file-table-shop",
                "Shop",
                "Shop.g.cs",
                false
            ),
            FeatureText_RemoteOutputNameRow(
                "markdown-remote-service-file-table-padded-shop-cs",
                "  Shop.cs  ",
                "Shop.g.cs",
                false
            ),
            FeatureText_RemoteOutputNameRow(
                "markdown-remote-service-file-table-upper-case-suffix",
                "Shop.G.CS",
                "Shop.G.CS",
                false
            ),
            FeatureText_RemoteOutputNameRow(
                "markdown-remote-service-file-table-shop-cs-multi-dialect",
                "Shop.cs",
                "Shop.g.cs",
                true
            ),
            FeatureTextRow(
                "code-remote-service-request-record-names-per-entity",
                RemoteClass,
                "Generate_RemoteServices_RequestRecordNamesDoNotCollideAcrossEntities",
                FeatureText_RemoteCollidingRequestNamesDiagram,
                remote,
                code:
                [
                    Type("Order_LineSummaryRequest", kind: "record", modifiers: "private sealed"),
                    Member(
                        "Order_LineSummaryRequest",
                        "CustomerId",
                        "int CustomerId { get; init; }"
                    ),
                    Type("OrderLine_SummaryRequest", kind: "record", modifiers: "private sealed"),
                    Member("OrderLine_SummaryRequest", "Quantity", "int Quantity { get; init; }"),
                    NoType("OrderLineSummaryRequest"),
                ],
                compile: true
            ),
            FeatureTextRow(
                "code-remote-service-off-no-client-types",
                RemoteClass,
                "Generate_Default_DoesNotEmitRemoteServices",
                Columns_RemoteServiceDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                },
                code:
                [
                    FeatureText_NoNameContaining("RemoteJson"),
                    FeatureText_NoNameContaining("HttpRemoteRepository"),
                    FeatureText_NoNameContaining("AddGeneratedHttpRemoteRepositories"),
                ]
            ),
            FeatureTextRow(
                "code-remote-service-split-server-file-cross-usings",
                RemoteClass,
                "Generate_RemoteServicesWithSplit_EmitsServerFileWithCrossUsings",
                Columns_RemoteServiceDiagram,
                split,
                code:
                [
                    Using("RemoteServer.g.cs", "Acme.Entities"),
                    Using("RemoteServer.g.cs", "Acme.Repositories"),
                    Using("RemoteServer.g.cs", "Acme.Runtime"),
                    Using("RemoteServer.g.cs", "Acme.Runtime.AspNetCore"),
                ],
                fileNamespaces: new Dictionary<string, string>
                {
                    ["RemoteServer.g.cs"] = "Acme.RemoteServer",
                }
            ),
            FeatureTextRow(
                "code-remote-service-split-engine-in-fixed-runtime-file",
                RemoteClass,
                "Generate_RemoteServicesWithSplit_SeparatesEngineIntoFixedRuntimeFile",
                Columns_RemoteServiceDiagram,
                split,
                code:
                [
                    Using("Runtime.AspNetCore.g.cs", "Acme.Runtime"),
                    Type(
                        "RemoteServerEngine",
                        file: "Runtime.AspNetCore.g.cs",
                        kind: "class",
                        modifiers: "internal static"
                    ),
                    Type(
                        "RemoteBadRequestException",
                        file: "Runtime.AspNetCore.g.cs",
                        kind: "class",
                        modifiers: "internal sealed"
                    ),
                    Type(
                        "RemoteErrorDetailPolicy",
                        file: "Runtime.AspNetCore.g.cs",
                        kind: "class",
                        modifiers: "internal sealed"
                    ),
                    NoReferencesTo("Runtime.AspNetCore.g.cs", "Acme.Entities"),
                    FeatureText_NoNameContainingInFile(
                        "Runtime.AspNetCore.g.cs",
                        "MapOrderEndpoints"
                    ),
                    Type(
                        "GeneratedRemoteEndpoints",
                        file: "RemoteServer.g.cs",
                        kind: "class",
                        modifiers: "public static partial"
                    ),
                    // per-entity のエンドポイントは、固定エンジンの汎用マッピングを修飾付きで呼ぶだけ
                    Calls(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "RemoteServerEngine.MapCrud",
                        count: 1
                    ),
                ],
                fileNamespaces: new Dictionary<string, string>
                {
                    ["Runtime.AspNetCore.g.cs"] = "Acme.Runtime.AspNetCore",
                }
            ),
            FeatureTextRow(
                "code-remote-service-runtime-packages-server-uses-package",
                RemoteClass,
                "Generate_RemoteServicesWithRuntimePackages_ReferencesAspNetCorePackage",
                Columns_RemoteServiceDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme",
                    OutputFileName = "Shop.g.cs",
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                    UseRuntimePackages = true,
                },
                code:
                [
                    Using("Shop.RemoteServer.g.cs", RuntimePackages.AspNetCore),
                    NoType("RemoteServerEngine"),
                    NoType("RemoteBadRequestException"),
                    Type(
                        "GeneratedRemoteEndpoints",
                        file: "Shop.RemoteServer.g.cs",
                        kind: "class",
                        modifiers: "public static partial"
                    ),
                    // per-entity 側のテキストはモードに依らず同一（固定エンジンの汎用マッピングを修飾付きで呼ぶだけ）
                    Calls(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "RemoteServerEngine.MapCrud",
                        count: 1
                    ),
                ]
            ),
            FeatureTextRow(
                "code-remote-service-efcore-only-client-and-server",
                RemoteClass,
                "Generate_RemoteServicesWithEfCoreOnly_Works",
                Columns_RemoteServiceDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                    GenerateRemoteServices = true,
                },
                code:
                [
                    Type("HttpOrderRemoteRepository", file: "QuickEREntities.g.cs", kind: "class"),
                    Member("GeneratedRemoteEndpoints", "MapGeneratedRemoteEndpoints"),
                    Type("GeneratedRemoteEndpoints", file: "QuickEREntities.RemoteServer.g.cs"),
                ]
            ),
            FeatureTextRow(
                "code-remote-service-split-http-client-own-file",
                RemoteClass,
                "Generate_Split_MovesHttpClientToOwnFile",
                Columns_RemoteServiceDiagram,
                remote with
                {
                    SplitFilesByCategory = true,
                },
                code:
                [
                    Type(
                        "IOrderRemoteRepository",
                        file: "Repositories.g.cs",
                        kind: "interface",
                        modifiers: "public partial"
                    ),
                    NoUsing("Repositories.g.cs", "System.Net.Http"),
                    Type(
                        "HttpOrderRemoteRepository",
                        file: "Repositories.Http.g.cs",
                        kind: "class",
                        modifiers: "public sealed partial"
                    ),
                    Type(
                        "GeneratedHttpRemoteRepositoryServiceCollectionExtensions",
                        file: "Repositories.Http.g.cs",
                        kind: "class",
                        modifiers: "public static"
                    ),
                    Type(
                        "OwnedHttpClient",
                        file: "Repositories.Http.g.cs",
                        kind: "class",
                        modifiers: "private sealed"
                    ),
                    Member(
                        "GeneratedHttpRemoteRepositoryServiceCollectionExtensions",
                        "AddGeneratedHttpRemoteRepositories",
                        count: 4
                    ),
                ],
                fileNamespaces: new Dictionary<string, string>
                {
                    ["Repositories.Http.g.cs"] = "Test.Ns.Repositories",
                }
            ),
            FeatureTextRow(
                "code-remote-service-non-split-http-client-in-main",
                RemoteClass,
                "Generate_NonSplit_KeepsHttpClientInMainFile",
                Columns_RemoteServiceDiagram,
                remote,
                code:
                [
                    Type(
                        "HttpOrderRemoteRepository",
                        file: "QuickEREntities.g.cs",
                        kind: "class",
                        modifiers: "public sealed partial"
                    ),
                    Type(
                        "GeneratedHttpRemoteRepositoryServiceCollectionExtensions",
                        file: "QuickEREntities.g.cs",
                        kind: "class",
                        modifiers: "public static"
                    ),
                ]
            ),
        ];
    }

    // ===== IncludeGraphGenerationTests =====

    private const string IncludeClass = "IncludeGraphGenerationTests";

    /// <summary>IncludeGraphGenerationTests.SiblingBranches_ShareOneNodePerNavigation の図（root → mid → {leaf, twig}）</summary>
    private static ErDiagram FeatureText_IncludeSiblingDiagram()
    {
        var rootId = Columns_Key("root_id");
        var root = new Entity { TableName = "root", Columns = { rootId } };

        var midId = Columns_Key("mid_id");
        var midRootId = Columns_Fk("root_id", isForeignKey: false);
        var mid = new Entity { TableName = "mid", Columns = { midId, midRootId } };

        var leafId = Columns_Key("leaf_id");
        var leafMidId = Columns_Fk("mid_id", isForeignKey: false);
        var leaf = new Entity { TableName = "leaf", Columns = { leafId, leafMidId } };

        var twigId = Columns_Key("twig_id");
        var twigMidId = Columns_Fk("mid_id", isForeignKey: false);
        var twig = new Entity { TableName = "twig", Columns = { twigId, twigMidId } };

        return new ErDiagram
        {
            Entities = { root, mid, leaf, twig },
            Relationships =
            {
                Columns_OneToMany(root, rootId, mid, midRootId),
                Columns_OneToMany(mid, midId, leaf, leafMidId),
                Columns_OneToMany(mid, midId, twig, twigMidId),
            },
        };
    }

    /// <summary>
    /// <c>SqlQueryExtensions</c> の静的フィールド（<c>_{エンティティ}Graph</c>＝<c>Lazy&lt;IReadOnlyList&lt;IncludeNode&gt;&gt;</c>）が作るツリーを、
    /// <c>型.ナビ(子,子)</c> の文字列（兄弟は並びを問わず名前順）に直して期待と完全一致で比べる
    /// </summary>
    /// <param name="graphs">フィールド名とその期待のツリー（ルートが複数なら <c>,</c> 区切り）</param>
    private static ExecCheck FeatureText_IncludeTrees(
        params (string Field, string Expected)[] graphs
    ) =>
        new(
            "include-graph-trees",
            assembly =>
            {
                var extensions = ProbeKit.Type(assembly, "SqlQueryExtensions");

                return graphs
                    .Select(graph =>
                    {
                        var lazy = ProbeKit.GetStaticField(extensions, graph.Field);
                        var roots = (System.Collections.IEnumerable)
                            lazy.GetType().GetProperty("Value")!.GetValue(lazy)!;
                        var actual = string.Join(
                            ",",
                            roots
                                .Cast<object>()
                                .Select(FeatureText_RenderIncludeNode)
                                .Order(StringComparer.Ordinal)
                        );

                        return actual == graph.Expected
                            ? null
                            : $"{graph.Field} のツリーが {actual}（期待 {graph.Expected}）";
                    })
                    .OfType<string>();
            }
        );

    private static string FeatureText_RenderIncludeNode(object node)
    {
        var property = (PropertyInfo)ProbeKit.Get(node, "Property")!;
        var children = ((System.Collections.IEnumerable)ProbeKit.Get(node, "Children")!)
            .Cast<object>()
            .Select(FeatureText_RenderIncludeNode)
            .Order(StringComparer.Ordinal);

        return $"{property.DeclaringType!.Name}.{property.Name}({string.Join(",", children)})";
    }

    /// <summary>
    /// 子を持たない（または辿るとツリーが空になる）エンティティの <c>IncludeGraph</c> は、渡したクエリをそのまま返す（何も足さない形）。
    /// 初期化していない <c>SqlQuery&lt;T&gt;</c> を渡して、同じ参照が返ることで見る
    /// </summary>
    private static ExecCheck FeatureText_IncludeGraphIsNoOp(params string[] entityClasses) =>
        new(
            "include-graph-no-op",
            assembly =>
            {
                var extensions = ProbeKit.Type(assembly, "SqlQueryExtensions");
                var queryDefinition = ProbeKit.Type(assembly, "SqlQuery`1");

                return entityClasses
                    .Select(entityClass =>
                    {
                        var query = RuntimeHelpers.GetUninitializedObject(
                            queryDefinition.MakeGenericType(ProbeKit.Type(assembly, entityClass))
                        );
                        var returned = ProbeKit.CallStatic(extensions, "IncludeGraph", query);

                        return ReferenceEquals(returned, query)
                            ? null
                            : $"{entityClass} の IncludeGraph が渡したクエリと別のものを返した";
                    })
                    .OfType<string>();
            }
        );

    private static CodeRule FeatureText_IncludeGraphMember(string entity) =>
        Member(
            "SqlQueryExtensions",
            "IncludeGraph",
            $"SqlQuery<{entity}> IncludeGraph(this SqlQuery<{entity}> query)",
            modifiers: "public static"
        );

    private static CodeRule FeatureText_GetByIdMember(
        string entity,
        string keyType,
        bool includable
    ) =>
        Member(
            "SqlQueryExtensions",
            "GetByIdAsync",
            includable
                ? $"Task<{entity}?> GetByIdAsync<TProperty>(this IncludableSqlQuery<{entity}, TProperty> query, {keyType} id, CancellationToken cancellationToken = default(CancellationToken))"
                : $"Task<{entity}?> GetByIdAsync(this SqlQuery<{entity}> query, {keyType} id, CancellationToken cancellationToken = default(CancellationToken))",
            modifiers: "public static"
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextIncludeRows()
    {
        var repository = Columns_IncludeGraphOptions();

        return
        [
            FeatureTextRow(
                "code-include-graph-chain-nested-tree-and-leaf-no-op",
                IncludeClass,
                "Chain_BuildsNestedTree / LeafEntity_IsGeneratedAsNoOp",
                Columns_IncludeGraphChainDiagram,
                repository,
                code:
                [
                    Member(
                        "SqlQueryExtensions",
                        "_midEntityGraph",
                        "Lazy<IReadOnlyList<IncludeNode>> _midEntityGraph",
                        modifiers: "private static readonly"
                    ),
                    NoMember("SqlQueryExtensions", "_leafEntityGraph"),
                    FeatureText_IncludeGraphMember("RootEntity"),
                    // IncludeGraph のオーバーロードはエンティティごとにあり、AddIncludeNodes を呼ぶのは子を持つ root と mid の 2 本（leaf は no-op）
                    Calls("SqlQueryExtensions", "IncludeGraph", "AddIncludeNodes", count: 2),
                    ReferencesSymbol(
                        "SqlQueryExtensions",
                        "IncludeGraph",
                        "SqlQueryExtensions._rootEntityGraph"
                    ),
                    ReferencesSymbol(
                        "SqlQueryExtensions",
                        "IncludeGraph",
                        "SqlQueryExtensions._midEntityGraph"
                    ),
                    FeatureText_IncludeGraphMember("LeafEntity"),
                ],
                exec:
                [
                    FeatureText_IncludeTrees(
                        ("_rootEntityGraph", "RootEntity.Mids(MidEntity.Leafs())"),
                        ("_midEntityGraph", "MidEntity.Leafs()")
                    ),
                    FeatureText_IncludeGraphIsNoOp("LeafEntity"),
                ]
            ),
            FeatureTextRow(
                "code-include-graph-sibling-branches-share-one-node",
                IncludeClass,
                "SiblingBranches_ShareOneNodePerNavigation",
                FeatureText_IncludeSiblingDiagram,
                repository,
                exec:
                [
                    FeatureText_IncludeTrees(
                        ("_rootEntityGraph", "RootEntity.Mids(MidEntity.Leafs(),MidEntity.Twigs())")
                    ),
                ]
            ),
            FeatureTextRow(
                "code-include-graph-self-reference-is-skipped",
                IncludeClass,
                "SelfReference_IsSkippedAndReported",
                Columns_IncludeGraphSelfReferenceDiagram,
                repository,
                code:
                [
                    NoMember("SqlQueryExtensions", "_nodeEntityGraph"),
                    FeatureText_IncludeGraphMember("NodeEntity"),
                ],
                exec: [FeatureText_IncludeGraphIsNoOp("NodeEntity")]
            ),
            FeatureTextRow(
                "code-include-graph-mutual-reference-drops-revisiting-edge",
                IncludeClass,
                "MutualReference_SkipsOnlyTheRevisitingEdge",
                Columns_IncludeGraphMutualReferenceDiagram,
                repository,
                exec:
                [
                    FeatureText_IncludeTrees(
                        ("_alphaEntityGraph", "AlphaEntity.Betas()"),
                        ("_betaEntityGraph", "BetaEntity.Alphas()")
                    ),
                ]
            ),
            FeatureTextRow(
                "code-include-graph-without-repository-contract-emits-nothing",
                IncludeClass,
                "WithoutRepositoryContract_EmitsNothing",
                Columns_IncludeGraphChainDiagram,
                new CodeGenerationOptions { RootNamespace = "Test.Ns" },
                code:
                [
                    NoType("SqlQueryExtensions"),
                    Text_NoTypeDeclaring("IncludeGraph"),
                    Text_NoTypeDeclaring("GetByIdAsync"),
                ]
            ),
            FeatureTextRow(
                "code-include-graph-efcore-only-emits-sugar",
                IncludeClass,
                "ContractOnlyBackends_EmitGetById(true, false) / ContractOnlyBackends_EmitIncludeGraph(true, false)",
                Columns_IncludeGraphChainDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateEfCoreRepositories = true,
                    GenerateInMemoryRepositories = false,
                },
                code:
                [
                    FeatureText_GetByIdMember("RootEntity", "int", includable: false),
                    FeatureText_GetByIdMember("RootEntity", "int", includable: true),
                    FeatureText_IncludeGraphMember("RootEntity"),
                ]
            ),
            FeatureTextRow(
                "code-include-graph-inmemory-only-emits-sugar",
                IncludeClass,
                "ContractOnlyBackends_EmitGetById(false, true) / ContractOnlyBackends_EmitIncludeGraph(false, true)",
                Columns_IncludeGraphChainDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateEfCoreRepositories = false,
                    GenerateInMemoryRepositories = true,
                },
                code:
                [
                    FeatureText_GetByIdMember("RootEntity", "int", includable: false),
                    FeatureText_GetByIdMember("RootEntity", "int", includable: true),
                    FeatureText_IncludeGraphMember("RootEntity"),
                ]
            ),
        ];
    }

    // ===== ApiReferenceDocTests =====

    private const string ApiDocsClass = "ApiReferenceDocTests";

    private const string ApiDocsEnglish = "QuickEREntities.g.md";

    private const string ApiDocsJapanese = "QuickEREntities.ja.g.md";

    /// <summary>ApiReferenceDocTests.Markdown_Cells_FoldEveryLineBreakCharacter の図（customers.name の説明に NEL・FF・LS・PS を含む）</summary>
    private static ErDiagram FeatureText_ApiDocsLineBreakDiagram()
    {
        var diagram = CustomerOrderDiagram();
        diagram.Entities[0].Columns[1].Description =
            $"a{(char)0x85}b{(char)0x0C}c{(char)0x2028}d{(char)0x2029}e";
        return diagram;
    }

    /// <summary>ApiReferenceDocTests.Example_UsesFirstEntityWithRepository の図（先頭の customers が複合主キー＝Repository が出ない）</summary>
    private static ErDiagram FeatureText_ApiDocsCompositeKeyFirstDiagram()
    {
        var diagram = CustomerOrderDiagram();
        diagram.Entities[0].Columns[1].IsPrimaryKey = true;
        return diagram;
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextApiDocsRows() =>
        [
            FeatureTextRow(
                "markdown-api-docs-cells-fold-every-line-break-character",
                ApiDocsClass,
                "Markdown_Cells_FoldEveryLineBreakCharacter",
                FeatureText_ApiDocsLineBreakDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                },
                markdown:
                [
                    MarkdownRule.Cell(
                        ApiDocsEnglish,
                        "Property",
                        "Name",
                        "Description",
                        "a b c d e"
                    ),
                    MarkdownRule.TablesWellFormed(ApiDocsEnglish),
                    MarkdownRule.Cell(ApiDocsJapanese, "プロパティ", "Name", "説明", "a b c d e"),
                    MarkdownRule.TablesWellFormed(ApiDocsJapanese),
                ]
            ),
            FeatureTextRow(
                "markdown-api-docs-example-uses-first-entity-with-repository",
                ApiDocsClass,
                "Example_UsesFirstEntityWithRepository",
                FeatureText_ApiDocsCompositeKeyFirstDiagram,
                new CodeGenerationOptions { GenerateApiDocs = true, GenerateRepositories = true },
                markdown: [MarkdownRule.CodeCreatesExactly(ApiDocsEnglish, ["OrderEntity"])]
            ),
            FeatureTextRow(
                "markdown-api-docs-single-dialect-schema-and-default-di",
                ApiDocsClass,
                "SingleDialect_Content_ContainsSchemaAndDefaultDi",
                CustomerOrderDiagram,
                new CodeGenerationOptions { GenerateApiDocs = true, GenerateRepositories = true },
                markdown:
                [
                    MarkdownRule.ColumnEquals(
                        ApiDocsEnglish,
                        "Class",
                        ["CustomerEntity", "OrderEntity"]
                    ),
                    MarkdownRule.ColumnContains(
                        ApiDocsEnglish,
                        "Property",
                        ["CustomerId", "Amount"]
                    ),
                    MarkdownRule.ColumnContains(
                        ApiDocsEnglish,
                        "DB type",
                        ["int32", "decimal(10,2)"]
                    ),
                    MarkdownRule.CodeInvokes(ApiDocsEnglish, "AddGeneratedSqlServerRepositories"),
                    MarkdownRule.Link(
                        ApiDocsEnglish,
                        "https://github.com/kokko-labs/QuickER/blob/main/docs/code-generation.md"
                    ),
                ]
            ),
            FeatureTextRow(
                "markdown-api-docs-efcore-di",
                ApiDocsClass,
                "EfCore_Content_ContainsEfCoreDi",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                },
                markdown:
                [
                    MarkdownRule.CodeInvokes(ApiDocsEnglish, "AddGeneratedEfCoreRepositories"),
                    MarkdownRule.CodeInvokes(
                        ApiDocsEnglish,
                        "AddGeneratedSqlServerRepositories",
                        present: false
                    ),
                ]
            ),
            FeatureTextRow(
                "markdown-api-docs-multi-dialect-di",
                ApiDocsClass,
                "MultiDialect_Content_ContainsDialectSpecificDi",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                markdown:
                [
                    MarkdownRule.CodeInvokes(ApiDocsEnglish, "AddGeneratedSqlServerRepositories"),
                    MarkdownRule.CodeInvokes(ApiDocsEnglish, "AddGeneratedSqliteRepositories"),
                ]
            ),
            FeatureTextRow(
                "markdown-api-docs-entity-only-omits-data-access-sections",
                ApiDocsClass,
                "EntityOnly_OmitsDataAccessSections",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    GenerateEditModels = false,
                    GenerateMappers = false,
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = false,
                    GenerateInMemoryRepositories = false,
                },
                markdown:
                [
                    MarkdownRule.ColumnContains(ApiDocsEnglish, "Class", ["CustomerEntity"]),
                    // 見出し（H2）は「Entities」「Entity details」「Generated file layout」の 3 つだけ（「Data access API」「Usage」の節が丸ごと無い）
                    MarkdownRule.HeadingCount(ApiDocsEnglish, 2, 3),
                    MarkdownRule.CodeBlockCount(ApiDocsEnglish, 0),
                ]
            ),
        ];

    // ===== ValueObjectForeignKeyUnificationTests =====

    private const string VoForeignKeyClass = "ValueObjectForeignKeyUnificationTests";

    /// <summary>ValueObjectForeignKeyUnificationTests.SelfReferenceDiagram（nodes.parent_node_id → nodes.node_id）</summary>
    private static ErDiagram FeatureText_VoSelfReferenceDiagram()
    {
        var nodeId = Columns_Key("node_id");
        var parentNodeId = Columns_Fk("parent_node_id", isForeignKey: true);
        var nodes = new Entity { TableName = "nodes", Columns = { nodeId, parentNodeId } };

        return new ErDiagram
        {
            Entities = { nodes },
            Relationships = { Columns_OneToMany(nodes, nodeId, nodes, parentNodeId) },
        };
    }

    /// <summary>ValueObjectForeignKeyUnificationTests.FKチェーンは不動点まで辿る の図（codes ← mids.mid_ref ← leafs.leaf_ref）</summary>
    private static ErDiagram FeatureText_VoFkChainDiagram()
    {
        var codeId = Columns_Key("code_id");
        var codes = new Entity { TableName = "codes", Columns = { codeId } };

        var midId = Columns_Key("mid_id");
        var midRef = Columns_Fk("mid_ref", isForeignKey: true);
        var mids = new Entity { TableName = "mids", Columns = { midId, midRef } };

        var leafId = Columns_Key("leaf_id");
        var leafRef = Columns_Fk("leaf_ref", isForeignKey: true);
        var leafs = new Entity { TableName = "leafs", Columns = { leafId, leafRef } };

        return new ErDiagram
        {
            Entities = { codes, mids, leafs },
            Relationships =
            {
                Columns_OneToMany(codes, codeId, mids, midRef),
                // 中間列（それ自身が FK）を親に持つ FK＝チェーン
                Columns_OneToMany(mids, midRef, leafs, leafRef),
            },
        };
    }

    /// <summary>ValueObjectForeignKeyUnificationTests.相互FKは循環しても決定的に解決する の図（alpha.peer_id と beta.peer_ref が互いを親に持つ）</summary>
    private static ErDiagram FeatureText_VoMutualForeignKeyDiagram()
    {
        var alphaId = Columns_Key("alpha_id");
        var peerId = Columns_Fk("peer_id", isForeignKey: true);
        var alpha = new Entity { TableName = "alpha", Columns = { alphaId, peerId } };

        var betaId = Columns_Key("beta_id");
        var peerRef = Columns_Fk("peer_ref", isForeignKey: true);
        var beta = new Entity { TableName = "beta", Columns = { betaId, peerRef } };

        return new ErDiagram
        {
            Entities = { alpha, beta },
            Relationships =
            {
                Columns_OneToMany(alpha, peerId, beta, peerRef),
                Columns_OneToMany(beta, peerRef, alpha, peerId),
            },
        };
    }

    /// <summary>ValueObjectForeignKeyUnificationTests.下地の型が違う列ペアは統一しない の図（参照先は nvarchar(20)・子は int）</summary>
    private static ErDiagram FeatureText_VoChildTypeDiffersDiagram()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "nvarchar(20)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var orderId = Columns_Key("order_id");
        var shipCustomerId = Columns_Fk("ship_customer_id", isForeignKey: true);
        var orders = new Entity { TableName = "orders", Columns = { orderId, shipCustomerId } };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { Columns_OneToMany(customers, customerId, orders, shipCustomerId) },
        };
    }

    /// <summary>ValueObjectForeignKeyUnificationTests.同じ型の親を複数参照するのは正常 の図（別テーブルだが列名が同じ親が 2 つ）</summary>
    private static ErDiagram FeatureText_VoTwoParentsOfSameTypeDiagram()
    {
        var personId = Columns_Key("person_id");
        var people = new Entity { TableName = "people", Columns = { personId } };

        var otherPersonId = Columns_Key("person_id");
        var archived = new Entity { TableName = "archived_people", Columns = { otherPersonId } };

        var itemId = Columns_Key("item_id");
        var ownerId = Columns_Fk("owner_id", isForeignKey: true);
        var items = new Entity { TableName = "items", Columns = { itemId, ownerId } };

        return new ErDiagram
        {
            Entities = { people, archived, items },
            Relationships =
            {
                Columns_OneToMany(people, personId, items, ownerId),
                Columns_OneToMany(archived, otherPersonId, items, ownerId),
            },
        };
    }

    /// <summary>エンティティ <paramref name="entity"/> の列プロパティ <paramref name="property"/> の型が <paramref name="type"/>（<c>{ get; set; }</c>）で、子列名由来の型 <paramref name="absentType"/> は無い</summary>
    private static IEnumerable<CodeRule> FeatureText_VoPropertyType(
        string entity,
        string property,
        string type,
        string? absentType
    )
    {
        yield return Member(
            entity,
            property,
            $"{type} {property} {{ get; set; }}",
            modifiers: "public"
        );

        if (absentType is not null)
        {
            yield return NoType(absentType);
            yield return Text_NoMemberOfType(absentType);
            yield return Text_NoMemberOfType(absentType + "?");
        }
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextVoForeignKeyRows()
    {
        var vo = Columns_VoOptions();

        return
        [
            FeatureTextRow(
                "code-vo-foreign-key-child-shares-parent-type-in-entity-and-editmodel",
                VoForeignKeyClass,
                "子列は親のVO型を共有する / EditModelも親のVO型を共有する",
                Columns_VoMismatchedNameDiagram,
                vo,
                code:
                [
                    .. FeatureText_VoPropertyType(
                        "OrderEntity",
                        "ShipCustomerId",
                        "CustomerIdValue?",
                        "ShipCustomerIdValue"
                    ),
                    Type("CustomerIdValue", kind: "class", modifiers: "public sealed partial"),
                    Member(
                        "OrderEditModel",
                        "ShipCustomerId",
                        "CustomerIdValue? ShipCustomerId { get; internal set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderEditModel",
                        "_shipCustomerId",
                        "CustomerIdValue? _shipCustomerId",
                        modifiers: "private"
                    ),
                ]
            ),
            FeatureTextRow(
                "model-vo-foreign-key-efcore-fluent-uses-parent-type",
                VoForeignKeyClass,
                "EfCoreFluentも親のVO型を使う",
                Columns_VoMismatchedNameDiagram,
                vo with
                {
                    GenerateEfCoreRepositories = true,
                    GenerateRepositories = false,
                },
                code:
                [
                    Member(
                        "OrderEntity",
                        "ShipCustomerId",
                        "CustomerIdValue? ShipCustomerId { get; set; }",
                        modifiers: "public"
                    ),
                ],
                model:
                [
                    // モデルの CLR 型は上の Member（親の VO 型）で、プロバイダ側は int の変換器。モデルを組めること自体が FK と PK の CLR 型の互換の検証
                    EfCoreModelFact.Property(
                        "OrderEntity",
                        "ShipCustomerId",
                        column: "ship_customer_id",
                        providerClrType: typeof(int)
                    ),
                ]
            ),
            FeatureTextRow(
                "code-vo-foreign-key-self-reference-shares-parent-type",
                VoForeignKeyClass,
                "自己参照も親のVO型を共有する",
                FeatureText_VoSelfReferenceDiagram,
                vo,
                code:
                [
                    .. FeatureText_VoPropertyType(
                        "NodeEntity",
                        "ParentNodeId",
                        "NodeIdValue?",
                        "ParentNodeIdValue"
                    ),
                ]
            ),
            FeatureTextRow(
                "code-vo-foreign-key-chain-converges-to-root-parent-type",
                VoForeignKeyClass,
                "FKチェーンは不動点まで辿る",
                FeatureText_VoFkChainDiagram,
                vo,
                code:
                [
                    .. FeatureText_VoPropertyType(
                        "MidEntity",
                        "MidRef",
                        "CodeIdValue?",
                        "MidRefValue"
                    ),
                    .. FeatureText_VoPropertyType(
                        "LeafEntity",
                        "LeafRef",
                        "CodeIdValue?",
                        "LeafRefValue"
                    ),
                ]
            ),
            FeatureTextRow(
                "code-vo-foreign-key-mutual-cycle-keeps-own-types",
                VoForeignKeyClass,
                "相互FKは循環しても決定的に解決する",
                FeatureText_VoMutualForeignKeyDiagram,
                vo,
                code:
                [
                    .. FeatureText_VoPropertyType("AlphaEntity", "PeerId", "PeerIdValue?", null),
                    .. FeatureText_VoPropertyType("BetaEntity", "PeerRef", "PeerRefValue?", null),
                ]
            ),
            FeatureTextRow(
                "code-vo-foreign-key-underlying-type-differs-keeps-own-type",
                VoForeignKeyClass,
                "下地の型が違う列ペアは統一しない",
                FeatureText_VoChildTypeDiffersDiagram,
                vo,
                code:
                [
                    .. FeatureText_VoPropertyType(
                        "OrderEntity",
                        "ShipCustomerId",
                        "ShipCustomerIdValue?",
                        null
                    ),
                ]
            ),
            FeatureTextRow(
                "code-vo-foreign-key-value-objects-off-keeps-plain-type",
                VoForeignKeyClass,
                "VO無効では統一しない",
                Columns_VoMismatchedNameDiagram,
                vo with
                {
                    GenerateValueObjects = false,
                },
                code: [.. FeatureText_VoPropertyType("OrderEntity", "ShipCustomerId", "int?", null)]
            ),
            FeatureTextRow(
                "code-vo-foreign-key-two-parents-of-same-type",
                VoForeignKeyClass,
                "同じ型の親を複数参照するのは正常",
                FeatureText_VoTwoParentsOfSameTypeDiagram,
                vo,
                code:
                [
                    .. FeatureText_VoPropertyType("ItemEntity", "OwnerId", "PersonIdValue?", null),
                ]
            ),
        ];
    }

    // ===== XmlDocDescriptionTests =====

    private const string XmlDocClass = "XmlDocDescriptionTests";

    /// <summary>XmlDocDescriptionTests.BuildDiagram（customers テーブルの PK と name 列に、テーブル・列の説明を差し込む）</summary>
    private static ErDiagram FeatureText_XmlDocDiagram(
        string tableDescription,
        string columnDescription
    ) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Description = tableDescription,
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                            Description = columnDescription,
                        },
                    ],
                },
            ],
        };

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextXmlDocRows()
    {
        var plain = new CodeGenerationOptions { RootNamespace = "Sample.Domain" };

        return
        [
            FeatureTextRow(
                "code-xmldoc-table-description-is-entity-and-editmodel-summary",
                XmlDocClass,
                "EntityClass_WithDescription_UsesDescriptionInSummary / EditModelClass_WithDescription_UsesDescriptionInSummary",
                () => FeatureText_XmlDocDiagram("顧客マスタ", ""),
                plain,
                code:
                [
                    XmlDocOwners("summary", "顧客マスタ", ["CustomerEditModel", "CustomerEntity"]),
                ]
            ),
            FeatureTextRow(
                "code-xmldoc-column-description-is-property-summary",
                XmlDocClass,
                "EntityProperty_WithDescription_UsesDescriptionInSummary",
                () => FeatureText_XmlDocDiagram("", "顧客名"),
                plain,
                code:
                [
                    XmlDocOwners(
                        "summary",
                        "顧客名",
                        [
                            "CustomerEditModel.BindingName",
                            "CustomerEditModel._bindingName",
                            "CustomerEntity.Name",
                        ]
                    ),
                ]
            ),
            FeatureTextRow(
                "code-xmldoc-column-description-is-value-object-summary",
                XmlDocClass,
                "ValueObject_WithDescription_UsesDescriptionInSummary",
                () => FeatureText_XmlDocDiagram("", "顧客名"),
                plain with
                {
                    GenerateValueObjects = true,
                },
                code:
                [
                    XmlDocOwners(
                        "summary",
                        "顧客名",
                        [
                            "CustomerEditModel.BindingName",
                            "CustomerEditModel._bindingName",
                            "CustomerEntity.Name",
                            "NameValue",
                        ]
                    ),
                ]
            ),
            FeatureTextRow(
                "code-xmldoc-xml-special-characters-are-escaped",
                XmlDocClass,
                "Description_WithXmlSpecialChars_IsEscaped",
                () => FeatureText_XmlDocDiagram("", "A & B < C > D"),
                plain,
                // 要素の中身は XML のままの文字列で比べる＝特殊文字が &amp; &lt; &gt; にエスケープされている
                code:
                [
                    XmlDocOwners(
                        "summary",
                        "A &amp; B &lt; C &gt; D",
                        [
                            "CustomerEditModel.BindingName",
                            "CustomerEditModel._bindingName",
                            "CustomerEntity.Name",
                        ]
                    ),
                ],
                compile: true
            ),
            FeatureTextRow(
                "code-xmldoc-line-breaks-collapse-to-one-line",
                XmlDocClass,
                "Description_WithLineBreaks_IsCollapsedToSingleLine",
                () => FeatureText_XmlDocDiagram("", "1 行目\r\n2 行目\n3 行目"),
                plain,
                code:
                [
                    XmlDocOwners(
                        "summary",
                        "1 行目 2 行目 3 行目",
                        [
                            "CustomerEditModel.BindingName",
                            "CustomerEditModel._bindingName",
                            "CustomerEntity.Name",
                        ]
                    ),
                ],
                compile: true
            ),
        ];
    }

    // ===== ExtensionShimGenerationTests =====

    private const string ShimClass = "ExtensionShimGenerationTests";

    /// <summary>型 <paramref name="name"/>（単純名）の型パラメータが 1 つで、その制約が <paramref name="constraint"/>（<see cref="CodeRule.Format"/>）の 1 つだけ</summary>
    private static CodeRule FeatureText_SingleTypeParameterConstraint(
        string name,
        string constraint
    ) =>
        NoTypes(
            $"{name} の型パラメータの制約が {constraint} だけでない",
            type =>
                type.Name == name
                && !(
                    type.TypeParameters.Length == 1
                    && type.TypeParameters[0].ConstraintTypes.Length == 1
                    && type.TypeParameters[0].ConstraintTypes[0].ToDisplayString(CodeRule.Format)
                        == constraint
                )
        );

    /// <summary>型 <paramref name="name"/>（単純名）が直接のインターフェイスとして <paramref name="implemented"/> を実装する</summary>
    private static CodeRule FeatureText_Implements(string name, string implemented) =>
        NoTypes(
            $"{name} が {implemented} を実装しない",
            type =>
                type.Name == name
                && !type.Interfaces.Any(item =>
                    item.ToDisplayString(CodeRule.Format) == implemented
                )
        );

    /// <summary>マーカー <c>IValueObject</c> を直接実装する型は、共通ルート <c>ValueObjectBase</c> の 1 本だけ（系統別基底・値オブジェクトは継承で得る）</summary>
    private static CodeRule FeatureText_MarkerImplementedOnlyByRoot() =>
        NoTypes(
            "IValueObject を直接実装する ValueObjectBase 以外",
            type =>
                type.Name != "ValueObjectBase"
                && type.Interfaces.Any(item =>
                    item.ToDisplayString(CodeRule.Format) == "IValueObject"
                )
        );

    /// <summary>系統別基底 6 本の「クラス名 → 直接の基底」対応（すべて共通ルートへ着地する）</summary>
    private static (string Class, string BaseType)[] FeatureText_ValueObjectFamilyBases() =>
        [
            ("ValueObjectOrderedBase<TSelf, TValue>", "ValueObjectBase<TSelf, TValue>"),
            ("ValueObjectStringBase<TSelf>", "ValueObjectBase<TSelf, string>"),
            ("ValueObjectBooleanBase<TSelf>", "ValueObjectBase<TSelf, bool>"),
            ("ValueObjectDateTimeBase<TSelf>", "ValueObjectOrderedBase<TSelf, DateTime>"),
            ("ValueObjectBinaryBase<TSelf>", "ValueObjectBase<TSelf, byte[]>"),
            ("ValueObjectGuidKeyBase<TSelf>", "ValueObjectBase<TSelf, string>"),
        ];

    /// <summary>Entity・EditModel・値オブジェクトの拡張面（空の partial が現行名で固定ランタイムの <c>*Core</c> へ着地する）</summary>
    private static IEnumerable<CodeRule> FeatureText_ShimSurfaceRules(
        string? entities,
        string? editModels,
        string? valueObjects
    )
    {
        yield return Type(
            "EntityBase",
            file: entities,
            kind: "class",
            modifiers: "public abstract partial",
            baseType: "EntityBaseCore"
        );
        yield return FeatureText_EmptyType("EntityBase");
        yield return Type(
            "EditModelBase<TSelf>",
            file: editModels,
            kind: "class",
            modifiers: "public abstract partial",
            baseType: "EditModelBaseCore<TSelf>"
        );
        yield return FeatureText_SingleTypeParameterConstraint(
            "EditModelBase",
            "EditModelBase<TSelf>"
        );
        yield return FeatureText_EmptyType("EditModelBase", arity: 1);
        yield return Type(
            "IValueObject",
            file: valueObjects,
            kind: "interface",
            modifiers: "public partial",
            interfaces: ["IValueObjectCore"]
        );
        yield return FeatureText_EmptyType("IValueObject");

        // 全 VO の共通ルートだけがマーカー IValueObject を実装し、系統別 6 本は継承でルートへ着地する
        yield return Type(
            "ValueObjectBase<TSelf, TValue>",
            file: valueObjects,
            kind: "class",
            modifiers: "public abstract partial",
            baseType: "ValueObjectBaseCore<TSelf, TValue>",
            interfaces: ["IValueObject"]
        );
        yield return FeatureText_MarkerImplementedOnlyByRoot();

        foreach (var (className, baseType) in FeatureText_ValueObjectFamilyBases())
        {
            yield return Type(
                className,
                file: valueObjects,
                kind: "class",
                modifiers: "public abstract partial",
                baseType: baseType
            );
        }

        // 文字列 VO の基底は翻訳判定の契約を実装する（式木翻訳がインターフェイスマップで引く）
        yield return FeatureText_Implements(
            "ValueObjectStringBase",
            "IStringMatchValueObject<TSelf>"
        );
    }

    /// <summary>per-型の基底句が現行名のままである（拡張面を経由して固定ランタイムへ着地する）値オブジェクトと EditModel・Entity</summary>
    private static IEnumerable<CodeRule> FeatureText_ShimPerTypeSurfaceRules() =>
        [
            Type(
                "CustomerEntity",
                kind: "class",
                modifiers: "public partial",
                baseType: "EntityBase"
            ),
            Type(
                "CustomerEditModel",
                kind: "class",
                modifiers: "public partial",
                baseType: "EditModelBase<CustomerEditModel>"
            ),
            Type("NameValue", baseType: "ValueObjectStringBase<NameValue>"),
            Type("IsActiveValue", baseType: "ValueObjectBooleanBase<IsActiveValue>"),
        ];

    /// <summary>固定ランタイムの <c>*Core</c>（実装の置き場）の宣言</summary>
    private static IEnumerable<CodeRule> FeatureText_ShimCoreRules(string? file) =>
        [
            Type("EntityBaseCore", file: file, kind: "class", modifiers: "public abstract"),
            Type(
                "EditModelBaseCore<TSelf>",
                file: file,
                kind: "class",
                modifiers: "public abstract",
                baseType: "EditModelBaseCore"
            ),
            Type("IValueObjectCore", file: file, kind: "interface", modifiers: "public"),
            Type(
                "ValueObjectBaseCore<TSelf, TValue>",
                file: file,
                kind: "class",
                modifiers: "public abstract"
            ),
            // 系統別基底が使う共有ヘルパー
            Type("ValueObjectComparisons", file: file, kind: "class", modifiers: "public static"),
            Type(
                "ValueObjectBinaryOperations",
                file: file,
                kind: "class",
                modifiers: "public static"
            ),
        ];

    /// <summary>固定 infra の宣言（パッケージ参照モードでは 1 つも出ない）</summary>
    private static IEnumerable<CodeRule> FeatureText_ShimNoFixedInfraRules() =>
        [
            NoType("EntityBaseCore"),
            NoType("EditModelBaseCore"),
            NoType("IValueObjectCore"),
            NoType("ValueObjectBaseCore"),
            NoType("ValueObjectComparisons"),
            NoType("ValueObjectBinaryOperations"),
            NoType("IStringMatchValueObject"),
        ];

    /// <summary>Repository 契約シム 2 本（リモート面・全機能面）</summary>
    private static IEnumerable<CodeRule> FeatureText_ShimContractRules(string? file) =>
        [
            Type(
                "IRemoteRepository<TEntity, TKey>",
                file: file,
                kind: "interface",
                modifiers: "public partial",
                interfaces: ["IRemoteRepositoryCore<TEntity, TKey>"]
            ),
            Type(
                "IRepository<TEntity, TKey>",
                file: file,
                kind: "interface",
                modifiers: "public partial",
                interfaces: ["IRepositoryCore<TEntity, TKey>", "IRemoteRepository<TEntity, TKey>"]
            ),
        ];

    /// <summary>
    /// 実装側 4 枝のシム（クラス宣言と、固定ランタイムの <c>*Core</c> を継承する基底句）。コンストラクタ引数を <c>*Core</c> へ転送する本体は、
    /// 型の合う引数でしかコンパイルできない（行のコンパイルで見る）
    /// </summary>
    private static IEnumerable<CodeRule> FeatureText_ShimBackendRules(bool perFile) =>
        [
            Type(
                "SqlServerRepository<TEntity, TKey>",
                file: perFile ? "Repositories.SqlServer.g.cs" : null,
                kind: "class",
                modifiers: "public abstract partial",
                baseType: "SqlServerRepositoryCore<TEntity, TKey>"
            ),
            Type(
                "EfCoreRepository<TEntity, TKey, TContext>",
                file: perFile ? "Repositories.EntityFrameworkCore.g.cs" : null,
                kind: "class",
                modifiers: "public abstract partial",
                baseType: "EfCoreRepositoryCore<TEntity, TKey, TContext>"
            ),
            Type(
                "InMemoryRepository<TEntity, TKey>",
                file: perFile ? "Repositories.InMemory.g.cs" : null,
                kind: "class",
                modifiers: "public abstract partial",
                baseType: "InMemoryRepositoryCore<TEntity, TKey>"
            ),
            Type(
                "HttpRemoteRepository<TEntity, TKey>",
                file: perFile ? "Repositories.Http.g.cs" : null,
                kind: "class",
                modifiers: "public abstract partial",
                baseType: "HttpRemoteRepositoryCore<TEntity, TKey>"
            ),
        ];

    private static CodeRule FeatureText_ShimMapperRule(string? file) =>
        Type(
            "MapperBase<TEntity, TEditModel>",
            file: file,
            kind: "class",
            modifiers: "public abstract partial",
            baseType: "MapperBaseCore<TEntity, TEditModel>"
        );

    /// <summary>per-型の宣言句が現行名のままである（<c>*Core</c> 直付けへ落ちると、利用者が拡張面へ足したメンバーがその型にだけ届かなくなる）</summary>
    private static IEnumerable<CodeRule> FeatureText_ShimPerTypeRepositoryRules() =>
        [
            Type(
                "ICustomerRemoteRepository",
                kind: "interface",
                modifiers: "public partial",
                interfaces: ["IRemoteRepository<CustomerEntity, int>"]
            ),
            Type(
                "ICustomerRepository",
                kind: "interface",
                modifiers: "public partial",
                interfaces: ["ICustomerRemoteRepository", "IRepository<CustomerEntity, int>"]
            ),
            Type("CustomerRepository", baseType: "SqlServerRepository<CustomerEntity, int>"),
            Type(
                "EfCoreCustomerRepository",
                baseType: "EfCoreRepository<CustomerEntity, int, QuickErDbContext>"
            ),
            Type("InMemoryCustomerRepository", baseType: "InMemoryRepository<CustomerEntity, int>"),
            Type(
                "HttpCustomerRemoteRepository",
                baseType: "HttpRemoteRepository<CustomerEntity, int>"
            ),
            Type("CustomerMapper", baseType: "MapperBase<CustomerEntity, CustomerEditModel>"),
        ];

    private static IEnumerable<CodeRule> FeatureText_ShimNoBackendFixedInfraRules() =>
        [
            NoType("IRepositoryCore"),
            NoType("IRemoteRepositoryCore"),
            NoType("MapperBaseCore"),
            NoType("SqlServerRepositoryCore"),
            NoType("EfCoreRepositoryCore"),
            NoType("InMemoryRepositoryCore"),
            NoType("HttpRemoteRepositoryCore"),
        ];

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextExtensionShimRows()
    {
        var surfaces = Columns_ExtensionShimOptions();
        var backends = Columns_ExtensionShimBackendOptions();
        var splitSurfaces = FeatureText_ShimSurfaceRules(
            "Entities.g.cs",
            "EditModels.g.cs",
            "ValueObjects.g.cs"
        );

        return
        [
            FeatureTextRow(
                "code-extension-shim-inline-surfaces-and-per-type-clauses",
                ShimClass,
                "Inline_ShouldEmitExtensionSurfacesAndKeepPerTypeBaseClauses",
                Columns_ExtensionShimDiagram,
                surfaces,
                code:
                [
                    .. FeatureText_ShimSurfaceRules(null, null, null),
                    .. FeatureText_ShimPerTypeSurfaceRules(),
                    .. FeatureText_ShimCoreRules(null),
                    Type(
                        "IValueObject<TSelf>",
                        kind: "interface",
                        modifiers: "public",
                        interfaces: ["IValueObjectCore"]
                    ),
                    Type(
                        "IValueObject<TSelf, TValue>",
                        kind: "interface",
                        modifiers: "public",
                        interfaces: ["IValueObject<TSelf>"]
                    ),
                ],
                // 実装の主コンストラクタが *Core へ渡す引数は型が全部違うので、並びの入れ替えはコンパイルで止まる
                compile: true
            ),
            FeatureTextRow(
                "code-extension-shim-split-surfaces-with-per-type-files",
                ShimClass,
                "Split_ShouldPlaceExtensionSurfacesWithPerTypeCode",
                Columns_ExtensionShimDiagram,
                surfaces with
                {
                    SplitFilesByCategory = true,
                },
                code: [.. splitSurfaces, .. FeatureText_ShimCoreRules("Runtime.g.cs")],
                compile: true
            ),
            FeatureTextRow(
                "code-extension-shim-package-mode-surfaces-without-fixed-infra",
                ShimClass,
                "PackageMode_ShouldStillEmitExtensionSurfaces",
                Columns_ExtensionShimDiagram,
                surfaces with
                {
                    SplitFilesByCategory = true,
                    UseRuntimePackages = true,
                },
                code: [.. splitSurfaces, .. FeatureText_ShimNoFixedInfraRules()],
                compile: true
            ),
            FeatureTextRow(
                "code-extension-shim-layered-surfaces-in-layer-files",
                ShimClass,
                "Layered_ShouldPlaceExtensionSurfacesInDomainAndPresentation",
                Columns_ExtensionShimDiagram,
                surfaces with
                {
                    LayeredOutput = true,
                },
                code:
                [
                    Type("EntityBase", file: "Entities.g.cs", baseType: "EntityBaseCore"),
                    Type("IValueObject", file: "ValueObjects.g.cs", kind: "interface"),
                    Type(
                        "EditModelBase<TSelf>",
                        file: "EditModels.g.cs",
                        baseType: "EditModelBaseCore<TSelf>"
                    ),
                ],
                // 実装の主コンストラクタが *Core へ渡す引数は型が全部違うので、並びの入れ替えはコンパイルで止まる
                compile: true
            ),
            FeatureTextRow(
                "code-extension-shim-inline-repository-and-mapper-surfaces",
                ShimClass,
                "Inline_ShouldEmitRepositoryAndMapperExtensionSurfaces",
                Columns_ExtensionShimDiagram,
                backends,
                code:
                [
                    .. FeatureText_ShimContractRules(null),
                    FeatureText_ShimMapperRule(null),
                    .. FeatureText_ShimBackendRules(perFile: false),
                    Type(
                        "IRemoteRepositoryCore<TEntity, TKey>",
                        kind: "interface",
                        modifiers: "public"
                    ),
                    Type(
                        "IRepositoryCore<TEntity, TKey>",
                        kind: "interface",
                        modifiers: "public",
                        interfaces: ["IRemoteRepositoryCore<TEntity, TKey>"]
                    ),
                    Type(
                        "MapperBaseCore<TEntity, TEditModel>",
                        kind: "class",
                        modifiers: "public abstract"
                    ),
                    .. FeatureText_ShimPerTypeRepositoryRules(),
                ],
                // 実装の主コンストラクタが *Core へ渡す引数は型が全部違うので、並びの入れ替えはコンパイルで止まる
                compile: true
            ),
            FeatureTextRow(
                "code-extension-shim-split-repository-and-mapper-surfaces",
                ShimClass,
                "Split_ShouldPlaceRepositoryAndMapperShimsWithPerTypeCode",
                Columns_ExtensionShimDiagram,
                backends with
                {
                    SplitFilesByCategory = true,
                },
                code:
                [
                    .. FeatureText_ShimContractRules("Repositories.g.cs"),
                    FeatureText_ShimMapperRule("Mappers.g.cs"),
                    .. FeatureText_ShimBackendRules(perFile: true),
                    Type(
                        "IRepositoryCore<TEntity, TKey>",
                        file: "Runtime.g.cs",
                        kind: "interface",
                        modifiers: "public"
                    ),
                    Type(
                        "MapperBaseCore<TEntity, TEditModel>",
                        file: "Runtime.g.cs",
                        kind: "class",
                        modifiers: "public abstract"
                    ),
                ],
                // 実装の主コンストラクタが *Core へ渡す引数は型が全部違うので、並びの入れ替えはコンパイルで止まる
                compile: true
            ),
            FeatureTextRow(
                "code-extension-shim-package-mode-repository-and-mapper-surfaces",
                ShimClass,
                "PackageMode_ShouldStillEmitRepositoryAndMapperShims",
                Columns_ExtensionShimDiagram,
                backends with
                {
                    SplitFilesByCategory = true,
                    UseRuntimePackages = true,
                },
                code:
                [
                    .. FeatureText_ShimContractRules("Repositories.g.cs"),
                    FeatureText_ShimMapperRule("Mappers.g.cs"),
                    .. FeatureText_ShimBackendRules(perFile: true),
                    .. FeatureText_ShimNoBackendFixedInfraRules(),
                    .. FeatureText_ShimPerTypeRepositoryRules(),
                ],
                compile: true
            ),
            FeatureTextRow(
                "code-extension-shim-multi-dialect-one-shim-per-engine",
                ShimClass,
                "MultiDialect_ShouldEmitOneDialectShimPerEngine",
                Columns_ExtensionShimDiagram,
                surfaces with
                {
                    GenerateValueObjects = false,
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    SplitFilesByCategory = true,
                },
                code:
                [
                    Type(
                        "SqlServerRepository<TEntity, TKey>",
                        file: "Repositories.SqlServer.g.cs",
                        kind: "class",
                        modifiers: "public abstract partial",
                        baseType: "SqlServerRepositoryCore<TEntity, TKey>"
                    ),
                    Type(
                        "SqliteRepository<TEntity, TKey>",
                        file: "Repositories.Sqlite.g.cs",
                        kind: "class",
                        modifiers: "public abstract partial",
                        baseType: "SqliteRepositoryCore<TEntity, TKey>"
                    ),
                    // 契約シムは方言に依らず契約ファイルに 1 組だけ（別ファイルには出ない＝全体で 1 個）
                    .. FeatureText_ShimContractRules("Repositories.g.cs"),
                ],
                compile: true
            ),
            FeatureTextRow(
                "code-extension-shim-layered-repository-and-mapper-surfaces",
                ShimClass,
                "Layered_ShouldPlaceRepositoryAndMapperShimsByLayer",
                Columns_ExtensionShimDiagram,
                backends with
                {
                    LayeredOutput = true,
                },
                code:
                [
                    Type("IRepository<TEntity, TKey>", file: "Repositories.g.cs"),
                    Type("SqlServerRepository<TEntity, TKey>", file: "Repositories.SqlServer.g.cs"),
                    Type("MapperBase<TEntity, TEditModel>", file: "Mappers.g.cs"),
                ],
                // 実装の主コンストラクタが *Core へ渡す引数は型が全部違うので、並びの入れ替えはコンパイルで止まる
                compile: true
            ),
        ];
    }

    // ===== DbDefinitionMetadataTests =====

    private const string DbMetaClass = "DbDefinitionMetadataTests";

    /// <summary>DbDefinitionMetadataTests.BuildDiagram（説明つきの customers・解析できない自由記述型の extra 列）。<paramref name="rowVersion"/> なら行バージョン列を足す</summary>
    private static ErDiagram FeatureText_DbMetaDiagram(bool rowVersion = false)
    {
        var entity = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "customers",
            Description = "顧客マスタ",
            Columns =
            {
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "customer_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                    Description = "顧客名",
                },
                // 型カタログが解析できない自由記述型（属性は省略されるべき）
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "extra",
                    DataType = "sql_variant",
                    IsNullable = true,
                },
            },
        };

        if (rowVersion)
        {
            entity.Columns.Add(
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                }
            );
        }

        return new ErDiagram { Entities = { entity } };
    }

    /// <summary>DbDefinitionMetadataTests.BuildSqliteDialectDiagram（5 方言で解釈の割れない型だけの sqlite 方言の図）</summary>
    private static ErDiagram FeatureText_DbMetaSqliteDialectDiagram() =>
        new()
        {
            TargetDbms = DialectNames.Sqlite,
            Entities =
            {
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    {
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "balance",
                            DataType = "decimal(10,2)",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    /// <summary>属性の定義 2 本と、CustomerEntity への付与（説明つきの表・int32 の PK・説明つきの string(50)・自由記述型には付けない）</summary>
    private static IEnumerable<CodeRule> FeatureText_DbMetaRules() =>
        [
            Type(
                "DbColumnMetaAttribute",
                kind: "class",
                modifiers: "public sealed",
                baseType: "Attribute"
            ),
            Type(
                "DbTableMetaAttribute",
                kind: "class",
                modifiers: "public sealed",
                baseType: "Attribute"
            ),
            Attribute("CustomerEntity", null, "DbTableMeta(Description = \"顧客マスタ\")"),
            // 説明の無い PK 列は Description の named 引数を持たない（引数の完全一致）
            Attribute("CustomerEntity", "CustomerId", "DbColumnMeta(\"int32\")"),
            Attribute(
                "CustomerEntity",
                "Name",
                "DbColumnMeta(\"string(50)\", Description = \"顧客名\")"
            ),
            NoAttribute("CustomerEntity", "Extra", "DbColumnMeta"),
            PropertiesWithAttribute("DbColumnMeta", 2, "CustomerEntity"),
        ];

    private static ScenarioRow<CodegenCase> FeatureText_DbMetaTargetRow(
        string id,
        string description,
        params string[] dialects
    ) =>
        FeatureTextRow(
            id,
            DbMetaClass,
            description,
            () => FeatureText_DbMetaDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = dialects,
            },
            code: [.. FeatureText_DbMetaRules()]
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextDbMetaRows()
    {
        var plain = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = false,
            GenerateEfCoreRepositories = false,
        };

        return
        [
            FeatureTextRow(
                "code-db-meta-attributes-on-with-descriptions",
                DbMetaClass,
                "IncludeDataAnnotations_On_EmitsMetaAttributes / EmptyDescription_OmitsDescriptionArgument / UnparseableType_OmitsColumnMeta",
                () => FeatureText_DbMetaDiagram(),
                plain,
                code:
                [
                    .. FeatureText_DbMetaRules(),
                    // 型カタログが解析できない自由記述型（sql_variant）の列もプロパティは生成され、属性だけが付かない
                    Member(
                        "CustomerEntity",
                        "Extra",
                        "string? Extra { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            FeatureTextRow(
                "code-db-meta-attributes-off-emits-none",
                DbMetaClass,
                "IncludeDataAnnotations_Off_OmitsMetaAttributes",
                () => FeatureText_DbMetaDiagram(),
                plain with
                {
                    IncludeDataAnnotations = false,
                },
                code:
                [
                    NoType("DbColumnMetaAttribute"),
                    NoType("DbTableMetaAttribute"),
                    PropertiesWithAttribute("DbColumnMeta", 0),
                    NoAttribute("CustomerEntity", null, "DbTableMeta"),
                ]
            ),
            FeatureTextRow(
                "code-db-meta-row-version-column-has-no-column-meta",
                DbMetaClass,
                "RowVersionColumn_OmitsColumnMeta",
                () => FeatureText_DbMetaDiagram(rowVersion: true),
                plain,
                code:
                [
                    Member("CustomerEntity", "RowVer", "byte[] RowVer { get; set; }"),
                    NoAttribute("CustomerEntity", "RowVer", "DbColumnMeta"),
                    // ほかの列には付く（付与そのものが無効なのではない）。省くのは意図的な判断で、型カタログが rowversion を解析できないからではない（下の解析の判定）
                    PropertiesWithAttribute("DbColumnMeta", 2, "CustomerEntity"),
                ],
                parsed:
                [
                    new ParsedCheck(
                        "rowversion-token-is-formattable",
                        _ =>
                            new QuickER.Provider.SqlServer.SqlServerTypeCatalog().TryParse(
                                "rowversion",
                                out var canonical
                            )
                                ? QuickER.Provider.CanonicalTypeToken.Format(canonical)
                                == "rowversion"
                                    ? []
                                    : ["rowversion のトークンが rowversion でない"]
                                : ["型カタログが rowversion を解析できない"]
                    ),
                ]
            ),
            FeatureText_DbMetaTargetRow(
                "code-db-meta-identical-sqlserver-only",
                "Metadata_IsIdentical_AcrossTargetDatabases（sqlserver 単独）",
                DialectNames.SqlServer
            ),
            FeatureText_DbMetaTargetRow(
                "code-db-meta-identical-sqlite-only",
                "Metadata_IsIdentical_AcrossTargetDatabases（sqlite 単独）",
                DialectNames.Sqlite
            ),
            FeatureText_DbMetaTargetRow(
                "code-db-meta-identical-multi-target",
                "Metadata_IsIdentical_AcrossTargetDatabases（マルチターゲット）",
                DialectNames.SqlServer,
                DialectNames.Sqlite
            ),
            FeatureText_DbMetaSqliteDialectRow(
                "code-db-meta-sqlite-dialect-single",
                "Metadata_SurvivesSqlColumnTypeSupplement_WhenPrimaryDialectIsSqlite（sqlite 単独）",
                DialectNames.Sqlite
            ),
            FeatureText_DbMetaSqliteDialectRow(
                "code-db-meta-sqlite-dialect-multi-supplement",
                "Metadata_SurvivesSqlColumnTypeSupplement_WhenPrimaryDialectIsSqlite（sqlite ＋ sqlserver の補完）",
                DialectNames.Sqlite,
                DialectNames.SqlServer
            ),
        ];
    }

    /// <summary>図の方言が sqlite のとき、単一方言でも補完が走るマルチターゲットでも、全列の <c>[DbColumnMeta]</c> が同じ</summary>
    private static ScenarioRow<CodegenCase> FeatureText_DbMetaSqliteDialectRow(
        string id,
        string description,
        params string[] dialects
    ) =>
        FeatureTextRow(
            id,
            DbMetaClass,
            description,
            FeatureText_DbMetaSqliteDialectDiagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateEfCoreRepositories = false,
                RepositoryDialects = dialects,
            },
            code:
            [
                Attribute("CustomerEntity", "CustomerId", "DbColumnMeta(\"int32\")"),
                Attribute("CustomerEntity", "Name", "DbColumnMeta(\"string(50)\")"),
                Attribute("CustomerEntity", "Balance", "DbColumnMeta(\"decimal(10,2)\")"),
                PropertiesWithAttribute("DbColumnMeta", 3, "CustomerEntity"),
            ],
            typeDialect: DialectNames.Sqlite
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextRows() =>
        [
            .. FeatureTextRemoteRows(),
            .. FeatureTextIncludeRows(),
            .. FeatureTextIncludeGetByIdRows(),
            .. FeatureTextApiDocsRows(),
            .. FeatureTextVoForeignKeyRows(),
            .. FeatureTextXmlDocRows(),
            .. FeatureTextExtensionShimRows(),
            .. FeatureTextDbMetaRows(),
            .. FeatureTextRemoteRestRows(),
            .. FeatureTextApiDocsRestRows(),
            .. FeatureTextVoDslRows(),
        ];

    // ===== RemoteServiceGenerationTests：残りの 5 本の構造の部分 =====

    private const string RemoteClientExtensions =
        "GeneratedHttpRemoteRepositoryServiceCollectionExtensions";

    /// <summary>リモートサービスの標準の入力（Order に名前付きクエリ 2 件）で、出力名を指定するもの</summary>
    private static CodeGenerationOptions FeatureText_RemoteOptions(string? outputFileName = null) =>
        new()
        {
            RootNamespace = "Test.Ns",
            OutputFileName = outputFileName ?? "QuickEREntities.g.cs",
            GenerateRepositories = true,
            GenerateRemoteServices = true,
        };

    /// <summary><c>AddGeneratedHttpRemoteRepositories</c> の 4 つの多重定義（非 keyed と keyed ×（baseAddress・HttpClient ファクトリ））のシグネチャ</summary>
    private static IEnumerable<CodeRule> FeatureText_HttpRegistrationSignatures() =>
        new[]
        {
            "string baseAddress",
            "Func<IServiceProvider, HttpClient> httpClientFactory",
            "object? serviceKey, string baseAddress",
            "object? serviceKey, Func<IServiceProvider, HttpClient> httpClientFactory",
        }.Select(parameters =>
            Member(
                RemoteClientExtensions,
                "AddGeneratedHttpRemoteRepositories",
                $"IServiceCollection AddGeneratedHttpRemoteRepositories(this IServiceCollection services, {parameters})",
                modifiers: "public static"
            )
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextRemoteRestRows() =>
        [
            FeatureTextRow(
                "code-remote-service-main-client-and-server-engine-declarations",
                RemoteClass,
                "Generate_RemoteServices_EmitsClientInMainAndServerInSeparateFile（型・メンバーの宣言・using・呼び出しの構造の部分）",
                Columns_RemoteServiceDiagram,
                FeatureText_RemoteOptions("Shop.g.cs"),
                code:
                [
                    // 本体：リモート面の契約とクライアント固定 infra・per-entity 実装・DI
                    Type(
                        "IOrderRemoteRepository",
                        file: "Shop.g.cs",
                        kind: "interface",
                        modifiers: "public partial"
                    ),
                    Type(
                        "RemoteJson",
                        file: "Shop.g.cs",
                        kind: "class",
                        modifiers: "public static"
                    ),
                    Type(
                        "RemoteRepositoryException",
                        file: "Shop.g.cs",
                        kind: "class",
                        modifiers: "public sealed"
                    ),
                    Type(
                        "HttpRemoteRepository<TEntity, TKey>",
                        file: "Shop.g.cs",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    Type(
                        "HttpOrderRemoteRepository",
                        file: "Shop.g.cs",
                        kind: "class",
                        modifiers: "public sealed partial"
                    ),
                    Member(
                        "HttpOrderRemoteRepository",
                        ".ctor",
                        "HttpOrderRemoteRepository(HttpClient httpClient)"
                    ),
                    Member(RemoteClientExtensions, "AddGeneratedHttpRemoteRepositories", count: 4),
                    Calls(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "AddClient<IOrderRemoteRepository>",
                        count: 2
                    ),
                    Creates(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "HttpOrderRemoteRepository",
                        ["client"],
                        count: 2
                    ),
                    // サーバーファイル：固定エンジン（スキーマ非依存）＋エンドポイントのマッピング（スキーマ依存）
                    Type(
                        "RemoteServerEngine",
                        file: "Shop.RemoteServer.g.cs",
                        kind: "class",
                        modifiers: "internal static"
                    ),
                    Type(
                        "GeneratedRemoteEndpoints",
                        file: "Shop.RemoteServer.g.cs",
                        kind: "class",
                        modifiers: "public static partial"
                    ),
                    Member(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "RouteGroupBuilder MapGeneratedRemoteEndpoints(this IEndpointRouteBuilder endpoints, RemoteAccess access, string prefix = \"/quicker\", bool exposeErrorDetails = false)",
                        modifiers: "public static"
                    ),
                    ReferencesSymbol(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "RemotePaths.DefaultPrefix"
                    ),
                    // per-entity のエンドポイントは固定エンジンの汎用マッピングを修飾付きで呼ぶ
                    Calls(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "RemoteServerEngine.MapCrud<OrderEntity, int, IOrderRemoteRepository>",
                        count: 1
                    ),
                    // 競合は 409・リクエスト解釈の失敗は 400・Kestrel の BadHttpRequestException はステータス素通し
                    CallArguments(
                        "RemoteServerEngine",
                        "ClassifyFailuresAsync",
                        "WriteErrorAsync",
                        ["context", "409", "\"SaveConflict\"", "Message", "ex"]
                    ),
                    CallArguments(
                        "RemoteServerEngine",
                        "ClassifyFailuresAsync",
                        "WriteErrorAsync",
                        ["context", "400", "\"BadRequest\"", "Message"]
                    ),
                    CallArguments(
                        "RemoteServerEngine",
                        "ClassifyFailuresAsync",
                        "WriteErrorAsync",
                        ["context", "StatusCode", "\"BadRequest\"", "Message"]
                    ),
                    ReferencesSymbol(
                        "RemoteServerEngine",
                        "ClassifyFailuresAsync",
                        "BadHttpRequestException.StatusCode"
                    ),
                    Type(
                        "RemoteBadRequestException",
                        file: "Shop.RemoteServer.g.cs",
                        kind: "class",
                        modifiers: "internal sealed",
                        baseType: "Exception"
                    ),
                    // 500 の詳細公開は実行時引数・OnServerError はラッパー RaiseServerError をメタデータへ載せてエンジンが呼ぶ
                    Creates(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "RemoteErrorDetailPolicy",
                        ["exposeErrorDetails", "?"]
                    ),
                    ReferencesSymbol(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "GeneratedRemoteEndpoints.RaiseServerError"
                    ),
                    Type(
                        "RemoteErrorDetailPolicy",
                        file: "Shop.RemoteServer.g.cs",
                        kind: "class",
                        modifiers: "internal sealed"
                    ),
                    Member(
                        "RemoteErrorDetailPolicy",
                        "OnServerError",
                        "Action<HttpContext, Exception>? OnServerError { get; }",
                        modifiers: "public"
                    ),
                    Calls("RemoteServerEngine", "WriteServerErrorAsync", "Invoke", count: 1),
                    Member(
                        "GeneratedRemoteEndpoints",
                        "RaiseServerError",
                        "void RaiseServerError(HttpContext context, Exception ex)",
                        modifiers: "private static"
                    ),
                    CallArguments(
                        "GeneratedRemoteEndpoints",
                        "RaiseServerError",
                        "RemoteServerEngine.LogServerError",
                        ["context", "hookError"]
                    ),
                    Member(
                        "GeneratedRemoteEndpoints",
                        "OnServerError",
                        "void OnServerError(HttpContext context, Exception ex)",
                        modifiers: "static partial"
                    ),
                    // サーバーファイルは ASP.NET Core の using を持ち、本体は持たない
                    Using("Shop.RemoteServer.g.cs", "Microsoft.AspNetCore.Builder"),
                    NoUsingUnder("Shop.g.cs", "Microsoft.AspNetCore"),
                    NoReferencesTo("Shop.g.cs", "Microsoft.AspNetCore"),
                ]
            ),
            FeatureTextRow(
                "code-remote-service-named-queries-forward-client-and-server",
                RemoteClass,
                "Generate_RemoteServices_ForwardsNamedQueries（宣言・呼び出しの構造の部分）",
                Columns_RemoteServiceDiagram,
                FeatureText_RemoteOptions(),
                code:
                [
                    // クライアント：シグネチャは契約と同一・本体は InvokeAsync / InvokeNullableAsync への転送
                    Member(
                        "HttpOrderRemoteRepository",
                        "GetByCustomerAsync",
                        "Task<IReadOnlyList<OrderEntity>> GetByCustomerAsync(int customerId, int take, int skip = 0, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    CallArguments(
                        "HttpOrderRemoteRepository",
                        "GetByCustomerAsync",
                        "InvokeAsync<IReadOnlyList<OrderEntity>>",
                        ["\"GetByCustomer\"", "?", "cancellationToken"]
                    ),
                    CallArguments(
                        "HttpOrderRemoteRepository",
                        "FindTopAsync",
                        "InvokeNullableAsync<OrderEntity?>",
                        ["\"FindTop\"", "null", "cancellationToken"]
                    ),
                    // サーバー：リクエストレコード（PascalCase プロパティ）＋リモート面への委譲ハンドラ（パラメータなしはレコードも本文の読み取りも無い）
                    Type("Order_GetByCustomerRequest", kind: "record", modifiers: "private sealed"),
                    Member(
                        "Order_GetByCustomerRequest",
                        "CustomerId",
                        "int CustomerId { get; init; }"
                    ),
                    Member("Order_GetByCustomerRequest", "Take", "int Take { get; init; }"),
                    Member("Order_GetByCustomerRequest", "Skip", "int Skip { get; init; }"),
                    CallArguments(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "MapPost",
                        ["\"Order/GetByCustomer\"", "?"]
                    ),
                    CallArguments(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "MapPost",
                        ["\"Order/FindTop\"", "?"]
                    ),
                    NoType("Order_FindTopRequest"),
                    // ページング引数はリクエスト解釈の境界で検証してから渡す
                    CallArguments(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "RemoteServerEngine.ValidatedPaging",
                        ["Take", "Skip"]
                    ),
                    CallArguments(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "GetByCustomerAsync",
                        ["CustomerId", "Take", "Skip", "RequestAborted"],
                        receiver: "repository"
                    ),
                ]
            ),
            FeatureTextRow(
                "code-remote-service-uniqueness-check-is-mapped-by-generic-crud",
                RemoteClass,
                "Generate_RemoteServices_MapsUniquenessCheckFromTheGenericCrudMapping（宣言・呼び出しの構造の部分）",
                Columns_RemoteServiceDiagram,
                FeatureText_RemoteOptions(),
                code:
                [
                    // 重複事前チェックは固定エンジンの汎用マッピング（MapCrud）が張り、per-entity にはレコードも経路も残らない
                    Calls("RemoteServerEngine", "MapCrud", "CheckUniquenessAsync", count: 1),
                    NoType("Order_CheckUniquenessRequest"),
                    NoType("OrderCheckUniquenessRequest"),
                    Calls(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "CheckUniquenessAsync",
                        count: 0
                    ),
                    CallArguments(
                        "GeneratedRemoteEndpoints",
                        "MapOrderEndpoints",
                        "MapPost",
                        ["\"Order/CheckUniqueness\"", "?"],
                        count: 0
                    ),
                ]
            ),
            FeatureTextRow(
                "code-remote-service-http-registration-keyed-and-plain-overloads",
                RemoteClass,
                "Generate_RemoteServices_EmitsKeyedHttpRegistrationOverloads（宣言・呼び出しの構造の部分）",
                Columns_RemoteServiceDiagram,
                FeatureText_RemoteOptions(),
                code:
                [
                    .. FeatureText_HttpRegistrationSignatures(),
                    // keyed 版：共有 HttpClient は同じキーで登録し、per-entity のリモート面は keyed scoped
                    Calls(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "AddKeyedSingleton",
                        count: 1
                    ),
                    Calls(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "GetRequiredKeyedService<OwnedHttpClient>",
                        count: 1
                    ),
                    CallArguments(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "AddKeyedScoped",
                        ["serviceKey", "?"],
                        receiver: "services",
                        count: 1
                    ),
                    // 非 keyed 版：従来どおりキー無しで登録する
                    CallArguments(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "AddScoped",
                        ["?"],
                        receiver: "services",
                        count: 1
                    ),
                    CallArguments(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "AddSingleton",
                        ["?"],
                        receiver: "services",
                        count: 1
                    ),
                    Calls(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "AddClient<IOrderRemoteRepository>",
                        count: 2
                    ),
                    Creates(
                        RemoteClientExtensions,
                        "AddGeneratedHttpRemoteRepositories",
                        "OwnedHttpClient",
                        count: 2
                    ),
                ]
            ),
            FeatureTextRow(
                "code-remote-service-routes-shared-through-remote-paths",
                RemoteClass,
                "Generate_RemoteServices_SharesRoutesThroughRemotePaths（宣言・参照の構造の部分）",
                Columns_RemoteServiceDiagram,
                FeatureText_RemoteOptions(),
                code:
                [
                    Type(
                        "RemotePaths",
                        file: "QuickEREntities.g.cs",
                        kind: "class",
                        modifiers: "public static"
                    ),
                    Member(
                        "RemotePaths",
                        "DefaultPrefix",
                        "string DefaultPrefix",
                        modifiers: "public const"
                    ),
                    Member(
                        "RemotePaths",
                        "HealthRoute",
                        "string HealthRoute",
                        modifiers: "public const"
                    ),
                    Member(
                        "HttpRemoteRepositoryCore<TEntity, TKey>",
                        "PingAsync",
                        "Task<bool> PingAsync(CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    ReferencesSymbol(
                        "HttpRemoteRepositoryCore<TEntity, TKey>",
                        "PingAsync",
                        "RemotePaths.HealthRoute"
                    ),
                    ReferencesSymbol(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "RemotePaths.DefaultPrefix"
                    ),
                    ReferencesSymbol(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "RemotePaths.HealthRoute"
                    ),
                ]
            ),
            FeatureTextRow(
                "exec-remote-paths-constants",
                RemoteClass,
                "Generate_RemoteServices_SharesRoutesThroughRemotePaths（定数の値。手書き実装の要る名前付きクエリは外した図）",
                () =>
                {
                    var diagram = Columns_RemoteServiceDiagram();
                    diagram.Queries.Clear();

                    return diagram;
                },
                FeatureText_RemoteOptions(),
                exec:
                [
                    new ExecCheck(
                        "remote-paths-constants",
                        assembly =>
                        {
                            var paths = ProbeKit.Type(assembly, "RemotePaths");
                            var problems = new List<string>();

                            if (ProbeKit.GetStaticField(paths, "DefaultPrefix") is not "/quicker")
                            {
                                problems.Add("DefaultPrefix が /quicker でない");
                            }

                            if (ProbeKit.GetStaticField(paths, "HealthRoute") is not "health")
                            {
                                problems.Add("HealthRoute が health でない");
                            }

                            return problems;
                        }
                    ),
                ]
            ),
        ];

    // ===== ApiReferenceDocTests：残りの 3 本の構造の部分 =====

    /// <summary>ApiReferenceDocTests.BuildDiagram（customers 1:N orders。表名・説明は呼び出し側が差し替える）</summary>
    private static ErDiagram FeatureText_ApiDocsRawCellsDiagram()
    {
        var diagram = CustomerOrderDiagram();
        diagram.Entities[0].Description = "say \"hi\" | pipe";
        diagram.Entities[1].TableName = "or\"ders";
        diagram.Entities[0].Columns[1].Description = "col \"desc\" | pipe";

        return diagram;
    }

    private const string ApiDocsRepositoryJapanese = "QuickEREntities.ja.g.md";

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextApiDocsRestRows() =>
        [
            FeatureTextRow(
                "markdown-api-docs-cells-use-raw-values",
                ApiDocsClass,
                "Markdown_Cells_UseRawValuesWithMarkdownEscaping",
                FeatureText_ApiDocsRawCellsDiagram,
                new CodeGenerationOptions { GenerateApiDocs = true },
                markdown:
                [
                    // テーブル名は生のまま（C# リテラルエスケープの \" が読者に見えない）
                    MarkdownRule.ColumnContains(ApiDocsEnglish, "Table", ["customers", "or\"ders"]),
                    // 説明は生の値で、Markdown の安全化は | のエスケープだけ（\" などのエスケープが紛れ込まない）
                    MarkdownRule.Cell(
                        ApiDocsEnglish,
                        "Class",
                        "CustomerEntity",
                        "Description",
                        "say \"hi\" | pipe"
                    ),
                    MarkdownRule.Cell(
                        ApiDocsEnglish,
                        "Property",
                        "Name",
                        "Description",
                        "col \"desc\" | pipe"
                    ),
                    MarkdownRule.EscapedCharactersInCells(ApiDocsEnglish, ["|"]),
                ]
            ),
            FeatureTextRow(
                "markdown-api-docs-japanese-only-same-as-japanese-of-both",
                ApiDocsClass,
                "JapaneseOnly_ContentMatchesJapaneseOfBoth",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Japanese,
                    GenerateRepositories = true,
                },
                markdown:
                [
                    MarkdownRule.Heading(ApiDocsJapanese, 2, "データアクセス API"),
                    MarkdownRule.SameContentAs(
                        ApiDocsJapanese,
                        () =>
                            CodegenRunner.Generate(
                                CustomerOrderDiagram(),
                                DialectNames.SqlServer,
                                new CodeGenerationOptions
                                {
                                    GenerateApiDocs = true,
                                    ApiDocsLanguage = ApiDocsLanguage.Both,
                                    GenerateRepositories = true,
                                }
                            ),
                        ApiDocsJapanese
                    ),
                ]
            ),
            FeatureTextRow(
                "markdown-api-docs-japanese-headings-and-code",
                ApiDocsClass,
                "JapaneseDoc_ContainsJapaneseHeadingsAndDi（見出し・コードの呼び出しの構造の部分）",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                    GenerateRepositories = true,
                },
                markdown:
                [
                    MarkdownRule.Heading(ApiDocsJapanese, 2, "データアクセス API"),
                    MarkdownRule.Heading(ApiDocsJapanese, 2, "使い方"),
                    // 拡張メソッド名（コード）は言語非依存で共通に出る
                    MarkdownRule.CodeInvokes(ApiDocsJapanese, "AddGeneratedSqlServerRepositories"),
                    // 英語の見出しは日本語版には出ない
                    MarkdownRule.Heading(ApiDocsJapanese, 2, "Data access API", present: false),
                ]
            ),
        ];

    // ===== ValueObjectForeignKeyUnificationTests：DSL 条件式 =====

    /// <summary>ValueObjectForeignKeyUnificationTests.DSL条件式も親のVO型でCreateする の図（FK 列名 ≠ 参照先 PK 列名の図に、FK 列で絞るクエリを足す）</summary>
    private static ErDiagram FeatureText_VoDslDiagram()
    {
        var diagram = Columns_VoMismatchedNameDiagram();
        var orders = diagram.Entities.First(entity => entity.TableName == "orders");

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = Guid.NewGuid(),
                EntityId = orders.Id,
                Name = "GetByShipCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "shipCustomerId", Type = "int32" },
                },
                Condition = "ship_customer_id = @shipCustomerId",
            }
        );

        return diagram;
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextVoDslRows() =>
        [
            FeatureTextRow(
                "code-vo-foreign-key-dsl-condition-creates-parent-type",
                VoForeignKeyClass,
                "DSL条件式も親のVO型でCreateする",
                FeatureText_VoDslDiagram,
                Columns_VoOptions(),
                code:
                [
                    CallArguments(
                        "OrderRepository",
                        "GetByShipCustomerAsync",
                        "ValueObjectBaseCore<CustomerIdValue, int>.Create",
                        ["shipCustomerId"]
                    ),
                ],
                compile: true
            ),
        ];

    // ===== IncludeGraphGenerationTests：GetByIdAsync の宣言と SQLite の実行 =====

    /// <summary>
    /// <c>GetByIdAsync</c>（<c>SqlQuery</c> 版・<c>Include</c> を挟んだ <c>IncludableSqlQuery</c> 版・葉のエンティティ）を、生成物の型を静的に使って SQLite の
    /// Repository の上で動かす検証コード（<paramref name="valueObjects"/> が true なら主キーは値オブジェクト）。問題を 1 本の文字列にして返す（空なら問題なし）
    /// </summary>
    private static GeneratedFile FeatureText_GetByIdProbeSource(bool valueObjects)
    {
        string Key(string entity, int value) =>
            valueObjects ? $"{entity}IdValue.Create({value})" : value.ToString();

        return new GeneratedFile
        {
            FileName = "GetByIdProbe.cs",
            Content = $$"""
                using System.Collections.Generic;
                using System.Threading.Tasks;

                namespace Test.Ns;

                public static class GetByIdProbe
                {
                    public static async Task<string> RunAsync(
                        IRootRepository roots,
                        IMidRepository mids,
                        ILeafRepository leaves
                    )
                    {
                        await roots.InsertAsync(new RootEntity { RootId = {{Key("Root", 1)}} });
                        await roots.InsertAsync(new RootEntity { RootId = {{Key("Root", 2)}} });
                        await mids.InsertAsync(new MidEntity { MidId = {{Key(
                    "Mid",
                    10
                )}}, RootId = {{Key("Root", 1)}} });
                        await leaves.InsertAsync(new LeafEntity { LeafId = {{Key(
                    "Leaf",
                    100
                )}}, MidId = {{Key("Mid", 10)}} });
                        await leaves.InsertAsync(new LeafEntity { LeafId = {{Key(
                    "Leaf",
                    101
                )}}, MidId = {{Key("Mid", 10)}} });

                        var problems = new List<string>();

                        // SqlQuery 版：主キーが一致する行だけが返り、無い主キーは null
                        var found = await roots.Query().GetByIdAsync({{Key("Root", 2)}});

                        if (found is null || !found.RootId.Equals({{Key("Root", 2)}}))
                        {
                            problems.Add("SqlQuery 版の GetByIdAsync が主キー 2 の行を返さない");
                        }

                        if (await roots.Query().GetByIdAsync({{Key("Root", 99)}}) is not null)
                        {
                            problems.Add("存在しない主キーが null にならない");
                        }

                        // IncludableSqlQuery 版：Include の指定を保ったまま主キーで 1 行に絞る
                        var included = await roots.Query().Include(root => root.Mids).GetByIdAsync({{Key(
                    "Root",
                    1
                )}});

                        if (included is null || included.Mids.Count != 1)
                        {
                            problems.Add("IncludableSqlQuery 版の GetByIdAsync が Include した子を載せた行を返さない");
                        }

                        // 葉のエンティティ：キー列で絞る（別の葉を返さない）
                        var leaf = await leaves.Query().GetByIdAsync({{Key("Leaf", 101)}});

                        if (leaf is null || !leaf.LeafId.Equals({{Key("Leaf", 101)}}))
                        {
                            problems.Add("葉の GetByIdAsync が主キー 101 の行を返さない");
                        }

                        return string.Join(" / ", problems);
                    }
                }
                """,
        };
    }

    /// <summary>連鎖の図を SQLite で動かして、<see cref="FeatureText_GetByIdProbeSource"/> の検証コードを実行する</summary>
    private static ExecCheck FeatureText_SqliteGetById() =>
        new(
            "sqlite-get-by-id",
            assembly =>
                ProbeKit.WithSqlite(
                    assembly,
                    Columns_IncludeGraphChainDiagram(),
                    async (provider, _) =>
                    {
                        var result = (string)
                            ProbeKit.Await(
                                ProbeKit.CallStatic(
                                    ProbeKit.Type(assembly, "GetByIdProbe"),
                                    "RunAsync",
                                    provider.GetRequiredService(
                                        ProbeKit.Type(assembly, "IRootRepository")
                                    ),
                                    provider.GetRequiredService(
                                        ProbeKit.Type(assembly, "IMidRepository")
                                    ),
                                    provider.GetRequiredService(
                                        ProbeKit.Type(assembly, "ILeafRepository")
                                    )
                                )
                            )!;

                        return result.Length == 0 ? [] : [result];
                    }
                )
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> FeatureTextIncludeGetByIdRows()
    {
        var repository = Columns_IncludeGraphOptions();
        var sqlite = repository with { RepositoryDialects = [DialectNames.Sqlite] };

        return
        [
            FeatureTextRow(
                "code-include-graph-get-by-id-emits-both-overloads",
                IncludeClass,
                "GetById_EmitsBothOverloads（SqlQuery 版と IncludableSqlQuery 版の宣言。本体は行 exec-include-graph-get-by-id-returns-matching-row-or-null が SQLite で実行する）",
                Columns_IncludeGraphChainDiagram,
                repository,
                code:
                [
                    FeatureText_GetByIdMember("RootEntity", "int", includable: false),
                    FeatureText_GetByIdMember("RootEntity", "int", includable: true),
                ]
            ),
            FeatureTextRow(
                "code-include-graph-get-by-id-uses-contract-key-type",
                IncludeClass,
                "GetById_UsesContractKeyType（値オブジェクト有効：契約が受けるキー型と同じ VO 型を糖衣も受ける）",
                Columns_IncludeGraphChainDiagram,
                repository with
                {
                    GenerateValueObjects = true,
                },
                code:
                [
                    Type(
                        "IRootRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRepository<RootEntity, RootIdValue>"]
                    ),
                    FeatureText_GetByIdMember("RootEntity", "RootIdValue", includable: false),
                    FeatureText_GetByIdMember("RootEntity", "RootIdValue", includable: true),
                ]
            ),
            FeatureTextRow(
                "code-include-graph-get-by-id-is-emitted-for-leaf-entity",
                IncludeClass,
                "GetById_IsEmittedForLeafEntity（葉のエンティティにも GetByIdAsync の宣言が出る。キー列で絞る本体は行 exec-include-graph-get-by-id-returns-matching-row-or-null が SQLite で実行する）",
                Columns_IncludeGraphChainDiagram,
                repository,
                code:
                [
                    FeatureText_GetByIdMember("LeafEntity", "int", includable: false),
                    FeatureText_GetByIdMember("LeafEntity", "int", includable: true),
                ]
            ),
            FeatureTextRow(
                "exec-include-graph-get-by-id-returns-matching-row-or-null",
                IncludeClass,
                "GetById_EmitsBothOverloads / GetById_IsEmittedForLeafEntity（SQLite で実行：主キーに一致する行を返し無ければ null・Include を保つ・葉もキー列で絞る）",
                Columns_IncludeGraphChainDiagram,
                sqlite,
                typeDialect: DialectNames.Sqlite,
                extraSources: [FeatureText_GetByIdProbeSource(valueObjects: false)],
                exec: [FeatureText_SqliteGetById()]
            ),
            FeatureTextRow(
                "exec-include-graph-get-by-id-with-value-object-key",
                IncludeClass,
                "GetById_UsesContractKeyType（SQLite で実行：値オブジェクトの主キーでも GetByIdAsync が行を返す）",
                Columns_IncludeGraphChainDiagram,
                sqlite with
                {
                    GenerateValueObjects = true,
                },
                typeDialect: DialectNames.Sqlite,
                extraSources: [FeatureText_GetByIdProbeSource(valueObjects: true)],
                exec: [FeatureText_SqliteGetById()]
            ),
        ];
    }
}
