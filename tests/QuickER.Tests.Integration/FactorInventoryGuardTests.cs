using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using ParityFixture = QuickER.Tests.GeneratedBackendParityFixture;
using SyncFixture = QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// Integration が受け持つ製品の仕様の軸（<see cref="FactorInventoryAssignment"/>）の棚卸し：すべての項目が、Integration の
/// エンジンの台帳（<see cref="EngineRegistry"/>）の次元表の Source に載っているか、理由つきで除外されているかのちょうど一方
/// </summary>
/// <remarks>
/// <para>
/// 同期の型（SyncConflictReason・SyncMode・SyncConflictPolicy・SyncLocalRowChangeReason・SyncOptions）は、エンジンの次元表と同じく
/// 生成フィクスチャの写し（<c>QuickER.Tests.GeneratedSyncModelFixture</c>）を、ConcurrencyMode と SaveOperation は
/// <c>QuickER.Tests.GeneratedBackendParityFixture</c> の写しを反射する（Integration は QuickER.Runtime.Sync を参照しない。
/// 次元表と違う写しを反射すると、名前が同じでも別の型として載っていないことになる）。
/// SyncDialectCapabilities と SchemaImportWarningKind は製品の型（QuickER.Provider）。
/// </para>
/// <para>
/// 除外の項目の名前は文字列で書き、軸から生成しない（製品に値・プロパティを足すと、載せるか除外するまで赤になる）。
/// </para>
/// </remarks>
public sealed class FactorInventoryGuardTests
{
    /// <summary>Integration が検査する軸</summary>
    private static IReadOnlyList<InventoryAxis> Axes { get; } =
    [
        InventoryAxis.Properties(
            "SyncDialectCapabilities",
            typeof(global::QuickER.Provider.SyncDialectCapabilities)
        ),
        InventoryAxis.Enum<SyncFixture.SyncConflictReason>("SyncConflictReason"),
        InventoryAxis.Enum<SyncFixture.SyncMode>("SyncMode"),
        InventoryAxis.Enum<SyncFixture.SyncConflictPolicy>("SyncConflictPolicy"),
        InventoryAxis.Enum<SyncFixture.SyncLocalRowChangeReason>("SyncLocalRowChangeReason"),
        InventoryAxis.Properties("SyncOptions", typeof(SyncFixture.SyncOptions)),
        InventoryAxis.Enum<ParityFixture.ConcurrencyMode>("ConcurrencyMode"),
        InventoryAxis.Enum<ParityFixture.SaveOperation>("SaveOperation"),
        InventoryAxis.Enum<global::QuickER.Provider.SchemaImportWarningKind>(
            "SchemaImportWarningKind"
        ),
    ];

    /// <summary>同期計画の形を変える方言の能力の除外の理由</summary>
    private const string SyncPlanCapabilityReason =
        "同期計画の総当たり（SyncPlanFuzzTestsBase）は方言の派生ごとに能力の値を固定して流すだけで、能力を次元表の水準として観測していない（方言の名前の範囲に畳まれ、能力の値ごとの網羅を数えない）";

    /// <summary>差分の比較を変える方言の能力の除外の理由</summary>
    private const string DiffCapabilityReason =
        "差分の比較の正規化は SchemaDiffDialectNormalizationTests・方言ごとの *TypeNotationTests・DialectDocsTableTests の単体のテストが方言ごとに固定しているだけで、方言のエンジン（Dialect）の次元表がまだ無い（単体のテストをエンジンへ移すときに水準にする）";

    /// <summary>識別子の上限の能力の除外の理由</summary>
    private const string IdentifierLimitReason =
        "名前の総当たりの DDL の往復（NameFuzzDdlRoundTripTestsBase）は方言の派生ごとに上限を固定して流すだけで、上限と切り詰めを次元表の水準として観測していない（LongDefaultConstraintNameTests・IdentifierLengthLimitDeclarationTests が単体で固定）";

    /// <summary>取込の警告の種別の除外の理由</summary>
    private const string ImportWarningReason =
        "取込の警告の種別は SchemaImportWarningTextTests（全種別の文言）・SchemaImportWarningKindMapTests（扱いの表との写像）と方言ごとの *ImportFidelityIntegrationTests の単体のテストが固定しているだけで、方言のエンジン（Dialect）の次元表がまだ無い（単体のテストをエンジンへ移すときに水準にする）";

