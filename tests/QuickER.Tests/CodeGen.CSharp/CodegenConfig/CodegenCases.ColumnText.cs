using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture;
using QuickER.Tests.GeneratedPortableFixture;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges.CodeRule;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：列の種類・マルチターゲット・ランタイムパッケージなどの小さなクラス の、生成テキストの文字列で判定していたテストを、宣言の期待・実行の探針・解析の判定へ言い換えたもの
/// </summary>
internal static partial class CodegenCases
{
    /// <summary>列の種類・マルチターゲットなどのテストクラスの行を作る（説明は <paramref name="testClass"/> ＋ "." ＋ 元のメソッド名）</summary>
    private static ScenarioRow<CodegenCase> ColumnTextRow(
        string id,
        string testClass,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        IReadOnlyList<CodeRule>? code = null,
        IReadOnlyList<EfCoreModelFact>? model = null,
        IReadOnlyList<ExecCheck>? exec = null,
        IReadOnlyList<ParsedCheck>? parsed = null,
        bool compile = false,
        string typeDialect = DialectNames.SqlServer,
        ReducedReferences? compileWithout = null,
        DiagnosticExpectation? diagnostics = null,
        IReadOnlyList<GeneratedFile>? extraSources = null,
        IReadOnlyList<CodegenRelation>? relations = null,
        IReadOnlyList<FileRule>? fileRules = null,
        CodeGenerationOptions? comparisonOptions = null,
        IReadOnlyList<MarkdownRule>? markdown = null
    ) =>
        new(
            id,
            null,
            testClass + "." + description,
            new CodegenCase(
                diagram,
                options,
                typeDialect,
                diagnostics ?? DiagnosticExpectation.Contains(),
                Compile: compile,
                Probes: model is null ? null : [CodegenProbe.EfCoreModel],
                ProbeFacts: model is null ? null : new CodegenProbeFacts(ModelFacts: model),
                ExtraSources: extraSources,
                Relations: relations,
                FileRules: fileRules,
                ComparisonOptions: comparisonOptions,
                Code: code,
                CompileWithout: compileWithout,
                Exec: exec,
                Parsed: parsed,
                Markdown: markdown
            )
        );

    // ===== 実行の探針の共通の部品 =====

    /// <summary>表示・入力の書式を固定して <paramref name="body"/> を流す（文字列の入力の解析・書式がカルチャに依らないように）</summary>
    private static IEnumerable<string> ColumnText_WithCulture(
        CultureInfo culture,
        Func<IEnumerable<string>> body
    )
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return body().ToList();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>EditModel（<c>{stem}EditModel</c>）を作る</summary>
    private static object ColumnText_NewEditModel(Assembly assembly, string stem) =>
        ProbeKit.Create(ProbeKit.Type(assembly, stem + "EditModel"));

    /// <summary>画面の入力（<c>Binding{property}</c>）へ文字列を入れる（確定値は入力の変換が決める）</summary>
    private static void ColumnText_Input(object model, string property, string text) =>
        ProbeKit.Set(model, "Binding" + property, text);

    /// <summary>EditModel を検証する（子を含む）。true ならエラーなし</summary>
    private static bool ColumnText_Validate(object model) =>
        (bool)ProbeKit.Call(model, "Validate", true)!;

    /// <summary>画面の入力（<c>Binding{property}</c>）に登録されているエラーの文</summary>
    private static IReadOnlyList<string> ColumnText_Errors(object model, string property) =>
        ((IEnumerable)ProbeKit.Call(model, "GetErrors", "Binding" + property)!)
            .Cast<object>()
            .Select(error => error.ToString() ?? string.Empty)
            .ToList();

    /// <summary>
    /// <c>EntitySaveMetadata</c> の書き込み集合（INSERT の列・UPDATE の列）から <paramref name="excluded"/> が外れ、<paramref name="written"/> は入り、
    /// 読む列（SELECT）には <paramref name="excluded"/> も残る
    /// </summary>
    private static ExecCheck ColumnText_WriteExclusion(
        string entity,
        string[] excluded,
        string[] written
    ) =>
        new(
            "write-exclusion",
            assembly =>
            {
                var problems = new List<string>();
                var metadataType = ProbeKit.Type(assembly, "EntitySaveMetadata");
                var metadata = metadataType
                    .GetMethod("For", [typeof(Type)])!
                    .Invoke(null, [ProbeKit.Type(assembly, entity)])!;

                IReadOnlySet<string> Names(string property) =>
                    (
                        (IEnumerable<PropertyInfo>)
                            metadataType.GetProperty(property)!.GetValue(metadata)!
                    )
                        .Select(item => item.Name)
                        .ToHashSet(StringComparer.Ordinal);

                foreach (
                    var (what, property) in new[]
                    {
                        ("INSERT", "InsertProperties"),
                        ("UPDATE", "NonKeyProperties"),
                    }
                )
                {
                    var names = Names(property);

                    problems.AddRange(
                        excluded
                            .Where(names.Contains)
                            .Select(name => $"{what} の列に {name} がある（外れるはず）")
                    );
                    problems.AddRange(
                        written
                            .Where(name => !names.Contains(name))
                            .Select(name => $"{what} の列に {name} が無い（書くはず）")
                    );
                }

                var selected = Names("SelectProperties");

                problems.AddRange(
                    excluded
                        .Where(name => !selected.Contains(name))
                        .Select(name => $"SELECT の列に {name} が無い（読むはず）")
                );

                return problems;
            }
        );

