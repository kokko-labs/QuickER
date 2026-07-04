using System.Globalization;
using System.Windows;
using System.Windows.Media;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>ER 図全体を <see cref="MainViewModel"/> から直接ベクタ描画する印刷用レンダラ</summary>
/// <remarks>
/// SVG 出力（<see cref="ImageExportService.BuildSvg"/>）と同じ描画内容を
/// <see cref="DrawingContext"/> へ直接描く（影・選択強調・鳥足マーカーなし。リレーションは線＋ラベル）。
/// レイアウトは SVG 出力と同一の <see cref="DiagramMetricsService.CalculateCardLayout"/> を共有し、
/// 文字は GlyphRun として XPS/PDF にベクタ保持されるため、拡大しても鮮明でファイルも小さい。
/// VisualBrush・Effect・ビットマップを使わないことでラスタライズを一切発生させない
/// </remarks>
public static class DiagramVectorRenderer
{
    // フォントサイズ（BuildSvg の style 定義と同一）
    private const double TitleFontSize = 13;
    private const double BodyFontSize = 13;
    private const double DescriptionFontSize = 11;
    private const double RelationLabelFontSize = 10;

    // 図の外接矩形の周囲に確保するパディング（px）
    private const double BoundsPadding = 20;

    /// <summary>計測（DiagramMetricsService）と同一のフォントファミリー。ピクセル単位のズレを防ぐ</summary>
    private static readonly FontFamily DiagramFontFamily = new("Segoe UI");

    // 書体（BuildSvg の .title / .col / .pk・.fk / .desc / .label に対応）
    private static readonly Typeface TitleTypeface = new(
        DiagramFontFamily,
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal
    );
    private static readonly Typeface BodyTypeface = new(
        DiagramFontFamily,
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal
    );
    private static readonly Typeface KeyIndicatorTypeface = new(
        DiagramFontFamily,
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal
    );
    private static readonly Typeface DescriptionTypeface = new(
        DiagramFontFamily,
        FontStyles.Italic,
        FontWeights.Normal,
        FontStretches.Normal
    );

    // 配色（BuildSvg の style 定義と同一）
    private static readonly Brush TitleBrush = CreateFrozenBrush("#1F2937");
    private static readonly Brush BodyBrush = CreateFrozenBrush("#1F2937");
    private static readonly Brush MetaBrush = CreateFrozenBrush("#6B7280");
    private static readonly Brush DescriptionBrush = CreateFrozenBrush("#6B7280");
    private static readonly Brush PrimaryKeyBrush = CreateFrozenBrush("#D93025");
    private static readonly Brush ForeignKeyBrush = CreateFrozenBrush("#1A73E8");
    private static readonly Brush RelationLabelBrush = CreateFrozenBrush("#374151");
    private static readonly Pen EntityBorderPen = CreateFrozenPen("#9DB7DD", 1);
    private static readonly Pen RelationPen = CreateFrozenPen("#5F6B7A", 1.6);

    /// <summary>全エンティティの外接矩形に周囲パディングを加えた図全体の範囲を求める</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/></param>
    /// <returns>図全体の範囲（エンティティ 0 件なら 800x600 の既定矩形）</returns>
    public static Rect CalculateDiagramBounds(MainViewModel vm)
    {
        if (vm.Entities.Count == 0)
        {
            return new Rect(0, 0, 800, 600);
        }

        double minX = double.MaxValue,
            minY = double.MaxValue,
            maxX = double.MinValue,
            maxY = double.MinValue;

        foreach (var entity in vm.Entities)
        {
            minX = Math.Min(minX, entity.X);
            minY = Math.Min(minY, entity.Y);
            maxX = Math.Max(maxX, entity.X + entity.Width);
            maxY = Math.Max(maxY, entity.Y + entity.DisplayHeight);
        }

        return new Rect(
            minX - BoundsPadding,
            minY - BoundsPadding,
            maxX - minX + BoundsPadding * 2,
            maxY - minY + BoundsPadding * 2
        );
    }

    /// <summary>図全体を図座標系（キャンバス座標そのまま）でベクタ描画した DrawingVisual を返す</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/></param>
    public static DrawingVisual RenderDiagram(MainViewModel vm)
    {
        var visual = new DrawingVisual();

        using var dc = visual.RenderOpen();

        // 背景（透明のままだと PDF ビューアのダークモードで黒く見えるため白で塗る）
        dc.DrawRectangle(Brushes.White, null, CalculateDiagramBounds(vm));

        // リレーション（線＋ラベル。カードの下に描くためエンティティより先）
        foreach (var relationship in vm.Relationships)
        {
            dc.DrawLine(
                RelationPen,
                new Point(relationship.X1, relationship.Y1),
                new Point(relationship.X2, relationship.Y2)
            );

            // BuildSvg の text-anchor="middle"（x 中央・y ベースライン）と同じ位置に描く
            var label = CreateText(
                relationship.Label,
                RelationLabelFontSize,
                BodyTypeface,
                RelationLabelBrush
            );
            label.TextAlignment = TextAlignment.Center;
            dc.DrawText(
                label,
                new Point(relationship.LabelX, relationship.LabelY - label.Baseline)
            );
        }

        // エンティティ
        foreach (var entity in vm.Entities)
        {
            dc.PushTransform(new TranslateTransform(entity.X, entity.Y));
            DrawEntityCard(dc, entity);
            dc.Pop();
        }

        return visual;
    }

