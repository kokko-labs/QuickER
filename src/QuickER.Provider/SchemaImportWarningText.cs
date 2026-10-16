using QuickER.Provider.Resources;

namespace QuickER.Provider;

/// <summary>取込警告（<see cref="SchemaImportWarning"/>）を、種別ごとの見出しと項目の一覧の文言にする</summary>
/// <remarks>
/// <para>
/// 文言はここに 1 つだけ置き、GUI（DB 取込の内訳）と CLI（<c>quicker scaffold</c> の stderr）が同じ文を出す。
/// 表示側は見出しと項目に自分の飾り（GUI の箇条書きの記号・CLI の「Warning:」と字下げ）を付けるだけで、
/// 種別ごとの文言を持たない。<see cref="SchemaImportWarningKind"/> で分岐する箇所が Provider の外に無いことは
/// <c>SchemaImportWarningTextTests</c> がソースを走査して確かめる。
/// </para>
/// <para>
/// 取込は箇所ごとに 1 件の警告を返し、表示は種別ごとに 1 項目へまとめてその中に箇所を並べる（数百件でも埋もれさせない）。
/// 図に入らないオブジェクトを外した種別（<see cref="SchemaImportWarningKinds.IsAggregated"/>）の項目は名前だけ、
/// それ以外の種別の項目は箇所ごとの 1 文にする。
/// </para>
/// <para>
/// <see cref="SchemaImportWarning.Subject"/> / <see cref="SchemaImportWarning.Detail"/> の意味は
/// <see cref="SchemaImportWarningKind"/> ごとに決まる（各種別の XmlDoc が正本）。
/// 名前は DB 由来の任意の文字列で改行を含み得るので、1 行 1 件を崩さないよう <see cref="SqlComment.Sanitize"/> で畳む。
/// </para>
/// </remarks>
public static class SchemaImportWarningText
{
    /// <summary>取込警告を、種別ごとの見出しと項目の一覧へまとめる</summary>
    /// <remarks>種別は警告に最初に現れた順、項目は警告の並びのまま。</remarks>
    /// <exception cref="ArgumentOutOfRangeException">定義されていない種別（黙って空行にしない）</exception>
    public static IReadOnlyList<SchemaImportWarningGroup> Group(
        IEnumerable<SchemaImportWarning> warnings
    )
    {
        ArgumentNullException.ThrowIfNull(warnings);

        return warnings
            .GroupBy(warning => warning.Kind)
            .Select(group =>
            {
                var items = group.Select(Describe).ToList();
                return new SchemaImportWarningGroup(
                    group.Key,
                    Heading(group.Key, items.Count),
                    items
                );
            })
            .ToList();
    }

