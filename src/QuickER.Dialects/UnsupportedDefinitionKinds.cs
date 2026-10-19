using System.Collections.ObjectModel;

namespace QuickER.Dialects;

/// <summary>種類ごとの ID・向き・図の印（扱いの表の表 1 の写し）</summary>
/// <remarks>
/// 既定値を持たない（全種類を明示して書き、網羅テストが欠けを赤にする）。図の印を持つのは A1（計算列）と
/// A5a（指定した値を拒む自動採番）だけ。
/// </remarks>
public static class UnsupportedDefinitionKinds
{
    /// <summary>全種類の ID・向き・図の印</summary>
    public static IReadOnlyDictionary<UnsupportedDefinitionKind, UnsupportedKindInfo> All { get; } =
        new ReadOnlyDictionary<UnsupportedDefinitionKind, UnsupportedKindInfo>(Build());

    /// <summary>種類の ID・向き・図の印を引く</summary>
    /// <param name="kind">種類</param>
    /// <exception cref="ArgumentOutOfRangeException">定義されていない種類</exception>
    public static UnsupportedKindInfo Info(UnsupportedDefinitionKind kind) =>
        All.TryGetValue(kind, out var info)
            ? info
            : throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown unsupported definition kind."
            );

    // 向き 1（DB にあるが図が表せない）・印なし
    private static UnsupportedKindInfo InDatabase(string id) =>
        new(id, UnsupportedDirection.InDatabaseOnly, HasDiagramMark: false);

    // 向き 1・図に印を持つ（A1・A5a）
    private static UnsupportedKindInfo InDatabaseWithMark(string id) =>
        new(id, UnsupportedDirection.InDatabaseOnly, HasDiagramMark: true);

    // 向き 2（図にあるが方言・機能が表せない）
    private static UnsupportedKindInfo InDiagram(string id) =>
        new(id, UnsupportedDirection.InDiagramOnly, HasDiagramMark: false);

    private static Dictionary<UnsupportedDefinitionKind, UnsupportedKindInfo> Build() =>
        new()
        {
            // 向き 1
            { UnsupportedDefinitionKind.ComputedColumn, InDatabaseWithMark("A1") },
            { UnsupportedDefinitionKind.DefaultValue, InDatabase("A2") },
            { UnsupportedDefinitionKind.CheckConstraint, InDatabase("A3") },
            { UnsupportedDefinitionKind.Collation, InDatabase("A4") },
            { UnsupportedDefinitionKind.IdentityRejectingValues, InDatabaseWithMark("A5a") },
            { UnsupportedDefinitionKind.IdentityAcceptingValues, InDatabase("A5b") },
            { UnsupportedDefinitionKind.InvisibleColumn, InDatabase("A6a") },
            { UnsupportedDefinitionKind.ColumnOnUpdate, InDatabase("A6b") },
            { UnsupportedDefinitionKind.SpatialReferenceId, InDatabase("A6c") },
            { UnsupportedDefinitionKind.PrimaryKeyNullabilityAdjusted, InDatabase("A7") },
            { UnsupportedDefinitionKind.NonUniqueIndex, InDatabase("B1") },
            { UnsupportedDefinitionKind.NonConstraintUniqueIndex, InDatabase("B2") },
            { UnsupportedDefinitionKind.Trigger, InDatabase("B3") },
            { UnsupportedDefinitionKind.DisabledConstraint, InDatabase("B4a") },
            { UnsupportedDefinitionKind.UnvalidatedConstraint, InDatabase("B4b") },
            { UnsupportedDefinitionKind.ConstraintClause, InDatabase("B5") },
            { UnsupportedDefinitionKind.View, InDatabase("C1") },
            { UnsupportedDefinitionKind.MaterializedView, InDatabase("C2a") },
            { UnsupportedDefinitionKind.MaterializedViewInternals, InDatabase("C2b") },
            { UnsupportedDefinitionKind.VirtualTable, InDatabase("C3a") },
            { UnsupportedDefinitionKind.ShadowTable, InDatabase("C3b") },
            { UnsupportedDefinitionKind.ExternalTable, InDatabase("C3c") },
            { UnsupportedDefinitionKind.PartitionedTable, InDatabase("C4a") },
            { UnsupportedDefinitionKind.ChildPartition, InDatabase("C4b") },
            { UnsupportedDefinitionKind.TemporalHistoryTable, InDatabase("C5") },
            { UnsupportedDefinitionKind.TableOption, InDatabase("C6") },
            { UnsupportedDefinitionKind.TemporaryTable, InDatabase("C7a") },
            { UnsupportedDefinitionKind.ExtensionOwnedTable, InDatabase("C7b") },
            { UnsupportedDefinitionKind.SystemTable, InDatabase("C7c") },
            { UnsupportedDefinitionKind.ForeignKeyToExcludedTable, InDatabase("C9") },
            { UnsupportedDefinitionKind.FlattenedType, InDatabase("D1a") },
            { UnsupportedDefinitionKind.UserDefinedType, InDatabase("D1b") },
            { UnsupportedDefinitionKind.UnparsableType, InDatabase("D2a") },
            { UnsupportedDefinitionKind.UnemittableTypeNotation, InDatabase("D2b") },
            { UnsupportedDefinitionKind.UndeclaredColumnType, InDatabase("D2c") },
            { UnsupportedDefinitionKind.PaddedName, InDatabase("N1") },
            { UnsupportedDefinitionKind.TableWithoutReadableColumns, InDatabase("T1") },
            // 向き 2
            { UnsupportedDefinitionKind.ComputedColumnTypeChange, InDiagram("A1b") },
            { UnsupportedDefinitionKind.UniqueConstraintOnPrimaryKey, InDiagram("B6") },
            { UnsupportedDefinitionKind.CompositeOrMissingPrimaryKey, InDiagram("B7a") },
            { UnsupportedDefinitionKind.CompositeForeignKey, InDiagram("B7b") },
            { UnsupportedDefinitionKind.ManyToManyRelationship, InDiagram("B7c") },
            { UnsupportedDefinitionKind.NotEditableColumn, InDiagram("D3") },
            { UnsupportedDefinitionKind.UnrepresentableType, InDiagram("D5") },
            { UnsupportedDefinitionKind.OnUpdateReferentialAction, InDiagram("E1") },
            { UnsupportedDefinitionKind.SetDefaultReferentialAction, InDiagram("E2") },
            { UnsupportedDefinitionKind.Description, InDiagram("E5") },
            { UnsupportedDefinitionKind.SchemaQualifiedTableName, InDiagram("E6") },
            { UnsupportedDefinitionKind.ColumnOrder, InDiagram("X1") },
            { UnsupportedDefinitionKind.ForeignKeyConstraintName, InDiagram("X2") },
            { UnsupportedDefinitionKind.CompositeForeignKeyColumnOrder, InDiagram("X3") },
            { UnsupportedDefinitionKind.ForeignKeyTypeMismatch, InDiagram("X4") },
            { UnsupportedDefinitionKind.ForeignKeyNotOnCandidateKey, InDiagram("X5") },
        };
}
