using System.IO;
using ClosedXML.Excel;
using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>
/// このアプリが出力したテーブル定義書 Excel を読み込み、ER 図モデルへ復元するサービスです。
/// </summary>
public static class TableDefinitionDocumentImporter
{
    private const string SummarySheetName = "テーブル一覧";
    private const string RelationshipSheetName = "リレーション一覧";
    private const int SummaryDataStartRow = 2;
    private const int RelationshipDataStartRow = 2;
    private const int DetailTableInfoRow = 2;
    private const int DetailColumnDataStartRow = 5;

    /// <summary>
    /// テーブル定義書ファイルを読み込み、<see cref="ErDiagram" /> として返します。
    /// </summary>
    /// <param name="path">読み込む Excel ファイルパスです。</param>
    /// <returns>復元した ER 図です。</returns>
    public static ErDiagram Load(string path)
    {
        using var workbook = new XLWorkbook(path);
        return Load(workbook);
    }

    /// <summary>
    /// 読み込み済みのブックから ER 図を復元します。
    /// </summary>
    /// <param name="workbook">対象ブックです。</param>
    /// <returns>復元した ER 図です。</returns>
    public static ErDiagram Load(XLWorkbook workbook)
    {
        var summarySheet = FindWorksheet(workbook, SummarySheetName) ?? throw new InvalidDataException($"'{SummarySheetName}' シートが見つかりません。");
        var relationshipSheet = FindWorksheet(workbook, RelationshipSheetName) ?? throw new InvalidDataException($"'{RelationshipSheetName}' シートが見つかりません。");

        var summaries = ReadSummarySheet(summarySheet);
        var entities = ReadDetailSheets(workbook, summaries);
        var relationships = ReadRelationshipSheet(relationshipSheet, entities);

        return new ErDiagram { Entities = entities.Values.ToList(), Relationships = relationships };
    }

    /// <summary>
    /// テーブル一覧シートからテーブル基本情報を取得します。
    /// </summary>
    private static Dictionary<string, TableSummaryRow> ReadSummarySheet(IXLWorksheet worksheet)
    {
        var summaries = new Dictionary<string, TableSummaryRow>(StringComparer.OrdinalIgnoreCase);

        for (var row = SummaryDataStartRow; ; row++)
        {
            var tableName = GetCellText(worksheet, row, 3);

            if (string.IsNullOrWhiteSpace(tableName))
            {
                break;
            }

            if (!summaries.TryAdd(tableName, new TableSummaryRow(tableName, GetCellText(worksheet, row, 4), GetCellText(worksheet, row, 5))))
            {
                throw new InvalidDataException($"テーブル一覧シートに重複したテーブル名 '{tableName}' があります。");
            }
        }

        if (summaries.Count == 0)
        {
            throw new InvalidDataException("テーブル一覧シートにテーブル情報がありません。");
        }

        return summaries;
    }

