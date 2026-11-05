using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges.CodeRule;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：<c>CSharpCodeGenerationServiceTests</c> の、生成テキストの文字列で判定していたテストを宣言の期待（<see cref="CodeRule"/>）・
/// EF Core のモデルの事実・参照を絞ったコンパイル・実行の探針（EditModel・Mapper・DI の振る舞いと SQLite での実行）へ言い換えたもの
/// </summary>
internal static partial class CodegenCases
{
    private static ScenarioRow<CodegenCase> TextRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        IReadOnlyList<CodeRule>? code = null,
        IReadOnlyList<EfCoreModelFact>? model = null,
        ReducedReferences? compileWithout = null,
        IReadOnlyDictionary<string, string>? fileNamespaces = null,
        string typeDialect = DialectNames.SqlServer,
        IReadOnlyList<FileRule>? fileRules = null,
        IReadOnlyList<ExecCheck>? exec = null,
        IReadOnlyList<GeneratedFile>? extraSources = null,
        DiagnosticExpectation? diagnostics = null
    ) =>
        new(
            id,
            null,
            ServiceClass + description,
            new CodegenCase(
                diagram,
                options,
                typeDialect,
                diagnostics ?? DiagnosticExpectation.Contains(),
                FileRules: fileRules,
                FileNamespaces: fileNamespaces,
                Probes: model is null ? null : [CodegenProbe.EfCoreModel],
                ProbeFacts: model is null ? null : new CodegenProbeFacts(ModelFacts: model),
                ExtraSources: extraSources,
                Code: code,
                CompileWithout: compileWithout,
                Exec: exec
            )
        );

    /// <summary>名前 <paramref name="member"/> のメンバーを宣言する型が 1 つも無い（メソッドの不在など）</summary>
    private static CodeRule Text_NoTypeDeclaring(string member) =>
        NoTypes(
            $"{member} を宣言する",
            type => type.GetMembers(member).Any(item => !item.IsImplicitlyDeclared)
        );

    /// <summary>型 <paramref name="typeText"/>（<see cref="CodeRule.Format"/> の表記）のプロパティ・フィールド・メソッドの戻り値・引数を持つ型が 1 つも無い</summary>
    private static CodeRule Text_NoMemberOfType(string typeText) =>
        NoTypes(
            $"{typeText} をメンバーの型に持つ",
            type =>
                type.GetMembers()
                    .Any(member =>
                        member switch
                        {
                            IPropertySymbol property => Text_IsType(property.Type, typeText),
                            IFieldSymbol field => Text_IsType(field.Type, typeText),
                            IMethodSymbol method => Text_IsType(method.ReturnType, typeText)
                                || method.Parameters.Any(parameter =>
                                    Text_IsType(parameter.Type, typeText)
                                ),
                            _ => false,
                        }
                    )
        );

    private static bool Text_IsType(ITypeSymbol type, string typeText) =>
        type.ToDisplayString(CodeRule.Format) == typeText;

    /// <summary>
    /// 出力に使われない名前空間オプションは不正でも生成を止めず、出力にも効かない：不正な値の生成物と、同じ場所へ正しい値を入れた生成物が、
    /// 全ファイル・診断とも同じ（関係 <see cref="CodegenRelation.OptionNoEffect"/>。不正な値が出力のどこかに現れれば違いになる）
    /// </summary>
    private static ScenarioRow<CodegenCase> UnusedNamespaceRow(
        string id,
        string scenario,
        CodeGenerationOptions invalid,
        CodeGenerationOptions valid
    ) =>
        new(
            id,
            null,
            ServiceClass
                + $"Generate_InvalidButUnusedNamespaceOption_ShouldSucceed(\"{scenario}\")",
            new CodegenCase(
                SingleEntityDiagram,
                invalid,
                DialectNames.SqlServer,
                DiagnosticExpectation.Contains(),
                FileRules: [new FileRule(".g.cs", NonEmpty: true, AnyDirectory: true)],
                Relations: [CodegenRelation.OptionNoEffect],
                ComparisonOptions: valid
            )
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> UnusedNamespaceRows()
    {
        var packageRuntime = ServiceRoot() with
        {
            SplitFilesByCategory = true,
            UseRuntimePackages = true,
            RuntimeNamespace = "123 bad",
        };
        var layeredRoot = new CodeGenerationOptions
        {
            RootNamespace = "123 bad",
            LayeredOutput = true,
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
        };
        var splitAllExplicitRoot = new CodeGenerationOptions
        {
            RootNamespace = "123 bad",
            SplitFilesByCategory = true,
            RuntimeNamespace = "Sample.Runtime",
            EntityNamespace = "Sample.Entities",
            EditModelNamespace = "Sample.EditModels",
            MapperNamespace = "Sample.Mappers",
            RepositoryNamespace = "Sample.Repositories",
            ValueObjectNamespace = "Sample.ValueObjects",
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = true,
        };

        return
        [
            UnusedNamespaceRow(
                "relation-unused-invalid-namespace-package-runtime",
                "package-runtime",
                packageRuntime,
                packageRuntime with
                {
                    RuntimeNamespace = "Sample.UnusedRuntime",
                }
            ),
            UnusedNamespaceRow(
                "relation-unused-invalid-namespace-layered-root",
                "layered-root",
                layeredRoot,
                layeredRoot with
                {
                    RootNamespace = "Sample.UnusedRoot",
                }
            ),
            UnusedNamespaceRow(
                "relation-unused-invalid-namespace-split-all-explicit-root",
                "split-all-explicit-root",
                splitAllExplicitRoot,
                splitAllExplicitRoot with
                {
                    RootNamespace = "Sample.UnusedRoot",
                }
            ),
        ];
    }

    private static IEnumerable<ScenarioRow<CodegenCase>> TextRows() =>
        [
            .. UnusedNamespaceRows(),
            .. TextEditModelRows(),
            .. TextRepositoryRows(),
            .. TextValueObjectAndEfCoreRows(),
            TextRow(
                "code-single-file-entity-editmodel-attributes",
                "Generate_ShouldCreateSingleGeneratedFileWithEntityAndEditModel",
                Service_CustomersDiagram,
                ServiceRoot(),
                code:
                [
                    Using(AllFiles, "System.ComponentModel.DataAnnotations"),
                    Using(AllFiles, "System.ComponentModel.DataAnnotations.Schema"),
                    Type("CustomerEntity", kind: "class", modifiers: "public partial"),
                    Type(
                        "CustomerEditModel",
                        kind: "class",
                        modifiers: "public partial",
                        baseType: "EditModelBase<CustomerEditModel>"
                    ),
                    Type("EditModelBaseCore", kind: "class", modifiers: "public abstract"),
                    Attribute("CustomerEntity", null, "Table(\"customers\")"),
                    Attribute("CustomerEntity", "CustomerId", "Key"),
                    Attribute("CustomerEntity", "Name", "MaxLength(100)"),
                    Member(
                        "CustomerEditModel",
                        "BindingName",
                        "string BindingName { get; set; }",
                        modifiers: "public"
                    ),
                ],
                fileNamespaces: new Dictionary<string, string>
                {
                    ["QuickEREntities.g.cs"] = "Sample.Domain",
                }
            ),
            TextRow(
                "code-snake-case-table-names-pascal-entity-names",
                "Generate_ShouldConvertSnakeCaseTableNamesToPascalCaseEntityNames",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "airconditioner_category",
                                Service_Column(
                                    "airconditioner_category_id",
                                    "int",
                                    primaryKey: true
                                )
                            ),
                        ],
                    },
                ServiceRoot(),
                code:
                [
                    Type(
                        "AirconditionerCategoryEntity",
                        kind: "class",
                        modifiers: "public partial"
                    ),
                    Type(
                        "AirconditionerCategoryEditModel",
                        kind: "class",
                        modifiers: "public partial"
                    ),
                    Type(
                        "AirconditionerCategoryMapper",
                        kind: "class",
                        modifiers: "public sealed partial"
                    ),
                ]
            ),
            TextRow(
                "code-many-to-many-relationship-leaves-no-collection-navigation",
                "Generate_ShouldWarnAndSkipManyToManyRelationship",
                Service_ManyToManyDiagram,
                ServiceRoot(),
                code:
                [
                    Type("UserEntity", kind: "class"),
                    Type("RoleEntity", kind: "class"),
                    Text_NoMemberOfType("ICollection<RoleEntity>"),
                ]
            ),
            TextRow(
                "code-sql-column-type-absent-without-repository-and-annotations",
                "Generate_SqlColumnTypeAttribute_IsGatedOnRepositoryOrDataAnnotations（両方 OFF）",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    IncludeDataAnnotations = false,
                },
                code:
                [
                    Type("ItemEntity", kind: "class"),
                    NoType("SqlColumnTypeAttribute"),
                    PropertiesWithAttribute("SqlColumnType", 0),
                ]
            ),
            TextRow(
                "code-sql-column-type-present-with-repository-and-annotations",
                "Generate_SqlColumnTypeAttribute_IsGatedOnRepositoryOrDataAnnotations（Repository ON）",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                    IncludeDataAnnotations = true,
                },
                code: [Attribute("ItemEntity", "ItemId", "SqlColumnType(SqlDbType.Int)")]
            ),
            TextRow(
                "code-sql-column-type-present-with-annotations-only",
                "Generate_SqlColumnTypeAttribute_IsGatedOnRepositoryOrDataAnnotations（IncludeDataAnnotations のみ ON）",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    IncludeDataAnnotations = true,
                },
                code: [Attribute("ItemEntity", "ItemId", "SqlColumnType(SqlDbType.Int)")]
            ),
            TextRow(
                "code-entity-only-emits-no-editmodel-or-mapper",
                "Generate_EntityOnly_ShouldNotContainUiModelOrMapper",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateEditModels = false,
                    GenerateMappers = false,
                },
                code:
                [
                    Type("ItemEntity", kind: "class", modifiers: "public partial"),
                    NoType("ItemEditModel"),
                    NoType("ItemMapper"),
                ]
            ),
            TextRow(
                "code-many-entities-emit-first-and-last-entity",
                "Generate_ManyEntities_ShouldNotHitScribanLoopLimit",
                Text_ManyEntitiesDiagram,
                ServiceRoot() with
                {
                    GenerateEditModels = false,
                    GenerateMappers = false,
                    GenerateRepositories = false,
                },
                code:
                [
                    Type("Items1Entity", kind: "class", modifiers: "public partial"),
                    Type("Items1100Entity", kind: "class", modifiers: "public partial"),
                ]
            ),
            TextRow(
                "code-rowversion-column-marked-store-generated",
                "Generate_SingleRowVersionColumn_ShouldSucceed",
                () => Service_RowVersionDiagram("rowversion"),
                ServiceRoot(),
                code: [Attribute("ItemEntity", "RowVer", "StoreGeneratedColumn")]
            ),
            TextRow(
                "code-timestamp-column-marked-store-generated",
                "Generate_NonPrimaryKeyRowVersionColumn_ShouldSucceed",
                () => Service_RowVersionDiagram("timestamp"),
                ServiceRoot(),
                code: [Attribute("ItemEntity", "RowVer", "StoreGeneratedColumn")]
            ),
            TextRow(
                "code-non-primary-key-unbounded-binary-marked-with-exclusion",
                "Generate_NonPrimaryKeyUnboundedBinaryColumn_WithExclusion_ShouldSucceed",
                () => Service_UnboundedBinaryDiagram(binaryColumnIsPrimaryKey: false),
                ServiceRoot() with
                {
                    ExcludeUnboundedBinaryColumns = true,
                },
                code: [Attribute("AssetEntity", "Payload", "UnboundedBinaryColumn")]
            ),
            TextRow(
                "code-distinct-entity-class-names-both-declared",
                "Generate_DistinctEntityClassNames_ShouldSucceed",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "customer",
                                Service_Column("customer_id", "int", primaryKey: true)
                            ),
                            Service_Entity(
                                "customer_address",
                                Service_Column("customer_address_id", "int", primaryKey: true)
                            ),
                        ],
                    },
                ServiceRoot(),
                code:
                [
                    Type("CustomerEntity", kind: "class"),
                    Type("CustomerAddressEntity", kind: "class"),
                ]
            ),
            TextRow(
                "code-column-properties-typed-int-and-string",
                "Generate_DistinctColumnPropertyNames_ShouldSucceed",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "users",
                                Service_Column("user_id", "int", primaryKey: true),
                                Service_Column("user_name", "nvarchar(50)")
                            ),
                        ],
                    },
                ServiceRoot(),
                code:
                [
                    Member("UserEntity", "UserId", "int UserId { get; set; }", modifiers: "public"),
                    Member(
                        "UserEntity",
                        "UserName",
                        "string UserName { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-same-column-name-in-different-entities-both-entities-declared",
                "Generate_SamePropertyNameInDifferentEntities_ShouldSucceed",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "users",
                                Service_Column("user-id", "int", primaryKey: true)
                            ),
                            Service_Entity(
                                "user_profiles",
                                Service_Column("user_id", "int", primaryKey: true)
                            ),
                        ],
                    },
                ServiceRoot(),
                code: [Type("UserEntity", kind: "class"), Type("UserProfileEntity", kind: "class")]
            ),
            TextRow(
                "code-generated-member-names-user-id-navigation-and-binding",
                "Generate_DistinctGeneratedMemberNames_ShouldSucceed",
                () =>
                    Service_CustomerOrderDiagram(
                        parentKey: "user_id",
                        parentExtra: Service_Column("user_name", "nvarchar(50)"),
                        childForeignKey: "user_id"
                    ),
                ServiceRoot(),
                code:
                [
                    Member(
                        "CustomerEntity",
                        "UserId",
                        "int UserId { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "CustomerEntity",
                        "UserName",
                        "string UserName { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderEntity",
                        "Customer",
                        "CustomerEntity Customer { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "CustomerEditModel",
                        "BindingUserName",
                        "string BindingUserName { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-column-matching-navigation-name-keeps-both-members",
                "Generate_ColumnNameMatchingOtherEntityNavigation_ShouldSucceed",
                () =>
                    Service_CustomerOrderDiagram(
                        parentKey: "customer_id",
                        parentExtra: Service_Column("customer", "nvarchar(50)"),
                        childForeignKey: "customer_id"
                    ),
                ServiceRoot(),
                code:
                [
                    Member(
                        "CustomerEntity",
                        "Customer",
                        "string Customer { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderEntity",
                        "Customer",
                        "CustomerEntity Customer { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-base-class-member-named-column-property-declared",
                "Generate_ColumnNamedAfterBaseClassOnlyMember_ShouldSucceed",
                () =>
                    Service_ColumnsDiagram(
                        "items",
                        null,
                        "validate",
                        "accept_changes",
                        "mark_added"
                    ),
                ServiceRoot(),
                code:
                [
                    Member(
                        "ItemEntity",
                        "Validate",
                        "int Validate { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-multi-segment-root-namespace-declared",
                "Generate_ValidMultiSegmentRootNamespace_ShouldSucceed",
                SingleEntityDiagram,
                new CodeGenerationOptions { RootNamespace = "My.App.Data" },
                fileNamespaces: new Dictionary<string, string>
                {
                    ["QuickEREntities.g.cs"] = "My.App.Data",
                }
            ),
            TextRow(
                "code-sql-column-type-emitted-with-annotations-without-repository",
                "Generate_IncludeDataAnnotationsWithoutRepository_ShouldEmitSqlColumnTypeAttribute",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    IncludeDataAnnotations = true,
                },
                code:
                [
                    Type(
                        "SqlColumnTypeAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    Attribute(
                        "CustomerEntity",
                        "Name",
                        "SqlColumnType(SqlDbType.NVarChar, Size = 50)"
                    ),
                    Attribute(
                        "CustomerEntity",
                        "Balance",
                        "SqlColumnType(SqlDbType.Decimal, Precision = 10, Scale = 2)"
                    ),
                    Using(AllFiles, "System.Data"),
                    Text_NoTypeDeclaring("_columnTypeCache"),
                ]
            ),
            TextRow(
                "code-sql-column-type-absent-without-annotations-and-repository",
                "Generate_NoAnnotationsNoRepository_ShouldNotEmitSqlColumnTypeAttribute",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    IncludeDataAnnotations = false,
                },
                code:
                [
                    Type("CustomerEntity", kind: "class"),
                    NoType("SqlColumnTypeAttribute"),
                    PropertiesWithAttribute("SqlColumnType", 0),
                ]
            ),
            TextRow(
                "code-save-hook-extensions-once-efcore-only",
                "Generate_AddSaveHook_ShouldBeEmittedExactlyOnce_EvenForEfCoreOnly（EF Core 単独）",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateEfCoreRepositories = true,
                },
                code:
                [
                    Using(AllFiles, "Microsoft.Extensions.DependencyInjection"),
                    Type(
                        "SaveHookServiceCollectionExtensions",
                        kind: "class",
                        modifiers: "public static",
                        count: 1
                    ),
                ]
            ),
            TextRow(
                "code-save-hook-extensions-once-quicker-and-inmemory",
                "Generate_AddSaveHook_ShouldBeEmittedExactlyOnce_EvenForEfCoreOnly（QuickER 版 Repository ＋ インメモリ）",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                    GenerateInMemoryRepositories = true,
                },
                code:
                [
                    Type(
                        "SaveHookServiceCollectionExtensions",
                        kind: "class",
                        modifiers: "public static",
                        count: 1
                    ),
                ]
            ),
            TextRow(
                "code-column-attribute-on-every-column-without-annotations",
                "Generate_NoAnnotations_ShouldStillEmitColumnAttributeOnEveryColumn",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    IncludeDataAnnotations = false,
                },
                code:
                [
                    Attribute("CustomerEntity", "CustomerId", "Column(\"customer_id\")"),
                    Attribute("CustomerEntity", "Name", "Column(\"name\")"),
                    Using(AllFiles, "System.ComponentModel.DataAnnotations.Schema"),
                    NoTypes(
                        "[Table] を付ける",
                        type =>
                            type.GetAttributes()
                                .Any(item => item.AttributeClass?.Name == "TableAttribute")
                    ),
                    PropertiesWithAttribute("Key", 0),
                    PropertiesWithAttribute("Required", 0),
                    PropertiesWithAttribute("MaxLength", 0),
                ]
            ),
            TextRow(
                "code-value-object-types-absent-when-disabled",
                "Generate_ValueObjects_Disabled_ShouldNotEmitValueObjectTypes",
                Service_ValueObjectDiagram,
                ServiceRoot(),
                code:
                [
                    NoTypes(
                        "名前に ValueObjectBase を含む",
                        type => type.Name.Contains("ValueObjectBase", StringComparison.Ordinal)
                    ),
                    NoType("IValueObject"),
                    NoType("ValueObjectJsonConverterFactory"),
                    Member(
                        "CustomerEntity",
                        "CustomerId",
                        "int CustomerId { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-guidkey-primary-key-value-object-bases",
                "Generate_ValueObjects_GuidKey_ShouldUseGuidKeyBaseForStringPrimaryKey",
                () => Service_GuidKeyDiagram("nvarchar(36)"),
                Service_GuidKeyOptions(),
                code:
                [
                    Type("DocumentIdValue", baseType: "ValueObjectGuidKeyBase<DocumentIdValue>"),
                    Type("TitleValue", baseType: "ValueObjectStringBase<TitleValue>"),
                ]
            ),
            TextRow(
                "code-same-name-columns-share-one-value-object",
                "Generate_ValueObjects_ConflictingSameNameColumns_ShouldWarnButGenerate",
                Text_ConflictingNameColumnsDiagram,
                ServiceRoot() with
                {
                    GenerateValueObjects = true,
                },
                code:
                [
                    Type("NameValue", kind: "class", modifiers: "public sealed partial", count: 1),
                ]
            ),
            TextRow(
                "code-sbyte-column-value-object-ordered-base",
                "Generate_ValueObjects_SByteColumn_ShouldDeriveOrderedBase",
                Text_SByteColumnDiagram,
                ServiceRoot() with
                {
                    GenerateValueObjects = true,
                },
                code:
                [
                    Type(
                        "LevelValue",
                        kind: "class",
                        modifiers: "public sealed partial",
                        baseType: "ValueObjectOrderedBase<LevelValue, sbyte>"
                    ),
                ],
                typeDialect: DialectNames.MySql
            ),
            TextRow(
                "code-snapshot-suffix-column-properties",
                "Generate_SnapshotSuffixColumn_ShouldGenerate",
                Text_SnapshotSuffixDiagram,
                ServiceRoot(),
                code:
                [
                    Member("ProbeEntity", "Foo", "int Foo { get; set; }", modifiers: "public"),
                    Member(
                        "ProbeEntity",
                        "FooSnapshot",
                        "int? FooSnapshot { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-split-category-files-namespaces-and-usings",
                "Generate_Split_ShouldEmitOneFilePerCategoryPlusRuntime",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    GenerateValueObjects = true,
                    GenerateRepositories = true,
                },
                code:
                [
                    Type(
                        "EntityBaseCore",
                        file: "Runtime.g.cs",
                        kind: "class",
                        modifiers: "public abstract"
                    ),
                    Type(
                        "EditModelBaseCore",
                        file: "Runtime.g.cs",
                        kind: "class",
                        modifiers: "public abstract"
                    ),
                    Type(
                        "CustomerEntity",
                        file: "Entities.g.cs",
                        kind: "class",
                        modifiers: "public partial"
                    ),
                    Type("EntityBaseCore", file: "Entities.g.cs", count: 0),
                    Using("Entities.g.cs", "Sample.Domain.Runtime"),
                    Using("Mappers.g.cs", "Sample.Domain.Entities"),
                    Using("Mappers.g.cs", "Sample.Domain.EditModels"),
                ],
                fileNamespaces: new Dictionary<string, string>
                {
                    ["Runtime.g.cs"] = "Sample.Domain.Runtime",
                    ["Runtime.SqlServer.g.cs"] = "Sample.Domain.Runtime.SqlServer",
                    ["Entities.g.cs"] = "Sample.Domain.Entities",
                    ["ValueObjects.g.cs"] = "Sample.Domain.ValueObjects",
                }
            ),
            TextRow(
                "code-split-same-namespace-files-skip-self-using",
                "Generate_Split_SameNamespace_ShouldStillEmitSeparateFiles",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    GenerateEditModels = true,
                    GenerateMappers = false,
                    GenerateRepositories = false,
                    EntityNamespace = "Shared.Models",
                    EditModelNamespace = "Shared.Models",
                },
                code: [NoUsing("Entities.g.cs", "Shared.Models")],
                fileNamespaces: new Dictionary<string, string>
                {
                    ["Entities.g.cs"] = "Shared.Models",
                    ["EditModels.g.cs"] = "Shared.Models",
                }
            ),
            TextRow(
                "code-split-runtime-namespace-defaults-to-root-runtime",
                "Generate_Split_RuntimeNamespace_ShouldDefaultToRootDotRuntime",
                SingleEntityDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    SplitFilesByCategory = true,
                },
                fileNamespaces: new Dictionary<string, string>
                {
                    ["Runtime.g.cs"] = "Acme.App.Runtime",
                }
            ),
            TextRow(
                "model-efcore-one-to-many-foreign-key",
                "Generate_EfCore_ShouldConfigureOneToManyRelationship",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateEfCoreRepositories = true,
                },
                model:
                [
                    EfCoreModelFact.SingleForeignKey(
                        "OrderEntity",
                        dependentProperties: ["CustomerId"],
                        deleteBehavior: DeleteBehavior.ClientNoAction,
                        dependentToPrincipal: "Customer",
                        principalToDependent: "Orders"
                    ),
                ]
            ),
            TextRow(
                "model-efcore-cascade-delete-behavior",
                "Generate_EfCore_ShouldConfigureCascadeDeleteFromModel",
                () =>
                {
                    var diagram = Service_ValueObjectDiagram();
                    diagram.Relationships[0].OnDelete = ForeignKeyReferentialAction.Cascade;
                    return diagram;
                },
                ServiceRoot() with
                {
                    GenerateEfCoreRepositories = true,
                },
                model:
                [
                    EfCoreModelFact.SingleForeignKey(
                        "OrderEntity",
                        deleteBehavior: DeleteBehavior.Cascade
                    ),
                ]
            ),
            TextRow(
                "model-efcore-value-object-conversion",
                "Generate_EfCore_WithValueObjects_ShouldEmitHasConversion",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateEfCoreRepositories = true,
                    GenerateValueObjects = true,
                },
                model:
                [
                    EfCoreModelFact.Property(
                        "CustomerEntity",
                        "CustomerId",
                        providerClrType: typeof(int)
                    ),
                    EfCoreModelFact.Property(
                        "CustomerEntity",
                        "Name",
                        maxLength: 50,
                        providerClrType: typeof(string)
                    ),
                ]
            ),
            TextRow(
                "model-efcore-rowversion-is-row-version",
                "Generate_EfCore_ShouldConfigureRowVersion",
                Text_RowVersionDocumentsDiagram,
                ServiceRoot() with
                {
                    GenerateEfCoreRepositories = true,
                },
                model:
                [
                    EfCoreModelFact.Property(
                        "DocumentEntity",
                        "RowVersion",
                        column: "row_version",
                        required: true,
                        concurrencyToken: true,
                        valueGenerated: ValueGenerated.OnAddOrUpdate
                    ),
                ]
            ),
            TextRow(
                "code-efcore-disabled-emits-no-ef-code",
                "Generate_EfCore_Disabled_ShouldNotEmitAnyEfCode",
                Service_ValueObjectDiagram,
                ServiceRoot(),
                code:
                [
                    NoType("QuickErDbContext"),
                    NoTypes(
                        "OnModelCreating を宣言する",
                        type => !type.GetMembers("OnModelCreating").IsEmpty
                    ),
                    NoUsingUnder(AllFiles, "Microsoft.EntityFrameworkCore"),
                ],
                compileWithout: ReducedReferences.WithoutEfCore
            ),
            TextRow(
                "code-efcore-split-dedicated-file-namespace-usings",
                "Generate_EfCore_Split_ShouldEmitDedicatedFileAndNamespace",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    GenerateEfCoreRepositories = true,
                },
                code:
                [
                    Using("Repositories.EntityFrameworkCore.g.cs", "Microsoft.EntityFrameworkCore"),
                    Using(
                        "Repositories.EntityFrameworkCore.g.cs",
                        "Sample.Domain.Runtime.EntityFrameworkCore"
                    ),
                    Type(
                        "QuickErDbContext",
                        file: "Repositories.EntityFrameworkCore.g.cs",
                        kind: "class",
                        modifiers: "public partial",
                        baseType: "DbContext"
                    ),
                    Type(
                        "EfCoreRepositoryCore<TEntity, TKey, TContext>",
                        file: "Runtime.EntityFrameworkCore.g.cs"
                    ),
                    NoUsing("Entities.g.cs", "Microsoft.EntityFrameworkCore"),
                ],
                fileNamespaces: new Dictionary<string, string>
                {
                    ["Repositories.EntityFrameworkCore.g.cs"] =
                        "Sample.Domain.Repositories.EntityFrameworkCore",
                    ["Runtime.EntityFrameworkCore.g.cs"] =
                        "Sample.Domain.Runtime.EntityFrameworkCore",
                }
            ),
            TextRow(
                "code-split-neutral-files-have-no-foreign-usings-plain",
                "Generate_Split_ShouldNotLeakForeignUsingsIntoUnrelatedFiles(false)",
                Service_ValueObjectDiagram,
                Text_SplitFullOptions(valueObjects: false),
                code: Text_NoForeignUsings(
                    "Entities.g.cs",
                    "EditModels.g.cs",
                    "Mappers.g.cs",
                    "Runtime.g.cs"
                )
            ),
            TextRow(
                "code-split-neutral-files-have-no-foreign-usings-value-objects",
                "Generate_Split_ShouldNotLeakForeignUsingsIntoUnrelatedFiles(true)",
                Service_ValueObjectDiagram,
                Text_SplitFullOptions(valueObjects: true),
                code: Text_NoForeignUsings(
                    "Entities.g.cs",
                    "EditModels.g.cs",
                    "Mappers.g.cs",
                    "Runtime.g.cs",
                    "ValueObjects.g.cs"
                )
            ),
            TextRow(
                "code-efcore-only-split-repository-file-no-ado-using-plain",
                "Generate_EfCoreOnly_Split_RepositoryFile_ShouldNotUseSqlClient(false)",
                Service_ValueObjectDiagram,
                Text_EfCoreOnlySplitOptions(valueObjects: false),
                code: Text_RepositoryFileRules()
            ),
            TextRow(
                "code-efcore-only-split-repository-file-no-ado-using-value-objects",
                "Generate_EfCoreOnly_Split_RepositoryFile_ShouldNotUseSqlClient(true)",
                Service_ValueObjectDiagram,
                Text_EfCoreOnlySplitOptions(valueObjects: true),
                code: Text_RepositoryFileRules()
            ),
            TextRow(
                "code-efcore-split-namespace-derived-from-repository",
                "Generate_EfCore_Split_ShouldDeriveNamespaceFromRepository",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    GenerateEfCoreRepositories = true,
                    RepositoryNamespace = "Acme.Persistence.Repos",
                },
                fileNamespaces: new Dictionary<string, string>
                {
                    ["Repositories.EntityFrameworkCore.g.cs"] =
                        "Acme.Persistence.Repos.EntityFrameworkCore",
                }
            ),
            TextRow(
                "code-efcore-only-emits-contract-and-efcore-without-quicker-ado",
                "Generate_EfCoreOnly_ShouldSucceedWithoutError",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateEfCoreRepositories = true,
                    GenerateRepositories = false,
                },
                code:
                [
                    Type("ISqlExecutor", kind: "interface", modifiers: "public partial"),
                    Type("SqlQuery<TEntity>", kind: "class", modifiers: "public sealed"),
                    Type("RawSqlMapper", kind: "class", modifiers: "internal static"),
                    Type(
                        "QuickErDbContext",
                        kind: "class",
                        modifiers: "public partial",
                        baseType: "DbContext"
                    ),
                    Member(
                        "GeneratedEfCoreRepositoryServiceCollectionExtensions",
                        "AddGeneratedEfCoreRepositories",
                        modifiers: "public static"
                    ),
                    NoType("SqlExecutor"),
                    NoType("SqlServerRepositoryCore"),
                    Text_NoTypeDeclaring("AddGeneratedSqlServerRepositories"),
                ]
            ),
            TextRow(
                "code-efcore-only-no-sqlclient-inline-plain",
                "Generate_EfCoreOnly_ShouldNotDependOnSqlClient(split: false, vo: false)",
                Service_ValueObjectDiagram,
                Text_EfCoreOnlyOptions(split: false, valueObjects: false),
                code: [NoUsingUnder(AllFiles, "Microsoft.Data.SqlClient")],
                compileWithout: ReducedReferences.WithoutSqlClient
            ),
            TextRow(
                "code-efcore-only-no-sqlclient-split-plain",
                "Generate_EfCoreOnly_ShouldNotDependOnSqlClient(split: true, vo: false)",
                Service_ValueObjectDiagram,
                Text_EfCoreOnlyOptions(split: true, valueObjects: false),
                code: [NoUsingUnder(AllFiles, "Microsoft.Data.SqlClient")],
                compileWithout: ReducedReferences.WithoutSqlClient
            ),
            TextRow(
                "code-efcore-only-no-sqlclient-inline-value-objects",
                "Generate_EfCoreOnly_ShouldNotDependOnSqlClient(split: false, vo: true)",
                Service_ValueObjectDiagram,
                Text_EfCoreOnlyOptions(split: false, valueObjects: true),
                code: [NoUsingUnder(AllFiles, "Microsoft.Data.SqlClient")],
                compileWithout: ReducedReferences.WithoutSqlClient
            ),
            TextRow(
                "code-efcore-only-no-sqlclient-split-value-objects",
                "Generate_EfCoreOnly_ShouldNotDependOnSqlClient(split: true, vo: true)",
                Service_ValueObjectDiagram,
                Text_EfCoreOnlyOptions(split: true, valueObjects: true),
                code: [NoUsingUnder(AllFiles, "Microsoft.Data.SqlClient")],
                compileWithout: ReducedReferences.WithoutSqlClient
            ),
            TextRow(
                "code-efcore-split-ef-files-use-no-sqlclient-types",
                "Generate_EfCore_Split_ShouldKeepEfCodeFreeOfSqlClientTypes",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    GenerateEfCoreRepositories = true,
                },
                // EF Core の 2 ファイルが SqlClient の型を使わないことを、出力全体を SqlClient なしでコンパイルできることで示す
                // （この構成は EF Core 単独で、QuickER 版の ADO を出さない）
                compileWithout: ReducedReferences.WithoutSqlClient,
                code:
                [
                    Type("EfCoreSqlExecutor<TContext>", file: "Runtime.EntityFrameworkCore.g.cs"),
                    Type(
                        "EfCoreRepositoryCore<TEntity, TKey, TContext>",
                        file: "Runtime.EntityFrameworkCore.g.cs"
                    ),
                ]
            ),
            TextRow(
                "code-inmemory-only-emits-contract-and-inmemory",
                "Generate_InMemoryOnly_EmitsContractAndInMemory_NoAdoOrEf",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = false,
                    GenerateInMemoryRepositories = true,
                },
                code:
                [
                    Type("IItemRepository", kind: "interface", modifiers: "public partial"),
                    Type("InMemoryDataStore", kind: "class"),
                    Type("InMemoryItemRepository", kind: "class"),
                    Type("InMemorySampleData", kind: "class"),
                    Member(
                        "GeneratedInMemoryRepositoryServiceCollectionExtensions",
                        "AddGeneratedInMemoryRepositories"
                    ),
                    NoUsingUnder(AllFiles, "Microsoft.Data"),
                    NoUsingUnder(AllFiles, "Microsoft.EntityFrameworkCore"),
                    NoType("ItemRepository"),
                    Text_NoTypeDeclaring("AddGeneratedSqlServerRepositories"),
                    Text_NoTypeDeclaring("AddGeneratedSqliteRepositories"),
                ],
                compileWithout: ReducedReferences.WithoutDataAccess
            ),
            TextRow(
                "code-inmemory-runtime-package-leaves-fixed-infra-out",
                "Generate_InMemoryWithRuntimePackages_UsesInMemoryPackage",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = false,
                    GenerateInMemoryRepositories = true,
                    UseRuntimePackages = true,
                },
                code:
                [
                    NoType("InMemoryDataStore"),
                    NoType("EntityBaseCore"),
                    Using(AllFiles, "QuickER.Runtime.InMemory"),
                    Member(
                        "GeneratedInMemoryRepositoryServiceCollectionExtensions",
                        "AddGeneratedInMemoryRepositories"
                    ),
                ]
            ),
            TextRow(
                "code-remote-server-runtime-package-leaves-engine-out",
                "Generate_RemoteServicesWithRuntimePackages_UsesAspNetCorePackage",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                    UseRuntimePackages = true,
                },
                code:
                [
                    NoType("RemoteServerEngine"),
                    NoType("DeferredOctetStreamBody"),
                    NoType("RemoteBadRequestException"),
                    Using("QuickEREntities.RemoteServer.g.cs", "QuickER.Runtime.AspNetCore"),
                    Member("GeneratedRemoteEndpoints", "MapGeneratedRemoteEndpoints"),
                    Member(
                        "GeneratedRemoteEndpoints",
                        "OnServerError",
                        "void OnServerError(HttpContext context, Exception ex)",
                        modifiers: "static partial"
                    ),
                ],
                fileRules: [new FileRule(".RemoteServer.g.cs", Count: 1)]
            ),
            TextRow(
                "code-unbounded-binary-attribute-unused-without-exclusion",
                "Generate_ExcludeUnboundedBinary_Off_DoesNotMarkOrReport",
                Service_BinaryColumnDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                },
                code:
                [
                    Type(
                        "UnboundedBinaryColumnAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    PropertiesWithAttribute("UnboundedBinaryColumn", 0),
                ]
            ),
            TextRow(
                "code-rowversion-marked-store-generated-and-excluded-from-write",
                "Generate_RowVersionColumn_MarksStoreGeneratedAndExcludesFromWrite",
                Service_BinaryColumnDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                },
                code:
                [
                    Type(
                        "StoreGeneratedColumnAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    Attribute("DocumentEntity", "RowVersion", "StoreGeneratedColumn"),
                    PropertiesWithAttribute("StoreGeneratedColumn", 1),
                    Calls(
                        "EntitySaveMetadata",
                        "Build",
                        "GetCustomAttribute<StoreGeneratedColumnAttribute>"
                    ),
                    Member("EntitySaveMetadata", "InsertProperties", modifiers: "public required"),
                    Member(
                        "DocumentEntity",
                        "RowVersion",
                        "byte[] RowVersion { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-no-rowversion-marks-nothing-store-generated",
                "Generate_NoRowVersion_DoesNotMarkStoreGenerated",
                SingleEntityDiagram,
                ServiceRoot(),
                code:
                [
                    Type(
                        "StoreGeneratedColumnAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    PropertiesWithAttribute("StoreGeneratedColumn", 0),
                ]
            ),
            TextRow(
                "code-binary-stream-accessors-absent-without-exclusion",
                "Generate_BinaryStreamAccessors_Off_NotGenerated",
                Service_BinaryColumnDiagram,
                // 元のテストは Repository を出さない既定で呼んでおり、除外を ON にしても出ない入力だった（空振り）。
                // Repository を出す構成で、除外 OFF が Stream アクセサを出さないことを見る
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                },
                code: Text_NoBinaryStreamAccessors()
            ),
            TextRow(
                "code-binary-stream-accessors-absent-in-efcore-only",
                "Generate_BinaryStreamAccessors_EfOnly_NotGenerated",
                Service_BinaryColumnDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                    ExcludeUnboundedBinaryColumns = true,
                },
                code: Text_NoBinaryStreamAccessors()
            ),
            TextRow(
                "code-binary-stream-accessors-on-remote-interface",
                "Generate_BinaryStreamAccessors_OnRemoteInterface_WhenRemoteContracts",
                Service_BinaryColumnDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                    ExcludeUnboundedBinaryColumns = true,
                    GenerateRemoteContracts = true,
                },
                code:
                [
                    Member(
                        "IDocumentRemoteRepository",
                        "ReadPhotoAsync",
                        "Task<bool> ReadPhotoAsync(int id, Stream destination, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Type(
                        "IDocumentRepository",
                        kind: "interface",
                        interfaces:
                        [
                            "IDocumentRemoteRepository",
                            "IRepository<DocumentEntity, int>",
                        ]
                    ),
                    NoMember("IDocumentRepository", "ReadPhotoAsync"),
                    NoMember("IDocumentRepository", "WritePhotoAsync"),
                    Member(
                        "DocumentRepositoryBinaryStreamExtensions",
                        "ReadPhotoToFileAsync",
                        "Task<bool> ReadPhotoToFileAsync(this IDocumentRemoteRepository repository, int id, string path, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "DocumentRepositoryBinaryStreamExtensions",
                        "WritePhotoFromFileAsync",
                        "Task<bool> WritePhotoFromFileAsync(this IDocumentRemoteRepository repository, int id, string path, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                ]
            ),
            TextRow(
                "code-binary-stream-accessors-on-full-interface",
                "Generate_BinaryStreamAccessors_OnFullInterface_WhenNoRemoteContracts",
                Service_BinaryColumnDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                    ExcludeUnboundedBinaryColumns = true,
                },
                code:
                [
                    Type(
                        "IDocumentRepository",
                        kind: "interface",
                        interfaces: ["IRepository<DocumentEntity, int>"]
                    ),
                    Member(
                        "IDocumentRepository",
                        "ReadPhotoAsync",
                        "Task<bool> ReadPhotoAsync(int id, Stream destination, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "DocumentRepositoryBinaryStreamExtensions",
                        "ReadPhotoToFileAsync",
                        "Task<bool> ReadPhotoToFileAsync(this IDocumentRepository repository, int id, string path, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "DocumentRepositoryBinaryStreamExtensions",
                        "WritePhotoFromFileAsync",
                        "Task<bool> WritePhotoFromFileAsync(this IDocumentRepository repository, int id, string path, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                ]
            ),
            // 主キーの順序を上書きした図（b, a）は、修正 H の行 compile-composite-key-only-efcore が同じ構成（EF Core 版だけ）で
            // コンパイルと EF Core のモデルの主キーの並びを見る（図は非キー列 note が 1 本多いだけで、HasKey の並びに効かない）。
            // ここでは上書きの無い図（列の宣言順 a, b）を、同じ判定で見る
            new ScenarioRow<CodegenCase>(
                "compile-efcore-composite-key-follows-column-declaration-order",
                null,
                ServiceClass
                    + "Generate_EfCore_CompositeKey_ShouldFollowPrimaryKeyColumnOrder（順序指定なし）",
                new CodegenCase(
                    () => Text_CompositeKeyDiagram(reversePrimaryKeyOrder: false),
                    ServiceRoot() with
                    {
                        GenerateEfCoreRepositories = true,
                    },
                    DialectNames.SqlServer,
                    DiagnosticExpectation.Contains(),
                    Compile: true,
                    Probes: [CodegenProbe.EfCoreModel],
                    ProbeFacts: new CodegenProbeFacts(
                        ModelFacts: [EfCoreModelFact.PrimaryKey("PairEntity", ["A", "B"])]
                    )
                )
            ),
        ];

    // ===== EditModel・Mapper（フェーズ 3） =====

    /// <summary>customers(customer_id) 1:N orders(order_id, customer_id) の図（Generate_ShouldCreateNavigationAndJsonIgnoreOnParentReference ほか）</summary>
    private static ErDiagram Text_CustomerOrderNavigationDiagram() =>
        Service_CustomerOrderDiagram(
            parentKey: "customer_id",
            parentExtra: null,
            childForeignKey: "customer_id"
        );

    /// <summary>
    /// 確定値の変更と RowState：ロード中の代入は昇格しない（RowState は実体から写る）・ロード後の入力は Updated へ昇格する・MarkAdded / MarkRemoved が
    /// 状態を変える（<c>AfterConfirmedValueSet</c> の <c>IsLoading</c> の分岐・<c>ShouldMarkUpdated</c> の既定・Mapper の RowState の写し）
    /// </summary>
    private static ExecCheck Text_RowStateTransitions(string stem, string key) =>
        new(
            "row-state-transitions",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                    {
                        var problems = new List<string>();
                        var entity = Text_New(assembly, stem + "Entity");
                        ProbeKit.Set(entity, key, 1);
                        Text_SetState(entity, "Unchanged");
                        var model = Text_Load(assembly, stem, entity);
                        Text_Expect(
                            problems,
                            Text_State(model) == "Unchanged",
                            $"ロードした EditModel の RowState が {Text_State(model)}（期待 Unchanged。ロード中の代入が昇格した）"
                        );

                        ColumnText_Input(model, key, "2");
                        Text_Expect(
                            problems,
                            Text_State(model) == "Updated" && Equals(ProbeKit.Get(model, key), 2),
                            $"ロード後の入力で RowState が {Text_State(model)}・{key} が {ProbeKit.Get(model, key)}（期待 Updated・2）"
                        );

                        var added = Text_New(assembly, stem + "Entity");
                        Text_SetState(added, "Added");
                        Text_Expect(
                            problems,
                            Text_State(Text_Load(assembly, stem, added)) == "Added",
                            "Added の実体からロードした EditModel が Added にならない"
                        );

                        var fresh = Text_New(assembly, stem + "EditModel");
                        ProbeKit.Call(fresh, "MarkAdded");
                        Text_Expect(
                            problems,
                            Text_State(fresh) == "Added",
                            "MarkAdded で Added にならない"
                        );
                        ColumnText_Input(fresh, key, "3");
                        var applied = Text_ToEntity(assembly, stem, fresh);
                        Text_Expect(
                            problems,
                            Text_State(applied) == "Added",
                            $"Added の EditModel から作った実体の RowState が {Text_State(applied)}（期待 Added）"
                        );
                        ProbeKit.Call(fresh, "MarkRemoved");
                        Text_Expect(
                            problems,
                            Text_State(fresh) == "Removed",
                            "MarkRemoved で Removed にならない"
                        );

                        return problems;
                    }
                )
        );

    /// <summary>
    /// 子コレクションの親リンク・Mapper の子の写し：EditModel の子コレクションへ足した子の型付き ParentModel が親になる・Mapper が子を実体へ写す・
    /// 削除した子は includeRemoved のときだけ写る・ロードで子が EditModel へ写る・コレクションを差し替えても親リンクが張られる・
    /// コレクションの HasChanges は削除で立つ
    /// </summary>
    private static ExecCheck Text_CustomerOrderGraph() =>
        new(
            "customer-order-graph",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                    {
                        var problems = new List<string>();
                        var customer = Text_New(assembly, "CustomerEditModel");
                        ColumnText_Input(customer, "CustomerId", "1");
                        var order = Text_New(assembly, "OrderEditModel");
                        ColumnText_Input(order, "OrderId", "10");
                        ColumnText_Input(order, "CustomerId", "1");
                        Text_Add(customer, "Orders", order);
                        Text_Expect(
                            problems,
                            ReferenceEquals(ProbeKit.Get(order, "ParentModel"), customer),
                            "子コレクションへ足した OrderEditModel の ParentModel が親の CustomerEditModel にならない"
                        );

                        var entity = Text_ToEntity(assembly, "Customer", customer);
                        var orders = Text_Items(entity, "Orders");
                        Text_Expect(
                            problems,
                            orders.Count == 1 && Equals(ProbeKit.Get(orders[0], "OrderId"), 10),
                            $"Mapper が子を実体へ写さない（Orders {orders.Count} 件）"
                        );

                        var loadedEntity = Text_New(assembly, "CustomerEntity");
                        ProbeKit.Set(loadedEntity, "CustomerId", 1);
                        var child = Text_New(assembly, "OrderEntity");
                        ProbeKit.Set(child, "OrderId", 5);
                        ProbeKit.Set(child, "CustomerId", 1);
                        ((IList)ProbeKit.Get(loadedEntity, "Orders")!).Add(child);
                        var loaded = Text_Load(assembly, "Customer", loadedEntity);
                        var loadedOrders = Text_Items(loaded, "Orders");
                        Text_Expect(
                            problems,
                            loadedOrders.Count == 1
                                && Equals(ProbeKit.Get(loadedOrders[0], "OrderId"), 5),
                            $"ロードで子が EditModel へ写らない（Orders {loadedOrders.Count} 件）"
                        );

                        var collection = ProbeKit.Get(loaded, "Orders")!;
                        Text_Expect(
                            problems,
                            Equals(ProbeKit.Get(collection, "HasChanges"), false),
                            "ロード直後の子コレクションの HasChanges が true"
                        );
                        ((IList)collection).Remove(loadedOrders[0]);
                        Text_Expect(
                            problems,
                            Equals(ProbeKit.Get(collection, "HasChanges"), true),
                            "子を削除しても子コレクションの HasChanges が立たない"
                        );
                        var withRemoved = Text_Items(
                            Text_ToEntity(assembly, "Customer", loaded, includeRemoved: true),
                            "Orders"
                        );
                        Text_Expect(
                            problems,
                            withRemoved.Count == 1 && Text_State(withRemoved[0]) == "Removed",
                            $"includeRemoved の写しに削除した子が Removed で入らない（{withRemoved.Count} 件）"
                        );
                        Text_Expect(
                            problems,
                            Text_Items(Text_ToEntity(assembly, "Customer", loaded), "Orders").Count
                                == 0,
                            "includeRemoved でない写しに削除した子が入る"
                        );

                        var replacement = Activator.CreateInstance(
                            ProbeKit.Get(customer, "Orders")!.GetType()
                        )!;
                        var moved = Text_New(assembly, "OrderEditModel");
                        ((IList)replacement).Add(moved);
                        ProbeKit.Set(customer, "Orders", replacement);
                        Text_Expect(
                            problems,
                            ReferenceEquals(ProbeKit.Get(moved, "ParentModel"), customer),
                            "差し替えた子コレクションの要素の ParentModel が親にならない"
                        );

                        return problems;
                    }
                )
        );

    /// <summary>
    /// 必須の検証と子への連鎖：空の EditModel は主キーの必須エラーになり、文言は <c>EditModelMessages.Required</c> が作る・入力すれば消える・
    /// 子の検証は includeChildren のときだけ走り、CollectErrors は子のエラーをパスつきで集める
    /// </summary>
    private static ExecCheck Text_CustomerOrderValidation() =>
        new(
            "customer-order-validation",
            assembly =>
                Text_WithStaticFunc(
                    assembly,
                    "EditModelMessages",
                    "Required",
                    new Func<string, string, string>(
                        (property, display) => $"required:{property}:{display}"
                    ),
                    () =>
                    {
                        var problems = new List<string>();
                        var customer = Text_New(assembly, "CustomerEditModel");
                        Text_Expect(
                            problems,
                            !ColumnText_Validate(customer),
                            "空の CustomerEditModel の検証が通る"
                        );
                        var errors = ColumnText_Errors(customer, "CustomerId");
                        Text_Expect(
                            problems,
                            errors.SequenceEqual(["required:CustomerId:CustomerId"]),
                            $"CustomerId の必須エラーが [{string.Join(", ", errors)}]（期待 [required:CustomerId:CustomerId]）"
                        );

                        ColumnText_Input(customer, "CustomerId", "1");
                        Text_Expect(
                            problems,
                            ColumnText_Validate(customer)
                                && ColumnText_Errors(customer, "CustomerId").Count == 0,
                            "入力した後も CustomerId の必須エラーが残る"
                        );

                        var order = Text_New(assembly, "OrderEditModel");
                        Text_Add(customer, "Orders", order);
                        Text_Expect(
                            problems,
                            (bool)ProbeKit.Call(customer, "Validate", false)!,
                            "includeChildren: false の検証が子のエラーで落ちる"
                        );
                        Text_Expect(
                            problems,
                            !(bool)ProbeKit.Call(customer, "Validate", true)!,
                            "includeChildren: true の検証が子のエラーを見ない"
                        );

                        var paths = ((IEnumerable)ProbeKit.Call(customer, "CollectErrors", true)!)
                            .Cast<object>()
                            .Select(error => (string)ProbeKit.Get(error, "Path")!)
                            .Distinct()
                            .ToList();
                        Text_Expect(
                            problems,
                            paths.SequenceEqual(["Orders[0]"]),
                            $"CollectErrors(true) のパスが [{string.Join(", ", paths)}]（期待 [Orders[0]]）"
                        );
                        Text_Expect(
                            problems,
                            !((IEnumerable)ProbeKit.Call(customer, "CollectErrors", false)!)
                                .Cast<object>()
                                .Any(),
                            "CollectErrors(false) が子のエラーを集める"
                        );

                        var itemPaths = (
                            (IEnumerable)
                                ProbeKit.Call(
                                    ProbeKit.Get(customer, "Orders")!,
                                    "CollectErrors",
                                    true
                                )!
                        )
                            .Cast<object>()
                            .Select(error => (string)ProbeKit.Get(error, "Path")!)
                            .Distinct()
                            .ToList();
                        Text_Expect(
                            problems,
                            itemPaths.SequenceEqual(["[0]"]),
                            $"子コレクションの CollectErrors のパスが [{string.Join(", ", itemPaths)}]（期待 [[0]]）"
                        );

                        return problems;
                    }
                )
        );

    /// <summary>
    /// 入力の変換・元へ戻す・兄弟の位置・行編集の取り消し・変更通知（orders(order_id int, amount decimal) の EditModel）
    /// </summary>
    private static ExecCheck Text_OrderEditModelBehavior() =>
        new(
            "order-editmodel-behavior",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                        Text_WithStaticFunc(
                            assembly,
                            "EditModelMessages",
                            "ParseFailed",
                            new Func<string, string, string, string, string>(
                                (property, display, input, type) =>
                                    $"parse:{property}:{display}:{input}:{type}"
                            ),
                            () =>
                            {
                                var problems = new List<string>();
                                var model = Text_New(assembly, "OrderEditModel");

                                foreach (
                                    var (property, input, expected) in new[]
                                    {
                                        ("OrderId", "abc", "parse:OrderId:OrderId:abc:int"),
                                        ("Amount", "x", "parse:Amount:Amount:x:decimal"),
                                    }
                                )
                                {
                                    ColumnText_Input(model, property, input);
                                    var errors = ColumnText_Errors(model, property);
                                    Text_Expect(
                                        problems,
                                        errors.SequenceEqual([expected]),
                                        $"{property} の変換エラーが [{string.Join(", ", errors)}]（期待 [{expected}]）"
                                    );
                                }

                                ColumnText_Input(model, "OrderId", "12");
                                ColumnText_Input(model, "Amount", "1.5");
                                Text_Expect(
                                    problems,
                                    Equals(ProbeKit.Get(model, "OrderId"), 12)
                                        && Equals(ProbeKit.Get(model, "Amount"), 1.5m)
                                        && ColumnText_Errors(model, "OrderId").Count == 0,
                                    "入力 12 / 1.5 が確定値 int? / decimal? へ入らない"
                                );

                                ColumnText_Input(model, "OrderId", "zzz");
                                ProbeKit.Call(model, "RevertInput");
                                Text_Expect(
                                    problems,
                                    Equals(ProbeKit.Get(model, "BindingOrderId"), "12")
                                        && ColumnText_Errors(model, "OrderId").Count == 0,
                                    $"RevertInput で入力が確定値へ戻らない（BindingOrderId {ProbeKit.Get(model, "BindingOrderId")}）"
                                );

                                ProbeKit.Call(model, "BeginEdit");
                                ColumnText_Input(model, "OrderId", "13");
                                ProbeKit.Call(model, "CancelEdit");
                                Text_Expect(
                                    problems,
                                    Equals(ProbeKit.Get(model, "OrderId"), 12)
                                        && Equals(ProbeKit.Get(model, "BindingOrderId"), "12"),
                                    "CancelEdit で BeginEdit の時点の確定値・入力へ戻らない"
                                );

                                var first = Text_New(assembly, "OrderEditModel");
                                var second = Text_New(assembly, "OrderEditModel");
                                var alone = Text_New(assembly, "OrderEditModel");
                                var collection = Activator.CreateInstance(
                                    ProbeKit
                                        .Type(assembly, "EditModelCollection`1")
                                        .MakeGenericType(first.GetType())
                                )!;
                                ((IList)collection).Add(first);
                                ((IList)collection).Add(second);
                                Text_Expect(
                                    problems,
                                    ReferenceEquals(Text_CallTyped(first, "GetNext"), second)
                                        && ReferenceEquals(
                                            Text_CallTyped(second, "GetPrevious"),
                                            first
                                        )
                                        && Text_CallTyped(second, "GetNext") is null,
                                    "型付きの GetNext / GetPrevious が兄弟を返さない"
                                );
                                Text_Expect(
                                    problems,
                                    Equals(ProbeKit.Get(first, "IndexInParent"), 0)
                                        && Equals(ProbeKit.Get(second, "IndexInParent"), 1)
                                        && Equals(ProbeKit.Get(alone, "IndexInParent"), -1),
                                    "IndexInParent が 0 / 1 / -1 にならない"
                                );
                                Text_Expect(
                                    problems,
                                    Equals(ProbeKit.Get(first, "IsFirstInParent"), true)
                                        && Equals(ProbeKit.Get(second, "IsLastInParent"), true)
                                        && Equals(ProbeKit.Get(first, "IsLastInParent"), false),
                                    "IsFirstInParent / IsLastInParent が位置に合わない"
                                );
                                Text_Expect(
                                    problems,
                                    ReferenceEquals(
                                        ProbeKit.Get(first, "ParentCollection"),
                                        collection
                                    ),
                                    "ParentCollection が所属コレクションを返さない"
                                );
                                Text_Expect(
                                    problems,
                                    (bool)ProbeKit.Call(second, "MoveToFirst")!
                                        && ReferenceEquals(((IList)collection)[0], second),
                                    "MoveToFirst で先頭へ移らない"
                                );
                                Text_Expect(
                                    problems,
                                    (bool)ProbeKit.Call(second, "MoveToLast")!
                                        && ReferenceEquals(((IList)collection)[1], second)
                                        && !(bool)ProbeKit.Call(alone, "MoveToLast")!,
                                    "MoveToLast が末尾へ移らない・所属の無い EditModel で true を返す"
                                );
                                Text_Expect(
                                    problems,
                                    (bool)ProbeKit.Call(first, "RemoveFromParent")!
                                        && ((IList)collection).Count == 1,
                                    "RemoveFromParent でコレクションから外れない"
                                );

                                var notified = new List<string>();
                                var observed = Text_New(assembly, "OrderEditModel");
                                ((INotifyPropertyChanged)observed).PropertyChanged += (_, e) =>
                                    notified.Add(e.PropertyName ?? string.Empty);
                                ProbeKit.Call(observed, "MarkAdded");
                                Text_Expect(
                                    problems,
                                    notified.Contains("IsAdded") && notified.Contains("HasChanges"),
                                    $"RowState の変更で IsAdded / HasChanges の変更通知が出ない（[{string.Join(", ", notified)}]）"
                                );

                                return problems;
                            }
                        )
                )
        );

    /// <summary>
    /// Mapper の写し：確定値を実体へ写し、必須の確定値が無ければ断る・新しい行の空文字列は未入力として読み、保存済みの行の空文字列は値のまま・
    /// ロード後の入力は確定値から導く（products(product_id, name nvarchar(200))）
    /// </summary>
    private static ExecCheck Text_ProductMapperBehavior() =>
        new(
            "product-mapper-behavior",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                    {
                        var problems = new List<string>();
                        var model = Text_New(assembly, "ProductEditModel");
                        ColumnText_Input(model, "ProductId", "1");
                        ColumnText_Input(model, "Name", "Widget");
                        var entity = Text_ToEntity(assembly, "Product", model);
                        Text_Expect(
                            problems,
                            Equals(ProbeKit.Get(entity, "ProductId"), 1)
                                && Equals(ProbeKit.Get(entity, "Name"), "Widget"),
                            "確定値が実体へ写らない"
                        );

                        var missingName = Text_New(assembly, "ProductEditModel");
                        ColumnText_Input(missingName, "ProductId", "1");
                        Text_Expect(
                            problems,
                            Text_Throws(() => Text_ToEntity(assembly, "Product", missingName)),
                            "必須の Name が無いまま実体へ写せた"
                        );
                        Text_Expect(
                            problems,
                            Text_Throws(() =>
                                Text_ToEntity(
                                    assembly,
                                    "Product",
                                    Text_New(assembly, "ProductEditModel")
                                )
                            ),
                            "必須の ProductId が無いまま実体へ写せた"
                        );

                        foreach (
                            var (state, expected) in new[]
                            {
                                ("Added", (string?)null),
                                ("Unchanged", ""),
                            }
                        )
                        {
                            var blank = Text_New(assembly, "ProductEntity");
                            ProbeKit.Set(blank, "Name", "");
                            Text_SetState(blank, state);
                            var loaded = Text_Load(assembly, "Product", blank);
                            Text_Expect(
                                problems,
                                Equals(ProbeKit.Get(loaded, "Name"), expected),
                                $"{state} の行の空文字列の Name が {ProbeKit.Get(loaded, "Name") ?? "null"} として読まれた（期待 {expected ?? "null"}）"
                            );
                        }

                        var saved = Text_New(assembly, "ProductEntity");
                        ProbeKit.Set(saved, "ProductId", 7);
                        ProbeKit.Set(saved, "Name", "Gadget");
                        Text_SetState(saved, "Unchanged");
                        var reloaded = Text_Load(assembly, "Product", saved);
                        Text_Expect(
                            problems,
                            Equals(ProbeKit.Get(reloaded, "BindingName"), "Gadget")
                                && Equals(ProbeKit.Get(reloaded, "BindingProductId"), "7"),
                            "ロード後の入力（Binding*）が確定値から導かれない"
                        );

                        return problems;
                    }
                )
        );

    /// <summary>
    /// 型付き ParentModel：1 対 1 の単一子は親の setter で親リンクが張られ・付け替えで外れる・自己参照の子は自分の型の親を返す
    /// </summary>
    private static ExecCheck Text_TypedParentModels() =>
        new(
            "typed-parent-models",
            assembly =>
            {
                var problems = new List<string>();
                var owner = Text_New(assembly, "ProfileOwnerEditModel");
                var profile = Text_New(assembly, "ProfileEditModel");
                ProbeKit.Set(owner, "Profile", profile);
                Text_Expect(
                    problems,
                    ReferenceEquals(ProbeKit.Get(profile, "ParentModel"), owner),
                    "単一子 Profile の ParentModel が親の ProfileOwnerEditModel にならない"
                );
                var next = Text_New(assembly, "ProfileEditModel");
                ProbeKit.Set(owner, "Profile", next);
                Text_Expect(
                    problems,
                    ProbeKit.Get(profile, "ParentModel") is null
                        && ReferenceEquals(ProbeKit.Get(next, "ParentModel"), owner),
                    "単一子を付け替えても、外した子の親リンクが外れない・新しい子へ張られない"
                );

                var parent = Text_New(assembly, "CategoryEditModel");
                var child = Text_New(assembly, "CategoryEditModel");
                Text_Add(parent, "Categories", child);
                Text_Expect(
                    problems,
                    ReferenceEquals(ProbeKit.Get(child, "ParentModel"), parent),
                    "自己参照の子の ParentModel が親の CategoryEditModel にならない"
                );

                return problems;
            }
        );

    /// <summary>
    /// バイナリ・値型の入力の変換とロード：Base64 の入力が確定値へ入り、読めない入力はエラー・ロードは確定値の直接代入で、バイナリは複製・
    /// 新しい行の空配列は未入力・入力（Binding*）は確定値から導く（files(file_id, is_active bit, filedata varbinary(max))）
    /// </summary>
    private static ExecCheck Text_FileBindingConversions() =>
        new(
            "file-binding-conversions",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                    {
                        var problems = new List<string>();
                        var model = Text_New(assembly, "FileEditModel");
                        ColumnText_Input(model, "Filedata", Convert.ToBase64String([1, 2, 3]));
                        Text_Expect(
                            problems,
                            ProbeKit.Get(model, "Filedata") is byte[] bytes
                                && bytes.SequenceEqual(new byte[] { 1, 2, 3 }),
                            "Base64 の入力が確定値の byte[] へ入らない"
                        );
                        ColumnText_Input(model, "Filedata", "!!not base64!!");
                        Text_Expect(
                            problems,
                            ColumnText_Errors(model, "Filedata").Count == 1,
                            "Base64 でない入力が変換エラーにならない"
                        );

                        var stored = new byte[] { 4, 5 };
                        var entity = Text_New(assembly, "FileEntity");
                        ProbeKit.Set(entity, "FileId", 5);
                        ProbeKit.Set(entity, "IsActive", true);
                        ProbeKit.Set(entity, "Filedata", stored);
                        Text_SetState(entity, "Unchanged");
                        var loaded = Text_Load(assembly, "File", entity);
                        var loadedBytes = ProbeKit.Get(loaded, "Filedata") as byte[];
                        Text_Expect(
                            problems,
                            loadedBytes is not null
                                && loadedBytes.SequenceEqual(stored)
                                && !ReferenceEquals(loadedBytes, stored),
                            "ロードしたバイナリが実体と同じ中身の別の配列にならない（共有している・写らない）"
                        );
                        Text_Expect(
                            problems,
                            Equals(ProbeKit.Get(loaded, "IsActive"), true)
                                && Equals(ProbeKit.Get(loaded, "BindingIsActive"), true.ToString())
                                && Equals(ProbeKit.Get(loaded, "BindingFileId"), "5")
                                && Equals(
                                    ProbeKit.Get(loaded, "BindingFiledata"),
                                    Convert.ToBase64String(stored)
                                ),
                            "ロード後の確定値・入力（Binding*）が実体の値から導かれない"
                        );

                        var added = Text_New(assembly, "FileEntity");
                        ProbeKit.Set(added, "Filedata", Array.Empty<byte>());
                        Text_SetState(added, "Added");
                        var addedModel = Text_Load(assembly, "File", added);
                        Text_Expect(
                            problems,
                            ProbeKit.Get(addedModel, "Filedata") is null
                                && Equals(
                                    ProbeKit.Get(addedModel, "BindingFiledata"),
                                    string.Empty
                                ),
                            "新しい行の空配列が未入力（null）として読まれない"
                        );

                        return problems;
                    }
                )
        );

    /// <summary>NULL 許容のバイナリ列は Mapper が EditModel の値（null を含む）をそのまま実体へ写す</summary>
    private static ExecCheck Text_NullablePhotoMapper() =>
        new(
            "nullable-photo-mapper",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                    {
                        var problems = new List<string>();
                        var model = Text_New(assembly, "FileEditModel");
                        ColumnText_Input(model, "FileId", "1");
                        Text_Expect(
                            problems,
                            ProbeKit.Get(Text_ToEntity(assembly, "File", model), "Photo") is null,
                            "未入力の Photo が実体で null にならない"
                        );
                        ColumnText_Input(model, "Photo", Convert.ToBase64String([7]));
                        Text_Expect(
                            problems,
                            ProbeKit.Get(Text_ToEntity(assembly, "File", model), "Photo")
                                is byte[] bytes
                                && bytes.SequenceEqual(new byte[] { 7 }),
                            "入力した Photo が実体へ写らない"
                        );
                        return problems;
                    }
                )
        );

    /// <summary>変更通知の partial メソッドを実装して呼ばれ方を記録する利用側のコード（Sample.Domain の CustomerEditModel）</summary>
    private static GeneratedFile Text_ChangeHookSource() =>
        new()
        {
            FileName = "ChangeHookProbe.cs",
            Content = """
                #nullable enable
                namespace Sample.Domain;

                public partial class CustomerEditModel
                {
                    internal readonly System.Collections.Generic.List<string> HookLog = new();

                    partial void OnCustomerIdChanging(int? value) => HookLog.Add($"changing:{value}");

                    partial void OnCustomerIdChanging(int? oldValue, int? newValue) =>
                        HookLog.Add($"changing:{oldValue}->{newValue}");

                    partial void OnCustomerIdChanged(int? value) => HookLog.Add($"changed:{value}");

                    partial void OnCustomerIdChanged(int? oldValue, int? newValue) =>
                        HookLog.Add($"changed:{oldValue}->{newValue}");

                    partial void OnNameChanged(string? oldValue, string? newValue) =>
                        HookLog.Add($"name:{oldValue}->{newValue}");
                }
                """,
        };

    /// <summary>確定値の setter が代入の前後で新値版・旧値新値版の両方のフックを呼び、値が変わらなければ何も呼ばない</summary>
    private static ExecCheck Text_ChangeHooks() =>
        new(
            "change-hooks",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                    {
                        var problems = new List<string>();
                        var model = Text_New(assembly, "CustomerEditModel");
                        var log =
                            (List<string>)
                                model
                                    .GetType()
                                    .GetField(
                                        "HookLog",
                                        BindingFlags.NonPublic | BindingFlags.Instance
                                    )!
                                    .GetValue(model)!;
                        ColumnText_Input(model, "CustomerId", "1");
                        ColumnText_Input(model, "CustomerId", "2");
                        ColumnText_Input(model, "CustomerId", "2");
                        ColumnText_Input(model, "Name", "A");
                        string[] expected =
                        [
                            "changing:1",
                            "changing:->1",
                            "changed:1",
                            "changed:->1",
                            "changing:2",
                            "changing:1->2",
                            "changed:2",
                            "changed:1->2",
                            "name:->A",
                        ];
                        Text_Expect(
                            problems,
                            log.SequenceEqual(expected),
                            $"フックの呼ばれ方が [{string.Join(", ", log)}]（期待 [{string.Join(", ", expected)}]）"
                        );
                        return problems;
                    }
                )
        );

    /// <summary>
    /// vault_items（VO 有効）の EditModel / Mapper：無制限バイナリ除外が ON なら非 NULL の除外列 seal も既存の行で必須にならず、Mapper は入力があるときだけ代入する。
    /// OFF なら seal は必須で、欠けた seal の写しは断る（通常列 label はどちらでも必須）
    /// </summary>
    private static ExecCheck Text_VaultEditModelAndMapper(bool exclude) =>
        new(
            exclude ? "vault-excluded-binary-unset-keeps-current" : "vault-binary-required",
            assembly =>
            {
                var problems = new List<string>();
                var model = Text_New(assembly, "VaultItemEditModel");
                ColumnText_Input(model, "ItemId", "1");
                ProbeKit.Call(model, "MarkUnchanged");
                ColumnText_Validate(model);
                Text_Expect(
                    problems,
                    ColumnText_Errors(model, "Label").Count > 0,
                    "既存の行で未入力の label が必須エラーにならない"
                );
                Text_Expect(
                    problems,
                    (ColumnText_Errors(model, "Seal").Count > 0) == !exclude,
                    $"既存の行で未入力の seal の必須エラーが{(exclude ? "出た（除外列は必須にしない）" : "出ない")}"
                );
                Text_Expect(
                    problems,
                    ColumnText_Errors(model, "NoteBlob").Count == 0,
                    "NULL 許容の note_blob が必須エラーになった"
                );

                object Seal(params byte[] bytes) =>
                    ProbeKit.CallStatic(ProbeKit.Type(assembly, "SealValue"), "Create", bytes)!;

                var filled = Text_New(assembly, "VaultItemEditModel");
                ColumnText_Input(filled, "ItemId", "1");
                ColumnText_Input(filled, "Label", "L");
                var entity = Text_New(assembly, "VaultItemEntity");
                ProbeKit.Set(entity, "Seal", Seal(9));
                var mapper = Text_New(assembly, "VaultItemMapper");

                if (exclude)
                {
                    ProbeKit.Call(mapper, "ApplyToEntity", filled, entity, false);
                    Text_Expect(
                        problems,
                        Equals(ProbeKit.Get(entity, "Seal"), Seal(9)),
                        "未入力の seal で実体の現在値が変わった"
                    );
                    ColumnText_Input(filled, "Seal", Convert.ToBase64String([1, 2]));
                    ProbeKit.Call(mapper, "ApplyToEntity", filled, entity, false);
                    Text_Expect(
                        problems,
                        Equals(ProbeKit.Get(entity, "Seal"), Seal(1, 2)),
                        "入力した seal が実体へ写らない"
                    );
                }
                else
                {
                    Text_Expect(
                        problems,
                        Text_Throws(() =>
                            ProbeKit.Call(mapper, "ApplyToEntity", filled, entity, false)
                        ),
                        "seal が無いまま実体へ写せた（欠落は例外のはず）"
                    );
                }

                var noLabel = Text_New(assembly, "VaultItemEditModel");
                ColumnText_Input(noLabel, "ItemId", "1");
                ColumnText_Input(noLabel, "Seal", Convert.ToBase64String([1]));
                Text_Expect(
                    problems,
                    Text_Throws(() =>
                        ProbeKit.Call(
                            mapper,
                            "ApplyToEntity",
                            noLabel,
                            Text_New(assembly, "VaultItemEntity"),
                            false
                        )
                    ),
                    "label が無いまま実体へ写せた"
                );

                return problems;
            }
        );

    /// <summary>生成物の呼び出しが例外を投げるか</summary>
    private static bool Text_Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (TargetInvocationException)
        {
            return true;
        }
    }

    /// <summary>型 <paramref name="type"/> の ParentModel が <paramref name="parent"/>? を返す型付きの版である型が無い</summary>
    private static CodeRule Text_NoTypedParentModelOf(string parent) =>
        NoTypes(
            $"ParentModel が {parent}? を返す",
            type =>
                type.GetMembers("ParentModel")
                    .OfType<IPropertySymbol>()
                    .Any(property => property.Type.Name == parent)
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> TextEditModelRows() =>
        [
            TextRow(
                "code-navigation-entity-editmodel-mapper-graph",
                "Generate_ShouldCreateNavigationAndJsonIgnoreOnParentReference",
                Text_CustomerOrderNavigationDiagram,
                ServiceRoot(),
                code:
                [
                    // 実体のナビゲーションと EntityBase
                    Attribute(
                        "CustomerEntity",
                        "Orders",
                        "NavigationReference(\"customers\", \"customer_id\", \"orders\", \"customer_id\", true, true, false)"
                    ),
                    Member(
                        "CustomerEntity",
                        "Orders",
                        "ICollection<OrderEntity> Orders { get; set; }",
                        modifiers: "public"
                    ),
                    Attribute("OrderEntity", "Customer", "JsonIgnore"),
                    Member(
                        "OrderEntity",
                        "Customer",
                        "CustomerEntity Customer { get; set; }",
                        modifiers: "public"
                    ),
                    Attribute(
                        "OrderEntity",
                        "Customer",
                        "NavigationReference(\"customers\", \"customer_id\", \"orders\", \"customer_id\", false, false, true)"
                    ),
                    Type(
                        "CustomerEntity",
                        kind: "class",
                        modifiers: "public partial",
                        baseType: "EntityBase"
                    ),
                    Type("EntityBaseCore", kind: "class", modifiers: "public abstract"),
                    Member(
                        "EntityBaseCore",
                        "HasSameValues",
                        "bool HasSameValues(EntityBaseCore? other)",
                        modifiers: "public"
                    ),
                    Member(
                        "EntityBaseCore",
                        "GetValueHashCode",
                        "int GetValueHashCode()",
                        modifiers: "public"
                    ),
                    Member(
                        "EntityBaseCore",
                        "ToJson",
                        "string ToJson(bool writeIndented = false)",
                        modifiers: "public"
                    ),
                    Member(
                        "EntityBaseCore",
                        "Clone",
                        "EntityBaseCore Clone()",
                        modifiers: "public"
                    ),
                    Using("QuickEREntities.g.cs", "System.Text.Json"),
                    Using("QuickEREntities.g.cs", "System.Text.Json.Serialization"),
                    Using("QuickEREntities.g.cs", "System.Collections"),
                    Using("QuickEREntities.g.cs", "System.Reflection"),
                    Text_NoTypeDeclaring("_cloneJsonOptions"),
                    // EditModel の RowState と確定値の後段
                    Type("EditModelBaseCore", kind: "class", modifiers: "public abstract"),
                    Member(
                        "EditModelBaseCore",
                        "RowState",
                        "RowState RowState { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MarkAdded",
                        "void MarkAdded()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MarkRemoved",
                        "void MarkRemoved()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "AfterConfirmedValueSet",
                        "void AfterConfirmedValueSet(string propertyName)",
                        modifiers: "protected"
                    ),
                    CallArguments(
                        "EditModelBaseCore",
                        "AfterConfirmedValueSet",
                        "OnConfirmedValueChanged",
                        ["propertyName"]
                    ),
                    CallArguments(
                        "EditModelBaseCore",
                        "AfterConfirmedValueSet",
                        "ShouldMarkUpdated",
                        ["propertyName"]
                    ),
                    Calls("EditModelBaseCore", "AfterConfirmedValueSet", "MarkUpdated", count: 1),
                    CallArguments(
                        "CustomerEditModel",
                        "CustomerId",
                        "AfterConfirmedValueSet",
                        ["\"CustomerId\""]
                    ),
                    Member(
                        "EditModelBaseCore",
                        "OnConfirmedValueChanged",
                        "void OnConfirmedValueChanged(string propertyName)",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "ShouldMarkUpdated",
                        "bool ShouldMarkUpdated(string propertyName)",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "SetProperty",
                        "bool SetProperty<T>(ref T field, T value, string propertyName)",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "ExecuteLoad",
                        "void ExecuteLoad(Action action)",
                        modifiers: "public"
                    ),
                    // Mapper のロードの外枠と定型
                    CallOrder(
                        "MapperBaseCore<TEntity, TEditModel>",
                        "ApplyToEditModel",
                        ["BeginLoad", "EndLoad"]
                    ),
                    Member(
                        "CustomerMapper",
                        "LoadColumns",
                        "void LoadColumns(CustomerEntity entity, CustomerEditModel editModel)",
                        modifiers: "protected override"
                    ),
                    Member(
                        "CustomerMapper",
                        "CompleteLoad",
                        "void CompleteLoad(CustomerEntity entity, CustomerEditModel editModel)",
                        modifiers: "protected override"
                    ),
                    Type(
                        "MapperBase<TEntity, TEditModel>",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    Type(
                        "CustomerMapper",
                        baseType: "MapperBase<CustomerEntity, CustomerEditModel>"
                    ),
                    Type("OrderMapper", baseType: "MapperBase<OrderEntity, OrderEditModel>"),
                    Member(
                        "MapperBaseCore<TEntity, TEditModel>",
                        "CreateEditModel",
                        "TEditModel CreateEditModel()",
                        modifiers: "public"
                    ),
                    Member(
                        "CustomerMapper",
                        "AfterEditModelCreated",
                        "void AfterEditModelCreated(CustomerEditModel editModel)",
                        modifiers: "protected override"
                    ),
                    CallArguments(
                        "CustomerMapper",
                        "AfterEditModelCreated",
                        "OnEditModelCreated",
                        ["editModel"]
                    ),
                    Member(
                        "CustomerMapper",
                        "OnEditModelCreated",
                        "void OnEditModelCreated(CustomerEditModel editModel)",
                        modifiers: "partial"
                    ),
                    NoCalls("EntityBaseCore.MarkUpdated"),
                    CallArguments(
                        "CustomerMapper",
                        "ApplyToEntity",
                        "CreateEntities",
                        ["Orders", "includeRemoved"]
                    ),
                    // 削除追跡コレクションと変更の集約・一括操作
                    Type(
                        "EditModelCollection<T>",
                        kind: "class",
                        modifiers: "public sealed partial",
                        baseType: "ObservableCollection<T>"
                    ),
                    Member(
                        "MapperBaseCore<TEntity, TEditModel>",
                        "CreateEntities",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "HasChanges",
                        "bool HasChanges { get; }",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "AcceptChanges",
                        "void AcceptChanges(bool includeChildren = true)",
                        modifiers: "public"
                    ),
                    // 親モデルの取得
                    Member(
                        "EditModelBaseCore",
                        "ParentModel",
                        "EditModelBaseCore? ParentModel { get; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderEditModel",
                        "ParentModel",
                        "CustomerEditModel? ParentModel { get; }",
                        modifiers: "public new"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "SetParentModel",
                        "void SetParentModel(EditModelBaseCore? parentModel)",
                        modifiers: "internal"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "RaiseParentCollectionChanged",
                        "void RaiseParentCollectionChanged()",
                        modifiers: "internal"
                    ),
                    CallArguments(
                        "EditModelBaseCore",
                        "RaiseParentCollectionChanged",
                        "OnPropertyChanged",
                        ["\"ParentCollection\""]
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "OwnerModel",
                        "EditModelBaseCore? OwnerModel { get; set; }",
                        modifiers: "internal"
                    ),
                    CallArguments(
                        "EditModelCollection<T>",
                        "OwnerModel",
                        "SetParentModel",
                        ["value"],
                        receiver: "item"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "CollectErrors",
                        "IEnumerable<EditModelError> CollectErrors(bool includeChildren = true)",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "MoveTo",
                        "bool MoveTo(T item, int newIndex)",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "AddRange",
                        "void AddRange(IEnumerable<T> items)",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "InsertRange",
                        "void InsertRange(int index, IEnumerable<T> items)",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "RemoveAll",
                        "void RemoveAll()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "RemoveRange",
                        "void RemoveRange(int index, int count)",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        ".ctor",
                        "EditModelCollection(IEnumerable<T> items)",
                        modifiers: "public"
                    ),
                    Member(
                        "MapperBaseCore<TEntity, TEditModel>",
                        "CreateEditModels",
                        "EditModelCollection<TEditModel> CreateEditModels(IEnumerable<TEntity> entities)",
                        modifiers: "public"
                    ),
                    // 子コレクションのナビ
                    Member(
                        "CustomerEditModel",
                        "_orders",
                        "EditModelCollection<OrderEditModel> _orders",
                        modifiers: "private"
                    ),
                    Member(
                        "CustomerEditModel",
                        "Orders",
                        "EditModelCollection<OrderEditModel> Orders { get; set; }",
                        modifiers: "public"
                    ),
                    CallArguments("CustomerEditModel", "Orders", "GetChildren", ["_orders"]),
                    CallArguments(
                        "CustomerEditModel",
                        "Orders",
                        "SetChildren",
                        ["_orders", "value", "\"Orders\""]
                    ),
                    Member("CustomerMapper", "ApplyToEntity", modifiers: "public override"),
                    CallArguments("CustomerMapper", "CompleteLoad", "CreateEditModels", ["Orders"]),
                    // 検証
                    Type("EditModelMessages", kind: "class", modifiers: "public static"),
                    Member(
                        "EditModelMessages",
                        "Required",
                        "Func<string, string, string> Required { get; set; }",
                        modifiers: "public static"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "Validate",
                        "bool Validate(bool includeChildren = true)",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "ValidateSelf",
                        "void ValidateSelf()",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "EditModelBaseCore<TSelf>",
                        "ValidateSelf",
                        "void ValidateSelf()",
                        modifiers: "protected override"
                    ),
                    Calls("EditModelBaseCore<TSelf>", "ValidateSelf", "ClearRequiredError"),
                    Member(
                        "CustomerEditModel",
                        "OnValidate",
                        "void OnValidate()",
                        modifiers: "partial"
                    ),
                    FeatureText_NoNameContaining("ValidateChildren"),
                    FeatureText_NoNameContaining("CollectChildErrors"),
                    Member(
                        "EditModelBaseCore",
                        "RegisterChildren",
                        "void RegisterChildren()",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "RegisterExtraChildren",
                        "void RegisterExtraChildren()",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "AddChild",
                        "void AddChild(string name, Func<EditModelBaseCore?> accessor)",
                        modifiers: "protected"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "AddChildren",
                        "void AddChildren<T>(string name, Func<EditModelCollection<T>?> accessor)",
                        modifiers: "protected"
                    ),
                    Member(
                        "CustomerEditModel",
                        "RegisterChildren",
                        "void RegisterChildren()",
                        modifiers: "protected override"
                    ),
                    CallArguments(
                        "CustomerEditModel",
                        "RegisterChildren",
                        "AddChildren",
                        ["\"Orders\"", "?"]
                    ),
                    Type("EditModelError", kind: "record", modifiers: "public sealed"),
                    Member("EditModelError", "Path", "string Path { get; init; }"),
                    Member("EditModelError", "Property", "string Property { get; init; }"),
                    Member("EditModelError", "Message", "string Message { get; init; }"),
                    Member(
                        "EditModelBaseCore",
                        "CollectErrors",
                        "IEnumerable<EditModelError> CollectErrors(bool includeChildren = true)",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "CollectErrors",
                        "void CollectErrors(string path, bool includeChildren, List<EditModelError> errors)",
                        modifiers: "internal"
                    ),
                    CallArguments(
                        "EditModelBaseCore",
                        "CollectErrors",
                        "CollectOwnErrors",
                        ["path"]
                    ),
                    CallArguments(
                        "EditModelBaseCore",
                        "CollectErrors",
                        "CollectErrors",
                        ["path", "includeChildren", "errors"],
                        receiver: "link"
                    ),
                    FeatureText_NoNameContaining("OnCollectChildErrors"),
                ],
                exec:
                [
                    Text_RowStateTransitions("Customer", "CustomerId"),
                    Text_CustomerOrderGraph(),
                    Text_CustomerOrderValidation(),
                ]
            ),
            TextRow(
                "code-pascal-case-table-names-entity-and-navigation",
                "Generate_ShouldPreservePascalCaseTableNamesInEntityAndNavigationNames",
                Text_PascalCaseDiagram,
                ServiceRoot(),
                code:
                [
                    Type(
                        "AirconditionerCategoryEntity",
                        kind: "class",
                        modifiers: "public partial"
                    ),
                    Member(
                        "AirconditionerCategoryEntity",
                        "Airconditioners",
                        "ICollection<AirconditionerEntity> Airconditioners { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "AirconditionerEntity",
                        "AirconditionerCategory",
                        "AirconditionerCategoryEntity AirconditionerCategory { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-mapper-class-and-hooks",
                "Generate_ShouldCreateMapperClass",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "products",
                                Service_Column("product_id", "int", primaryKey: true),
                                Service_Column("name", "nvarchar(200)")
                            ),
                        ],
                    },
                ServiceRoot(),
                code:
                [
                    NoType("IProductMapper"),
                    Type(
                        "ProductMapper",
                        kind: "class",
                        modifiers: "public sealed partial",
                        baseType: "MapperBase<ProductEntity, ProductEditModel>",
                        interfaces: []
                    ),
                    Member(
                        "MapperBaseCore<TEntity, TEditModel>",
                        "CreateEditModel",
                        "TEditModel CreateEditModel(TEntity entity)",
                        modifiers: "public"
                    ),
                    Member("ProductMapper", "ApplyToEntity"),
                    FeatureText_NoNameContaining("CommitToEditModel"),
                    FeatureText_NoNameContaining("CommitToEntity"),
                    Member(
                        "MapperBaseCore<TEntity, TEditModel>",
                        "CreateEntity",
                        "TEntity CreateEntity()",
                        modifiers: "public"
                    ),
                    Member(
                        "ProductMapper",
                        "AfterEntityCreated",
                        "void AfterEntityCreated(ProductEntity entity)",
                        modifiers: "protected override"
                    ),
                    CallArguments(
                        "ProductMapper",
                        "AfterEntityCreated",
                        "OnEntityCreated",
                        ["entity"]
                    ),
                    Type(
                        "MapperBase<TEntity, TEditModel>",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    Member(
                        "MapperBaseCore<TEntity, TEditModel>",
                        "CreateEntity",
                        "TEntity CreateEntity(TEditModel editModel, bool includeRemoved)",
                        modifiers: "public"
                    ),
                    Member(
                        "ProductMapper",
                        "OnEntityCreated",
                        "void OnEntityCreated(ProductEntity entity)",
                        modifiers: "partial"
                    ),
                    CallArguments(
                        "ProductMapper",
                        "ApplyToEntity",
                        "Required",
                        ["ProductId", "\"ProductId\""]
                    ),
                    CallArguments(
                        "ProductMapper",
                        "ApplyToEntity",
                        "Required",
                        ["Name", "\"Name\""]
                    ),
                    Member(
                        "MapperBaseCore<TEntity, TEditModel>",
                        "ApplyToEditModel",
                        "void ApplyToEditModel(TEntity entity, TEditModel editModel)",
                        modifiers: "public"
                    ),
                    Member(
                        "ProductMapper",
                        "LoadColumns",
                        "void LoadColumns(ProductEntity entity, ProductEditModel editModel)",
                        modifiers: "protected override"
                    ),
                    Calls("MapperBaseCore<TEntity, TEditModel>", "ApplyToEditModel", "RevertInput"),
                    ReferencesSymbol(
                        "ProductMapper",
                        null,
                        "ProductEditModel.BindingName",
                        present: false
                    ),
                    FeatureText_NoNameContaining("LoadFrom"),
                    CallArguments(
                        "ProductMapper",
                        "CompleteLoad",
                        "OnEditModelLoaded",
                        ["entity", "editModel"]
                    ),
                    Member(
                        "ProductMapper",
                        "OnEditModelLoaded",
                        "void OnEditModelLoaded(ProductEntity entity, ProductEditModel editModel)",
                        modifiers: "partial"
                    ),
                    CallArguments(
                        "ProductMapper",
                        "ApplyToEntity",
                        "OnEntityApplied",
                        ["editModel", "entity"]
                    ),
                    Member(
                        "ProductMapper",
                        "OnEntityApplied",
                        "void OnEntityApplied(ProductEditModel editModel, ProductEntity entity)",
                        modifiers: "partial"
                    ),
                ],
                exec: [Text_ProductMapperBehavior()]
            ),
            TextRow(
                "code-editmodel-input-revert-siblings-row-edit",
                "Generate_EditModel_ShouldContainRevertInputMethod",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "orders",
                                Service_Column("order_id", "int", primaryKey: true),
                                Service_Column("amount", "decimal")
                            ),
                        ],
                    },
                ServiceRoot(),
                code:
                [
                    Member(
                        "OrderEditModel",
                        "OrderId",
                        "int? OrderId { get; internal set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderEditModel",
                        "Amount",
                        "decimal? Amount { get; internal set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderEditModel",
                        "BindingOrderId",
                        "string BindingOrderId { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderEditModel",
                        "BindingAmount",
                        "string BindingAmount { get; set; }",
                        modifiers: "public"
                    ),
                    Calls("OrderEditModel", "BindingOrderId", "ConvertParsedInput<int>", count: 1),
                    Calls(
                        "OrderEditModel",
                        "BindingAmount",
                        "ConvertParsedInput<decimal>",
                        count: 1
                    ),
                    CallArguments(
                        "OrderEditModel",
                        "BindingOrderId",
                        "GetDisplayName",
                        ["\"OrderId\"", "null"]
                    ),
                    CallArguments(
                        "OrderEditModel",
                        "BindingAmount",
                        "GetDisplayName",
                        ["\"Amount\"", "null"]
                    ),
                    Member(
                        "EditModelMessages",
                        "ParseFailed",
                        "Func<string, string, string, string, string> ParseFailed { get; set; }",
                        modifiers: "public static"
                    ),
                    FeatureText_NoNameContaining("CustomizeParseErrorMessage"),
                    FeatureText_NoNameContaining("CustomizeRequiredErrorMessage"),
                    FeatureText_NoNameContaining("CustomizeDuplicateErrorMessage"),
                    FeatureText_NoNameContaining("ResolveParseErrorMessage"),
                    FeatureText_NoNameContaining("BuildParseErrorMessage"),
                    FeatureText_NoNameContaining("BuildRequiredErrorMessage"),
                    Member(
                        "EditModelBaseCore",
                        "RevertInput",
                        "void RevertInput()",
                        modifiers: "public"
                    ),
                    Calls("EditModelBaseCore", "RevertInput", "ExecuteRevert", count: 1),
                    Member(
                        "EditModelBaseCore",
                        "RevertCore",
                        "void RevertCore()",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "EditModelBaseCore<TSelf>",
                        "RevertCore",
                        "void RevertCore()",
                        modifiers: "protected override"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "Owner",
                        "IList? Owner { get; set; }",
                        modifiers: "internal"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "GetNext",
                        "EditModelBaseCore? GetNext()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "GetPrevious",
                        "EditModelBaseCore? GetPrevious()",
                        modifiers: "public"
                    ),
                    Type(
                        "EditModelBaseCore<TSelf>",
                        kind: "class",
                        modifiers: "public abstract",
                        baseType: "EditModelBaseCore"
                    ),
                    Member(
                        "EditModelBaseCore<TSelf>",
                        "GetNext",
                        "TSelf? GetNext()",
                        modifiers: "public new"
                    ),
                    Member(
                        "EditModelBaseCore<TSelf>",
                        "GetPrevious",
                        "TSelf? GetPrevious()",
                        modifiers: "public new"
                    ),
                    Type("OrderEditModel", baseType: "EditModelBase<OrderEditModel>"),
                    Member(
                        "EditModelBaseCore",
                        "IndexInParent",
                        "int IndexInParent { get; }",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "IsFirstInParent",
                        "bool IsFirstInParent { get; }",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "IsLastInParent",
                        "bool IsLastInParent { get; }",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "RemoveFromParent",
                        "bool RemoveFromParent()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MoveToFirst",
                        "bool MoveToFirst()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MoveToLast",
                        "bool MoveToLast()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MoveToNext",
                        "bool MoveToNext()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MoveToPrevious",
                        "bool MoveToPrevious()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MoveCore",
                        "void MoveCore(int oldIndex, int newIndex)",
                        modifiers: "protected virtual"
                    ),
                    Member(
                        "EditModelBaseCore<TSelf>",
                        "ParentCollection",
                        "EditModelCollection<TSelf>? ParentCollection { get; }",
                        modifiers: "public"
                    ),
                    NoMemberInHierarchy("OrderEditModel", "Parent"),
                    Member(
                        "EditModelBaseCore",
                        "ParentModel",
                        "EditModelBaseCore? ParentModel { get; }",
                        modifiers: "public"
                    ),
                    NoMember("OrderEditModel", "ParentModel"),
                    Member(
                        "EditModelBaseCore<TSelf>",
                        "MoveCore",
                        "void MoveCore(int oldIndex, int newIndex)",
                        modifiers: "protected override"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "AcceptChanges",
                        "void AcceptChanges(bool includeChildren = true)",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "HasGraphChanges",
                        "bool HasGraphChanges(bool includeChildren = true)",
                        modifiers: "public"
                    ),
                    FeatureText_NoNameContaining("AcceptChildChanges"),
                    FeatureText_NoNameContaining("ChildHasChanges"),
                    NoMember("OrderEditModel", "RegisterChildren"),
                    Type(
                        "EditModelBaseCore",
                        interfaces:
                        [
                            "INotifyPropertyChanged",
                            "INotifyDataErrorInfo",
                            "IEditableObject",
                        ]
                    ),
                    Member(
                        "EditModelBaseCore",
                        "BeginEdit",
                        "void BeginEdit()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "CancelEdit",
                        "void CancelEdit()",
                        modifiers: "public"
                    ),
                    Member("EditModelBaseCore", "EndEdit", "void EndEdit()", modifiers: "public"),
                    Member(
                        "EditModelBaseCore<TSelf>",
                        "BeginEditCore",
                        "void BeginEditCore()",
                        modifiers: "protected override"
                    ),
                    Member(
                        "EditModelBaseCore<TSelf>",
                        "CancelEditCore",
                        "void CancelEditCore()",
                        modifiers: "protected override"
                    ),
                    Calls("EditModelBaseCore<TSelf>", "CancelEditCore", "ExecuteRevert", count: 1),
                    Member(
                        "OrderEditModel",
                        "EndEditCore",
                        "void EndEditCore()",
                        modifiers: "protected override"
                    ),
                    CallArguments("OrderEditModel", "EndEditCore", "OnEndEdit", []),
                    NoMember("OrderEditModel", "_orderIdSnapshot"),
                    NoMember("OrderEditModel", "_bindingOrderIdSnapshot"),
                    Member(
                        "OrderEditModel",
                        "OnBeginEdit",
                        "void OnBeginEdit()",
                        modifiers: "partial"
                    ),
                    Member("OrderEditModel", "OnEndEdit", "void OnEndEdit()", modifiers: "partial"),
                    Member(
                        "OrderEditModel",
                        "OnCancelEdit",
                        "void OnCancelEdit()",
                        modifiers: "partial"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "RaisePositionChanged",
                        "void RaisePositionChanged()",
                        modifiers: "internal"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "RaiseParentCollectionChanged",
                        "void RaiseParentCollectionChanged()",
                        modifiers: "internal"
                    ),
                    CallArguments(
                        "EditModelBaseCore",
                        "RaiseParentCollectionChanged",
                        "OnPropertyChanged",
                        ["\"ParentCollection\""]
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "NotifyPositionsChanged",
                        "void NotifyPositionsChanged()",
                        modifiers: "private"
                    ),
                    Member(
                        "EditModelCollection<T>",
                        "MoveItem",
                        "void MoveItem(int oldIndex, int newIndex)",
                        modifiers: "protected override"
                    ),
                ],
                exec: [Text_OrderEditModelBehavior()]
            ),
            TextRow(
                "code-editmodel-typed-parent-model-for-unambiguous-parents",
                "Generate_EditModel_ShouldEmitTypedParentModelForUnambiguousParents",
                Text_TypedParentDiagram,
                ServiceRoot() with
                {
                    GenerateMappers = false,
                    GenerateRepositories = false,
                },
                code:
                [
                    Member(
                        "ProfileOwnerEditModel",
                        "_profile",
                        "ProfileEditModel? _profile",
                        modifiers: "private"
                    ),
                    CallArguments("ProfileOwnerEditModel", "Profile", "SetParentModel", ["null"]),
                    CallArguments("ProfileOwnerEditModel", "Profile", "SetParentModel", ["this"]),
                    Member(
                        "ProfileEditModel",
                        "ParentModel",
                        "ProfileOwnerEditModel? ParentModel { get; }",
                        modifiers: "public new"
                    ),
                    Member(
                        "CategoryEditModel",
                        "ParentModel",
                        "CategoryEditModel? ParentModel { get; }",
                        modifiers: "public new"
                    ),
                    NoMember("LineItemEditModel", "ParentModel"),
                    Text_NoTypedParentModelOf("SalesOrderEditModel"),
                    Text_NoTypedParentModelOf("ProductEditModel"),
                ],
                exec: [Text_TypedParentModels()]
            ),
            TextRow(
                "code-editmodel-binary-and-value-type-binding-conversions",
                "Generate_EditModel_WithBinaryAndValueTypes_ShouldUseSafeBindingConversions",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "files",
                                Service_Column("file_id", "int", primaryKey: true),
                                Service_Column("is_active", "bit"),
                                Service_Column("filedata", "varbinary(max)")
                            ),
                        ],
                    },
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                },
                code:
                [
                    Member(
                        "FileEditModel",
                        "FileId",
                        "int? FileId { get; internal set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "FileEditModel",
                        "IsActive",
                        "bool? IsActive { get; internal set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "FileEntity",
                        "Filedata",
                        "byte[] Filedata { get; set; }",
                        modifiers: "public"
                    ),
                    ReferencesSymbol(
                        "FileMapper",
                        null,
                        "FileEditModel.BindingIsActive",
                        present: false
                    ),
                    ReferencesSymbol("FileMapper", null, "Convert.ToBase64String", present: false),
                    NoMember("FileEditModel", "_errorFiledata"),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "_metadata",
                        "EntitySaveMetadata _metadata",
                        modifiers: "private static readonly"
                    ),
                    CallArguments(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "_metadata",
                        "EntitySaveMetadata.For",
                        ["?"]
                    ),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "_connectionFactory",
                        "ISqlConnectionFactory _connectionFactory",
                        modifiers: "private readonly"
                    ),
                ],
                exec: [Text_FileBindingConversions()]
            ),
            TextRow(
                "code-nullable-binary-column-nullable-byte-array",
                "Generate_NullableBinaryColumn_ShouldUseNullableByteArray",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "files",
                                Service_Column("file_id", "int", primaryKey: true),
                                Service_Column("photo", "varbinary(max)", nullable: true)
                            ),
                        ],
                    },
                ServiceRoot(),
                code:
                [
                    Member(
                        "FileEntity",
                        "Photo",
                        "byte[]? Photo { get; set; }",
                        modifiers: "public"
                    ),
                ],
                exec: [Text_NullablePhotoMapper()]
            ),
            TextRow(
                "code-editmodel-change-hook-partial-methods",
                "Generate_EditModel_ShouldGenerateChangeHookPartialMethods",
                Service_CustomersDiagram,
                ServiceRoot(),
                code:
                [
                    Member(
                        "CustomerEditModel",
                        "OnCustomerIdChanging",
                        "void OnCustomerIdChanging(int? value)",
                        modifiers: "partial"
                    ),
                    Member(
                        "CustomerEditModel",
                        "OnCustomerIdChanging",
                        "void OnCustomerIdChanging(int? oldValue, int? newValue)",
                        modifiers: "partial"
                    ),
                    Member(
                        "CustomerEditModel",
                        "OnCustomerIdChanged",
                        "void OnCustomerIdChanged(int? value)",
                        modifiers: "partial"
                    ),
                    Member(
                        "CustomerEditModel",
                        "OnCustomerIdChanged",
                        "void OnCustomerIdChanged(int? oldValue, int? newValue)",
                        modifiers: "partial"
                    ),
                    Member(
                        "CustomerEditModel",
                        "OnNameChanged",
                        "void OnNameChanged(string? oldValue, string? newValue)",
                        modifiers: "partial"
                    ),
                    CallOrder(
                        "CustomerEditModel",
                        "CustomerId",
                        [
                            "OnCustomerIdChanging",
                            "OnCustomerIdChanging",
                            "OnCustomerIdChanged",
                            "OnCustomerIdChanged",
                        ]
                    ),
                    CallArguments(
                        "CustomerEditModel",
                        "CustomerId",
                        "OnCustomerIdChanging",
                        ["value"]
                    ),
                    CallArguments(
                        "CustomerEditModel",
                        "CustomerId",
                        "OnCustomerIdChanging",
                        ["oldValue", "value"]
                    ),
                    CallArguments(
                        "CustomerEditModel",
                        "CustomerId",
                        "OnCustomerIdChanged",
                        ["value"]
                    ),
                    CallArguments(
                        "CustomerEditModel",
                        "CustomerId",
                        "OnCustomerIdChanged",
                        ["oldValue", "value"]
                    ),
                ],
                extraSources: [Text_ChangeHookSource()],
                exec: [Text_ChangeHooks()]
            ),
            .. new[] { true, false }.Select(exclude =>
                TextRow(
                    exclude
                        ? "code-vo-excluded-binary-editmodel-mapper-treat-unset-as-current"
                        : "code-vo-binary-without-exclusion-editmodel-mapper-keep-required",
                    exclude
                        ? "Generate_ExcludeUnboundedBinary_On_EditModelAndMapper_TreatUnsetAsCurrentValue"
                        : "Generate_ExcludeUnboundedBinary_Off_EditModelAndMapper_KeepRequiredInput",
                    Text_VaultItemsDiagram,
                    ServiceRoot() with
                    {
                        GenerateRepositories = true,
                        GenerateValueObjects = true,
                        ExcludeUnboundedBinaryColumns = exclude,
                    },
                    code:
                    [
                        CallArguments(
                            "VaultItemMapper",
                            "ApplyToEntity",
                            "Required",
                            ["Seal", "\"Seal\""],
                            count: exclude ? 0 : 1
                        ),
                        CallArguments(
                            "VaultItemMapper",
                            "ApplyToEntity",
                            "Required",
                            ["NoteBlob", "\"NoteBlob\""],
                            count: 0
                        ),
                        CallArguments(
                            "VaultItemMapper",
                            "ApplyToEntity",
                            "Required",
                            ["Label", "\"Label\""]
                        ),
                    ],
                    exec: [Text_VaultEditModelAndMapper(exclude)]
                )
            ),
        ];

    // ===== 実行の探針の補助（EditModel・Mapper・DI の振る舞い） =====

    /// <summary>生成物の型 <paramref name="typeName"/> のインスタンスを引数なしで作る</summary>
    private static object Text_New(Assembly assembly, string typeName) =>
        ProbeKit.Create(ProbeKit.Type(assembly, typeName));

    /// <summary>EditModel・実体の <c>RowState</c> を名前で読む</summary>
    private static string Text_State(object target) =>
        ProbeKit.Get(target, "RowState")?.ToString() ?? "(null)";

    /// <summary>EditModel・実体の <c>RowState</c> を名前で書く</summary>
    private static void Text_SetState(object target, string state)
    {
        var property = target.GetType().GetProperty("RowState")!;
        ProbeKit.Set(target, "RowState", Enum.Parse(property.PropertyType, state));
    }

    /// <summary>Mapper（<c>{stem}Mapper</c>）で実体から EditModel を作る（ロード）</summary>
    private static object Text_Load(Assembly assembly, string stem, object entity) =>
        ProbeKit.Call(Text_New(assembly, stem + "Mapper"), "CreateEditModel", entity)!;

    /// <summary>Mapper（<c>{stem}Mapper</c>）で EditModel から実体を作る</summary>
    private static object Text_ToEntity(
        Assembly assembly,
        string stem,
        object model,
        bool includeRemoved = false
    ) => ProbeKit.Call(Text_New(assembly, stem + "Mapper"), "CreateEntity", model, includeRemoved)!;

    /// <summary>コレクションのプロパティ <paramref name="property"/> の要素を並べる</summary>
    private static IReadOnlyList<object> Text_Items(object target, string property) =>
        ((IEnumerable)ProbeKit.Get(target, property)!).Cast<object>().ToList();

    /// <summary>コレクションのプロパティ <paramref name="property"/> へ要素を足す</summary>
    private static void Text_Add(object target, string property, object item) =>
        ((IList)ProbeKit.Get(target, property)!).Add(item);

    /// <summary>
    /// 型付きの版（戻り値が <paramref name="target"/> 自身の型のもの）のメソッド <paramref name="method"/> を引数なしで呼ぶ
    /// （基底の版と <c>new</c> で隠した型付きの版が並ぶメソッド用）
    /// </summary>
    private static object? Text_CallTyped(object target, string method) =>
        target
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate =>
                candidate.Name == method
                && candidate.GetParameters().Length == 0
                && candidate.ReturnType == target.GetType()
            )
            .Invoke(target, null);

    /// <summary>条件が成り立たなければ問題 <paramref name="problem"/> を足す</summary>
    private static void Text_Expect(List<string> problems, bool condition, string problem)
    {
        if (!condition)
        {
            problems.Add(problem);
        }
    }

    /// <summary>生成物の静的クラスの <c>Func</c> のプロパティを一時的に差し替えて <paramref name="body"/> を流す（終われば戻す）</summary>
    private static IEnumerable<string> Text_WithStaticFunc(
        Assembly assembly,
        string typeName,
        string property,
        Delegate replacement,
        Func<IEnumerable<string>> body
    )
    {
        var type = ProbeKit.Type(assembly, typeName);
        var info = type.GetProperty(property, BindingFlags.Public | BindingFlags.Static)!;
        var previous = info.GetValue(null);

        try
        {
            info.SetValue(null, replacement);
            return body().ToList();
        }
        finally
        {
            info.SetValue(null, previous);
        }
    }

    /// <summary>DI の登録 <paramref name="services"/> に、サービス型 <paramref name="service"/>（単純名・型引数を含む表記可）が実装型 <paramref name="implementation"/> で寿命 <paramref name="lifetime"/> のまま 1 件ある</summary>
    private static IEnumerable<string> Text_Registered(
        IServiceCollection services,
        string service,
        string implementation,
        ServiceLifetime lifetime
    )
    {
        static string Show(Type? type) =>
            type is null ? "(なし)"
            : type.IsGenericType
                ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Show))}>"
            : type.Name;

        var found = services.Where(descriptor => Show(descriptor.ServiceType) == service).ToList();

        if (found.Count != 1)
        {
            yield return $"DI: {service} の登録が {found.Count} 件（期待 1 件）";
            yield break;
        }

        if (Show(found[0].ImplementationType) != implementation || found[0].Lifetime != lifetime)
        {
            yield return $"DI: {service} の登録が {Show(found[0].ImplementationType)}・{found[0].Lifetime}（期待 {implementation}・{lifetime}）";
        }
    }

    /// <summary>分割×フル構成（QuickER 版 Repository と EF Core の両方が ON。外部 using が発生し得る最大構成）</summary>
    private static CodeGenerationOptions Text_SplitFullOptions(bool valueObjects) =>
        ServiceRoot() with
        {
            SplitFilesByCategory = true,
            GenerateValueObjects = valueObjects,
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
        };

    /// <summary>分割・EF Core 単独（Repository 契約だけのファイルになる）</summary>
    private static CodeGenerationOptions Text_EfCoreOnlySplitOptions(bool valueObjects) =>
        ServiceRoot() with
        {
            SplitFilesByCategory = true,
            GenerateValueObjects = valueObjects,
            GenerateRepositories = false,
            GenerateEfCoreRepositories = true,
        };

    /// <summary>EF Core 単独（分割の有無・値オブジェクトの有無）</summary>
    private static CodeGenerationOptions Text_EfCoreOnlyOptions(bool split, bool valueObjects) =>
        ServiceRoot() with
        {
            SplitFilesByCategory = split,
            GenerateValueObjects = valueObjects,
            GenerateEfCoreRepositories = true,
            GenerateRepositories = false,
        };

    /// <summary>依存を使わないファイルに、EF Core・SqlClient・DI の using が 1 つも無い</summary>
    private static IReadOnlyList<CodeRule> Text_NoForeignUsings(params string[] files) =>
        [
            .. files.SelectMany(file =>
                new[]
                {
                    NoUsingUnder(file, "Microsoft.EntityFrameworkCore"),
                    NoUsingUnder(file, "Microsoft.Data.SqlClient"),
                    NoUsingUnder(file, "Microsoft.Extensions.DependencyInjection"),
                }
            ),
        ];

    /// <summary>EF Core 単独の分割で、契約だけの Repositories.g.cs が ADO の using を持たず、DI の using と Save フックの登録拡張を持つ</summary>
    private static IReadOnlyList<CodeRule> Text_RepositoryFileRules() =>
        [
            NoUsing("Repositories.g.cs", "Microsoft.Data.SqlClient"),
            NoUsing("Repositories.g.cs", "Microsoft.Data.Sqlite"),
            Using("Repositories.g.cs", "Microsoft.Extensions.DependencyInjection"),
            Type(
                "SaveHookServiceCollectionExtensions",
                file: "Repositories.g.cs",
                kind: "class",
                modifiers: "public static"
            ),
        ];

    /// <summary>Stream アクセサ（<c>ReadPhotoAsync</c>・<c>BinaryStreamExtensions</c> で終わる型）が無い</summary>
    private static IReadOnlyList<CodeRule> Text_NoBinaryStreamAccessors() =>
        [
            Text_NoTypeDeclaring("ReadPhotoAsync"),
            NoTypes(
                "名前が BinaryStreamExtensions で終わる",
                type => type.Name.EndsWith("BinaryStreamExtensions", StringComparison.Ordinal)
            ),
        ];

    // ---- 元のテストの図（そのまま写す） ----

    /// <summary>CSharpCodeGenerationServiceTests.RowVersionDiagram の図（documents の rowversion 列 row_version）</summary>
    private static ErDiagram Text_RowVersionDocumentsDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "documents",
                    Service_Column("document_id", "int", primaryKey: true),
                    Service_Column("row_version", "rowversion")
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.Generate_ManyEntities_ShouldNotHitScribanLoopLimit の図（主キーだけの表 1100 個）</summary>
    private static ErDiagram Text_ManyEntitiesDiagram() =>
        new()
        {
            Entities =
            [
                .. Enumerable
                    .Range(1, 1100)
                    .Select(index =>
                        Service_Entity(
                            $"items_{index}",
                            Service_Column("item_id", "int", primaryKey: true)
                        )
                    ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.Generate_ValueObjects_ConflictingSameNameColumns_ShouldWarnButGenerate の図（name 列の幅が違う 2 表）</summary>
    private static ErDiagram Text_ConflictingNameColumnsDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "customers",
                    Service_Column("customer_id", "int", primaryKey: true),
                    Service_Column("name", "nvarchar(50)")
                ),
                Service_Entity(
                    "products",
                    Service_Column("product_id", "int", primaryKey: true),
                    Service_Column("name", "nvarchar(100)")
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.Generate_ValueObjects_SByteColumn_ShouldDeriveOrderedBase の図（MySQL の tinyint 列 level）</summary>
    private static ErDiagram Text_SByteColumnDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "measures",
                    Service_Column("measure_id", "int", primaryKey: true),
                    Service_Column("level", "tinyint")
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.Generate_SnapshotSuffixColumn_ShouldGenerate の図（foo と foo_snapshot が同居する）</summary>
    private static ErDiagram Text_SnapshotSuffixDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "probes",
                    Service_Column("probe_id", "int", primaryKey: true),
                    Service_Column("foo", "int"),
                    Service_Column("foo_snapshot", "int", nullable: true)
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.Generate_ShouldPreservePascalCaseTableNamesInEntityAndNavigationNames の図</summary>
    private static ErDiagram Text_PascalCaseDiagram()
    {
        var categoryId = Service_Column("AirconditionerCategoryId", "int", primaryKey: true);
        var itemCategoryId = Service_Column("AirconditionerCategoryId", "int", foreignKey: true);
        var category = Service_Entity("AirconditionerCategory", categoryId);
        var item = Service_Entity(
            "Airconditioner",
            Service_Column("AirconditionerId", "int", primaryKey: true),
            itemCategoryId
        );

        return new ErDiagram
        {
            Entities = [category, item],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = category.Id,
                    TargetEntityId = item.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(categoryId.Id, itemCategoryId.Id)],
                },
            ],
        };
    }

    /// <summary>CSharpCodeGenerationServiceTests.Generate_EditModel_ShouldEmitTypedParentModelForUnambiguousParents の図（1 対 1・自己参照・複数親）</summary>
    private static ErDiagram Text_TypedParentDiagram()
    {
        var ownerPk = Service_Column("profile_owner_id", "int", primaryKey: true);
        var profileFk = Service_Column("profile_owner_id", "int", foreignKey: true);
        var categoryPk = Service_Column("category_id", "int", primaryKey: true);
        var categoryParentFk = Service_Column("parent_category_id", "int", foreignKey: true);
        var salesOrderPk = Service_Column("sales_order_id", "int", primaryKey: true);
        var productPk = Service_Column("product_id", "int", primaryKey: true);
        var lineOrderFk = Service_Column("sales_order_id", "int", foreignKey: true);
        var lineProductFk = Service_Column("product_id", "int", foreignKey: true);
        var owner = Service_Entity("profile_owner", ownerPk);
        var profile = Service_Entity(
            "profile",
            Service_Column("profile_id", "int", primaryKey: true),
            profileFk
        );
        var category = Service_Entity("category", categoryPk, categoryParentFk);
        var salesOrder = Service_Entity("sales_order", salesOrderPk);
        var product = Service_Entity("product", productPk);
        var lineItem = Service_Entity(
            "line_item",
            Service_Column("line_item_id", "int", primaryKey: true),
            lineOrderFk,
            lineProductFk
        );

        static Relationship Link(
            RelationshipType type,
            Entity source,
            Entity target,
            Column principal,
            Column dependent
        ) =>
            new()
            {
                Id = Guid.NewGuid(),
                Type = type,
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                ColumnPairs = [new(principal.Id, dependent.Id)],
            };

        return new ErDiagram
        {
            Entities = [owner, profile, category, salesOrder, product, lineItem],
            Relationships =
            [
                Link(RelationshipType.OneToOne, owner, profile, ownerPk, profileFk),
                Link(RelationshipType.OneToMany, category, category, categoryPk, categoryParentFk),
                Link(RelationshipType.OneToMany, salesOrder, lineItem, salesOrderPk, lineOrderFk),
                Link(RelationshipType.OneToMany, product, lineItem, productPk, lineProductFk),
            ],
        };
    }

    /// <summary>CSharpCodeGenerationServiceTests.BinaryEditModelDiagram の図（vault_items：非 NULL / NULL 許容の無制限バイナリ列と通常列）</summary>
    private static ErDiagram Text_VaultItemsDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "vault_items",
                    Service_Column("item_id", "int", primaryKey: true),
                    Service_Column("label", "nvarchar(50)"),
                    Service_Column("seal", "varbinary(max)"),
                    Service_Column("note_blob", "varbinary(max)", nullable: true)
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.Generate_Repository_ShouldEmitSqlColumnTypeAttributes の図（int・varchar(50)・nvarchar(max)・decimal(10,2)・geography）</summary>
    private static ErDiagram Text_SqlColumnTypeItemsDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "items",
                    Service_Column("item_id", "int", primaryKey: true),
                    Service_Column("code", "varchar(50)"),
                    Service_Column("note", "nvarchar(max)", nullable: true),
                    Service_Column("price", "decimal(10,2)"),
                    Service_Column("location", "geography", nullable: true)
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.Generate_RepositorySql_ShouldExcludeNavigationProperties の図（customers に name 列）</summary>
    private static ErDiagram Text_CustomerOrderWithNameDiagram() =>
        Service_CustomerOrderDiagram(
            parentKey: "customer_id",
            parentExtra: Service_Column("name", "nvarchar(100)"),
            childForeignKey: "customer_id"
        );

    /// <summary>CSharpCodeGenerationServiceTests.Generate_LargeSchemaExceeding1MB_ShouldNotTruncateOutput の図（4 列の表 200 個）</summary>
    private static ErDiagram Text_LargeSchemaDiagram() =>
        new()
        {
            Entities =
            [
                .. Enumerable
                    .Range(1, 200)
                    .Select(index =>
                        Service_Entity(
                            $"table_{index}",
                            Service_Column("id", "int", primaryKey: true),
                            Service_Column("name", "nvarchar(100)"),
                            Service_Column("amount", "decimal", nullable: true),
                            Service_Column("created_at", "datetime2")
                        )
                    ),
            ],
        };

    /// <summary>QuickER 版 Repository の主コンストラクタ（接続ファクトリ・Save フック・生 SQL 実行器）</summary>
    private static CodeRule Text_RepositoryConstructor(string repository) =>
        Member(
            repository,
            ".ctor",
            $"{repository}(ISqlConnectionFactory connectionFactory, ISaveHookRegistry? saveHooks = null, ISqlExecutor? sqlExecutor = null)",
            modifiers: "public sealed partial"
        );

    /// <summary>DI の拡張 <c>AddGeneratedSqlServerRepositories(接続文字列)</c> の登録に、<paramref name="expected"/> の組がある</summary>
    private static ExecCheck Text_SqlServerRegistrations(
        params (string Service, string Implementation, ServiceLifetime Lifetime)[] expected
    ) =>
        new(
            "sqlserver-registrations",
            assembly =>
            {
                var services = ProbeKit.Services(
                    assembly,
                    "AddGeneratedSqlServerRepositories",
                    "Server=(local);Database=probe;Integrated Security=true"
                );
                return expected.SelectMany(item =>
                    Text_Registered(services, item.Service, item.Implementation, item.Lifetime)
                );
            }
        );

    /// <summary>
    /// ナビゲーションのプロパティは列にならない：ナビゲーションを張った実体を QuickER 版 Repository（SQLite 方言）で実際に INSERT・SELECT でき、
    /// 読み戻した列の値が入れたとおり（ナビゲーションが列に混ざれば SQLite が存在しない列として断る）
    /// </summary>
    private static ExecCheck Text_SqliteNavigationNotColumns() =>
        new(
            "sqlite-navigation-not-columns",
            assembly =>
                ProbeKit.WithSqlite(
                    assembly,
                    Text_CustomerOrderWithNameDiagram(),
                    async (provider, _) =>
                    {
                        var problems = new List<string>();
                        var customers = provider.GetRequiredService(
                            ProbeKit.Type(assembly, "ICustomerRepository")
                        );
                        var orders = provider.GetRequiredService(
                            ProbeKit.Type(assembly, "IOrderRepository")
                        );
                        var customer = Text_New(assembly, "CustomerEntity");
                        ProbeKit.Set(customer, "CustomerId", 1);
                        ProbeKit.Set(customer, "Name", "Alice");
                        await (Task)
                            ProbeKit.Call(
                                customers,
                                "InsertAsync",
                                customer,
                                CancellationToken.None
                            )!;

                        var order = Text_New(assembly, "OrderEntity");
                        ProbeKit.Set(order, "OrderId", 10);
                        ProbeKit.Set(order, "CustomerId", 1);
                        ProbeKit.Set(order, "Customer", customer);
                        await (Task)
                            ProbeKit.Call(orders, "InsertAsync", order, CancellationToken.None)!;

                        var loadedCustomer = ProbeKit.Await(
                            ProbeKit.Call(customers, "GetByIdAsync", 1, CancellationToken.None)
                        );
                        var loadedOrder = ProbeKit.Await(
                            ProbeKit.Call(orders, "GetByIdAsync", 10, CancellationToken.None)
                        );
                        Text_Expect(
                            problems,
                            loadedCustomer is not null
                                && Equals(ProbeKit.Get(loadedCustomer, "Name"), "Alice"),
                            "ナビゲーションを張った customers の行を INSERT・SELECT で読み戻せない"
                        );
                        Text_Expect(
                            problems,
                            loadedOrder is not null
                                && Equals(ProbeKit.Get(loadedOrder, "CustomerId"), 1),
                            "親参照を張った orders の行を INSERT・SELECT で読み戻せない"
                        );
                        return problems;
                    }
                )
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> TextRepositoryRows()
    {
        var repositories = ServiceRoot() with { GenerateRepositories = true };

        return
        [
            TextRow(
                "code-repository-infrastructure-contracts-and-registration",
                "Generate_ShouldCreateRepositoryInfrastructure",
                Service_CustomersDiagram,
                repositories,
                code:
                [
                    Using("QuickEREntities.g.cs", "Microsoft.Data.SqlClient"),
                    Using("QuickEREntities.g.cs", "Microsoft.Extensions.DependencyInjection"),
                    Type(
                        "IRepository<TEntity, TKey>",
                        kind: "interface",
                        modifiers: "public partial"
                    ),
                    Type(
                        "SqlServerRepository<TEntity, TKey>",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    Type("EntitySaveMetadata", kind: "class", modifiers: "internal sealed"),
                    Type(
                        "ICustomerRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRepository<CustomerEntity, int>"]
                    ),
                    Type("CustomerRepository", kind: "class", modifiers: "public sealed partial"),
                    Text_RepositoryConstructor("CustomerRepository"),
                    CallArguments(
                        "EntitySaveMetadata",
                        "Build",
                        "EntitySaveMetadata.QuoteTableName",
                        ["Name"]
                    ),
                ],
                exec:
                [
                    Text_SqlServerRegistrations(
                        ("ICustomerRepository", "CustomerRepository", ServiceLifetime.Scoped)
                    ),
                ]
            ),
            TextRow(
                "code-repository-raw-sql-methods-delegate-to-executor",
                "Generate_Repository_ShouldEmitRawSqlMethods",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Member(
                        "IRepositoryCore<TEntity, TKey>",
                        "QueryBySqlAsync",
                        "Task<IReadOnlyList<TEntity>> QueryBySqlAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "IRepositoryCore<TEntity, TKey>",
                        "ExecuteSqlAsync",
                        "Task<int> ExecuteSqlAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "IRepositoryCore<TEntity, TKey>",
                        "ExecuteScalarSqlAsync",
                        "Task<TResult?> ExecuteScalarSqlAsync<TResult>(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "QueryBySqlAsync",
                        "Task<IReadOnlyList<TEntity>> QueryBySqlAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "ExecuteSqlAsync",
                        "Task<int> ExecuteSqlAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "ExecuteScalarSqlAsync",
                        "Task<TResult?> ExecuteScalarSqlAsync<TResult>(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    CallArguments(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "QueryBySqlAsync",
                        "QueryBySqlAsync<TEntity>",
                        ["sql", "parameters", "cancellationToken"],
                        receiver: "_sqlExecutor"
                    ),
                    CallArguments(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "ExecuteSqlAsync",
                        "ExecuteSqlAsync",
                        ["sql", "parameters", "cancellationToken"],
                        receiver: "_sqlExecutor"
                    ),
                    CallArguments(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "ExecuteScalarSqlAsync",
                        "ExecuteScalarSqlAsync<TResult>",
                        ["sql", "parameters", "cancellationToken"],
                        receiver: "_sqlExecutor"
                    ),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "_sqlExecutor",
                        "ISqlExecutor _sqlExecutor",
                        modifiers: "private readonly"
                    ),
                    Member(
                        "SqlExecutor",
                        "BindParameters",
                        "void BindParameters(SqlCommand command, object? parameters)",
                        modifiers: "internal static"
                    ),
                    FeatureText_NoNameContaining("BindRawSqlParameters"),
                    Member(
                        "EntitySaveMetadata",
                        "MapEntityFromRawSql",
                        "TEntity MapEntityFromRawSql<TEntity>(SqlDataReader reader)",
                        modifiers: "public"
                    ),
                    CallArguments(
                        "RawSqlMapper",
                        "ConvertSingleValue",
                        "RawValueConverter.ConvertRaw",
                        ["raw", "targetType"]
                    ),
                    CallArguments(
                        "RawValueConverter",
                        "ConvertRaw",
                        "ConvertCore",
                        ["raw", "targetType", "CultureInfo.InvariantCulture", "true"]
                    ),
                    CallArguments(
                        "RawValueConverter",
                        "ConvertCore",
                        "Convert.ChangeType",
                        ["raw", "underlying", "provider"]
                    ),
                    Using("QuickEREntities.g.cs", "System.Globalization"),
                ]
            ),
            TextRow(
                "code-repository-sql-executor-and-projection",
                "Generate_Repository_ShouldEmitSqlExecutorAndProjection",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Type("ISqlExecutor", kind: "interface", modifiers: "public partial"),
                    Type(
                        "SqlExecutor",
                        kind: "class",
                        modifiers: "public sealed partial",
                        interfaces: ["ISqlExecutor"]
                    ),
                    Member(
                        "SqlExecutor",
                        ".ctor",
                        "SqlExecutor(ISqlConnectionFactory connectionFactory)",
                        modifiers: "public sealed partial"
                    ),
                    Member(
                        "ISqlExecutor",
                        "QueryProjectionBySqlAsync",
                        "Task<IReadOnlyList<TResult>> QueryProjectionBySqlAsync<TResult>(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "SqlExecutor",
                        "QueryProjectionBySqlAsync",
                        "Task<IReadOnlyList<TResult>> QueryProjectionBySqlAsync<TResult>(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    Member(
                        "SqlExecutor",
                        "QueryBySqlAsync",
                        "Task<IReadOnlyList<TEntity>> QueryBySqlAsync<TEntity>(string sql, object? parameters = null, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    Member(
                        "RawSqlMapper",
                        "IsSingleValueType",
                        "bool IsSingleValueType(Type type)",
                        modifiers: "private static"
                    ),
                    Member(
                        "RawSqlMapper",
                        "_projectionAccessorCache",
                        "ConcurrentDictionary<Type, ProjectionAccessor> _projectionAccessorCache",
                        modifiers: "private static readonly"
                    ),
                ],
                exec:
                [
                    Text_SqlServerRegistrations(
                        ("ISqlExecutor", "SqlExecutor", ServiceLifetime.Singleton)
                    ),
                ]
            ),
            TextRow(
                "code-repository-sql-column-type-attributes-by-db-type",
                "Generate_Repository_ShouldEmitSqlColumnTypeAttributes",
                Text_SqlColumnTypeItemsDiagram,
                repositories,
                code:
                [
                    Type(
                        "SqlColumnTypeAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    Member(
                        "SqlColumnTypeAttribute",
                        ".ctor",
                        "SqlColumnTypeAttribute(SqlDbType dbType)",
                        modifiers: "public"
                    ),
                    Attribute("ItemEntity", "ItemId", "SqlColumnType(SqlDbType.Int)"),
                    Attribute("ItemEntity", "Code", "SqlColumnType(SqlDbType.VarChar, Size = 50)"),
                    Attribute("ItemEntity", "Note", "SqlColumnType(SqlDbType.NVarChar, Size = -1)"),
                    Attribute(
                        "ItemEntity",
                        "Price",
                        "SqlColumnType(SqlDbType.Decimal, Precision = 10, Scale = 2)"
                    ),
                    NoAttribute("ItemEntity", "Location", "SqlColumnType"),
                    Member("EntitySaveMetadata", "AddColumnParameter", modifiers: "private static"),
                ]
            ),
            TextRow(
                "code-repository-lambda-query-builder",
                "Generate_ShouldCreateLambdaQueryBuilder",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Using("QuickEREntities.g.cs", "System.Linq.Expressions"),
                    Type("SqlQuery<TEntity>", kind: "class", modifiers: "public sealed"),
                    Type("SqlExpressionTranslator", kind: "class", modifiers: "internal static"),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "Query",
                        "SqlQuery<TEntity> Query()",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "Where",
                        "SqlQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "OrderBy",
                        "SqlQuery<TEntity> OrderBy(Expression<Func<TEntity, object?>> keySelector)",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "OrderByDescending",
                        "SqlQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object?>> keySelector)",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "Take",
                        "SqlQuery<TEntity> Take(int count)",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "Skip",
                        "SqlQuery<TEntity> Skip(int count)",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "ToListAsync",
                        "Task<IReadOnlyList<TEntity>> ToListAsync(CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "FirstOrDefaultAsync",
                        "Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "CountAsync",
                        "Task<int> CountAsync(CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "AnyAsync",
                        "Task<bool> AnyAsync(CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    CallArguments(
                        "SqlServerSqlQueryExecutor<TEntity>",
                        "AddParameters",
                        "AddQueryParameter",
                        ["command", "Name", "ColumnName", "value", "VariableLength"],
                        receiver: "metadata"
                    ),
                ]
            ),
            TextRow(
                "code-repository-collection-contains-in-clause-helpers",
                "Generate_ShouldTranslateCollectionContainsToInClause",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Member("SqlExpressionTranslator", "TryGetIn", modifiers: "private static"),
                    Member("SqlExpressionTranslator", "BuildInClause", modifiers: "private static"),
                    Calls("SqlExpressionTranslator", "Visit", "TryGetIn"),
                    CallArguments(
                        "SqlExpressionTranslator",
                        "Visit",
                        "BuildInClause",
                        ["inColumn", "inCollection", "parameters"]
                    ),
                    CallArguments(
                        "SqlExpressionTranslator",
                        "Visit",
                        "BuildInClause",
                        ["notInColumn", "notInCollection", "parameters", "true"]
                    ),
                    Using("QuickEREntities.g.cs", "System.Collections"),
                ]
            ),
            TextRow(
                "code-repository-null-or-empty-date-part-equals-helpers",
                "Generate_ShouldTranslateNullOrEmptyDatePartAndEquals",
                SingleEntityDiagram,
                repositories with
                {
                    GenerateValueObjects = true,
                },
                code:
                [
                    Member(
                        "SqlExpressionTranslator",
                        "TryGetNullOrEmpty",
                        modifiers: "private static"
                    ),
                    Member(
                        "SqlExpressionTranslator",
                        "TryGetDatePart",
                        "bool TryGetDatePart(Expression expression, out string sql)",
                        modifiers: "private static"
                    ),
                    Member("SqlExpressionTranslator", "TryGetEquals", modifiers: "private static"),
                    Member(
                        "SqlExpressionTranslator",
                        "TryColumnName",
                        "string? TryColumnName(Expression expression)",
                        modifiers: "private static"
                    ),
                ]
            ),
            TextRow(
                "code-value-object-typed-materializer-table",
                "Generate_ValueObjects_ShouldKeepTypeSpecializedMaterializer",
                SingleEntityDiagram,
                repositories with
                {
                    GenerateValueObjects = true,
                },
                code:
                [
                    Member(
                        "EntitySaveMetadata",
                        "_typedReaders",
                        "IReadOnlyDictionary<Type, MethodInfo> _typedReaders",
                        modifiers: "private static readonly"
                    ),
                    CallArguments(
                        "EntitySaveMetadata",
                        "BuildTypedReaders",
                        "Getter",
                        ["\"GetDecimal\""]
                    ),
                ]
            ),
            TextRow(
                "code-repository-cascade-save-infrastructure",
                "Generate_ShouldCreateCascadeSaveInfrastructure",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Type("RowState", kind: "enum", modifiers: "public"),
                    Type("EntityBaseCore", kind: "class", modifiers: "public abstract"),
                    Member(
                        "EntityBaseCore",
                        "RowState",
                        "RowState RowState { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MarkAdded",
                        "void MarkAdded()",
                        modifiers: "public"
                    ),
                    Member(
                        "EditModelBaseCore",
                        "MarkRemoved",
                        "void MarkRemoved()",
                        modifiers: "public"
                    ),
                    Member(
                        "IRemoteRepositoryCore<TEntity, TKey>",
                        "SaveAsync",
                        "Task<int> SaveAsync(TEntity entity, bool cascadeSave = true, bool cascadeDelete = true, bool insertWhenUpdateMissing = false, ConcurrencyMode mode = Optimistic, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "IRemoteRepositoryCore<TEntity, TKey>",
                        "SaveAsync",
                        "Task<int> SaveAsync(IEnumerable<TEntity> entities, bool cascadeSave = true, bool cascadeDelete = true, bool insertWhenUpdateMissing = false, ConcurrencyMode mode = Optimistic, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "SaveAsync",
                        modifiers: "public async",
                        count: 2
                    ),
                    CallArguments(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "SaveAsync",
                        "BeginTransactionAsync",
                        ["cancellationToken"],
                        receiver: "connection",
                        count: 2
                    ),
                    Member(
                        "IRepositoryCore<TEntity, TKey>",
                        "BulkInsertAsync",
                        "Task<int> BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "BulkInsertAsync",
                        "Task<int> BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    Calls(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "BulkInsertAsync",
                        "CreateDataReader",
                        count: 1
                    ),
                    Type(
                        "EntityDataReader",
                        kind: "class",
                        modifiers: "private sealed",
                        interfaces: ["IDataReader"]
                    ),
                    Type("EntityGraphSaver", kind: "class", modifiers: "internal static"),
                    Type("EntitySaveMetadata", kind: "class", modifiers: "internal sealed"),
                    Type(
                        "SaveConflictException",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Exception"
                    ),
                    Type("CascadeNavigation", kind: "record", modifiers: "internal sealed"),
                    Calls("EntityGraphSaver", "UpdateAsync", "SaveConflictException.NotFound"),
                    Member(
                        "SaveConflictException",
                        "NotFound",
                        modifiers: "public static",
                        count: 2
                    ),
                    Member("SaveConflictException", "Modified", modifiers: "public static"),
                    NoCalls("EntityBaseCore.MarkUpdated"),
                ],
                exec: [Text_RowStateTransitions("Item", "ItemId")]
            ),
            TextRow(
                "code-repository-json-include-query-helpers",
                "Generate_ShouldCreateJsonIncludeQuery",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Using("QuickEREntities.g.cs", "System.Text.Json"),
                    CallArguments(
                        "SqlServerSqlQueryExecutor<TEntity>",
                        "ToListAsync",
                        "JsonSerializer.Deserialize<List<TEntity>>",
                        ["json", "SqlServerSqlQueryExecutor.JsonOptions"]
                    ),
                    Type("JsonQueryPlanner", kind: "class", modifiers: "internal static"),
                    Member(
                        "SqlQuery<TEntity>",
                        "Include",
                        "IncludableSqlQuery<TEntity, TProperty> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigationSelector)",
                        modifiers: "public"
                    ),
                    Member(
                        "SqlQuery<TEntity>",
                        "Include",
                        "IncludableSqlQuery<TEntity, TElement> Include<TElement>(Expression<Func<TEntity, ICollection<TElement>>> navigationSelector)",
                        modifiers: "public"
                    ),
                    Member(
                        "IncludableSqlQuery<TEntity, TProperty>",
                        "ThenInclude",
                        "IncludableSqlQuery<TEntity, TNext> ThenInclude<TNext>(Expression<Func<TProperty, TNext>> navigationSelector)",
                        modifiers: "public"
                    ),
                    Member(
                        "IncludableSqlQuery<TEntity, TProperty>",
                        "ThenInclude",
                        "IncludableSqlQuery<TEntity, TNext> ThenInclude<TNext>(Expression<Func<TProperty, ICollection<TNext>>> navigationSelector)",
                        modifiers: "public"
                    ),
                    CallArguments(
                        "SqlServerSqlQueryExecutor<TEntity>",
                        "ReadJsonAsync",
                        "String.Concat",
                        ["chunks"]
                    ),
                ]
            ),
            TextRow(
                "code-repository-execute-delete-and-cascade-planner",
                "Generate_ShouldCreateExecuteDeleteOnQuery",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Member("SqlQuery<TEntity>", "ExecuteDeleteAsync", modifiers: "public async"),
                    Member(
                        "IRemoteRepositoryCore<TEntity, TKey>",
                        "DeleteAsync",
                        "Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Type("CascadeDeletePlanner", kind: "class", modifiers: "internal static"),
                    Member(
                        "CascadeDeletePlanner",
                        "BuildDeleteStatements",
                        modifiers: "public static"
                    ),
                    Member(
                        "EntitySaveMetadata",
                        "CascadeNavigations",
                        "IReadOnlyList<CascadeNavigation> CascadeNavigations { get; init; }",
                        modifiers: "public required"
                    ),
                ]
            ),
            TextRow(
                "code-repository-navigation-properties-declared",
                "Generate_RepositorySql_ShouldExcludeNavigationProperties",
                Text_CustomerOrderWithNameDiagram,
                repositories,
                code:
                [
                    Calls(
                        "SqlServerSqlQueryExecutor<TEntity>",
                        "IncludeNavigationProperties",
                        "GetCustomAttribute<NavigationReferenceAttribute>"
                    ),
                    Member(
                        "CustomerEntity",
                        "Orders",
                        "ICollection<OrderEntity> Orders { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderEntity",
                        "Customer",
                        "CustomerEntity Customer { get; set; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "exec-sqlite-repository-navigation-properties-are-not-columns",
                "Generate_RepositorySql_ShouldExcludeNavigationProperties（SQLite 方言で実行。元の SQL の断片の不在は SQL を実行時に組むため空振り）",
                Text_CustomerOrderWithNameDiagram,
                repositories with
                {
                    RepositoryDialects = [DialectNames.Sqlite],
                },
                typeDialect: DialectNames.Sqlite,
                exec: [Text_SqliteNavigationNotColumns()]
            ),
            TextRow(
                "code-large-schema-emits-last-entity-and-repository",
                "Generate_LargeSchemaExceeding1MB_ShouldNotTruncateOutput",
                Text_LargeSchemaDiagram,
                repositories,
                code:
                [
                    Type("Table200Entity", kind: "class", modifiers: "public partial"),
                    Type("Table200Repository", kind: "class", modifiers: "public sealed partial"),
                    Text_RepositoryConstructor("Table200Repository"),
                ]
            ),
            TextRow(
                "code-repository-navigation-reference-attribute-defined",
                "Generate_RepositoryWithoutRelationships_ShouldEmitNavigationReferenceAttribute",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Type(
                        "NavigationReferenceAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    Calls(
                        "SqlServerSqlQueryExecutor<TEntity>",
                        "IncludeNavigationProperties",
                        "GetCustomAttribute<NavigationReferenceAttribute>"
                    ),
                ]
            ),
            TextRow(
                "code-sql-column-type-integral-digits-property",
                "Generate_SqlColumnType_IntegralDigitsProperty_ShouldComputeFromPrecisionAndScale",
                Service_ValueObjectDiagram,
                ServiceRoot(),
                code:
                [
                    Member(
                        "SqlColumnTypeAttribute",
                        "IntegralDigits",
                        "int IntegralDigits { get; }",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-runtime-core-api-signatures",
                "Generate_RuntimeCoreApis_ShouldEmitExpectedSignatures",
                SingleEntityDiagram,
                repositories,
                code:
                [
                    Type(
                        "SaveHookRegistry",
                        kind: "class",
                        modifiers: "internal sealed",
                        interfaces: ["ISaveHookRegistry"]
                    ),
                    Member(
                        "SaveHookRegistry",
                        "Add",
                        "SaveHookRegistry Add<TEntity>(ISaveHook<TEntity> hook)",
                        modifiers: "public"
                    ),
                    Type(
                        "ServiceProviderSaveHookRegistry",
                        kind: "class",
                        modifiers: "internal sealed"
                    ),
                    Type(
                        "SaveHookServiceCollectionExtensions",
                        kind: "class",
                        modifiers: "public static"
                    ),
                    Member(
                        "SaveHookServiceCollectionExtensions",
                        "AddSaveHook",
                        "IServiceCollection AddSaveHook(this IServiceCollection services, object hook)",
                        modifiers: "public static"
                    ),
                    Member(
                        "EntityBaseCore",
                        "MarkAdded",
                        "void MarkAdded(bool includeChildren = false)",
                        modifiers: "public",
                        count: 1
                    ),
                    Member("EntityBaseCore", "MarkAdded", count: 1),
                    Member(
                        "EntityBaseCore",
                        "MarkRemoved",
                        "void MarkRemoved()",
                        modifiers: "public"
                    ),
                    Member("EntityBaseCore", "MarkRemoved", count: 1),
                    Type("SaveConflictReason", kind: "enum", modifiers: "public"),
                    Member(
                        "SaveConflictException",
                        "Reason",
                        "SaveConflictReason Reason { get; }",
                        modifiers: "public"
                    ),
                    Member(
                        "SaveConflictException",
                        "EntityTypeName",
                        "string? EntityTypeName { get; }",
                        modifiers: "public"
                    ),
                    Member(
                        "SaveConflictException",
                        ".ctor",
                        "SaveConflictException(string message, SaveConflictReason reason, string? entityTypeName, string? key)",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-excluded-unbounded-binary-marker-on-photo-only",
                "Generate_ExcludeUnboundedBinary_On_MarksColumnAndReportsInfo",
                Service_BinaryColumnDiagram,
                ServiceRoot() with
                {
                    ExcludeUnboundedBinaryColumns = true,
                },
                code:
                [
                    Type(
                        "UnboundedBinaryColumnAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    Attribute("DocumentEntity", "Photo", "UnboundedBinaryColumn"),
                    Member(
                        "DocumentEntity",
                        "Photo",
                        "byte[]? Photo { get; set; }",
                        modifiers: "public"
                    ),
                    PropertiesWithAttribute("UnboundedBinaryColumn", 1),
                ],
                diagnostics: DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_ExcludedUnboundedBinaryColumns),
                        "DocumentEntity.Photo (documents.photo)"
                    ),
                    1
                )
            ),
            TextRow(
                "code-rowversion-concurrency-contract-and-metadata",
                "Generate_RowVersionColumn_EmitsConcurrencyContractAndVersionedSql",
                Service_BinaryColumnDiagram,
                repositories,
                code:
                [
                    Type("ConcurrencyMode", kind: "enum", modifiers: "public"),
                    Member("ConcurrencyMode", "Optimistic"),
                    Member("ConcurrencyMode", "ForceOverwrite"),
                    Member(
                        "IRemoteRepositoryCore<TEntity, TKey>",
                        "UpdateAsync",
                        "Task<bool> UpdateAsync(TEntity entity, ConcurrencyMode mode = Optimistic, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "EntitySaveMetadata",
                        "RowVersionProperty",
                        "PropertyInfo? RowVersionProperty { get; init; }",
                        modifiers: "public"
                    ),
                    .. new[]
                    {
                        "InsertReturningSql",
                        "UpdateVersionedSql",
                        "UpdateForcedSql",
                        "DeleteVersionedSql",
                    }.Select(name =>
                        Member(
                            "EntitySaveMetadata",
                            name,
                            $"string? {name} {{ get; init; }}",
                            modifiers: "public"
                        )
                    ),
                    Member(
                        "EntitySaveMetadata",
                        "BindRowVersionParameter",
                        "void BindRowVersionParameter(SqlCommand command, EntityBaseCore entity)",
                        modifiers: "public"
                    ),
                    Type("RowVersionCollector", kind: "class", modifiers: "internal sealed"),
                    Type("RowVersionConcurrency", kind: "class", modifiers: "internal static"),
                ]
            ),
            TextRow(
                "code-binary-stream-accessors-write-and-delegation",
                "Generate_BinaryStreamAccessors_On_GeneratesContractAndSugar",
                Service_BinaryColumnDiagram,
                repositories with
                {
                    ExcludeUnboundedBinaryColumns = true,
                },
                code:
                [
                    Member(
                        "IDocumentRepository",
                        "ReadPhotoAsync",
                        "Task<bool> ReadPhotoAsync(int id, Stream destination, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "IDocumentRepository",
                        "WritePhotoAsync",
                        "Task<bool> WritePhotoAsync(int id, Stream? source, long? length = null, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    CallArguments(
                        "DocumentRepository",
                        "ReadPhotoAsync",
                        "ReadUnboundedBinaryColumnAsync",
                        ["\"Photo\"", "id", "destination", "cancellationToken"]
                    ),
                    CallArguments(
                        "DocumentRepository",
                        "WritePhotoAsync",
                        "WriteUnboundedBinaryColumnAsync",
                        ["\"Photo\"", "id", "source", "length", "cancellationToken"]
                    ),
                    Type(
                        "DocumentRepositoryBinaryStreamExtensions",
                        kind: "class",
                        modifiers: "public static"
                    ),
                    Member("DocumentRepositoryBinaryStreamExtensions", "ReadPhotoToFileAsync"),
                    Member("DocumentRepositoryBinaryStreamExtensions", "WritePhotoFromFileAsync"),
                ]
            ),
        ];
    }

    /// <summary>値オブジェクト <paramref name="valueObject"/> の検証の本体（<c>IValueObject&lt;TSelf, TValue&gt;.ValidateCore</c> の明示的な実装）のメンバー名</summary>
    private static string Text_ValidateCore(string valueObject, string valueType) =>
        $"Sample.Domain.IValueObject<Sample.Domain.{valueObject},{valueType}>.ValidateCore";

    /// <summary>型 <paramref name="name"/>（型パラメータ <paramref name="arity"/> 個）の型パラメータ <paramref name="parameter"/> の制約が <paramref name="constraint"/> だけ</summary>
    private static CodeRule Text_TypeParameterConstraint(
        string name,
        int arity,
        string parameter,
        string constraint
    ) =>
        NoTypes(
            $"{name} の {parameter} の制約が {constraint} だけでない",
            type =>
                type.Name == name
                && type.Arity == arity
                && !type.TypeParameters.Any(item =>
                    item.Name == parameter
                    && item.ConstraintTypes.Length == 1
                    && item.ConstraintTypes[0].ToDisplayString(CodeRule.Format) == constraint
                )
        );

    /// <summary>
    /// 値オブジェクトの入力・検証・ロード：値オブジェクトの主キーの入力は解析して VO を作り、読めない入力はエラー・name の宣言幅 50 と balance の
    /// decimal(10,2) の検証は共有ルールの文言（<c>ValueObjectValidationMessages</c>）で落ちる・ロードは VO のインスタンスをそのまま代入する
    /// </summary>
    private static ExecCheck Text_ValueObjectEditModelBehavior() =>
        new(
            "value-object-editmodel-behavior",
            assembly =>
                ColumnText_WithCulture(
                    CultureInfo.InvariantCulture,
                    () =>
                        Text_WithStaticFunc(
                            assembly,
                            "ValueObjectValidationMessages",
                            "MaxLengthExceeded",
                            new Func<string, int, int, string>(
                                (display, max, actual) => $"max:{display}:{max}:{actual}"
                            ),
                            () =>
                            {
                                var problems = new List<string>();
                                var model = Text_New(assembly, "CustomerEditModel");
                                ColumnText_Input(model, "CustomerId", "abc");
                                Text_Expect(
                                    problems,
                                    ColumnText_Errors(model, "CustomerId").Count == 1,
                                    "読めない入力が値オブジェクトの主キーの変換エラーにならない"
                                );
                                ColumnText_Input(model, "CustomerId", "5");
                                var id = ProbeKit.Get(model, "CustomerId");
                                Text_Expect(
                                    problems,
                                    id?.GetType().Name == "CustomerIdValue"
                                        && Equals(ProbeKit.Get(id, "Value"), 5),
                                    "入力 5 が CustomerIdValue(5) にならない"
                                );

                                ColumnText_Input(model, "Name", new string('a', 51));
                                var nameErrors = ColumnText_Errors(model, "Name");
                                Text_Expect(
                                    problems,
                                    nameErrors.Any(error =>
                                        error.Contains("max:", StringComparison.Ordinal)
                                        && error.Contains(":50:51", StringComparison.Ordinal)
                                    ),
                                    $"51 文字の name が宣言幅 50 の検証で落ちない（[{string.Join(", ", nameErrors)}]）"
                                );
                                ColumnText_Input(model, "Balance", "123456789.5");
                                Text_Expect(
                                    problems,
                                    ColumnText_Errors(model, "Balance").Count == 1,
                                    "整数部 9 桁の balance が decimal(10,2) の検証で落ちない"
                                );
                                ColumnText_Input(model, "Balance", "1.125");
                                Text_Expect(
                                    problems,
                                    ColumnText_Errors(model, "Balance").Count == 1,
                                    "小数部 3 桁の balance が decimal(10,2) の検証で落ちない"
                                );
                                ColumnText_Input(model, "Balance", "12345678.12");
                                Text_Expect(
                                    problems,
                                    ColumnText_Errors(model, "Balance").Count == 0,
                                    "decimal(10,2) に収まる balance がエラーになった"
                                );

                                var entity = Text_New(assembly, "CustomerEntity");
                                var value = ProbeKit.CallStatic(
                                    ProbeKit.Type(assembly, "CustomerIdValue"),
                                    "Create",
                                    3
                                );
                                ProbeKit.Set(entity, "CustomerId", value);
                                Text_SetState(entity, "Unchanged");
                                var loaded = Text_Load(assembly, "Customer", entity);
                                Text_Expect(
                                    problems,
                                    ReferenceEquals(ProbeKit.Get(loaded, "CustomerId"), value),
                                    "ロードが実体の VO のインスタンスをそのまま代入しない"
                                );

                                return problems;
                            }
                        )
                )
        );

    /// <summary>
    /// EF Core の DI 拡張：<c>AddGeneratedEfCoreRepositories(構成)</c> が DbContext のファクトリ・EF Core 版の実行器・エンティティごとの
    /// EF Core 版 Repository を登録し、ファクトリが文脈を作るときに渡した構成を呼ぶ
    /// </summary>
    private static ExecCheck Text_EfCoreRegistrations() =>
        new(
            "efcore-registrations",
            assembly =>
            {
                var problems = new List<string>();
                var configured = 0;
                var services = ProbeKit.Services(
                    assembly,
                    "AddGeneratedEfCoreRepositories",
                    new Action<DbContextOptionsBuilder>(options =>
                    {
                        configured++;
                        options.UseSqlite("Data Source=:memory:").IgnoreServiceProviderCacheLimit();
                    })
                );
                problems.AddRange(
                    Text_Registered(
                        services,
                        "ISqlExecutor",
                        "EfCoreSqlExecutor<QuickErDbContext>",
                        ServiceLifetime.Singleton
                    )
                );
                problems.AddRange(
                    Text_Registered(
                        services,
                        "ICustomerRepository",
                        "EfCoreCustomerRepository",
                        ServiceLifetime.Scoped
                    )
                );
                problems.AddRange(
                    Text_Registered(
                        services,
                        "IOrderRepository",
                        "EfCoreOrderRepository",
                        ServiceLifetime.Scoped
                    )
                );

                using var provider = services.BuildServiceProvider();
                var contextType = ProbeKit.Type(assembly, "QuickErDbContext");
                var factory = provider.GetService(
                    typeof(IDbContextFactory<>).MakeGenericType(contextType)
                );

                if (factory is null)
                {
                    problems.Add("IDbContextFactory<QuickErDbContext> が登録されない");
                }
                else
                {
                    using var context = (DbContext)ProbeKit.Call(factory, "CreateDbContext")!;
                    Text_Expect(
                        problems,
                        configured > 0,
                        "DbContext のファクトリが渡した構成（configureDbContext）を呼ばない"
                    );
                }

                return problems;
            }
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> TextValueObjectAndEfCoreRows()
    {
        var efCore = ServiceRoot() with { GenerateEfCoreRepositories = true };

        return
        [
            TextRow(
                "code-value-objects-emitted-and-applied",
                "Generate_ValueObjects_Enabled_ShouldEmitAndApplyValueObjects",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateValueObjects = true,
                },
                code:
                [
                    Type(
                        "ValueObjectBaseCore<TSelf, TValue>",
                        kind: "class",
                        modifiers: "public abstract"
                    ),
                    Type(
                        "ValueObjectOrderedBase<TSelf, TValue>",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    Type(
                        "ValueObjectStringBase<TSelf>",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    .. new[] { "Contains", "StartsWith", "EndsWith" }.SelectMany(name =>
                        new[]
                        {
                            Member(
                                "ValueObjectStringBase<TSelf>",
                                name,
                                $"bool {name}(TSelf value)",
                                modifiers: "public"
                            ),
                            Member(
                                "ValueObjectStringBase<TSelf>",
                                name,
                                $"bool {name}(string value)",
                                modifiers: "public"
                            ),
                        }
                    ),
                    Type(
                        "ValueObjectBinaryBase<TSelf>",
                        kind: "class",
                        modifiers: "public abstract partial"
                    ),
                    Type("IValueObject<TSelf, TValue>", kind: "interface", modifiers: "public"),
                    Member("IValueObjectCore", "DisplayValue", "string DisplayValue { get; }"),
                    Member(
                        "ValueObjectBaseCore<TSelf, TValue>",
                        "DisplayValue",
                        "string DisplayValue { get; }",
                        modifiers: "public virtual"
                    ),
                    Type(
                        "ValueObjectJsonConverterFactory",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "JsonConverterFactory"
                    ),
                    Type(
                        "CustomerIdValue",
                        kind: "class",
                        modifiers: "public sealed partial",
                        baseType: "ValueObjectOrderedBase<CustomerIdValue, int>",
                        count: 1
                    ),
                    Type("NameValue", baseType: "ValueObjectStringBase<NameValue>"),
                    Type("PhotoValue", baseType: "ValueObjectBinaryBase<PhotoValue>"),
                    Type("IsActiveValue", baseType: "ValueObjectBooleanBase<IsActiveValue>"),
                    Type(
                        "ValueObjectBooleanBase<TSelf>",
                        kind: "class",
                        modifiers: "public abstract partial",
                        baseType: "ValueObjectBase<TSelf, bool>"
                    ),
                    CallArguments(
                        "NameValue",
                        Text_ValidateCore("NameValue", "System.String"),
                        "ValueObjectStringRules.ValidateMaxLength",
                        ["value", "50", "NameValue.DisplayName", "errors"]
                    ),
                    CallArguments(
                        "BalanceValue",
                        Text_ValidateCore("BalanceValue", "System.Decimal"),
                        "ValueObjectDecimalRules.Validate",
                        ["value", "10", "2", "BalanceValue.DisplayName", "errors"]
                    ),
                    CallArguments(
                        "NameValue",
                        Text_ValidateCore("NameValue", "System.String"),
                        "ValueObjectRules.ValidateRequired",
                        ["value", "NameValue.DisplayName", "errors"]
                    ),
                    Type(
                        "ValueObjectValidationMessages",
                        kind: "class",
                        modifiers: "public static"
                    ),
                    Member(
                        "ValueObjectValidationMessages",
                        "MaxLengthExceeded",
                        "Func<string, int, int, string> MaxLengthExceeded { get; set; }",
                        modifiers: "public static"
                    ),
                    Type("ValueObjectRules", kind: "class", modifiers: "public static"),
                    Type("ValueObjectStringRules", kind: "class", modifiers: "public static"),
                    Type("ValueObjectNumberRules", kind: "class", modifiers: "public static"),
                    Type("ValueObjectDecimalRules", kind: "class", modifiers: "public static"),
                    Type("GeneratedDisplayNames", kind: "class", modifiers: "public static"),
                    FeatureText_NoNameContaining("CustomizeMaxLengthErrorMessage"),
                    FeatureText_NoNameContaining("CustomizeScaleErrorMessage"),
                    FeatureText_NoNameContaining("CustomizePrecisionErrorMessage"),
                    FeatureText_NoNameContaining("CustomizeValueRequiredErrorMessage"),
                    Text_NoTypeDeclaring("ValidateDecimal"),
                    Type(
                        "SqlColumnTypeAttribute",
                        kind: "class",
                        modifiers: "public sealed",
                        baseType: "Attribute"
                    ),
                    Attribute(
                        "CustomerEntity",
                        "Name",
                        "SqlColumnType(SqlDbType.NVarChar, Size = 50)"
                    ),
                    Attribute(
                        "CustomerEntity",
                        "Balance",
                        "SqlColumnType(SqlDbType.Decimal, Precision = 10, Scale = 2)"
                    ),
                    Member(
                        "CustomerEntity",
                        "CustomerId",
                        "CustomerIdValue CustomerId { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "CustomerEditModel",
                        "CustomerId",
                        "CustomerIdValue? CustomerId { get; internal set; }",
                        modifiers: "public"
                    ),
                    Calls(
                        "CustomerEditModel",
                        "BindingCustomerId",
                        "ConvertParsedValueObjectInput<CustomerIdValue, int>",
                        count: 1
                    ),
                    ReferencesSymbol(
                        "CustomerMapper",
                        null,
                        "CustomerEditModel.BindingCustomerId",
                        present: false
                    ),
                ],
                exec: [Text_ValueObjectEditModelBehavior()]
            ),
            .. new[] { ("nvarchar(50)", "50"), ("nvarchar(20)", "20") }.Select(item =>
                TextRow(
                    $"code-guidkey-value-object-max-length-validation-{item.Item2}",
                    item.Item2 == "50"
                        ? "Generate_ValueObjects_GuidKey_ShouldEmitMaxLengthValidation"
                        : "Generate_ValueObjects_GuidKey_ShortColumn_ShouldWarnButGenerate",
                    () => Service_GuidKeyDiagram(item.Item1),
                    Service_GuidKeyOptions(),
                    code:
                    [
                        Type(
                            "DocumentIdValue",
                            baseType: "ValueObjectGuidKeyBase<DocumentIdValue>"
                        ),
                        CallArguments(
                            "DocumentIdValue",
                            Text_ValidateCore("DocumentIdValue", "System.String"),
                            "ValueObjectStringRules.ValidateMaxLength",
                            ["value", item.Item2, "DocumentIdValue.DisplayName", "errors"]
                        ),
                    ]
                )
            ),
            TextRow(
                "model-efcore-dbcontext-fluent-configuration",
                "Generate_EfCore_ShouldEmitDbContextWithFluentConfiguration",
                Service_ValueObjectDiagram,
                efCore,
                code:
                [
                    Using("QuickEREntities.g.cs", "Microsoft.EntityFrameworkCore"),
                    Type(
                        "QuickErDbContext",
                        kind: "class",
                        modifiers: "public partial",
                        baseType: "DbContext"
                    ),
                    Member(
                        "QuickErDbContext",
                        ".ctor",
                        "QuickErDbContext(DbContextOptions<QuickErDbContext> options)",
                        modifiers: "public"
                    ),
                    Member(
                        "QuickErDbContext",
                        "OnModelCreating",
                        "void OnModelCreating(ModelBuilder modelBuilder)",
                        modifiers: "protected override"
                    ),
                    Member(
                        "QuickErDbContext",
                        "OnModelCreatingPartial",
                        "void OnModelCreatingPartial(ModelBuilder modelBuilder)",
                        modifiers: "partial"
                    ),
                    Member(
                        "QuickErDbContext",
                        "Customers",
                        "DbSet<CustomerEntity> Customers { get; }",
                        modifiers: "public"
                    ),
                    CallArguments("QuickErDbContext", "Customers", "Set<CustomerEntity>", []),
                    Member(
                        "QuickErDbContext",
                        "Orders",
                        "DbSet<OrderEntity> Orders { get; }",
                        modifiers: "public"
                    ),
                    CallArguments("QuickErDbContext", "Orders", "Set<OrderEntity>", []),
                    Calls(
                        "QuickErDbContext",
                        "OnModelCreating",
                        "Entity<CustomerEntity>",
                        count: 1
                    ),
                ],
                model:
                [
                    EfCoreModelFact.Table("CustomerEntity", "customers"),
                    EfCoreModelFact.PrimaryKey("CustomerEntity", ["CustomerId"]),
                    EfCoreModelFact.Property(
                        "CustomerEntity",
                        "Name",
                        column: "name",
                        required: true,
                        maxLength: 50
                    ),
                    EfCoreModelFact.Property(
                        "CustomerEntity",
                        "Balance",
                        column: "balance",
                        precision: 10,
                        scale: 2
                    ),
                ]
            ),
            TextRow(
                "model-efcore-ignores-entity-base-members",
                "Generate_EfCore_ShouldIgnoreEntityBaseMembers",
                Service_ValueObjectDiagram,
                efCore,
                code:
                [
                    CallLambdaMembers(
                        "QuickErDbContext",
                        "OnModelCreating",
                        "Ignore",
                        [
                            "EntityBaseCore.RowState",
                            "EntityBaseCore.IsAdded",
                            "EntityBaseCore.IsUpdated",
                            "EntityBaseCore.IsRemoved",
                            "EntityBaseCore.HasChanges",
                        ],
                        within: "Entity<CustomerEntity>"
                    ),
                ],
                model: [EfCoreModelFact.NoProperty("CustomerEntity", "RowState")]
            ),
            TextRow(
                "code-efcore-only-sql-query-delegates-to-executor",
                "Generate_EfCoreOnly_SqlQuery_ShouldOnlyDelegateToExecutor",
                SingleEntityDiagram,
                efCore with
                {
                    GenerateRepositories = false,
                },
                code:
                [
                    Type("SqlQuery<TEntity>", kind: "class", modifiers: "public sealed"),
                    CallArguments(
                        "SqlQuery<TEntity>",
                        "ToListAsync",
                        "ToListAsync",
                        ["?", "cancellationToken"],
                        receiver: "_executor"
                    ),
                    Calls("SqlQuery<TEntity>", "ToListAsync", "BuildPlan", count: 1),
                    CallArguments("SqlQuery<TEntity>", "ToListAsync", "ConfigureAwait", ["false"]),
                    Text_NoTypeDeclaring("_connectionFactory"),
                    NoType("SqlExpressionTranslator"),
                    NoType("JsonQueryPlanner"),
                    Text_NoTypeDeclaring("ReadJsonAsync"),
                    Text_NoTypeDeclaring("BuildJsonSelect"),
                    Type("IItemRepository", interfaces: ["IRepository<ItemEntity, int>"]),
                ]
            ),
            TextRow(
                "code-efcore-repositories-core-shim-and-per-entity",
                "Generate_EfCore_ShouldEmitEfCoreRepositories",
                Service_ValueObjectDiagram,
                efCore,
                code:
                [
                    Type(
                        "EfCoreRepositoryCore<TEntity, TKey, TContext>",
                        kind: "class",
                        modifiers: "public abstract",
                        interfaces: ["IRepositoryCore<TEntity, TKey>"]
                    ),
                    Member(
                        "EfCoreRepositoryCore<TEntity, TKey, TContext>",
                        ".ctor",
                        "EfCoreRepositoryCore(IDbContextFactory<TContext> contextFactory, ISaveHookRegistry? saveHooks = null, ISqlExecutor? sqlExecutor = null)",
                        modifiers: "public abstract"
                    ),
                    Text_TypeParameterConstraint(
                        "EfCoreRepositoryCore",
                        3,
                        "TContext",
                        "DbContext"
                    ),
                    Type(
                        "EfCoreRepository<TEntity, TKey, TContext>",
                        kind: "class",
                        modifiers: "public abstract partial",
                        baseType: "EfCoreRepositoryCore<TEntity, TKey, TContext>"
                    ),
                    Type(
                        "EfCoreCustomerRepository",
                        kind: "class",
                        modifiers: "public sealed partial",
                        baseType: "EfCoreRepository<CustomerEntity, int, QuickErDbContext>",
                        interfaces: ["ICustomerRepository"]
                    ),
                    Calls(
                        "EfCoreRepositoryCore<TEntity, TKey, TContext>",
                        "GetByIdAsync",
                        "AsNoTracking"
                    ),
                    Calls(
                        "EfCoreRepositoryCore<TEntity, TKey, TContext>",
                        "GetByIdAsync",
                        "MarkUnchanged"
                    ),
                ]
            ),
            TextRow(
                "code-efcore-save-tracks-cascade-closure",
                "Generate_EfCore_ShouldConvertRowStateOfCascadeClosureInSave",
                Service_ValueObjectDiagram,
                efCore,
                code:
                [
                    Calls(
                        "EfCoreRepositoryCore<TEntity, TKey, TContext>",
                        "TrackNode",
                        "EnumerateCascadeChildren"
                    ),
                    NoCalls("ChangeTracker.TrackGraph"),
                    CallArguments(
                        "EfCoreRepositoryCore<TEntity, TKey, TContext>",
                        "SaveTrackedGraphAsync",
                        "EntityGraphSaver.AcceptChanges",
                        ["root", "cascadeSave"]
                    ),
                ]
            ),
            TextRow(
                "code-efcore-sql-executor-shares-raw-sql-mapper",
                "Generate_EfCore_ShouldEmitEfCoreSqlExecutor",
                Service_ValueObjectDiagram,
                efCore,
                code:
                [
                    Type(
                        "EfCoreSqlExecutor<TContext>",
                        kind: "class",
                        modifiers: "public sealed partial",
                        interfaces: ["ISqlExecutor"]
                    ),
                    Member(
                        "EfCoreSqlExecutor<TContext>",
                        ".ctor",
                        "EfCoreSqlExecutor(IDbContextFactory<TContext> contextFactory)",
                        modifiers: "public sealed partial"
                    ),
                    FeatureText_SingleTypeParameterConstraint("EfCoreSqlExecutor", "DbContext"),
                    CallArguments(
                        "EfCoreSqlExecutor<TContext>",
                        "CreateCommandAsync",
                        "EfCoreCommands.Create",
                        ["Database", "sql", "parameters"]
                    ),
                    Calls("EfCoreCommands", "Create", "GetDbConnection", count: 1),
                    Calls("EfCoreCommands", "Create", "CreateCommand", count: 1),
                    CallArguments(
                        "EfCoreSqlExecutor<TContext>",
                        "QueryProjectionBySqlAsync",
                        "RawSqlMapper.ReadProjectionRowsAsync<TResult>",
                        ["reader", "cancellationToken"]
                    ),
                    CallArguments(
                        "EfCoreCommands",
                        "Create",
                        "RawSqlMapper.BindParameters",
                        ["command", "parameters"]
                    ),
                    Member(
                        "RawSqlMapper",
                        "BindParameters",
                        "void BindParameters(DbCommand command, object? parameters)",
                        modifiers: "internal static"
                    ),
                    Member(
                        "EntitySaveMetadata",
                        "MapEntityFromRawSql",
                        "TEntity MapEntityFromRawSql<TEntity>(DbDataReader reader)",
                        modifiers: "public"
                    ),
                ]
            ),
            TextRow(
                "code-efcore-di-extension-registrations",
                "Generate_EfCore_ShouldEmitDiExtension",
                Service_ValueObjectDiagram,
                efCore,
                code:
                [
                    Member(
                        "GeneratedEfCoreRepositoryServiceCollectionExtensions",
                        "AddGeneratedEfCoreRepositories",
                        "IServiceCollection AddGeneratedEfCoreRepositories(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDbContext)",
                        modifiers: "public static"
                    ),
                ],
                exec: [Text_EfCoreRegistrations()]
            ),
            TextRow(
                "code-efcore-sql-query-executor-backend",
                "Generate_EfCore_ShouldAddExecutorBackendToSqlQuery",
                Service_ValueObjectDiagram,
                efCore,
                code:
                [
                    Type("ISqlQueryExecutor<TEntity>", kind: "interface", modifiers: "internal"),
                    Type("SqlQueryPlan<TEntity>", kind: "record", modifiers: "internal sealed"),
                    Member(
                        "SqlQuery<TEntity>",
                        ".ctor",
                        "SqlQuery(ISqlQueryExecutor<TEntity> executor)",
                        modifiers: "internal"
                    ),
                    Type("SqlQuery<TEntity>", kind: "class", modifiers: "public sealed"),
                    CallArguments(
                        "SqlQuery<TEntity>",
                        "Where",
                        "Add",
                        ["predicate"],
                        receiver: "_predicates"
                    ),
                    Type(
                        "EfCoreSqlQueryExecutor<TEntity, TContext>",
                        kind: "class",
                        modifiers: "internal sealed"
                    ),
                ]
            ),
            TextRow(
                "code-efcore-disabled-sql-query-unified-through-ado-executor",
                "Generate_EfCore_Disabled_ShouldStillUnifyThroughAdoExecutor",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                },
                code:
                [
                    Type("ISqlQueryExecutor<TEntity>", kind: "interface", modifiers: "internal"),
                    Type("SqlQueryPlan<TEntity>", kind: "record", modifiers: "internal sealed"),
                    Type(
                        "SqlServerSqlQueryExecutor<TEntity>",
                        kind: "class",
                        modifiers: "internal sealed"
                    ),
                    Member(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "Query",
                        "SqlQuery<TEntity> Query()",
                        modifiers: "public"
                    ),
                    FeatureText_NoNameContaining("EfCore"),
                    NoType("QuickErDbContext"),
                    NoUsingUnder(AllFiles, "Microsoft.EntityFrameworkCore"),
                    Member(
                        "EntitySaveMetadata",
                        "MapEntityFromRawSql",
                        "TEntity MapEntityFromRawSql<TEntity>(SqlDataReader reader)",
                        modifiers: "public"
                    ),
                ],
                compileWithout: ReducedReferences.WithoutEfCore
            ),
        ];
    }

    /// <summary>CSharpCodeGenerationServiceTests.CompositeKeyDiagram の図（複合主キー a・b。主キーの順序の上書きを切り替える）</summary>
    private static ErDiagram Text_CompositeKeyDiagram(bool reversePrimaryKeyOrder)
    {
        var a = Service_Column("a", "int", primaryKey: true);
        var b = Service_Column("b", "int", primaryKey: true);
        var entity = Service_Entity("pairs", a, b);
        entity.PrimaryKeyColumnIds = reversePrimaryKeyOrder ? [b.Id, a.Id] : [];

        return new ErDiagram { Entities = [entity] };
    }
}
