using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>決まった行のファイルの期待 1 つ（拡張子で選んだファイルの数・置き場・名前）</summary>
/// <param name="Suffix">選ぶファイル名の末尾（例 <c>.g.md</c>）</param>
/// <param name="Count">選んだファイルの数（null なら見ない）</param>
/// <param name="Directory">選んだファイルの全部の置き場（区切りは <c>/</c>。直下は null）</param>
/// <param name="NonEmpty">1 つ以上あること</param>
/// <param name="Names">選んだファイルの名前の集合（null なら見ない）</param>
/// <param name="AnyDirectory">置き場を見ない（層別出力など、置き場が出力の層に従うとき）</param>
/// <param name="Ordered">名前の並び順も見る（<paramref name="Names"/> の順に、出力の順で並ぶこと）</param>
/// <param name="Prefix">選ぶファイル名の先頭（null なら見ない。「Runtime. で始まるファイルが 0 個」のように、名前の組が決まらないときに使う）</param>
internal sealed record FileRule(
    string Suffix,
    int? Count = null,
    string? Directory = null,
    bool NonEmpty = false,
    IReadOnlyList<string>? Names = null,
    bool AnyDirectory = false,
    bool Ordered = false,
    string? Prefix = null
)
{
    /// <summary>照合する（問題を返す）</summary>
    public IReadOnlyList<string> Check(CodeGenerationResult result)
    {
        var selected = result
            .Files.Where(file =>
                file.FileName.EndsWith(Suffix, StringComparison.Ordinal)
                && (Prefix is null || file.FileName.StartsWith(Prefix, StringComparison.Ordinal))
            )
            .ToList();
        var problems = new List<string>();

        if (Count is { } count && selected.Count != count)
        {
            problems.Add(
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"ファイル（{Suffix}）: {selected.Count} 個（期待 {count} 個）"
                )
            );
        }

        if (NonEmpty && selected.Count == 0)
        {
            problems.Add($"ファイル（{Suffix}）: 1 つも無い");
        }

        foreach (
            var file in selected.Where(file =>
                !AnyDirectory && NormalizeDirectory(file.RelativeDirectory) != Directory
            )
        )
        {
            problems.Add(
                $"ファイル（{Suffix}）: {file.FileName} の置き場 {file.RelativeDirectory ?? "(直下)"}（期待 {Directory ?? "(直下)"}）"
            );
        }

        if (
            Names is not null
            && !selected
                .Select(file => file.FileName)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(Names)
        )
        {
            problems.Add(
                $"ファイル（{Suffix}）: 名前が [{string.Join(", ", selected.Select(file => file.FileName))}]（期待 [{string.Join(", ", Names)}]）"
            );
        }

        if (
            Ordered
            && Names is not null
            && !selected.Select(file => file.FileName).SequenceEqual(Names, StringComparer.Ordinal)
        )
        {
            problems.Add(
                $"ファイル（{Suffix}）: 並びが [{string.Join(", ", selected.Select(file => file.FileName))}]（期待 [{string.Join(", ", Names)}]）"
            );
        }

        return problems;
    }

    private static string? NormalizeDirectory(string? directory) =>
        string.IsNullOrEmpty(directory) ? null : directory.Replace('\\', '/').TrimEnd('/');
}

