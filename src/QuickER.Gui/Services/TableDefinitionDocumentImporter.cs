using System.IO;
using ClosedXML.Excel;
using QuickER.Model;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>本アプリが出力したテーブル定義書 Excel を読み込み ER 図モデルへ復元するサービス</summary>
/// <remarks>
/// <see cref="TableDefinitionDocumentExporter"/> が刻む非表示の定義名タグで役割シートを特定するため、
/// シート名がローカライズされていても（ユーザーがリネームしても）取り込める。
/// 行位置は <see cref="TableDefinitionDocumentLayout"/> をエクスポータと共有する。
/// </remarks>
public static class TableDefinitionDocumentImporter
{
    /// <summary>テーブル定義書ファイルを読み込み <see cref="ErDiagram" /> として返す</summary>
    /// <param name="path">読み込む Excel ファイルパス</param>
    /// <returns>復元した ER 図</returns>
    public static ErDiagram Load(string path)
    {
        using var workbook = new XLWorkbook(path);
        return Load(workbook);
    }

    /// <summary>読み込み済みのブックから ER 図を復元する</summary>
    /// <param name="workbook">対象ブック</param>
    /// <returns>復元した ER 図</returns>
    /// <exception cref="InvalidDataException">必須シートの欠落や整合性不一致を検出した場合にスローする</exception>
    public static ErDiagram Load(XLWorkbook workbook)
    {
        // テーブル一覧・リレーション一覧は必須（タグ欠落＝旧形式または他アプリ出力）
        var summarySheet =
            ResolveRoleSheet(workbook, TableDefinitionDocumentLayout.SummaryDefinedName)
            ?? throw new InvalidDataException(Strings.TableDoc_MissingRoleTag);
        var relationshipSheet =
            ResolveRoleSheet(workbook, TableDefinitionDocumentLayout.RelationshipsDefinedName)
            ?? throw new InvalidDataException(Strings.TableDoc_MissingRoleTag);

        // 一覧 2 枚以外を詳細シートとみなす（参照比較）
        var roleSheets = new HashSet<IXLWorksheet> { summarySheet, relationshipSheet };

        var summaries = ReadSummarySheet(summarySheet);
        var entities = ReadDetailSheets(workbook, summaries, roleSheets);
        var relationships = ReadRelationshipSheet(relationshipSheet, entities);

        return new ErDiagram { Entities = entities.Values.ToList(), Relationships = relationships };
    }

