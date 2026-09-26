using System.IO;
using System.Text.RegularExpressions;
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
public static partial class TableDefinitionDocumentImporter
{
    /// <summary>キー表記中の一意制約ラベル（<c>UQ1</c> など）に一致する正規表現</summary>
    private static readonly Regex UniqueConstraintLabelRegex = UniqueConstraintKeyLabelRegex();

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
        var diagram = new ErDiagram
        {
            Entities = entities.Values.ToList(),
            Relationships = relationships,
        };

        // 対象 DBMS は言語非依存のカスタムプロパティから復元する（欠落・空値は既定値のまま＝寛容仕様）
        var targetDbms = workbook
            .CustomProperties.FirstOrDefault(property =>
                string.Equals(
                    property.Name,
                    TableDefinitionDocumentLayout.TargetDbmsPropertyName,
                    StringComparison.Ordinal
                )
            )
            ?.Value?.ToString();

        if (!string.IsNullOrWhiteSpace(targetDbms))
        {
            diagram.TargetDbms = targetDbms.Trim();
        }

        return diagram;
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

    /// <summary>詳細シートのカラム行を読み取り、キー表記から PK / FK / 一意制約を復元する</summary>
    /// <remarks>
    /// キー表記の <c>UQ{n}</c> は同じ番号が同じ制約を表すため、番号ごとに構成列をまとめて 1 つの
    /// <see cref="UniqueConstraint"/> へ復元する（制約名は定義書に載らないため未設定＝合成名になる）
    /// </remarks>
    private static void ReadColumns(IXLWorksheet worksheet, Entity entity)
    {
        // 一意制約の番号 → 構成列 ID（列の出現順＝宣言順）
        var uniqueConstraintColumns = new SortedDictionary<int, List<Guid>>();

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
            var column = new Column
            {
                Name = columnName,
                Description = GetCellText(worksheet, row, 3),
                DataType = dataType,
                IsNullable = string.IsNullOrWhiteSpace(GetCellText(worksheet, row, 5)),
                IsPrimaryKey = keyText.Contains("PK", StringComparison.OrdinalIgnoreCase),
                IsForeignKey = keyText.Contains("FK", StringComparison.OrdinalIgnoreCase),
            };
            entity.Columns.Add(column);

            foreach (var number in ParseUniqueConstraintNumbers(keyText))
            {
                if (!uniqueConstraintColumns.TryGetValue(number, out var columnIds))
                {
                    columnIds = [];
                    uniqueConstraintColumns[number] = columnIds;
                }

                columnIds.Add(column.Id);
            }
        }

        if (entity.Columns.Count == 0)
        {
            throw new InvalidDataException(
                string.Format(Strings.TableDoc_TableNoColumns, entity.TableName)
            );
        }