/// <summary>決まった行 1 つ（元の単発テストの図・オプション・期待の強さをそのまま持つ）</summary>
/// <param name="Diagram">図の組み立て</param>
/// <param name="Options">生成オプション</param>
/// <param name="TypeDialect">図の方言（型解決のプロバイダ）</param>
/// <param name="Diagnostics">診断の期待（完全一致・包含・件数）</param>
/// <param name="Entrance">入口</param>
/// <param name="FileRules">ファイルの期待（拡張子ごと）</param>
/// <param name="FileLayout">ファイル構成の規則（<see cref="FileLayoutOracle"/>）を掛けるか</param>
/// <param name="Namespaces">名前空間の規則を掛けるか</param>
/// <param name="Compile">コンパイルするか</param>
/// <param name="Probes">探針</param>
/// <param name="FileNamespaces">ファイル名 → そのファイルが宣言する名前空間の完全一致（null なら見ない）</param>
/// <param name="ExtraSources">一緒にコンパイルする追加のソース（利用側のコード断片など。手動実装のクエリと同じ経路でコンパイルに加わる。null なら無し）</param>
/// <param name="Relations">関係（null なら掛けない）</param>
/// <param name="ProbeFacts">探針へ渡す追加の事実（EF Core のモデルの事実・シードの行数。null なら無し）</param>
/// <param name="ComparisonOptions">オプションが効く関係（<see cref="CodegenRelation.OptionEffect"/>）の比較用のオプション</param>
/// <param name="ExactRuntimeTypeSets">Runtime 系の型集合の関係（<see cref="CodegenRelation.RuntimeTypeSet"/>）で、型の集合がパッケージと同じであるべきファイル名</param>
/// <param name="CompileAsOneAssembly">
/// 層別出力も層ごとの別プロジェクトでなく 1 アセンブリへまとめてコンパイルする（層をまたぐ同名の型など、1 アセンブリでしか出ない崩れを見る形。
/// 層別でなければ意味を持たない）
/// </param>
/// <param name="Code">生成コードの宣言の期待（<see cref="CodeRule"/>。null なら見ない）</param>
/// <param name="CompileWithout">参照を絞ったコンパイル（<see cref="ReducedReferences"/>。null なら掛けない）</param>
/// <param name="Exec">生成物を読み込んで動かす期待（<see cref="ExecCheck"/>。null なら無し）</param>
/// <param name="Parsed">生成結果を製品の解析器に通す期待（<see cref="ParsedCheck"/>。null なら無し）</param>
/// <param name="Markdown">API リファレンスの Markdown の構文木の期待（<see cref="MarkdownRule"/>。null なら無し）</param>
/// <param name="MapperDialects">
/// 型マッパーを渡す方言（null なら実効方言の全部＝本番と同じ。絞るのは direct の入口で関係を掛けない行だけ。
/// 関係と他の入口は全部の方言のマッパーで生成し直すので、絞った入力と比べられない）
/// </param>
internal sealed record CodegenCase(
    Func<ErDiagram> Diagram,
    CodeGenerationOptions Options,
    string TypeDialect,
    DiagnosticExpectation Diagnostics,
    CodegenEntrance Entrance = CodegenEntrance.Direct,
    IReadOnlyList<FileRule>? FileRules = null,
    bool FileLayout = false,
    bool Namespaces = false,
    bool Compile = false,
    IReadOnlyList<CodegenProbe>? Probes = null,
    IReadOnlyDictionary<string, string>? FileNamespaces = null,
    IReadOnlyList<GeneratedFile>? ExtraSources = null,
    IReadOnlyList<CodegenRelation>? Relations = null,
    CodegenProbeFacts? ProbeFacts = null,
    CodeGenerationOptions? ComparisonOptions = null,
    IReadOnlyList<string>? ExactRuntimeTypeSets = null,
    bool CompileAsOneAssembly = false,
    IReadOnlyList<CodeRule>? Code = null,
    ReducedReferences? CompileWithout = null,
    IReadOnlyList<ExecCheck>? Exec = null,
    IReadOnlyList<ParsedCheck>? Parsed = null,
    IReadOnlyList<MarkdownRule>? Markdown = null,
    IReadOnlyList<string>? MapperDialects = null
)
{
    /// <summary>期待を作る</summary>
    public CodegenExpectation Expectation() =>
        new(
            Diagnostics,
            FileRules is null && !FileLayout && FileNamespaces is null
                ? null
                : (options, result) =>
                    (FileRules ?? [])
                        .SelectMany(rule => rule.Check(result))
                        .Concat(FileLayout ? FileLayoutOracle.Check(options, result) : [])
                        .Concat(CheckFileNamespaces(result))
                        .ToList(),
            Namespaces,
            Compile,
            Semantics: false,
            Probes ?? [],
            Relations ?? [],
            ProbeFacts,
            ComparisonOptions,
            ExactRuntimeTypeSets,
            CompileAsOneAssembly,
            Code ?? [],
            CompileWithout,
            Exec ?? [],
            Parsed ?? [],
            Markdown ?? []
        );

    /// <summary>ファイルが宣言する名前空間が <see cref="FileNamespaces"/> と完全に一致するか（問題を返す）</summary>
    private IReadOnlyList<string> CheckFileNamespaces(CodeGenerationResult result)
    {
        var problems = new List<string>();

        foreach (var (fileName, expected) in FileNamespaces ?? new Dictionary<string, string>())
        {
            var files = result.Files.Where(file => file.FileName == fileName).ToList();

            if (files.Count != 1)
            {
                problems.Add($"名前空間: {fileName} が {files.Count} 個（期待 1 個）");
                continue;
            }

            var declared = NamespaceJudge.Declared(files[0]);

            if (!declared.SetEquals([expected]))
            {
                problems.Add(
                    $"名前空間: {fileName} の namespace が [{string.Join(", ", declared.Order(StringComparer.Ordinal))}]（期待 [{expected}]）"
                );
            }
        }

        return problems;
    }
}

