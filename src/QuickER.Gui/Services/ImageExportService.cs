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
    /// <summary>PNG 出力の既定の総画素数の上限（1 億画素＝Pbgra32 で約 400MB）</summary>
    /// <remarks>
    /// <see cref="RenderTargetBitmap"/> は総画素数が 2^30 を超えると
    /// <see cref="OverflowException"/> を投げる（メモリ不足ではなく確保サイズの計算で溢れる）ため、
    /// 大きな図では「保存できない」ではなく「縮小して保存する」ほうが役に立つ。
    /// 上限自体はその限界より十分低く採る＝1 枚のビットマップに加えて PNG 符号化の作業領域が要る。
    /// </remarks>
    public const long DefaultMaxTotalPixels = 100_000_000;

    /// <summary>PNG 出力の結果（実寸と、実際に書き出した画素サイズ）</summary>
    /// <param name="SourceWidth">縮小しなかった場合の幅 (px)</param>
    /// <param name="SourceHeight">縮小しなかった場合の高さ (px)</param>
    /// <param name="OutputWidth">実際に書き出した幅 (px)</param>
    /// <param name="OutputHeight">実際に書き出した高さ (px)</param>
    /// <remarks>
    /// 実寸を <see cref="long"/> で持つのは、エンティティを極端に遠くへ置いた図で
    /// <see cref="int"/> の範囲を超え得るため（負の値へ化けると上限の判定も素通りする）。
    /// 出力側は上限に収まっているので <see cref="int"/> でよい。
    /// </remarks>
    public readonly record struct PngExportResult(
        long SourceWidth,
        long SourceHeight,
        int OutputWidth,
        int OutputHeight
    )
    {
        /// <summary>上限に収めるため縮小したか</summary>
        public bool WasScaledDown => OutputWidth != SourceWidth || OutputHeight != SourceHeight;
    }

    /// <summary>WPF の <see cref="Visual"/> を PNG ファイルへ書き出す</summary>
    /// <param name="visual">レンダリング対象の Visual（通常はキャンバス Grid）</param>
    /// <param name="path">出力先パス</param>
    /// <param name="width">出力幅 (px) 0 以下なら Visual のサイズを使用する</param>
    /// <param name="height">出力高 (px) 0 以下なら Visual のサイズを使用する</param>
    /// <param name="maxTotalPixels">総画素数の上限（既定は <see cref="DefaultMaxTotalPixels"/>）</param>
    /// <returns>実寸と実際に書き出した画素サイズ（縮小したかの判定に使う）</returns>
    /// <remarks>
    /// 上限を超える図は<b>縦横比を保ったまま縮小して</b>書き出す。判定と縮小は
    /// <see cref="RenderTargetBitmap"/> を作る<b>前</b>に行う＝超えた時点で例外になるため、
    /// 作ってから縮めることはできない。縮小後の画素数は切り捨てで求めるので上限を超えない。
    /// </remarks>
    public static PngExportResult ExportPng(
        Visual visual,
        string path,
        double width = 0,
        double height = 0,
        long maxTotalPixels = DefaultMaxTotalPixels
    )
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

        var sourceWidth = CeilToPixels(width);
        var sourceHeight = CeilToPixels(height);
        var (pixelWidth, pixelHeight) = FitWithinPixelBudget(
            width,
            height,
            sourceWidth,
            sourceHeight,
            maxTotalPixels
        );
        var scaledDown = pixelWidth != sourceWidth || pixelHeight != sourceHeight;

        var (dpiX, dpiY) = ResolveDpi(scaledDown, pixelWidth, pixelHeight, width, height);
        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);

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

        return new PngExportResult(sourceWidth, sourceHeight, pixelWidth, pixelHeight);
    }

    /// <summary>描画に使う DPI を決める</summary>
    /// <remarks>
    /// 画素サイズを縮めるだけでは中身が切り取られるため、縮小したときは DPI で描画側も同じ倍率へ縮める。
    /// <b>縮小しないときは 96 ちょうどを使う</b>＝画素サイズは実寸の切り上げなので、倍率を常に
    /// 「画素数 ÷ 実寸」で求めると 96 からわずかにずれ（例 96.019）、縮小していない図の
    /// 解像度情報と描画倍率が従来の出力から変わってしまう。
    /// </remarks>
    internal static (double DpiX, double DpiY) ResolveDpi(
        bool scaledDown,
        int pixelWidth,
        int pixelHeight,
        double width,
        double height
    ) => scaledDown ? (96 * pixelWidth / width, 96 * pixelHeight / height) : (96, 96);

    /// <summary>画素数として使える範囲（1 以上・<see cref="int"/> 以内）へ切り捨てて収める</summary>
    private static int ClampToPixelCount(double value) =>
        (int)Math.Clamp(Math.Floor(value), 1, int.MaxValue);

    /// <summary>実寸 (px) を切り上げて求める（<see cref="int"/> の範囲を超え得るため <see cref="long"/>）</summary>
    private static long CeilToPixels(double value) =>
        (long)Math.Clamp(Math.Ceiling(value), 1, long.MaxValue);

    /// <summary>総画素数が上限に収まる画素サイズを、縦横比を保ったまま求める</summary>
    /// <remarks>
    /// 総画素数は <see cref="double"/> で比べる。<see cref="long"/> の積はエンティティを極端に
    /// 遠くへ置いた図であふれ得るためで、この判定に <see cref="double"/> の精度で足りる
    /// （上限付近の 1 画素の差は問題にならない）。
    /// </remarks>
    internal static (int Width, int Height) FitWithinPixelBudget(
        double width,
        double height,
        long sourceWidth,
        long sourceHeight,
        long maxTotalPixels
    )
    {
        if ((double)sourceWidth * sourceHeight <= maxTotalPixels)
        {
            // 上限に収まっている＝どちらの辺も上限以下なので int へ収まる
            return ((int)sourceWidth, (int)sourceHeight);
        }

        var scale = Math.Sqrt(maxTotalPixels / ((double)sourceWidth * sourceHeight));

        // 切り捨てで求めるので、上限を超える丸め誤差は出ない（0 px は作れないので下限は 1 px）。
        // int で受ける前に丸めるのは、片辺が極端に長い図では縮小後でも int を超え得るため
        // （その場合も直後の上限チェックが上限内へ収める）
        var pixelWidth = ClampToPixelCount(width * scale);
        var pixelHeight = ClampToPixelCount(height * scale);

        // 極端に細長い図では片辺が 1 px へ張り付き、もう一方だけで上限を超え得る
        if ((long)pixelWidth * pixelHeight > maxTotalPixels)
        {
            if (pixelWidth >= pixelHeight)
            {
                pixelWidth = ClampToPixelCount(maxTotalPixels / (double)pixelHeight);
            }
            else
            {
                pixelHeight = ClampToPixelCount(maxTotalPixels / (double)pixelWidth);
            }
        }

        return (pixelWidth, pixelHeight);
    }

    /// <summary><see cref="MainViewModel"/> の現在状態から SVG ファイルを書き出す</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/></param>
    /// <param name="path">出力先パス</param>
    public static void ExportSvg(MainViewModel vm, string path) =>
        File.WriteAllText(path, BuildSvg(vm), Encoding.UTF8);

    // SVG の text はベースライン Y 指定のため、テキスト上端へフォントサイズ相当のオフセットを加える
    private const double BodyBaselineOffset = 13;
    private const double DescriptionBaselineOffset = 11;

    /// <summary>リレーション線の太さ（style の .rel と自己参照ループの半径計算で共有する）</summary>
    private const double RelationStrokeThickness = 1.6;

    /// <summary>SVG 文字列を生成する（テスト検証のため公開する）</summary>
    /// <remarks>
    /// エンティティの高さ・行配置はキャンバス描画と同じ
    /// <see cref="DiagramMetricsService.CalculateCardLayout"/> を用いる
    /// リレーション線の端点は <see cref="EntityViewModel.DisplayHeight"/> を基礎に計算されるため、
    /// 同一計算を共有することで線とカード枠のズレを防ぐ。
    /// 自己参照リレーションは両端点が同一点になり線が描けないため、画面（MainWindow.xaml）と同じく
    /// <see cref="DiagramMetricsService.CalculateSelfLoopEllipse"/> のループ楕円で描く
    /// 印刷（<see cref="DiagramVectorRenderer"/>）は本メソッドと同じ見た目を DrawingContext で
    /// 描く鏡写し実装のため、配色・フォント・座標を変えるときは両方を揃えること
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

        // 自己参照ループはエンティティの右上へはみ出すため、キャンバス範囲へ明示的に含める（右端が欠けるのを防ぐ）
        foreach (var r in vm.Relationships)
        {
            if (!r.IsSelfRelationship)
            {
                continue;
            }

            maxX = Math.Max(maxX, r.SelfLoopLeft + r.SelfLoopWidth + padding);
            maxY = Math.Max(maxY, r.SelfLoopTop + r.SelfLoopHeight + padding);
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
                + $".pk{{font:bold 13px 'Segoe UI',sans-serif;fill:{ColumnKeyMarkPalette.PrimaryKeyColor}}}"
                + $".fk{{font:bold 13px 'Segoe UI',sans-serif;fill:{ColumnKeyMarkPalette.ForeignKeyColor}}}"
                + $".uq{{font:bold 13px 'Segoe UI',sans-serif;fill:{ColumnKeyMarkPalette.UniqueColor}}}"
                + $".rel{{stroke:#5F6B7A;stroke-width:{F(RelationStrokeThickness)};fill:none}}"
                + ".label{font:10px 'Segoe UI',sans-serif;fill:#374151}"
                + "</style>"
        );

        // 背景が透明のままだとダークモードのビューアで黒く見えるため、白背景を最初に敷く
        sb.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#fff\" />");

        // リレーション
        foreach (var r in vm.Relationships)
        {
            // 自己参照は両端点が同一点になり線が消えてしまうため、画面と同じループ楕円を描く
            if (r.IsSelfRelationship)
            {
                var loop = DiagramMetricsService.CalculateSelfLoopEllipse(
                    r,
                    RelationStrokeThickness
                );
                sb.AppendLine(
                    $"  <ellipse class=\"rel\" cx=\"{F(loop.CenterX)}\" cy=\"{F(loop.CenterY)}\" rx=\"{F(loop.RadiusX)}\" ry=\"{F(loop.RadiusY)}\" />"
                );
            }
            else
            {
                sb.AppendLine(
                    $"  <line class=\"rel\" x1=\"{F(r.X1)}\" y1=\"{F(r.Y1)}\" x2=\"{F(r.X2)}\" y2=\"{F(r.Y2)}\" />"
                );
            }

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

                // キー標識（PK > FK > UQ の優先度は ColumnViewModel.KeyMark が 1 本化して決める）
                if (KeyMarkCssClass(c.KeyMark) is { } keyMarkClass)
                {
                    sb.AppendLine(
                        $"    <text class=\"{keyMarkClass}\" x=\"6\" y=\"{F(baseline)}\">{c.KeyMarkText}</text>"
                    );
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

    /// <summary>キー標識に対応する SVG の CSS クラス名を返す（標識なしは <c>null</c>＝出力しない）</summary>
    private static string? KeyMarkCssClass(ColumnKeyMark mark) =>
        mark switch
        {
            ColumnKeyMark.PrimaryKey => "pk",
            ColumnKeyMark.ForeignKey => "fk",
            ColumnKeyMark.Unique => "uq",
            _ => null,
        };

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
