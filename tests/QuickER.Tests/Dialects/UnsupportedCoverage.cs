using QuickER.Dialects;

namespace QuickER.Tests.Dialects;

/// <summary>
/// 方言の宣言の「検出する」セルと、扱いの表の「なし」以外のマスを、それぞれを固定するテストに結び付ける登録
/// </summary>
/// <remarks>
/// <para>
/// テストの名前は「クラス名.メソッド名」で、<see cref="UnsupportedCoverageTests"/> が実在を確かめる。
/// </para>
/// <para>
/// まだ実装していないセル・マスは未実装の一覧に、実装するフェーズ（区切り 33 の 2b〜2f）を添えて載せる。
/// 検出の未実装は 2d の終わりに、扱いの未実装は 2f の終わりに空になる。「要判断」はレビューで判断を仰いでいるもの。
/// </para>
/// </remarks>
public static class UnsupportedCoverage
{
    /// <summary>検出のテスト（方言・種類・テスト）</summary>
    public static readonly (
        string Dialect,
        UnsupportedDefinitionKind Kind,
        string[] Tests
    )[] Detections =
    [
        (
            DialectNames.SqlServer,
            UnsupportedDefinitionKind.ComputedColumn,
            [
                "SqlServerImportFidelityIntegrationTests.Import_ComputedColumn_IsMarkedAndWarns",
                "SqlServerImportFidelityIntegrationTests.Import_TemporalTable_ExcludesHistoryAndMarksPeriodColumns",
            ]
        ),
        (
            DialectNames.PostgreSql,
            UnsupportedDefinitionKind.ComputedColumn,
            ["PostgreSqlImportFidelityIntegrationTests.Import_GeneratedColumn_IsMarkedAndWarns"]
        ),
        (
            DialectNames.MySql,
            UnsupportedDefinitionKind.ComputedColumn,
            ["MySqlImportFidelityIntegrationTests.Import_GeneratedColumn_IsMarkedAndWarns"]
        ),
        (
            DialectNames.Oracle,
            UnsupportedDefinitionKind.ComputedColumn,
            ["OracleImportFidelityIntegrationTests.Import_VirtualColumn_IsMarkedAndWarns"]
        ),
        (
            DialectNames.Sqlite,
            UnsupportedDefinitionKind.ComputedColumn,
            ["SqliteImportFidelityIntegrationTests.Import_GeneratedColumn_IsMarkedAndWarns"]
        ),
        (
            DialectNames.Sqlite,
            UnsupportedDefinitionKind.PrimaryKeyNullabilityAdjusted,
            [
                "SqliteImportFidelityIntegrationTests.Import_RowidTableWithTextPrimaryKey_WarnsAndAdjustsToNotNull",
            ]
        ),
        (
            DialectNames.Sqlite,
            UnsupportedDefinitionKind.NonUniqueIndex,
            [
                "SqliteImportFidelityIntegrationTests.Import_MultipleVirtualTablesWithOrdinaryForeignKey_ExcludesEachBodyOnly",
                "SqliteSchemaSyncIntegrationTests.Rebuild_PreservesDataAndAuxiliaryObjects",
            ]
        ),
        (
            DialectNames.Sqlite,
            UnsupportedDefinitionKind.NonConstraintUniqueIndex,
            [
                "SqliteImportFidelityIntegrationTests.Import_NonConstraintUniqueIndexes_AreKeptAsAuxiliaryObjects",
            ]
        ),
        (
            DialectNames.Sqlite,
            UnsupportedDefinitionKind.Trigger,
            ["SqliteSchemaSyncIntegrationTests.Rebuild_PreservesDataAndAuxiliaryObjects"]
        ),
        (
            DialectNames.SqlServer,
            UnsupportedDefinitionKind.DisabledConstraint,
            [
                "SqlServerImportFidelityIntegrationTests.Import_DisabledForeignKey_IsExcludedButUntrustedForeignKeyIsImported",
            ]
        ),
        (
            DialectNames.Oracle,
            UnsupportedDefinitionKind.DisabledConstraint,
            ["OracleImportFidelityIntegrationTests.Import_DisabledConstraints_AreNotImported"]
        ),
        (
            DialectNames.Sqlite,
            UnsupportedDefinitionKind.VirtualTable,
            [
                "SqliteImportFidelityIntegrationTests.Import_Fts5VirtualTable_ExcludedWithSingleWarning",
                "SqliteImportFidelityIntegrationTests.Import_RTreeVirtualTable_ExcludedWithSingleWarning",
            ]
        ),
        (
            DialectNames.PostgreSql,
            UnsupportedDefinitionKind.PartitionedTable,
            [
                "PostgreSqlImportFidelityIntegrationTests.Import_IncludesPartitionParent_ExcludesPartitions",
            ]
        ),
        (
            DialectNames.MySql,
            UnsupportedDefinitionKind.PartitionedTable,
            ["MySqlImportFidelityIntegrationTests.Import_PartitionedTable_IsImportedWithWarning"]
        ),
        (
            DialectNames.Oracle,
            UnsupportedDefinitionKind.PartitionedTable,
            ["OracleImportFidelityIntegrationTests.Import_PartitionedTable_IsImportedWithWarning"]
        ),
        (
            DialectNames.SqlServer,
            UnsupportedDefinitionKind.TemporalHistoryTable,
            [
                "SqlServerImportFidelityIntegrationTests.Import_TemporalTable_ExcludesHistoryAndMarksPeriodColumns",
            ]
        ),
        (
            DialectNames.PostgreSql,
            UnsupportedDefinitionKind.FlattenedType,
            [
                "PostgreSqlImportFidelityIntegrationTests.Import_DomainColumn_FlattensToBaseTypeAndWarns",
            ]
        ),
        (
            DialectNames.PostgreSql,
            UnsupportedDefinitionKind.UnemittableTypeNotation,
            ["PostgreSqlImportFidelityIntegrationTests.Import_ColumnTypeNotEmittable_IsReported"]
        ),
        (
            DialectNames.SqlServer,
            UnsupportedDefinitionKind.UnemittableTypeNotation,
            [
                "ExistingUnsupportedBehaviorTests.DetectUnemittableColumnTypes_NamesUnsafeTypes",
                "ExistingUnsupportedBehaviorTests.AllImporters_CallDetectUnemittableColumnTypes",
            ]
        ),
        (
            DialectNames.MySql,
            UnsupportedDefinitionKind.UnemittableTypeNotation,
            [
                "ExistingUnsupportedBehaviorTests.DetectUnemittableColumnTypes_NamesUnsafeTypes",
                "ExistingUnsupportedBehaviorTests.AllImporters_CallDetectUnemittableColumnTypes",
            ]
        ),
        (
            DialectNames.Oracle,
            UnsupportedDefinitionKind.UnemittableTypeNotation,
            [
                "ExistingUnsupportedBehaviorTests.DetectUnemittableColumnTypes_NamesUnsafeTypes",
                "ExistingUnsupportedBehaviorTests.AllImporters_CallDetectUnemittableColumnTypes",
            ]
        ),
        (
            DialectNames.Sqlite,
            UnsupportedDefinitionKind.UnemittableTypeNotation,
            [
                "ExistingUnsupportedBehaviorTests.DetectUnemittableColumnTypes_NamesUnsafeTypes",
                "ExistingUnsupportedBehaviorTests.AllImporters_CallDetectUnemittableColumnTypes",
            ]
        ),
        (
            DialectNames.PostgreSql,
            UnsupportedDefinitionKind.TableWithoutReadableColumns,
            ["PostgreSqlImportFidelityIntegrationTests.Import_TableWithoutColumns_IsReported"]
        ),
    ];