        // 番号順に制約を復元する（出力時の連番＝登場順のため、往復で制約の並びが保たれる）
        foreach (var columnIds in uniqueConstraintColumns.Values)
        {
            entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = columnIds });
        }
    }

    /// <summary>キー表記から一意制約の番号（<c>UQ{n}</c> の n）を抽出する</summary>
    /// <remarks>
    /// 1 列が複数の制約に参加する場合はカンマ連結（例: <c>UQ1,UQ2</c>）で、他のキー表記とは
    /// <c>/</c> 区切り（例: <c>PK/UQ1</c>）で並ぶ。番号なしの <c>UQ</c> は制約を特定できないため無視する
    /// </remarks>
    private static IEnumerable<int> ParseUniqueConstraintNumbers(string keyText)
    {
        foreach (Match match in UniqueConstraintLabelRegex.Matches(keyText))
        {
            if (int.TryParse(match.Groups["number"].Value, out var number))
            {
                yield return number;
            }
        }
    }

    /// <summary>リレーション一覧シートからリレーションを復元し、参照列の外部キー化を行う</summary>
    /// <remarks>
    /// 参照元（FK 側）は子テーブル、参照先（PK 側）は親テーブルに対応する。
    /// 重複の判定はテーブルの組だけでなく<b>構成列まで</b>含める——同じ親子の間に複数の外部キー
    /// （配送先・請求先など）を持つ図を定義書は 1 行 1 リレーションで書き出せるため、
    /// テーブルの組だけで弾くとその定義書を読み戻せない。
    /// </remarks>
    private static List<Relationship> ReadRelationshipSheet(
        IXLWorksheet worksheet,
        IReadOnlyDictionary<string, Entity> entities
    )
    {
        var relationships = new List<Relationship>();
        var existingSignatures = new HashSet<RelationshipSignature>(
            RelationshipSignatureComparer.Instance
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

            // 多対多は列対応を持たない（ジャンクションテーブル前提）ため構成列は空のまま＝
            // 署名はテーブルの組だけになり、同一ペアの 2 行目は従来どおり重複として弾かれる
            var childColumnNames = new List<string>();
            var parentColumnNames = new List<string>();

            if (type != RelationshipType.ManyToMany)
            {
                // 複合外部キーはカンマ区切りの複数列表記（単一列は列名 1 つ）
                childColumnNames = SplitColumnNames(GetCellText(worksheet, row, 4));
                parentColumnNames = SplitColumnNames(GetCellText(worksheet, row, 6));

                if (childColumnNames.Count == 0 || parentColumnNames.Count == 0)
                {
                    throw new InvalidDataException(
                        string.Format(Strings.TableDoc_RelMissingColumn, row)
                    );
                }

                if (childColumnNames.Count != parentColumnNames.Count)
                {
                    throw new InvalidDataException(
                        string.Format(Strings.TableDoc_RelColumnCountMismatch, row)
                    );
                }
            }

            // 構成列まで含めた署名で重複を判定する（列名の解決より前＝行の書かれ方だけで決まる）
            if (
                !existingSignatures.Add(
                    new RelationshipSignature(
                        parent.TableName,
                        child.TableName,
                        string.Join(',', parentColumnNames),
                        string.Join(',', childColumnNames)
                    )
                )
            )
            {
                throw new InvalidDataException(
                    string.Format(Strings.TableDoc_RelDuplicate, parent.TableName, child.TableName)
                );
            }

            if (type != RelationshipType.ManyToMany)
            {
                var pairs = new List<RelationshipColumnPair>();

                for (var i = 0; i < childColumnNames.Count; i++)
                {
                    var childColumn = child.Columns.FirstOrDefault(column =>
                        string.Equals(
                            column.Name,
                            childColumnNames[i],
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                    var parentColumn = parent.Columns.FirstOrDefault(column =>
                        string.Equals(
                            column.Name,
                            parentColumnNames[i],
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                    if (childColumn is null)
                    {
                        throw new InvalidDataException(
                            string.Format(
                                Strings.TableDoc_RelChildColumnNotFound,
                                child.TableName,
                                childColumnNames[i]
                            )
                        );
                    }

                    if (parentColumn is null)
                    {
                        throw new InvalidDataException(
                            string.Format(
                                Strings.TableDoc_RelParentColumnNotFound,
                                parent.TableName,
                                parentColumnNames[i]
                            )
                        );
                    }

                    childColumn.IsForeignKey = true;
                    pairs.Add(new RelationshipColumnPair(parentColumn.Id, childColumn.Id));
                }

                relationship.ColumnPairs = pairs;
            }

            relationships.Add(relationship);
        }

        return relationships;
    }

    /// <summary>定義書の関係表記（1:1 / N:1 / N:N）を内部の列挙値へ変換する</summary>
    /// <summary>参照元列・参照先列セルのカンマ区切り表記を列名一覧へ分割する（空白のみの要素は捨てる）</summary>
    private static List<string> SplitColumnNames(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

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

    /// <summary>キー表記中の一意制約ラベル（<c>UQ{n}</c>）に一致する正規表現を生成する</summary>
    [GeneratedRegex(@"UQ(?<number>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UniqueConstraintKeyLabelRegex();

    /// <summary>テーブル一覧シート 1 行分の情報</summary>
    private sealed record TableSummaryRow(string TableName, string Description, string Memo);

    /// <summary>リレーション行の同一性を表す署名（重複検出に用いる）</summary>
    /// <param name="Parent">親（参照先）テーブル名</param>
    /// <param name="Child">子（参照元）テーブル名</param>
    /// <param name="ParentColumns">参照先列をセルの宣言順でカンマ連結したもの（多対多は空文字）</param>
    /// <param name="ChildColumns">参照元列をセルの宣言順でカンマ連結したもの（多対多は空文字）</param>
    /// <remarks>
    /// 構成列を含めるのは、同じ親子の間に複数の外部キーを持つ図を読み戻せるようにするため。
    /// 制約名は署名へ含めない——テーブルと構成列が同じで名前だけ違う 2 行は、DB としても区別に
    /// 意味がなく、重複行として止めるほうが検査の意味が残る。
    /// 列名は定義書の表記どおり（カンマ区切りセルの分割結果）を連結する＝セルが列名にカンマを
    /// 含められない以上、この連結で取り違えは起きない。
    /// </remarks>
    private sealed record RelationshipSignature(
        string Parent,
        string Child,
        string ParentColumns,
        string ChildColumns
    );

    /// <summary>リレーション署名を大文字小文字無視で比較する比較器（重複検出に用いる）</summary>
    private sealed class RelationshipSignatureComparer : IEqualityComparer<RelationshipSignature>
    {
        /// <summary>共有インスタンス</summary>
        public static RelationshipSignatureComparer Instance { get; } = new();

        /// <inheritdoc />
        public bool Equals(RelationshipSignature? x, RelationshipSignature? y)
        {
            if (x is null || y is null)
            {
                return ReferenceEquals(x, y);
            }

            return string.Equals(x.Parent, y.Parent, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Child, y.Child, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    x.ParentColumns,
                    y.ParentColumns,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    x.ChildColumns,
                    y.ChildColumns,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        /// <inheritdoc />
        public int GetHashCode(RelationshipSignature obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Parent),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Child),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ParentColumns),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ChildColumns)
            );
        }
    }
}
