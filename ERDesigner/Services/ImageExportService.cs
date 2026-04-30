using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>
/// ER 図のキャンバスを画像 (PNG) または SVG として書き出すサービスです。
/// </summary>
public static class ImageExportService
{
    /// <summary>WPF の <see cref="Visual"/> を PNG ファイルに書き出します。</summary>
    /// <param name="visual">レンダリング対象の Visual（通常はキャンバス Grid）。</param>
    /// <param name="path">出力先パス。</param>
    /// <param name="width">出力幅 (px)。0 以下なら Visual のサイズを使用。</param>
    /// <param name="height">出力高 (px)。0 以下なら Visual のサイズを使用。</param>
    public static void ExportPng(Visual visual, string path, double width = 0, double height = 0)
    {
        var bounds = VisualTreeHelper.GetDescendantBounds(visual);
        if (width <= 0) width = double.IsFinite(bounds.Width) && bounds.Width > 0 ? bounds.Width : 800;
        if (height <= 0) height = double.IsFinite(bounds.Height) && bounds.Height > 0 ? bounds.Height : 600;

        var rtb = new RenderTargetBitmap(
            (int)System.Math.Ceiling(width),
            (int)System.Math.Ceiling(height),
            96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>
    /// <see cref="MainViewModel"/> の現在の状態から SVG ファイルを書き出します。
    /// </summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/>。</param>
    /// <param name="path">出力先パス。</param>
    public static void ExportSvg(MainViewModel vm, string path)
        => File.WriteAllText(path, BuildSvg(vm), Encoding.UTF8);

    /// <summary>SVG 文字列を生成します。テスト用に公開しています。</summary>
    public static string BuildSvg(MainViewModel vm)
    {
        // 簡易にエンティティ高さを推定
        const double rowHeight = 18;
        const double headerHeight = 28;
        const double padding = 30;

        double Height(EntityViewModel e) => headerHeight + System.Math.Max(1, e.Columns.Count) * rowHeight + 8;

        double maxX = 400, maxY = 300;
        foreach (var e in vm.Entities)
        {
            maxX = System.Math.Max(maxX, e.X + e.Width + padding);
            maxY = System.Math.Max(maxY, e.Y + Height(e) + padding);
        }

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{maxX.ToString(ci)}\" height=\"{maxY.ToString(ci)}\" viewBox=\"0 0 {maxX.ToString(ci)} {maxY.ToString(ci)}\">");
        sb.AppendLine("  <style>.entity{fill:#fff;stroke:#9DB7DD;stroke-width:1}.header{fill:#DCEBFF}.title{font:bold 12px 'Segoe UI',sans-serif;fill:#1F3A66}.col{font:11px 'Segoe UI',sans-serif;fill:#1F2937}.pk{fill:#D93025;font-weight:bold}.fk{fill:#1A73E8;font-weight:bold}.rel{stroke:#5F6B7A;stroke-width:1.6;fill:none}.label{font:10px 'Segoe UI',sans-serif;fill:#374151}</style>");

        // リレーション
        foreach (var r in vm.Relationships)
        {
            sb.AppendLine($"  <line class=\"rel\" x1=\"{r.X1.ToString(ci)}\" y1=\"{r.Y1.ToString(ci)}\" x2=\"{r.X2.ToString(ci)}\" y2=\"{r.Y2.ToString(ci)}\" />");
            sb.AppendLine($"  <text class=\"label\" x=\"{r.LabelX.ToString(ci)}\" y=\"{r.LabelY.ToString(ci)}\" text-anchor=\"middle\">{System.Security.SecurityElement.Escape(r.Label)}</text>");
        }

        // エンティティ
        foreach (var e in vm.Entities)
        {
            var h = Height(e);
            sb.AppendLine($"  <g transform=\"translate({e.X.ToString(ci)},{e.Y.ToString(ci)})\">");
            sb.AppendLine($"    <rect class=\"entity\" width=\"{e.Width.ToString(ci)}\" height=\"{h.ToString(ci)}\" rx=\"6\" ry=\"6\" />");
            sb.AppendLine($"    <rect class=\"header\" width=\"{e.Width.ToString(ci)}\" height=\"{headerHeight.ToString(ci)}\" rx=\"6\" ry=\"6\" />");
            sb.AppendLine($"    <text class=\"title\" x=\"10\" y=\"18\">{System.Security.SecurityElement.Escape($"{e.DisplayName} ({e.TableName})")}</text>");
            for (int i = 0; i < e.Columns.Count; i++)
            {
                var c = e.Columns[i];
                var y = headerHeight + 14 + i * rowHeight;
                var marker = c.IsPrimaryKey ? "PK" : c.IsForeignKey ? "FK" : "";
                var markerClass = c.IsPrimaryKey ? "pk" : c.IsForeignKey ? "fk" : "col";
                sb.AppendLine($"    <text class=\"{markerClass}\" x=\"10\" y=\"{y.ToString(ci)}\">{marker}</text>");
                sb.AppendLine($"    <text class=\"col\" x=\"40\" y=\"{y.ToString(ci)}\">{System.Security.SecurityElement.Escape(c.Name)}</text>");
                sb.AppendLine($"    <text class=\"col\" x=\"{(e.Width - 8).ToString(ci)}\" y=\"{y.ToString(ci)}\" text-anchor=\"end\">{System.Security.SecurityElement.Escape(c.DataType)}</text>");
            }
            sb.AppendLine("  </g>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