/// <summary>
/// 決まった行の表（元の単発テストの図・オプション・期待を、元と同じ強さで 1 行にする。元のテストはまだ消さない）
/// </summary>
/// <remarks>
/// 行の Id は「{段}-{守っていること}-{形}」。説明は元のテストの DisplayName かメソッド名。網羅の計数には数えない。
/// </remarks>
internal static partial class CodegenCases
{
    /// <summary>表（主題ごとの行を並べた和）</summary>
    /// <remarks>
    /// 主題ごとの行は <c>CodegenCases.{主題}.cs</c> の private static なメソッドが返し、ここで並べる。
    /// 主題のファイルの静的なデータはフィールドでなくメソッド・式形式のプロパティにする（partial の初期化順で null にならないように）。
    /// </remarks>
    public static ScenarioTable<CodegenCase> Table { get; } =
        new(
            EngineIds.CodegenConfig,
            [
                .. SeedRows(),
                .. ServiceRows(),
                .. FileRows(),
                .. DialectRows(),
                .. DialectDslRows(),
                .. ColumnRows(),
                .. ExecutionRows(),
                .. RelationRows(),
                .. CompileRows(),
                .. GuiRows(),
                .. CompositeKeyOnlyRows(),
                .. TextRows(),
                .. EfCoreTextRows(),
                .. QueryTextRows(),
                .. FeatureTextRows(),
                .. ColumnTextRows(),
                .. NullablePrimaryKeyRows(),
            ]
        );

