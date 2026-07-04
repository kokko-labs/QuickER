using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>ER 図のキャンバスを画像（PNG）または SVG として書き出すサービス</summary>
public static class ImageExportService
{
    /// <summary>WPF の <see cref="Visual"/> を PNG ファイルへ書き出す</summary>
    /// <param name="visual">レンダリング対象の Visual（通常はキャンバス Grid）</param>
    /// <param name="path">出力先パス</param>
    /// <param name="width">出力幅 (px) 0 以下なら Visual のサイズを使用する</param>
    /// <param name="height">出力高 (px) 0 以下なら Visual のサイズを使用する</param>
    public static void ExportPng(Visual visual, string path, double width = 0, double height = 0)
    {
        var bounds = VisualTreeHelper.GetDescendantBounds(visual);

        // サイズ未指定時は実測値、実測不能時は既定サイズ（800x600）へフォールバックする
        if (width <= 0)
        {
            width = double.IsFinite(bounds.Width) && bounds.Width > 0 ? bounds.Width : 800;
        }

        if (height <= 0)
        {
            height = double.IsFinite(bounds.Height) && bounds.Height > 0 ? bounds.Height : 600;
        }

        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(width),
            (int)Math.Ceiling(height),
            96,
            96,
            PixelFormats.Pbgra32
        );

        // 背景が透明のままだとダークモードのビューアで黒く見えるため、白背景を先に敷く
        var background = new DrawingVisual();

        using (var dc = background.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        }

        rtb.Render(background);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary><see cref="MainViewModel"/> の現在状態から SVG ファイルを書き出す</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/></param>
    /// <param name="path">出力先パス</param>
    public static void ExportSvg(MainViewModel vm, string path) =>
        File.WriteAllText(path, BuildSvg(vm), Encoding.UTF8);

    // SVG の text はベースライン Y 指定のため、テキスト上端へフォントサイズ相当のオフセットを加える
    private const double BodyBaselineOffset = 13;
    private const double DescriptionBaselineOffset = 11;

    /// <summary>SVG 文字列を生成する（テスト検証のため公開する）</summary>
    /// <remarks>
    /// エンティティの高さ・行配置はキャンバス描画と同じ
    /// <see cref="DiagramMetricsService.CalculateCardLayout"/> を用いる
    /// リレーション線の端点は <see cref="EntityViewModel.DisplayHeight"/> を基礎に計算されるため、
    /// 同一計算を共有することで線とカード枠のズレを防ぐ
    /// </remarks>
    public static string BuildSvg(MainViewModel vm)
    {
        const double padding = 30;

        double maxX = 400,
            maxY = 300;

        foreach (var e in vm.Entities)
        {
            maxX = Math.Max(maxX, e.X + e.Width + padding);
            maxY = Math.Max(maxY, e.Y + e.DisplayHeight + padding);
        }

        // 小数点記号がロケール依存にならないよう不変カルチャで数値整形する
        var ci = CultureInfo.InvariantCulture;

        string F(double value) => value.ToString("0.##", ci);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        sb.AppendLine(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(maxX)}\" height=\"{F(maxY)}\" viewBox=\"0 0 {F(maxX)} {F(maxY)}\">"
        );

        // フォント・配色はキャンバス XAML（MainWindow.xaml のエンティティテンプレート）と揃える
        sb.AppendLine(
            "  <style>"
                + ".entity{fill:#fff;stroke:#9DB7DD;stroke-width:1}"
                + ".title{font:600 13px 'Segoe UI',sans-serif;fill:#1F2937}"
                + ".col{font:13px 'Segoe UI',sans-serif;fill:#1F2937}"
                + ".meta{font:13px 'Segoe UI',sans-serif;fill:#6B7280}"
                + ".desc{font:italic 11px 'Segoe UI',sans-serif;fill:#6B7280}"
                + ".pk{font:bold 13px 'Segoe UI',sans-serif;fill:#D93025}"
                + ".fk{font:bold 13px 'Segoe UI',sans-serif;fill:#1A73E8}"
                + ".rel{stroke:#5F6B7A;stroke-width:1.6;fill:none}"
                + ".label{font:10px 'Segoe UI',sans-serif;fill:#374151}"
                + "</style>"
        );

        // 背景が透明のままだとダークモードのビューアで黒く見えるため、白背景を最初に敷く
        sb.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#fff\" />");

        // リレーション
        foreach (var r in vm.Relationships)
        {
            sb.AppendLine(
                $"  <line class=\"rel\" x1=\"{F(r.X1)}\" y1=\"{F(r.Y1)}\" x2=\"{F(r.X2)}\" y2=\"{F(r.Y2)}\" />"
            );
            sb.AppendLine(
                $"  <text class=\"label\" x=\"{F(r.LabelX)}\" y=\"{F(r.LabelY)}\" text-anchor=\"middle\">{SecurityElement.Escape(r.Label)}</text>"
            );
        }

