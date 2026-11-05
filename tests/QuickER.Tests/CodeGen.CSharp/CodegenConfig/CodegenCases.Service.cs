using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：<c>CSharpCodeGenerationServiceTests</c> の診断・ファイル構成・成否で判定していたテスト
/// </summary>
internal static partial class CodegenCases
{
    private const string ServiceClass = "CSharpCodeGenerationServiceTests.";

    /// <summary>生成が成り立ち、.g.cs が 1 つ以上ある（成功の期待の足場。エラーなら段の食い違いで赤になる）</summary>
    private static IReadOnlyList<FileRule> ServiceGenerated =>
        [new FileRule(".g.cs", NonEmpty: true)];

    private static ScenarioRow<CodegenCase> ServiceRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        DiagnosticExpectation diagnostics,
        IReadOnlyList<FileRule>? fileRules = null
    ) =>
        new(
            id,
            null,
            ServiceClass + description,
            new CodegenCase(
                diagram,
                options,
                DialectNames.SqlServer,
                diagnostics,
                FileRules: fileRules
            )
        );

    private static CodeGenerationOptions ServiceRoot() => new() { RootNamespace = "Sample.Domain" };

    private static IEnumerable<ScenarioRow<CodegenCase>> ServiceRows() =>
        [
            // ===== ファイル構成 =====
            ServiceRow(
                "files-default-single-file-name",
                "Generate_ShouldCreateSingleGeneratedFileWithEntityAndEditModel",
                Service_CustomersDiagram,
                ServiceRoot(),
                DiagnosticExpectation.Contains(),
                [new FileRule("", Count: 1, Names: ["QuickEREntities.g.cs"])]
            ),
            ServiceRow(
                "files-split-one-file-per-category-plus-runtime",
                "Generate_Split_ShouldEmitOneFilePerCategoryPlusRuntime",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    GenerateValueObjects = true,
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(
                        "",
                        Count: 8,
                        Names:
                        [
                            "Runtime.g.cs",
                            "Runtime.SqlServer.g.cs",
                            "ValueObjects.g.cs",
                            "Entities.g.cs",
                            "EditModels.g.cs",
                            "Mappers.g.cs",
                            "Repositories.g.cs",
                            "Repositories.SqlServer.g.cs",
                        ]
                    ),
                ]
            ),
            ServiceRow(
                "files-split-same-namespace-separate-files",
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
                DiagnosticExpectation.Contains(),
                [
                    new FileRule("Entities.g.cs", NonEmpty: true),
                    new FileRule("EditModels.g.cs", NonEmpty: true),
                ]
            ),
            ServiceRow(
                "files-efcore-split-dedicated-file",
                "Generate_EfCore_Split_ShouldEmitDedicatedFileAndNamespace",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    GenerateEfCoreRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule("Repositories.EntityFrameworkCore.g.cs", NonEmpty: true)]
            ),
            ServiceRow(
                "files-efcore-only-succeeds",
                "Generate_EfCoreOnly_ShouldSucceedWithoutError",
                Service_ValueObjectDiagram,
                ServiceRoot() with
                {
                    GenerateEfCoreRepositories = true,
                    GenerateRepositories = false,
                },
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            // ===== 成功（対照） =====
            ServiceRow(
                "diag-single-rowversion-column-succeeds",
                "Generate_SingleRowVersionColumn_ShouldSucceed",
                () => Service_RowVersionDiagram("rowversion"),
                ServiceRoot(),
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-non-primary-key-rowversion-column-succeeds",
                "Generate_NonPrimaryKeyRowVersionColumn_ShouldSucceed",
                () => Service_RowVersionDiagram("timestamp"),
                ServiceRoot(),
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-non-primary-key-unbounded-binary-with-exclusion-succeeds",
                "Generate_NonPrimaryKeyUnboundedBinaryColumn_WithExclusion_ShouldSucceed",
                () => Service_UnboundedBinaryDiagram(binaryColumnIsPrimaryKey: false),
                ServiceRoot() with
                {
                    ExcludeUnboundedBinaryColumns = true,
                },
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-primary-key-unbounded-binary-without-exclusion-succeeds",
                "Generate_PrimaryKeyUnboundedBinaryColumn_WithoutExclusion_ShouldSucceed",
                () => Service_UnboundedBinaryDiagram(binaryColumnIsPrimaryKey: true),
                ServiceRoot(),
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-distinct-entity-class-names-succeeds",
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
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-distinct-column-property-names-succeeds",
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
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-same-property-name-different-entities-succeeds",
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
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-distinct-generated-member-names-succeeds",
                "Generate_DistinctGeneratedMemberNames_ShouldSucceed",
                () =>
                    Service_CustomerOrderDiagram(
                        parentKey: "user_id",
                        parentExtra: Service_Column("user_name", "nvarchar(50)"),
                        childForeignKey: "user_id"
                    ),
                ServiceRoot(),
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-column-matching-other-entity-navigation-succeeds",
                "Generate_ColumnNameMatchingOtherEntityNavigation_ShouldSucceed",
                () =>
                    Service_CustomerOrderDiagram(
                        parentKey: "customer_id",
                        parentExtra: Service_Column("customer", "nvarchar(50)"),
                        childForeignKey: "customer_id"
                    ),
                ServiceRoot(),
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-default-display-name-without-description-succeeds",
                "Generate_ColumnNamedDefaultDisplayNameWithoutDescription_ShouldSucceed",
                () => Service_ColumnsDiagram("items", null, "default_display_name"),
                ServiceRoot(),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision)
                        ),
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_GeneratedMemberNamesDifferOnlyInCase)
                        ),
                        ExpectedDiagnostic.WarningAnyItems(
                            nameof(Strings.CodeGen_Warning_EntityDisplayNameCollision)
                        ),
                        ExpectedDiagnostic.WarningAnyItems(
                            nameof(Strings.CodeGen_Warning_EditModelDisplayNameCollision)
                        )
                    ),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-base-class-only-member-names-succeed",
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
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-valid-multi-segment-root-namespace-succeeds",
                "Generate_ValidMultiSegmentRootNamespace_ShouldSucceed",
                SingleEntityDiagram,
                new CodeGenerationOptions { RootNamespace = "My.App.Data" },
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            // ===== 多対多・複合外部キーの警告 =====
            ServiceRow(
                "diag-many-to-many-skipped-once",
                "Generate_ShouldWarnAndSkipManyToManyRelationship / Generate_ManyToManyWarning_ShouldNotBeDuplicated",
                Service_ManyToManyDiagram,
                ServiceRoot(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.WarningAnyItems(
                        nameof(Strings.CodeGen_Warning_ManyToManySkipped)
                    ),
                    1
                ),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-composite-relationship-warning-named-constraint",
                "Generate_CompositeRelationshipWarning_UsesReadableName(\"FK_child_parent\")",
                () => Service_CompositeRelationshipDiagram("FK_child_parent"),
                ServiceRoot(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_RelationshipCompositeSkipped),
                        "parents → children"
                    ),
                    1
                ),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-composite-relationship-warning-unnamed-constraint",
                "Generate_CompositeRelationshipWarning_UsesReadableName(null)",
                () => Service_CompositeRelationshipDiagram(null),
                ServiceRoot(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_RelationshipCompositeSkipped),
                        "parents → children"
                    ),
                    1
                ),
                ServiceGenerated
            ),
            // ===== オプションの組み合わせの検証 =====
            ServiceRow(
                "diag-repository-requires-data-annotations",
                "Generate_RepositoryWithoutDataAnnotations_ShouldFailWithError",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                    IncludeDataAnnotations = false,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_RepositoryRequiresDataAnnotations)
                    )
                )
            ),
            ServiceRow(
                "diag-remote-requires-contract-both",
                "Generate_RemoteWithoutRepositoryContract_ShouldFailWithError(remoteContracts: true, remoteServices: true)",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRemoteContracts = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(nameof(Strings.CodeGen_Error_RemoteRequiresContract))
                )
            ),
            ServiceRow(
                "diag-remote-with-contract-quicker-repositories",
                "Generate_RemoteWithRepositoryContract_ShouldSucceed(GenerateRepositories)",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                    GenerateEfCoreRepositories = false,
                    GenerateInMemoryRepositories = false,
                    GenerateRemoteContracts = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_RemoteRequiresContract)
                        )
                    ),
                [new FileRule(".RemoteServer.g.cs", NonEmpty: true)]
            ),
            ServiceRow(
                "diag-remote-with-contract-efcore",
                "Generate_RemoteWithRepositoryContract_ShouldSucceed(GenerateEfCoreRepositories)",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                    GenerateInMemoryRepositories = false,
                    GenerateRemoteContracts = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_RemoteRequiresContract)
                        )
                    ),
                [new FileRule(".RemoteServer.g.cs", NonEmpty: true)]
            ),
            ServiceRow(
                "diag-remote-with-contract-inmemory",
                "Generate_RemoteWithRepositoryContract_ShouldSucceed(GenerateInMemoryRepositories)",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = false,
                    GenerateInMemoryRepositories = true,
                    GenerateRemoteContracts = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_RemoteRequiresContract)
                        )
                    ),
                [new FileRule(".RemoteServer.g.cs", NonEmpty: true)]
            ),
            ServiceRow(
                "diag-mapper-without-editmodel-fails",
                "Generate_MapperWithoutEditModel_ShouldFailWithError",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    GenerateEditModels = false,
                    GenerateMappers = true,
                    GenerateRepositories = false,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_MapperRequiresEntityAndEditModel)
                    )
                )
            ),
            // ===== rowversion・無制限バイナリ列 =====
            ServiceRow(
                "diag-multiple-rowversion-columns",
                "Generate_MultipleRowVersionColumns_ShouldFailWithError",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "items",
                                Service_Column("item_id", "int", primaryKey: true),
                                Service_Column("row_ver", "rowversion"),
                                Service_Column("row_ver2", "timestamp")
                            ),
                        ],
                    },
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_MultipleRowVersionColumns),
                        "items",
                        "'row_ver', 'row_ver2'"
                    )
                )
            ),
            ServiceRow(
                "diag-primary-key-rowversion-column",
                "Generate_PrimaryKeyRowVersionColumn_ShouldFailWithError",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "items",
                                Service_Column("row_ver", "rowversion", primaryKey: true),
                                Service_Column("name", "nvarchar(50)")
                            ),
                        ],
                    },
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_PrimaryKeyRowVersionColumn),
                        "items",
                        "row_ver"
                    )
                )
            ),
            ServiceRow(
                "diag-primary-key-unbounded-binary-with-exclusion",
                "Generate_PrimaryKeyUnboundedBinaryColumn_WithExclusion_ShouldFailWithError",
                () => Service_UnboundedBinaryDiagram(binaryColumnIsPrimaryKey: true),
                ServiceRoot() with
                {
                    ExcludeUnboundedBinaryColumns = true,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_PrimaryKeyUnboundedBinaryColumn),
                        "assets",
                        "payload"
                    )
                )
            ),
            ServiceRow(
                "diag-exclude-unbounded-binary-on-reports-info",
                "Generate_ExcludeUnboundedBinary_On_MarksColumnAndReportsInfo",
                Service_BinaryColumnDiagram,
                ServiceRoot() with
                {
                    ExcludeUnboundedBinaryColumns = true,
                },
                DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.Info(
                        nameof(Strings.CodeGen_Info_ExcludedUnboundedBinaryColumns)
                    ),
                ]),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-exclude-unbounded-binary-off-reports-nothing",
                "Generate_ExcludeUnboundedBinary_Off_DoesNotMarkOrReport",
                Service_BinaryColumnDiagram,
                ServiceRoot() with
                {
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Exact([]),
                ServiceGenerated
            ),
            // ===== 名前の衝突 =====
            ServiceRow(
                "diag-type-name-collision-singular-plural",
                "Generate_CollidingEntityClassNames_ShouldFailWithError",
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
                                "customers",
                                Service_Column("customer_id", "int", primaryKey: true)
                            ),
                        ],
                    },
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_GeneratedTypeNameCollision),
                        "CustomerEntity",
                        "'customer'",
                        "'customers'"
                    )
                )
            ),
            ServiceRow(
                "diag-column-property-name-collision",
                "Generate_CollidingColumnPropertyNames_ShouldFailWithError",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "users",
                                Service_Column("user-id", "int", primaryKey: true),
                                Service_Column("user_id", "int")
                            ),
                        ],
                    },
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_ColumnPropertyNameCollision),
                        "users",
                        "UserId",
                        "'user-id', 'user_id'"
                    )
                )
            ),
            ServiceRow(
                "diag-editmodel-binding-member-collision",
                "Generate_CollidingEditModelBindingMemberNames_ShouldFailWithError",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "items",
                                Service_Column("id", "int", primaryKey: true),
                                Service_Column("binding_id", "int")
                            ),
                        ],
                    },
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "ItemEditModel",
                        "BindingId",
                        "'id'",
                        "'binding_id'"
                    )
                )
            ),
            ServiceRow(
                "diag-column-navigation-name-collision",
                "Generate_ColumnCollidingWithNavigationName_ShouldFailWithError",
                () =>
                    Service_CustomerOrderDiagram(
                        parentKey: "customer_id",
                        parentExtra: null,
                        childForeignKey: "customer_id",
                        childExtra: Service_Column("customer", "nvarchar(50)")
                    ),
                ServiceRoot() with
                {
                    GenerateEditModels = false,
                    GenerateMappers = false,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "OrderEntity",
                        "'Customer'",
                        "'customer'"
                    )
                )
            ),
            ServiceRow(
                "diag-member-named-as-enclosing-class-entity",
                "Generate_MemberNamedAsEnclosingClass_ShouldFailWithError(order_entity, OrderEntity)",
                () => Service_OrdersWithColumnDiagram("order_entity"),
                ServiceRoot(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "OrderEntity"
                    ),
                    1
                )
            ),
            ServiceRow(
                "diag-member-named-as-enclosing-class-editmodel",
                "Generate_MemberNamedAsEnclosingClass_ShouldFailWithError(order_edit_model, OrderEditModel)",
                () => Service_OrdersWithColumnDiagram("order_edit_model"),
                ServiceRoot(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "OrderEditModel"
                    ),
                    1
                )
            ),
            ServiceRow(
                "diag-fixed-member-editmodel-column-table",
                "Generate_ColumnCollidingWithEditModelColumnTable_ShouldFailWithError",
                () => Service_ColumnsDiagram("items", null, "edit_model_columns"),
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "ItemEditModel",
                        "items",
                        "_editModelColumns",
                        "'edit_model_columns'"
                    )
                )
            ),
            ServiceRow(
                "diag-fixed-member-editmodel-parent-collection",
                "Generate_ColumnCollidingWithEditModelParentCollection_ShouldFailWithError",
                () => Service_ColumnsDiagram("items", null, "parent_collection"),
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "ItemEditModel",
                        "items",
                        "ParentCollection",
                        "'parent_collection'"
                    )
                )
            ),
            ServiceRow(
                "diag-fixed-member-value-change-hook",
                "Generate_ColumnCollidingWithValueChangeHook_ShouldFailWithError",
                () => Service_ColumnsDiagram("items", null, "a", "on_a_changing"),
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "ItemEditModel",
                        "OnAChanging",
                        "'a'",
                        "'on_a_changing'"
                    )
                )
            ),
            ServiceRow(
                "diag-fixed-member-entity-default-display-name",
                "Generate_ColumnCollidingWithEntityDefaultDisplayName_ShouldFailWithError",
                () => Service_ColumnsDiagram("items", "Item master", "default_display_name"),
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "ItemEntity",
                        "items",
                        "DefaultDisplayName",
                        "'default_display_name'"
                    )
                )
            ),
            ServiceRow(
                "diag-row-state-column-fails",
                "Generate_RowStateColumn_ShouldError",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "audit_rows",
                                Service_Column("audit_id", "int", primaryKey: true),
                                Service_Column("row_state", "nvarchar(20)", nullable: true)
                            ),
                        ],
                    },
                ServiceRoot(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                        "RowState"
                    )
                )
            ),
            // ===== 名前空間 =====
            ServiceRow(
                "diag-invalid-root-namespace",
                "Generate_InvalidRootNamespace_ShouldFailWithError",
                SingleEntityDiagram,
                new CodeGenerationOptions { RootNamespace = "Bad-Namespace" },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RootNamespace",
                        "Bad-Namespace"
                    )
                )
            ),
            ServiceRow(
                "diag-root-namespace-empty-segment-leading-dot",
                "Generate_RootNamespaceWithEmptySegment_ShouldFailWithError(\".Sample\")",
                SingleEntityDiagram,
                new CodeGenerationOptions { RootNamespace = ".Sample" },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RootNamespace",
                        ".Sample"
                    )
                )
            ),
            ServiceRow(
                "diag-root-namespace-empty-segment-trailing-dot",
                "Generate_RootNamespaceWithEmptySegment_ShouldFailWithError(\"Sample.\")",
                SingleEntityDiagram,
                new CodeGenerationOptions { RootNamespace = "Sample." },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RootNamespace",
                        "Sample."
                    )
                )
            ),
            ServiceRow(
                "diag-root-namespace-empty-segment-double-dot",
                "Generate_RootNamespaceWithEmptySegment_ShouldFailWithError(\"Sample..Domain\")",
                SingleEntityDiagram,
                new CodeGenerationOptions { RootNamespace = "Sample..Domain" },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RootNamespace",
                        "Sample..Domain"
                    )
                )
            ),
            ServiceRow(
                "diag-root-namespace-empty-segment-blank",
                "Generate_RootNamespaceWithEmptySegment_ShouldFailWithError(\"Sample. .Domain\")",
                SingleEntityDiagram,
                new CodeGenerationOptions { RootNamespace = "Sample. .Domain" },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RootNamespace",
                        "Sample. .Domain"
                    )
                )
            ),
            ServiceRow(
                "diag-root-namespace-reserved-keyword",
                "Generate_RootNamespaceWithReservedKeyword_ShouldFailWithError",
                SingleEntityDiagram,
                new CodeGenerationOptions { RootNamespace = "Sample.class.Domain" },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RootNamespace",
                        "Sample.class.Domain"
                    )
                )
            ),
            ServiceRow(
                "diag-invalid-category-namespace",
                "Generate_InvalidCategoryNamespace_ShouldFailWithError",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    EntityNamespace = "Sample.1Domain",
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "EntityNamespace",
                        "Sample.1Domain"
                    )
                )
            ),
            ServiceRow(
                "diag-invalid-repository-namespace-multi-dialect-without-split",
                "Generate_InvalidRepositoryNamespace_WithoutSplit_ShouldFailWithError",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = false,
                    GenerateRepositories = true,
                    IncludeDataAnnotations = true,
                    RepositoryDialects = ["sqlserver", "sqlite"],
                    RepositoryNamespace = "123 bad",
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RepositoryNamespace",
                        "123 bad"
                    )
                )
            ),
            ServiceRow(
                "diag-invalid-repository-namespace-single-dialect-without-split-unused",
                "Generate_InvalidRepositoryNamespace_SingleDialectWithoutSplit_ShouldSucceed",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = false,
                    GenerateRepositories = true,
                    IncludeDataAnnotations = true,
                    RepositoryNamespace = "123 bad",
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.Error(
                            nameof(Strings.CodeGen_Error_InvalidNamespace),
                            "RepositoryNamespace"
                        )
                    ),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-invalid-repository-namespace-without-repository-bucket-unused",
                "Generate_InvalidRepositoryNamespace_WithoutRepositoryBucket_ShouldSucceed",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = false,
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = false,
                    RepositoryNamespace = "123 bad",
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.Error(
                            nameof(Strings.CodeGen_Error_InvalidNamespace),
                            "RepositoryNamespace"
                        )
                    ),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-invalid-unused-namespace-package-runtime",
                "Generate_InvalidButUnusedNamespaceOption_ShouldSucceed(package-runtime)",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    UseRuntimePackages = true,
                    RuntimeNamespace = "123 bad",
                },
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-invalid-unused-namespace-layered-root",
                "Generate_InvalidButUnusedNamespaceOption_ShouldSucceed(layered-root)",
                SingleEntityDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "123 bad",
                    LayeredOutput = true,
                    GenerateRepositories = true,
                    RepositoryDialects = ["sqlserver", "sqlite"],
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(".g.cs", NonEmpty: true, AnyDirectory: true)]
            ),
            ServiceRow(
                "diag-invalid-unused-namespace-split-all-explicit-root",
                "Generate_InvalidButUnusedNamespaceOption_ShouldSucceed(split-all-explicit-root)",
                SingleEntityDiagram,
                new CodeGenerationOptions
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
                },
                DiagnosticExpectation.Contains(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-invalid-used-namespace-inline-runtime",
                "Generate_InvalidAndUsedNamespaceOption_ShouldFailWithError(inline-runtime)",
                SingleEntityDiagram,
                ServiceRoot() with
                {
                    SplitFilesByCategory = true,
                    RuntimeNamespace = "123 bad",
                },
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RuntimeNamespace",
                        "123 bad"
                    ),
                    1
                )
            ),
            ServiceRow(
                "diag-invalid-used-namespace-split-root-fallback",
                "Generate_InvalidAndUsedNamespaceOption_ShouldFailWithError(split-root-fallback)",
                SingleEntityDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "123 bad",
                    SplitFilesByCategory = true,
                    EntityNamespace = "Sample.Entities",
                },
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RootNamespace",
                        "123 bad"
                    ),
                    1
                )
            ),
            ServiceRow(
                "diag-invalid-used-namespace-split-remote-server-root",
                "Generate_InvalidAndUsedNamespaceOption_ShouldFailWithError(split-remote-server-root)",
                SingleEntityDiagram,
                new CodeGenerationOptions
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
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_InvalidNamespace),
                        "RootNamespace",
                        "123 bad"
                    ),
                    1
                )
            ),
            // ===== 値オブジェクト =====
            ServiceRow(
                "diag-guidkey-wide-column-no-warning",
                "Generate_ValueObjects_GuidKey_ShouldEmitMaxLengthValidation",
                () => Service_GuidKeyDiagram("nvarchar(50)"),
                Service_GuidKeyOptions(),
                Service_NoGuidKeyWarning(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-guidkey-short-column-warns-once",
                "Generate_ValueObjects_GuidKey_ShortColumn_ShouldWarnButGenerate",
                () => Service_GuidKeyDiagram("nvarchar(20)"),
                Service_GuidKeyOptions(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_GuidKeyColumnShorterThanGuid),
                        "documents.document_id",
                        "20",
                        "36"
                    ),
                    1
                ),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-guidkey-width-36-no-warning",
                "Generate_ValueObjects_GuidKey_WideEnoughColumn_ShouldNotWarn(nvarchar(36))",
                () => Service_GuidKeyDiagram("nvarchar(36)"),
                Service_GuidKeyOptions(),
                Service_NoGuidKeyWarning(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-guidkey-width-max-no-warning",
                "Generate_ValueObjects_GuidKey_WideEnoughColumn_ShouldNotWarn(nvarchar(max))",
                () => Service_GuidKeyDiagram("nvarchar(max)"),
                Service_GuidKeyOptions(),
                Service_NoGuidKeyWarning(),
                ServiceGenerated
            ),
            ServiceRow(
                "diag-valueobject-conflicting-same-name-columns-warns",
                "Generate_ValueObjects_ConflictingSameNameColumns_ShouldWarnButGenerate",
                () =>
                    new ErDiagram
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
                    },
                ServiceRoot() with
                {
                    GenerateValueObjects = true,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_ValueObjectDefinitionMismatch),
                        "NameValue"
                    )
                ),
                [new FileRule("", Count: 1)]
            ),
            ServiceRow(
                "diag-valueobject-same-name-different-clr-types",
                "Generate_ValueObjects_SameNameColumnsWithDifferentClrTypes_ShouldError",
                () =>
                    new ErDiagram
                    {
                        Entities =
                        [
                            Service_Entity(
                                "documents",
                                Service_Column("document_id", "int", primaryKey: true),
                                Service_Column("payload", "varbinary(max)")
                            ),
                            Service_Entity(
                                "archives",
                                Service_Column("archive_id", "int", primaryKey: true),
                                Service_Column("payload", "nvarchar(20)")
                            ),
                        ],
                    },
                ServiceRoot() with
                {
                    GenerateValueObjects = true,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_ValueObjectValueTypeConflict),
                        "PayloadValue",
                        "documents.payload",
                        "archives.payload"
                    )
                )
            ),
        ];

    // ---- 元のテストの図（そのまま写す） ----

    private static DiagnosticExpectation Service_NoGuidKeyWarning() =>
        DiagnosticExpectation
            .Contains()
            .Without(
                ExpectedDiagnostic.Warning(
                    nameof(Strings.CodeGen_Warning_GuidKeyColumnShorterThanGuid),
                    "documents.document_id"
                )
            );

    private static CodeGenerationOptions Service_GuidKeyOptions() =>
        ServiceRoot() with
        {
            GenerateValueObjects = true,
            UseGuidKeyForStringPrimaryKey = true,
        };

    private static Column Service_Column(
        string name,
        string dataType,
        bool primaryKey = false,
        bool? nullable = false,
        bool foreignKey = false
    )
    {
        var column = new Column
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = dataType,
            IsPrimaryKey = primaryKey,
            IsForeignKey = foreignKey,
        };

        if (nullable is { } isNullable)
        {
            column.IsNullable = isNullable;
        }

        return column;
    }

    private static Entity Service_Entity(string tableName, params Column[] columns) =>
        new()
        {
            Id = Guid.NewGuid(),
            TableName = tableName,
            Columns = [.. columns],
        };

    /// <summary>CSharpCodeGenerationServiceTests.GenerateGuidKeyDiagram の図</summary>
    private static ErDiagram Service_GuidKeyDiagram(string primaryKeyDataType) =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "documents",
                    Service_Column("document_id", primaryKeyDataType, primaryKey: true),
                    Service_Column("title", "nvarchar(100)")
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.Generate_ShouldWarnAndSkipManyToManyRelationship の図</summary>
    private static ErDiagram Service_ManyToManyDiagram()
    {
        var users = Service_Entity("users", Service_Column("user_id", "int", primaryKey: true));
        var roles = Service_Entity("roles", Service_Column("role_id", "int", primaryKey: true));

        return new ErDiagram
        {
            Entities = [users, roles],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = users.Id,
                    TargetEntityId = roles.Id,
                    Type = RelationshipType.ManyToMany,
                },
            ],
        };
    }

    /// <summary>customers(customer_id, name)</summary>
    private static ErDiagram Service_CustomersDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "customers",
                    Service_Column("customer_id", "int", primaryKey: true),
                    Service_Column("name", "nvarchar(100)")
                ),
            ],
        };

    /// <summary>主キー item_id ＋ 版の列 row_ver 1 本の図（rowversion の成功側の対照）</summary>
    private static ErDiagram Service_RowVersionDiagram(string versionType) =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "items",
                    Service_Column("item_id", "int", primaryKey: true),
                    Service_Column("row_ver", versionType)
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.BuildUnboundedBinaryDiagram の図</summary>
    private static ErDiagram Service_UnboundedBinaryDiagram(bool binaryColumnIsPrimaryKey) =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "assets",
                    Service_Column("asset_id", "int", primaryKey: !binaryColumnIsPrimaryKey),
                    Service_Column(
                        "payload",
                        "varbinary(max)",
                        primaryKey: binaryColumnIsPrimaryKey
                    )
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.BinaryColumnDiagram の図</summary>
    private static ErDiagram Service_BinaryColumnDiagram() =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "documents",
                    Service_Column("document_id", "int", primaryKey: true),
                    Service_Column("photo", "varbinary(max)", nullable: true),
                    Service_Column("row_version", "rowversion")
                ),
            ],
        };

    /// <summary>CSharpCodeGenerationServiceTests.GenerateWithColumns の図（主キー id ＋ int の列）</summary>
    private static ErDiagram Service_ColumnsDiagram(
        string tableName,
        string? tableDescription,
        params string[] columnNames
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
                    Columns =
                    [
                        Service_Column("id", "int", primaryKey: true),
                        .. columnNames.Select(columnName => Service_Column(columnName, "int")),
                    ],
                },
            ],
        };

    /// <summary>orders(order_id, 指定の int 列)（MemberNamedAsEnclosingClass の図）</summary>
    private static ErDiagram Service_OrdersWithColumnDiagram(string columnName) =>
        new()
        {
            Entities =
            [
                Service_Entity(
                    "orders",
                    Service_Column("order_id", "int", primaryKey: true),
                    Service_Column(columnName, "int")
                ),
            ],
        };

    /// <summary>customers 1:N orders の図（親の主キー・外部キーの列名と追加の列を切り替える）</summary>
    private static ErDiagram Service_CustomerOrderDiagram(
        string parentKey,
        Column? parentExtra,
        string childForeignKey,
        Column? childExtra = null
    )
    {
        var parentPk = Service_Column(parentKey, "int", primaryKey: true);
        var childFk = Service_Column(childForeignKey, "int", foreignKey: true);
        var parent = Service_Entity(
            "customers",
            new[] { parentPk, parentExtra }.OfType<Column>().ToArray()
        );
        var child = Service_Entity(
            "orders",
            new[] { Service_Column("order_id", "int", primaryKey: true), childFk, childExtra }
                .OfType<Column>()
                .ToArray()
        );

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = [new(parentPk.Id, childFk.Id)],
                },
            ],
        };
    }

    /// <summary>CSharpCodeGenerationServiceTests.Generate_CompositeRelationshipWarning_UsesReadableName の図</summary>
    private static ErDiagram Service_CompositeRelationshipDiagram(string? constraintName)
    {
        var parentA = new Column
        {
            Name = "a",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var parentB = new Column
        {
            Name = "b",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var childId = new Column
        {
            Name = "id",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var childA = new Column { Name = "a_ref", DataType = "int" };
        var childB = new Column { Name = "b_ref", DataType = "int" };
        var parent = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "parents",
            Columns = [parentA, parentB],
        };
        var child = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "children",
            Columns = [childId, childA, childB],
        };
        var relationship = new Relationship
        {
            Id = Guid.NewGuid(),
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ConstraintName = constraintName,
            ColumnPairs =
            [
                new RelationshipColumnPair(parentA.Id, childA.Id),
                new RelationshipColumnPair(parentB.Id, childB.Id),
            ],
        };

        return new ErDiagram { Entities = [parent, child], Relationships = [relationship] };
    }

    /// <summary>CSharpCodeGenerationServiceTests.ValueObjectDiagram の図（customers 5 列・orders 2 列の 1 対多。P3-4b で元の図へ戻した）</summary>
    private static ErDiagram Service_ValueObjectDiagram()
    {
        var customerPk = Service_Column("customer_id", "int", primaryKey: true);
        var orderFk = Service_Column("customer_id", "int", foreignKey: true);
        var customer = Service_Entity(
            "customers",
            customerPk,
            Service_Column("name", "nvarchar(50)"),
            Service_Column("balance", "decimal(10,2)", nullable: true),
            Service_Column("photo", "varbinary(max)", nullable: true),
            Service_Column("is_active", "bit")
        );
        var order = Service_Entity(
            "orders",
            Service_Column("order_id", "int", primaryKey: true),
            orderFk
        );

        return new ErDiagram
        {
            Entities = [customer, order],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    ColumnPairs = [new(customerPk.Id, orderFk.Id)],
                    Type = RelationshipType.OneToMany,
                },
            ],
        };
    }
}
