using System.Windows;

namespace QuickER.Services;

/// <summary>
/// キャンバスのズーム・パン・fit-to-window の座標計算を担う純関数群
/// </summary>
/// <remarks>
/// WPF の型（<see cref="Rect"/> / <see cref="Size"/> / <see cref="Point"/>）は参照するが、
/// UI 要素やディスパッチャには一切依存しないためヘッドレスで単体テスト可能。
/// スクロールオフセットはコンテンツ（＝拡大後）座標系のピクセルで扱う。
/// </remarks>
public static class ViewportCalculator
{
    /// <summary>ズーム倍率の下限（10%）</summary>
    public const double MinZoom = 0.1;

    /// <summary>ズーム倍率の上限（200%）</summary>
    public const double MaxZoom = 2.0;

    /// <summary>1 ノッチあたりのズーム倍率（乗算）</summary>
    public const double ZoomStep = 1.1;

    /// <summary>指定倍率を <see cref="MinZoom"/>〜<see cref="MaxZoom"/> の範囲へ丸める</summary>
    public static double ClampZoom(double zoom)
    {
        // NaN は既定倍率 1.0 として扱い、無効値でビューが壊れるのを防ぐ
        if (double.IsNaN(zoom))
        {
            return 1.0;
        }

        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    /// <summary>
    /// マウス位置を中心にズームした際の、新しいスクロールオフセットを求める
    /// </summary>
    /// <param name="oldZoom">ズーム前の倍率</param>
    /// <param name="newZoom">ズーム後の倍率</param>
    /// <param name="mouse">ビューポート左上を原点とするマウス座標（px）</param>
    /// <param name="oldOffset">ズーム前のスクロールオフセット（コンテンツ座標 px）</param>
    /// <returns>ズーム後のスクロールオフセット（コンテンツ座標 px）</returns>
    /// <remarks>
    /// 不変量: ズーム前後でマウス直下のコンテンツ座標が動かない。
    /// contentPoint = (oldOffset + mouse) / oldZoom を固定点とし、
    /// newOffset = contentPoint * newZoom − mouse を返す。
    /// </remarks>
    public static Vector ZoomAtPoint(double oldZoom, double newZoom, Point mouse, Vector oldOffset)
    {
        // マウス直下にあるコンテンツ座標（拡大前の論理座標）を求める
        var contentX = (oldOffset.X + mouse.X) / oldZoom;
        var contentY = (oldOffset.Y + mouse.Y) / oldZoom;

        // 同じコンテンツ座標が新倍率でも同じ画面位置に来るようオフセットを逆算する
        var newOffsetX = contentX * newZoom - mouse.X;
        var newOffsetY = contentY * newZoom - mouse.Y;

        // 負のオフセットは無意味なため 0 で下限を切る（ScrollViewer の挙動に合わせる）
        return new Vector(Math.Max(0, newOffsetX), Math.Max(0, newOffsetY));
    }

    /// <summary>
    /// コンテンツのバウンディングボックスがビューポートに収まる倍率とスクロール位置を求める
    /// </summary>
    /// <param name="contentBounds">整列対象コンテンツのバウンディングボックス（余白を含まない論理座標）</param>
    /// <param name="viewport">ビューポート（表示領域）のサイズ（px）</param>
    /// <param name="margin">コンテンツ周囲に確保する余白（論理座標 px）</param>
    /// <returns>(倍率, オフセット)。コンテンツをビューポート中央に配置する</returns>
    /// <remarks>
    /// 空図やゼロサイズのビューポートでは 100% ＋ 原点を返す。
    /// fit は「収まるよう縮小する」操作であり、小さい図を等倍超へ拡大すると
    /// 文字が巨大化して不自然なため、倍率は <see cref="MinZoom"/>〜100% にクランプする。
    /// </remarks>
    public static ViewportFit CalculateFit(Rect contentBounds, Size viewport, double margin)
    {
        // 空図・不正入力は等倍・原点で返す（ズーム操作の意味がないため）
        if (
            contentBounds.IsEmpty
            || contentBounds.Width <= 0
            || contentBounds.Height <= 0
            || viewport.Width <= 0
            || viewport.Height <= 0
        )
        {
            return new ViewportFit(1.0, new Vector(0, 0));
        }

        // 余白込みのコンテンツ寸法（論理座標）
        var contentWidth = contentBounds.Width + margin * 2;
        var contentHeight = contentBounds.Height + margin * 2;

        // 幅・高さ双方が収まる倍率を採用する（拡大はせず、上限は等倍）
        var zoom = ClampZoom(
            Math.Min(1.0, Math.Min(viewport.Width / contentWidth, viewport.Height / contentHeight))
        );

        // 余白込みコンテンツの中心（論理座標）
        var centerX = contentBounds.X + contentBounds.Width / 2;
        var centerY = contentBounds.Y + contentBounds.Height / 2;

        // コンテンツ中心がビューポート中央に来るようオフセットを決める
        var offsetX = centerX * zoom - viewport.Width / 2;
        var offsetY = centerY * zoom - viewport.Height / 2;

        return new ViewportFit(zoom, new Vector(Math.Max(0, offsetX), Math.Max(0, offsetY)));
    }

    /// <summary>
    /// 指定したコンテンツ座標の点を、現在の倍率を保ったままビューポート中央へ据えるスクロールオフセットを求める
    /// </summary>
    /// <param name="contentPoint">中央に据える点（拡大前の論理座標）</param>
    /// <param name="zoom">維持する倍率（1.0 = 100%）</param>
    /// <param name="viewport">ビューポート（表示領域）のサイズ（px）</param>
    /// <returns>ビューポート中央に点が来るスクロールオフセット（コンテンツ座標 px）</returns>
    /// <remarks>
    /// エンティティ検索のジャンプで用いる。倍率は変更せず、視点だけを移動する。
    /// 拡大後の点位置（contentPoint * zoom）からビューポート半分を引いた位置が左上オフセットになる。
    /// 負のオフセットは無意味なため 0 で下限を切る（ScrollViewer の挙動に合わせる）。
    /// </remarks>
    public static Vector CenterOnPoint(Point contentPoint, double zoom, Size viewport)
    {
        // 拡大後の点位置からビューポート半分だけ戻した位置を左上オフセットとする
        var offsetX = contentPoint.X * zoom - viewport.Width / 2;
        var offsetY = contentPoint.Y * zoom - viewport.Height / 2;

        return new Vector(Math.Max(0, offsetX), Math.Max(0, offsetY));
    }
}

/// <summary>fit-to-window の計算結果（適用する倍率とスクロールオフセット）</summary>
/// <param name="Zoom">適用する倍率</param>
/// <param name="Offset">適用するスクロールオフセット（コンテンツ座標 px）</param>
public readonly record struct ViewportFit(double Zoom, Vector Offset);
