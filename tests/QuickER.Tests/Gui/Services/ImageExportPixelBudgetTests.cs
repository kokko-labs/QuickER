using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeAssertions;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// PNG 出力の総画素数の上限（<see cref="ImageExportService.ExportPng"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RenderTargetBitmap"/> は総画素数が 2^30 を超えると <see cref="OverflowException"/> を
/// 投げる（メモリ不足ではなく確保サイズの計算で溢れる）。作ってから縮めることはできないので、
/// 判定と縮小はビットマップを作る前に行う。
/// </para>
/// <para>
/// 上限は引数で差し替えられる＝テストは 1 億画素の図を作らずに、同じ判定を小さな数で確かめる。
/// </para>
/// </remarks>
public class ImageExportPixelBudgetTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-pngbudget-" + Guid.NewGuid().ToString("N")
    );

    public ImageExportPixelBudgetTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末失敗はテスト結果に影響させない
        }
    }

    /// <summary>指定サイズを塗りつぶしただけの Visual を作る</summary>
    private static Visual CreateVisual(double width, double height)
    {
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, width, height));
        }

        return visual;
    }

    /// <summary>書き出した PNG の画素サイズを読み直す</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad
        );

        return (frame.PixelWidth, frame.PixelHeight);
    }

    [Fact(DisplayName = "ExportPng: 上限に収まる図は実寸のまま書き出す")]
    public void ExportPng_WithinBudget_KeepsFullSize()
    {
        var path = Path.Combine(_folder, "full.png");

        var result = ImageExportService.ExportPng(
            CreateVisual(200, 100),
            path,
            width: 200,
            height: 100,
            maxTotalPixels: 20_000
        );

        result.WasScaledDown.Should().BeFalse();
        result.OutputWidth.Should().Be(200);
        result.OutputHeight.Should().Be(100);
        ReadPngSize(path).Should().Be((200, 100));
    }

    /// <summary>上限を超える図が、縦横比を保ったまま上限内へ縮小されることを検証する</summary>
    [Fact(DisplayName = "ExportPng: 上限を超える図は縦横比を保って縮小する")]
    public void ExportPng_OverBudget_IsScaledDownWithinLimit()
    {
        var path = Path.Combine(_folder, "scaled.png");

        var result = ImageExportService.ExportPng(
            CreateVisual(2000, 1000),
            path,
            width: 2000,
            height: 1000,
            maxTotalPixels: 20_000
        );

        result.WasScaledDown.Should().BeTrue();
        result.SourceWidth.Should().Be(2000);
        result.SourceHeight.Should().Be(1000);

        // 切り捨てで求めるため、丸めで上限を超えることはない
        ((long)result.OutputWidth * result.OutputHeight)
            .Should()
            .BeLessThanOrEqualTo(20_000);

        // 縦横比 2:1 を保つ（切り捨ての 1 px までは許容する）
        result.OutputWidth.Should().BeCloseTo(result.OutputHeight * 2, 1);

        ReadPngSize(path).Should().Be((result.OutputWidth, result.OutputHeight));
    }

    /// <summary>極端に細長い図でも上限を超えないことを検証する</summary>
    /// <remarks>
    /// 縦横比を保つと片辺が 0 px になる形。0 px のビットマップは作れないので 1 px へ切り上げるが、
    /// そのままではもう一方だけで上限を超え得る。
    /// </remarks>
    [Fact(DisplayName = "ExportPng: 極端に細長い図でも上限を超えない")]
    public void ExportPng_ExtremeAspectRatio_StaysWithinLimit()
    {
        var path = Path.Combine(_folder, "thin.png");

        var result = ImageExportService.ExportPng(
            CreateVisual(10_000, 2),
            path,
            width: 10_000,
            height: 2,
            maxTotalPixels: 100
        );

        result.OutputWidth.Should().BeGreaterThan(0);
        result.OutputHeight.Should().BeGreaterThan(0);
        ((long)result.OutputWidth * result.OutputHeight).Should().BeLessThanOrEqualTo(100);
        ReadPngSize(path).Should().Be((result.OutputWidth, result.OutputHeight));
    }

    /// <summary>縮小しても図の中身が切り取られない（DPI で描画側も縮む）ことを検証する</summary>
    /// <remarks>
    /// 画素サイズだけを縮めると左上だけが写る。図の右下隅にだけ色を置き、縮小後の右下隅に
    /// その色があることで全体が収まっていることを確かめる（切り取られていれば白のまま）。
    /// 全面を塗った図では、切り取っても写る範囲がすべて塗りになるため区別できない。
    /// </remarks>
    [Fact(DisplayName = "ExportPng: 縮小しても図全体が収まる")]
    public void ExportPng_ScaledDown_StillContainsWholeDiagram()
    {
        var path = Path.Combine(_folder, "whole.png");
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            // 右下の一角だけを塗る（左上は白背景のまま）
            dc.DrawRectangle(Brushes.Red, null, new Rect(1800, 900, 200, 100));
        }

        var result = ImageExportService.ExportPng(
            visual,
            path,
            width: 2000,
            height: 1000,
            maxTotalPixels: 20_000
        );

        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad
        );
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[4];
        converted.CopyPixels(
            new Int32Rect(result.OutputWidth - 1, result.OutputHeight - 1, 1, 1),
            pixels,
            4,
            0
        );

        // Bgra32 の赤（B=0, G=0, R=255）＝図の塗りが右下隅まで届いている
        pixels[2].Should().BeGreaterThan(200, "縮小後も図全体が収まっていなければならない");
        pixels[0].Should().BeLessThan(100);
    }

    /// <summary>int の範囲を超える寸法でも上限の判定が効くことを検証する</summary>
    /// <remarks>
    /// 実寸を <see cref="int"/> で持つと、エンティティを極端に遠くへ置いた図で負の値へ化け、
    /// 「負の値の積 ≤ 上限」で判定を素通りしてしまう（そのあと別の例外で落ちる）。
    /// 判定は <see cref="long"/> と <see cref="double"/> のまま行う。
    /// </remarks>
    /// <remarks>
    /// 片辺だけがあふれる形（1 件目）が要点。<see cref="int"/> へ落とすと一方だけが負になり、
    /// 負の積が上限以下に見えて判定を素通りする（両辺があふれると積が正へ戻るので素通りしない）。
    /// </remarks>
    [Theory(DisplayName = "FitWithinPixelBudget: int を超える寸法でも上限内へ収める")]
    [InlineData(3_000_000_000d, 10d)]
    [InlineData(10d, 3_000_000_000d)]
    [InlineData(3_000_000_000d, 4_000_000_000d)]
    public void FitWithinPixelBudget_DimensionsBeyondInt_StayWithinLimit(
        double width,
        double height
    )
    {
        var (pixelWidth, pixelHeight) = ImageExportService.FitWithinPixelBudget(
            width,
            height,
            (long)width,
            (long)height,
            ImageExportService.DefaultMaxTotalPixels
        );

        pixelWidth.Should().BeGreaterThan(0);
        pixelHeight.Should().BeGreaterThan(0);
        ((long)pixelWidth * pixelHeight)
            .Should()
            .BeLessThanOrEqualTo(ImageExportService.DefaultMaxTotalPixels);
    }

    /// <summary>両辺が大きい図でも縦横比が保たれることを検証する</summary>
    /// <remarks>
    /// 片辺が極端に短い図では、縮小後にもう一方だけで上限を超えて追加のクランプが効くため、
    /// 縦横比は保てない（この 3:4 の図は保てる側）。
    /// </remarks>
    [Fact(DisplayName = "FitWithinPixelBudget: 大きな図でも縦横比を保つ")]
    public void FitWithinPixelBudget_LargeDiagram_KeepsAspectRatio()
    {
        var (pixelWidth, pixelHeight) = ImageExportService.FitWithinPixelBudget(
            3_000_000_000d,
            4_000_000_000d,
            3_000_000_000L,
            4_000_000_000L,
            ImageExportService.DefaultMaxTotalPixels
        );

        // 縦横比 3:4 を保つ（切り捨ての 1 px までは許容する）
        (pixelWidth * 4)
            .Should()
            .BeCloseTo(pixelHeight * 3, 4);
    }

    /// <summary>縮小しないときの DPI が 96 ちょうどであることを検証する</summary>
    /// <remarks>
    /// 画素サイズは実寸の切り上げなので、倍率を常に「画素数 ÷ 実寸」で求めると 96 から
    /// わずかにずれる（例 200.5 px 幅で 96.019）。縮小していない図の解像度情報と描画倍率が
    /// 従来の出力から変わるため、縮小しないときは 96 を使う。
    /// <para>
    /// 書き出した PNG から読み戻して確かめることはできない。PNG は解像度を 1 メートルあたりの
    /// 画素数（整数）で持つため、96 も 96.019 も同じ値へ丸められて区別が付かない（実測では
    /// どちらも 95.2754 dpi として読み戻る）。決定そのものを直接確かめる。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "ResolveDpi: 縮小しないときは 96 ちょうど・縮小時は同じ倍率へ")]
    public void ResolveDpi_UsesExactly96WhenNotScaled()
    {
        // 実寸 200.5 x 100.5 は 201 x 101 px へ切り上がるが、縮小はしていない
        ImageExportService.ResolveDpi(false, 201, 101, 200.5, 100.5).Should().Be((96d, 96d));

        // 縮小したときは画素サイズと実寸の比（＝切り取られないための倍率）
        ImageExportService.ResolveDpi(true, 100, 50, 200, 100).Should().Be((48d, 48d));
    }
}