    /// <summary>エンティティカード 1 枚をカード左上原点で描画する（BuildSvg のエンティティ出力を鏡写し）</summary>
    private static void DrawEntityCard(DrawingContext dc, EntityViewModel entity)
    {
        var layout = DiagramMetricsService.CalculateCardLayout(
            entity,
            entity.ShowDescriptionsInDiagram,
            entity.IsCompactView
        );
        var w = entity.Width;

        // カード枠（白地・角丸 6・枠 #9DB7DD 1px）
        dc.DrawRoundedRectangle(
            Brushes.White,
            EntityBorderPen,
            new Rect(0, 0, w, layout.TotalHeight),
            6,
            6
        );

        // 見出し帯（上側の角のみ丸める。BuildSvg のパスと同一形状）
        var headerBrush = CreateFrozenBrush(
            EntityTitleColorPalette.Normalize(entity.TitleBackgroundColor)
        );
        dc.DrawGeometry(headerBrush, null, CreateHeaderBandGeometry(w, layout.HeaderHeight));

        // タイトル（DrawText は上端基準のため TitleTop をそのまま使う）
        dc.DrawText(
            CreateText(entity.TableName, TitleFontSize, TitleTypeface, TitleBrush),
            new Point(10, layout.TitleTop)
        );

        // テーブル説明（説明表示 ON かつ説明があるときのみ）
        if (layout.HeaderDescriptionHeight > 0)
        {
            DrawDescriptionLines(
                dc,
                entity.Description,
                x: 10,
                top: layout.HeaderDescriptionTop,
                width: layout.HeaderDescriptionWidth,
                lineHeight: layout.DescriptionLineHeight
            );
        }

        // カラム行（簡易表示中は PK/FK のみが layout.Rows に含まれる）
        foreach (var row in layout.Rows)
        {
            var column = row.Column;

            if (column.IsPrimaryKey)
            {
                dc.DrawText(
                    CreateText("PK", BodyFontSize, KeyIndicatorTypeface, PrimaryKeyBrush),
                    new Point(6, row.TextTop)
                );
            }
            else if (column.IsForeignKey)
            {
                dc.DrawText(
                    CreateText("FK", BodyFontSize, KeyIndicatorTypeface, ForeignKeyBrush),
                    new Point(6, row.TextTop)
                );
            }

            dc.DrawText(
                CreateText(column.Name, BodyFontSize, BodyTypeface, BodyBrush),
                new Point(40, row.TextTop)
            );

            // 型は右端へ右詰め、NULL 許容表示はその左隣へ配置する（キャンバスの Grid 列構成と同じ並び）
            var typeRight = w - 7;
            var typeText = CreateText(column.DataType, BodyFontSize, BodyTypeface, MetaBrush);
            dc.DrawText(
                typeText,
                new Point(typeRight - typeText.WidthIncludingTrailingWhitespace, row.TextTop)
            );

            if (entity.ShowNullabilityInDiagram)
            {
                var nullabilityRight =
                    typeRight - DiagramMetricsService.MeasureBodyTextWidth(column.DataType) - 8;
                var nullabilityText = CreateText(
                    column.IsNullable ? "NULL" : "NOT NULL",
                    BodyFontSize,
                    BodyTypeface,
                    MetaBrush
                );
                dc.DrawText(
                    nullabilityText,
                    new Point(
                        nullabilityRight - nullabilityText.WidthIncludingTrailingWhitespace,
                        row.TextTop
                    )
                );
            }

            // カラム説明（説明表示 ON かつ説明があるときのみ）
            if (row.DescriptionHeight > 0)
            {
                DrawDescriptionLines(
                    dc,
                    column.Description,
                    x: 40,
                    top: row.DescriptionTop,
                    width: row.DescriptionWidth,
                    lineHeight: layout.DescriptionLineHeight
                );
            }
        }
    }

    /// <summary>見出し帯の形状（上角のみ丸め）を生成する（BuildSvg の path M0,hh V6 Q0,0 6,0 … と同一）</summary>
    private static StreamGeometry CreateHeaderBandGeometry(double width, double headerHeight)
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, headerHeight), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(0, 6), isStroked: false, isSmoothJoin: false);
            ctx.QuadraticBezierTo(
                new Point(0, 0),
                new Point(6, 0),
                isStroked: false,
                isSmoothJoin: false
            );
            ctx.LineTo(new Point(width - 6, 0), isStroked: false, isSmoothJoin: false);
            ctx.QuadraticBezierTo(
                new Point(width, 0),
                new Point(width, 6),
                isStroked: false,
                isSmoothJoin: false
            );
            ctx.LineTo(new Point(width, headerHeight), isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>説明テキストを指定幅で折り返し、1 行ずつ描画する（BuildSvg の説明出力と同一の折返し）</summary>
    private static void DrawDescriptionLines(
        DrawingContext dc,
        string? text,
        double x,
        double top,
        double width,
        double lineHeight
    )
    {
        var lines = DiagramMetricsService.WrapDescription(text, width);

        for (var i = 0; i < lines.Count; i++)
        {
            dc.DrawText(
                CreateText(lines[i], DescriptionFontSize, DescriptionTypeface, DescriptionBrush),
                new Point(x, top + i * lineHeight)
            );
        }
    }

    /// <summary>描画用の <see cref="FormattedText"/> を生成する（pixelsPerDip は計測と同じ 1.0）</summary>
    private static FormattedText CreateText(
        string? text,
        double fontSize,
        Typeface typeface,
        Brush brush
    ) =>
        new(
            text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            1.0
        );

    /// <summary>16 進カラーコードから凍結済みブラシを生成する</summary>
    private static SolidColorBrush CreateFrozenBrush(string hexColor)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        brush.Freeze();

        return brush;
    }

    /// <summary>16 進カラーコードから凍結済みペンを生成する</summary>
    private static Pen CreateFrozenPen(string hexColor, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(hexColor), thickness);
        pen.Freeze();

        return pen;
    }
}
