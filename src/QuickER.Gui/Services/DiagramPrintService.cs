using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>印刷時のサイズモード</summary>
public enum PrintSizeMode
{
    /// <summary>用紙 1 ページへ縮小フィットして印刷する（拡大はしない）</summary>
    FitToPage,

    /// <summary>原寸大で印刷する（用紙サイズを図の実寸に合わせる。Microsoft Print to PDF 等の PDF 出力向け）</summary>
    ActualSize,
}

/// <summary>ER 図を用紙 1 ページへ印刷するサービス</summary>
/// <remarks>
/// 図全体を常に 1 ページへ収める。縮小フィット（<see cref="PrintSizeMode.FitToPage"/>・拡大はしない）と、
/// 用紙サイズ自体を図の実寸へ合わせる原寸大（<see cref="PrintSizeMode.ActualSize"/>）の 2 モードを持つ。
/// 図は SVG 出力と同じ内容を <see cref="DiagramVectorRenderer"/> でベクタ描画する。
/// 文字は Glyphs として XPS/PDF に保持されるため、拡大しても鮮明でファイルも小さい。
/// プレビューは持たず WPF 標準の <see cref="PrintDialog"/> のみを用いる。
/// ページ組み立て（<see cref="CreatePrintVisual"/>）は PrintDialog 非依存の純粋処理として分離し、
/// テスト・検証を可能にしている
/// </remarks>
public static class DiagramPrintService
{
    // ヘッダのフォント設定（ウィンドウの既定フォントと揃える）
    private const double HeaderFontSize = 12;
    private static readonly Typeface HeaderTypeface = new(
        new FontFamily("Segoe UI, Yu Gothic UI, Meiryo"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal
    );
    private static readonly Brush HeaderBrush = CreateFrozenBrush("#374151");

    // ヘッダと図の間に空ける余白（px）
    private const double HeaderGap = 8;

    // ヘッダ文字を印刷可能領域の左端から内側へずらす横方向の余白（px）。
    // 縮小フィット時にドライバ報告の印刷可能領域が用紙端（原点 0）から始まると
    // タイトルが左端ぎりぎりに印字されるため、少し内側へ寄せる。
    // 縦方向へは広げない（原寸大モードの図配置領域が縮み等倍が崩れるのを避ける）
    private const double HeaderLeftInset = 12;

    // 原寸大印刷・印刷可能領域フォールバックで用いるページ余白（px・上下左右共通）
    private const double PageMargin = 40;

    /// <summary>図を用紙へ収めるための縮小フィット倍率を求める（拡大はしない）</summary>
    /// <param name="content">図の実寸サイズ</param>
    /// <param name="available">図を配置できる領域のサイズ</param>
    /// <returns>1.0 を上限とする縮小倍率。いずれかの寸法が 0 以下なら 1.0</returns>
    public static double CalculateFitScale(Size content, Size available)
    {
        // 実寸・領域のいずれかが不正（0 以下）なら等倍で扱う
        if (
            content.Width <= 0
            || content.Height <= 0
            || available.Width <= 0
            || available.Height <= 0
        )
        {
            return 1.0;
        }

        // 幅・高さで厳しい方の倍率を採用し、1.0 を超えない（拡大しない）
        return Math.Min(
            1.0,
            Math.Min(available.Width / content.Width, available.Height / content.Height)
        );
    }

    /// <summary>ページ上部に表示するヘッダ文字列（タイトル＋任意で印刷日時）を組み立てる</summary>
    /// <param name="title">ヘッダに表示するタイトル。空欄なら何も印字しない（フォールバックしない）</param>
    /// <param name="printedAt">印刷日時</param>
    /// <param name="includeTimestamp">印刷日時を付与するかどうか</param>
    /// <returns>
    /// タイトルと日時のうち存在するものを 2 スペース区切りで連結した文字列。
    /// タイトル空欄なら日時のみ、日時なしならタイトルのみ、両方なければ空文字
    /// </returns>
    public static string BuildHeaderText(string? title, DateTime printedAt, bool includeTimestamp)
    {
        var displayTitle = title?.Trim() ?? string.Empty;
        var timestamp = includeTimestamp ? $"{printedAt:yyyy/MM/dd HH:mm}" : string.Empty;

        if (displayTitle.Length == 0)
        {
            return timestamp;
        }

        return timestamp.Length == 0 ? displayTitle : $"{displayTitle}  {timestamp}";
    }

    /// <summary>原寸大印刷時のカスタム用紙サイズを求める</summary>
    /// <remarks>
    /// 図の実寸に左右余白（40×2）とヘッダ領域（ヘッダ高さ＋余白）を加えたサイズを用紙とすることで、
    /// 図を縮小せず 1 ページへ収める。実寸が不正（0 以下）なら既定サイズ（800x600）を実寸とみなす
    /// </remarks>
    /// <param name="content">図の実寸サイズ</param>
    /// <param name="headerHeight">ヘッダ文字列の描画高さ</param>
    /// <returns>原寸大印刷に必要な用紙サイズ（DIP）</returns>
    public static Size CalculateActualSizePageSize(Size content, double headerHeight)
    {
        // 実寸が不正なら縮小フィット時と同じ既定サイズへフォールバックする
        if (content.Width <= 0 || content.Height <= 0)
        {
            content = new Size(800, 600);
        }

        return new Size(
            content.Width + PageMargin * 2,
            content.Height + PageMargin * 2 + headerHeight + HeaderGap
        );
    }

    /// <summary>原寸大印刷時の印刷可能領域を自前で求める（用紙全体から上下左右の余白を引いた矩形）</summary>
    /// <remarks>
    /// 原寸大モードの用紙は自分で決めたサイズのため、印刷可能領域も自前で確定する。
    /// ドライバの <c>GetPrintCapabilities</c> はカスタム用紙サイズを反映せず標準用紙
    /// （例: Microsoft Print to PDF は A3 相当）の領域を返すため、それを使うと
    /// 巨大な用紙の隅へ縮小配置されてしまう（実測で確認済みの挙動）。
    /// <c>MergeAndValidatePrintTicket</c> もカスタムサイズを A4 へ正規化して返す一方で
    /// 実際の印刷はカスタムサイズで行われるため、判定には使えない
    /// </remarks>
    /// <param name="pageSize">CalculateActualSizePageSize で求めた用紙サイズ（DIP）</param>
    public static Rect CalculateActualSizeImageableArea(Size pageSize) =>
        new(
            PageMargin,
            PageMargin,
            Math.Max(1, pageSize.Width - PageMargin * 2),
            Math.Max(1, pageSize.Height - PageMargin * 2)
        );

    /// <summary>印刷ページ全体の <see cref="DrawingVisual"/> を組み立てる（PrintDialog 非依存の純粋処理）</summary>
    /// <param name="vm">描画対象の <see cref="MainViewModel"/></param>
    /// <param name="contentBounds">図の実寸バウンディングボックス（<see cref="DiagramVectorRenderer.CalculateDiagramBounds"/>）</param>
    /// <param name="imageableArea">用紙の印刷可能領域（物理原点基準の Rect）</param>
    /// <param name="headerText">ページ上部に表示するヘッダ文字列</param>
    /// <returns>ヘッダと縮小配置した図を含む DrawingVisual</returns>
    public static DrawingVisual CreatePrintVisual(
        MainViewModel vm,
        Rect contentBounds,
        Rect imageableArea,
        string headerText
    )
    {
        var visual = new DrawingVisual();

        using var dc = visual.RenderOpen();

        // ヘッダ（FormattedText）。PrintVisual は Visual 原点を用紙物理原点に置くため、
        // 印刷可能領域の左上を基準に、左端から少し内側へ寄せて描く
        var header = CreateHeaderFormattedText(headerText);

        dc.DrawText(header, new Point(imageableArea.Left + HeaderLeftInset, imageableArea.Top));

        // 図の配置領域（ヘッダ高さ + 余白を空けた残り）
        var diagramTop = imageableArea.Top + header.Height + HeaderGap;
        var available = new Size(
            imageableArea.Width,
            Math.Max(0, imageableArea.Bottom - diagramTop)
        );

        var scale = CalculateFitScale(contentBounds.Size, available);
        var scaledWidth = contentBounds.Width * scale;
        var scaledHeight = contentBounds.Height * scale;

        // 残り領域内で中央配置する
        var offsetX = imageableArea.Left + (available.Width - scaledWidth) / 2;
        var offsetY = diagramTop + (available.Height - scaledHeight) / 2;

        // 図をベクタのまま「紙面位置へ移動 → 縮小 → 図座標原点の打ち消し」の変換で貼り込む。
        // ビットマップを経由しないため、文字は Glyphs として保持され拡大しても鮮明
        dc.PushTransform(new TranslateTransform(offsetX, offsetY));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushTransform(new TranslateTransform(-contentBounds.X, -contentBounds.Y));

        dc.DrawDrawing(DiagramVectorRenderer.RenderDiagram(vm).Drawing);

        dc.Pop();
        dc.Pop();
        dc.Pop();

        return visual;
    }

    /// <summary>PrintDialog を表示し、図全体を用紙 1 ページへ印刷する</summary>
    /// <param name="vm">描画対象の <see cref="MainViewModel"/></param>
    /// <param name="title">ヘッダ表示用のタイトル。空欄ならヘッダへ印字しない（印刷ジョブ名のみ「無題」とする）</param>
    /// <param name="includeTimestamp">ヘッダに印刷日時を印字するかどうか</param>
    /// <param name="sizeMode">縮小フィットで印刷するか、用紙を図の実寸へ合わせて原寸大で印刷するか</param>
    public static void Print(
        MainViewModel vm,
        string title,
        bool includeTimestamp,
        PrintSizeMode sizeMode
    )
    {
        var printDialog = new PrintDialog();

        // 用紙向きの既定は横（ユーザーはダイアログで変更可能）
        var ticket = printDialog.PrintTicket;

        if (ticket is not null)
        {
            ticket.PageOrientation = PageOrientation.Landscape;
            printDialog.PrintTicket = ticket;
        }

        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        // 図の実寸は VM から直接求める（エンティティ 0 件時のフォールバックも同メソッドが持つ）
        var bounds = DiagramVectorRenderer.CalculateDiagramBounds(vm);

        var headerText = BuildHeaderText(title, DateTime.Now, includeTimestamp);
        Rect imageableArea;

        if (sizeMode == PrintSizeMode.ActualSize)
        {
            // 原寸大モードでは用紙サイズ自体を図の実寸（＋余白・ヘッダ領域）へ合わせる。
            // サイズを直接指定するため回転はさせない（Portrait 固定）
            var headerHeight = CreateHeaderFormattedText(headerText).Height;
            var pageSize = CalculateActualSizePageSize(bounds.Size, headerHeight);
            var actualSizeTicket = printDialog.PrintTicket;

            if (actualSizeTicket is not null)
            {
                actualSizeTicket.PageMediaSize = new PageMediaSize(pageSize.Width, pageSize.Height);
                actualSizeTicket.PageOrientation = PageOrientation.Portrait;
                printDialog.PrintTicket = actualSizeTicket;
            }

            // 印刷可能領域はドライバへ問い合わせず自前で確定する（理由は CalculateActualSizeImageableArea を参照）。
            // 用紙を図に合わせているため available == content となり、
            // CalculateFitScale が自然に 1.0 を返して縮小フィットと同じ合成ロジックがそのまま原寸で描く
            imageableArea = CalculateActualSizeImageableArea(pageSize);
        }
        else
        {
            // 縮小フィットでは実際の用紙を使うため、ドライバ報告の印刷可能領域に従う
            // （取得失敗・例外時は用紙全体から一定余白を引いた領域へフォールバックする）
            imageableArea = ResolveImageableArea(printDialog);
        }

        var jobName = string.IsNullOrWhiteSpace(title) ? "無題" : title;
        var pageVisual = CreatePrintVisual(vm, bounds, imageableArea, headerText);

        printDialog.PrintVisual(pageVisual, $"QuickER ER図 - {jobName}");
    }

    /// <summary>ヘッダ描画用の <see cref="FormattedText"/> を生成する（合成と用紙サイズ計算で共用）</summary>
    private static FormattedText CreateHeaderFormattedText(string headerText) =>
        new(
            headerText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            HeaderTypeface,
            HeaderFontSize,
            HeaderBrush,
            1.0
        );

    /// <summary>16 進カラーコードから凍結済みブラシを生成する</summary>
    private static SolidColorBrush CreateFrozenBrush(string hexColor)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        brush.Freeze();

        return brush;
    }

    /// <summary>印刷可能領域を求める（取得できない場合は用紙全体から一定余白を引いた領域へフォールバック）</summary>
    private static Rect ResolveImageableArea(PrintDialog printDialog)
    {
        try
        {
            var area = printDialog
                .PrintQueue.GetPrintCapabilities(printDialog.PrintTicket)
                .PageImageableArea;

            if (area is not null)
            {
                return new Rect(
                    area.OriginWidth,
                    area.OriginHeight,
                    area.ExtentWidth,
                    area.ExtentHeight
                );
            }
        }
        catch
        {
            // GetPrintCapabilities は環境により例外を投げるため、フォールバックへ委ねる
        }

        return new Rect(
            PageMargin,
            PageMargin,
            printDialog.PrintableAreaWidth - PageMargin * 2,
            printDialog.PrintableAreaHeight - PageMargin * 2
        );
    }
}
