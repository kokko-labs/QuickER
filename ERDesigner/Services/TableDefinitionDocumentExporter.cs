using ClosedXML.Excel;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>
/// ER 図のテーブル定義から Excel 形式のテーブル定義書をゼロから生成するサービスです。
/// </summary>
public static class TableDefinitionDocumentExporter
{
    private const string SummarySheetName = "テーブル一覧";
    private const string RelationshipSheetName = "リレーション一覧";
    private const string DetailLinkText = "詳細";
    private const string BackToSummaryText = "テーブル一覧に戻る";
    private const string DefaultFontName = "游ゴシック";
    private const double DefaultFontSize = 11;
    private const double DefaultRowHeight = 18.75;
    private static readonly char[] InvalidWorksheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];

    /// <summary>現在のダイアグラムからテーブル定義書の Excel ブックを生成します。</summary>
    /// <param name="vm">対象の <see cref="MainViewModel" />。</param>
    /// <returns>生成済みの Excel ブック。</returns>
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

    /// <summary>テーブル定義書を Excel ファイルとして保存します。</summary>
    /// <param name="vm">対象の <see cref="MainViewModel" />。</param>
    /// <param name="path">出力先ファイルパス。</param>
    public static void SaveTo(MainViewModel vm, string path)
    {
        using var workbook = BuildWorkbook(vm);
        workbook.SaveAs(path);
    }

    /// <summary>ブック全体の基本書式を設定します。</summary>
    private static void ApplyWorkbookStyle(XLWorkbook workbook)
    {
        workbook.Style.Font.FontName = DefaultFontName;
        workbook.Style.Font.FontSize = DefaultFontSize;
        workbook.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>テーブル一覧シートを生成します。</summary>
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
            ApplyHyperlinkStyle(worksheet.Cell(row, 2), detailSheet.Worksheet.Name);
        }

        UpdatePrintArea(worksheet, $"A1:E{Math.Max(headerRow, dataStartRow + detailSheets.Count - 1)}");
    }

    /// <summary>リレーション一覧シートを生成します。</summary>
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
            worksheet.Cell(row, 3).Value = relationship.Source.TableName;
            worksheet.Cell(row, 4).Value = GetColumnName(relationship.Source, relationship.SourceColumnId);
            worksheet.Cell(row, 5).Value = relationship.Target.TableName;
            worksheet.Cell(row, 6).Value = GetColumnName(relationship.Target, relationship.TargetColumnId);
            worksheet.Cell(row, 7).Value = GetRelationshipTypeLabel(relationship.Type);
        }

        UpdatePrintArea(worksheet, $"A1:J{Math.Max(headerRow, dataStartRow + orderedRelationships.Count - 1)}");
    }

    /// <summary>テーブル単位の定義書シートを生成します。</summary>
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
        ApplyHyperlinkStyle(worksheet.Cell(footerRow, 1), SummarySheetName);
        UpdatePrintArea(worksheet, $"A1:G{footerRow}");
    }

    /// <summary>テーブル一覧シートの固定書式を設定します。</summary>
    private static void ConfigureSummaryWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Column(1).Width = 5;
        worksheet.Column(2).Width = 6;
        worksheet.Column(3).Width = 35.7109375;
        worksheet.Column(4).Width = 35.7109375;
        worksheet.Column(5).Width = 50.7109375;
        ConfigureWorksheet(worksheet);
    }

    /// <summary>リレーション一覧シートの固定書式を設定します。</summary>
    private static void ConfigureRelationshipWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Column(1).Width = 5;
        worksheet.Column(2).Width = 50.7109375;

        for (var column = 3; column <= 6; column++)
        {
            worksheet.Column(column).Width = 30.7109375;
        }

        worksheet.Column(7).Width = 10.7109375;
        worksheet.Column(8).Width = 14.7109375;
        worksheet.Column(9).Width = 14.7109375;
        worksheet.Column(10).Width = 50.7109375;
        ConfigureWorksheet(worksheet);
    }

    /// <summary>詳細シートの固定書式を設定します。</summary>
    private static void ConfigureDetailWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Column(1).Width = 5.7109375;
        worksheet.Column(2).Width = 30.7109375;
        worksheet.Column(3).Width = 30.7109375;
        worksheet.Column(4).Width = 17.7109375;
        worksheet.Column(5).Width = 8.7109375;
        worksheet.Column(6).Width = 8.7109375;
        worksheet.Column(7).Width = 40.7109375;
        worksheet.Column(8).Width = 40.7109375;
        ConfigureWorksheet(worksheet);
    }

    /// <summary>各シート共通の設定を行います。</summary>
    private static void ConfigureWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Style.Font.FontName = DefaultFontName;
        worksheet.Style.Font.FontSize = DefaultFontSize;
        worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
    }

    /// <summary>見出し行の書式を適用します。</summary>
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

    /// <summary>データ行の書式を適用します。</summary>
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

    /// <summary>詳細リンクの書式とリンク先を設定します。</summary>
    private static void ApplyHyperlinkStyle(IXLCell cell, string targetSheetName)
    {
        cell.SetHyperlink(new XLHyperlink($"{EscapeSheetName(targetSheetName)}!A1"));
        cell.Style.Font.FontName = DefaultFontName;
        cell.Style.Font.FontSize = DefaultFontSize;
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
        cell.Style.Font.FontColor = XLColor.Blue;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    /// <summary>フッターの戻りリンク書式を適用します。</summary>
    private static void ApplyFooterHyperlinkStyle(IXLCell cell)
    {
        cell.Style.Font.FontName = DefaultFontName;
        cell.Style.Font.FontSize = DefaultFontSize;
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
        cell.Style.Font.FontColor = XLColor.Blue;
    }

    /// <summary>印刷範囲を設定します。</summary>
    private static void UpdatePrintArea(IXLWorksheet worksheet, string address)
    {
        worksheet.PageSetup.PrintAreas.Clear();
        worksheet.PageSetup.PrintAreas.Add(address);
    }

    /// <summary>関連種別を定義書向けの表記に変換します。</summary>
    private static string GetRelationshipTypeLabel(RelationshipType type) =>
        type switch
        {
            RelationshipType.OneToOne => "1:1",
            RelationshipType.OneToMany => "N:1",
            RelationshipType.ManyToMany => "N:N",
            _ => type.ToString(),
        };

    /// <summary>テーブル内の FK に連番を振った表示ラベルを構築します。</summary>
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

    /// <summary>キー列の表示ラベルを返します。</summary>
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

    /// <summary>外部キー列の参照先文字列を返します。</summary>
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

    /// <summary>列 ID からカラム名を解決します。</summary>
    private static string GetColumnName(EntityViewModel entity, Guid? columnId)
    {
        if (columnId is null)
        {
            return string.Empty;
        }

        return entity.Columns.FirstOrDefault(column => column.Id == columnId)?.Name ?? string.Empty;
    }

    /// <summary>Excel シート名に使えない文字を除去し、31 文字制限も考慮した名前を返します。</summary>
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

    /// <summary>ハイパーリンク用にシート名をエスケープします。</summary>
    private static string EscapeSheetName(string sheetName) => $"'{sheetName.Replace("'", "''")}'";

    /// <summary>詳細シート生成時に必要な情報をまとめます。</summary>
    private sealed record DetailSheetContext(int Number, EntityViewModel Entity, IXLWorksheet Worksheet);
}