    /// <summary>検出の未実装（方言・種類・実装するフェーズ）</summary>
    public static readonly (
        string Dialect,
        UnsupportedDefinitionKind Kind,
        string Phase
    )[] PendingDetections =
    [
        (DialectNames.SqlServer, UnsupportedDefinitionKind.View, "2b"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.View, "2b"),
        (DialectNames.MySql, UnsupportedDefinitionKind.View, "2b"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.View, "2b"),
        (DialectNames.Sqlite, UnsupportedDefinitionKind.View, "2b"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.MaterializedView, "2b"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.MaterializedView, "2b"),
        (DialectNames.SqlServer, UnsupportedDefinitionKind.ExternalTable, "2b"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.ExternalTable, "2b"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.ExternalTable, "2b"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.ChildPartition, "2b"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.TemporaryTable, "2b"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.ForeignKeyToExcludedTable, "2b"),
        (DialectNames.Sqlite, UnsupportedDefinitionKind.ForeignKeyToExcludedTable, "2b"),
        (DialectNames.SqlServer, UnsupportedDefinitionKind.DefaultValue, "2c"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.DefaultValue, "2c"),
        (DialectNames.MySql, UnsupportedDefinitionKind.DefaultValue, "2c"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.DefaultValue, "2c"),
        (DialectNames.Sqlite, UnsupportedDefinitionKind.DefaultValue, "2c"),
        (DialectNames.MySql, UnsupportedDefinitionKind.Collation, "2c"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.IdentityAcceptingValues, "2c"),
        (DialectNames.MySql, UnsupportedDefinitionKind.IdentityAcceptingValues, "2c"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.IdentityAcceptingValues, "2c"),
        (DialectNames.Sqlite, UnsupportedDefinitionKind.IdentityAcceptingValues, "2c"),
        (DialectNames.MySql, UnsupportedDefinitionKind.InvisibleColumn, "2c"),
        (DialectNames.MySql, UnsupportedDefinitionKind.ColumnOnUpdate, "2c"),
        (DialectNames.MySql, UnsupportedDefinitionKind.SpatialReferenceId, "2c"),
        (DialectNames.SqlServer, UnsupportedDefinitionKind.PartitionedTable, "2c"),
        (DialectNames.SqlServer, UnsupportedDefinitionKind.FlattenedType, "2c"),
        (DialectNames.SqlServer, UnsupportedDefinitionKind.PaddedName, "2c"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.PaddedName, "2c"),
        (DialectNames.MySql, UnsupportedDefinitionKind.PaddedName, "2c"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.PaddedName, "2c"),
        (DialectNames.Sqlite, UnsupportedDefinitionKind.PaddedName, "2c"),
        (DialectNames.SqlServer, UnsupportedDefinitionKind.IdentityRejectingValues, "2d"),
        (DialectNames.PostgreSql, UnsupportedDefinitionKind.IdentityRejectingValues, "2d"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.IdentityRejectingValues, "2d"),
        (DialectNames.MySql, UnsupportedDefinitionKind.TableWithoutReadableColumns, "要判断"),
        (DialectNames.Oracle, UnsupportedDefinitionKind.TableWithoutReadableColumns, "要判断"),
    ];