    /// <summary>
    /// 詳細シート群からエンティティを復元します。
    /// </summary>
    private static Dictionary<string, Entity> ReadDetailSheets(XLWorkbook workbook, IReadOnlyDictionary<string, TableSummaryRow> summaries)
    {
        var entities = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var detailSheets = workbook
            .Worksheets.Where(sheet =>
                !string.Equals(sheet.Name, SummarySheetName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sheet.Name, RelationshipSheetName, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        if (detailSheets.Count != summaries.Count)
        {
            throw new InvalidDataException("テーブル一覧シートと詳細シートの件数が一致しません。");
        }

        foreach (var sheet in detailSheets)
        {
            var tableName = GetCellText(sheet, DetailTableInfoRow, 2);

            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new InvalidDataException($"シート '{sheet.Name}' の B{DetailTableInfoRow} にテーブル名がありません。");
            }

            if (!summaries.TryGetValue(tableName, out var summary))
            {
                throw new InvalidDataException($"詳細シート '{sheet.Name}' のテーブル名 '{tableName}' がテーブル一覧シートに存在しません。");
            }

            if (entities.ContainsKey(tableName))
            {
                throw new InvalidDataException($"詳細シートに重複したテーブル名 '{tableName}' があります。");
            }

            var description = GetCellText(sheet, DetailTableInfoRow, 3);

            if (
                !string.IsNullOrWhiteSpace(summary.Description)
                && !string.IsNullOrWhiteSpace(description)
                && !string.Equals(summary.Description, description, StringComparison.Ordinal)
            )
            {
                throw new InvalidDataException($"テーブル '{tableName}' の説明がテーブル一覧と詳細シートで一致しません。");
            }

            var entity = new Entity
            {
                TableName = tableName,
                Description = string.IsNullOrWhiteSpace(description) ? summary.Description : description,
                Memo = summary.Memo,
            };

            ReadColumns(sheet, entity);
            entities.Add(tableName, entity);
        }

        foreach (var summary in summaries.Keys)
        {
            if (!entities.ContainsKey(summary))
            {
                throw new InvalidDataException($"テーブル '{summary}' の詳細シートが見つかりません。");
            }
        }

        return entities;
    }

    /// <summary>
    /// 詳細シートからカラム情報を復元します。
    /// </summary>
    private static void ReadColumns(IXLWorksheet worksheet, Entity entity)
    {
        for (var row = DetailColumnDataStartRow; ; row++)
        {
            var columnName = GetCellText(worksheet, row, 2);

            if (string.IsNullOrWhiteSpace(columnName))
            {
                break;
            }

            var dataType = GetCellText(worksheet, row, 4);

            if (string.IsNullOrWhiteSpace(dataType))
            {
                throw new InvalidDataException($"テーブル '{entity.TableName}' のカラム '{columnName}' にデータ型がありません。");
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
            throw new InvalidDataException($"テーブル '{entity.TableName}' にカラム定義がありません。");
        }
    }

    /// <summary>
    /// リレーション一覧シートからリレーションを復元します。
    /// </summary>
    private static List<Relationship> ReadRelationshipSheet(IXLWorksheet worksheet, IReadOnlyDictionary<string, Entity> entities)
    {
        var relationships = new List<Relationship>();
        var existingPairs = new HashSet<(string Parent, string Child)>(StringComparerOrdinalIgnoreCaseTupleComparer.Instance);

        for (var row = RelationshipDataStartRow; ; row++)
        {
            var childTableName = GetCellText(worksheet, row, 3);
            var parentTableName = GetCellText(worksheet, row, 5);

            if (string.IsNullOrWhiteSpace(childTableName) && string.IsNullOrWhiteSpace(parentTableName))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(childTableName) || string.IsNullOrWhiteSpace(parentTableName))
            {
                throw new InvalidDataException($"リレーション一覧シートの {row} 行目に参照元または参照先テーブル名がありません。");
            }

            if (!entities.TryGetValue(parentTableName, out var parent))
            {
                throw new InvalidDataException($"リレーション一覧シートの参照先テーブル '{parentTableName}' が存在しません。");
            }

            if (!entities.TryGetValue(childTableName, out var child))
            {
                throw new InvalidDataException($"リレーション一覧シートの参照元テーブル '{childTableName}' が存在しません。");
            }

            if (!existingPairs.Add((parent.TableName, child.TableName)))
            {
                throw new InvalidDataException($"テーブル '{parent.TableName}' と '{child.TableName}' のリレーションが重複しています。");
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

                if (string.IsNullOrWhiteSpace(childColumnName) || string.IsNullOrWhiteSpace(parentColumnName))
                {
                    throw new InvalidDataException($"リレーション一覧シートの {row} 行目に参照カラムがありません。");
                }

                var childColumn = child.Columns.FirstOrDefault(column => string.Equals(column.Name, childColumnName, StringComparison.OrdinalIgnoreCase));
                var parentColumn = parent.Columns.FirstOrDefault(column => string.Equals(column.Name, parentColumnName, StringComparison.OrdinalIgnoreCase));

                if (childColumn is null)
                {
                    throw new InvalidDataException($"テーブル '{child.TableName}' に参照元カラム '{childColumnName}' が存在しません。");
                }

                if (parentColumn is null)
                {
                    throw new InvalidDataException($"テーブル '{parent.TableName}' に参照先カラム '{parentColumnName}' が存在しません。");
                }

                childColumn.IsForeignKey = true;
                relationship.TargetColumnId = childColumn.Id;
                relationship.SourceColumnId = parentColumn.Id;
            }

            relationships.Add(relationship);
        }

        return relationships;
    }

    /// <summary>
    /// 定義書の関連種別表記を内部列挙へ変換します。
    /// </summary>
    private static RelationshipType ParseRelationshipType(string text, int row)
    {
        return text.Trim() switch
        {
            "1:1" => RelationshipType.OneToOne,
            "N:1" => RelationshipType.OneToMany,
            "N:N" => RelationshipType.ManyToMany,
            _ => throw new InvalidDataException($"リレーション一覧シートの {row} 行目の関係 '{text}' を解釈できません。"),
        };
    }

    /// <summary>
    /// セル文字列をトリムして取得します。
    /// </summary>
    private static string GetCellText(IXLWorksheet worksheet, int row, int column) => worksheet.Cell(row, column).GetString().Trim();

    /// <summary>
    /// 指定名のシートを検索します。
    /// </summary>
    private static IXLWorksheet? FindWorksheet(XLWorkbook workbook, string name)
    {
        return workbook.Worksheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 空白文字列を null に正規化します。
    /// </summary>
    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// テーブル一覧シート 1 行分の情報です。
    /// </summary>
    private sealed record TableSummaryRow(string TableName, string Description, string Memo);

    /// <summary>
    /// 大文字小文字を無視してタプル比較する comparer です。
    /// </summary>
    private sealed class StringComparerOrdinalIgnoreCaseTupleComparer : IEqualityComparer<(string Parent, string Child)>
    {
        public static StringComparerOrdinalIgnoreCaseTupleComparer Instance { get; } = new();

        public bool Equals((string Parent, string Child) x, (string Parent, string Child) y)
        {
            return string.Equals(x.Parent, y.Parent, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Child, y.Child, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Parent, string Child) obj)
        {
            return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Parent), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Child));
        }
    }
}