        // エンティティ
        foreach (var e in vm.Entities)
        {
            var layout = DiagramMetricsService.CalculateCardLayout(
                e,
                e.ShowDescriptionsInDiagram,
                e.IsCompactView
            );
            var w = e.Width;
            var headerColor = EntityTitleColorPalette.Normalize(e.TitleBackgroundColor);

            sb.AppendLine($"  <g transform=\"translate({F(e.X)},{F(e.Y)})\">");
            sb.AppendLine(
                $"    <rect class=\"entity\" width=\"{F(w)}\" height=\"{F(layout.TotalHeight)}\" rx=\"6\" ry=\"6\" />"
            );

            // 見出し帯（キャンバスと同じく上側の角のみ丸める）
            sb.AppendLine(
                $"    <path d=\"M0,{F(layout.HeaderHeight)} V6 Q0,0 6,0 H{F(w - 6)} Q{F(w)},0 {F(w)},6 V{F(layout.HeaderHeight)} Z\" fill=\"{headerColor}\" />"
            );
            sb.AppendLine(
                $"    <text class=\"title\" x=\"10\" y=\"{F(layout.TitleTop + BodyBaselineOffset)}\">{SecurityElement.Escape(e.TableName)}</text>"
            );

            // テーブル説明（説明表示 ON かつ説明があるときのみ）
            if (layout.HeaderDescriptionHeight > 0)
            {
                AppendDescriptionLines(
                    sb,
                    e.Description,
                    x: 10,
                    top: layout.HeaderDescriptionTop,
                    width: layout.HeaderDescriptionWidth,
                    lineHeight: layout.DescriptionLineHeight,
                    F
                );
            }

            // カラム行（簡易表示中は PK/FK のみが layout.Rows に含まれる）
            foreach (var row in layout.Rows)
            {
                var c = row.Column;
                var baseline = row.TextTop + BodyBaselineOffset;

                if (c.IsPrimaryKey)
                {
                    sb.AppendLine($"    <text class=\"pk\" x=\"6\" y=\"{F(baseline)}\">PK</text>");
                }
                else if (c.IsForeignKey)
                {
                    sb.AppendLine($"    <text class=\"fk\" x=\"6\" y=\"{F(baseline)}\">FK</text>");
                }

                sb.AppendLine(
                    $"    <text class=\"col\" x=\"40\" y=\"{F(baseline)}\">{SecurityElement.Escape(c.Name)}</text>"
                );

                // 型は右端へ右詰め、NULL 許容表示はその左隣へ配置する（キャンバスの Grid 列構成と同じ並び）
                var typeRight = w - 7;
                sb.AppendLine(
                    $"    <text class=\"meta\" x=\"{F(typeRight)}\" y=\"{F(baseline)}\" text-anchor=\"end\">{SecurityElement.Escape(c.DataType)}</text>"
                );

                if (e.ShowNullabilityInDiagram)
                {
                    var nullabilityRight =
                        typeRight - DiagramMetricsService.MeasureBodyTextWidth(c.DataType) - 8;
                    sb.AppendLine(
                        $"    <text class=\"meta\" x=\"{F(nullabilityRight)}\" y=\"{F(baseline)}\" text-anchor=\"end\">{(c.IsNullable ? "NULL" : "NOT NULL")}</text>"
                    );
                }

                // カラム説明（説明表示 ON かつ説明があるときのみ）
                if (row.DescriptionHeight > 0)
                {
                    AppendDescriptionLines(
                        sb,
                        c.Description,
                        x: 40,
                        top: row.DescriptionTop,
                        width: row.DescriptionWidth,
                        lineHeight: layout.DescriptionLineHeight,
                        F
                    );
                }
            }

            sb.AppendLine("  </g>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /// <summary>説明テキストを指定幅で折り返し、1 行ずつ text 要素として追記する</summary>
    private static void AppendDescriptionLines(
        StringBuilder sb,
        string? text,
        double x,
        double top,
        double width,
        double lineHeight,
        Func<double, string> format
    )
    {
        var lines = DiagramMetricsService.WrapDescription(text, width);

        for (var i = 0; i < lines.Count; i++)
        {
            var baseline = top + i * lineHeight + DescriptionBaselineOffset;
            sb.AppendLine(
                $"    <text class=\"desc\" x=\"{format(x)}\" y=\"{format(baseline)}\">{SecurityElement.Escape(lines[i])}</text>"
            );
        }
    }
}