    /// <summary>テーブル一覧シートからテーブル名・説明・備考を取得する</summary>
    private static Dictionary<string, TableSummaryRow> ReadSummarySheet(IXLWorksheet worksheet)
    {
        var summaries = new Dictionary<string, TableSummaryRow>(StringComparer.OrdinalIgnoreCase);

        for (var row = TableDefinitionDocumentLayout.SummaryDataStartRow; ; row++)
        {
            var tableName = GetCellText(
                worksheet,
                row,
                TableDefinitionDocumentLayout.SummaryTableNameColumn
            );

            if (string.IsNullOrWhiteSpace(tableName))
            {
                break;
            }

            if (
                !summaries.TryAdd(
                    tableName,
                    new TableSummaryRow(
                        tableName,
                        GetCellText(
                            worksheet,
                            row,
                            TableDefinitionDocumentLayout.SummaryDescriptionColumn
                        ),
                        GetCellText(worksheet, row, TableDefinitionDocumentLayout.SummaryMemoColumn)
                    )
                )
            )
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_SummaryDuplicateTable, tableName)
                );
            }
        }

        if (summaries.Count == 0)
        {
            throw new InvalidDataException(Strings.TableDoc_SummaryNoTables);
        }

        return summaries;
    }

    /// <summary>詳細シート群からエンティティを復元する（一覧との件数の整合性を検証する）</summary>
    /// <remarks>
    /// テーブル名は詳細シートの A1（タイトルセル）から読む（シート名は 31 文字切詰め・
    /// 重複回避が入るため使わない）。説明・備考はテーブル一覧シートからのみ復元する。
    /// </remarks>
    private static Dictionary<string, Entity> ReadDetailSheets(
        XLWorkbook workbook,
        IReadOnlyDictionary<string, TableSummaryRow> summaries,
        IReadOnlySet<IXLWorksheet> roleSheets
    )
    {
        var entities = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        // 役割シート（一覧 2 枚）以外を詳細シートとみなす（参照比較）
        var detailSheets = workbook.Worksheets.Where(sheet => !roleSheets.Contains(sheet)).ToList();

        if (detailSheets.Count != summaries.Count)
        {
            throw new InvalidDataException(Strings.TableDoc_CountMismatch);
        }

        foreach (var sheet in detailSheets)
        {
            var tableName = GetCellText(sheet, TableDefinitionDocumentLayout.DetailTitleRow, 1);

            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new InvalidDataException(
                    string.Format(
                        Strings.TableDoc_DetailMissingTableName,
                        sheet.Name,
                        TableDefinitionDocumentLayout.DetailTitleRow
                    )
                );
            }

            if (!summaries.TryGetValue(tableName, out var summary))
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_DetailTableNotInSummary, sheet.Name, tableName)
                );
            }

            if (entities.ContainsKey(tableName))
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_DetailDuplicateTable, tableName)
                );
            }

            var entity = new Entity
            {
                TableName = tableName,
                Description = summary.Description,
                Memo = summary.Memo,
            };

            ReadColumns(sheet, entity);
            entities.Add(tableName, entity);
        }

        foreach (var summary in summaries.Keys)
        {
            if (!entities.ContainsKey(summary))
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_DetailSheetNotFound, summary)
                );
            }
        }

        return entities;
    }

    /// <summary>詳細シートのカラム行を読み取り、キー表記から PK / FK を復元する</summary>
    private static void ReadColumns(IXLWorksheet worksheet, Entity entity)
    {
        for (var row = TableDefinitionDocumentLayout.DetailColumnDataStartRow; ; row++)
        {
            var columnName = GetCellText(worksheet, row, 2);

            if (string.IsNullOrWhiteSpace(columnName))
            {
                break;
            }

            var dataType = GetCellText(worksheet, row, 4);

            if (string.IsNullOrWhiteSpace(dataType))
            {
                throw new InvalidDataException(
                    string.Format(
                        Strings.TableDoc_ColumnMissingDataType,
                        entity.TableName,
                        columnName
                    )
                );
            }

            var keyText = GetCellText(worksheet, row, 6);
            entity.Columns.Add(
                new Column
                {
                    Name = columnName,
                    Description = GetCellText(worksheet, row, 3),
                    DataType = dataType,
                    IsNullable = string.IsNullOrWhiteSpace(GetCellText(worksheet, row, 5)),
                    IsPrimaryKey = keyText.Contains("PK", StringComparison.OrdinalIgnoreCase),
                    IsForeignKey = keyText.Contains("FK", StringComparison.OrdinalIgnoreCase),
                }
            );
        }

        if (entity.Columns.Count == 0)
        {
            throw new InvalidDataException(
                string.Format(Strings.TableDoc_TableNoColumns, entity.TableName)
            );
        }
    }

    /// <summary>リレーション一覧シートからリレーションを復元し、参照列の外部キー化を行う</summary>
    /// <remarks>参照元（FK 側）は子テーブル、参照先（PK 側）は親テーブルに対応する</remarks>
    private static List<Relationship> ReadRelationshipSheet(
        IXLWorksheet worksheet,
        IReadOnlyDictionary<string, Entity> entities
    )
    {
        var relationships = new List<Relationship>();
        var existingPairs = new HashSet<(string Parent, string Child)>(
            StringComparerOrdinalIgnoreCaseTupleComparer.Instance
        );

        for (var row = TableDefinitionDocumentLayout.RelationshipDataStartRow; ; row++)
        {
            var childTableName = GetCellText(worksheet, row, 3);
            var parentTableName = GetCellText(worksheet, row, 5);

            if (
                string.IsNullOrWhiteSpace(childTableName)
                && string.IsNullOrWhiteSpace(parentTableName)
            )
            {
                break;
            }

            if (
                string.IsNullOrWhiteSpace(childTableName)
                || string.IsNullOrWhiteSpace(parentTableName)
            )
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_RelMissingTableName, row)
                );
            }

            if (!entities.TryGetValue(parentTableName, out var parent))
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_RelParentNotFound, parentTableName)
                );
            }

            if (!entities.TryGetValue(childTableName, out var child))
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_RelChildNotFound, childTableName)
                );
            }

            if (!existingPairs.Add((parent.TableName, child.TableName)))
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_RelDuplicate, parent.TableName, child.TableName)
                );
            }

            var type = ParseRelationshipType(GetCellText(worksheet, row, 7), row);
            var relationship = new Relationship
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = type,
                ConstraintName = NullIfWhiteSpace(GetCellText(worksheet, row, 2)),
                OnDelete = ForeignKeyReferentialActionHelper.Parse(GetCellText(worksheet, row, 8)),
                OnUpdate = ForeignKeyReferentialActionHelper.Parse(GetCellText(worksheet, row, 9)),
            };

            if (type != RelationshipType.ManyToMany)
            {
                var childColumnName = GetCellText(worksheet, row, 4);
                var parentColumnName = GetCellText(worksheet, row, 6);

                if (
                    string.IsNullOrWhiteSpace(childColumnName)
                    || string.IsNullOrWhiteSpace(parentColumnName)
                )
                {
                    throw new InvalidDataException(
                        string.Format(Strings.TableDoc_RelMissingColumn, row)
                    );
                }

                var childColumn = child.Columns.FirstOrDefault(column =>
                    string.Equals(column.Name, childColumnName, StringComparison.OrdinalIgnoreCase)
                );
                var parentColumn = parent.Columns.FirstOrDefault(column =>
                    string.Equals(column.Name, parentColumnName, StringComparison.OrdinalIgnoreCase)
                );

                if (childColumn is null)
                {
                    throw new InvalidDataException(
                        string.Format(
                            Strings.TableDoc_RelChildColumnNotFound,
                            child.TableName,
                            childColumnName
                        )
                    );
                }

                if (parentColumn is null)
                {
                    throw new InvalidDataException(
                        string.Format(
                            Strings.TableDoc_RelParentColumnNotFound,
                            parent.TableName,
                            parentColumnName
                        )
                    );
                }

                childColumn.IsForeignKey = true;
                relationship.TargetColumnId = childColumn.Id;
                relationship.SourceColumnId = parentColumn.Id;
            }

            relationships.Add(relationship);
        }

        return relationships;
    }

    /// <summary>定義書の関係表記（1:1 / N:1 / N:N）を内部の列挙値へ変換する</summary>
    private static RelationshipType ParseRelationshipType(string text, int row)
    {
        return text.Trim() switch
        {
            "1:1" => RelationshipType.OneToOne,
            "N:1" => RelationshipType.OneToMany,
            "N:N" => RelationshipType.ManyToMany,
            _ => throw new InvalidDataException(
                string.Format(Strings.TableDoc_RelUnknownRelation, row, text)
            ),
        };
    }

    /// <summary>セル文字列を前後トリムして取得する</summary>
    private static string GetCellText(IXLWorksheet worksheet, int row, int column) =>
        worksheet.Cell(row, column).GetString().Trim();

    /// <summary>役割タグ（非表示の定義名）が指すシートを解決する（未定義・無効参照は null）</summary>
    private static IXLWorksheet? ResolveRoleSheet(XLWorkbook workbook, string definedName)
    {
        if (!workbook.DefinedNames.TryGetValue(definedName, out var defined))
        {
            return null;
        }

        // 参照先シートが削除で無効化された場合は解決不能扱いとする
        if (!defined.IsValid)
        {
            return null;
        }

        try
        {
            return defined.Ranges.FirstOrDefault()?.Worksheet;
        }
        catch (Exception)
        {
            // 無効参照（#REF!）評価中の例外も解決不能として扱う
            return null;
        }
    }

    /// <summary>空白文字列を null へ正規化する</summary>
    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>テーブル一覧シート 1 行分の情報</summary>
    private sealed record TableSummaryRow(string TableName, string Description, string Memo);

    /// <summary>親子テーブル名の組を大文字小文字無視で比較する比較器（重複検出に用いる）</summary>
    private sealed class StringComparerOrdinalIgnoreCaseTupleComparer
        : IEqualityComparer<(string Parent, string Child)>
    {
        /// <summary>共有インスタンス</summary>
        public static StringComparerOrdinalIgnoreCaseTupleComparer Instance { get; } = new();

        /// <inheritdoc />
        public bool Equals((string Parent, string Child) x, (string Parent, string Child) y)
        {
            return string.Equals(x.Parent, y.Parent, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Child, y.Child, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public int GetHashCode((string Parent, string Child) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Parent),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Child)
            );
        }
    }
}
