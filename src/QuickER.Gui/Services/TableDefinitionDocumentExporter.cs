using System.Globalization;
using ClosedXML.Excel;
using QuickER.Model;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>ER 図のテーブル定義から Excel 形式のテーブル定義書を新規生成するサービス</summary>
/// <remarks>
/// 表紙・改訂履歴・テーブル一覧・リレーション一覧・テーブルごとの詳細シートを相互リンク付きで出力する。
/// 固定文言は <see cref="CultureInfo"/> を明示指定して解決するため（画面表示は CurrentUICulture）、
/// 役割シートの特定はシート名の文字列一致ではなく非表示の定義名タグで行う（取込側と対で参照する）。
/// </remarks>
public static class TableDefinitionDocumentExporter
{
    /// <summary>既定のフォント名</summary>
    private const string DefaultFontName = "游ゴシック";

    /// <summary>既定のフォントサイズ</summary>
    private const double DefaultFontSize = 11;

    /// <summary>既定の行高さ</summary>
    private const double DefaultRowHeight = 18.75;

    /// <summary>シート見出しのフォントサイズ</summary>
    private const double SheetTitleFontSize = 14;

    /// <summary>表紙の文書名のフォントサイズ</summary>
    private const double CoverTitleFontSize = 20;

    /// <summary>表紙のシステム名のフォントサイズ</summary>
    private const double CoverSystemNameFontSize = 14;

    /// <summary>詳細シートの列数（No./カラム名/説明/データ型/必須/キー/参照先/備考）</summary>
    private const int DetailColumnCount = 8;

    /// <summary>見出し行の背景色（濃紺）</summary>
    private static readonly XLColor HeaderFillColor = XLColor.FromHtml("#1F4E79");

    /// <summary>見出し行の文字色（白）</summary>
    private static readonly XLColor HeaderFontColor = XLColor.White;

    /// <summary>表紙・改訂履歴シートのタブ色（濃紺）</summary>
    private static readonly XLColor CoverTabColor = XLColor.FromHtml("#1F4E79");

    /// <summary>一覧シートのタブ色（青）</summary>
    private static readonly XLColor ListTabColor = XLColor.FromHtml("#2E75B6");

    /// <summary>詳細シートのタブ色（灰青）</summary>
    private static readonly XLColor DetailTabColor = XLColor.FromHtml("#8EAADB");

    /// <summary>ハイパーリンクの文字色</summary>
    private static readonly XLColor HyperlinkFontColor = XLColor.Blue;

    /// <summary>作成日セルの表示書式（カルチャ非依存）</summary>
    private const string DateFormat = "yyyy-mm-dd";

