using System.Windows;
using FluentAssertions;
using QuickER.Services;

namespace QuickER.Tests.Services;

/// <summary><see cref="ViewportCalculator"/> のズーム・fit 計算を検証するテストクラス</summary>
/// <remarks>UI に依存しない純関数のみを対象とする</remarks>
public class ViewportCalculatorTests
{
    /// <summary>下限（10%）未満は 10% へ丸められることを検証する</summary>
    [Fact(DisplayName = "ClampZoom: 下限未満は 0.1 に丸める")]
    public void ClampZoom_BelowMin_ClampsToMin()
    {
        ViewportCalculator.ClampZoom(0.01).Should().Be(ViewportCalculator.MinZoom);
    }

    /// <summary>上限（200%）超は 200% へ丸められることを検証する</summary>
    [Fact(DisplayName = "ClampZoom: 上限超は 2.0 に丸める")]
    public void ClampZoom_AboveMax_ClampsToMax()
    {
        ViewportCalculator.ClampZoom(10.0).Should().Be(ViewportCalculator.MaxZoom);
    }

    /// <summary>範囲内の値はそのまま返ることを検証する</summary>
    [Fact(DisplayName = "ClampZoom: 範囲内はそのまま")]
    public void ClampZoom_WithinRange_ReturnsSame()
    {
        ViewportCalculator.ClampZoom(1.5).Should().Be(1.5);
    }

    /// <summary>境界値がそのまま採用されることを検証する</summary>
    [Fact(DisplayName = "ClampZoom: 境界値はそのまま採用")]
    public void ClampZoom_Boundaries_ReturnedAsIs()
    {
        ViewportCalculator.ClampZoom(ViewportCalculator.MinZoom).Should().Be(0.1);
        ViewportCalculator.ClampZoom(ViewportCalculator.MaxZoom).Should().Be(2.0);
    }

    /// <summary>NaN は既定倍率 1.0 として扱われることを検証する</summary>
    [Fact(DisplayName = "ClampZoom: NaN は 1.0 として扱う")]
    public void ClampZoom_NaN_ReturnsOne()
    {
        ViewportCalculator.ClampZoom(double.NaN).Should().Be(1.0);
    }

    /// <summary>
    /// ズーム後、マウス直下のコンテンツ座標がズーム前と一致すること（不変量）を検証する
    /// </summary>
    [Theory(DisplayName = "ZoomAtPoint: マウス直下のコンテンツ座標が不動")]
    [InlineData(1.0, 1.1, 300, 200, 500, 400)]
    [InlineData(2.0, 1.0, 100, 150, 800, 600)]
    [InlineData(1.0, 2.0, 0, 0, 0, 0)]
    [InlineData(1.5, 0.5, 250, 250, 1000, 500)]
    public void ZoomAtPoint_ContentPointUnderMouse_IsInvariant(
        double oldZoom,
        double newZoom,
        double mouseX,
        double mouseY,
        double offsetX,
        double offsetY
    )
    {
        var mouse = new Point(mouseX, mouseY);
        var oldOffset = new Vector(offsetX, offsetY);

        var newOffset = ViewportCalculator.ZoomAtPoint(oldZoom, newZoom, mouse, oldOffset);

        // ズーム前後でマウス直下のコンテンツ座標（論理座標）を突き合わせる
        var contentBefore = new Point(
            (oldOffset.X + mouse.X) / oldZoom,
            (oldOffset.Y + mouse.Y) / oldZoom
        );
        var contentAfter = new Point(
            (newOffset.X + mouse.X) / newZoom,
            (newOffset.Y + mouse.Y) / newZoom
        );

        // 下限 0 でクランプされない範囲では厳密に一致する（本ケースはいずれも正のオフセット）
        contentAfter.X.Should().BeApproximately(contentBefore.X, 1e-9);
        contentAfter.Y.Should().BeApproximately(contentBefore.Y, 1e-9);
    }

    /// <summary>算出オフセットが負になるケースでは 0 で下限クランプされることを検証する</summary>
    [Fact(DisplayName = "ZoomAtPoint: 負オフセットは 0 に丸める")]
    public void ZoomAtPoint_NegativeOffset_ClampedToZero()
    {
        // 縮小 + 原点付近では逆算オフセットが負になり得る
        var newOffset = ViewportCalculator.ZoomAtPoint(
            2.0,
            0.5,
            new Point(10, 10),
            new Vector(0, 0)
        );

        newOffset.X.Should().BeGreaterThanOrEqualTo(0);
        newOffset.Y.Should().BeGreaterThanOrEqualTo(0);
    }