    /// <summary>まだどのエンジンの次元表にも載っていない項目（軸・項目・受け持つエンジン・区切り・理由）</summary>
    private static IReadOnlyList<InventoryExclusion> Exclusions { get; } =
    [
        .. new[]
        {
            "SupportsAlterColumn",
            "SupportsForeignKeyAlter",
            "SupportsDescriptions",
            "AlterColumnRequiresForeignKeyRebuild",
            "AlterColumnRequiresKeyConstraintRebuild",
            "ForeignKeyRequiresLeadingIndex",
            "PersistsForeignKeyConstraintNames",
            "ColumnReorder",
            "SetDefaultRequiresColumnDefault",
            "SetNullRequiresNullableChildColumn",
            "SupportsSchemaQualifiedTableNames",
            "ChecksDefaultOnColumnTypeChange",
            "SupportsComputedColumnTypeChange",
            "SupportsUniqueConstraintOnPrimaryKey",
            "ReferencedKeyRequiresSameColumnOrder",
            "ForeignKeyAllowsStringLengthDifference",
            "TransactionalDdl",
        }.Select(member => new InventoryExclusion(
            "SyncDialectCapabilities",
            member,
            EngineIds.SyncPlan,
            "P2",
            SyncPlanCapabilityReason
        )),
        .. new[]
        {
            "NormalizeTypeNotation",
            "SupportsOnUpdateAction",
            "SupportsSetDefaultAction",
        }.Select(member => new InventoryExclusion(
            "SyncDialectCapabilities",
            member,
            EngineIds.Dialect,
            "P3",
            DiffCapabilityReason
        )),
        .. new[] { "IdentifierLengthLimit", "TruncatesLongIdentifiers" }.Select(
            member => new InventoryExclusion(
                "SyncDialectCapabilities",
                member,
                EngineIds.Name,
                "P2",
                IdentifierLimitReason
            )
        ),
        .. new[]
        {
            "ModifiedOnServer",
            "MissingOnServer",
            "DuplicateOnServer",
            "ParentMissingOnServer",
            "DeleteRefusedOnServer",
            "UniqueValueTakenOnServer",
        }.Select(member => new InventoryExclusion(
            "SyncConflictReason",
            member,
            EngineIds.SyncModel,
            "P2",
            "同期のモデルは競合の理由を実行の結果（SyncResult.Conflicts）として表明に使うだけで、理由ごとにそれを起こす操作を次元表の水準として観測していない（理由ごとの形は決定的なシナリオ n7-*・n5-* などが個別に固定）"
        )),
        .. new[]
        {
            "DiscardedByServerWins",
            "ReloadedForUniqueValue",
            "RemovedBeforeDownload",
        }.Select(member => new InventoryExclusion(
            "SyncLocalRowChangeReason",
            member,
            EngineIds.SyncModel,
            "P2",
            "同期のモデルは実行が自分から変えた行の理由を実行の結果（SyncResult.LocalRowChanges）として表明に使うだけで、理由を次元表の水準として観測していない（理由ごとの形は決定的なシナリオ n2-*・n8-* などが個別に固定）"
        )),
        new InventoryExclusion(
            "SyncOptions",
            "ExcludedEntityTypes",
            EngineIds.SyncModel,
            "P2",
            "乱数の操作の列は SyncModelGenerator.NextSync が同期の 15% で 1 つの表を除外するが、除外の有無と表を次元表の因子にしておらず網羅を数えない"
        ),
        new InventoryExclusion(
            "SyncOptions",
            "DownloadBatchSize",
            EngineIds.SyncModel,
            "P2",
            "乱数の操作の列は SyncModelGenerator.NextSync がバッチの大きさを {1, 2, 3, 500} から引くが、次元表の因子にしておらず網羅を数えない"
        ),
        new InventoryExclusion(
            "SyncOptions",
            "PropagateDeletes",
            EngineIds.SyncModel,
            "P2",
            "乱数の操作の列は ModelSyncOp の既定（true）のままで削除伝搬を切るランを作らない（false は決定的なシナリオ n1-*・n2-* だけ）"
        ),
        new InventoryExclusion(
            "SyncOptions",
            "IncludeUnboundedBinary",
            EngineIds.SyncModel,
            "P2",
            "乱数の操作の列は blob を常に運ぶ（SyncModelGenerator.NextSync が IncludeBlobs を true に固定＝運ばないランを挟むと収束の表明が成り立たない）ので false を作らず、次元表の因子にもしていない"
        ),
        .. new[] { "Insert", "Update", "Delete" }.Select(member => new InventoryExclusion(
            "SaveOperation",
            member,
            EngineIds.GeneratedRuntime,
            "P2",
            "実装先どうしの一致は Save フックが受けた操作を結果（フックの記録）として比べるだけで、フックの操作の種類を次元表の水準として観測していない（SaveHookRuntimeTestsBase の単体のシナリオが固定）"
        )),
        .. new[]
        {
            "DomainTypeFlattened",
            "TableColumnsUnavailable",
            "ForeignKeyOutsideScope",
            "TableNameCollision",
            "ColumnNameCollision",
            "ColumnTypeNotEmittable",
            "PartitionDefinitionLost",
            "VirtualTableExcluded",
            "ComputedColumnExpressionLost",
            "DisabledConstraintExcluded",
            "TemporalHistoryTableExcluded",
            "PrimaryKeyNullabilityAdjusted",
            "ViewExcluded",
            "MaterializedViewExcluded",
            "ExternalTableExcluded",
            "ChildPartitionExcluded",
            "TemporaryTableExcluded",
            "ForeignKeyToExcludedTable",
            "ColumnDefaultLost",
            "AutoIncrementLost",
            "ColumnCollationLost",
            "InvisibleColumnLost",
            "ColumnOnUpdateLost",
            "SpatialReferenceIdLost",
            "AliasTypeFlattened",
            "NameTrimmed",
            "IdentityColumnMarked",
            "DottedTableNameExcluded",
            "UndeclaredColumnType",
        }.Select(member => new InventoryExclusion(
            "SchemaImportWarningKind",
            member,
            EngineIds.Dialect,
            "P3",
            ImportWarningReason
        )),
    ];

    [Fact(
        DisplayName = "[棚卸し] Integration が検査する軸は、軸の割り当ての表の Integration の軸とちょうど一致する"
    )]
    public void Axes_MatchAssignment() =>
        Axes.Select(axis => axis.Name)
            .Should()
            .Equal(FactorInventoryAssignment.AxesFor(FactorInventoryAssignment.Integration));

    [Fact(
        DisplayName = "[棚卸し] Integration の軸のすべての項目は、エンジンの次元表の Source か理由つきの除外のちょうど一方にある"
    )]
    public void EveryMember_IsSourcedOrExcluded() =>
        FactorInventoryGuard.Verify(Axes, EngineRegistry.All, Exclusions);
}