    /// <summary>
    /// 主キー（<paramref name="keys"/>）だけ入力した EditModel を Mapper で実体へ写すと、列 <paramref name="column"/> は、未入力なら実体の現在値
    /// （<paramref name="current"/>）を保ち、入力があれば代入する（<paramref name="input"/> → <paramref name="expected"/>）
    /// </summary>
    private static ExecCheck ColumnText_MapperSkipsAbsentInput(
        string stem,
        (string Property, string Text)[] keys,
        string column,
        object current,
        string input,
        object expected
    ) =>
        new(
            "mapper-skips-absent-input",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                    {
                        var problems = new List<string>();
                        var mapper = ProbeKit.Create(ProbeKit.Type(assembly, stem + "Mapper"));

                        object Apply(string? text)
                        {
                            var model = ColumnText_NewEditModel(assembly, stem);

                            foreach (var (property, value) in keys)
                            {
                                ColumnText_Input(model, property, value);
                            }

                            if (text is not null)
                            {
                                ColumnText_Input(model, column, text);
                            }

                            var entity = ProbeKit.Create(ProbeKit.Type(assembly, stem + "Entity"));
                            ProbeKit.Set(entity, column, current);
                            ProbeKit.Call(mapper, "ApplyToEntity", model, entity, false);
                            return ProbeKit.Get(entity, column)!;
                        }

                        if (!Equals(Apply(null), current))
                        {
                            problems.Add(
                                $"{column} が未入力なのに実体の現在値 {current} が変わった"
                            );
                        }

                        if (!Equals(Apply(input), expected))
                        {
                            problems.Add(
                                $"{column} の入力 {input} が実体へ {expected} として入らない"
                            );
                        }

                        return problems;
                    }
                )
        );

    /// <summary>
    /// 主キーなどの必須の入力（<paramref name="filled"/>）だけを入れた EditModel の検証が、<paramref name="required"/> の列の未入力を必須エラーにし、
    /// <paramref name="optional"/> の列の未入力はエラーにしない
    /// </summary>
    private static ExecCheck ColumnText_RequiredInput(
        string stem,
        (string Property, string Text)[] filled,
        string[] required,
        string[] optional
    ) =>
        new(
            "required-input",
            assembly =>
            {
                var problems = new List<string>();
                var model = ColumnText_NewEditModel(assembly, stem);

                foreach (var (property, value) in filled)
                {
                    ColumnText_Input(model, property, value);
                }

                var valid = ColumnText_Validate(model);

                problems.AddRange(
                    required
                        .Where(property => ColumnText_Errors(model, property).Count == 0)
                        .Select(property => $"{property} が未入力なのに必須エラーが無い")
                );
                problems.AddRange(
                    optional
                        .Where(property => ColumnText_Errors(model, property).Count > 0)
                        .Select(property => $"{property} が未入力で必須エラーになった")
                );

                if (valid != (required.Length == 0))
                {
                    problems.Add($"検証の結果が {valid}（必須の未入力 {required.Length} 件）");
                }

                return problems;
            }
        );

    /// <summary>コンテキスト（<c>QuickErDbContext</c>）を SQLite のプロバイダで作ってモデルを組む（実接続はしない）</summary>
    private static T ColumnText_WithModel<T>(Assembly assembly, Func<IModel, T> read)
    {
        var contextType = ProbeKit.Type(assembly, "QuickErDbContext");
        var builder = (DbContextOptionsBuilder)
            Activator.CreateInstance(
                typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType)
            )!;
        builder.UseSqlite("Data Source=:memory:").IgnoreServiceProviderCacheLimit();

        using var context = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
        return read(context.Model);
    }

    /// <summary>DI のコンテナを作る（スコープの検査はしない）</summary>
    private static ServiceProvider ColumnText_Provider(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });

    /// <summary>
    /// DI の登録（<paramref name="services"/>）から、リモート面の契約（<paramref name="remote"/>）と全機能面の契約（<paramref name="full"/>）が
    /// 同じスコープで同じインスタンスへ解決される（<paramref name="key"/> を指定すれば keyed の登録で）
    /// </summary>
    private static IEnumerable<string> ColumnText_ForwardsToFullContract(
        Assembly assembly,
        IServiceCollection services,
        string remote,
        string full,
        object? key = null
    )
    {
        using var provider = ColumnText_Provider(services);
        using var scope = provider.CreateScope();
        var remoteType = ProbeKit.Type(assembly, remote);
        var fullType = ProbeKit.Type(assembly, full);
        var where = key is null ? "非 keyed" : $"keyed（{key}）";

        object Resolve(Type type) =>
            key is null
                ? scope.ServiceProvider.GetRequiredService(type)
                : scope.ServiceProvider.GetRequiredKeyedService(type, key);

        if (!ReferenceEquals(Resolve(remoteType), Resolve(fullType)))
        {
            yield return $"{where}: {remote} と {full} が同じインスタンスに解決されない";
        }
    }

    // ===== 計算列・採番列（ComputedColumnGenerationTests・IdentityColumnGenerationTests） =====

    private const string ComputedClass = "ComputedColumnGenerationTests";

    private const string IdentityClass = "IdentityColumnGenerationTests";

    /// <summary>元のテストの <c>Generate</c>（計算列・採番列とも同じ）</summary>
    private static CodeGenerationOptions ColumnText_ItemOptions(
        bool efCore = false,
        bool editModels = false
    ) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = !efCore,
            GenerateEfCoreRepositories = efCore,
            GenerateEditModels = editModels,
            GenerateMappers = editModels,
        };

    /// <summary>NOT NULL の <c>Seq</c> を持つ採番列の図の EditModel の入力（主キー・数量）</summary>
    private static (string Property, string Text)[] ColumnText_ItemKeys =>
        [("ItemId", "1"), ("Qty", "2")];

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_ComputedIdentityRows() =>
        [
            ColumnTextRow(
                "code-computed-column-marker-and-write-exclusion",
                ComputedClass,
                "Generate_ComputedColumn_MarksAttributeAndExcludesFromWrite",
                () => Columns_ComputedDiagram(computed: true),
                ColumnText_ItemOptions(),
                code:
                [
                    Type(
                        "ComputedColumnAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    Attribute("ItemEntity", "Total", "ComputedColumn"),
                    PropertiesWithAttribute("ComputedColumn", 1),
                    Member("ItemEntity", "Total", "decimal? Total { get; set; }"),
                ],
                exec: [ColumnText_WriteExclusion("ItemEntity", ["Total"], ["Qty"])]
            ),
            ColumnTextRow(
                "code-computed-column-none-marks-nothing",
                ComputedClass,
                "Generate_NoComputedColumn_DoesNotMarkAnyProperty",
                () => Columns_ComputedDiagram(computed: false),
                ColumnText_ItemOptions(),
                code:
                [
                    Type(
                        "ComputedColumnAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    PropertiesWithAttribute("ComputedColumn", 0),
                ]
            ),
            ColumnTextRow(
                "model-computed-column-value-generated-on-add-or-update",
                ComputedClass,
                "Generate_EfCore_ComputedColumn_ConfiguresValueGenerated",
                () => Columns_ComputedDiagram(computed: true),
                ColumnText_ItemOptions(efCore: true),
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Total",
                        column: "total",
                        precision: 21,
                        scale: 2,
                        valueGenerated: ValueGenerated.OnAddOrUpdate
                    ),
                ]
            ),
            ColumnTextRow(
                "model-computed-column-none-value-generated-never",
                ComputedClass,
                "Generate_EfCore_NoComputedColumn_DoesNotConfigureValueGenerated",
                () => Columns_ComputedDiagram(computed: false),
                ColumnText_ItemOptions(efCore: true),
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "ItemId",
                        valueGenerated: ValueGenerated.Never
                    ),
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Qty",
                        valueGenerated: ValueGenerated.Never
                    ),
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Total",
                        valueGenerated: ValueGenerated.Never
                    ),
                ]
            ),
            ColumnTextRow(
                "exec-computed-column-editmodel-not-required-mapper-skips-absent",
                ComputedClass,
                "Generate_ComputedColumn_EditModelIsNotRequiredAndMapperSkipsAbsentInput",
                () => Columns_ComputedDiagram(computed: true),
                ColumnText_ItemOptions(editModels: true),
                exec:
                [
                    ColumnText_RequiredInput("Item", ColumnText_ItemKeys, [], ["Total"]),
                    ColumnText_MapperSkipsAbsentInput(
                        "Item",
                        ColumnText_ItemKeys,
                        "Total",
                        5m,
                        "7",
                        7m
                    ),
                ]
            ),
            ColumnTextRow(
                "exec-computed-column-none-not-null-column-stays-required",
                ComputedClass,
                "Generate_NonComputedNotNullColumn_StaysRequired",
                () => Columns_ComputedDiagram(computed: false),
                ColumnText_ItemOptions(editModels: true),
                exec: [ColumnText_RequiredInput("Item", [("ItemId", "1")], ["Qty"], [])]
            ),
            ColumnTextRow(
                "exec-computed-column-not-null-value-type-mapper-unwraps-nullable",
                ComputedClass,
                "Generate_NotNullValueTypeComputedColumn_MapperUnwrapsNullable",
                Columns_NotNullComputedDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    GenerateEditModels = true,
                    GenerateMappers = true,
                },
                compile: true,
                exec:
                [
                    ColumnText_MapperSkipsAbsentInput(
                        "TemporalItem",
                        [("Id", "1")],
                        "PeriodStart",
                        new DateTime(2020, 1, 1),
                        "2024-03-05 10:20:30",
                        new DateTime(2024, 3, 5, 10, 20, 30)
                    ),
                ]
            ),
            ColumnTextRow(
                "code-identity-column-marker-and-write-exclusion",
                IdentityClass,
                "Generate_IdentityColumn_MarksAttributeAndExcludesFromWrite",
                () => Columns_IdentityDiagram(identity: true, identityPrimaryKey: true),
                ColumnText_ItemOptions(),
                code:
                [
                    Type(
                        "IdentityColumnAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    Attribute("ItemEntity", "Seq", "IdentityColumn"),
                    PropertiesWithAttribute("IdentityColumn", 1),
                    Member("ItemEntity", "Seq", "long Seq { get; set; }"),
                ],
                exec: [ColumnText_WriteExclusion("ItemEntity", ["Seq"], ["Qty"])]
            ),
            ColumnTextRow(
                "code-identity-column-none-marks-nothing",
                IdentityClass,
                "Generate_NoIdentityColumn_DoesNotMarkAnyProperty",
                () => Columns_IdentityDiagram(identity: false),
                ColumnText_ItemOptions(),
                code:
                [
                    Type(
                        "IdentityColumnAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    PropertiesWithAttribute("IdentityColumn", 0),
                ]
            ),
            ColumnTextRow(
                "model-identity-column-value-generated-on-add-or-update",
                IdentityClass,
                "Generate_EfCore_IdentityColumn_ConfiguresValueGenerated",
                () => Columns_IdentityDiagram(identity: true, identityPrimaryKey: true),
                ColumnText_ItemOptions(efCore: true),
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Seq",
                        column: "seq",
                        required: true,
                        valueGenerated: ValueGenerated.OnAddOrUpdate
                    ),
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "ItemId",
                        valueGenerated: ValueGenerated.OnAdd
                    ),
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Qty",
                        valueGenerated: ValueGenerated.Never
                    ),
                ]
            ),
            ColumnTextRow(
                "model-identity-column-none-value-generated-never",
                IdentityClass,
                "Generate_EfCore_NoIdentityColumn_DoesNotConfigureValueGenerated",
                () => Columns_IdentityDiagram(identity: false),
                ColumnText_ItemOptions(efCore: true),
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "ItemId",
                        valueGenerated: ValueGenerated.Never
                    ),
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Qty",
                        valueGenerated: ValueGenerated.Never
                    ),
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Seq",
                        valueGenerated: ValueGenerated.Never
                    ),
                ]
            ),
            ColumnTextRow(
                "exec-identity-column-editmodel-not-required-mapper-skips-absent",
                IdentityClass,
                "Generate_IdentityColumn_EditModelIsNotRequiredAndMapperSkipsAbsentInput",
                () => Columns_IdentityDiagram(identity: true),
                ColumnText_ItemOptions(editModels: true),
                exec:
                [
                    ColumnText_RequiredInput("Item", ColumnText_ItemKeys, [], ["Seq"]),
                    ColumnText_MapperSkipsAbsentInput(
                        "Item",
                        ColumnText_ItemKeys,
                        "Seq",
                        9L,
                        "5",
                        5L
                    ),
                ]
            ),
            ColumnTextRow(
                "exec-identity-column-none-not-null-column-stays-required",
                IdentityClass,
                "Generate_NonIdentityNotNullColumn_StaysRequired",
                () => Columns_IdentityDiagram(identity: false),
                ColumnText_ItemOptions(editModels: true),
                exec: [ColumnText_RequiredInput("Item", ColumnText_ItemKeys, ["Seq"], [])]
            ),
        ];

    // ===== PostgreSQL の配列列（PostgreSqlArrayColumnGenerationTests） =====

    private const string ArrayClass = "PostgreSqlArrayColumnGenerationTests";

    /// <summary>
    /// インメモリのストアは、書いた行の配列を呼び出し側が書き換えても変わらず（配列を複製して持つ）、シード値の NOT NULL の配列列は空の配列（null でない）
    /// </summary>
    private static ExecCheck ColumnText_ArrayInMemory() =>
        new(
            "inmemory-array-copy-and-seed",
            assembly =>
            {
                var problems = new List<string>();
                var entityType = ProbeKit.Type(assembly, "ArticleEntity");
                var storeType = ProbeKit.Type(assembly, "InMemoryDataStore");
                var store = ProbeKit.Create(storeType);
                var tags = new[] { 1, 2, 3 };
                var entity = ProbeKit.Create(entityType);

                ColumnText_Assign(entity, "Id", 1);
                ColumnText_Assign(entity, "Tags", tags);
                ColumnText_Assign(entity, "Name", "a");
                ProbeKit.Call(entity, "MarkAdded", false);
                ProbeKit.Call(store, "Put", entity);
                tags[0] = 99;

                var stored = storeType
                    .GetMethod("Find")!
                    .MakeGenericMethod(entityType)
                    .Invoke(store, [1])!;

                if (!((int[])ProbeKit.Get(stored, "Tags")!).SequenceEqual([1, 2, 3]))
                {
                    problems.Add(
                        "呼び出し側が配列を書き換えたらストアの行の Tags が変わった（配列が共有されている）"
                    );
                }

                var seeded = ProbeKit.Create(storeType);
                ProbeKit.CallStatic(ProbeKit.Type(assembly, "InMemorySampleData"), "Seed", seeded);

                var rows = (IEnumerable)
                    storeType
                        .GetMethod("Snapshot")!
                        .MakeGenericMethod(entityType)
                        .Invoke(seeded, null)!;
                var count = 0;

                foreach (var row in rows)
                {
                    count++;

                    if (ProbeKit.Get(row, "Tags") is not int[] { Length: 0 })
                    {
                        problems.Add("シード値の Tags が空の配列でない");
                    }
                }

                if (count == 0)
                {
                    problems.Add("シード値の行が 1 つも無い");
                }

                return problems;
            }
        );

    /// <summary>新しい <c>ArticleEntity</c> の <c>Tags</c>（NOT NULL の配列）は空の配列で始まる</summary>
    private static ExecCheck ColumnText_ArrayDefaultsEmpty() =>
        new(
            "array-column-starts-empty",
            assembly =>
                ProbeKit.Get(ProbeKit.Create(ProbeKit.Type(assembly, "ArticleEntity")), "Tags")
                    is int[] { Length: 0 }
                    ? []
                    : ["新しい ArticleEntity の Tags が空の int[] でない"]
        );

    /// <summary>
    /// 生成物（1 ファイル）を C# リバースに通すと、配列でない列だけが復元され、飛ばした配列列（<c>tags</c>・<c>labels</c>）は警告で名指しされる
    /// </summary>
    private static ParsedCheck ColumnText_ReverseSkipsArrayColumns() =>
        new(
            "reverse-skips-array-columns",
            result =>
            {
                var source = result.Files.Single(file =>
                    file.FileName.EndsWith(".g.cs", StringComparison.Ordinal)
                );
                var reversed = new CSharpReverseParser().Parse(
                    source.Content,
                    new PostgreSqlTypeCatalog(),
                    IdentifierLengthLimit.Unlimited
                );
                var problems = new List<string>();

                if (reversed.Entities.Count != 1)
                {
                    return [$"復元した表が {reversed.Entities.Count} 個（期待 1 個）"];
                }

                var columns = reversed.Entities[0].Columns.Select(column => column.Name).ToList();

                if (!columns.SequenceEqual(["id", "name"]))
                {
                    problems.Add($"復元した列が [{string.Join(", ", columns)}]（期待 [id, name]）");
                }

                foreach (var skipped in new[] { "tags", "labels" })
                {
                    if (
                        !reversed.Warnings.Any(warning =>
                            warning.Contains(skipped, StringComparison.Ordinal)
                        )
                    )
                    {
                        problems.Add($"飛ばした列 {skipped} を名指しする警告が無い");
                    }
                }

                return problems;
            }
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_ArrayRows() =>
        [
            .. new[] { false, true }.Select(valueObjects =>
                ColumnTextRow(
                    $"exec-postgresql-array-columns-inmemory-copies-and-seeds{(valueObjects ? "-vo" : "")}",
                    ArrayClass,
                    $"Generate_ArrayColumnsWithInMemory_CopiesAndSeedsArrays({valueObjects})",
                    PostgreSqlArrayDiagram,
                    Columns_PostgreSqlArrayOptions(
                        editModels: true,
                        valueObjects: valueObjects
                    ) with
                    {
                        GenerateInMemoryRepositories = true,
                    },
                    exec: [ColumnText_ArrayInMemory()],
                    typeDialect: DialectNames.PostgreSql
                )
            ),
            ColumnTextRow(
                "code-postgresql-array-columns-element-typed-arrays",
                ArrayClass,
                "Generate_ArrayColumns_EmitElementTypedArrays",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: false, valueObjects: false),
                code:
                [
                    Member("ArticleEntity", "Tags", "int[] Tags { get; set; }"),
                    Member("ArticleEntity", "Labels", "string[]? Labels { get; set; }"),
                    Member("ArticleEntity", "Codes", "long[]? Codes { get; set; }"),
                ],
                exec: [ColumnText_ArrayDefaultsEmpty()],
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnTextRow(
                "code-postgresql-array-columns-not-in-editmodel",
                ArrayClass,
                "Generate_ArrayColumns_AreNotInEditModel",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: false),
                code:
                [
                    Member("ArticleEditModel", "BindingName", "string BindingName { get; set; }"),
                    Text_NoTypeDeclaring("BindingTags"),
                    Text_NoTypeDeclaring("BindingLabels"),
                ],
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnTextRow(
                "code-postgresql-array-columns-not-value-objects",
                ArrayClass,
                "Generate_ArrayColumns_AreNotValueObjects",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: false, valueObjects: true),
                code:
                [
                    Type("NameValue", kind: "class", modifiers: "public sealed partial"),
                    NoType("TagsValue"),
                    NoType("LabelsValue"),
                ],
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnTextRow(
                "parsed-postgresql-array-columns-have-no-token-and-reverse-skips",
                ArrayClass,
                "Reverse_ArrayColumns_AreSkippedWithWarning",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: false, valueObjects: false),
                code:
                [
                    Type("ArticleEntity", kind: "class"),
                    Attribute("ArticleEntity", "Name", "DbColumnMeta(\"string(50)\")"),
                    Attribute("ArticleEntity", "Tags", "Column(\"tags\")"),
                    NoAttribute("ArticleEntity", "Tags", "DbColumnMeta"),
                ],
                parsed: [ColumnText_ReverseSkipsArrayColumns()],
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnTextRow(
                "compile-postgresql-array-columns-named-query-projection",
                ArrayClass,
                "Generate_NamedQueryProjectingArrayColumn_Compiles",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: false),
                compile: true,
                code:
                [
                    Type("ArticleSummary", kind: "class"),
                    Member("ArticleSummary", "Tags", "int[] Tags { get; set; }"),
                ],
                typeDialect: DialectNames.PostgreSql
            ),
        ];

    // ===== 表示名（DisplayNameGenerationTests） =====

    private const string DisplayNameClass = "DisplayNameGenerationTests";

    /// <summary>DisplayNameGenerationTests.SingleEntity：単一エンティティの図（列は名前・型・主キー・NULL 可・説明）</summary>
    private static ErDiagram ColumnText_SingleEntity(
        string tableName,
        string? tableDescription,
        params (
            string Name,
            string DataType,
            bool IsPk,
            bool IsNullable,
            string? Description
        )[] columns
    ) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = tableName,
                    Description = tableDescription ?? string.Empty,
                    Columns = columns
                        .Select(column => new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = column.Name,
                            DataType = column.DataType,
                            IsPrimaryKey = column.IsPk,
                            IsNullable = column.IsNullable,
                            Description = column.Description ?? string.Empty,
                        })
                        .ToList(),
                },
            ],
        };

    /// <summary>顧客の図（<c>customer_id</c> と、説明つきの <c>name</c>）</summary>
    private static ErDiagram ColumnText_DisplayNameCustomers() =>
        ColumnText_SingleEntity(
            "customers",
            tableDescription: null,
            ("customer_id", "int", true, false, null),
            ("name", "nvarchar(50)", false, false, "顧客名")
        );

    /// <summary>値オブジェクトの静的な <c>DisplayName</c> が <paramref name="expected"/>（型名 → 表示名）どおり</summary>
    private static ExecCheck ColumnText_ValueObjectDisplayNames(
        params (string Type, string DisplayName)[] expected
    ) =>
        new(
            "value-object-display-names",
            assembly =>
                expected
                    .Select(item =>
                        (
                            item.Type,
                            item.DisplayName,
                            Actual: ProbeKit.GetStatic(
                                ProbeKit.Type(assembly, item.Type),
                                "DisplayName"
                            )
                        )
                    )
                    .Where(item => !Equals(item.Actual, item.DisplayName))
                    .Select(item =>
                        $"{item.Type}.DisplayName が「{item.Actual}」（期待「{item.DisplayName}」）"
                    )
        );

    /// <summary>エンティティ（<paramref name="entity"/>）の <c>DisplayName</c> が <paramref name="expected"/></summary>
    private static ExecCheck ColumnText_EntityDisplayName(string entity, string expected) =>
        new(
            "entity-display-name",
            assembly =>
                ProbeKit.Get(ProbeKit.Create(ProbeKit.Type(assembly, entity)), "DisplayName")
                    is string actual
                && actual == expected
                    ? []
                    : [$"{entity}.DisplayName が期待「{expected}」と違う"]
        );

    /// <summary>DisplayName を持たない・基底の既定が出る、の共通の宣言（元の <c>CustomizeDisplayName</c> の不在と基底の既定）</summary>
    private static IEnumerable<CodeRule> ColumnText_DisplayNameBase() =>
        [
            Type("GeneratedDisplayNames", kind: "class", modifiers: "public static"),
            Member(
                "EntityBaseCore",
                "DisplayName",
                "string DisplayName { get; }",
                modifiers: "public"
            ),
            Member(
                "EntityBaseCore",
                "DefaultDisplayName",
                "string DefaultDisplayName { get; }",
                modifiers: "protected virtual"
            ),
            Text_NoTypeDeclaring("CustomizeDisplayName"),
        ];

    /// <summary>
    /// EditModel の入力のエラーを作る文面の差し替え点（<c>EditModelMessages.Required</c> / <c>ParseFailed</c>）が、表示名を受け取る
    /// （必須のエラーは <paramref name="requiredName"/>、変換のエラーは <paramref name="parseName"/>。差し替え点を引数を書き出す関数に替えて確かめる）
    /// </summary>
    private static ExecCheck ColumnText_EditModelMessagesCarryDisplayNames(
        string requiredProperty,
        string requiredName,
        string parseProperty,
        string parseName
    ) =>
        new(
            "editmodel-messages-carry-display-names",
            assembly =>
            {
                var problems = new List<string>();
                var messages = ProbeKit.Type(assembly, "EditModelMessages");
                var required = messages.GetProperty("Required")!;
                var parseFailed = messages.GetProperty("ParseFailed")!;
                var originalRequired = required.GetValue(null);
                var originalParseFailed = parseFailed.GetValue(null);

                try
                {
                    required.SetValue(
                        null,
                        (Func<string, string, string>)(
                            (property, display) => $"required|{property}|{display}"
                        )
                    );
                    parseFailed.SetValue(
                        null,
                        (Func<string, string, string, string, string>)(
                            (property, display, input, type) =>
                                $"parse|{property}|{display}|{input}|{type}"
                        )
                    );

                    var model = ColumnText_NewEditModel(assembly, "Customer");

                    ColumnText_Input(model, parseProperty, "abc");
                    ColumnText_Validate(model);

                    var requiredErrors = ColumnText_Errors(model, requiredProperty);
                    var parseErrors = ColumnText_Errors(model, parseProperty);

                    if (!requiredErrors.Contains($"required|{requiredProperty}|{requiredName}"))
                    {
                        problems.Add(
                            $"{requiredProperty} の必須エラーが [{string.Join(", ", requiredErrors)}]（期待は表示名「{requiredName}」を受け取る）"
                        );
                    }

                    if (!parseErrors.Contains($"parse|{parseProperty}|{parseName}|abc|int"))
                    {
                        problems.Add(
                            $"{parseProperty} の変換エラーが [{string.Join(", ", parseErrors)}]（期待は表示名「{parseName}」を受け取る）"
                        );
                    }
                }
                finally
                {
                    required.SetValue(null, originalRequired);
                    parseFailed.SetValue(null, originalParseFailed);
                }

                return problems;
            }
        );

    /// <summary>実体の列 <paramref name="property"/> へ値を入れる（値オブジェクトの構成なら、その型の <c>Create</c> で包んでから）</summary>
    private static void ColumnText_Assign(object entity, string property, object value)
    {
        var propertyType = entity.GetType().GetProperty(property)!.PropertyType;
        var target = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        ProbeKit.Set(
            entity,
            property,
            target.IsInstanceOfType(value) ? value : ProbeKit.CallStatic(target, "Create", value)
        );
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_DisplayNameRows()
    {
        const string tableDescription = "行1\r\n\"引用\"\\パス";
        const string columnDescription = "列\"名\"\\end";

        return
        [
            ColumnTextRow(
                "exec-display-name-value-object-description-or-property-name",
                DisplayNameClass,
                "Generate_ValueObjectDisplayName_UsesDescriptionOrPropertyNameFallback",
                ColumnText_DisplayNameCustomers,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateValueObjects = true,
                },
                code: [.. ColumnText_DisplayNameBase()],
                exec:
                [
                    ColumnText_ValueObjectDisplayNames(
                        ("NameValue", "顧客名"),
                        ("CustomerIdValue", "CustomerId")
                    ),
                ]
            ),
            ColumnTextRow(
                "exec-display-name-entity-description",
                DisplayNameClass,
                "Generate_EntityDisplayName_UsesDescriptionOrClassNameFallback（Description あり）",
                () =>
                    ColumnText_SingleEntity(
                        "customers",
                        tableDescription: "顧客マスタ",
                        ("customer_id", "int", true, false, null)
                    ),
                new CodeGenerationOptions { RootNamespace = "Sample.Domain" },
                code:
                [
                    .. ColumnText_DisplayNameBase(),
                    Member(
                        "CustomerEntity",
                        "DefaultDisplayName",
                        "string DefaultDisplayName { get; }",
                        modifiers: "protected override"
                    ),
                ],
                exec: [ColumnText_EntityDisplayName("CustomerEntity", "顧客マスタ")]
            ),
            ColumnTextRow(
                "exec-display-name-entity-class-name-fallback",
                DisplayNameClass,
                "Generate_EntityDisplayName_UsesDescriptionOrClassNameFallback（Description なし）",
                () =>
                    ColumnText_SingleEntity(
                        "customers",
                        tableDescription: null,
                        ("customer_id", "int", true, false, null)
                    ),
                new CodeGenerationOptions { RootNamespace = "Sample.Domain" },
                code:
                [
                    .. ColumnText_DisplayNameBase(),
                    NoMember("CustomerEntity", "DefaultDisplayName"),
                ],
                exec: [ColumnText_EntityDisplayName("CustomerEntity", "CustomerEntity")]
            ),
            ColumnTextRow(
                "exec-display-name-without-value-objects-wires-helper",
                DisplayNameClass,
                "Generate_WithoutValueObjects_WiresGetDisplayNameHelper",
                ColumnText_DisplayNameCustomers,
                new CodeGenerationOptions { RootNamespace = "Sample.Domain" },
                code:
                [
                    Member(
                        "CustomerEditModel",
                        "GetDisplayName",
                        "string GetDisplayName(string propertyName, string? description)",
                        modifiers: "private static"
                    ),
                    Text_NoTypeDeclaring("CustomizePropertyDisplayName"),
                ],
                exec:
                [
                    ColumnText_EditModelMessagesCarryDisplayNames(
                        "Name",
                        "顧客名",
                        "CustomerId",
                        "CustomerId"
                    ),
                ]
            ),
            ColumnTextRow(
                "code-display-name-entity-column-collision-omits-override",
                DisplayNameClass,
                "Generate_EntityWithDisplayNameColumn_OmitsMembersWarnsAndKeepsOtherEntities",
                Columns_DisplayNameCollisionDiagram,
                new CodeGenerationOptions { RootNamespace = "Sample.Domain" },
                code:
                [
                    Member(
                        "EntityBaseCore",
                        "DefaultDisplayName",
                        "string DefaultDisplayName { get; }",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "LabelEntity",
                        "DisplayName",
                        "string DisplayName { get; set; }",
                        modifiers: "public new"
                    ),
                    NoTypes(
                        "DefaultDisplayName を上書きする",
                        type =>
                            type.GetMembers("DefaultDisplayName")
                                .Any(member => member.IsOverride && !member.IsImplicitlyDeclared)
                    ),
                    NoTypes(
                        "LabelEntity 以外のエンティティが基底の DisplayName を隠す",
                        type =>
                            type.Name != "LabelEntity"
                            && type.Name.EndsWith("Entity", StringComparison.Ordinal)
                            && type.GetMembers("DisplayName")
                                .Any(member => !member.IsImplicitlyDeclared)
                    ),
                ],
                exec: [ColumnText_EntityDisplayName("CustomerEntity", "CustomerEntity")]
            ),
            ColumnTextRow(
                "exec-display-name-description-special-characters",
                DisplayNameClass,
                "Generate_DescriptionWithSpecialChars_EscapesForCSharpLiteral",
                () =>
                    ColumnText_SingleEntity(
                        "customers",
                        tableDescription,
                        ("customer_id", "int", true, false, null),
                        ("name", "nvarchar(50)", false, false, columnDescription)
                    ),
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateValueObjects = true,
                    IncludeDataAnnotations = true,
                },
                code:
                [
                    Attribute(
                        "CustomerEntity",
                        null,
                        "DbTableMeta(Description = \"行1 \\\"引用\\\"\\\\パス\")"
                    ),
                    Attribute(
                        "CustomerEntity",
                        "Name",
                        "DbColumnMeta(\"string(50)\", Description = \"列\\\"名\\\"\\\\end\")"
                    ),
                ],
                exec:
                [
                    ColumnText_EntityDisplayName("CustomerEntity", "行1 \"引用\"\\パス"),
                    ColumnText_ValueObjectDisplayNames(("NameValue", columnDescription)),
                ]
            ),
        ];
    }

    // ===== 列の型の小さなテスト（ネットワーク型・フォールバック型・負のスケール） =====

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_TypeRows() =>
        [
            ColumnTextRow(
                "code-postgresql-network-types-inet-excluded-from-editmodel",
                "PostgreSqlNetworkTypeGenerationTests",
                "Generate_InetIsExcludedFromEditModel",
                Columns_NetworkDiagram,
                Columns_NetworkOptions(editModels: true, valueObjects: false, inMemory: false),
                code:
                [
                    Member("HostEntity", "Addr", "IPAddress Addr { get; set; }"),
                    Member("HostEntity", "BackupAddr", "IPAddress? BackupAddr { get; set; }"),
                    Member("HostEntity", "Net", "IPNetwork Net { get; set; }"),
                    Member("HostEntity", "Lease", "TimeSpan Lease { get; set; }"),
                    Member("HostEditModel", "BindingNet"),
                    Member("HostEditModel", "BindingLease"),
                    Text_NoTypeDeclaring("BindingAddr"),
                    Text_NoTypeDeclaring("BindingBackupAddr"),
                ],
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnTextRow(
                "code-fallback-type-column-is-string-property",
                "FallbackTypeGenerationTests",
                "Generate_FallbackTypeColumn_PropertyIsStringLikeAnyOtherStringColumn",
                () => Columns_FallbackDiagram(includeUnknownType: true),
                Columns_FallbackOptions(),
                code: [Member("ItemEntity", "Location", "string? Location { get; set; }")]
            ),
            ColumnTextRow(
                "code-negative-scale-column-is-decimal-property",
                "NegativeScaleColumnGenerationTests",
                "Generate_NegativeScaleColumn_PropertyIsDecimal",
                () => Columns_NegativeScaleDiagram("numeric(10,-2)"),
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                },
                code: [Member("OrderEntity", "Amount", "decimal? Amount { get; set; }")]
            ),
        ];

    // ===== SQLite の REAL（SqliteRealColumnGenerationTests） =====

    private const string SqliteRealClass = "SqliteRealColumnGenerationTests";

    /// <summary>SqliteRealColumnGenerationTests.Generate の図（<paramref name="dataType"/> の列 <c>value</c> を持つ SQLite の表）</summary>
    private static ErDiagram ColumnText_MeasuresDiagram(string dataType) =>
        new()
        {
            TargetDbms = DialectNames.Sqlite,
            Entities =
            {
                new Entity
                {
                    TableName = "measures",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "INTEGER",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "value",
                            DataType = dataType,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

    private static CodeGenerationOptions ColumnText_MeasuresOptions() =>
        new()
        {
            RootNamespace = "QuickER.Tests.SqliteReal",
            GenerateValueObjects = false,
            SplitFilesByCategory = false,
        };

    /// <summary>生成物（1 ファイル）を C# リバースに通すと、列 <c>value</c> が <c>REAL</c> へ戻り、警告が無い</summary>
    private static ParsedCheck ColumnText_RealRoundTrip() =>
        new(
            "real-round-trips",
            result =>
            {
                var source = result.Files.Single(file =>
                    file.FileName.EndsWith(".g.cs", StringComparison.Ordinal)
                );
                var reversed = new CSharpReverseParser().Parse(
                    source.Content,
                    new SqliteTypeCatalog(),
                    IdentifierLengthLimit.Unlimited
                );
                var problems = new List<string>();
                var column = reversed
                    .Entities.SingleOrDefault()
                    ?.Columns.SingleOrDefault(item => item.Name == "value");

                if (column?.DataType != "REAL")
                {
                    problems.Add(
                        $"復元した value の型が {column?.DataType ?? "(列なし)"}（期待 REAL）"
                    );
                }

                problems.AddRange(reversed.Warnings.Select(warning => $"警告が出た: {warning}"));
                return problems;
            }
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_SqliteRealRows() =>
        [
            ColumnTextRow(
                "code-sqlite-real-double-with-verbatim-spelling",
                SqliteRealClass,
                "Real_GeneratesDoubleWithVerbatimSpelling",
                () => ColumnText_MeasuresDiagram("REAL"),
                ColumnText_MeasuresOptions(),
                code:
                [
                    Member("MeasureEntity", "Value", "double Value { get; set; }"),
                    Attribute(
                        "MeasureEntity",
                        "Value",
                        "DbColumnMeta(\"float64\", NativeType = \"REAL\")"
                    ),
                ],
                parsed: [ColumnText_RealRoundTrip()],
                typeDialect: DialectNames.Sqlite
            ),
            ColumnTextRow(
                "code-sqlite-float-double-without-verbatim",
                SqliteRealClass,
                "Float_GeneratesDoubleWithoutVerbatim",
                () => ColumnText_MeasuresDiagram("FLOAT"),
                ColumnText_MeasuresOptions(),
                code:
                [
                    Member("MeasureEntity", "Value", "double Value { get; set; }"),
                    Attribute("MeasureEntity", "Value", "DbColumnMeta(\"float64\")"),
                ],
                typeDialect: DialectNames.Sqlite
            ),
        ];

    // ===== 一意制約の名前（UniqueConstraintNameGenerationTests） =====

    private const string UniqueNameClass = "UniqueConstraintNameGenerationTests";

    /// <summary>UniqueConstraintNameGenerationTests.GeneratedUniqueConstraintName_MatchesDdl の図（<paramref name="collides"/> ならほかの表が合成名と同じ名前の一意制約を持つ）</summary>
    private static ErDiagram ColumnText_UniqueNameDiagram(bool collides)
    {
        var entity = new Entity
        {
            TableName = "dbo.Orders",
            Columns =
            {
                new Column
                {
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
            },
        };
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [entity.Columns[1].Id] });

        var diagram = new ErDiagram { Entities = { entity } };

        if (collides)
        {
            var other = new Entity
            {
                TableName = "dbo.Other",
                Columns =
                {
                    new Column
                    {
                        Name = "other_id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "label",
                        DataType = "nvarchar(20)",
                        IsNullable = false,
                    },
                },
            };
            other.UniqueConstraints.Add(
                new UniqueConstraint
                {
                    Name = "UQ_dbo_Orders_code",
                    ColumnIds = [other.Columns[1].Id],
                }
            );
            diagram.Entities.Add(other);
        }

        return diagram;
    }

    /// <summary>生成コードの一意制約名が、同じ図の DDL（<paramref name="ddl"/>）が付ける一意制約名 <paramref name="expected"/> と同じ</summary>
    private static ParsedCheck ColumnText_UniqueNameMatchesDdl(
        Func<string> ddl,
        string quote,
        string expected
    ) =>
        new(
            "unique-constraint-name-matches-ddl",
            _ =>
                ddl().Contains(quote.Replace("{0}", expected), StringComparison.Ordinal)
                    ? []
                    : [$"DDL に一意制約名 {expected} が無い（生成コードの名前と食い違う）"]
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_UniqueNameRows()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "QuickER.Tests.UniqueConstraintName",
            SplitFilesByCategory = false,
        };
        var longTable = new string('t', 60);

        ErDiagram LongTableDiagram()
        {
            var entity = new Entity
            {
                TableName = longTable,
                Columns =
                {
                    new Column
                    {
                        Name = "id",
                        DataType = "integer",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "code",
                        DataType = "varchar(20)",
                        IsNullable = false,
                    },
                },
            };
            entity.UniqueConstraints.Add(
                new UniqueConstraint { ColumnIds = [entity.Columns[1].Id] }
            );

            return new ErDiagram { TargetDbms = DialectNames.PostgreSql, Entities = { entity } };
        }

        var shortened = UniqueConstraint.SynthesizeName(
            longTable,
            ["code"],
            IdentifierLengthLimits.PostgreSql
        );

        return
        [
            .. new[]
            {
                (Collides: false, Expected: "UQ_dbo_Orders_code"),
                (Collides: true, Expected: "UQ_dbo_Orders_code_2"),
            }.Select(item =>
                ColumnTextRow(
                    $"code-unique-constraint-name-schema-qualified-matches-ddl{(item.Collides ? "-collision" : "")}",
                    UniqueNameClass,
                    $"GeneratedUniqueConstraintName_MatchesDdl({item.Collides}, \"{item.Expected}\")",
                    () => ColumnText_UniqueNameDiagram(item.Collides),
                    options,
                    code:
                    [
                        Attribute(
                            "DboOrderEntity",
                            null,
                            $"UniqueConstraint([\"Code\"], Name = \"{item.Expected}\")"
                        ),
                    ],
                    parsed:
                    [
                        ColumnText_UniqueNameMatchesDdl(
                            () =>
                                new SqlServerDdlGenerator().Build(
                                    ColumnText_UniqueNameDiagram(item.Collides)
                                ),
                            "[{0}]",
                            item.Expected
                        ),
                    ]
                )
            ),
            ColumnTextRow(
                "code-unique-constraint-name-shortened-like-ddl",
                UniqueNameClass,
                "GeneratedUniqueConstraintName_IsShortenedLikeDdl",
                LongTableDiagram,
                options,
                code:
                [
                    Attribute(
                        char.ToUpperInvariant(longTable[0]) + longTable[1..] + "Entity",
                        null,
                        "UniqueConstraint([\"Code\"], Name = \"" + shortened + "\")"
                    ),
                ],
                parsed:
                [
                    ColumnText_UniqueNameMatchesDdl(
                        () => new PostgreSqlProvider().DdlGenerator.Build(LongTableDiagram()),
                        "\"{0}\"",
                        shortened
                    ),
                ],
                typeDialect: DialectNames.PostgreSql
            ),
        ];
    }

    // ===== EditModel の日付の表示・新規行だけ必須・保存後の読み戻し =====

    /// <summary>EditModelDateBindingGenerationTests.CreateDiagram の図（date 列と datetime2 列）</summary>
    private static ErDiagram ColumnText_DateBindingDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "orders",
                    Service_Column("order_id", "int", primaryKey: true),
                    Service_Column("delivery_date", "date", nullable: true),
                    Service_Column("ordered_at", "datetime2", nullable: true)
                ),
            ],
        };

    /// <summary>
    /// 実体を EditModel へ読み込むと、date 列の表示は日付のみの短い書式、datetime2 列の表示は秒未満を保つ書式で導出される
    /// （カルチャは en-US に固定）
    /// </summary>
    private static ExecCheck ColumnText_DateBinding() =>
        new(
            "date-binding-format",
            assembly =>
                ColumnText_WithCulture(
                    new CultureInfo("en-US"),
                    () =>
                    {
                        var problems = new List<string>();
                        var delivery = new DateTime(2024, 3, 5);
                        var ordered = new DateTime(2024, 3, 5, 10, 20, 30).AddTicks(1234567);
                        var entity = ProbeKit.Create(ProbeKit.Type(assembly, "OrderEntity"));
                        ColumnText_Assign(entity, "OrderId", 1);
                        ColumnText_Assign(entity, "DeliveryDate", delivery);
                        ColumnText_Assign(entity, "OrderedAt", ordered);

                        var mapper = ProbeKit.Create(ProbeKit.Type(assembly, "OrderMapper"));
                        var model = ProbeKit.Call(mapper, "CreateEditModel", entity)!;

                        if (ProbeKit.Get(model, "BindingDeliveryDate") as string != "3/5/2024")
                        {
                            problems.Add(
                                $"BindingDeliveryDate が「{ProbeKit.Get(model, "BindingDeliveryDate")}」（期待「3/5/2024」＝日付のみの短い書式）"
                            );
                        }

                        if (
                            !DateTime.TryParse(
                                ProbeKit.Get(model, "BindingOrderedAt") as string,
                                CultureInfo.CurrentCulture,
                                DateTimeStyles.None,
                                out var parsed
                            )
                            || parsed != ordered
                        )
                        {
                            problems.Add(
                                $"BindingOrderedAt が「{ProbeKit.Get(model, "BindingOrderedAt")}」（秒未満を保たない）"
                            );
                        }

                        return problems;
                    }
                )
        );

    /// <summary>ExcludedBinaryRequiredWhenAddedGenerationTests.CreateDiagram の図</summary>
    private static ErDiagram ColumnText_ExcludedBinaryDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "documents",
                    Service_Column("document_id", "int", primaryKey: true),
                    Service_Column("title", "nvarchar(50)"),
                    Service_Column("thumb", "varbinary(max)"),
                    Service_Column("payload", "varbinary(max)", nullable: true),
                    Service_Column("checksum", "varbinary(16)"),
                    Service_Column("row_ver", "rowversion")
                ),
            ],
        };

    /// <summary>
    /// 何も入力していない EditModel の検証で、新規行（Added）と既存の行（Unchanged）それぞれについて、列ごとの必須エラーの有無が
    /// <paramref name="expected"/>（列 → 新規行でエラーか・既存の行でエラーか）のとおり
    /// </summary>
    private static ExecCheck ColumnText_RequiredWhenAdded(
        params (string Property, bool WhenAdded, bool WhenExisting)[] expected
    ) =>
        new(
            "required-when-added",
            assembly =>
            {
                var problems = new List<string>();

                foreach (
                    var (state, markState) in new[]
                    {
                        ("新規行", "MarkAdded"),
                        ("既存の行", "MarkUnchanged"),
                    }
                )
                {
                    var model = ColumnText_NewEditModel(assembly, "Document");
                    ProbeKit.Call(model, markState);
                    ColumnText_Validate(model);

                    foreach (var (property, whenAdded, whenExisting) in expected)
                    {
                        var want = state == "新規行" ? whenAdded : whenExisting;
                        var has = ColumnText_Errors(model, property).Count > 0;

                        if (has != want)
                        {
                            problems.Add(
                                $"{state}: {property} の必須エラーが{(has ? "ある" : "無い")}（期待は{(want ? "ある" : "無い")}）"
                            );
                        }
                    }
                }

                return problems;
            }
        );

    /// <summary>AcceptSavedExcludedBinaryGenerationTests.CreateDiagram の図</summary>
    private static ErDiagram ColumnText_AcceptSavedDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "documents",
                    Service_Column("document_id", "int", primaryKey: true),
                    Service_Column("payload", "varbinary(max)", nullable: true)
                ),
            ],
        };

    /// <summary>
    /// 保存した実体を <c>AcceptSaved</c> で EditModel へ受け入れると、除外列 <c>Payload</c> は <paramref name="reset"/> が true なら未取得（null）へ戻り、
    /// false ならそのまま
    /// </summary>
    private static ExecCheck ColumnText_AcceptSavedPayload(bool reset) =>
        new(
            "accept-saved-payload",
            assembly =>
            {
                var model = ColumnText_NewEditModel(assembly, "Document");
                ColumnText_Input(model, "DocumentId", "1");

                var mapper = ProbeKit.Create(ProbeKit.Type(assembly, "DocumentMapper"));
                var entity = ProbeKit.Call(mapper, "CreateEntity", model, true)!;
                var payload = new byte[] { 1, 2, 3 };
                ProbeKit.Set(entity, "Payload", payload);
                ProbeKit.Call(entity, "MarkUnchanged");
                ProbeKit.Call(mapper, "AcceptSaved", model, entity);

                var after = ProbeKit.Get(entity, "Payload") as byte[];

                return reset
                    ? (after is null ? [] : ["AcceptSaved の後も実体の Payload が未取得へ戻らない"])
                    : (
                        after is not null && after.SequenceEqual(payload)
                            ? []
                            :
                            [
                                "AcceptSaved が実体の Payload を変えた（EditModel が持ち続けるはず）",
                            ]
                    );
            }
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_EditModelRows()
    {
        var documentOptions = (bool exclude, bool valueObjects) =>
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateRepositories = true,
                GenerateValueObjects = valueObjects,
                ExcludeUnboundedBinaryColumns = exclude,
            };

        (string Property, bool WhenAdded, bool WhenExisting)[] excluded =
        [
            ("Thumb", true, false),
            ("Payload", false, false),
            ("RowVer", false, false),
            ("Checksum", true, true),
            ("Title", true, true),
        ];
        (string Property, bool WhenAdded, bool WhenExisting)[] notExcluded =
        [
            ("Thumb", true, true),
            ("Payload", false, false),
            ("RowVer", false, false),
            ("Checksum", true, true),
            ("Title", true, true),
        ];

        return
        [
            .. new[] { false, true }.SelectMany(valueObjects =>
                new[]
                {
                    ColumnTextRow(
                        $"exec-excluded-binary-required-when-added-only-not-null-excluded{(valueObjects ? "-vo" : "")}",
                        "ExcludedBinaryRequiredWhenAddedGenerationTests",
                        $"RequiredWhenAdded_OnlyForNonNullableExcludedBinaryColumn({valueObjects})",
                        ColumnText_ExcludedBinaryDiagram,
                        documentOptions(true, valueObjects),
                        exec: [ColumnText_RequiredWhenAdded(excluded)]
                    ),
                    ColumnTextRow(
                        $"exec-excluded-binary-required-when-added-never-without-exclusion{(valueObjects ? "-vo" : "")}",
                        "ExcludedBinaryRequiredWhenAddedGenerationTests",
                        $"RequiredWhenAdded_NeverSet_WhenExclusionDisabled({valueObjects})",
                        ColumnText_ExcludedBinaryDiagram,
                        documentOptions(false, valueObjects),
                        exec: [ColumnText_RequiredWhenAdded(notExcluded)]
                    ),
                }
            ),
            ColumnTextRow(
                "exec-editmodel-date-binding-short-date-format",
                "EditModelDateBindingGenerationTests",
                "DateColumn_UsesShortDateFormat",
                ColumnText_DateBindingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateEditModels = true,
                    GenerateValueObjects = false,
                },
                exec: [ColumnText_DateBinding()]
            ),
            ColumnTextRow(
                "exec-editmodel-date-binding-short-date-format-vo",
                "EditModelDateBindingGenerationTests",
                "DateColumn_WithValueObjects_UsesShortDateFormatOfUnderlyingValue",
                ColumnText_DateBindingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateEditModels = true,
                    GenerateValueObjects = true,
                },
                exec: [ColumnText_DateBinding()]
            ),
            .. new (bool Repositories, bool EfCore, bool InMemory, bool Expected)[]
            {
                (true, false, false, true),
                (false, false, true, true),
                (false, true, false, false),
                (true, true, false, false),
            }.Select(item =>
                ColumnTextRow(
                    $"exec-accept-saved-excluded-binary-override-{(item.Repositories ? (item.EfCore ? "repositories-and-efcore" : "repositories-only") : (item.EfCore ? "efcore-only" : "inmemory-only"))}",
                    "AcceptSavedExcludedBinaryGenerationTests",
                    $"LoadSavedColumnsOverride_IsGeneratedOnlyWithoutEfCore({item.Repositories}, {item.EfCore}, {item.InMemory}, {item.Expected})",
                    ColumnText_AcceptSavedDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        GenerateEditModels = true,
                        GenerateRepositories = item.Repositories,
                        GenerateEfCoreRepositories = item.EfCore,
                        GenerateInMemoryRepositories = item.InMemory,
                        ExcludeUnboundedBinaryColumns = true,
                    },
                    code:
                    [
                        item.Expected
                            ? Member(
                                "DocumentMapper",
                                "LoadSavedColumns",
                                modifiers: "protected override"
                            )
                            : NoMember("DocumentMapper", "LoadSavedColumns"),
                    ],
                    exec: [ColumnText_AcceptSavedPayload(item.Expected)]
                )
            ),
        ];
    }

    // ===== リモート契約（RemoteContractGenerationTests） =====

    private const string RemoteContractClass = "RemoteContractGenerationTests";

    /// <summary>RemoteContractGenerationTests.CreateDiagram の図（Order エンティティ＋名前付きクエリ 1 件）</summary>
    private static ErDiagram ColumnText_RemoteOrderDiagram()
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
        order.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsNullable = false,
            }
        );
        order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });

        var diagram = new ErDiagram { Entities = { order } };
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "GetByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
            }
        );

        return diagram;
    }

    private const string SqlServerConnection = "Server=.;Database=x;TrustServerCertificate=true";

    private const string SqliteConnection = "Data Source=:memory:";

    private static ExecCheck ColumnText_RemoteForwards(
        string registration,
        Func<Assembly, object[]> arguments,
        (object? Key, string Registration, Func<Assembly, object[]> Arguments)[]? more = null
    ) =>
        new(
            "remote-face-forwards-to-full-contract",
            assembly =>
            {
                var problems = ColumnText_ForwardsToFullContract(
                        assembly,
                        ProbeKit.Services(assembly, registration, arguments(assembly)),
                        "IOrderRemoteRepository",
                        "IOrderRepository"
                    )
                    .ToList();

                foreach (var (key, name, extra) in more ?? [])
                {
                    problems.AddRange(
                        ColumnText_ForwardsToFullContract(
                            assembly,
                            ProbeKit.Services(assembly, name, extra(assembly)),
                            "IOrderRemoteRepository",
                            "IOrderRepository",
                            key
                        )
                    );
                }

                return problems;
            }
        );

    /// <summary>DI の登録から全機能面の契約だけが解決でき、リモート面の契約の型は存在しない</summary>
    private static ExecCheck ColumnText_UnifiedContractResolves() =>
        new(
            "unified-contract-resolves",
            assembly =>
            {
                using var provider = ColumnText_Provider(
                    ProbeKit.Services(
                        assembly,
                        "AddGeneratedSqlServerRepositories",
                        SqlServerConnection
                    )
                );
                using var scope = provider.CreateScope();

                return
                    scope.ServiceProvider.GetService(ProbeKit.Type(assembly, "IOrderRepository"))
                        is null
                    ? ["IOrderRepository が解決できない"]
                    : [];
            }
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_RemoteContractRows() =>
        [
            ColumnTextRow(
                "exec-remote-contracts-on-adds-remote-face",
                RemoteContractClass,
                "Generate_RemoteContracts_AddsRemoteFace",
                ColumnText_RemoteOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                    GenerateRemoteContracts = true,
                },
                code:
                [
                    Type(
                        "IOrderRemoteRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRemoteRepository<OrderEntity, int>"]
                    ),
                    Member(
                        "IOrderRemoteRepository",
                        "GetByCustomerAsync",
                        "Task<IReadOnlyList<OrderEntity>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Type(
                        "IOrderRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IOrderRemoteRepository", "IRepository<OrderEntity, int>"]
                    ),
                    Type("OrderRepository", kind: "class", interfaces: ["IOrderRepository"]),
                    Member(
                        "OrderRepository",
                        ".ctor",
                        "OrderRepository(ISqlConnectionFactory connectionFactory, ISaveHookRegistry? saveHooks = null, ISqlExecutor? sqlExecutor = null)"
                    ),
                ],
                exec:
                [
                    ColumnText_RemoteForwards(
                        "AddGeneratedSqlServerRepositories",
                        _ => [SqlServerConnection]
                    ),
                ]
            ),
            ColumnTextRow(
                "exec-remote-contracts-off-keeps-unified-contract",
                RemoteContractClass,
                "Generate_Default_KeepsUnifiedContract",
                ColumnText_RemoteOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                },
                code:
                [
                    Type(
                        "IOrderRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRepository<OrderEntity, int>"]
                    ),
                    NoType("IOrderRemoteRepository"),
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
                        "IRemoteRepository<TEntity, TKey>",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRemoteRepositoryCore<TEntity, TKey>"]
                    ),
                ],
                exec: [ColumnText_UnifiedContractResolves()]
            ),
            ColumnTextRow(
                "exec-remote-contracts-on-efcore-forwards",
                RemoteContractClass,
                "Generate_RemoteContractsWithEfCore_AddsForwardingRegistration",
                ColumnText_RemoteOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                    GenerateRemoteContracts = true,
                },
                code:
                [
                    Type(
                        "EfCoreOrderRepository",
                        kind: "class",
                        modifiers: "public sealed partial",
                        interfaces: ["IOrderRepository"]
                    ),
                ],
                exec:
                [
                    ColumnText_RemoteForwards(
                        "AddGeneratedEfCoreRepositories",
                        _ =>
                            [
                                (Action<DbContextOptionsBuilder>)(
                                    options =>
                                        options
                                            .UseSqlite(SqliteConnection)
                                            .IgnoreServiceProviderCacheLimit()
                                ),
                            ]
                    ),
                ]
            ),
            ColumnTextRow(
                "exec-remote-contracts-on-inmemory-forwards",
                RemoteContractClass,
                "Generate_RemoteContractsWithInMemory_AddsForwardingRegistration",
                ColumnText_RemoteOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                    GenerateInMemoryRepositories = true,
                    GenerateRemoteContracts = true,
                },
                code:
                [
                    Type(
                        "InMemoryOrderRepository",
                        kind: "class",
                        interfaces: ["IOrderRepository"]
                    ),
                ],
                exec: [ColumnText_RemoteForwards("AddGeneratedInMemoryRepositories", _ => [false])]
            ),
            ColumnTextRow(
                "exec-remote-contracts-on-multi-target-keyed-forwards",
                RemoteContractClass,
                "Generate_RemoteContractsWithMultiTarget_AddsKeyedForwarding",
                ColumnText_RemoteOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    GenerateRemoteContracts = true,
                },
                code: [Type("IOrderRemoteRepository", kind: "interface")],
                exec:
                [
                    ColumnText_RemoteForwards(
                        "AddGeneratedSqlServerRepositories",
                        _ => [SqlServerConnection],
                        [
                            (
                                "server",
                                "AddGeneratedSqlServerRepositories",
                                _ => ["server", SqlServerConnection]
                            ),
                            (
                                "local",
                                "AddGeneratedSqliteRepositories",
                                _ => ["local", SqliteConnection]
                            ),
                        ]
                    ),
                ]
            ),
        ];

    // ===== マルチターゲットの Repository（MultiTargetRepositoryGenerationTests） =====

    private const string MultiTargetClass = "MultiTargetRepositoryGenerationTests";

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_MultiTargetRows() =>
        [
            ColumnTextRow(
                "code-multitarget-sqlite-diagram-with-sqlserver-target-sql-column-type",
                MultiTargetClass,
                "Generate_SqliteDiagramWithSqlServerTarget_EmitsSqlColumnTypeAttribute",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.Sqlite, DialectNames.SqlServer],
                },
                code:
                [
                    Attribute("CustomerEntity", "CustomerId", "SqlColumnType(SqlDbType.Int)"),
                    Attribute(
                        "CustomerEntity",
                        "Name",
                        "SqlColumnType(SqlDbType.VarChar, Size = 50)"
                    ),
                ],
                typeDialect: DialectNames.Sqlite
            ),
            ColumnTextRow(
                "code-multitarget-sqlite-only-has-no-sql-column-type-or-sqlclient",
                MultiTargetClass,
                "Generate_SqliteOnly_DoesNotEmitSqlColumnTypeAttribute",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.Sqlite],
                },
                code:
                [
                    NoType("SqlColumnTypeAttribute"),
                    PropertiesWithAttribute("SqlColumnType", 0),
                    NoUsingUnder(AllFiles, "Microsoft.Data.SqlClient"),
                ],
                compileWithout: ReducedReferences.WithoutSqlClient,
                typeDialect: DialectNames.Sqlite
            ),
            .. new[] { false, true }.Select(valueObjects =>
                ColumnTextRow(
                    $"code-multitarget-nonsplit-repository-namespace-outside-root{(valueObjects ? "-vo" : "")}",
                    MultiTargetClass,
                    $"MultiDialect_NonSplit_RepositoryNamespaceOutsideRoot_Compiles({valueObjects})",
                    CustomerOrderDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "MyApp",
                        RepositoryNamespace = "Data.Repos",
                        GenerateRepositories = true,
                        GenerateValueObjects = valueObjects,
                        RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    },
                    code:
                    [
                        Type("CustomerRepository", ns: "Data.Repos.SqlServer", kind: "class"),
                        Using("QuickEREntities.g.cs", "MyApp"),
                    ]
                )
            ),
            ColumnTextRow(
                "code-multitarget-nonsplit-contract-once-and-dialect-namespaces",
                MultiTargetClass,
                "MultiDialect_NonSplit_ContractOnceAndDialectNamespaces",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    SplitFilesByCategory = false,
                },
                code:
                [
                    Type(
                        "IRepository<TEntity, TKey>",
                        kind: "interface",
                        modifiers: "public partial"
                    ),
                    Type("SqlQuery<TEntity>", kind: "class", modifiers: "public sealed"),
                    Type("ICustomerRepository", kind: "interface", modifiers: "public partial"),
                    Type(
                        "CustomerRepository",
                        ns: "Sample.Domain.Repositories.SqlServer",
                        kind: "class",
                        baseType: "SqlServerRepository<CustomerEntity, int>",
                        interfaces: ["ICustomerRepository"]
                    ),
                    Type(
                        "CustomerRepository",
                        ns: "Sample.Domain.Repositories.Sqlite",
                        kind: "class",
                        baseType: "SqliteRepository<CustomerEntity, int>",
                        interfaces: ["ICustomerRepository"]
                    ),
                ]
            ),
        ];

    // ===== rowversion × マルチターゲット（MultiTargetRowVersionGenerationTests） =====

    private const string RowVersionClass = "MultiTargetRowVersionGenerationTests";

    /// <summary>共有 Entity の版列が行バージョン（<c>byte[]</c>）で <c>[StoreGeneratedColumn]</c> が付く（日時の <c>RowVer</c> は無い）</summary>
    private static IReadOnlyList<CodeRule> ColumnText_RowVersionUnified() =>
        [
            Member("SyncItemEntity", "RowVer", "byte[] RowVer { get; set; }"),
            Attribute("SyncItemEntity", "RowVer", "StoreGeneratedColumn"),
        ];

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_RowVersionRows() =>
        [
            ColumnTextRow(
                "code-rowversion-multitarget-fixture-unifies-to-byte-array",
                RowVersionClass,
                "Generate_RowVersionMultiTarget_UnifiesToByteArrayWithoutError",
                MultiTargetRowVersionFixtureDefinition.Build,
                MultiTargetRowVersionFixtureDefinition.Options,
                code: [.. ColumnText_RowVersionUnified()]
            ),
            ColumnTextRow(
                "code-rowversion-multitarget-sqlite-diagram-unifies-timestamp",
                RowVersionClass,
                "Generate_SqliteDiagramWithSqlServerTarget_UnifiesTimestampToRowVersion",
                Dialects_TimestampSpellingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Mixed",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                code: [.. ColumnText_RowVersionUnified()],
                typeDialect: DialectNames.Sqlite
            ),
            ColumnTextRow(
                "code-rowversion-multitarget-facade-unifies-timestamp",
                RowVersionClass,
                "DiagramCodeGenerator_TimestampMultiTarget_SucceedsWithInfo",
                Dialects_TimestampSpellingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Hybrid",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                code: [Member("SyncItemEntity", "RowVer", "byte[] RowVer { get; set; }")]
            ),
            ColumnTextRow(
                "code-rowversion-sqlite-only-timestamp-stays-datetime",
                RowVersionClass,
                "Generate_SqliteOnly_TimestampStaysDateTime",
                Dialects_TimestampSpellingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.LocalOnly",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.Sqlite],
                },
                code:
                [
                    Member("SyncItemEntity", "RowVer", "DateTime RowVer { get; set; }"),
                    NoAttribute("SyncItemEntity", "RowVer", "StoreGeneratedColumn"),
                ],
                typeDialect: DialectNames.Sqlite
            ),
            ColumnTextRow(
                "relation-rowversion-efcore-only-hidden-dialects-have-no-effect",
                RowVersionClass,
                "Generate_EfCoreOnlyWithHiddenDialects_IgnoresDialectDictionaries",
                Dialects_TimestampSpellingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.EfOnly",
                    GenerateEfCoreRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                code:
                [
                    Member("SyncItemEntity", "RowVer", "DateTime RowVer { get; set; }"),
                    PropertiesWithAttribute("SqlColumnType", 0),
                ],
                relations: [CodegenRelation.OptionNoEffect],
                comparisonOptions: new CodeGenerationOptions
                {
                    RootNamespace = "Sample.EfOnly",
                    GenerateEfCoreRepositories = true,
                },
                typeDialect: DialectNames.Sqlite
            ),
        ];

    // ===== 同じ 2 表の間の複数のリレーション（NavigationRoleNameTests） =====

    private const string NavigationClass = "NavigationRoleNameTests";

    private const string NavigationNamespace = "Test.Nav";

    private static Column ColumnText_NavColumn(
        string name,
        string dataType,
        bool primaryKey = false,
        bool nullable = false
    ) =>
        new()
        {
            Name = name,
            DataType = dataType,
            IsPrimaryKey = primaryKey,
            IsForeignKey = !primaryKey && name.EndsWith("_id", StringComparison.Ordinal),
            IsNullable = nullable,
        };

    private static Relationship ColumnText_NavReference(
        Entity parent,
        Column parentColumn,
        Entity child,
        Column childColumn,
        RelationshipType type
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            ColumnPairs = { new(parentColumn.Id, childColumn.Id) },
        };

    /// <summary>customers を orders が 2 本（ship / bill）で参照する図</summary>
    private static ErDiagram ColumnText_ShipBillDiagram(RelationshipType type)
    {
        var customerId = ColumnText_NavColumn("customer_id", "int", primaryKey: true);
        var customers = new Entity
        {
            TableName = "customers",
            Columns = { customerId, ColumnText_NavColumn("name", "nvarchar(50)") },
        };
        var shipCustomerId = ColumnText_NavColumn("ship_customer_id", "int", nullable: true);
        var billCustomerId = ColumnText_NavColumn("bill_customer_id", "int", nullable: true);
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                ColumnText_NavColumn("order_id", "int", primaryKey: true),
                shipCustomerId,
                billCustomerId,
            },
        };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships =
            {
                ColumnText_NavReference(customers, customerId, orders, shipCustomerId, type),
                ColumnText_NavReference(customers, customerId, orders, billCustomerId, type),
            },
        };
    }

    /// <summary>nodes が自分自身を 2 本（parent / origin）で参照する図</summary>
    private static ErDiagram ColumnText_SelfReferenceDiagram()
    {
        var nodeId = ColumnText_NavColumn("node_id", "int", primaryKey: true);
        var parentId = ColumnText_NavColumn("parent_id", "int", nullable: true);
        var originId = ColumnText_NavColumn("origin_id", "int", nullable: true);
        var nodes = new Entity { TableName = "nodes", Columns = { nodeId, parentId, originId } };

        return new ErDiagram
        {
            Entities = { nodes },
            Relationships =
            {
                ColumnText_NavReference(nodes, nodeId, nodes, parentId, RelationshipType.OneToMany),
                ColumnText_NavReference(nodes, nodeId, nodes, originId, RelationshipType.OneToMany),
            },
        };
    }

    /// <summary>外部キー列の名前を変えた 2 本のリレーションの図（役割名の決め方）</summary>
    private static ErDiagram ColumnText_RoleNameDiagram(string foreignKeyColumn)
    {
        var customerId = ColumnText_NavColumn("customer_id", "int", primaryKey: true);
        var customers = new Entity { TableName = "customers", Columns = { customerId } };
        var first = ColumnText_NavColumn(foreignKeyColumn, "int", nullable: true);
        var second = ColumnText_NavColumn("other_customer_id", "int", nullable: true);
        var orders = new Entity
        {
            TableName = "orders",
            Columns = { ColumnText_NavColumn("order_key", "int", primaryKey: true), first, second },
        };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships =
            {
                ColumnText_NavReference(
                    customers,
                    customerId,
                    orders,
                    first,
                    RelationshipType.OneToMany
                ),
                ColumnText_NavReference(
                    customers,
                    customerId,
                    orders,
                    second,
                    RelationshipType.OneToMany
                ),
            },
        };
    }

    /// <summary>QuickER 版 Repository・EF Core・インメモリ・EditModel・Mapper を全部出す構成（元のテストの <c>Generate</c>）</summary>
    private static CodeGenerationOptions ColumnText_NavigationOptions() =>
        new()
        {
            RootNamespace = NavigationNamespace,
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = true,
            IncludeDataAnnotations = true,
        };

    /// <summary>インメモリの Include が、2 本のナビゲーションをそれぞれ別の行へ結ぶ（元のテストのプローブ）</summary>
    private static GeneratedFile ColumnText_NavigationProbeSource() =>
        new()
        {
            FileName = "NavigationProbe.cs",
            Content = $$"""
                using System.Linq;

                namespace {{NavigationNamespace}};

                internal static class NavigationProbe
                {
                    public static string Run()
                    {
                        var store = new InMemoryDataStore();
                        store.Put(new CustomerEntity { CustomerId = 1, Name = "Ship" });
                        store.Put(new CustomerEntity { CustomerId = 2, Name = "Bill" });
                        store.Put(new OrderEntity { OrderId = 10, ShipCustomerId = 1, BillCustomerId = 2 });

                        var order = new InMemoryOrderRepository(store)
                            .Query()
                            .Include(o => o.ShipCustomer)
                            .Include(o => o.BillCustomer)
                            .FirstOrDefaultAsync()
                            .GetAwaiter()
                            .GetResult()!;
                        var customers = new InMemoryCustomerRepository(store)
                            .Query()
                            .Include(c => c.OrdersByShipCustomer)
                            .Include(c => c.OrdersByBillCustomer)
                            .ToListAsync()
                            .GetAwaiter()
                            .GetResult();
                        var ship = customers.Single(c => c.CustomerId == 1);

                        return $"{order.ShipCustomer!.Name}/{order.BillCustomer!.Name}/"
                            + $"{ship.OrdersByShipCustomer.Count}/{ship.OrdersByBillCustomer.Count}";
                    }
                }
                """,
        };

    private static ExecCheck ColumnText_NavigationProbe(string expected) =>
        new(
            "inmemory-include-follows-each-navigation",
            assembly =>
                ProbeKit.CallStatic(ProbeKit.Type(assembly, "NavigationProbe"), "Run")
                    is string actual
                && actual == expected
                    ? []
                    : [$"インメモリの Include の結果が期待 {expected} と違う"]
        );

    /// <summary>
    /// EF Core のモデルで、エンティティ <paramref name="dependent"/> の外部キーが <paramref name="pairs"/> の数だけあり、その対（子から親・親から子の
    /// ナビゲーション名。null なら見ない）が期待どおり
    /// </summary>
    private static ExecCheck ColumnText_ForeignKeys(
        string dependent,
        params (string? ToPrincipal, string? ToDependent)[] pairs
    ) =>
        new(
            "efcore-foreign-keys",
            assembly =>
                ColumnText_WithModel(
                    assembly,
                    model =>
                    {
                        var problems = new List<string>();
                        var type = model
                            .GetEntityTypes()
                            .SingleOrDefault(item => item.ClrType.Name == dependent);

                        if (type is null)
                        {
                            return [$"{dependent} がモデルに無い"];
                        }

                        var foreignKeys = type.GetForeignKeys().ToList();

                        if (foreignKeys.Count != pairs.Length)
                        {
                            problems.Add(
                                $"{dependent} の外部キーが {foreignKeys.Count} 本（期待 {pairs.Length} 本）"
                            );
                        }

                        foreach (var (toPrincipal, toDependent) in pairs)
                        {
                            if (
                                !foreignKeys.Any(foreignKey =>
                                    (
                                        toPrincipal is null
                                        || foreignKey.DependentToPrincipal?.Name == toPrincipal
                                    )
                                    && (
                                        toDependent is null
                                        || foreignKey.PrincipalToDependent?.Name == toDependent
                                    )
                                )
                            )
                            {
                                problems.Add(
                                    $"{dependent} に ({toPrincipal} ↔ {toDependent}) を結ぶ外部キーが無い"
                                );
                            }
                        }

                        return problems;
                    }
                )
        );

    /// <summary>EF Core のモデルで、エンティティ <paramref name="entity"/> のナビゲーションの名前の集合が <paramref name="names"/> と一致する</summary>
    private static ExecCheck ColumnText_NavigationNames(string entity, params string[] names) =>
        new(
            "efcore-navigation-names",
            assembly =>
                ColumnText_WithModel(
                    assembly,
                    model =>
                    {
                        var actual = model
                            .GetEntityTypes()
                            .Single(item => item.ClrType.Name == entity)
                            .GetNavigations()
                            .Select(navigation => navigation.Name)
                            .ToHashSet(StringComparer.Ordinal);

                        return actual.SetEquals(names)
                            ? []
                            : (IReadOnlyList<string>)
                                [
                                    $"{entity} のナビゲーションが [{string.Join(", ", actual.Order(StringComparer.Ordinal))}]（期待 [{string.Join(", ", names)}]）",
                                ];
                    }
                )
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_NavigationRows() =>
        [
            ColumnTextRow(
                "exec-navigation-role-names-two-relationships-one-to-many",
                NavigationClass,
                "TwoRelationshipsBetweenSameTables_UseRoleNames",
                () => ColumnText_ShipBillDiagram(RelationshipType.OneToMany),
                ColumnText_NavigationOptions(),
                code:
                [
                    Member(
                        "OrderEntity",
                        "ShipCustomer",
                        "CustomerEntity? ShipCustomer { get; set; }"
                    ),
                    Member(
                        "OrderEntity",
                        "BillCustomer",
                        "CustomerEntity? BillCustomer { get; set; }"
                    ),
                    Member(
                        "CustomerEntity",
                        "OrdersByShipCustomer",
                        "ICollection<OrderEntity> OrdersByShipCustomer { get; set; }"
                    ),
                    Member(
                        "CustomerEntity",
                        "OrdersByBillCustomer",
                        "ICollection<OrderEntity> OrdersByBillCustomer { get; set; }"
                    ),
                ],
                extraSources: [ColumnText_NavigationProbeSource()],
                exec:
                [
                    ColumnText_NavigationProbe("Ship/Bill/1/0"),
                    ColumnText_NavigationNames("OrderEntity", "ShipCustomer", "BillCustomer"),
                    ColumnText_ForeignKeys(
                        "OrderEntity",
                        ("ShipCustomer", "OrdersByShipCustomer"),
                        ("BillCustomer", "OrdersByBillCustomer")
                    ),
                ]
            ),
            ColumnTextRow(
                "exec-navigation-role-names-two-relationships-one-to-one",
                NavigationClass,
                "TwoOneToOneRelationshipsBetweenSameTables_UseSingularRoleNames",
                () => ColumnText_ShipBillDiagram(RelationshipType.OneToOne),
                ColumnText_NavigationOptions(),
                compile: true,
                code:
                [
                    Member(
                        "CustomerEntity",
                        "OrderByShipCustomer",
                        "OrderEntity? OrderByShipCustomer { get; set; }"
                    ),
                    Member(
                        "CustomerEntity",
                        "OrderByBillCustomer",
                        "OrderEntity? OrderByBillCustomer { get; set; }"
                    ),
                    Member(
                        "OrderEntity",
                        "ShipCustomer",
                        "CustomerEntity? ShipCustomer { get; set; }"
                    ),
                ],
                exec: [ColumnText_ForeignKeys("OrderEntity", (null, null), (null, null))]
            ),
            ColumnTextRow(
                "exec-navigation-role-names-two-self-references",
                NavigationClass,
                "TwoSelfReferences_UseRoleNames",
                ColumnText_SelfReferenceDiagram,
                ColumnText_NavigationOptions(),
                code:
                [
                    Member(
                        "NodeEntity",
                        "NodesByParent",
                        "ICollection<NodeEntity> NodesByParent { get; set; }"
                    ),
                    Member(
                        "NodeEntity",
                        "NodesByOrigin",
                        "ICollection<NodeEntity> NodesByOrigin { get; set; }"
                    ),
                ],
                exec: [ColumnText_ForeignKeys("NodeEntity", (null, null), (null, null))]
            ),
            .. new[]
            {
                ("ship_customer_id", "ShipCustomer"),
                ("ShipCustomerID", "ShipCustomer"),
                ("shipper", "ShipperCustomer"),
                ("id", "IdCustomer"),
            }.Select(item =>
                ColumnTextRow(
                    $"code-navigation-role-name-follows-foreign-key-column-{item.Item1.ToLowerInvariant().Replace('_', '-')}",
                    NavigationClass,
                    $"RoleName_FollowsForeignKeyColumn(\"{item.Item1}\", \"{item.Item2}\")",
                    () => ColumnText_RoleNameDiagram(item.Item1),
                    ColumnText_NavigationOptions(),
                    code:
                    [
                        Member(
                            "OrderEntity",
                            item.Item2,
                            $"CustomerEntity? {item.Item2} {{ get; set; }}"
                        ),
                        Member(
                            "CustomerEntity",
                            "OrdersBy" + item.Item2,
                            $"ICollection<OrderEntity> OrdersBy{item.Item2} {{ get; set; }}"
                        ),
                        Member(
                            "OrderEntity",
                            "OtherCustomer",
                            "CustomerEntity? OtherCustomer { get; set; }"
                        ),
                    ]
                )
            ),
        ];

    // ===== 分割生成の固定 infra とスキーマ依存物（SplitRuntimeSymmetryTests） =====

    private const string SplitSymmetryClass = "SplitRuntimeSymmetryTests";

    /// <summary>型が宣言を持つファイルが <paramref name="file"/>（ファイル名の完全一致。<paramref name="prefix"/> なら前方一致）か</summary>
    private static bool ColumnText_DeclaredIn(
        INamedTypeSymbol type,
        string file,
        bool prefix = false
    ) =>
        type.DeclaringSyntaxReferences.Any(reference =>
            prefix
                ? CodeRuleContext
                    .FileName(reference.SyntaxTree)
                    .StartsWith(file, StringComparison.Ordinal)
                : CodeRuleContext.FileName(reference.SyntaxTree) == file
        );

    private static class ColumnTextFixedTypes
    {
        /// <summary>アセンブリ（行の生成物のコンパイル）ごとの集合</summary>
        public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            IAssemblySymbol,
            HashSet<(string Name, int Arity)>
        > Cache = new();
    }

    /// <summary>固定 infra のファイル（<c>Runtime.*</c>）に宣言されたトップレベルの型の（名前・型引数の数）の集合（アセンブリごとに 1 回だけ作る）</summary>
    private static HashSet<(string Name, int Arity)> ColumnText_FixedTypes(
        IAssemblySymbol assembly
    ) =>
        ColumnTextFixedTypes.Cache.GetValue(
            assembly,
            _ =>
            {
                var names = new HashSet<(string Name, int Arity)>();
                var pending = new Stack<INamespaceSymbol>([assembly.GlobalNamespace]);

                while (pending.Count > 0)
                {
                    var current = pending.Pop();

                    foreach (var child in current.GetNamespaceMembers())
                    {
                        pending.Push(child);
                    }

                    foreach (
                        var type in current
                            .GetTypeMembers()
                            .Where(type => ColumnText_DeclaredIn(type, "Runtime.", prefix: true))
                    )
                    {
                        names.Add((type.Name, type.Arity));
                    }
                }

                return names;
            }
        );

    /// <summary>
    /// 固定 infra のファイル（<c>Runtime.*</c>）とスキーマ依存のファイルが宣言するトップレベルの型の集合（名前・型引数の数）が排他：
    /// スキーマ依存のファイルにだけ宣言された型で、固定 infra と同じ名前・同じ型引数の数のものが無い
    /// </summary>
    private static CodeRule ColumnText_FixedAndSchemaTypesAreDisjoint() =>
        NoTypes(
            "固定 infra（Runtime.*）と同じ名前・型引数の数の型を、スキーマ依存のファイルが宣言する",
            type =>
                type.ContainingType is null
                && ColumnText_DeclaredIn(type, "Runtime.", prefix: true) is false
                && ColumnText_FixedTypes(type.ContainingAssembly).Contains((type.Name, type.Arity))
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_SplitSymmetryRows()
    {
        var fixedFiles = new[] { "Runtime.g.cs", "Runtime.SqlServer.g.cs", "Runtime.Sqlite.g.cs" };

        return
        [
            ColumnTextRow(
                "code-split-sync-fixed-engine-and-per-entity-code",
                SplitSymmetryClass,
                "SplitSyncFiles_ShouldSeparateFixedEngineFromPerEntityCode",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    SplitFilesByCategory = true,
                },
                code:
                [
                    Type(
                        "JournalingRepositoryBase<TEntity, TKey>",
                        file: "Runtime.Sync.g.cs",
                        kind: "class",
                        modifiers: "public abstract"
                    ),
                    NoTypes(
                        "Runtime.Sync.g.cs に宣言された Journaling で始まる sealed な型",
                        type =>
                            ColumnText_DeclaredIn(type, "Runtime.Sync.g.cs")
                            && type.IsSealed
                            && type.Name.StartsWith("Journaling", StringComparison.Ordinal)
                    ),
                    NoTypes(
                        "Runtime.Sync.g.cs に宣言され、AddGeneratedSyncSupport を持つ",
                        type =>
                            ColumnText_DeclaredIn(type, "Runtime.Sync.g.cs")
                            && type.GetMembers("AddGeneratedSyncSupport")
                                .Any(member => !member.IsImplicitlyDeclared)
                    ),
                    NoUsing("Runtime.Sync.g.cs", "Microsoft.Extensions.DependencyInjection"),
                    NoReferencesTo("Runtime.Sync.g.cs", "Microsoft.Extensions.DependencyInjection"),
                    NoTypes(
                        "Repositories.Sync.g.cs に宣言された SyncEngine・SyncJournal で始まるクラス",
                        type =>
                            ColumnText_DeclaredIn(type, "Repositories.Sync.g.cs")
                            && type.TypeKind == TypeKind.Class
                            && (
                                type.Name.StartsWith("SyncEngine", StringComparison.Ordinal)
                                || type.Name.StartsWith("SyncJournal", StringComparison.Ordinal)
                            )
                    ),
                ]
            ),
            ColumnTextRow(
                "code-split-runtime-files-hold-no-schema-dependent-types",
                SplitSymmetryClass,
                "SplitRuntimeFiles_ShouldNotContainSchemaDependentTypes",
                Columns_SplitRuntimeDiagram,
                Columns_SplitRuntimeMultiDialectOptions(),
                fileRules:
                [
                    new FileRule(".g.cs", Prefix: "Runtime.", NonEmpty: true, AnyDirectory: true),
                ],
                code:
                [
                    Type("CustomerEntity", kind: "class"),
                    ColumnText_FixedAndSchemaTypesAreDisjoint(),
                    NoTypes(
                        "固定 infra（Runtime.*）に宣言された ServiceCollectionExtensions で終わる型",
                        type =>
                            ColumnText_DeclaredIn(type, "Runtime.", prefix: true)
                            && type.Name.EndsWith(
                                "ServiceCollectionExtensions",
                                StringComparison.Ordinal
                            )
                    ),
                    .. fixedFiles.Select(file =>
                        NoReferencesTo(file, "Microsoft.Extensions.DependencyInjection")
                    ),
                ]
            ),
        ];
    }

    // ===== パッケージ参照モード（RuntimePackageModeCompilationTests） =====

    /// <summary>
    /// 代表構成×分割の生成物に、固定 infra の定義（パッケージが提供する <c>*Core</c> の型）が出ない。構成は元のテストと同じ 28 通りで、
    /// パッケージ参照の決まった行（<c>compile-package-*</c>）と同じ図・オプションを使う（行ごとにその行のコンパイルは掛けず、宣言の期待だけを足す）
    /// </summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_PackageModeRows() =>
        CompilePackageRows()
            .Select(row =>
                row with
                {
                    Id = "code-package-mode-no-fixed-infra-" + row.Id["compile-package-".Length..],
                    Description = row.Description.Replace(
                        "Generate_PackageMode_CompilesWithGuidedPackagesOnly",
                        "Generate_PackageMode_EmitsNoFixedInfraDefinitions"
                    ),
                    Input = row.Input with
                    {
                        Compile = false,
                        Code =
                        [
                            NoType("EntityBaseCore"),
                            NoType("IRepositoryCore"),
                            NoType("SqlServerRepositoryCore"),
                            NoType("SqliteRepositoryCore"),
                            NoType("InMemoryDataStore"),
                        ],
                    },
                }
            );

    // ===== 符号なし整数の値オブジェクト（ValueObjectUnsignedOrderingTests） =====

    /// <summary>ValueObjectUnsignedOrderingTests.BuildDiagram の図（符号なし整数の主キーの 3 表と、それぞれの主キーを大小比較する DSL クエリ）</summary>
    private static ErDiagram ColumnText_UnsignedDiagram()
    {
        var diagram = new ErDiagram();

        foreach (
            var (table, key, dataType) in new[]
            {
                ("shorts", "short_id", "smallint unsigned"),
                ("ints", "int_id", "int unsigned"),
                ("longs", "long_id", "bigint unsigned"),
            }
        )
        {
            var entity = new Entity
            {
                TableName = table,
                Columns =
                [
                    new Column
                    {
                        Name = key,
                        DataType = dataType,
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                ],
            };

            diagram.Entities.Add(entity);
            diagram.Queries.Add(
                new QueryDefinition
                {
                    EntityId = entity.Id,
                    Name = "GetAboveTen",
                    Returns = QueryReturnShape.List,
                    Condition = $"{key} > 10",
                }
            );
        }

        return diagram;
    }

    /// <summary>比較演算子と <c>IComparable</c> を静的に使う検証コード（元のテストのプローブ）</summary>
    private static GeneratedFile ColumnText_UnsignedProbeSource() =>
        new()
        {
            FileName = "UnsignedOrderingProbe.cs",
            Content = """
                namespace Sample.Domain;

                internal static class UnsignedOrderingProbe
                {
                    public static bool Compare() =>
                        ShortIdValue.Create(1) < ShortIdValue.Create(2)
                        && IntIdValue.Create(1u) <= IntIdValue.Create(2u)
                        && LongIdValue.Create(3ul) > LongIdValue.Create(2ul)
                        && ((System.IComparable<IntIdValue>)IntIdValue.Create(1u)).CompareTo(IntIdValue.Create(2u)) < 0;
                }
                """,
        };

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_UnsignedRows() =>
        [
            ColumnTextRow(
                "exec-unsigned-value-objects-have-ordering-operators",
                "ValueObjectUnsignedOrderingTests",
                "UnsignedValueObjects_HaveOrderingOperators",
                ColumnText_UnsignedDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateValueObjects = true,
                    GenerateInMemoryRepositories = true,
                    GenerateRepositories = false,
                },
                // 生成の入口は型トークンを付けるので、元の生成（トークンなし）と違い、綴りが戻らない列の Info が 1 種類出る。警告・エラーは出ない
                diagnostics: DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.Info(
                        nameof(Strings.CodeGen_Info_NonCanonicalTypeSpellingColumns)
                    ),
                ]),
                code:
                [
                    Type("ShortIdValue", baseType: "ValueObjectOrderedBase<ShortIdValue, ushort>"),
                    Type("IntIdValue", baseType: "ValueObjectOrderedBase<IntIdValue, uint>"),
                    Type("LongIdValue", baseType: "ValueObjectOrderedBase<LongIdValue, ulong>"),
                    Member("InMemoryShortRepository", "GetAboveTenAsync"),
                    Member("InMemoryIntRepository", "GetAboveTenAsync"),
                    Member("InMemoryLongRepository", "GetAboveTenAsync"),
                ],
                extraSources: [ColumnText_UnsignedProbeSource()],
                exec:
                [
                    new ExecCheck(
                        "unsigned-ordering-operators",
                        assembly =>
                            ProbeKit.CallStatic(
                                ProbeKit.Type(assembly, "UnsignedOrderingProbe"),
                                "Compare"
                            )
                                is true
                                ? []
                                :
                                [
                                    "符号なし整数の値オブジェクトの比較演算子・IComparable が期待どおりでない",
                                ]
                    ),
                ],
                typeDialect: DialectNames.MySql
            ),
        ];

    // ===== GuidKey の値オブジェクトと外部キー（ValueObjectForeignKeyUnificationTests） =====

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_GuidKeyRows() =>
        [
            ColumnTextRow(
                "code-guid-key-value-object-shared-by-foreign-key",
                "ValueObjectForeignKeyUnificationTests",
                "GuidKeyCase_EmitsGuidKeyValueObjectSharedByForeignKey",
                CompileDiagrams.StringKey,
                CompileDiagrams.GuidKeyOptions(),
                code:
                [
                    Type("TenantCodeValue", baseType: "ValueObjectGuidKeyBase<TenantCodeValue>"),
                    Member(
                        "TenantMemberEntity",
                        "OwnerTenantCode",
                        "TenantCodeValue OwnerTenantCode { get; set; }"
                    ),
                    Member("ITenantRepository", "CodeEqualsAsync"),
                    Member("ITenantMemberRepository", "OwnerCodeEqualsAsync"),
                    Text_NoTypeDeclaring("CodeContainsAsync"),
                    Text_NoTypeDeclaring("OwnerCodeStartsWithAsync"),
                ]
            ),
        ];

    // ===== 方言非依存の可搬フィクスチャ（PortableFixtureDialectIndependenceTests） =====

    /// <summary>可搬フィクスチャの生成物のうち、方言を問わず宣言されるもの</summary>
    private static IReadOnlyList<CodeRule> ColumnText_PortableDeclarations() =>
        [
            Type(
                "CustomerEntity",
                kind: "class",
                modifiers: "public partial",
                baseType: "EntityBase"
            ),
            Type("OrderEntity", kind: "class", modifiers: "public partial", baseType: "EntityBase"),
            Type(
                "QuickErDbContext",
                kind: "class",
                modifiers: "public partial",
                baseType: "DbContext"
            ),
            Type("EfCoreCustomerRepository", kind: "class", modifiers: "public sealed partial"),
            Type("EfCoreOrderRepository", kind: "class", modifiers: "public sealed partial"),
            Type("ValueObjectTranslatorPlugin", kind: "class", modifiers: "internal sealed"),
        ];

    /// <summary>
    /// 可搬フィクスチャの図を別の方言（<paramref name="others"/>）の型マッパで解決して生成した全ファイルが、行の生成結果と
    /// 名前も内容も同じ（方言非依存の生成物は方言によらず同一）
    /// </summary>
    private static ParsedCheck ColumnText_SameAcrossDialects(
        params (PortableDialect Dialect, string Name)[] others
    ) =>
        new(
            "same-across-dialects",
            result =>
            {
                var problems = new List<string>();

                foreach (var (dialect, name) in others)
                {
                    var other = CodegenRunner.Generate(
                        PortableFixtureDefinition.Build(dialect),
                        name,
                        PortableFixtureDefinition.Options
                    );
                    var left = result.Files.ToDictionary(
                        file => file.FileName,
                        file => file.Content
                    );
                    var right = other.Files.ToDictionary(
                        file => file.FileName,
                        file => file.Content
                    );

                    if (!left.Keys.ToHashSet().SetEquals(right.Keys))
                    {
                        problems.Add($"{name}: ファイルの名前の集合が違う");
                        continue;
                    }

                    problems.AddRange(
                        left.Where(file => file.Value != right[file.Key])
                            .Select(file => $"{name}: {file.Key} の内容が違う")
                    );
                }

                return problems;
            }
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_PortableRows() =>
        [
            ColumnTextRow(
                "code-portable-fixture-identical-across-postgresql-mysql-oracle",
                "PortableFixtureDialectIndependenceTests",
                "GeneratedCode_IsIdentical_AcrossPortableDialects（PostgreSql・MySql・Oracle の一致と SQL Server 固有属性の不在）",
                () => PortableFixtureDefinition.Build(PortableDialect.PostgreSql),
                PortableFixtureDefinition.Options,
                code:
                [
                    .. ColumnText_PortableDeclarations(),
                    PropertiesWithAttribute("SqlColumnType", 0),
                ],
                parsed:
                [
                    ColumnText_SameAcrossDialects(
                        (PortableDialect.MySql, DialectNames.MySql),
                        (PortableDialect.Oracle, DialectNames.Oracle)
                    ),
                ],
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnTextRow(
                "code-portable-fixture-sqlserver-adds-sql-column-type",
                "PortableFixtureDialectIndependenceTests",
                "GeneratedCode_IsIdentical_AcrossPortableDialects（SQL Server は同じ宣言に SQL Server 固有属性を足した上位互換）",
                () => PortableFixtureDefinition.Build(PortableDialect.SqlServer),
                PortableFixtureDefinition.Options,
                code:
                [
                    .. ColumnText_PortableDeclarations(),
                    Attribute("CustomerEntity", "CustomerId", "SqlColumnType(SqlDbType.Int)"),
                ]
            ),
        ];

    /// <summary>
    /// 解決できない列ペアは読み飛ばす：リレーションの列ペアの親の側が図に無い列を指す図（親の表 customers は列を持たない）でも、
    /// 値オブジェクトの外部キーの型の統一・EF Core の関係の構成・ナビゲーションの名前・インメモリのシードがその組を読み飛ばし、
    /// 生成物がエラー 0・警告 0 でコンパイルでき、EF Core のモデルも組める（ルートの CLAUDE.md「リレーションの列対応の正本は ColumnPairs」の生成側）
    /// </summary>
    /// <remarks>
    /// 元のテストを持たない行：P3-4b のカバレッジの門で、この読み飛ばしの枝（<c>CSharpGenerationModelBuilder</c> の
    /// ValueObjects・EfCore・NavigationNames・SampleData）を通す行が無くなったので、意図して足した（P3-4a の行が偶然この形の図を使っていた）。
    /// </remarks>
    private static ScenarioRow<CodegenCase> ColumnText_DanglingColumnPairRow() =>
        new(
            "compile-dangling-column-pair-is-skipped",
            null,
            "解決できない列ペア（親の側が図に無い列を指す）は、VO の外部キーの統一・EF Core の関係・ナビゲーション名・インメモリのシードが読み飛ばし、生成物はコンパイルできる",
            new CodegenCase(
                ColumnText_DanglingColumnPairDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    GenerateEfCoreRepositories = true,
                    GenerateInMemoryRepositories = true,
                    GenerateValueObjects = true,
                },
                DialectNames.SqlServer,
                // 今の挙動：解決できない列ペアのリレーションはナビゲーションを作らずに名指しし、列の無い親の表は
                // 主キーの無い表として Repository と EF Core の DbContext から外して名指しする（警告 3 つだけ・エラー 0）
                DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_RelationshipKeyUnknown),
                        "customers → orders"
                    ),
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_RepositorySingleKeyOnly),
                        "customers"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_EfCoreKeylessTableExcluded),
                        "customers"
                    ),
                ]),
                FileRules: [new FileRule(".g.cs", NonEmpty: true)],
                Compile: true,
                // EF Core のモデルを組めること（外した customers へ関係の構成を出すと、モデルを組むときに落ちる）
                Probes: [CodegenProbe.EfCoreModel],
                ProbeFacts: new CodegenProbeFacts(
                    ModelFacts:
                    [
                        EfCoreModelFact.Absent("CustomerEntity"),
                        EfCoreModelFact.Present("OrderEntity"),
                        EfCoreModelFact.NoNavigations("OrderEntity"),
                    ]
                )
            )
        );

    /// <summary>
    /// 主キーの無い親の表を指すリレーションは、EF Core の関係の構成を出さない：列ペアの列は両方とも在るが、親の表 customers に主キーが無い図。
    /// customers は DbContext から外れるので、関係の構成を出すとモデルを組むときに落ちる（<c>CSharpGenerationModelBuilder.EfCore</c> の読み飛ばし）
    /// </summary>
    /// <remarks>元のテストを持たない行：<see cref="ColumnText_DanglingColumnPairRow"/> と同じ理由（P3-4b のカバレッジの門）で足した。</remarks>
    private static ScenarioRow<CodegenCase> ColumnText_KeylessParentRow() =>
        new(
            "compile-keyless-parent-relationship-is-skipped-in-efcore",
            null,
            "主キーの無い親の表を指すリレーション（列ペアの列は在る）は、EF Core の関係の構成を出さず、生成物はコンパイルでき EF Core のモデルも組める",
            new CodegenCase(
                ColumnText_KeylessParentDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateEfCoreRepositories = true,
                },
                DialectNames.SqlServer,
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_EfCoreKeylessTableExcluded),
                        "customers"
                    )
                ),
                FileRules: [new FileRule(".g.cs", NonEmpty: true)],
                Compile: true,
                Probes: [CodegenProbe.EfCoreModel],
                ProbeFacts: new CodegenProbeFacts(
                    ModelFacts:
                    [
                        EfCoreModelFact.Absent("CustomerEntity"),
                        EfCoreModelFact.Present("OrderEntity"),
                        EfCoreModelFact.NoNavigations("OrderEntity"),
                    ]
                )
            )
        );

    /// <summary>親の表 customers（主キーなし・列 code）1 対多 子の表 orders（order_id・customer_code）。列ペアは code → customer_code</summary>
    private static ErDiagram ColumnText_KeylessParentDiagram()
    {
        var code = Service_Column("code", "nvarchar(20)");
        var customers = Service_Entity("customers", code);
        var customerCode = Service_Column("customer_code", "nvarchar(20)", foreignKey: true);
        var orders = Service_Entity(
            "orders",
            Service_Column("order_id", "int", primaryKey: true),
            customerCode
        );

        return new ErDiagram
        {
            Entities = [customers, orders],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = [new(code.Id, customerCode.Id)],
                    Type = RelationshipType.OneToMany,
                },
            ],
        };
    }

    /// <summary>親の表 customers（列なし）1 対多 子の表 orders（order_id・customer_id）。列ペアの親の側は図に無い列の Id</summary>
    private static ErDiagram ColumnText_DanglingColumnPairDiagram()
    {
        var customers = new Entity { Id = Guid.NewGuid(), TableName = "customers" };
        var orderFk = Service_Column("customer_id", "int", foreignKey: true);
        var orders = Service_Entity(
            "orders",
            Service_Column("order_id", "int", primaryKey: true),
            orderFk
        );

        return new ErDiagram
        {
            Entities = [customers, orders],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = [new(Guid.NewGuid(), orderFk.Id)],
                    Type = RelationshipType.OneToMany,
                },
            ],
        };
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnTextRows() =>
        [
            ColumnText_DanglingColumnPairRow(),
            ColumnText_KeylessParentRow(),
            .. ColumnText_ComputedIdentityRows(),
            .. ColumnText_ArrayRows(),
            .. ColumnText_DisplayNameRows(),
            .. ColumnText_TypeRows(),
            .. ColumnText_SqliteRealRows(),
            .. ColumnText_UniqueNameRows(),
            .. ColumnText_EditModelRows(),
            .. ColumnText_RemoteContractRows(),
            .. ColumnText_MultiTargetRows(),
            .. ColumnText_RowVersionRows(),
            .. ColumnText_NavigationRows(),
            .. ColumnText_SplitSymmetryRows(),
            .. ColumnText_PackageModeRows(),
            .. ColumnText_UnsignedRows(),
            .. ColumnText_GuidKeyRows(),
            .. ColumnText_PortableRows(),
            .. ColumnText_SqliteDialectRows(),
            .. ColumnText_RestRows(),
        ];

    // ===== P3-4b フェーズ 3：残りのクラスの構造の部分（LayeredOutputTests・PostgreSqlArrayColumnGenerationTests・MultiTarget*・StoreGeneratedCapabilityGenerationTests） =====

    private const string LayeredClass = "LayeredOutputTests";

    private const string StoreGeneratedClass = "StoreGeneratedCapabilityGenerationTests";

    /// <summary>LayeredOutputTests.CreateDiagram（Order エンティティ 1 つの最小図）</summary>
    private static ErDiagram ColumnText_LayeredDiagram()
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

    private static CodeGenerationOptions ColumnText_LayeredBaseOptions() =>
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

    /// <summary>StoreGeneratedCapabilityGenerationTests.Diagram（rowversion 列を 1 本持つ最小の図）</summary>
    private static ErDiagram ColumnText_StoreGeneratedDiagram()
    {
        var entity = new Entity
        {
            Id = new Guid("c1000000-0000-0000-0000-000000000001"),
            TableName = "sync_items",
            Columns =
            {
                new Column
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000002"),
                    Name = "item_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000003"),
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram { TargetDbms = DialectNames.SqlServer, Entities = { entity } };
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_RestRows() =>
        [
            // --- LayeredOutputTests ---
            ColumnTextRow(
                "code-layered-fixed-infra-is-public-and-api-docs-follow-layers",
                LayeredClass,
                "Generate_Layered_KeepsContentIdenticalExceptNamespaces（層別：固定 infra が public・API リファレンスが層の名前空間）",
                ColumnText_LayeredDiagram,
                ColumnText_LayeredBaseOptions() with
                {
                    LayeredOutput = true,
                },
                code:
                [
                    Type(
                        "IncludeNode",
                        file: "Runtime.g.cs",
                        kind: "class",
                        modifiers: "public sealed"
                    ),
                ],
                markdown:
                [
                    // ファイル一覧表は実際の名前空間を載せるので、層への追従（Acme.App.Entities でなく Domain.Entities）がそのまま反映される
                    MarkdownRule.Cell(
                        "ApiDocs.g.md",
                        "File",
                        "Entities.g.cs",
                        "Namespace",
                        "Domain.Entities"
                    ),
                ]
            ),
            ColumnTextRow(
                "code-split-fixed-infra-is-internal",
                LayeredClass,
                "Generate_Layered_KeepsContentIdenticalExceptNamespaces（分割：固定 infra が internal）",
                ColumnText_LayeredDiagram,
                ColumnText_LayeredBaseOptions() with
                {
                    SplitFilesByCategory = true,
                },
                code:
                [
                    Type(
                        "IncludeNode",
                        file: "Runtime.g.cs",
                        kind: "class",
                        modifiers: "internal sealed"
                    ),
                ]
            ),
            // --- PostgreSqlArrayColumnGenerationTests ---
            ColumnTextRow(
                "code-postgresql-array-columns-validate-unique-passes-unchecked",
                ArrayClass,
                "Generate_ValidateUnique_PassesArrayColumnsAsUnchecked",
                Columns_PostgreSqlArrayUniqueDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: false) with
                {
                    GenerateRepositories = true,
                    GenerateEfCoreRepositories = false,
                },
                code:
                [
                    CollectionInitializer(
                        "ArticleEditModel",
                        "ValidateUniqueAsync",
                        "List<string>",
                        ["\"Tags\"", "\"Labels\"", "\"Codes\""]
                    ),
                ],
                typeDialect: DialectNames.PostgreSql
            ),
            // --- MultiTargetRepositoryGenerationTests ---
            ColumnTextRow(
                "code-multitarget-nonsplit-omits-own-namespace-using",
                MultiTargetClass,
                "MultiDialect_NonSplit_NestedRepositoryNamespace_OmitsOwnUsing",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                code: [NoUsing(AllFiles, "Sample.Domain")]
            ),
            ColumnTextRow(
                "code-multitarget-nonsplit-dialect-named-di-and-keyed-overloads",
                MultiTargetClass,
                "MultiDialect_NonSplit_EmitsDialectNamedDiAndKeyedOverloads",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                code:
                [
                    .. new[] { "SqlServer", "Sqlite" }.SelectMany<string, CodeRule>(dialect =>
                    {
                        var extensions = $"Generated{dialect}RepositoryServiceCollectionExtensions";
                        var method = $"AddGenerated{dialect}Repositories";

                        return
                        [
                            Type(extensions, kind: "class", modifiers: "public static"),
                            Member(
                                extensions,
                                method,
                                $"IServiceCollection {method}(this IServiceCollection services, string connectionString)",
                                modifiers: "public static"
                            ),
                            // keyed 版（object? serviceKey 付き）と AddKeyedSingleton（接続ファクトリ・SQL 実行器）
                            Member(
                                extensions,
                                method,
                                $"IServiceCollection {method}(this IServiceCollection services, object? serviceKey, string connectionString)",
                                modifiers: "public static"
                            ),
                            Calls(extensions, method, "AddKeyedSingleton<ISqlExecutor>", count: 1),
                            Calls(
                                extensions,
                                method,
                                "AddKeyedSingleton<ISqlConnectionFactory>",
                                count: 1
                            ),
                            // keyed の per-entity 登録は共有のローカル関数へ畳まれ、各エンティティは契約とコンストラクタだけを渡す
                            Calls(
                                extensions,
                                method,
                                "AddRepository<ICustomerRepository>",
                                count: 1
                            ),
                        ];
                    }),
                    // 廃止した単一方言用の DI クラスは出さない
                    NoType("GeneratedRepositoryServiceCollectionExtensions"),
                ]
            ),
            ColumnTextRow(
                "code-multitarget-split-ado-usings-placed-per-dialect-file",
                MultiTargetClass,
                "MultiDialect_Split_AdoUsingsPlacedPerDialectFile",
                CustomerOrderDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    SplitFilesByCategory = true,
                },
                code:
                [
                    // 契約ファイル（per-entity 契約）・方言中立の固定 infra には ADO 依存が一切出ない
                    NoUsingUnder("Repositories.g.cs", "Microsoft.Data"),
                    NoReferencesTo("Repositories.g.cs", "Microsoft.Data"),
                    NoUsingUnder("Runtime.g.cs", "Microsoft.Data"),
                    NoReferencesTo("Runtime.g.cs", "Microsoft.Data"),
                    Type(
                        "ICustomerRepository",
                        file: "Repositories.g.cs",
                        kind: "interface",
                        modifiers: "public partial"
                    ),
                    // 方言中立契約の固定 infra は Runtime.g.cs（＝コアパッケージ相当）にのみ出る
                    Type(
                        "IRepositoryCore<TEntity, TKey>",
                        file: "Runtime.g.cs",
                        kind: "interface",
                        modifiers: "public"
                    ),
                    Type("IRepositoryCore<TEntity, TKey>"),
                    // 現行名を名乗る拡張シムは契約ファイル側（スキーマ依存物と同居）にだけ出る（方言ファイルには再掲しない）
                    Type(
                        "IRepository<TEntity, TKey>",
                        file: "Repositories.g.cs",
                        kind: "interface",
                        modifiers: "public partial"
                    ),
                    Type("IRepository<TEntity, TKey>"),
                    // sqlserver ファイルには SqlClient のみ・sqlite ファイルには Sqlite のみ
                    Using("Repositories.SqlServer.g.cs", "Microsoft.Data.SqlClient"),
                    NoReferencesTo("Repositories.SqlServer.g.cs", "Microsoft.Data.Sqlite"),
                    Using("Repositories.SqlServer.g.cs", "Sample.Domain.Repositories"),
                    Using("Repositories.SqlServer.g.cs", "Sample.Domain.Runtime.SqlServer"),
                    Using("Runtime.SqlServer.g.cs", "Microsoft.Data.SqlClient"),
                    NoReferencesTo("Runtime.SqlServer.g.cs", "Microsoft.Data.Sqlite"),
                    Using("Repositories.Sqlite.g.cs", "Microsoft.Data.Sqlite"),
                    NoReferencesTo("Repositories.Sqlite.g.cs", "Microsoft.Data.SqlClient"),
                    Using("Repositories.Sqlite.g.cs", "Sample.Domain.Repositories"),
                    Using("Repositories.Sqlite.g.cs", "Sample.Domain.Runtime.Sqlite"),
                    Using("Runtime.Sqlite.g.cs", "Microsoft.Data.Sqlite"),
                    NoReferencesTo("Runtime.Sqlite.g.cs", "Microsoft.Data.SqlClient"),
                ]
            ),
            // --- MultiTargetRowVersionGenerationTests ---
            ColumnTextRow(
                "code-rowversion-multitarget-dialect-asymmetry-in-metadata",
                RowVersionClass,
                "Generate_RowVersionMultiTarget_DialectAsymmetryInMetadata（版ガード付き SQL の持ち主は SQL Server 実装だけ）",
                MultiTargetRowVersionFixtureDefinition.Build,
                MultiTargetRowVersionFixtureDefinition.Options,
                code:
                [
                    Member(
                        "EntitySaveMetadata",
                        "UpdateVersionedSql",
                        "string? UpdateVersionedSql { get; init; }",
                        ns: MultiTargetRowVersionFixtureDefinition.NamespaceName
                            + ".Repositories.SqlServer"
                    ),
                    NoMember(
                        "EntitySaveMetadata",
                        "UpdateVersionedSql",
                        ns: MultiTargetRowVersionFixtureDefinition.NamespaceName
                            + ".Repositories.Sqlite"
                    ),
                ]
            ),
            // --- StoreGeneratedCapabilityGenerationTests ---
            ColumnTextRow(
                "relation-store-generated-dialect-order-independent",
                StoreGeneratedClass,
                "InMemoryRuntime_RowVersionDoc_IsIndependentOfDialectOrder（方言の指定順を入れ替えても出力が同じ）",
                ColumnText_StoreGeneratedDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Hybrid",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                    GenerateInMemoryRepositories = true,
                    RepositoryDialects = [DialectNames.Sqlite, DialectNames.SqlServer],
                },
                relations: [CodegenRelation.DialectOrder]
            ),
        ];

    // ===== SqliteRepositoryDialectTests：SQLite 方言の Repository の宣言・依存の不在・実際の SQLite での実行 =====

    private const string SqliteDialectClass = "SqliteRepositoryDialectTests";

    /// <summary>SqliteRepositoryDialectTests.SampleDiagram：親（customers）1 対多 子（orders）の Include 検証用の最小の図</summary>
    private static ErDiagram ColumnText_SqliteSampleDiagram()
    {
        var customerKey = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customer = new Entity
        {
            TableName = "customers",
            Columns =
            {
                customerKey,
                new Column
                {
                    Name = "name",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "created_at",
                    DataType = "datetime2",
                    IsNullable = false,
                },
            },
        };
        var orderForeignKey = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var order = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                orderForeignKey,
            },
        };

        return new ErDiagram
        {
            Entities = { customer, order },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    ColumnPairs = { new(customerKey.Id, orderForeignKey.Id) },
                },
            },
        };
    }

    /// <summary>SqliteRepositoryDialectTests.GenerateSqlite：SQLite 方言の QuickER 版 Repository だけを出すオプション</summary>
    private static CodeGenerationOptions ColumnText_SqliteDialectOptions(
        bool valueObjects = false
    ) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.Sqlite],
            GenerateValueObjects = valueObjects,
        };

    /// <summary>
    /// 親 customers・子 orders の行を SQLite の Repository の上で、ページング（<c>LIMIT / OFFSET</c>）・日付部品（<c>strftime</c>）・マルチクエリの Include・
    /// 更新・削除まで動かす検証コード（<paramref name="valueObjects"/> が true なら主キーは値オブジェクト）。問題を 1 本の文字列にして返す（空なら問題なし）
    /// </summary>
    private static GeneratedFile ColumnText_SqliteSampleProbeSource(bool valueObjects)
    {
        string Id(string entity, int value) =>
            valueObjects ? $"{entity}IdValue.Create({value})" : value.ToString();

        var (c1, c2, c3) = (Id("Customer", 1), Id("Customer", 2), Id("Customer", 3));
        var (o10, o11, o12) = (Id("Order", 10), Id("Order", 11), Id("Order", 12));
        string Name(string value) =>
            valueObjects ? $"NameValue.Create(\"{value}\")" : $"\"{value}\"";
        string Created(int year, int month, int day) =>
            valueObjects
                ? $"CreatedAtValue.Create(new DateTime({year}, {month}, {day}))"
                : $"new DateTime({year}, {month}, {day})";

        // 日付部品の絞り込みは、値オブジェクトが <c>DateTime</c> を包む構成では書けない（<c>.Year</c> が無い）ので、包まない構成だけで動かす
        var dateParts = valueObjects
            ? ""
            : """
                        var in2026 = await customers.Query().Where(c => c.CreatedAt.Year == 2026).OrderBy(c => c.CustomerId).ToListAsync();

                        if (in2026.Count != 2)
                        {
                            problems.Add($"CreatedAt.Year == 2026 が {in2026.Count} 行（期待 2 行）");
                        }
                """;

        return new GeneratedFile
        {
            FileName = "SqliteSampleProbe.cs",
            Content = $$"""
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using System.Threading.Tasks;

                namespace Sample.Domain;

                public static class SqliteSampleProbe
                {
                    public static async Task<string> RunAsync(ICustomerRepository customers, IOrderRepository orders)
                    {
                        await customers.InsertAsync(new CustomerEntity { CustomerId = {{c1}}, Name = {{Name(
                    "a"
                )}}, CreatedAt = {{Created(2025, 1, 5)}} });
                        await customers.InsertAsync(new CustomerEntity { CustomerId = {{c2}}, Name = {{Name(
                    "b"
                )}}, CreatedAt = {{Created(2026, 3, 10)}} });
                        await customers.InsertAsync(new CustomerEntity { CustomerId = {{c3}}, Name = {{Name(
                    "c"
                )}}, CreatedAt = {{Created(2026, 7, 1)}} });
                        await orders.InsertAsync(new OrderEntity { OrderId = {{o10}}, CustomerId = {{c1}} });
                        await orders.InsertAsync(new OrderEntity { OrderId = {{o11}}, CustomerId = {{c2}} });
                        await orders.InsertAsync(new OrderEntity { OrderId = {{o12}}, CustomerId = {{c2}} });

                        var problems = new List<string>();

                        // ページング：2 件目だけを取る
                        var page = await customers.Query().OrderBy(c => c.CustomerId).Skip(1).Take(1).ToListAsync();

                        if (page.Count != 1 || !page[0].CustomerId.Equals({{c2}}))
                        {
                            problems.Add("Skip(1).Take(1) が 2 件目の 1 行を返さない");
                        }

                        // 日付部品：年で絞る
                {{dateParts}}

                        // マルチクエリの Include：子を親ごとに載せる
                        var withOrders = await customers.Query().Include(c => c.Orders).OrderBy(c => c.CustomerId).ToListAsync();
                        var counts = string.Join(",", withOrders.Select(c => c.Orders.Count));

                        if (counts != "1,2,0")
                        {
                            problems.Add($"Include の子の数が {counts}（期待 1,2,0）");
                        }

                        // 更新・削除
                        var first = (await customers.Query().OrderBy(c => c.CustomerId).Take(1).ToListAsync())[0];
                        first.Name = {{Name("renamed")}};

                        if (!await customers.UpdateAsync(first) || !(await customers.GetByIdAsync({{c1}}))!.Name.Equals({{Name(
                    "renamed"
                )}}))
                        {
                            problems.Add("UpdateAsync の結果が読み戻せない");
                        }

                        if (!await orders.DeleteAsync({{o10}}) || await orders.GetByIdAsync({{o10}}) is not null)
                        {
                            problems.Add("DeleteAsync で行が消えない");
                        }

                        return string.Join(" / ", problems);
                    }
                }
                """,
        };
    }

    /// <summary>customers・orders の図を SQLite で動かして、<see cref="ColumnText_SqliteSampleProbeSource"/> の検証コードを実行する</summary>
    private static ExecCheck ColumnText_SqliteSampleExec() =>
        new(
            "sqlite-paging-date-part-include-update-delete",
            assembly =>
                ProbeKit.WithSqlite(
                    assembly,
                    ColumnText_SqliteSampleDiagram(),
                    async (provider, _) =>
                    {
                        var result = (string)
                            ProbeKit.Await(
                                ProbeKit.CallStatic(
                                    ProbeKit.Type(assembly, "SqliteSampleProbe"),
                                    "RunAsync",
                                    provider.GetRequiredService(
                                        ProbeKit.Type(assembly, "ICustomerRepository")
                                    ),
                                    provider.GetRequiredService(
                                        ProbeKit.Type(assembly, "IOrderRepository")
                                    )
                                )
                            )!;

                        return result.Length == 0 ? [] : [result];
                    }
                )
        );

    /// <summary>
    /// 生成物の接続ファクトリ（<c>SqlConnectionFactory</c>）で開いた接続の <c>PRAGMA foreign_keys</c> と <c>PRAGMA busy_timeout</c> を、接続文字列ごとに読む
    /// （<c>Foreign Keys</c> を書かなければ 1・明示の False は 0・True は 1。busy_timeout は <c>Default Timeout</c>（秒）のミリ秒）
    /// </summary>
    private static ExecCheck ColumnText_SqliteConnectionFactoryPragmas() =>
        new(
            "sqlite-connection-factory-pragmas",
            assembly =>
            {
                var factoryType = ProbeKit.Type(assembly, "SqlConnectionFactory");
                var problems = new List<string>();

                foreach (
                    var (connectionString, foreignKeys, busyTimeout) in new[]
                    {
                        ("Data Source=:memory:", 1L, 30000L),
                        ("Data Source=:memory:;Foreign Keys=False", 0L, 30000L),
                        ("Data Source=:memory:;Foreign Keys=True;Default Timeout=7", 1L, 7000L),
                    }
                )
                {
                    var factory = ProbeKit.Create(factoryType, connectionString);
                    using var connection = (Microsoft.Data.Sqlite.SqliteConnection)
                        ProbeKit.Call(factory, "CreateConnection")!;
                    connection.Open();

                    long Pragma(string name)
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = $"PRAGMA {name}";
                        return (long)command.ExecuteScalar()!;
                    }

                    if (Pragma("foreign_keys") != foreignKeys)
                    {
                        problems.Add(
                            $"{connectionString} の foreign_keys が {Pragma("foreign_keys")}（期待 {foreignKeys}）"
                        );
                    }

                    if (Pragma("busy_timeout") != busyTimeout)
                    {
                        problems.Add(
                            $"{connectionString} の busy_timeout が {Pragma("busy_timeout")}（期待 {busyTimeout}）"
                        );
                    }
                }

                return problems;
            }
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnText_SqliteDialectRows() =>
        [
            ColumnTextRow(
                "code-sqlite-dialect-emits-sqlite-runtime",
                SqliteDialectClass,
                "SqliteDialect_ShouldEmitSqliteSpecificRuntime（SQLite 固有の型・ローダ・接続の宣言と、ページング・日付部品・Include を SQLite で実行）",
                ColumnText_SqliteSampleDiagram,
                ColumnText_SqliteDialectOptions(),
                typeDialect: DialectNames.Sqlite,
                code:
                [
                    Using("QuickEREntities.g.cs", "Microsoft.Data.Sqlite"),
                    Type(
                        "SqliteRepository<TEntity, TKey>",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    Type(
                        "CustomerRepository",
                        kind: "class",
                        baseType: "SqliteRepository<CustomerEntity, int>"
                    ),
                    Type(
                        "OrderRepository",
                        kind: "class",
                        baseType: "SqliteRepository<OrderEntity, int>"
                    ),
                    Type("IncludeLoader", kind: "class", modifiers: "internal sealed"),
                    Member("SqliteSqlQueryExecutor<TEntity>", "MaterializeRootsAsync"),
                    Member("EntitySaveMetadata", "MapEntityObject", count: 2),
                    Member(
                        "ISqlConnectionFactory",
                        "CreateConnection",
                        "SqliteConnection CreateConnection()"
                    ),
                    Member(
                        "SqlConnectionFactory",
                        "CreateConnection",
                        "SqliteConnection CreateConnection()",
                        modifiers: "public"
                    ),
                    Creates("SqliteRepositoryCore<TEntity, TKey>", "GetByIdAsync", "SqliteCommand"),
                ],
                extraSources: [ColumnText_SqliteSampleProbeSource(valueObjects: false)],
                exec: [ColumnText_SqliteSampleExec()]
            ),
            ColumnTextRow(
                "code-sqlite-dialect-has-no-sql-server-dependencies",
                SqliteDialectClass,
                "SqliteDialect_ShouldNotEmitSqlServerDependencies（SQL Server の型・参照・属性の不在と、FOR JSON・FETCH NEXT・OUTPUT INSERTED を使わず SQLite で実行できること）",
                ColumnText_SqliteSampleDiagram,
                ColumnText_SqliteDialectOptions(),
                typeDialect: DialectNames.Sqlite,
                code:
                [
                    NoUsingUnder(AllFiles, "Microsoft.Data.SqlClient"),
                    NoType("SqlServerRepository"),
                    NoType("JsonQueryPlanner"),
                    NoType("RowVersionConcurrency"),
                    NoType("RowVersionCollector"),
                    QueryText_NoIdentifierUse("SqlDbType"),
                    PropertiesWithAttribute("SqlColumnType", 0),
                ],
                compileWithout: ReducedReferences.WithoutSqlClient,
                extraSources: [ColumnText_SqliteSampleProbeSource(valueObjects: false)],
                exec: [ColumnText_SqliteSampleExec()]
            ),
            ColumnTextRow(
                "code-sqlite-dialect-with-value-objects-has-no-sql-server-dependencies",
                SqliteDialectClass,
                "SqliteDialect_WithValueObjects_ShouldNotEmitSqlServerDependencies（値オブジェクト有効：SQLite の参照だけでコンパイルでき、SQL Server の型・参照が無く、SQLite で実行できる）",
                ColumnText_SqliteSampleDiagram,
                ColumnText_SqliteDialectOptions(valueObjects: true),
                typeDialect: DialectNames.Sqlite,
                code:
                [
                    Using("QuickEREntities.g.cs", "Microsoft.Data.Sqlite"),
                    NoUsingUnder(AllFiles, "Microsoft.Data.SqlClient"),
                    NoType("SqlServerRepository"),
                    QueryText_NoIdentifierUse("SqlDbType"),
                ],
                compileWithout: ReducedReferences.WithoutSqlClient,
                extraSources: [ColumnText_SqliteSampleProbeSource(valueObjects: true)],
                exec: [ColumnText_SqliteSampleExec()]
            ),
            ColumnTextRow(
                "exec-sqlite-dialect-connection-factory-foreign-keys-default-and-bootstrap",
                SqliteDialectClass,
                "SqliteDialect_ShouldEnableForeignKeysByDefaultAndEmitSchemaBootstrap（接続ファクトリを実際に開いて PRAGMA を読む：Foreign Keys 未指定なら ON・明示は尊重・busy_timeout は Default Timeout。DDL 適用ヘルパーの宣言）",
                ColumnText_SqliteSampleDiagram,
                ColumnText_SqliteDialectOptions(),
                typeDialect: DialectNames.Sqlite,
                code:
                [
                    Member(
                        "SqlConnectionFactory",
                        "ApplyForeignKeysDefault",
                        "string ApplyForeignKeysDefault(string connectionString)",
                        modifiers: "private static"
                    ),
                    Calls("SqlConnectionFactory", ".ctor", "ApplyForeignKeysDefault", count: 1),
                    Creates(
                        "SqlConnectionFactory",
                        "CreateConnection",
                        "SqliteConnection",
                        ["_connectionString"]
                    ),
                    Type("SqliteSchemaBootstrap", kind: "class", modifiers: "public static"),
                    Member(
                        "SqliteSchemaBootstrap",
                        "ApplyDdlAsync",
                        modifiers: "public static async"
                    ),
                    NoType("SqlServerSchemaBootstrap"),
                ],
                exec: [ColumnText_SqliteConnectionFactoryPragmas()]
            ),
            ColumnTextRow(
                "code-sqlserver-dialect-has-no-sqlite-dependencies",
                SqliteDialectClass,
                "SqlServerDialect_ShouldNotEmitSqliteDependencies（SQL Server 方言の生成物に SQLite の型・参照が無く、SQL Server の基底・共有部品・DDL 適用ヘルパーがある。FOR JSON・OUTPUT INSERTED の SQL の断片は元に残す）",
                ColumnText_SqliteSampleDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                },
                code:
                [
                    NoUsingUnder(AllFiles, "Microsoft.Data.Sqlite"),
                    NoType("SqliteRepository"),
                    NoType("IncludeLoader"),
                    NoMember("SqlConnectionFactory", "ApplyForeignKeysDefault"),
                    Type(
                        "SqlServerRepository<TEntity, TKey>",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    Type("RowVersionConcurrency", kind: "class"),
                    Type("SqlServerSchemaBootstrap", kind: "class", modifiers: "public static"),
                ],
                compileWithout: new ReducedReferences(SqlClient: true, Sqlite: false, EfCore: true)
            ),
        ];
}