    /// <summary>コンテンツが余白込みで収まる倍率が選ばれることを検証する</summary>
    [Fact(DisplayName = "CalculateFit: 余白込みで収まる倍率を選ぶ")]
    public void CalculateFit_ChoosesFittingZoom()
    {
        // 1800x800 のコンテンツ、余白 100、ビューポート 1000x1000
        var bounds = new Rect(100, 100, 1800, 800);
        var fit = ViewportCalculator.CalculateFit(bounds, new Size(1000, 1000), 100);

        // 制約は幅: 1000 / (1800 + 200) = 0.5、高さ: 1000 / (800 + 200) = 1.0 → 小さい方の 0.5
        fit.Zoom.Should().BeApproximately(0.5, 1e-9);
    }

    /// <summary>fit 後、コンテンツ中心がビューポート中央に来ることを検証する</summary>
    [Fact(DisplayName = "CalculateFit: コンテンツ中心がビューポート中央に来る")]
    public void CalculateFit_CentersContent()
    {
        // 中心 * zoom がビューポート半分を上回り、オフセットが両軸とも正になる配置を選ぶ
        var bounds = new Rect(600, 700, 400, 200);
        var viewport = new Size(1000, 1000);
        var fit = ViewportCalculator.CalculateFit(bounds, viewport, 50);

        // コンテンツ中心（論理座標）
        var centerX = bounds.X + bounds.Width / 2;
        var centerY = bounds.Y + bounds.Height / 2;

        // オフセットが正のケースなので、中心 * zoom - offset がビューポート中央に一致する
        var screenCenterX = centerX * fit.Zoom - fit.Offset.X;
        var screenCenterY = centerY * fit.Zoom - fit.Offset.Y;

        screenCenterX.Should().BeApproximately(viewport.Width / 2, 1e-6);
        screenCenterY.Should().BeApproximately(viewport.Height / 2, 1e-6);
    }

    /// <summary>ビューポートに余裕があっても fit は等倍を超えて拡大しないことを検証する</summary>
    [Fact(DisplayName = "CalculateFit: 小さい図でも 100% を超えて拡大しない")]
    public void CalculateFit_DoesNotZoomBeyondIdentity()
    {
        // 小さいコンテンツは理論上巨大な倍率で収まるが、fit は縮小専用のため等倍で頭打ち
        var bounds = new Rect(0, 0, 10, 10);
        var fit = ViewportCalculator.CalculateFit(bounds, new Size(2000, 2000), 0);

        fit.Zoom.Should().Be(1.0);
    }

    /// <summary>巨大コンテンツでも倍率が下限 10% を下回らないことを検証する</summary>
    [Fact(DisplayName = "CalculateFit: 倍率は下限 10% でクランプ")]
    public void CalculateFit_ClampsToMinZoom()
    {
        // 理論倍率が 10% を下回る巨大コンテンツは MinZoom で頭打ち
        var bounds = new Rect(0, 0, 100000, 100000);
        var fit = ViewportCalculator.CalculateFit(bounds, new Size(500, 500), 0);

        fit.Zoom.Should().Be(ViewportCalculator.MinZoom);
    }

    /// <summary>空図（空矩形）は等倍・原点を返すことを検証する</summary>
    [Fact(DisplayName = "CalculateFit: 空図は 100% + 原点")]
    public void CalculateFit_EmptyContent_ReturnsIdentity()
    {
        var fit = ViewportCalculator.CalculateFit(Rect.Empty, new Size(1000, 800), 80);

        fit.Zoom.Should().Be(1.0);
        fit.Offset.Should().Be(new Vector(0, 0));
    }

    /// <summary>ゼロサイズのビューポートは等倍・原点を返すことを検証する</summary>
    [Fact(DisplayName = "CalculateFit: ゼロサイズのビューポートは 100% + 原点")]
    public void CalculateFit_ZeroViewport_ReturnsIdentity()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var fit = ViewportCalculator.CalculateFit(bounds, new Size(0, 0), 80);

