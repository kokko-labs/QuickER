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
    private const double BodyTopMargin = 4;
    private const double BodyBottomMargin = 6;
    private const double RowVerticalMargin = 2;
    private const double ColumnIndicatorWidth = 34;
    private const double ColumnGap = 12;
    private const double NullabilityGap = 8;
    private const double ColumnDescriptionIndent = 34;
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
    private static readonly Lazy<double> DescriptionLineHeight = new(() =>
        MeasureTextHeight("Ag", DescriptionFontSize, fontStyle: FontStyles.Italic)
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
    ) => CalculateCardLayout(entity, showDescriptions, isCompactView).TotalHeight;

    /// <summary>カード内部のテキスト配置（見出し高さ・各カラム行の上端位置など）を計算する</summary>
    /// <remarks>
    /// キャンバス XAML と同じ余白・フォント計測で配置を求める共通ロジック
    /// リレーション線の接続位置の基礎となる <see cref="EstimateEntityHeight"/> と
    /// SVG エクスポートが同一の計算を共有することで、線とカードのズレを防ぐ
    /// </remarks>
    public static EntityCardLayout CalculateCardLayout(
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

        var titleTop = HeaderVerticalPadding / 2;
        var headerDescriptionTop = titleTop + titleHeight;
        var headerDescriptionHeight = 0d;

        if (showDescriptions && !string.IsNullOrWhiteSpace(entity.Description))
        {
            headerDescriptionHeight = MeasureWrappedTextHeight(
                entity.Description,
                DescriptionFontSize,
                headerTextWidth,
                fontStyle: FontStyles.Italic
            );
        }

        var headerHeight = HeaderVerticalPadding + titleHeight + headerDescriptionHeight;

        // 本文は「上余白 → (行余白 → カラム行 → 説明 → 行余白) × 行数 → 下余白」の順に積み上げる
        var rows = new List<EntityCardRowLayout>();
        var y = headerHeight + BodyTopMargin;

        foreach (var column in entity.Columns)
        {
            // 簡易表示中は PK/FK 以外のカラム行を表示しないため、配置からも除外する
            if (isCompactView && !column.IsPrimaryKey && !column.IsForeignKey)
            {
                continue;
            }

            var textTop = y + RowVerticalMargin;
            var descriptionTop = textTop + rowHeight;
            var descriptionHeight = 0d;

            if (showDescriptions && !string.IsNullOrWhiteSpace(column.Description))
            {
                descriptionHeight = MeasureWrappedTextHeight(
                    column.Description,
                    DescriptionFontSize,
                    columnDescriptionWidth,
                    fontStyle: FontStyles.Italic
                );
            }

            rows.Add(
                new EntityCardRowLayout(
                    column,
                    textTop,
                    descriptionTop,
                    descriptionHeight,
                    columnDescriptionWidth
                )
            );
            y = descriptionTop + descriptionHeight + RowVerticalMargin;
        }

        return new EntityCardLayout(
            headerHeight,
            titleTop,
            headerDescriptionTop,
            headerDescriptionHeight,
            headerTextWidth,
            rowHeight,
            DescriptionLineHeight.Value,
            rows,
            Math.Ceiling(y + BodyBottomMargin)
        );
    }

    /// <summary>説明テキストを指定幅で折り返した行のリストを返す（SVG など自動折返しのない出力用）</summary>
    /// <remarks>
    /// WPF の TextBlock 折返しの近似。日本語などは文字単位、英単語は行内最後の空白位置を優先して折り返す
    /// 行数が実描画とわずかに異なる可能性はあるが、高さ計算には
    /// <see cref="MeasureWrappedTextHeight"/> と同じ実測値を用いるため図形の位置はずれない
    /// </remarks>
    public static IReadOnlyList<string> WrapDescription(string? text, double maxWidth)
    {
        var lines = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return lines;
        }

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var start = 0;

            while (start < paragraph.Length)
            {
                var remaining = paragraph.Length - start;

                // 幅に収まる最長の文字数を二分探索で求める（最低 1 文字は進める）
                var low = 1;
                var high = remaining;
                var fit = 1;

                while (low <= high)
                {
                    var mid = (low + high) / 2;
                    var candidateWidth = MeasureTextWidth(
                        paragraph.Substring(start, mid),
                        DescriptionFontSize,
                        fontStyle: FontStyles.Italic
                    );

                    if (candidateWidth <= maxWidth)
                    {
                        fit = mid;
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }

                if (fit >= remaining)
                {
                    lines.Add(paragraph[start..]);
                    break;
                }

                // 英単語の途中で切れる場合は、行内最後の空白まで戻して折り返す
                var cut = fit;

                if (
                    IsWordCharacter(paragraph[start + fit - 1])
                    && IsWordCharacter(paragraph[start + fit])
                )
                {
                    var lastSpace = paragraph.LastIndexOf(' ', start + fit - 1, fit);

                    if (lastSpace > start)
                    {
                        cut = lastSpace - start;
                    }
                }

                lines.Add(paragraph.Substring(start, cut).TrimEnd());
                start += cut;

                // 折返し位置直後の空白は次行頭へ持ち越さない
                while (start < paragraph.Length && paragraph[start] == ' ')
                {
                    start++;
                }
            }
        }

        return lines;
    }

    /// <summary>単語の途中判定に用いる（ASCII 英数字のみを単語構成文字とみなす）</summary>
    private static bool IsWordCharacter(char c) => char.IsAsciiLetterOrDigit(c);

    /// <summary>本文フォント（カラム名・型と同じ設定）でのテキスト描画幅を返す（SVG 出力の配置計算用）</summary>
    public static double MeasureBodyTextWidth(string? text) => MeasureTextWidth(text, BodyFontSize);

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

