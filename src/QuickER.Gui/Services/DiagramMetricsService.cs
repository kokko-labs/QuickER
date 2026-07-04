using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>ER 図エンティティの表示幅・表示高さを見積もる共通サービス</summary>
/// <remarks>
/// WPF の <see cref="FormattedText"/> でテキスト寸法を実測し、レイアウト前に図形サイズを確定する
/// </remarks>
public static class DiagramMetricsService
{
    // 以下はエンティティ描画の余白・間隔・フォントサイズの定数（描画 XAML と整合させる）
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

    /// <summary>寸法計測に用いる既定フォントファミリー</summary>
    private static readonly FontFamily DefaultFontFamily = new("Segoe UI");

    // 行高さはフォント設定のみに依存する定数のため、初回計測値を遅延キャッシュする
    // FormattedText 生成は高コストで、ドラッグ中など高頻度に呼ばれるため再計測を避ける
    private static readonly Lazy<double> TitleLineHeight = new(() =>
        MeasureTextHeight("Ag", TitleFontSize, FontWeights.SemiBold)
    );
    private static readonly Lazy<double> BodyLineHeight = new(() =>
        MeasureTextHeight("Ag", BodyFontSize)
    );

    /// <summary>カラム名と型が重ならないよう、内容からエンティティ幅を自動計算する</summary>
    public static double CalculateAutoWidth(EntityViewModel entity)
    {
        var headerWidth =
            HeaderHorizontalPadding
            + MeasureTextWidth(entity.TableName, TitleFontSize, FontWeights.SemiBold);
        var bodyWidth =
            entity.Columns.Count == 0
                ? DefaultEntityWidth
                : entity.Columns.Max(column =>
                    BodyHorizontalMargin
                    + ColumnIndicatorWidth
                    + MeasureTextWidth(column.Name, BodyFontSize)
                    + (
                        entity.ShowNullabilityInDiagram
                            ? ColumnGap
                                + MeasureTextWidth(
                                    column.IsNullable ? "NULL" : "NOT NULL",
                                    BodyFontSize
                                )
                                + NullabilityGap
                            : 0
                    )
                    + ColumnGap
                    + MeasureTextWidth(column.DataType, BodyFontSize)
                    + 4
                );

        return Math.Max(DefaultEntityWidth, Math.Ceiling(Math.Max(headerWidth, bodyWidth)));
    }

    /// <summary>現在の表示状態（説明表示・簡易表示の有無）に応じたエンティティの表示高さを見積もる</summary>
    /// <remarks>
    /// 簡易表示（<paramref name="isCompactView"/>）が有効なときは PK/FK カラムのみを行として数える
    /// これによりカードが縦に縮み、リレーション線の接続位置（<see cref="EntityViewModel.DisplayHeight"/> 依存）が可視カラムに整合する
    /// </remarks>
    public static double EstimateEntityHeight(
        EntityViewModel entity,
        bool showDescriptions,
        bool isCompactView = false
    )
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
            headerHeight += MeasureWrappedTextHeight(
                entity.Description,
                DescriptionFontSize,
                headerTextWidth,
                fontStyle: FontStyles.Italic
            );
        }

        var bodyHeight = BodyVerticalMargin;

        foreach (var column in entity.Columns)
        {
            // 簡易表示中は PK/FK 以外のカラム行を表示しないため、高さの見積もりからも除外する
            if (isCompactView && !column.IsPrimaryKey && !column.IsForeignKey)
            {
                continue;
            }

            bodyHeight += ColumnRowMargin + rowHeight;

            if (showDescriptions && !string.IsNullOrWhiteSpace(column.Description))
            {
                bodyHeight += MeasureWrappedTextHeight(
                    column.Description,
                    DescriptionFontSize,
                    columnDescriptionWidth,
                    fontStyle: FontStyles.Italic
                );
            }
        }

        return Math.Ceiling(headerHeight + bodyHeight);
    }

    /// <summary>1 行テキストの描画幅を計測する（末尾空白を含む）</summary>
    private static double MeasureTextWidth(
        string? text,
        double fontSize,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return CreateFormattedText(
            text,
            fontSize,
            fontWeight ?? FontWeights.Normal,
            fontStyle ?? FontStyles.Normal
        ).WidthIncludingTrailingWhitespace;
    }

    /// <summary>1 行テキストの描画高さを計測する</summary>
    private static double MeasureTextHeight(
        string text,
        double fontSize,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null
    ) =>
        CreateFormattedText(
            text,
            fontSize,
            fontWeight ?? FontWeights.Normal,
            fontStyle ?? FontStyles.Normal
        ).Height;

    /// <summary>指定幅で折り返した場合のテキスト描画高さを計測する</summary>
    private static double MeasureWrappedTextHeight(
        string text,
        double fontSize,
        double maxWidth,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var formatted = CreateFormattedText(
            text,
            fontSize,
            fontWeight ?? FontWeights.Normal,
            fontStyle ?? FontStyles.Normal
        );
        formatted.MaxTextWidth = Math.Max(1, maxWidth);
        return formatted.Height;
    }

    /// <summary>計測用の <see cref="FormattedText"/> を生成する</summary>
    private static FormattedText CreateFormattedText(
        string text,
        double fontSize,
        FontWeight fontWeight,
        FontStyle fontStyle
    ) =>
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