        fit.Zoom.Should().Be(1.0);
        fit.Offset.Should().Be(new Vector(0, 0));
    }

    /// <summary>ゼロサイズのコンテンツは等倍・原点を返すことを検証する</summary>
    [Fact(DisplayName = "CalculateFit: ゼロサイズのコンテンツは 100% + 原点")]
    public void CalculateFit_ZeroSizeContent_ReturnsIdentity()
    {
        var bounds = new Rect(50, 50, 0, 0);
        var fit = ViewportCalculator.CalculateFit(bounds, new Size(1000, 800), 80);

        fit.Zoom.Should().Be(1.0);
        fit.Offset.Should().Be(new Vector(0, 0));
    }

    /// <summary>ビューポート未確定時は従来の左上カスケード配置になることを検証する</summary>
    [Fact(DisplayName = "NextEntityPosition: ビューポート未確定は従来カスケード")]
    public void NextEntityPosition_EmptyViewport_FallsBackToLegacyCascade()
    {
        var p0 = ViewportCalculator.NextEntityPosition(Rect.Empty, 0, 200);
        var p2 = ViewportCalculator.NextEntityPosition(Rect.Empty, 2, 200);

        p0.Should().Be(new Point(60, 60));
        p2.Should().Be(new Point(120, 120));
    }

    /// <summary>スクロール済みビューポートの内側（左上＋余白）へ配置されることを検証する</summary>
    [Fact(DisplayName = "NextEntityPosition: スクロール済み表示領域の内側へ配置")]
    public void NextEntityPosition_ScrolledViewport_PlacesInsideVisibleArea()
    {
        var viewport = new Rect(1000, 800, 900, 600);
        var p = ViewportCalculator.NextEntityPosition(viewport, 0, 200);

        p.Should().Be(new Point(1060, 860));
        viewport.Contains(new Rect(p.X, p.Y, 200, 100)).Should().BeTrue();
    }

    /// <summary>連続追加の斜めずらしが 8 個ごとに折り返し、表示領域内へ留まることを検証する</summary>
    [Fact(DisplayName = "NextEntityPosition: 斜めずらしは 8 個で折り返す")]
    public void NextEntityPosition_Cascade_WrapsEveryEight()
    {
        var viewport = new Rect(1000, 800, 900, 600);

        var p7 = ViewportCalculator.NextEntityPosition(viewport, 7, 200);
        var p8 = ViewportCalculator.NextEntityPosition(viewport, 8, 200);

        p7.Should().Be(new Point(1060 + 210, 860 + 210));
        p8.Should().Be(new Point(1060, 860));
    }

    /// <summary>ビューポートが狭い場合は右端・下端からはみ出さないよう内側へ寄せることを検証する</summary>
    [Fact(DisplayName = "NextEntityPosition: 狭い表示領域でははみ出さず内側へ寄せる")]
    public void NextEntityPosition_NarrowViewport_ClampsInside()
    {
        // 幅 250 のビューポートに幅 200 のエンティティ → x は Right - 200 - 20 へ寄る
        var viewport = new Rect(1000, 800, 250, 200);
        var p = ViewportCalculator.NextEntityPosition(viewport, 0, 200);

        p.X.Should().Be(1000 + 250 - 200 - 20);
        p.Y.Should().Be(800 + 200 - 160);
    }

    /// <summary>指定点が拡大後にビューポート中央へ来るオフセットを返すことを検証する</summary>
    [Fact(DisplayName = "CenterOnPoint: 点がビューポート中央へ来る")]
    public void CenterOnPoint_PlacesPointAtViewportCenter()
    {
        var contentPoint = new Point(600, 400);
        var viewport = new Size(1000, 800);
        const double zoom = 1.5;

        var offset = ViewportCalculator.CenterOnPoint(contentPoint, zoom, viewport);

        // 拡大後の点位置からオフセットを引いた画面座標がビューポート中央に一致する
        var screenX = contentPoint.X * zoom - offset.X;
        var screenY = contentPoint.Y * zoom - offset.Y;

        screenX.Should().BeApproximately(viewport.Width / 2, 1e-9);
        screenY.Should().BeApproximately(viewport.Height / 2, 1e-9);
    }

    /// <summary>倍率を変えても点が中央に来る（倍率は維持され座標系のみ拡縮する）ことを検証する</summary>
    [Theory(DisplayName = "CenterOnPoint: 任意の倍率で中央に据える")]
    [InlineData(1.0)]
    [InlineData(2.5)]
    public void CenterOnPoint_HonorsGivenZoom(double zoom)
    {
        // オフセットが両軸とも正になる（クランプされない）配置を選ぶ
        var contentPoint = new Point(800, 800);
        var viewport = new Size(1200, 900);

        var offset = ViewportCalculator.CenterOnPoint(contentPoint, zoom, viewport);

        var screenX = contentPoint.X * zoom - offset.X;
        var screenY = contentPoint.Y * zoom - offset.Y;

        screenX.Should().BeApproximately(viewport.Width / 2, 1e-9);
        screenY.Should().BeApproximately(viewport.Height / 2, 1e-9);
    }

    /// <summary>中央配置に必要なオフセットが負になるケースでは 0 で下限クランプされることを検証する</summary>
    [Fact(DisplayName = "CenterOnPoint: 負オフセットは 0 に丸める")]
    public void CenterOnPoint_NegativeOffset_ClampedToZero()
    {
        // 原点付近の点は中央に据えようとすると左上オフセットが負になり得る
        var offset = ViewportCalculator.CenterOnPoint(new Point(10, 10), 1.0, new Size(1000, 800));

        offset.X.Should().Be(0);
        offset.Y.Should().Be(0);
    }
}
