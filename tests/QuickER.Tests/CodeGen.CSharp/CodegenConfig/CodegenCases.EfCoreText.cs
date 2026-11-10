using Microsoft.EntityFrameworkCore.Metadata;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges.CodeRule;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：EF Core のモデルの構成を生成テキストの文字列で判定していたテスト（<c>EfCore*Tests</c>）を、EF Core のモデルの事実
/// （<see cref="Judges.EfCoreModelFact"/>）と宣言の期待へ言い換えたもの
/// </summary>
internal static partial class CodegenCases
{
    /// <summary>EF Core のテストクラスの行を作る（説明は <paramref name="testClass"/> ＋ "." ＋ 元のメソッド名）</summary>
    private static ScenarioRow<CodegenCase> EfCoreTextRow(
        string id,
        string testClass,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        IReadOnlyList<CodeRule>? code = null,
        IReadOnlyList<EfCoreModelFact>? model = null,
        string typeDialect = DialectNames.SqlServer
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
                Probes: model is null ? null : [CodegenProbe.EfCoreModel],
                ProbeFacts: model is null ? null : new CodegenProbeFacts(ModelFacts: model),
                Code: code
            )
        );

    // ===== EfCoreNonUnicodeColumnTests =====

    /// <summary>EfCoreNonUnicodeColumnTests.BuildDiagram の図（主キーと、指定の型の列 2 本）</summary>
    private static ErDiagram EfCoreText_NonUnicodeDiagram(
        string targetDbms,
        string nonUnicodeType,
        string unicodeType
    ) =>
        new()
        {
            TargetDbms = targetDbms,
            Entities =
            [
                Service_Entity(
                    "items",
                    Service_Column(
                        "item_id",
                        targetDbms == DialectNames.Oracle ? "NUMBER(10)" : "int",
                        primaryKey: true
                    ),
                    Service_Column("code", nonUnicodeType),
                    Service_Column("title", unicodeType)
                ),
            ],
        };

    private static CodeGenerationOptions EfCoreText_NonUnicodeOptions(bool valueObjects) =>
        new()
        {
            RootNamespace = "Test.NonUnicode",
            GenerateEfCoreRepositories = true,
            GenerateValueObjects = valueObjects,
        };

    /// <summary>Code は IsUnicode()=false、Title は未指定（SQL Server・Oracle の行）</summary>
    private static ScenarioRow<CodegenCase> EfCoreText_NonUnicodeRow(
        string id,
        string description,
        string dialect,
        string nonUnicode,
        string unicode,
        bool valueObjects
    ) =>
        EfCoreTextRow(
            id,
            "EfCoreNonUnicodeColumnTests",
            description,
            () => EfCoreText_NonUnicodeDiagram(dialect, nonUnicode, unicode),
            EfCoreText_NonUnicodeOptions(valueObjects),
            model:
            [
                EfCoreModelFact.Property("ItemEntity", "Code", unicode: false),
                EfCoreModelFact.Property("ItemEntity", "Title", unicodeUnspecified: true),
            ],
            typeDialect: dialect
        );

    // ===== EF Core に教える列の型（HasColumnType） =====

    /// <summary>
    /// 列の型を教える型と教えない型を並べた図（PostgreSQL の date・timestamp・timetz・json・jsonb・xml と、既定の対応で書ける
    /// text・timestamptz・integer[]。配列は要素の列の型に [] を付ける。MySQL では timestamp・char(36)・json）
    /// </summary>
    private static ErDiagram EfCoreText_ColumnTypeDiagram(string targetDbms) =>
        new()
        {
            TargetDbms = targetDbms,
            Entities =
            [
                Service_Entity(
                    "items",
                    targetDbms == DialectNames.PostgreSql
                        ?
                        [
                            Service_Column("item_id", "integer", primaryKey: true),
                            Service_Column("d", "date"),
                            Service_Column("ts", "timestamp(6)"),
                            Service_Column("tz", "time with time zone"),
                            Service_Column("j", "json"),
                            Service_Column("jb", "jsonb"),
                            Service_Column("x", "xml"),
                            Service_Column("txt", "text"),
                            Service_Column("tstz", "timestamptz"),
                            Service_Column("da", "date[]"),
                            Service_Column("jba", "jsonb[]"),
                            Service_Column("ia", "integer[]"),
                        ]
                        :
                        [
                            Service_Column("item_id", "int", primaryKey: true),
                            Service_Column("ts", "timestamp(6)"),
                            Service_Column("g", "char(36)"),
                            Service_Column("j", "json"),
                        ]
                ),
            ],
        };

    /// <summary>
    /// 型マッパーが EF Core に教える列の型を持つ列だけに HasColumnType が出て、モデルの列の型になる（値オブジェクトの変換と並ぶ）。
    /// ほかの列とほかの方言では列の型を構成しない（可搬図の出力が方言に依らず同じことは code-portable-fixture-* の行が見る）
    /// </summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> EfCoreText_ColumnTypeRows() =>
        [
            .. new[] { false, true }.Select(valueObjects =>
                EfCoreTextRow(
                    valueObjects
                        ? "model-efcore-column-type-postgresql-vo"
                        : "model-efcore-column-type-postgresql",
                    "EfCoreColumnType",
                    $"PostgreSQL の既定の対応で書けない列にだけ列の型を構成する（値オブジェクト{(valueObjects ? "あり" : "なし")}）",
                    () => EfCoreText_ColumnTypeDiagram(DialectNames.PostgreSql),
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Test.EfCoreColumnType",
                        GenerateEfCoreRepositories = true,
                        GenerateValueObjects = valueObjects,
                    },
                    model:
                    [
                        EfCoreModelFact.Property("ItemEntity", "D", columnType: "date"),
                        EfCoreModelFact.Property(
                            "ItemEntity",
                            "Ts",
                            columnType: "timestamp without time zone"
                        ),
                        EfCoreModelFact.Property(
                            "ItemEntity",
                            "Tz",
                            columnType: "time with time zone"
                        ),
                        EfCoreModelFact.Property("ItemEntity", "J", columnType: "json"),
                        EfCoreModelFact.Property("ItemEntity", "Jb", columnType: "jsonb"),
                        EfCoreModelFact.Property("ItemEntity", "X", columnType: "xml"),
                        EfCoreModelFact.Property(
                            "ItemEntity",
                            "Txt",
                            columnType: EfCoreModelFact.NoColumnType
                        ),
                        EfCoreModelFact.Property(
                            "ItemEntity",
                            "Tstz",
                            columnType: EfCoreModelFact.NoColumnType
                        ),
                        EfCoreModelFact.Property(
                            "ItemEntity",
                            "ItemId",
                            columnType: EfCoreModelFact.NoColumnType
                        ),
                        EfCoreModelFact.Property("ItemEntity", "Da", columnType: "date[]"),
                        EfCoreModelFact.Property("ItemEntity", "Jba", columnType: "jsonb[]"),
                        EfCoreModelFact.Property(
                            "ItemEntity",
                            "Ia",
                            columnType: EfCoreModelFact.NoColumnType
                        ),
                    ],
                    typeDialect: DialectNames.PostgreSql
                )
            ),
            EfCoreTextRow(
                "model-efcore-column-type-mysql-none",
                "EfCoreColumnType",
                "MySQL の列には列の型を構成しない（timestamp・char(36) はプロパティの型をドライバの型へそろえて読む）",
                () => EfCoreText_ColumnTypeDiagram(DialectNames.MySql),
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.EfCoreColumnType",
                    GenerateEfCoreRepositories = true,
                },
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Ts",
                        columnType: EfCoreModelFact.NoColumnType
                    ),
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "G",
                        columnType: EfCoreModelFact.NoColumnType
                    ),
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "J",
                        columnType: EfCoreModelFact.NoColumnType
                    ),
                ],
                typeDialect: DialectNames.MySql
            ),
        ];

    // ===== EfCoreUniqueIndexGenerationTests =====

    /// <summary>EfCoreUniqueIndexGenerationTests.BuildDiagram の図（一意制約を 2 つ持つ表と、一意制約の無い表）</summary>
    private static ErDiagram EfCoreText_UniqueIndexDiagram()
    {
        var code = Service_Column("code", "nvarchar(20)");
        var groupNo = Service_Column("group_no", "int");
        var label = Service_Column("label", "nvarchar(50)", nullable: true);
        var items = Service_Entity(
            "items",
            Service_Column("item_id", "int", primaryKey: true),
            code,
            groupNo,
            label
        );
        items.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [code.Id] });
        // 列の宣言順と違う並び（label, group_no）を宣言する
        items.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_items_label_group",
                ColumnIds = [label.Id, groupNo.Id],
            }
        );

        return new ErDiagram
        {
            TargetDbms = DialectNames.SqlServer,
            Entities =
            [
                items,
                Service_Entity(
                    "notes",
                    Service_Column("note_id", "int", primaryKey: true),
                    Service_Column("body", "nvarchar(100)")
                ),
            ],
        };
    }

    private static CodeGenerationOptions EfCoreText_UniqueIndexOptions(bool valueObjects) =>
        new()
        {
            RootNamespace = "Test.UniqueIndex",
            GenerateEfCoreRepositories = true,
            GenerateValueObjects = valueObjects,
        };

    // ===== EfCoreValueGeneratedNeverTests =====

    /// <summary>EfCoreValueGeneratedNeverTests.BuildDiagram の図（主キーの形を変えた 1 表。単一列の主キーの行だけ使う）</summary>
    private static ErDiagram EfCoreText_ValueGeneratedDiagram(
        string keyType,
        bool identity,
        bool composite = false
    )
    {
        var key = Service_Column("item_id", keyType, primaryKey: true);
        key.IsIdentity = identity;

        return new ErDiagram
        {
            TargetDbms = DialectNames.SqlServer,
            Entities =
            [
                Service_Entity(
                    "items",
                    key,
                    Service_Column("branch_no", "int", primaryKey: composite),
                    Service_Column("title", "nvarchar(50)")
                ),
            ],
        };
    }

    /// <summary>単一列の主キーの行：ItemId の鎖に ValueGeneratedNever があり、呼び出しはそれ 1 つだけ（ほかの列には付かない）</summary>
    private static ScenarioRow<CodegenCase> EfCoreText_SingleColumnKeyRow(
        string id,
        string description,
        string keyType,
        bool identity,
        bool valueObjects
    ) =>
        EfCoreTextRow(
            id,
            "EfCoreValueGeneratedNeverTests",
            description,
            () => EfCoreText_ValueGeneratedDiagram(keyType, identity),
            EfCoreText_ValueGeneratedOptions(valueObjects),
            code:
            [
                PropertyChain(
                    "QuickErDbContext",
                    "OnModelCreating",
                    "Property",
                    "ItemEntity.ItemId",
                    ["ValueGeneratedNever"]
                ),
                Calls("QuickErDbContext", "OnModelCreating", "ValueGeneratedNever", count: 1),
            ],
            model:
            [
                EfCoreModelFact.Property(
                    "ItemEntity",
                    "ItemId",
                    valueGenerated: ValueGenerated.Never
                ),
            ]
        );

    private static CodeGenerationOptions EfCoreText_ValueGeneratedOptions(bool valueObjects) =>
        new()
        {
            RootNamespace = "Test.ValueGeneratedNever",
            GenerateEfCoreRepositories = true,
            GenerateValueObjects = valueObjects,
        };

    // ===== EfCoreSchemaQualifiedTableTests =====

    /// <summary>EfCoreSchemaQualifiedTableTests.Diagram の図（主キー 1 列だけのテーブルを 3 つ）</summary>
    private static ErDiagram EfCoreText_SchemaQualifiedDiagram(
        string orders,
        string lines,
        string notes
    ) =>
        new()
        {
            TargetDbms = DialectNames.SqlServer,
            Entities =
            [
                Service_Entity(orders, Service_Column("id", "int", primaryKey: true)),
                Service_Entity(lines, Service_Column("id", "int", primaryKey: true)),
                Service_Entity(notes, Service_Column("id", "int", primaryKey: true)),
            ],
        };

    private static CodeGenerationOptions EfCoreText_SchemaQualifiedOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEfCoreRepositories = true,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

    // ===== EfCoreEntitySaveMetadataTests =====

    /// <summary>EfCoreEntitySaveMetadataTests.InlineGeneration_EmitsMaterializerOnlyWithQuickErRepositories の図（SQLite 方言の 1 表）</summary>
    private static ErDiagram EfCoreText_SaveMetadataDiagram() =>
        new()
        {
            TargetDbms = DialectNames.Sqlite,
            Entities =
            [
                Service_Entity("items", Service_Column("item_id", "INTEGER", primaryKey: true)),
            ],
        };

    /// <summary>行の組み立て器は QuickER 版 Repository があるときだけ出る（有り：両メンバーがちょうどの数、無し：どの型にも無い）</summary>
    private static ScenarioRow<CodegenCase> EfCoreText_SaveMetadataRow(
        string id,
        bool quickErRepositories,
        bool efCore
    ) =>
        EfCoreTextRow(
            id,
            "EfCoreEntitySaveMetadataTests",
            $"InlineGeneration_EmitsMaterializerOnlyWithQuickErRepositories({quickErRepositories}, {efCore}, {quickErRepositories})",
            EfCoreText_SaveMetadataDiagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Materializer",
                GenerateRepositories = quickErRepositories,
                GenerateEfCoreRepositories = efCore,
            },
            code: quickErRepositories
                ?
                [
                    Type("EntitySaveMetadata"),
                    Member("EntitySaveMetadata", "BuildSelectMaterializer"),
                    Member("EntitySaveMetadata", "MapEntity", count: 2, modifiers: "public"),
                    Text_NoTypeDeclaring("CompileOnFirstUse"),
                ]
                :
                [
                    Type("EntitySaveMetadata"),
                    NoMember("EntitySaveMetadata", "BuildSelectMaterializer"),
                    NoMember("EntitySaveMetadata", "MapEntity"),
                    Text_NoTypeDeclaring("CompileOnFirstUse"),
                ],
            typeDialect: DialectNames.Sqlite
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> EfCoreTextRows() =>
        [
            .. EfCoreText_ColumnTypeRows(),
            // --- EfCoreNonUnicodeColumnTests ---
            EfCoreText_NonUnicodeRow(
                "model-efcore-non-unicode-varchar-sqlserver",
                "SqlServer_NonUnicodeColumn_IsDeclared(varchar(20), nvarchar(50), False)",
                DialectNames.SqlServer,
                "varchar(20)",
                "nvarchar(50)",
                valueObjects: false
            ),
            EfCoreText_NonUnicodeRow(
                "model-efcore-non-unicode-varchar-sqlserver-vo",
                "SqlServer_NonUnicodeColumn_IsDeclared(varchar(20), nvarchar(50), True)",
                DialectNames.SqlServer,
                "varchar(20)",
                "nvarchar(50)",
                valueObjects: true
            ),
            EfCoreText_NonUnicodeRow(
                "model-efcore-non-unicode-char-sqlserver",
                "SqlServer_NonUnicodeColumn_IsDeclared(char(5), nchar(5), False)",
                DialectNames.SqlServer,
                "char(5)",
                "nchar(5)",
                valueObjects: false
            ),
            EfCoreText_NonUnicodeRow(
                "model-efcore-non-unicode-text-sqlserver",
                "SqlServer_NonUnicodeColumn_IsDeclared(text, ntext, False)",
                DialectNames.SqlServer,
                "text",
                "ntext",
                valueObjects: false
            ),
            EfCoreText_NonUnicodeRow(
                "model-efcore-non-unicode-varchar-max-sqlserver-vo",
                "SqlServer_NonUnicodeColumn_IsDeclared(varchar(max), nvarchar(max), True)",
                DialectNames.SqlServer,
                "varchar(max)",
                "nvarchar(max)",
                valueObjects: true
            ),
            EfCoreText_NonUnicodeRow(
                "model-efcore-non-unicode-varchar2-oracle",
                "Oracle_NonUnicodeColumn_IsDeclared",
                DialectNames.Oracle,
                "VARCHAR2(20)",
                "NVARCHAR2(50)",
                valueObjects: false
            ),
            EfCoreTextRow(
                "model-efcore-unicode-unspecified-varchar-postgresql",
                "EfCoreNonUnicodeColumnTests",
                "PostgreSql_Varchar_IsUnicode",
                () => EfCoreText_NonUnicodeDiagram(DialectNames.PostgreSql, "varchar(20)", "text"),
                EfCoreText_NonUnicodeOptions(valueObjects: false),
                model:
                [
                    EfCoreModelFact.Property("ItemEntity", "Code", unicodeUnspecified: true),
                    EfCoreModelFact.Property("ItemEntity", "Title", unicodeUnspecified: true),
                ],
                typeDialect: DialectNames.PostgreSql
            ),
            EfCoreTextRow(
                "code-efcore-non-unicode-follows-max-length",
                "EfCoreNonUnicodeColumnTests",
                "NonUnicode_IsEmittedNextToMaxLength（値オブジェクトなし）",
                () =>
                    EfCoreText_NonUnicodeDiagram(
                        DialectNames.SqlServer,
                        "varchar(20)",
                        "nvarchar(50)"
                    ),
                EfCoreText_NonUnicodeOptions(valueObjects: false),
                code:
                [
                    PropertyChain(
                        "QuickErDbContext",
                        "OnModelCreating",
                        "Property",
                        "ItemEntity.Code",
                        ["HasMaxLength", "IsUnicode"]
                    ),
                ],
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Code",
                        maxLength: 20,
                        unicode: false,
                        providerClrType: EfCoreModelFact.NoConverter
                    ),
                ]
            ),
            EfCoreTextRow(
                "code-efcore-non-unicode-follows-max-length-vo",
                "EfCoreNonUnicodeColumnTests",
                "NonUnicode_IsEmittedNextToMaxLength（値オブジェクトあり）",
                () =>
                    EfCoreText_NonUnicodeDiagram(
                        DialectNames.SqlServer,
                        "varchar(20)",
                        "nvarchar(50)"
                    ),
                EfCoreText_NonUnicodeOptions(valueObjects: true),
                code:
                [
                    PropertyChain(
                        "QuickErDbContext",
                        "OnModelCreating",
                        "Property",
                        "ItemEntity.Code",
                        ["HasMaxLength", "IsUnicode", "HasConversion"]
                    ),
                ],
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "Code",
                        maxLength: 20,
                        unicode: false,
                        providerClrType: typeof(string)
                    ),
                ]
            ),
            // --- EfCoreKeylessTableModelTests ---
            EfCoreTextRow(
                "code-efcore-keyless-table-ignored",
                "EfCoreKeylessTableModelTests",
                "主キーなしテーブルは除外されモデルは使える（生成物の宣言の部分）",
                ExecutionKeylessDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.EfCoreKeyless",
                    GenerateEfCoreRepositories = true,
                },
                code:
                [
                    Calls("QuickErDbContext", "OnModelCreating", "Ignore<LogEntity>", count: 1),
                    Calls("QuickErDbContext", "OnModelCreating", "Entity<LogEntity>", count: 0),
                    NoMember("QuickErDbContext", "Logs"),
                    NoTypes(
                        "名前に EfCoreLogRepository を含む型",
                        type => type.Name.Contains("EfCoreLogRepository", StringComparison.Ordinal)
                    ),
                ],
                model: [EfCoreModelFact.Absent("LogEntity")]
            ),
            // --- EfCorePrincipalKeyModelTests ---
            EfCoreTextRow(
                "code-efcore-no-principal-key-when-parent-is-primary-key",
                "EfCorePrincipalKeyModelTests",
                "親列が主キーのFKにはHasPrincipalKeyを出さない",
                () => Service_CustomerOrderDiagram("customer_id", null, "customer_id"),
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.EfCorePk",
                    GenerateEfCoreRepositories = true,
                    GenerateRepositories = false,
                    GenerateEditModels = false,
                    GenerateMappers = false,
                },
                code:
                [
                    Calls("QuickErDbContext", "OnModelCreating", "HasPrincipalKey", count: 0),
                    Calls("QuickErDbContext", "OnModelCreating", "HasForeignKey", count: 1),
                ],
                model:
                [
                    EfCoreModelFact.SingleForeignKey(
                        "OrderEntity",
                        principalProperties: ["CustomerId"],
                        principalIsPrimaryKey: true
                    ),
                ]
            ),
            // --- EfCoreRawSqlValueConversionTests ---
            EfCoreTextRow(
                "code-efcore-only-raw-sql-converts-column-values",
                "EfCoreRawSqlValueConversionTests",
                "Generate_EfCoreOnlyWithoutValueObjects_ConvertsRawColumnValues",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "probes",
                                Service_Column("probe_id", "int", primaryKey: true)
                            ),
                        ],
                    },
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateEfCoreRepositories = true,
                    GenerateValueObjects = false,
                },
                code:
                [
                    CallArguments(
                        "EntitySaveMetadata",
                        "SetColumnValue",
                        "RawValueConverter.ConvertRaw",
                        ["value", "propertyType"]
                    ),
                    CallArguments(
                        "EntitySaveMetadata",
                        "SetColumnValue",
                        "PropertyInfo.SetValue",
                        ["entity", "value"],
                        count: 0
                    ),
                ]
            ),
            // --- EfCoreUniqueIndexGenerationTests ---
            EfCoreTextRow(
                "model-efcore-unique-constraints-one-unique-index-each",
                "EfCoreUniqueIndexGenerationTests",
                "UniqueConstraints_EmitOneUniqueIndexEach(False) / CompositeUniqueConstraint_FollowsDeclaredColumnOrder / TableWithoutUniqueConstraints_EmitsNoIndex",
                EfCoreText_UniqueIndexDiagram,
                EfCoreText_UniqueIndexOptions(valueObjects: false),
                model:
                [
                    EfCoreModelFact.Index("ItemEntity", ["Code"], unique: true, totalIndexes: 2),
                    EfCoreModelFact.Index("ItemEntity", ["Label", "GroupNo"], unique: true),
                    EfCoreModelFact.NoIndexes("NoteEntity"),
                ]
            ),
            EfCoreTextRow(
                "model-efcore-unique-constraints-one-unique-index-each-vo",
                "EfCoreUniqueIndexGenerationTests",
                "UniqueConstraints_EmitOneUniqueIndexEach(True) / ValueObjectColumns_EmitUniqueIndex",
                EfCoreText_UniqueIndexDiagram,
                EfCoreText_UniqueIndexOptions(valueObjects: true),
                model:
                [
                    EfCoreModelFact.Property("ItemEntity", "Code", providerClrType: typeof(string)),
                    EfCoreModelFact.Index("ItemEntity", ["Code"], unique: true, totalIndexes: 2),
                    EfCoreModelFact.Index("ItemEntity", ["Label", "GroupNo"], unique: true),
                ]
            ),
            // --- EfCoreValueGeneratedNeverTests ---
            EfCoreTextRow(
                "model-efcore-identity-key-left-to-database",
                "EfCoreValueGeneratedNeverTests",
                "IdentityKey_IsLeftToTheDatabase",
                () => EfCoreText_ValueGeneratedDiagram("int", identity: true),
                EfCoreText_ValueGeneratedOptions(valueObjects: false),
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "ItemId",
                        valueGenerated: ValueGenerated.OnAdd
                    ),
                ]
            ),
            EfCoreTextRow(
                "model-efcore-guid-key-left-to-ef-core",
                "EfCoreValueGeneratedNeverTests",
                "GuidKey_IsLeftToEfCore(False)",
                () => EfCoreText_ValueGeneratedDiagram("uniqueidentifier", identity: false),
                EfCoreText_ValueGeneratedOptions(valueObjects: false),
                model:
                [
                    EfCoreModelFact.Property(
                        "ItemEntity",
                        "ItemId",
                        valueGenerated: ValueGenerated.OnAdd
                    ),
                ]
            ),
            EfCoreText_SingleColumnKeyRow(
                "code-efcore-never-int-key",
                "SingleColumnKey_IsValueGeneratedNever(int, False)",
                "int",
                identity: false,
                valueObjects: false
            ),
            EfCoreText_SingleColumnKeyRow(
                "code-efcore-never-int-key-vo",
                "SingleColumnKey_IsValueGeneratedNever(int, True)",
                "int",
                identity: false,
                valueObjects: true
            ),
            EfCoreText_SingleColumnKeyRow(
                "code-efcore-never-bigint-key",
                "SingleColumnKey_IsValueGeneratedNever(bigint, False)",
                "bigint",
                identity: false,
                valueObjects: false
            ),
            EfCoreText_SingleColumnKeyRow(
                "code-efcore-never-string-key",
                "SingleColumnKey_IsValueGeneratedNever(nvarchar(20), False)",
                "nvarchar(20)",
                identity: false,
                valueObjects: false
            ),
            EfCoreTextRow(
                "code-efcore-never-not-emitted-guid-key-vo",
                "EfCoreValueGeneratedNeverTests",
                "GuidKey_IsLeftToEfCore（値オブジェクトあり）",
                () => EfCoreText_ValueGeneratedDiagram("uniqueidentifier", identity: false),
                EfCoreText_ValueGeneratedOptions(valueObjects: true),
                code:
                [
                    Calls("QuickErDbContext", "OnModelCreating", "ValueGeneratedNever", count: 0),
                ]
            ),
            EfCoreTextRow(
                "code-efcore-never-not-emitted-composite-key",
                "EfCoreValueGeneratedNeverTests",
                "CompositeKey_EmitsNothing",
                () => EfCoreText_ValueGeneratedDiagram("int", identity: false, composite: true),
                EfCoreText_ValueGeneratedOptions(valueObjects: false),
                code:
                [
                    Calls("QuickErDbContext", "OnModelCreating", "ValueGeneratedNever", count: 0),
                ]
            ),
            // --- EfCoreSchemaQualifiedTableTests ---
            EfCoreTextRow(
                "model-efcore-schema-qualified-table-split",
                "EfCoreSchemaQualifiedTableTests",
                "ToTable_SplitsSchemaQualifiedName",
                () =>
                    EfCoreText_SchemaQualifiedDiagram("sales.orders", "sales.line.items", "notes"),
                EfCoreText_SchemaQualifiedOptions(),
                model:
                [
                    EfCoreModelFact.Table("SalesOrderEntity", "orders", "sales"),
                    EfCoreModelFact.Table("SalesLineItemEntity", "line.items", "sales"),
                    EfCoreModelFact.Table("NoteEntity", "notes"),
                ]
            ),
            EfCoreTextRow(
                "model-efcore-schema-qualified-table-escapes-both-arguments",
                "EfCoreSchemaQualifiedTableTests",
                "ToTable_EscapesBothArguments",
                () => EfCoreText_SchemaQualifiedDiagram("q\"s.t\\x", "lines", "notes"),
                EfCoreText_SchemaQualifiedOptions(),
                model: [EfCoreModelFact.Table("QSTXEntity", "t\\x", "q\"s")]
            ),
            // --- NameTrustBoundaryGenerationTests（P3-6 で移した） ---
            new ScenarioRow<CodegenCase>(
                "model-efcore-quoted-names-escaped",
                null,
                "EF Core の ToTable / HasColumnName は名前をエスケープして出力する（NameTrustBoundaryGenerationTests.QuotedNames_AreEscapedInEfCoreFluent。EF Core 単独の構成でもコンパイルできる）",
                new CodegenCase(
                    Compile_QuotedNameDiagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Sample.Domain",
                        GenerateEditModels = true,
                        GenerateMappers = true,
                        GenerateEfCoreRepositories = true,
                        IncludeDataAnnotations = true,
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(),
                    Compile: true,
                    Probes: [CodegenProbe.EfCoreModel],
                    ProbeFacts: new CodegenProbeFacts(
                        ModelFacts:
                        [
                            EfCoreModelFact.Table("WeIrdCustomerEntity", "we\"ird\\customers"),
                            EfCoreModelFact.Table("OrDerEntity", "or\"ders"),
                            EfCoreModelFact.Property(
                                "WeIrdCustomerEntity",
                                "CusTomerId",
                                column: "cus\"tomer\\id"
                            ),
                            EfCoreModelFact.Property(
                                "WeIrdCustomerEntity",
                                "NaMe",
                                column: "na\"me"
                            ),
                            EfCoreModelFact.Property(
                                "OrDerEntity",
                                "OrDerId",
                                column: "or\"der_id"
                            ),
                            EfCoreModelFact.Property(
                                "OrDerEntity",
                                "CusTomerId",
                                column: "cus\"tomer\\id"
                            ),
                        ]
                    )
                )
            ),
            // --- EfCoreEntitySaveMetadataTests ---
            EfCoreText_SaveMetadataRow(
                "code-efcore-save-metadata-no-materializer-efcore-only",
                false,
                true
            ),
            EfCoreText_SaveMetadataRow(
                "code-efcore-save-metadata-materializer-quicker-only",
                true,
                false
            ),
            EfCoreText_SaveMetadataRow(
                "code-efcore-save-metadata-materializer-quicker-and-efcore",
                true,
                true
            ),
            EfCoreText_PrincipalColumnRow(
                "diag-efcore-non-unique-principal-column-warning",
                uniquePrincipal: false
            ),
            EfCoreText_PrincipalColumnRow(
                "diag-efcore-unique-principal-column-no-warning",
                uniquePrincipal: true
            ),
        ];

    // ===== 外部キーの参照先の列 =====

    /// <summary>
    /// 外部キーの参照先の列の図：<paramref name="uniquePrincipal"/> なら items.code（1 列の一意制約）を、そうでなければ
    /// order_lines の複合主キー（order_id, line_no）の 1 列 order_id を参照する。どちらの図も主キーを参照する外部キーを 1 本持つ
    /// </summary>
    private static ErDiagram EfCoreText_PrincipalColumnDiagram(bool uniquePrincipal)
    {
        var parentKey = Service_Column(
            uniquePrincipal ? "item_id" : "order_id",
            "int",
            primaryKey: true
        );
        var parentSecond = uniquePrincipal
            ? Service_Column("code", "nvarchar(20)")
            : Service_Column("line_no", "int", primaryKey: true);
        var parent = Service_Entity(
            uniquePrincipal ? "items" : "order_lines",
            parentKey,
            parentSecond
        );

        if (uniquePrincipal)
        {
            parent.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [parentSecond.Id] });
        }

        var referenced = uniquePrincipal ? parentSecond : parentKey;
        var childReference = Service_Column(
            uniquePrincipal ? "item_code" : "order_id",
            referenced.DataType,
            nullable: true,
            foreignKey: true
        );
        var child = Service_Entity(
            uniquePrincipal ? "item_tags" : "line_notes",
            Service_Column("child_id", "int", primaryKey: true),
            childReference
        );
        var keyReference = Service_Column("team_id", "int", nullable: true, foreignKey: true);
        var owners = Service_Entity("teams", Service_Column("team_id", "int", primaryKey: true));
        child.Columns.Add(keyReference);

        return new ErDiagram
        {
            TargetDbms = DialectNames.SqlServer,
            Entities = [parent, child, owners],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = [new(referenced.Id, childReference.Id)],
                },
                // 主キーを参照する外部キーは名指ししない
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = owners.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = [new(owners.Columns[0].Id, keyReference.Id)],
                },
            ],
        };
    }

    /// <summary>
    /// EF Core は一意でない列を参照先に持つ外部キーを表せない（HasPrincipalKey が一意とみなす）ので、参照先の列が 1 列の主キーでも
    /// 1 列の一意制約でもない外部キーを警告で名指しする（生成物は変えない）。1 列の一意制約と主キーを参照する外部キーは名指ししない
    /// </summary>
    private static ScenarioRow<CodegenCase> EfCoreText_PrincipalColumnRow(
        string id,
        bool uniquePrincipal
    ) =>
        new(
            id,
            null,
            uniquePrincipal
                ? "外部キーの参照先が 1 列の一意制約か主キーなら、参照先の列の警告を出さない"
                : "外部キーの参照先が複合主キーの 1 列（一意でない列）なら、EF Core の参照先の列の警告で名指しする",
            new CodegenCase(
                () => EfCoreText_PrincipalColumnDiagram(uniquePrincipal),
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.PrincipalColumn",
                    GenerateEfCoreRepositories = true,
                },
                DialectNames.SqlServer,
                uniquePrincipal
                    ? DiagnosticExpectation
                        .Contains()
                        .Without(
                            ExpectedDiagnostic.WarningAnyItems(
                                nameof(Strings.CodeGen_Warning_EfCoreNonUniquePrincipalColumn)
                            )
                        )
                    : DiagnosticExpectation.CountOf(
                        ExpectedDiagnostic.WarningMentioning(
                            nameof(Strings.CodeGen_Warning_EfCoreNonUniquePrincipalColumn),
                            "line_notes",
                            "order_lines"
                        ),
                        1
                    ),
                FileRules: [new FileRule(".g.cs", NonEmpty: true)],
                Compile: true
            )
        );
}
