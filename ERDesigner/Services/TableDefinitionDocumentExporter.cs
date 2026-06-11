using ClosedXML.Excel;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>ER 図のテーブル定義から Excel 形式のテーブル定義書を新規生成するサービス</summary>
/// <remarks>テーブル一覧・リレーション一覧・テーブルごとの詳細シートを相互リンク付きで出力する</remarks>
public static class TableDefinitionDocumentExporter
{
    /// <summary>テーブル一覧シート名</summary>
    private const string SummarySheetName = "テーブル一覧";

    /// <summary>リレーション一覧シート名</summary>
    private const string RelationshipSheetName = "リレーション一覧";

    /// <summary>詳細シートへのリンクセルの表示文言</summary>
    private const string DetailLinkText = "詳細";

    /// <summary>一覧へ戻るリンクの表示文言</summary>
    private const string BackToSummaryText = "テーブル一覧に戻る";

    /// <summary>既定のフォント名</summary>
    private const string DefaultFontName = "游ゴシック";

    /// <summary>既定のフォントサイズ</summary>
    private const double DefaultFontSize = 11;

    /// <summary>既定の行高さ</summary>
    private const double DefaultRowHeight = 18.75;

    /// <summary>Excel シート名に使用できない文字</summary>
    private static readonly char[] InvalidWorksheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];

    /// <summary>現在のダイアグラムからテーブル定義書の Excel ブックを生成する</summary>
    /// <param name="vm">対象の <see cref="MainViewModel" /></param>
    /// <returns>生成済みの Excel ブック</returns>
    public static XLWorkbook BuildWorkbook(MainViewModel vm)
    {
        var workbook = new XLWorkbook();
        ApplyWorkbookStyle(workbook);

        var entities = vm.Entities.OrderBy(entity => entity.TableName, StringComparer.OrdinalIgnoreCase).ToList();
        var summaryWorksheet = workbook.Worksheets.Add(SummarySheetName);
        var relationshipWorksheet = workbook.Worksheets.Add(RelationshipSheetName);
        var usedWorksheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SummarySheetName, RelationshipSheetName };
        var detailSheets = new List<DetailSheetContext>();

        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            var worksheetName = CreateUniqueWorksheetName(entity.TableName, usedWorksheetNames);
            var worksheet = workbook.Worksheets.Add(worksheetName);
            detailSheets.Add(new DetailSheetContext(i + 1, entity, worksheet));
        }

        BuildSummaryWorksheet(summaryWorksheet, detailSheets);
        BuildRelationshipWorksheet(relationshipWorksheet, vm.Relationships);

        foreach (var detailSheet in detailSheets)
        {
            var relatedRelationships = vm.Relationships.Where(relationship => relationship.Source == detailSheet.Entity || relationship.Target == detailSheet.Entity).ToList();

            BuildEntityWorksheet(detailSheet.Worksheet, detailSheet.Entity, detailSheet.Number, relatedRelationships);
        }

        return workbook;
    }

    /// <summary>テーブル定義書を Excel ファイルとして保存する</summary>
    /// <param name="vm">対象の <see cref="MainViewModel" /></param>
    /// <param name="path">出力先ファイルパス</param>
    public static void SaveTo(MainViewModel vm, string path)
    {
        using var workbook = BuildWorkbook(vm);
        workbook.SaveAs(path);
    }

    /// <summary>ブック全体の基本書式を設定する</summary>
    private static void ApplyWorkbookStyle(XLWorkbook workbook)
    {
        workbook.Style.Font.FontName = DefaultFontName;
        workbook.Style.Font.FontSize = DefaultFontSize;
        workbook.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>テーブル一覧シートを生成する（各行に詳細シートへのリンクを付与する）</summary>
    private static void BuildSummaryWorksheet(IXLWorksheet worksheet, IReadOnlyList<DetailSheetContext> detailSheets)
    {
        ConfigureSummaryWorksheet(worksheet);

        var headerRow = 1;
        var dataStartRow = 2;
        var headers = new[] { "No.", DetailLinkText, "テーブル名", "説明", "備考" };

        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(headerRow, i + 1).Value = headers[i];
        }

        ApplyHeaderStyle(worksheet.Range(headerRow, 1, headerRow, headers.Length));

        for (var i = 0; i < detailSheets.Count; i++)
        {
            var detailSheet = detailSheets[i];
            var row = dataStartRow + i;

            ApplyDataRowStyle(worksheet.Range(row, 1, row, headers.Length));
            worksheet.Row(row).Height = DefaultRowHeight;
            worksheet.Cell(row, 1).Value = detailSheet.Number;
            worksheet.Cell(row, 2).Value = DetailLinkText;
            worksheet.Cell(row, 3).Value = detailSheet.Entity.TableName;
            worksheet.Cell(row, 4).Value = detailSheet.Entity.Description;
            worksheet.Cell(row, 5).Value = detailSheet.Entity.Memo;
            ApplyHyperlinkStyle(worksheet.Cell(row, 2), detailSheet.Worksheet.Name, XLAlignmentHorizontalValues.Center);
        }

        UpdatePrintArea(worksheet, $"A1:E{Math.Max(headerRow, dataStartRow + detailSheets.Count - 1)}");
    }

    /// <summary>リレーション一覧シートを生成する</summary>
    private static void BuildRelationshipWorksheet(IXLWorksheet worksheet, IEnumerable<RelationshipViewModel> relationships)
    {
        ConfigureRelationshipWorksheet(worksheet);

        var orderedRelationships = relationships
            .OrderBy(relationship => relationship.Source.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.Target.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var headerRow = 1;
        var dataStartRow = 2;
        var headers = new[] { "No.", "制約名", "参照元テーブル", "参照元カラム", "参照先テーブル", "参照先カラム", "関係", "ON DELETE", "ON UPDATE", "備考" };

        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(headerRow, i + 1).Value = headers[i];
        }

        ApplyHeaderStyle(worksheet.Range(headerRow, 1, headerRow, headers.Length));

        for (var i = 0; i < orderedRelationships.Count; i++)
        {
            var relationship = orderedRelationships[i];
            var row = dataStartRow + i;

            ApplyDataRowStyle(worksheet.Range(row, 1, row, headers.Length));
            worksheet.Row(row).Height = DefaultRowHeight;
            worksheet.Cell(row, 1).Value = i + 1;
            worksheet.Cell(row, 2).Value = relationship.ConstraintName ?? string.Empty;
            // 参照元（FK 保有側）は Target、参照先（PK 側）は Source に対応する
            worksheet.Cell(row, 3).Value = relationship.Target.TableName;
            worksheet.Cell(row, 4).Value = GetColumnName(relationship.Target, relationship.TargetColumnId);
            worksheet.Cell(row, 5).Value = relationship.Source.TableName;
            worksheet.Cell(row, 6).Value = GetColumnName(relationship.Source, relationship.SourceColumnId);
            worksheet.Cell(row, 7).Value = GetRelationshipTypeLabel(relationship.Type);
            worksheet.Cell(row, 8).Value = relationship.OnDelete.ToDisplayText();
            worksheet.Cell(row, 9).Value = relationship.OnUpdate.ToDisplayText();
        }

        UpdatePrintArea(worksheet, $"A1:J{Math.Max(headerRow, dataStartRow + orderedRelationships.Count - 1)}");
    }

    /// <summary>テーブル単位の定義書シートを生成する（カラム一覧と一覧への戻りリンクを含む）</summary>
    private static void BuildEntityWorksheet(IXLWorksheet worksheet, EntityViewModel entity, int tableNumber, IReadOnlyList<RelationshipViewModel> relationships)
    {
        ConfigureDetailWorksheet(worksheet);
        var foreignKeyLabels = BuildForeignKeyLabels(entity, relationships);

        worksheet.Cell(1, 1).Value = "No.";
        worksheet.Cell(1, 2).Value = "テーブル名";
        worksheet.Cell(1, 3).Value = "説明";
        ApplyHeaderStyle(worksheet.Range(1, 1, 1, 3));

        ApplyDataRowStyle(worksheet.Range(2, 1, 2, 3));
        worksheet.Row(2).Height = DefaultRowHeight;
        worksheet.Cell(2, 1).Value = tableNumber;
        worksheet.Cell(2, 2).Value = entity.TableName;
        worksheet.Cell(2, 3).Value = entity.Description;

        var columnHeaderRow = 4;
        var headers = new[] { "No.", "カラム名", "説明", "データ型", "必須", "キー", "参照先", "備考" };

        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(columnHeaderRow, i + 1).Value = headers[i];
        }

        ApplyHeaderStyle(worksheet.Range(columnHeaderRow, 1, columnHeaderRow, headers.Length));

        var dataStartRow = columnHeaderRow + 1;

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
            worksheet.Cell(row, 5).Value = column.IsNullable ? string.Empty : "〇";
            worksheet.Cell(row, 6).Value = GetKeyLabel(column, foreignKeyLabels.TryGetValue(column.Id, out var foreignKeyLabel) ? foreignKeyLabel : null);
            worksheet.Cell(row, 7).Value = GetReferenceText(entity, column, relationships);
        }

        var footerRow = dataStartRow + entity.Columns.Count + 1;
        worksheet.Row(footerRow).Height = DefaultRowHeight;
        worksheet.Cell(footerRow, 1).Value = BackToSummaryText;
        ApplyFooterHyperlinkStyle(worksheet.Cell(footerRow, 1));
        ApplyHyperlinkStyle(worksheet.Cell(footerRow, 1), SummarySheetName, XLAlignmentHorizontalValues.Left);
        UpdatePrintArea(worksheet, $"A1:G{footerRow}");
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

    /// <summary>各シート共通のフォント・配置・ページ設定を行う</summary>
    private static void ConfigureWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Style.Font.FontName = DefaultFontName;
        worksheet.Style.Font.FontSize = DefaultFontSize;
        worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
    }

    /// <summary>見出し行の書式（背景色・罫線など）を適用する</summary>
    private static void ApplyHeaderStyle(IXLRange range)
    {
        range.Style.Font.FontName = DefaultFontName;
        range.Style.Font.FontSize = DefaultFontSize;
        range.Style.Fill.BackgroundColor = XLColor.LightBlue;
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        range.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>データ行の書式（罫線など）を適用する</summary>
    private static void ApplyDataRowStyle(IXLRange range)
    {
        range.Style.Font.FontName = DefaultFontName;
        range.Style.Font.FontSize = DefaultFontSize;
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        range.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>ハイパーリンクの書式とリンク先シートを設定する</summary>
    private static void ApplyHyperlinkStyle(IXLCell cell, string targetSheetName, XLAlignmentHorizontalValues horizontalAlignment)
    {
        cell.SetHyperlink(new XLHyperlink($"{EscapeSheetName(targetSheetName)}!A1"));
        cell.Style.Font.FontName = DefaultFontName;
        cell.Style.Font.FontSize = DefaultFontSize;
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
        cell.Style.Font.FontColor = XLColor.Blue;
        cell.Style.Alignment.Horizontal = horizontalAlignment;
    }

    /// <summary>フッターの戻りリンクの文字書式を適用する</summary>
    private static void ApplyFooterHyperlinkStyle(IXLCell cell)
    {
        cell.Style.Font.FontName = DefaultFontName;
        cell.Style.Font.FontSize = DefaultFontSize;
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
        cell.Style.Font.FontColor = XLColor.Blue;
    }

    /// <summary>印刷範囲を指定アドレスへ設定する</summary>
    private static void UpdatePrintArea(IXLWorksheet worksheet, string address)
    {
        worksheet.PageSetup.PrintAreas.Clear();
        worksheet.PageSetup.PrintAreas.Add(address);
    }

    /// <summary>リレーション種別を定義書向けの表記（1:1 / N:1 / N:N）へ変換する</summary>
    private static string GetRelationshipTypeLabel(RelationshipType type) =>
        type switch
        {
            RelationshipType.OneToOne => "1:1",
            RelationshipType.OneToMany => "N:1",
            RelationshipType.ManyToMany => "N:N",
            _ => type.ToString(),
        };

    /// <summary>テーブル内の外部キーに連番（FK1, FK2…）を振った列 ID ごとの表示ラベルを構築する</summary>
    private static IReadOnlyDictionary<Guid, string> BuildForeignKeyLabels(EntityViewModel entity, IReadOnlyList<RelationshipViewModel> relationships)
    {
        var columnIndexes = entity.Columns.Select((column, index) => new { column.Id, index }).ToDictionary(item => item.Id, item => item.index);
        var foreignKeyLabels = new Dictionary<Guid, List<string>>();
        var targetRelationships = relationships
            .Where(relationship => relationship.Target == entity && relationship.TargetColumnId is not null)
            .OrderBy(relationship => columnIndexes.GetValueOrDefault(relationship.TargetColumnId!.Value, int.MaxValue))
            .ThenBy(relationship => relationship.Source.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => GetColumnName(relationship.Source, relationship.SourceColumnId), StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < targetRelationships.Count; i++)
        {
            var relationship = targetRelationships[i];
            var targetColumnId = relationship.TargetColumnId!.Value;

            if (!foreignKeyLabels.TryGetValue(targetColumnId, out var labels))
            {
                labels = [];
                foreignKeyLabels[targetColumnId] = labels;
            }

            labels.Add($"FK{i + 1}");
        }

        return foreignKeyLabels.ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value));
    }

    /// <summary>キー列の表示ラベルを返す（PK / FK / PK/FK の組み合わせを表現する）</summary>
    private static string GetKeyLabel(ColumnViewModel column, string? foreignKeyLabel)
    {
        if (column.IsPrimaryKey && !string.IsNullOrWhiteSpace(foreignKeyLabel))
        {
            return $"PK/{foreignKeyLabel}";
        }

        if (column.IsPrimaryKey)
        {
            return "PK";
        }

        if (!string.IsNullOrWhiteSpace(foreignKeyLabel))
        {
            return foreignKeyLabel;
        }

        if (column.IsForeignKey)
        {
            return "FK";
        }

        return string.Empty;
    }

    /// <summary>外部キー列の参照先（テーブル.カラム）を重複なく連結した文字列を返す</summary>
    private static string GetReferenceText(EntityViewModel entity, ColumnViewModel column, IReadOnlyList<RelationshipViewModel> relationships)
    {
        var references = relationships
            .Where(relationship => relationship.Target == entity && relationship.TargetColumnId == column.Id)
            .Select(relationship => $"{relationship.Source.TableName}.{GetColumnName(relationship.Source, relationship.SourceColumnId)}")
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(", ", references);
    }

    /// <summary>列 ID からカラム名を解決する（未指定・不一致時は空文字）</summary>
    private static string GetColumnName(EntityViewModel entity, Guid? columnId)
    {
        if (columnId is null)
        {
            return string.Empty;
        }

        return entity.Columns.FirstOrDefault(column => column.Id == columnId)?.Name ?? string.Empty;
    }

    /// <summary>Excel シート名に使えない文字を除去し、31 文字制限と重複回避を考慮した一意な名前を生成する</summary>
    private static string CreateUniqueWorksheetName(string tableName, ISet<string> usedWorksheetNames)
    {
        var sanitized = new string(tableName.Where(ch => !InvalidWorksheetNameChars.Contains(ch)).ToArray()).Trim().Trim('\'');

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
    private sealed record DetailSheetContext(int Number, EntityViewModel Entity, IXLWorksheet Worksheet);
}