    /// <summary>扱いのテスト（種類・機能・方言〔null は「なし」以外の全方言〕・テスト）</summary>
    public static readonly (
        UnsupportedDefinitionKind Kind,
        UnsupportedFeature Feature,
        string? Dialect,
        string[] Tests
    )[] Treatments =
    [
        (
            UnsupportedDefinitionKind.ComputedColumn,
            UnsupportedFeature.Import,
            null,
            [
                "SqlServerImportFidelityIntegrationTests.Import_ComputedColumn_IsMarkedAndWarns",
                "PostgreSqlImportFidelityIntegrationTests.Import_GeneratedColumn_IsMarkedAndWarns",
                "MySqlImportFidelityIntegrationTests.Import_GeneratedColumn_IsMarkedAndWarns",
                "OracleImportFidelityIntegrationTests.Import_VirtualColumn_IsMarkedAndWarns",
                "SqliteImportFidelityIntegrationTests.Import_GeneratedColumn_IsMarkedAndWarns",
            ]
        ),
        (
            UnsupportedDefinitionKind.ComputedColumn,
            UnsupportedFeature.Sync,
            null,
            [
                "ComputedColumnCommentTests.Sync_AddTable_ComputedColumn_EmitsNote",
                "ComputedColumnCommentTests.Sync_AddColumn_ComputedColumn_EmitsNote",
                "ComputedColumnCommentTests.Sync_SqliteRebuild_ComputedColumn_EmitsNote",
            ]
        ),
        (
            UnsupportedDefinitionKind.ComputedColumn,
            UnsupportedFeature.Ddl,
            null,
            ["ComputedColumnCommentTests.Ddl_ComputedColumn_EmitsNoteBeforeColumnLine"]
        ),
        (
            UnsupportedDefinitionKind.ComputedColumn,
            UnsupportedFeature.CSharpGeneration,
            null,
            [
                "ComputedColumnGenerationTests.Generate_ComputedColumn_MarksAttributeAndExcludesFromWrite",
                "ComputedColumnGenerationTests.Generate_ComputedColumn_ReportsInfoDiagnostic",
                "ComputedColumnGenerationTests.Generate_ComputedPrimaryKey_FailsWithError",
            ]
        ),
        (
            UnsupportedDefinitionKind.ComputedColumn,
            UnsupportedFeature.Export,
            null,
            [
                "MermaidFormatFidelityTests.DetectOmissions_ComputedColumn_IsReportedOnlyWhenPresent",
                "DbmlLiteralFidelityTests.DetectOmissions_ComputedColumn_IsReportedOnlyWhenPresent",
            ]
        ),
        (
            UnsupportedDefinitionKind.ComputedColumn,
            UnsupportedFeature.ToolHosts,
            null,
            [
                "EntityViewModelTests.ToModel_PreservesIsComputed",
                "ComputedColumnPersistenceTests.SaveAndLoad_RoundTripsIsComputed",
            ]
        ),
        (
            UnsupportedDefinitionKind.ComputedColumn,
            UnsupportedFeature.Reverse,
            null,
            ["CSharpReverseRoundTripTests.RoundTrip_ComputedColumn_Matches"]
        ),
        (
            UnsupportedDefinitionKind.DefaultValue,
            UnsupportedFeature.Sync,
            DialectNames.Sqlite,
            ["TableRebuildAttributeDetectorTests.Detects_Default"]
        ),
        (
            UnsupportedDefinitionKind.CheckConstraint,
            UnsupportedFeature.Sync,
            DialectNames.Sqlite,
            ["TableRebuildAttributeDetectorTests.Detects_Check"]
        ),
        (
            UnsupportedDefinitionKind.Collation,
            UnsupportedFeature.Sync,
            DialectNames.Sqlite,
            ["TableRebuildAttributeDetectorTests.Detects_Collate"]
        ),
        (
            UnsupportedDefinitionKind.IdentityAcceptingValues,
            UnsupportedFeature.Sync,
            DialectNames.Sqlite,
            ["TableRebuildAttributeDetectorTests.Detects_AutoIncrement"]
        ),
        (
            UnsupportedDefinitionKind.PrimaryKeyNullabilityAdjusted,
            UnsupportedFeature.Import,
            DialectNames.Sqlite,
            [
                "SqliteImportFidelityIntegrationTests.Import_RowidTableWithTextPrimaryKey_WarnsAndAdjustsToNotNull",
            ]
        ),
        (
            UnsupportedDefinitionKind.DisabledConstraint,
            UnsupportedFeature.Import,
            DialectNames.SqlServer,
            [
                "SqlServerImportFidelityIntegrationTests.Import_DisabledForeignKey_IsExcludedButUntrustedForeignKeyIsImported",
            ]
        ),
        (
            UnsupportedDefinitionKind.DisabledConstraint,
            UnsupportedFeature.Import,
            DialectNames.Oracle,
            ["OracleImportFidelityIntegrationTests.Import_DisabledConstraints_AreNotImported"]
        ),
        (
            UnsupportedDefinitionKind.ConstraintClause,
            UnsupportedFeature.Sync,
            DialectNames.Sqlite,
            [
                "SqliteSchemaSyncIntegrationTests.Rebuild_WarnsTableOptionsAndConstraintClausesLost",
                "TableRebuildAttributeDetectorTests.Detects_OnConflict",
                "TableRebuildAttributeDetectorTests.Detects_DeferredForeignKey",
            ]
        ),
        (
            UnsupportedDefinitionKind.View,
            UnsupportedFeature.Sync,
            DialectNames.Sqlite,
            [
                "SqliteSchemaSyncIntegrationTests.Rebuild_WithDependentView_Succeeds",
                "SqliteSchemaSyncIntegrationTests.Rebuild_BreakingView_RollsBack",
            ]
        ),
        (
            UnsupportedDefinitionKind.MaterializedViewInternals,
            UnsupportedFeature.Import,
            DialectNames.Oracle,
            [
                "OracleImportFidelityIntegrationTests.Import_ExcludesMaterializedViewLogTables",
                "OracleImportFidelityIntegrationTests.Import_ExcludesTemporaryTablesAndMaterializedViews",
            ]
        ),
        (
            UnsupportedDefinitionKind.ShadowTable,
            UnsupportedFeature.Import,
            DialectNames.Sqlite,
            [
                "SqliteImportFidelityIntegrationTests.Import_MultipleVirtualTablesWithOrdinaryForeignKey_ExcludesEachBodyOnly",
                "SqliteImportFidelityIntegrationTests.Import_Fts5VirtualTable_ExcludedWithSingleWarning",
            ]
        ),
        (
            UnsupportedDefinitionKind.PartitionedTable,
            UnsupportedFeature.Import,
            DialectNames.PostgreSql,
            [
                "PostgreSqlImportFidelityIntegrationTests.Import_IncludesPartitionParent_ExcludesPartitions",
            ]
        ),
        (
            UnsupportedDefinitionKind.PartitionedTable,
            UnsupportedFeature.Import,
            DialectNames.MySql,
            ["MySqlImportFidelityIntegrationTests.Import_PartitionedTable_IsImportedWithWarning"]
        ),
        (
            UnsupportedDefinitionKind.PartitionedTable,
            UnsupportedFeature.Import,
            DialectNames.Oracle,
            ["OracleImportFidelityIntegrationTests.Import_PartitionedTable_IsImportedWithWarning"]
        ),
        (
            UnsupportedDefinitionKind.TableOption,
            UnsupportedFeature.Sync,
            DialectNames.Sqlite,
            [
                "SqliteSchemaSyncIntegrationTests.Rebuild_WarnsTableOptionsAndConstraintClausesLost",
                "TableRebuildAttributeDetectorTests.Detects_TableOptions",
            ]
        ),
        (
            UnsupportedDefinitionKind.ExtensionOwnedTable,
            UnsupportedFeature.Import,
            DialectNames.PostgreSql,
            ["PostgreSqlDdlRoundTripIntegrationTests.Import_ExcludesExtensionOwnedTables"]
        ),
        (
            UnsupportedDefinitionKind.SystemTable,
            UnsupportedFeature.Import,
            DialectNames.SqlServer,
            ["SqlServerDdlRoundTripIntegrationTests.Import_ExcludesDatabaseToolsSupportTables"]
        ),
        (
            UnsupportedDefinitionKind.SystemTable,
            UnsupportedFeature.Import,
            DialectNames.Sqlite,
            ["SqliteImportFidelityIntegrationTests.Import_InternalSqliteTables_AreExcludedSilently"]
        ),
        (
            UnsupportedDefinitionKind.FlattenedType,
            UnsupportedFeature.Import,
            DialectNames.PostgreSql,
            [
                "PostgreSqlImportFidelityIntegrationTests.Import_DomainColumn_FlattensToBaseTypeAndWarns",
            ]
        ),
        (
            UnsupportedDefinitionKind.UnparsableType,
            UnsupportedFeature.CSharpGeneration,
            null,
            [
                "FallbackTypeGenerationTests.Generate_FallbackTypeColumn_ReportsInfoDiagnosticWithTableColumnAndDataType",
            ]
        ),
        (
            UnsupportedDefinitionKind.UnparsableType,
            UnsupportedFeature.Reverse,
            null,
            ["CSharpReverseParserTests.Parse_UnresolvableTypeToken_FallsBackWithWarning"]
        ),
        (
            UnsupportedDefinitionKind.UnemittableTypeNotation,
            UnsupportedFeature.Import,
            null,
            [
                "PostgreSqlImportFidelityIntegrationTests.Import_ColumnTypeNotEmittable_IsReported",
                "ExistingUnsupportedBehaviorTests.DetectUnemittableColumnTypes_NamesUnsafeTypes",
                "ExistingUnsupportedBehaviorTests.AllImporters_CallDetectUnemittableColumnTypes",
            ]
        ),
        (
            UnsupportedDefinitionKind.UnemittableTypeNotation,
            UnsupportedFeature.Sync,
            null,
            ["SqlTypeTextTests.SyncScriptBuilders_RejectUnsafeDataType"]
        ),
        (
            UnsupportedDefinitionKind.UnemittableTypeNotation,
            UnsupportedFeature.Ddl,
            null,
            ["SqlTypeTextTests.DdlGenerators_RejectUnsafeDataType"]
        ),
        (
            UnsupportedDefinitionKind.PaddedName,
            UnsupportedFeature.Diff,
            null,
            ["ExistingUnsupportedBehaviorTests.PaddedLiveNames_AreFoldedInDiff"]
        ),
        (
            UnsupportedDefinitionKind.TableWithoutReadableColumns,
            UnsupportedFeature.Import,
            DialectNames.PostgreSql,
            ["PostgreSqlImportFidelityIntegrationTests.Import_TableWithoutColumns_IsReported"]
        ),
        (
            UnsupportedDefinitionKind.ComputedColumnTypeChange,
            UnsupportedFeature.Diff,
            null,
            [
                "SchemaDiffServiceTests.ComputedColumn_TypeChange_IsSelectableOnlyWhereSupported",
                "SchemaDiffServiceTests.ComputedColumn_NullabilityChange_IsNotDiffed",
            ]
        ),
        (
            UnsupportedDefinitionKind.CompositeOrMissingPrimaryKey,
            UnsupportedFeature.CSharpGeneration,
            null,
            [
                "EfCoreKeylessTableModelTests.主キーなしテーブルは除外されモデルは使える",
                "RepositorySingleKeyWarningTests.Generate_NonSingleKeyTable_SkipsRepositoryWithWarning",
            ]
        ),
        (
            UnsupportedDefinitionKind.CompositeForeignKey,
            UnsupportedFeature.CSharpGeneration,
            null,
            [
                "CSharpCodeGenerationServiceTests.Generate_CompositeRelationshipWarning_UsesReadableName",
            ]
        ),
        (
            UnsupportedDefinitionKind.CompositeForeignKey,
            UnsupportedFeature.Export,
            null,
            [
                "MermaidTests.DetectOmissions_ListsUnsupportedInformation",
                "DbmlTests.CompositeForeignKey_RoundTripsThroughCompositeRefSyntax",
            ]
        ),
        (
            UnsupportedDefinitionKind.ManyToManyRelationship,
            UnsupportedFeature.Ddl,
            null,
            [
                "ExistingUnsupportedBehaviorTests.ManyToMany_DdlEmitsJunctionNote_AllDialects",
                "SqliteDdlGeneratorTests.Build_ManyToMany_EmitsComment",
            ]
        ),
        (
            UnsupportedDefinitionKind.ManyToManyRelationship,
            UnsupportedFeature.CSharpGeneration,
            null,
            ["CSharpCodeGenerationServiceTests.Generate_ShouldWarnAndSkipManyToManyRelationship"]
        ),
        (
            UnsupportedDefinitionKind.NotEditableColumn,
            UnsupportedFeature.CSharpGeneration,
            null,
            [
                "PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumns_AreReportedAsInfo",
                "PostgreSqlNetworkTypeGenerationTests.Generate_InetColumns_ReportedAsInfo",
            ]
        ),
        (
            UnsupportedDefinitionKind.UnrepresentableType,
            UnsupportedFeature.CSharpGeneration,
            null,
            [
                "NegativeScaleColumnGenerationTests.Generate_NegativeScaleColumn_ReportsInfoDiagnostic",
            ]
        ),
        (
            UnsupportedDefinitionKind.UnrepresentableType,
            UnsupportedFeature.ToolHosts,
            null,
            ["MainViewModelTargetDbmsTests.ChangeDbms_Unconverted_ConfirmsBeforeApply"]
        ),
        (
            UnsupportedDefinitionKind.UnrepresentableType,
            UnsupportedFeature.Reverse,
            null,
            ["CSharpReverseLengthlessTokenTests.Parse_LengthlessToken_KeepsTokenTextAndWarns"]
        ),
        (
            UnsupportedDefinitionKind.OnUpdateReferentialAction,
            UnsupportedFeature.Diff,
            null,
            [
                "SchemaDiffDialectNormalizationTests.UnsupportedReferentialAction_IsFolded",
                "OracleTypeNotationIntegrationTests.UnsupportedReferentialActions_LeaveNoForeignKeyDiff",
            ]
        ),
        (
            UnsupportedDefinitionKind.OnUpdateReferentialAction,
            UnsupportedFeature.Sync,
            DialectNames.Oracle,
            ["OracleSyncScriptBuilderTests.AddForeignKey_GeneratesConstraint_NoOnUpdate"]
        ),
        (
            UnsupportedDefinitionKind.OnUpdateReferentialAction,
            UnsupportedFeature.Ddl,
            DialectNames.Oracle,
            ["OracleDdlGeneratorTests.Build_OneToMany_EmitsForeignKey_NoOnUpdate"]
        ),
        (
            UnsupportedDefinitionKind.OnUpdateReferentialAction,
            UnsupportedFeature.Export,
            null,
            [
                "MermaidTests.DetectOmissions_ListsUnsupportedInformation",
                "DbmlTests.Export_ReferentialActions_RoundTrip",
            ]
        ),
        (
            UnsupportedDefinitionKind.OnUpdateReferentialAction,
            UnsupportedFeature.Reverse,
            null,
            [
                "CSharpReverseParserTests.Parse_RestoresForeignKeyMetadata",
                "CSharpReverseRoundTripTests.RoundTrip_UniqueConstraintsAndForeignKeyMetadata_Matches",
            ]
        ),
        (
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Diff,
            null,
            [
                "SchemaDiffDialectNormalizationTests.UnsupportedReferentialAction_IsFolded",
                "SchemaDiffDialectNormalizationTests.MySql_FoldsOnlySetDefault",
                "MySqlReferentialActionIntegrationTests.SetDefault_IsOmitted_AndLeavesNoForeignKeyDiff",
            ]
        ),
        (
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Sync,
            DialectNames.MySql,
            ["MySqlSyncScriptBuilderTests.AddForeignKey_SetDefault_IsOmittedWithNote"]
        ),
        (
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Ddl,
            DialectNames.MySql,
            ["MySqlDdlGeneratorTests.Build_SetDefault_IsOmittedWithNote"]
        ),
        (
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Export,
            null,
            [
                "MermaidTests.DetectOmissions_ListsUnsupportedInformation",
                "DbmlTests.Export_ReferentialActions_RoundTrip",
            ]
        ),
        (
            UnsupportedDefinitionKind.Description,
            UnsupportedFeature.Diff,
            null,
            ["SchemaDiffServiceTests.CapabilitiesWithoutDescriptions_SuppressesDescriptionDiffs"]
        ),
        (
            UnsupportedDefinitionKind.Description,
            UnsupportedFeature.Ddl,
            null,
            ["SqliteDdlGeneratorTests.Build_Descriptions_EmittedAsSqlComments"]
        ),
        (
            UnsupportedDefinitionKind.ColumnOrder,
            UnsupportedFeature.Diff,
            null,
            [
                "SchemaDiffServiceTests.Reorder_NativeDialect_GeneratesUnselectedReorderItem",
                "SchemaDiffServiceTests.Reorder_RebuildDialect_GeneratesReorderItem",
                "SchemaDiffServiceTests.Reorder_NoneDialectOrNoCapabilities_DoesNotGenerate",
            ]
        ),
        (
            UnsupportedDefinitionKind.ColumnOrder,
            UnsupportedFeature.Sync,
            DialectNames.MySql,
            [
                "MySqlSyncScriptBuilderTests.Reorder_GeneratesModifyAfterWithHeading",
                "MySqlSchemaSyncIntegrationTests.SchemaSync_ColumnReorder_AppliesToRealDb",
            ]
        ),
        (
            UnsupportedDefinitionKind.ColumnOrder,
            UnsupportedFeature.Sync,
            DialectNames.Sqlite,
            [
                "SyncPlannerTests.Rebuild_ReorderColumns_ProducesTargetOrderedRebuild",
                "SqliteSchemaSyncIntegrationTests.Rebuild_ColumnReorderWithInsertion_AppliesInOneSync",
            ]
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyConstraintName,
            UnsupportedFeature.Diff,
            null,
            [
                "SchemaDiffServiceTests.ForeignKeyChange_WithoutPersistedNames_EmitsSingleAlter",
                "SchemaDiffServiceTests.CapabilitiesWithoutFkConstraintNames_SuppressesNameOnlyForeignKeyDiff",
            ]
        ),
        (
            UnsupportedDefinitionKind.CompositeForeignKeyColumnOrder,
            UnsupportedFeature.Diff,
            null,
            [
                "SchemaDiffServiceTests.CompositeForeignKeyPairOrder_IsComparedOnlyWhereItMatters",
                "OracleSchemaSyncIntegrationTests.CompositeForeignKeyDeclaredInOtherOrder_ProducesNoDiff",
            ]
        ),
        (
            UnsupportedDefinitionKind.CompositeForeignKeyColumnOrder,
            UnsupportedFeature.Sync,
            DialectNames.MySql,
            [
                "SyncPlannerTests.AddForeignKeyOnNewKeyOrder_WithExcludedKeyChange_IsExcluded",
                "MySqlSyncDependencyIntegrationTests.ReorderReferencedCompositePrimaryKey_IsExcludedAndChangesNothing",
            ]
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyTypeMismatch,
            UnsupportedFeature.Diff,
            DialectNames.MySql,
            [
                "SyncPlannerTests.AlterColumn_StringLengthOnOneSide_RebuildsWhereLengthMayDiffer",
                "SyncPlannerTests.AreForeignKeyCompatibleTypes_AllowsStringLengthOnly",
            ]
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyTypeMismatch,
            UnsupportedFeature.Sync,
            null,
            ["SyncPlannerTests.AlterColumn_OneSideOfForeignKey_WarnsAndDoesNotInject"]
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyNotOnCandidateKey,
            UnsupportedFeature.Sync,
            null,
            [
                "SyncPlannerTests.AddForeignKey_NotOnCandidateKey_Warns",
                "SyncPlannerTests.AlterPrimaryKey_ReferencedColumnLeavesPrimaryKey_AddsWarning",
                "SyncPlannerTests.DropUniqueConstraint_OnReferencedColumn_AddsWarning",
            ]
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyNotOnCandidateKey,
            UnsupportedFeature.ToolHosts,
            null,
            ["ErDiagramToolResponseNoteTests.AddRelationship_ExplicitNonCandidateKey_IsReported"]
        ),
    ];

