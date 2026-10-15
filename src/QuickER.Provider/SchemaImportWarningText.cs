using QuickER.Provider.Resources;

namespace QuickER.Provider;

/// <summary>取込警告（<see cref="SchemaImportWarning"/>）を UI 言語の 1 行の文言にする</summary>
/// <remarks>
/// <para>
/// 文言はここに 1 つだけ置き、GUI（DB 取込の内訳）と CLI（<c>quicker scaffold</c> の stderr）が同じ文を出す。
/// 表示側は前後に自分の飾り（GUI の箇条書きの記号・CLI の「Warning:」）を付けるだけで、種別ごとの文言を持たない。
/// <see cref="SchemaImportWarningKind"/> で分岐する箇所が Provider の外に無いことは
/// <c>SchemaImportWarningTextTests</c> がソースを走査して確かめる。
/// </para>
/// <para>
/// <see cref="SchemaImportWarning.Subject"/> / <see cref="SchemaImportWarning.Detail"/> の意味は
/// <see cref="SchemaImportWarningKind"/> ごとに決まる（各種別の XmlDoc が正本）。
/// 名前は DB 由来の任意の文字列で改行を含み得るので、1 行 1 件を崩さないよう <see cref="SqlComment.Sanitize"/> で畳む。
/// </para>
/// </remarks>
public static class SchemaImportWarningText
{
    /// <summary>取込警告を UI 言語の 1 行にする</summary>
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
            SchemaImportWarningKind.VirtualTableExcluded => string.Format(
                Strings.ImportWarning_VirtualTableExcluded,
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
            SchemaImportWarningKind.TemporalHistoryTableExcluded => string.Format(
                Strings.ImportWarning_TemporalHistoryTableExcluded,
                table,
                detail
            ),
            SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted => string.Format(
                Strings.ImportWarning_PrimaryKeyNullabilityAdjusted,
                table,
                subject
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(warning),
                warning.Kind,
                "Unknown schema import warning kind."
            ),
        };
    }
}
