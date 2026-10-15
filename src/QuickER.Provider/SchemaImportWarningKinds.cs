using QuickER.Dialects;

namespace QuickER.Provider;

/// <summary>取込警告の種別（<see cref="SchemaImportWarningKind"/>）と、図で表せない定義の種類の対応</summary>
/// <remarks>
/// <para>
/// 取込警告の文言化の網羅は警告の種別の単位、方言の宣言の網羅は種類の単位で行うので、両者の対応をここに 1 つだけ置く。
/// 取込範囲の外を参照する外部キーと名前の衝突は、図で表せない定義の種類に入れない（種類の外＝<c>null</c>）。
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