    /// <summary>扱いの未実装（種類・機能・方言〔null は「なし」以外の全方言〕・実装するフェーズ）</summary>
    public static readonly (
        UnsupportedDefinitionKind Kind,
        UnsupportedFeature Feature,
        string? Dialect,
        string Phase
    )[] PendingTreatments =
    [
        (UnsupportedDefinitionKind.View, UnsupportedFeature.Import, null, "2b"),
        (UnsupportedDefinitionKind.MaterializedView, UnsupportedFeature.Import, null, "2b"),
        (UnsupportedDefinitionKind.VirtualTable, UnsupportedFeature.Import, null, "2b"),
        (UnsupportedDefinitionKind.ExternalTable, UnsupportedFeature.Import, null, "2b"),
        (UnsupportedDefinitionKind.ChildPartition, UnsupportedFeature.Import, null, "2b"),
        (UnsupportedDefinitionKind.TemporalHistoryTable, UnsupportedFeature.Import, null, "2b"),
        (UnsupportedDefinitionKind.TemporaryTable, UnsupportedFeature.Import, null, "2b"),
        (
            UnsupportedDefinitionKind.ForeignKeyToExcludedTable,
            UnsupportedFeature.Import,
            null,
            "2b"
        ),
        (UnsupportedDefinitionKind.DefaultValue, UnsupportedFeature.Import, null, "2c"),
        (UnsupportedDefinitionKind.Collation, UnsupportedFeature.Import, null, "2c"),
        (UnsupportedDefinitionKind.IdentityAcceptingValues, UnsupportedFeature.Import, null, "2c"),
        (UnsupportedDefinitionKind.InvisibleColumn, UnsupportedFeature.Import, null, "2c"),
        (UnsupportedDefinitionKind.ColumnOnUpdate, UnsupportedFeature.Import, null, "2c"),
        (UnsupportedDefinitionKind.SpatialReferenceId, UnsupportedFeature.Import, null, "2c"),
        (UnsupportedDefinitionKind.PaddedName, UnsupportedFeature.Import, null, "2c"),
        (
            UnsupportedDefinitionKind.PartitionedTable,
            UnsupportedFeature.Import,
            DialectNames.SqlServer,
            "2c"
        ),
        (
            UnsupportedDefinitionKind.FlattenedType,
            UnsupportedFeature.Import,
            DialectNames.SqlServer,
            "2c"
        ),
        (UnsupportedDefinitionKind.IdentityRejectingValues, UnsupportedFeature.Import, null, "2d"),
        (UnsupportedDefinitionKind.IdentityRejectingValues, UnsupportedFeature.Sync, null, "2d"),
        (UnsupportedDefinitionKind.IdentityRejectingValues, UnsupportedFeature.Ddl, null, "2d"),
        (
            UnsupportedDefinitionKind.IdentityRejectingValues,
            UnsupportedFeature.CSharpGeneration,
            null,
            "2d"
        ),
        (UnsupportedDefinitionKind.IdentityRejectingValues, UnsupportedFeature.Export, null, "2d"),
        (
            UnsupportedDefinitionKind.IdentityRejectingValues,
            UnsupportedFeature.ToolHosts,
            null,
            "2d"
        ),
        (UnsupportedDefinitionKind.IdentityRejectingValues, UnsupportedFeature.Reverse, null, "2d"),
        (
            UnsupportedDefinitionKind.UniqueConstraintOnPrimaryKey,
            UnsupportedFeature.Diff,
            null,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.UniqueConstraintOnPrimaryKey,
            UnsupportedFeature.Sync,
            null,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.UniqueConstraintOnPrimaryKey,
            UnsupportedFeature.Ddl,
            null,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Sync,
            DialectNames.SqlServer,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Sync,
            DialectNames.Oracle,
            "2e"
        ),
        (UnsupportedDefinitionKind.SchemaQualifiedTableName, UnsupportedFeature.Sync, null, "2e"),
        (
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Ddl,
            DialectNames.SqlServer,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Ddl,
            DialectNames.Oracle,
            "2e"
        ),
        (UnsupportedDefinitionKind.SchemaQualifiedTableName, UnsupportedFeature.Ddl, null, "2e"),
        (
            UnsupportedDefinitionKind.CompositeForeignKeyColumnOrder,
            UnsupportedFeature.Ddl,
            null,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyTypeMismatch,
            UnsupportedFeature.Diff,
            DialectNames.PostgreSql,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyTypeMismatch,
            UnsupportedFeature.Diff,
            DialectNames.Oracle,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyTypeMismatch,
            UnsupportedFeature.Diff,
            DialectNames.Sqlite,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.ForeignKeyTypeMismatch,
            UnsupportedFeature.Ddl,
            DialectNames.SqlServer,
            "2e"
        ),
        (
            UnsupportedDefinitionKind.DefaultValue,
            UnsupportedFeature.Sync,
            DialectNames.SqlServer,
            "2f"
        ),
        (
            UnsupportedDefinitionKind.DefaultValue,
            UnsupportedFeature.Sync,
            DialectNames.PostgreSql,
            "2f"
        ),
        (UnsupportedDefinitionKind.DefaultValue, UnsupportedFeature.Sync, DialectNames.MySql, "2f"),
        (
            UnsupportedDefinitionKind.DefaultValue,
            UnsupportedFeature.Sync,
            DialectNames.Oracle,
            "2f"
        ),
        (
            UnsupportedDefinitionKind.Collation,
            UnsupportedFeature.Sync,
            DialectNames.SqlServer,
            "2f"
        ),
        (
            UnsupportedDefinitionKind.Collation,
            UnsupportedFeature.Sync,
            DialectNames.PostgreSql,
            "2f"
        ),
        (UnsupportedDefinitionKind.Collation, UnsupportedFeature.Sync, DialectNames.MySql, "2f"),
        (
            UnsupportedDefinitionKind.IdentityAcceptingValues,
            UnsupportedFeature.Sync,
            DialectNames.MySql,
            "2f"
        ),
        (
            UnsupportedDefinitionKind.InvisibleColumn,
            UnsupportedFeature.Sync,
            DialectNames.MySql,
            "2f"
        ),
        (
            UnsupportedDefinitionKind.ColumnOnUpdate,
            UnsupportedFeature.Sync,
            DialectNames.MySql,
            "2f"
        ),
        (
            UnsupportedDefinitionKind.SpatialReferenceId,
            UnsupportedFeature.Sync,
            DialectNames.MySql,
            "2f"
        ),
        (
            UnsupportedDefinitionKind.TableWithoutReadableColumns,
            UnsupportedFeature.Import,
            DialectNames.MySql,
            "要判断"
        ),
        (
            UnsupportedDefinitionKind.TableWithoutReadableColumns,
            UnsupportedFeature.Import,
            DialectNames.Oracle,
            "要判断"
        ),
    ];
}
