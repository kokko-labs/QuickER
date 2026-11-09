using QuickER.Dialects;

namespace QuickER.Provider;

/// <summary>取込警告の種別（<see cref="SchemaImportWarningKind"/>）と、図で表せない定義の種類の対応</summary>
/// <remarks>
/// <para>
/// 取込警告の文言化の網羅は警告の種別の単位、方言の宣言の網羅は種類の単位で行うので、両者の対応をここに 1 つだけ置く。
/// 取込範囲の外を参照する外部キーと、名前の衝突・名前に <c>.</c> を含むテーブルは、図で表せない定義の種類に入れない（種類の外＝<c>null</c>）。
/// </para>
/// <para>
/// 対応の網羅（すべての種別が対応を持つ・取込で告知する種類が種別を持つ）は <c>SchemaImportWarningKindMapTests</c> が確かめる。
/// </para>
/// </remarks>
public static class SchemaImportWarningKinds
{
    /// <summary>警告の種別が告げる種類（種類の外なら <c>null</c>）</summary>
    /// <exception cref="ArgumentOutOfRangeException">定義されていない種別</exception>
    public static UnsupportedDefinitionKind? DefinitionKind(SchemaImportWarningKind kind) =>
        kind switch
        {
            SchemaImportWarningKind.DomainTypeFlattened => UnsupportedDefinitionKind.FlattenedType,
            SchemaImportWarningKind.TableColumnsUnavailable =>
                UnsupportedDefinitionKind.TableWithoutReadableColumns,
            SchemaImportWarningKind.ForeignKeyOutsideScope => null,
            SchemaImportWarningKind.TableNameCollision => null,
            SchemaImportWarningKind.ColumnNameCollision => null,
            SchemaImportWarningKind.ColumnTypeNotEmittable =>
                UnsupportedDefinitionKind.UnemittableTypeNotation,
            SchemaImportWarningKind.PartitionDefinitionLost =>
                UnsupportedDefinitionKind.PartitionedTable,
            SchemaImportWarningKind.VirtualTableExcluded => UnsupportedDefinitionKind.VirtualTable,
            SchemaImportWarningKind.ComputedColumnExpressionLost =>
                UnsupportedDefinitionKind.ComputedColumn,
            SchemaImportWarningKind.DisabledConstraintExcluded =>
                UnsupportedDefinitionKind.DisabledConstraint,
            SchemaImportWarningKind.TemporalHistoryTableExcluded =>
                UnsupportedDefinitionKind.TemporalHistoryTable,
            SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted =>
                UnsupportedDefinitionKind.PrimaryKeyNullabilityAdjusted,
            SchemaImportWarningKind.ViewExcluded => UnsupportedDefinitionKind.View,
            SchemaImportWarningKind.MaterializedViewExcluded =>
                UnsupportedDefinitionKind.MaterializedView,
            SchemaImportWarningKind.ExternalTableExcluded =>
                UnsupportedDefinitionKind.ExternalTable,
            SchemaImportWarningKind.ChildPartitionExcluded =>
                UnsupportedDefinitionKind.ChildPartition,
            SchemaImportWarningKind.TemporaryTableExcluded =>
                UnsupportedDefinitionKind.TemporaryTable,
            SchemaImportWarningKind.ForeignKeyToExcludedTable =>
                UnsupportedDefinitionKind.ForeignKeyToExcludedTable,
            SchemaImportWarningKind.ColumnDefaultLost => UnsupportedDefinitionKind.DefaultValue,
            SchemaImportWarningKind.AutoIncrementLost =>
                UnsupportedDefinitionKind.IdentityAcceptingValues,
            SchemaImportWarningKind.ColumnCollationLost => UnsupportedDefinitionKind.Collation,
            SchemaImportWarningKind.InvisibleColumnLost =>
                UnsupportedDefinitionKind.InvisibleColumn,
            SchemaImportWarningKind.ColumnOnUpdateLost => UnsupportedDefinitionKind.ColumnOnUpdate,
            SchemaImportWarningKind.SpatialReferenceIdLost =>
                UnsupportedDefinitionKind.SpatialReferenceId,
            SchemaImportWarningKind.AliasTypeFlattened => UnsupportedDefinitionKind.FlattenedType,
            SchemaImportWarningKind.NameTrimmed => UnsupportedDefinitionKind.PaddedName,
            SchemaImportWarningKind.IdentityColumnMarked =>
                UnsupportedDefinitionKind.IdentityRejectingValues,
            SchemaImportWarningKind.DottedTableNameExcluded => null,
            SchemaImportWarningKind.UndeclaredColumnType =>
                UnsupportedDefinitionKind.UndeclaredColumnType,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>
    /// その表を作り直すと、警告が告げる物（取込が図へ入れずに捨てた、表の中の列・外部キー）が黙って消える種別か
    /// </summary>
    /// <remarks>
    /// <para>
    /// テーブルの再構築（SQLite の同期）は live の図から表を作り直すので、取込が捨てた物は新しい表に無く、データごと消える。
    /// <c>true</c> の種別の警告がある表は、同期の計画が再構築を外して名指しする（<see cref="SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent"/>）。
    /// 列の属性・テーブルオプション（既定値・照合順序・生成列の式など）は再構築の属性の検出（<see cref="TableRebuildAttributeDetector"/>）が
    /// 警告して進めるので数えない。表の外の物（ビュー・除外した表そのもの）も数えない（その表を作り直しても消えない）。
    /// </para>
    /// <para>
    /// 既定の腕を持たない（種別を足したら、作り直しで消えるかをここで決める。決め忘れは <c>SchemaImportWarningKindMapTests</c> が赤にする）。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">定義されていない種別</exception>
    public static bool RebuildLosesTableContent(SchemaImportWarningKind kind) =>
        kind switch
        {
            // 大文字小文字・前後の空白だけ違う列名の後着の列（データを持つ）
            SchemaImportWarningKind.ColumnNameCollision => true,
            // 取込の範囲の外・取り込まなかった表を参照する外部キー
            SchemaImportWarningKind.ForeignKeyOutsideScope => true,
            SchemaImportWarningKind.ForeignKeyToExcludedTable => true,
            // 列を読めなかった表（図の表は列を持たない）
            SchemaImportWarningKind.TableColumnsUnavailable => true,
            // 前後の空白を除いた名前（作り直すと除いた名前の列になり、元の列のデータを写せない）
            SchemaImportWarningKind.NameTrimmed => true,
            // 無効化された制約（図に無い）
            SchemaImportWarningKind.DisabledConstraintExcluded => true,
            SchemaImportWarningKind.DomainTypeFlattened => false,
            SchemaImportWarningKind.AliasTypeFlattened => false,
            SchemaImportWarningKind.TableNameCollision => false,
            SchemaImportWarningKind.ColumnTypeNotEmittable => false,
            SchemaImportWarningKind.PartitionDefinitionLost => false,
            SchemaImportWarningKind.VirtualTableExcluded => false,
            SchemaImportWarningKind.ComputedColumnExpressionLost => false,
            SchemaImportWarningKind.TemporalHistoryTableExcluded => false,
            SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted => false,
            SchemaImportWarningKind.ViewExcluded => false,
            SchemaImportWarningKind.MaterializedViewExcluded => false,
            SchemaImportWarningKind.ExternalTableExcluded => false,
            SchemaImportWarningKind.ChildPartitionExcluded => false,
            SchemaImportWarningKind.TemporaryTableExcluded => false,
            SchemaImportWarningKind.ColumnDefaultLost => false,
            SchemaImportWarningKind.AutoIncrementLost => false,
            SchemaImportWarningKind.ColumnCollationLost => false,
            SchemaImportWarningKind.InvisibleColumnLost => false,
            SchemaImportWarningKind.ColumnOnUpdateLost => false,
            SchemaImportWarningKind.SpatialReferenceIdLost => false,
            SchemaImportWarningKind.IdentityColumnMarked => false,
            SchemaImportWarningKind.DottedTableNameExcluded => false,
            SchemaImportWarningKind.UndeclaredColumnType => false,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>種別ごとに件数と名前の一覧へまとめて告げる種別か（段 2＝図に入らないオブジェクトを外したもの）</summary>
    /// <remarks>
    /// <c>false</c> の種別（段 1）は箇所ごとの 1 文を項目にする。どちらも表示は種別ごとに見出しの下へ並べる。
    /// 扱いの表の取込のマス（「外す＋まとめて告知」）と一致することは <c>SchemaImportWarningKindMapTests</c> が確かめる。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">定義されていない種別</exception>
    public static bool IsAggregated(SchemaImportWarningKind kind) =>
        kind switch
        {
            SchemaImportWarningKind.VirtualTableExcluded
            or SchemaImportWarningKind.TemporalHistoryTableExcluded
            or SchemaImportWarningKind.ViewExcluded
            or SchemaImportWarningKind.MaterializedViewExcluded
            or SchemaImportWarningKind.ExternalTableExcluded
            or SchemaImportWarningKind.ChildPartitionExcluded
            or SchemaImportWarningKind.TemporaryTableExcluded => true,
            _ when Enum.IsDefined(kind) => false,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}