    /// <summary>種別の見出し（件数入り）</summary>
    /// <exception cref="ArgumentOutOfRangeException">定義されていない種別</exception>
    public static string Heading(SchemaImportWarningKind kind, int count) =>
        string.Format(
            kind switch
            {
                SchemaImportWarningKind.DomainTypeFlattened =>
                    Strings.ImportWarningHeading_DomainTypeFlattened,
                SchemaImportWarningKind.TableColumnsUnavailable =>
                    Strings.ImportWarningHeading_TableColumnsUnavailable,
                SchemaImportWarningKind.ForeignKeyOutsideScope =>
                    Strings.ImportWarningHeading_ForeignKeyOutsideScope,
                SchemaImportWarningKind.TableNameCollision =>
                    Strings.ImportWarningHeading_TableNameCollision,
                SchemaImportWarningKind.ColumnNameCollision =>
                    Strings.ImportWarningHeading_ColumnNameCollision,
                SchemaImportWarningKind.ColumnTypeNotEmittable =>
                    Strings.ImportWarningHeading_ColumnTypeNotEmittable,
                SchemaImportWarningKind.PartitionDefinitionLost =>
                    Strings.ImportWarningHeading_PartitionDefinitionLost,
                SchemaImportWarningKind.VirtualTableExcluded =>
                    Strings.ImportWarningHeading_VirtualTableExcluded,
                SchemaImportWarningKind.ComputedColumnExpressionLost =>
                    Strings.ImportWarningHeading_ComputedColumnExpressionLost,
                SchemaImportWarningKind.DisabledConstraintExcluded =>
                    Strings.ImportWarningHeading_DisabledConstraintExcluded,
                SchemaImportWarningKind.TemporalHistoryTableExcluded =>
                    Strings.ImportWarningHeading_TemporalHistoryTableExcluded,
                SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted =>
                    Strings.ImportWarningHeading_PrimaryKeyNullabilityAdjusted,
                SchemaImportWarningKind.ViewExcluded => Strings.ImportWarningHeading_ViewExcluded,
                SchemaImportWarningKind.MaterializedViewExcluded =>
                    Strings.ImportWarningHeading_MaterializedViewExcluded,
                SchemaImportWarningKind.ExternalTableExcluded =>
                    Strings.ImportWarningHeading_ExternalTableExcluded,
                SchemaImportWarningKind.ChildPartitionExcluded =>
                    Strings.ImportWarningHeading_ChildPartitionExcluded,
                SchemaImportWarningKind.TemporaryTableExcluded =>
                    Strings.ImportWarningHeading_TemporaryTableExcluded,
                SchemaImportWarningKind.ForeignKeyToExcludedTable =>
                    Strings.ImportWarningHeading_ForeignKeyToExcludedTable,
                SchemaImportWarningKind.ColumnDefaultLost =>
                    Strings.ImportWarningHeading_ColumnDefaultLost,
                SchemaImportWarningKind.AutoIncrementLost =>
                    Strings.ImportWarningHeading_AutoIncrementLost,
                SchemaImportWarningKind.ColumnCollationLost =>
                    Strings.ImportWarningHeading_ColumnCollationLost,
                SchemaImportWarningKind.InvisibleColumnLost =>
                    Strings.ImportWarningHeading_InvisibleColumnLost,
                SchemaImportWarningKind.ColumnOnUpdateLost =>
                    Strings.ImportWarningHeading_ColumnOnUpdateLost,
                SchemaImportWarningKind.SpatialReferenceIdLost =>
                    Strings.ImportWarningHeading_SpatialReferenceIdLost,
                SchemaImportWarningKind.AliasTypeFlattened =>
                    Strings.ImportWarningHeading_AliasTypeFlattened,
                SchemaImportWarningKind.NameTrimmed => Strings.ImportWarningHeading_NameTrimmed,
                SchemaImportWarningKind.IdentityColumnMarked =>
                    Strings.ImportWarningHeading_IdentityColumnMarked,
                SchemaImportWarningKind.DottedTableNameExcluded =>
                    Strings.ImportWarningHeading_DottedTableNameExcluded,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Unknown schema import warning kind."
                ),
            },
            count
        );

    /// <summary>取込警告 1 件を、見出しの下に並べる 1 行の項目にする</summary>
    /// <exception cref="ArgumentOutOfRangeException">定義されていない種別（黙って空行にしない）</exception>
    public static string Describe(SchemaImportWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        var table = SqlComment.Sanitize(warning.TableName);
        var subject = SqlComment.Sanitize(warning.Subject);
        var detail = SqlComment.Sanitize(warning.Detail);

        return warning.Kind switch
        {
            SchemaImportWarningKind.DomainTypeFlattened => string.Format(
                Strings.ImportWarning_DomainTypeFlattened,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.TableColumnsUnavailable => string.Format(
                Strings.ImportWarning_TableColumnsUnavailable,
                table
            ),
            SchemaImportWarningKind.ForeignKeyOutsideScope => string.Format(
                Strings.ImportWarning_ForeignKeyOutsideScope,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.TableNameCollision => string.Format(
                Strings.ImportWarning_TableNameCollision,
                subject,
                detail
            ),
            SchemaImportWarningKind.ColumnNameCollision => string.Format(
                Strings.ImportWarning_ColumnNameCollision,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.ColumnTypeNotEmittable => string.Format(
                Strings.ImportWarning_ColumnTypeNotEmittable,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.PartitionDefinitionLost => string.Format(
                Strings.ImportWarning_PartitionDefinitionLost,
                table
            ),
            // 式は取れない方言（SQLite）があるため、載せられるときだけ括弧書きの文面へ振り分ける
            SchemaImportWarningKind.ComputedColumnExpressionLost => detail.Length == 0
                ? string.Format(Strings.ImportWarning_ComputedColumnExpressionLost, table, subject)
                : string.Format(
                    Strings.ImportWarning_ComputedColumnExpressionLostWithExpression,
                    table,
                    subject,
                    detail
                ),
            SchemaImportWarningKind.DisabledConstraintExcluded => string.Format(
                Strings.ImportWarning_DisabledConstraintExcluded,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted => string.Format(
                Strings.ImportWarning_PrimaryKeyNullabilityAdjusted,
                table,
                subject
            ),
            SchemaImportWarningKind.ForeignKeyToExcludedTable => string.Format(
                Strings.ImportWarning_ForeignKeyToExcludedTable,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.ColumnDefaultLost => string.Format(
                Strings.ImportWarning_ColumnDefaultLost,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.AutoIncrementLost => string.Format(
                Strings.ImportWarning_AutoIncrementLost,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.ColumnCollationLost => string.Format(
                Strings.ImportWarning_ColumnCollationLost,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.InvisibleColumnLost => string.Format(
                Strings.ImportWarning_InvisibleColumnLost,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.ColumnOnUpdateLost => string.Format(
                Strings.ImportWarning_ColumnOnUpdateLost,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.SpatialReferenceIdLost => string.Format(
                Strings.ImportWarning_SpatialReferenceIdLost,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.AliasTypeFlattened => string.Format(
                Strings.ImportWarning_AliasTypeFlattened,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.NameTrimmed => string.Format(
                Strings.ImportWarning_NameTrimmed,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.IdentityColumnMarked => string.Format(
                Strings.ImportWarning_IdentityColumnMarked,
                table,
                subject,
                detail
            ),
            // スキーマを図に入れない方言はスキーマ名が空なので、空のスキーマ名を書かない文面へ振り分ける
            SchemaImportWarningKind.DottedTableNameExcluded => subject.Length == 0
                ? string.Format(Strings.ImportWarning_DottedTableNameExcluded, table)
                : string.Format(
                    Strings.ImportWarning_DottedTableNameExcludedInSchema,
                    table,
                    subject
                ),
            // 図に入らないオブジェクトを外した種別は、見出しが内容を言うので項目は名前だけ
            SchemaImportWarningKind.TemporalHistoryTableExcluded => string.Format(
                Strings.ImportWarningItem_TemporalHistoryTableExcluded,
                table,
                detail
            ),
            SchemaImportWarningKind.ChildPartitionExcluded => string.Format(
                Strings.ImportWarningItem_ChildPartitionExcluded,
                table,
                detail
            ),
            SchemaImportWarningKind.VirtualTableExcluded
            or SchemaImportWarningKind.ViewExcluded
            or SchemaImportWarningKind.MaterializedViewExcluded
            or SchemaImportWarningKind.ExternalTableExcluded
            or SchemaImportWarningKind.TemporaryTableExcluded => table,
            _ => throw new ArgumentOutOfRangeException(
                nameof(warning),
                warning.Kind,
                "Unknown schema import warning kind."
            ),
        };
    }
}

/// <summary>取込警告の種別ごとのまとまり（見出しと、見出しの下に並べる項目）</summary>
/// <param name="Kind">種別</param>
/// <param name="Heading">見出し（件数入り）</param>
/// <param name="Items">項目（箇所ごとに 1 行）</param>
public sealed record SchemaImportWarningGroup(
    SchemaImportWarningKind Kind,
    string Heading,
    IReadOnlyList<string> Items
);