/// <summary>エンティティカード内部の配置情報（キャンバス描画と同一の余白・フォント計測に基づく）</summary>
/// <param name="HeaderHeight">見出し帯の高さ（テーブル説明の表示分を含む）</param>
/// <param name="TitleTop">テーブル名テキストの上端 Y（カード左上原点）</param>
/// <param name="HeaderDescriptionTop">テーブル説明ブロックの上端 Y</param>
/// <param name="HeaderDescriptionHeight">テーブル説明ブロックの高さ（非表示時は 0）</param>
/// <param name="HeaderDescriptionWidth">テーブル説明の折返し幅</param>
/// <param name="RowHeight">カラム行 1 行のテキスト高さ</param>
/// <param name="DescriptionLineHeight">説明テキスト 1 行の高さ</param>
/// <param name="Rows">表示対象カラム行の配置（簡易表示中は PK/FK のみ）</param>
/// <param name="TotalHeight">カード全体の高さ（<see cref="EntityViewModel.DisplayHeight"/> と一致する）</param>
public sealed record EntityCardLayout(
    double HeaderHeight,
    double TitleTop,
    double HeaderDescriptionTop,
    double HeaderDescriptionHeight,
    double HeaderDescriptionWidth,
    double RowHeight,
    double DescriptionLineHeight,
    IReadOnlyList<EntityCardRowLayout> Rows,
    double TotalHeight
);

/// <summary>カード内の 1 カラム行の配置情報</summary>
/// <param name="Column">対象カラム</param>
/// <param name="TextTop">カラム行テキストの上端 Y（カード左上原点）</param>
/// <param name="DescriptionTop">カラム説明ブロックの上端 Y</param>
/// <param name="DescriptionHeight">カラム説明ブロックの高さ（非表示時は 0）</param>
/// <param name="DescriptionWidth">カラム説明の折返し幅</param>
public sealed record EntityCardRowLayout(
    ColumnViewModel Column,
    double TextTop,
    double DescriptionTop,
    double DescriptionHeight,
    double DescriptionWidth
);
