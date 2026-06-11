using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>
/// ER 図の表示幅・表示高さを見積もる共通サービスです。
/// </summary>
public static class DiagramMetricsService
{
    private const double DefaultEntityWidth = 200;
    private const double MinEntityWidth = 120;
    private const double HeaderHorizontalPadding = 20;
    private const double HeaderVerticalPadding = 12;
    private const double BodyHorizontalMargin = 12;
    private const double BodyVerticalMargin = 10;
    private const double ColumnIndicatorWidth = 34;
    private const double ColumnGap = 12;
    private const double NullabilityGap = 8;
    private const double ColumnDescriptionIndent = 34;
    private const double ColumnRowMargin = 4;
    private const double TitleFontSize = 13;
    private const double BodyFontSize = 13;
    private const double DescriptionFontSize = 11;

    private static readonly FontFamily DefaultFontFamily = new("Segoe UI");

    // 行高さはフォント設定にのみ依存する定数のため、1回だけ計測してキャッシュする。
    // (FormattedText の生成は高コストで、ドラッグ中など高頻度に呼ばれるため)
    private static readonly Lazy<double> TitleLineHeight = new(() => MeasureTextHeight("Ag", TitleFontSize, FontWeights.SemiBold));
    private static readonly Lazy<double> BodyLineHeight = new(() => MeasureTextHeight("Ag", BodyFontSize));

    /// <summary>
    /// カラム名と型が重ならないように、内容からエンティティ幅を自動計算します。
    /// </summary>
    public static double CalculateAutoWidth(EntityViewModel entity)
    {
        var headerWidth = HeaderHorizontalPadding + MeasureTextWidth(entity.TableName, TitleFontSize, FontWeights.SemiBold);
        var bodyWidth =
            entity.Columns.Count == 0
                ? DefaultEntityWidth
                : entity.Columns.Max(column =>
                    BodyHorizontalMargin
                    + ColumnIndicatorWidth
                    + MeasureTextWidth(column.Name, BodyFontSize)
                    + (entity.ShowNullabilityInDiagram ? ColumnGap + MeasureTextWidth(column.IsNullable ? "NULL" : "NOT NULL", BodyFontSize) + NullabilityGap : 0)
                    + ColumnGap
                    + MeasureTextWidth(column.DataType, BodyFontSize)
                    + 4
                );

        return Math.Max(DefaultEntityWidth, Math.Ceiling(Math.Max(headerWidth, bodyWidth)));
    }

    /// <summary>
    /// 現在の表示状態に応じたエンティティの表示高さを見積もります。
    /// </summary>
    public static double EstimateEntityHeight(EntityViewModel entity, bool showDescriptions)
    {
        var width = Math.Max(MinEntityWidth, entity.Width);
        var headerTextWidth = Math.Max(1, width - HeaderHorizontalPadding);
        var bodyTextWidth = Math.Max(1, width - BodyHorizontalMargin);
        var columnDescriptionWidth = Math.Max(1, bodyTextWidth - ColumnDescriptionIndent);
        var titleHeight = TitleLineHeight.Value;
        var rowHeight = BodyLineHeight.Value;

        var headerHeight = HeaderVerticalPadding + titleHeight;

        if (showDescriptions && !string.IsNullOrWhiteSpace(entity.Description))
        {
            headerHeight += MeasureWrappedTextHeight(entity.Description, DescriptionFontSize, headerTextWidth, fontStyle: FontStyles.Italic);
        }

        var bodyHeight = BodyVerticalMargin;

        foreach (var column in entity.Columns)
        {
            bodyHeight += ColumnRowMargin + rowHeight;

            if (showDescriptions && !string.IsNullOrWhiteSpace(column.Description))
            {
                bodyHeight += MeasureWrappedTextHeight(column.Description, DescriptionFontSize, columnDescriptionWidth, fontStyle: FontStyles.Italic);
            }
        }

        return Math.Ceiling(headerHeight + bodyHeight);
    }

    private static double MeasureTextWidth(string? text, double fontSize, FontWeight? fontWeight = null, FontStyle? fontStyle = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return CreateFormattedText(text, fontSize, fontWeight ?? FontWeights.Normal, fontStyle ?? FontStyles.Normal).WidthIncludingTrailingWhitespace;
    }

    private static double MeasureTextHeight(string text, double fontSize, FontWeight? fontWeight = null, FontStyle? fontStyle = null) =>
        CreateFormattedText(text, fontSize, fontWeight ?? FontWeights.Normal, fontStyle ?? FontStyles.Normal).Height;

    private static double MeasureWrappedTextHeight(string text, double fontSize, double maxWidth, FontWeight? fontWeight = null, FontStyle? fontStyle = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var formatted = CreateFormattedText(text, fontSize, fontWeight ?? FontWeights.Normal, fontStyle ?? FontStyles.Normal);
        formatted.MaxTextWidth = Math.Max(1, maxWidth);
        return formatted.Height;
    }

    private static FormattedText CreateFormattedText(string text, double fontSize, FontWeight fontWeight, FontStyle fontStyle) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(DefaultFontFamily, fontStyle, fontWeight, FontStretches.Normal),
            fontSize,
            Brushes.Black,
            1.0
        );
}