    /// <summary>Excel シート名に使用できない文字</summary>
    private static readonly char[] InvalidWorksheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];

    /// <summary>ER 図定義からテーブル定義書の Excel ブックを生成する</summary>
    /// <param name="diagram">対象の ER 図定義</param>
    /// <param name="documentTitle">表紙・印刷ヘッダーに載せるシステム名（未指定は空欄）</param>
    /// <param name="createdDate">作成日（未指定は当日。テストで決定的に注入するための入口）</param>
    /// <param name="culture">固定文言の言語（未指定は <see cref="CultureInfo.CurrentUICulture"/>）</param>
    /// <returns>生成済みの Excel ブック</returns>
    public static XLWorkbook BuildWorkbook(
        ErDiagram diagram,
        string? documentTitle = null,
        DateTime? createdDate = null,
        CultureInfo? culture = null
    )
    {
        var builder = new WorkbookBuilder(
            culture ?? CultureInfo.CurrentUICulture,
            createdDate ?? DateTime.Today,
            documentTitle ?? string.Empty
        );

        return builder.Build(diagram);
    }

    /// <summary>テーブル定義書を Excel ファイルとして保存する</summary>
    /// <param name="diagram">対象の ER 図定義</param>
    /// <param name="path">出力先ファイルパス</param>
    /// <param name="documentTitle">表紙・印刷ヘッダーに載せるシステム名</param>
    public static void SaveTo(ErDiagram diagram, string path, string? documentTitle = null)
    {
        using var workbook = BuildWorkbook(diagram, documentTitle);
        workbook.SaveAs(path);
    }

    /// <summary>ブック全体の基本書式を設定する</summary>
    private static void ApplyWorkbookStyle(XLWorkbook workbook)
    {
        workbook.Style.Font.FontName = DefaultFontName;
        workbook.Style.Font.FontSize = DefaultFontSize;
        workbook.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>役割シートを指す非表示の定義名タグを追加する（取込側がシート特定に用いる）</summary>
    private static void AddRoleTag(XLWorkbook workbook, string definedName, string sheetName)
    {
        var defined = workbook.DefinedNames.Add(definedName, $"{EscapeSheetName(sheetName)}!$A$1");
        defined.Visible = false;
    }

    /// <summary>シート見出し（左上セル）を設定する（14pt Bold・罫線なし）</summary>
    private static void SetSheetTitle(IXLWorksheet worksheet, string title)
    {
        var cell = worksheet.Cell(1, 1);
        cell.Value = title;
        cell.Style.Font.FontName = DefaultFontName;
        cell.Style.Font.FontSize = SheetTitleFontSize;
        cell.Style.Font.Bold = true;
    }

    /// <summary>見出し行のセルへ文言を書き込み、見出し書式を適用する</summary>
    private static void WriteHeaderRow(
        IXLWorksheet worksheet,
        int headerRow,
        IReadOnlyList<string> headers
    )
    {
        for (var i = 0; i < headers.Count; i++)
        {
            worksheet.Cell(headerRow, i + 1).Value = headers[i];
        }

        ApplyHeaderStyle(worksheet.Range(headerRow, 1, headerRow, headers.Count));
    }

    /// <summary>テーブル一覧シートの列幅など固定書式を設定する</summary>
    private static void ConfigureSummaryWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Column(1).Width = 5;
        worksheet.Column(2).Width = 6;
        worksheet.Column(3).Width = 35;
        worksheet.Column(4).Width = 35;
        worksheet.Column(5).Width = 50;
        ConfigureWorksheet(worksheet);
    }

    /// <summary>リレーション一覧シートの列幅など固定書式を設定する</summary>
    private static void ConfigureRelationshipWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Column(1).Width = 5;
        worksheet.Column(2).Width = 50;

        for (var column = 3; column <= 6; column++)
        {
            worksheet.Column(column).Width = 30;
        }

        worksheet.Column(7).Width = 10;
        worksheet.Column(8).Width = 14;
        worksheet.Column(9).Width = 14;
        worksheet.Column(10).Width = 50;
        ConfigureWorksheet(worksheet);
    }

    /// <summary>テーブル詳細シートの列幅など固定書式を設定する</summary>
    private static void ConfigureDetailWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Column(1).Width = 5;
        worksheet.Column(2).Width = 30;
        worksheet.Column(3).Width = 30;
        worksheet.Column(4).Width = 17;
        worksheet.Column(5).Width = 8;
        worksheet.Column(6).Width = 8;
        worksheet.Column(7).Width = 40;
        worksheet.Column(8).Width = 40;
        ConfigureWorksheet(worksheet);
    }

    /// <summary>各シート共通のフォント・配置を設定する（ページ設定は <c>ApplyCommonPageSetup</c> が担う）</summary>
    private static void ConfigureWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Style.Font.FontName = DefaultFontName;
        worksheet.Style.Font.FontSize = DefaultFontSize;
        worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>見出し行の書式（背景色・白字 Bold）を適用する</summary>
    private static void ApplyHeaderStyle(IXLRange range)
    {
        range.Style.Font.FontName = DefaultFontName;
        range.Style.Font.FontSize = DefaultFontSize;
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = HeaderFontColor;
        range.Style.Fill.BackgroundColor = HeaderFillColor;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>データ行の書式（フォント・上詰め）を適用する</summary>
    private static void ApplyDataRowStyle(IXLRange range)
    {
        range.Style.Font.FontName = DefaultFontName;
        range.Style.Font.FontSize = DefaultFontSize;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>表範囲へ罫線（外枠 Medium・内側 Thin）を適用する</summary>
    private static void ApplyTableBorders(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    /// <summary>ハイパーリンクの書式とリンク先シートを設定する</summary>
    private static void ApplyHyperlinkStyle(
        IXLCell cell,
        string targetSheetName,
        XLAlignmentHorizontalValues horizontalAlignment
    )
    {
        cell.SetHyperlink(new XLHyperlink($"{EscapeSheetName(targetSheetName)}!A1"));
        cell.Style.Font.FontName = DefaultFontName;
        cell.Style.Font.FontSize = DefaultFontSize;
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
        cell.Style.Font.FontColor = HyperlinkFontColor;
        cell.Style.Alignment.Horizontal = horizontalAlignment;
    }

    /// <summary>印刷範囲を指定アドレスへ設定する</summary>
    private static void UpdatePrintArea(IXLWorksheet worksheet, string address)
    {
        worksheet.PageSetup.PrintAreas.Clear();
        worksheet.PageSetup.PrintAreas.Add(address);
    }

    /// <summary>Excel シート名に使えない文字を除去し、31 文字制限と重複回避を考慮した一意な名前を生成する</summary>
    private static string CreateUniqueWorksheetName(
        string tableName,
        ISet<string> usedWorksheetNames
    )
    {
        var sanitized = new string(
            tableName.Where(ch => !InvalidWorksheetNameChars.Contains(ch)).ToArray()
        )
            .Trim()
            .Trim('\'');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Table";
        }

        if (sanitized.Length > 31)
        {
            sanitized = sanitized[..31];
        }

        var candidate = sanitized;
        var suffix = 1;

        while (!usedWorksheetNames.Add(candidate))
        {
            var suffixText = $"_{suffix}";
            var baseLength = Math.Max(1, 31 - suffixText.Length);
            candidate = sanitized[..Math.Min(sanitized.Length, baseLength)] + suffixText;
            suffix++;
        }

        return candidate;
    }

    /// <summary>ハイパーリンク参照用にシート名を引用符で囲み内部の引用符をエスケープする</summary>
    private static string EscapeSheetName(string sheetName) => $"'{sheetName.Replace("'", "''")}'";

    /// <summary>詳細シート生成に必要な番号・エンティティ・対応ワークシートを束ねる文脈情報</summary>
    private sealed record DetailSheetContext(int Number, Entity Entity, IXLWorksheet Worksheet);

    /// <summary>1 回のブック生成で共有するカルチャ・作成日・システム名を保持しつつシートを組み立てるビルダー</summary>
    /// <remarks>固定文言は <see cref="L"/> を通じて明示カルチャで解決する（静的プロパティ直読みは行わない）。</remarks>
    private sealed class WorkbookBuilder(
        CultureInfo culture,
        DateTime createdDate,
        string systemName
    )
    {
        /// <summary>固定文言を明示カルチャで解決する（未定義キーはキー名を返す）</summary>
        private string L(string key) => Strings.ResourceManager.GetString(key, culture) ?? key;

        /// <summary>ER 図からテーブル定義書ブックを組み立てる</summary>
        public XLWorkbook Build(ErDiagram diagram)
        {
            var workbook = new XLWorkbook();
            ApplyWorkbookStyle(workbook);

            var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
            var entities = TableDefinitionContentBuilder.OrderEntities(diagram.Entities);

            // ローカライズ済みシート名も防御として sanitize と重複回避を通す
            var usedWorksheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var coverSheet = workbook.Worksheets.Add(
                CreateUniqueWorksheetName(
                    L(nameof(Strings.TableDoc_Sheet_Cover)),
                    usedWorksheetNames
                )
            );
            var historySheet = workbook.Worksheets.Add(
                CreateUniqueWorksheetName(
                    L(nameof(Strings.TableDoc_Sheet_History)),
                    usedWorksheetNames
                )
            );
            var summarySheet = workbook.Worksheets.Add(
                CreateUniqueWorksheetName(
                    L(nameof(Strings.TableDoc_Sheet_Summary)),
                    usedWorksheetNames
                )
            );
            var relationshipSheet = workbook.Worksheets.Add(
                CreateUniqueWorksheetName(
                    L(nameof(Strings.TableDoc_Sheet_Relationships)),
                    usedWorksheetNames
                )
            );

            var detailSheets = new List<DetailSheetContext>();

            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                var worksheetName = CreateUniqueWorksheetName(entity.TableName, usedWorksheetNames);
                var worksheet = workbook.Worksheets.Add(worksheetName);
                detailSheets.Add(new DetailSheetContext(i + 1, entity, worksheet));
            }

            var entitySheetNames = detailSheets.ToDictionary(
                detail => detail.Entity.Id,
                detail => detail.Worksheet.Name
            );

            BuildCoverWorksheet(coverSheet, diagram, entities.Count, diagram.Relationships.Count);
            BuildHistoryWorksheet(historySheet);
            BuildSummaryWorksheet(summarySheet, detailSheets);
            BuildRelationshipWorksheet(relationshipSheet, diagram.Relationships, entitiesById);

            foreach (var detailSheet in detailSheets)
            {
                var relatedRelationships = diagram
                    .Relationships.Where(relationship =>
                        relationship.SourceEntityId == detailSheet.Entity.Id
                        || relationship.TargetEntityId == detailSheet.Entity.Id
                    )
                    .ToList();

                BuildEntityWorksheet(
                    detailSheet.Worksheet,
                    detailSheet.Entity,
                    detailSheet.Number,
                    relatedRelationships,
                    entitiesById,
                    entitySheetNames,
                    summarySheet.Name
                );
            }

            // 全シート構築後に役割タグ（非表示の定義名）と書式バージョンを刻む
            AddRoleTag(workbook, TableDefinitionDocumentLayout.CoverDefinedName, coverSheet.Name);
            AddRoleTag(
                workbook,
                TableDefinitionDocumentLayout.HistoryDefinedName,
                historySheet.Name
            );
            AddRoleTag(
                workbook,
                TableDefinitionDocumentLayout.SummaryDefinedName,
                summarySheet.Name
            );
            AddRoleTag(
                workbook,
                TableDefinitionDocumentLayout.RelationshipsDefinedName,
                relationshipSheet.Name
            );
            workbook.CustomProperties.Add(
                TableDefinitionDocumentLayout.FormatVersionPropertyName,
                TableDefinitionDocumentLayout.FormatVersionValue
            );

            return workbook;
        }

        /// <summary>表紙シートを生成する（文書名・システム名・書誌情報を配置する）</summary>
        private void BuildCoverWorksheet(
            IXLWorksheet worksheet,
            ErDiagram diagram,
            int tableCount,
            int relationshipCount
        )
        {
            ConfigureWorksheet(worksheet);
            worksheet.ShowGridLines = false;
            worksheet.TabColor = CoverTabColor;
            worksheet.Column(2).Width = 18;
            worksheet.Column(3).Width = 40;

            // 文書名（大見出し）
            var titleRange = worksheet.Range("B4:F4").Merge();
            titleRange.Value = L(nameof(Strings.TableDoc_DocumentTitle));
            titleRange.Style.Font.FontSize = CoverTitleFontSize;
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // システム名（文書名の直下）
            var systemRange = worksheet.Range("B6:F6").Merge();
            systemRange.Value = systemName;
            systemRange.Style.Font.FontSize = CoverSystemNameFontSize;
            systemRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // ラベル / 値の一覧（B9〜C13）
            var infoStartRow = 9;
            SetCoverLabel(worksheet, infoStartRow, L(nameof(Strings.TableDoc_Cover_TargetDbms)));
            worksheet.Cell(infoStartRow, 3).Value = diagram.TargetDbms;

            SetCoverLabel(worksheet, infoStartRow + 1, L(nameof(Strings.TableDoc_Cover_Version)));
            worksheet.Cell(infoStartRow + 1, 3).Value = "1.0";

            SetCoverLabel(
                worksheet,
                infoStartRow + 2,
                L(nameof(Strings.TableDoc_Cover_CreatedDate))
            );
            var dateCell = worksheet.Cell(infoStartRow + 2, 3);
            dateCell.Value = createdDate;
            dateCell.Style.DateFormat.Format = DateFormat;

            SetCoverLabel(
                worksheet,
                infoStartRow + 3,
                L(nameof(Strings.TableDoc_Cover_TableCount))
            );
            worksheet.Cell(infoStartRow + 3, 3).Value = tableCount;

            SetCoverLabel(
                worksheet,
                infoStartRow + 4,
                L(nameof(Strings.TableDoc_Cover_RelationshipCount))
            );
            worksheet.Cell(infoStartRow + 4, 3).Value = relationshipCount;

            ApplyCommonPageSetup(worksheet, 0);
            UpdatePrintArea(worksheet, "A1:F14");
        }

        /// <summary>表紙の書誌ラベルセルを設定する（Bold）</summary>
        private static void SetCoverLabel(IXLWorksheet worksheet, int row, string label)
        {
            var cell = worksheet.Cell(row, 2);
            cell.Value = label;
            cell.Style.Font.Bold = true;
        }

        /// <summary>改訂履歴シートを生成する（初版行を含む）</summary>
        private void BuildHistoryWorksheet(IXLWorksheet worksheet)
        {
            ConfigureWorksheet(worksheet);
            worksheet.TabColor = CoverTabColor;
            worksheet.Column(1).Width = 8;
            worksheet.Column(2).Width = 14;
            worksheet.Column(3).Width = 60;
            worksheet.Column(4).Width = 16;

            SetSheetTitle(worksheet, L(nameof(Strings.TableDoc_Sheet_History)));

            var headerRow = 3;
            var headers = new[]
            {
                L(nameof(Strings.TableDoc_History_Version)),
                L(nameof(Strings.TableDoc_History_Date)),
                L(nameof(Strings.TableDoc_History_Content)),
                L(nameof(Strings.TableDoc_History_Author)),
            };
            WriteHeaderRow(worksheet, headerRow, headers);

            // 初版行（版数 1.0・作成日・初版・作成者は空欄）
            var dataRow = 4;
            ApplyDataRowStyle(worksheet.Range(dataRow, 1, dataRow, headers.Length));
            worksheet.Row(dataRow).Height = DefaultRowHeight;
            worksheet.Cell(dataRow, 1).Value = "1.0";
            var dateCell = worksheet.Cell(dataRow, 2);
            dateCell.Value = createdDate;
            dateCell.Style.DateFormat.Format = DateFormat;
            worksheet.Cell(dataRow, 3).Value = L(nameof(Strings.TableDoc_History_InitialEntry));

            ApplyTableBorders(worksheet.Range(headerRow, 1, dataRow, headers.Length));

            worksheet.SheetView.FreezeRows(3);
            ApplyCommonPageSetup(worksheet, 0);
            UpdatePrintArea(worksheet, $"A1:D{dataRow}");
        }

        /// <summary>テーブル一覧シートを生成する（各行に詳細シートへのリンクを付与する）</summary>
        private void BuildSummaryWorksheet(
            IXLWorksheet worksheet,
            IReadOnlyList<DetailSheetContext> detailSheets
        )
        {
            ConfigureSummaryWorksheet(worksheet);
            worksheet.TabColor = ListTabColor;

            SetSheetTitle(worksheet, L(nameof(Strings.TableDoc_Sheet_Summary)));

            var headerRow = TableDefinitionDocumentLayout.SummaryHeaderRow;
            var dataStartRow = TableDefinitionDocumentLayout.SummaryDataStartRow;
            var detailLabel = L(nameof(Strings.TableDoc_Header_Detail));
            var headers = new[]
            {
                L(nameof(Strings.TableDoc_Header_No)),
                detailLabel,
                L(nameof(Strings.TableDoc_Header_TableName)),
                L(nameof(Strings.TableDoc_Header_Description)),
                L(nameof(Strings.TableDoc_Header_Memo)),
            };
            WriteHeaderRow(worksheet, headerRow, headers);

            for (var i = 0; i < detailSheets.Count; i++)
            {
                var detailSheet = detailSheets[i];
                var row = dataStartRow + i;

                ApplyDataRowStyle(worksheet.Range(row, 1, row, headers.Length));
                worksheet.Row(row).Height = DefaultRowHeight;
                worksheet.Cell(row, 1).Value = detailSheet.Number;
                worksheet.Cell(row, 2).Value = detailLabel;
                worksheet.Cell(row, 3).Value = detailSheet.Entity.TableName;
                worksheet.Cell(row, 4).Value = detailSheet.Entity.Description;
                worksheet.Cell(row, 5).Value = detailSheet.Entity.Memo;
                ApplyHyperlinkStyle(
                    worksheet.Cell(row, 2),
                    detailSheet.Worksheet.Name,
                    XLAlignmentHorizontalValues.Center
                );
            }

            var lastRow = Math.Max(headerRow, dataStartRow + detailSheets.Count - 1);
            ApplyTableBorders(worksheet.Range(headerRow, 1, lastRow, headers.Length));

            // 説明・備考列は折り返し表示
            if (detailSheets.Count > 0)
            {
                worksheet.Range(dataStartRow, 4, lastRow, 5).Style.Alignment.WrapText = true;
            }

            worksheet.SheetView.FreezeRows(3);
            worksheet.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();
            ApplyCommonPageSetup(worksheet, headerRow);
            UpdatePrintArea(worksheet, $"A1:E{lastRow}");
        }

        /// <summary>リレーション一覧シートを生成する</summary>
        private void BuildRelationshipWorksheet(
            IXLWorksheet worksheet,
            IEnumerable<Relationship> relationships,
            IReadOnlyDictionary<Guid, Entity> entitiesById
        )
        {
            ConfigureRelationshipWorksheet(worksheet);
            worksheet.TabColor = ListTabColor;

            SetSheetTitle(worksheet, L(nameof(Strings.TableDoc_Sheet_Relationships)));

            var orderedRelationships = TableDefinitionContentBuilder.OrderRelationships(
                relationships,
                entitiesById
            );
            var headerRow = TableDefinitionDocumentLayout.RelationshipHeaderRow;
            var dataStartRow = TableDefinitionDocumentLayout.RelationshipDataStartRow;
            var headers = new[]
            {
                L(nameof(Strings.TableDoc_Header_No)),
                L(nameof(Strings.TableDoc_Header_ConstraintName)),
                L(nameof(Strings.TableDoc_Header_SourceTable)),
                L(nameof(Strings.TableDoc_Header_SourceColumn)),
                L(nameof(Strings.TableDoc_Header_TargetTable)),
                L(nameof(Strings.TableDoc_Header_TargetColumn)),
                L(nameof(Strings.TableDoc_Header_Relation)),
                // ON DELETE / ON UPDATE は SQL 用語のためリテラル維持
                "ON DELETE",
                "ON UPDATE",
                L(nameof(Strings.TableDoc_Header_Memo)),
            };
            WriteHeaderRow(worksheet, headerRow, headers);

            for (var i = 0; i < orderedRelationships.Count; i++)
            {
                var relationship = orderedRelationships[i];
                var row = dataStartRow + i;

                ApplyDataRowStyle(worksheet.Range(row, 1, row, headers.Length));
                worksheet.Row(row).Height = DefaultRowHeight;
                worksheet.Cell(row, 1).Value = i + 1;
                worksheet.Cell(row, 2).Value = relationship.ConstraintName ?? string.Empty;
                // 参照元（FK 保有側）は Target、参照先（PK 側）は Source に対応する
                worksheet.Cell(row, 3).Value = TableDefinitionContentBuilder.TableNameOf(
                    entitiesById,
                    relationship.TargetEntityId
                );
                worksheet.Cell(row, 4).Value = TableDefinitionContentBuilder.ColumnNameOf(
                    entitiesById,
                    relationship.TargetEntityId,
                    relationship.TargetColumnId
                );
                worksheet.Cell(row, 5).Value = TableDefinitionContentBuilder.TableNameOf(
                    entitiesById,
                    relationship.SourceEntityId
                );
                worksheet.Cell(row, 6).Value = TableDefinitionContentBuilder.ColumnNameOf(
                    entitiesById,
                    relationship.SourceEntityId,
                    relationship.SourceColumnId
                );
                worksheet.Cell(row, 7).Value =
                    TableDefinitionContentBuilder.GetRelationshipTypeLabel(relationship.Type);
                worksheet.Cell(row, 8).Value = relationship.OnDelete.ToDisplayText();
                worksheet.Cell(row, 9).Value = relationship.OnUpdate.ToDisplayText();
            }

            var lastRow = Math.Max(headerRow, dataStartRow + orderedRelationships.Count - 1);
            ApplyTableBorders(worksheet.Range(headerRow, 1, lastRow, headers.Length));

            // 備考列は折り返し表示
            if (orderedRelationships.Count > 0)
            {
                worksheet.Range(dataStartRow, 10, lastRow, 10).Style.Alignment.WrapText = true;
            }

            worksheet.SheetView.FreezeRows(3);
            worksheet.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();
            ApplyCommonPageSetup(worksheet, headerRow);
            UpdatePrintArea(worksheet, $"A1:J{lastRow}");
        }

        /// <summary>テーブル単位の定義書シートを生成する（上部の一覧戻りリンクとカラム一覧を含む）</summary>
        private void BuildEntityWorksheet(
            IXLWorksheet worksheet,
            Entity entity,
            int tableNumber,
            IReadOnlyList<Relationship> relationships,
            IReadOnlyDictionary<Guid, Entity> entitiesById,
            IReadOnlyDictionary<Guid, string> entitySheetNames,
            string summarySheetName
        )
        {
            ConfigureDetailWorksheet(worksheet);
            worksheet.TabColor = DetailTabColor;
            var foreignKeyLabels = TableDefinitionContentBuilder.BuildForeignKeyLabels(
                entity,
                relationships,
                entitiesById
            );

            // 行1: A1 テーブル名タイトル ＋ 末尾列（H1）に一覧への戻りリンク（右寄せ）
            SetSheetTitle(worksheet, entity.TableName);
            var backCell = worksheet.Cell(1, DetailColumnCount);
            backCell.Value = L(nameof(Strings.TableDoc_BackToSummary));
            ApplyHyperlinkStyle(backCell, summarySheetName, XLAlignmentHorizontalValues.Right);

            // テーブル情報（行3 ヘッダ・行4 データ）
            var infoHeaderRow = 3;
            var infoDataRow = TableDefinitionDocumentLayout.DetailTableInfoRow;
            WriteHeaderRow(
                worksheet,
                infoHeaderRow,
                new[]
                {
                    L(nameof(Strings.TableDoc_Header_No)),
                    L(nameof(Strings.TableDoc_Header_TableName)),
                    L(nameof(Strings.TableDoc_Header_Description)),
                }
            );
            ApplyDataRowStyle(worksheet.Range(infoDataRow, 1, infoDataRow, 3));
            worksheet.Row(infoDataRow).Height = DefaultRowHeight;
            worksheet.Cell(infoDataRow, 1).Value = tableNumber;
            worksheet.Cell(infoDataRow, 2).Value = entity.TableName;
            worksheet.Cell(infoDataRow, 3).Value = entity.Description;
            ApplyTableBorders(worksheet.Range(infoHeaderRow, 1, infoDataRow, 3));

            // カラム見出し（行6）・カラムデータ（行7〜）
            var columnHeaderRow = TableDefinitionDocumentLayout.DetailColumnHeaderRow;
            var headers = new[]
            {
                L(nameof(Strings.TableDoc_Header_No)),
                L(nameof(Strings.TableDoc_Header_ColumnName)),
                L(nameof(Strings.TableDoc_Header_Description)),
                L(nameof(Strings.TableDoc_Header_DataType)),
                L(nameof(Strings.TableDoc_Header_Required)),
                L(nameof(Strings.TableDoc_Header_Key)),
                L(nameof(Strings.TableDoc_Header_Reference)),
                L(nameof(Strings.TableDoc_Header_Memo)),
            };
            WriteHeaderRow(worksheet, columnHeaderRow, headers);

            var dataStartRow = TableDefinitionDocumentLayout.DetailColumnDataStartRow;
            var requiredMark = L(nameof(Strings.TableDoc_RequiredMark));

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                var column = entity.Columns[i];
                var row = dataStartRow + i;

                ApplyDataRowStyle(worksheet.Range(row, 1, row, headers.Length));
                worksheet.Row(row).Height = DefaultRowHeight;
                worksheet.Cell(row, 1).Value = i + 1;
                worksheet.Cell(row, 2).Value = column.Name;
                worksheet.Cell(row, 3).Value = column.Description;
                worksheet.Cell(row, 4).Value = column.DataType;
                worksheet.Cell(row, 5).Value = column.IsNullable ? string.Empty : requiredMark;
                worksheet.Cell(row, 6).Value = TableDefinitionContentBuilder.GetKeyLabel(
                    column,
                    foreignKeyLabels.TryGetValue(column.Id, out var foreignKeyLabel)
                        ? foreignKeyLabel
                        : null
                );
                worksheet.Cell(row, 7).Value = TableDefinitionContentBuilder.GetReferenceText(
                    entity,
                    column,
                    relationships,
                    entitiesById
                );

                // 参照先が単一テーブルのときは該当詳細シートへリンク化（複数参照時はテキストのみ）
                var referencedIds = TableDefinitionContentBuilder.GetReferencedEntityIds(
                    entity,
                    column,
                    relationships
                );

                if (
                    referencedIds.Count == 1
                    && entitySheetNames.TryGetValue(referencedIds[0], out var targetSheet)
                )
                {
                    ApplyHyperlinkStyle(
                        worksheet.Cell(row, 7),
                        targetSheet,
                        XLAlignmentHorizontalValues.Left
                    );
                }
            }

            var lastRow = Math.Max(columnHeaderRow, dataStartRow + entity.Columns.Count - 1);
            ApplyTableBorders(worksheet.Range(columnHeaderRow, 1, lastRow, headers.Length));

            // 説明・備考列は折り返し表示
            if (entity.Columns.Count > 0)
            {
                worksheet.Range(dataStartRow, 3, lastRow, 3).Style.Alignment.WrapText = true;
                worksheet.Range(dataStartRow, 8, lastRow, 8).Style.Alignment.WrapText = true;
            }

            worksheet.SheetView.FreezeRows(6);
            ApplyCommonPageSetup(worksheet, columnHeaderRow);
            UpdatePrintArea(worksheet, $"A1:H{lastRow}");
        }

        /// <summary>各シート共通の印刷体裁（A4 横・1 ページ幅・余白・ヘッダー/フッター・繰り返し行）を適用する</summary>
        private void ApplyCommonPageSetup(IXLWorksheet worksheet, int repeatToRow)
        {
            var pageSetup = worksheet.PageSetup;
            pageSetup.PageOrientation = XLPageOrientation.Landscape;
            pageSetup.PaperSize = XLPaperSize.A4Paper;
            pageSetup.PagesWide = 1;
            pageSetup.PagesTall = 0;
            pageSetup.Margins.Top = 0.75;
            pageSetup.Margins.Bottom = 0.75;
            pageSetup.Margins.Left = 0.5;
            pageSetup.Margins.Right = 0.5;

            var docTitle = L(nameof(Strings.TableDoc_DocumentTitle));
            var headerText = string.IsNullOrEmpty(systemName)
                ? docTitle
                : $"{systemName} - {docTitle}";
            pageSetup.Header.Center.AddText(headerText, XLHFOccurrence.AllPages);

            // フッター中央に「頁 / 総頁」を組み立てる（AddText は IXLRichString を返すため都度呼ぶ）
            var footer = pageSetup.Footer.Center;
            footer.AddText(XLHFPredefinedText.PageNumber);
            footer.AddText(" / ", XLHFOccurrence.AllPages);
            footer.AddText(XLHFPredefinedText.NumberOfPages);

            if (repeatToRow > 0)
            {
                pageSetup.SetRowsToRepeatAtTop(1, repeatToRow);
            }
        }
    }
}