    /// <summary>フェーズ 1 で写した各判定の代表の行</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> SeedRows() =>
        [
            // ===== 診断 =====
            new ScenarioRow<CodegenCase>(
                "diag-remote-requires-contract-contracts-only",
                null,
                "CSharpCodeGenerationServiceTests.Generate_RemoteWithoutRepositoryContract_ShouldFailWithError(remoteContracts: true, remoteServices: false)",
                new CodegenCase(
                    SingleEntityDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        GenerateRemoteContracts = true,
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(
                        ExpectedDiagnostic.Error(
                            nameof(Strings.CodeGen_Error_RemoteRequiresContract)
                        )
                    )
                )
            ),
            new ScenarioRow<CodegenCase>(
                "diag-remote-requires-contract-services-only",
                null,
                "CSharpCodeGenerationServiceTests.Generate_RemoteWithoutRepositoryContract_ShouldFailWithError(remoteContracts: false, remoteServices: true)",
                new CodegenCase(
                    SingleEntityDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        GenerateRemoteServices = true,
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(
                        ExpectedDiagnostic.Error(
                            nameof(Strings.CodeGen_Error_RemoteRequiresContract)
                        )
                    )
                )
            ),
            new ScenarioRow<CodegenCase>(
                "diag-computed-primary-key-error-items",
                null,
                "主キーが計算列の図は生成時エラーになる（ComputedColumnGenerationTests.Generate_ComputedPrimaryKey_FailsWithError）",
                new CodegenCase(
                    () => ComputedItemsDiagram(computedIsPrimaryKey: true),
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        GenerateRepositories = true,
                        GenerateEditModels = false,
                        GenerateMappers = false,
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(
                        ExpectedDiagnostic.Error(
                            nameof(Strings.CodeGen_Error_PrimaryKeyComputedColumn),
                            "items",
                            "total"
                        )
                    )
                )
            ),
            new ScenarioRow<CodegenCase>(
                "diag-sync-requires-both-dialects-sqlserver",
                null,
                "同期支援は sqlserver + sqlite の 2 方言でなければ生成時エラー（SyncSupportGenerationTests.SyncSupport_RequiresBothDialects(\"sqlserver\")）",
                new CodegenCase(
                    SyncFixtureDefinition.Build,
                    SyncFixtureDefinition.Options with
                    {
                        RepositoryDialects = [DialectNames.SqlServer],
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(
                        ExpectedDiagnostic.Error(
                            nameof(Strings.CodeGen_Error_SyncSupportRequiresDialects),
                            DialectNames.SqlServer
                        )
                    )
                )
            ),
            // ===== ファイル構成 =====
            new ScenarioRow<CodegenCase>(
                "files-apidocs-subdirectory-places-markdown",
                null,
                "ApiDocsSubdirectory 指定で .g.md / .ja.g.md がサブフォルダ配置になる（.g.cs は不変）（ApiReferenceDocTests.ApiDocsSubdirectory_PlacesMarkdownIntoSubfolder）",
                new CodegenCase(
                    CustomerOrderDiagram,
                    new CodeGenerationOptions
                    {
                        GenerateApiDocs = true,
                        ApiDocsLanguage = ApiDocsLanguage.Both,
                        ApiDocsSubdirectory = "docs",
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(),
                    FileRules:
                    [
                        new FileRule(".g.md", Count: 2, Directory: "docs"),
                        new FileRule(".g.cs"),
                    ]
                )
            ),
            new ScenarioRow<CodegenCase>(
                "files-apidocs-ignore-code-subdirectory-split",
                null,
                "生成: API リファレンス Markdown はサブフォルダに追随しない（CodeSubdirectoryTests.Generate_ApiDocs_DoNotFollowSubdirectory の前半）",
                new CodegenCase(
                    OrderOnlyDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Acme.App",
                        SplitFilesByCategory = true,
                        GenerateRepositories = true,
                        GenerateApiDocs = true,
                        CodeSubdirectory = "Generated",
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(),
                    FileRules: [new FileRule(".g.md", NonEmpty: true)]
                )
            ),
            new ScenarioRow<CodegenCase>(
                "files-apidocs-subdirectory-over-code-subdirectory-split",
                null,
                "生成: API リファレンス Markdown はサブフォルダに追随しない（CodeSubdirectoryTests.Generate_ApiDocs_DoNotFollowSubdirectory の後半＝明示の ApiDocsSubdirectory へ）",
                new CodegenCase(
                    OrderOnlyDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Acme.App",
                        SplitFilesByCategory = true,
                        GenerateRepositories = true,
                        GenerateApiDocs = true,
                        CodeSubdirectory = "Generated",
                        ApiDocsSubdirectory = "docs",
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(),
                    FileRules: [new FileRule(".g.md", Directory: "docs")]
                )
            ),
            // ===== コンパイル =====
            new ScenarioRow<CodegenCase>(
                "compile-postgresql-array-columns-editmodels-vo",
                null,
                "配列列を含む図は EditModel・VO の有無に関わらずコンパイルできる（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumns_Compiles(true, true)）",
                new CodegenCase(
                    PostgreSqlArrayDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        GenerateEditModels = true,
                        GenerateMappers = true,
                        GenerateValueObjects = true,
                        GenerateRepositories = false,
                        GenerateEfCoreRepositories = true,
                    },
                    DialectNames.PostgreSql,
                    DiagnosticExpectation.Contains(),
                    Compile: true
                )
            ),
            // ===== 実行（探針） =====
            new ScenarioRow<CodegenCase>(
                "probe-guidkey-create-generates-unique-guid",
                null,
                "GuidKey の値オブジェクト（幅 50）：引数なし Create() の GUID 採番・Create(string) の保持と宣言幅超過の検証・TryCreate・等値・順序（GuidKeyValueObjectExecutionTests の 9 本）",
                new CodegenCase(
                    GuidKeyWidgetsDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        GenerateValueObjects = true,
                        UseGuidKeyForStringPrimaryKey = true,
                        GenerateEditModels = false,
                        GenerateMappers = false,
                        GenerateRepositories = false,
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(),
                    Probes: [CodegenProbe.GuidKeyCreate]
                )
            ),
            // ===== 入口 =====
            new ScenarioRow<CodegenCase>(
                "entrance-cli-api-docs-japanese-only",
                null,
                "--api-docs-lang Japanese は日本語版の API リファレンスだけを出力する（CliAppTests.Generate_ApiDocsLangJapanese_WritesJapaneseOnly。フラグの代わりに設定 JSON で渡す）",
                new CodegenCase(
                    CliCustomerDiagram,
                    new CodeGenerationOptions
                    {
                        OutputFileName = "Sample.g.cs",
                        GenerateApiDocs = true,
                        ApiDocsLanguage = ApiDocsLanguage.Japanese,
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(),
                    CodegenEntrance.Cli,
                    FileRules: [new FileRule(".g.md", Names: ["Sample.ja.g.md"])]
                )
            ),
            new ScenarioRow<CodegenCase>(
                "entrance-mcp-config-repositories-sqlite",
                null,
                "generate_csharp は config JSON を反映する（CodeGenToolSetTests.GenerateCSharp_AppliesConfigJson。SQLite の図・QuickER 版 Repository）",
                new CodegenCase(
                    () => CliCustomerDiagram(DialectNames.Sqlite),
                    new CodeGenerationOptions
                    {
                        GenerateRepositories = true,
                        RepositoryDialects = [DialectNames.Sqlite],
                    },
                    DialectNames.Sqlite,
                    DiagnosticExpectation.Contains(),
                    CodegenEntrance.Mcp,
                    FileLayout: true,
                    Namespaces: true,
                    Compile: true
                )
            ),
        ];

    /// <summary>決まった行を流す</summary>
    /// <param name="id">行の Id</param>
    public static Task RunAsync(string id)
    {
        var row = Table[id];
        var @case = row.Input;

        if (
            @case.MapperDialects is not null
            && (@case.Entrance != CodegenEntrance.Direct || @case.Relations is not null)
        )
        {
            throw new InvalidOperationException(
                $"行 {row.Id}: 型マッパーを絞る行（MapperDialects）は direct の入口で、関係を掛けない"
            );
        }

        var input = new CodegenInput(
            $"{row.Id}: {row.Description}",
            @case.Diagram(),
            @case.TypeDialect,
            @case.Options,
            @case.Entrance,
            _ => @case.ExtraSources ?? [],
            @case.Diagram,
            @case.MapperDialects
        );

        return CodegenRunner.RunAsync(input, _ => @case.Expectation(), shape: null, scope: row.Id);
    }

    // ---- 元のテストの図（そのまま写す） ----

    /// <summary>CSharpCodeGenerationServiceTests.SingleEntityDiagram</summary>
    private static ErDiagram SingleEntityDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>ComputedColumnGenerationTests.Diagram</summary>
    private static ErDiagram ComputedItemsDiagram(bool computedIsPrimaryKey) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = !computedIsPrimaryKey,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "qty",
                            DataType = "int",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "total",
                            DataType = "decimal(21,2)",
                            // 主キーにするときは NOT NULL（NULL 許容の主キーの読み替えの経路を意図せず通さない）
                            IsNullable = !computedIsPrimaryKey,
                            IsComputed = true,
                            IsPrimaryKey = computedIsPrimaryKey,
                        },
                    ],
                },
            ],
        };

    /// <summary>ApiReferenceDocTests.BuildDiagram</summary>
    private static ErDiagram CustomerOrderDiagram()
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
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "varchar(50)",
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
                    ColumnPairs = [new(customerPk, orderFk)],
                },
            ],
        };
    }

    /// <summary>CodeSubdirectoryTests.CreateDiagram</summary>
    private static ErDiagram OrderOnlyDiagram()
    {
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });

        return new ErDiagram { Entities = { order } };
    }

    /// <summary>PostgreSqlArrayColumnGenerationTests.BuildDiagram</summary>
    private static ErDiagram PostgreSqlArrayDiagram()
    {
        var id = new Column
        {
            Name = "id",
            DataType = "integer",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var tags = new Column
        {
            Name = "tags",
            DataType = "integer[]",
            IsNullable = false,
        };
        var labels = new Column
        {
            Name = "labels",
            DataType = "varchar(20)[]",
            IsNullable = true,
        };
        var codes = new Column
        {
            Name = "codes",
            DataType = "bigint[]",
            IsNullable = true,
        };
        var name = new Column
        {
            Name = "name",
            DataType = "varchar(50)",
            IsNullable = false,
        };
        var entity = new Entity
        {
            TableName = "articles",
            Columns = { id, tags, labels, codes, name },
        };

        return new ErDiagram
        {
            TargetDbms = DialectNames.PostgreSql,
            Entities = { entity },
            Queries =
            {
                new QueryDefinition
                {
                    EntityId = entity.Id,
                    Name = "FindByName",
                    Condition = "name = @name",
                    Parameters =
                    {
                        new QueryParameter { Name = "name", SourceColumnId = name.Id },
                    },
                    Returns = QueryReturnShape.Projection,
                    ResultTypeName = "ArticleSummary",
                    Fields =
                    {
                        new ProjectionField { Name = "Id", SourceColumnId = id.Id },
                        new ProjectionField { Name = "Tags", SourceColumnId = tags.Id },
                    },
                },
            },
        };
    }

    /// <summary>GuidKeyValueObjectExecutionTests.BuildGuidKeyValueObjectType の図（宣言幅 50）</summary>
    private static ErDiagram GuidKeyWidgetsDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "widgets",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "widget_id",
                            DataType = "nvarchar(50)",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "label",
                            DataType = "nvarchar(50)",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

    /// <summary>CliAppTests.CreateSampleSchema・CodeGenToolSetTests.CreateDiagram の図（Customer 1 表）</summary>
    private static ErDiagram CliCustomerDiagram() => CliCustomerDiagram(DialectNames.SqlServer);

    private static ErDiagram CliCustomerDiagram(string targetDbms)
    {
        var entity = new Entity { TableName = "Customer" };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(new Column { Name = "Name", DataType = "nvarchar(100)" });

        return new ErDiagram { TargetDbms = targetDbms, Entities = { entity } };
    }
}
