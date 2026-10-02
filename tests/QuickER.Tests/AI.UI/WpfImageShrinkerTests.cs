using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeAssertions;
using QuickER.AI.UI;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="WpfImageShrinker"/> の添付画像縮小ロジックを STA スレッド上で検証するテストクラス。
/// WPF の画像 API（<see cref="BitmapSource"/> / <see cref="PngBitmapEncoder"/>）を使うため STA が必須。
/// 観点は「上限内に収まる小さい画像はそのまま／長辺が候補上限（2048）を超えると等倍縮小／
/// アスペクト比維持／境界サイズ／デコード不能は null」。テスト画像はコード内で生成する。
/// </summary>
public class WpfImageShrinkerTests
{
    /// <summary>指定サイズの単色 Bgra32 画像を PNG バイト列にエンコードして返す（ソース画像の合成に使う）</summary>
    private static byte[] CreatePng(int width, int height)
    {
        var stride = width * 4;
        // 単色（ゼロ埋め）なので PNG は高圧縮になり、常に上限（5MB）内へ収まる
        var pixels = new byte[height * stride];
        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride
        );

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    /// <summary>PNG バイト列をデコードしてピクセル寸法を取り出す</summary>
    private static (int Width, int Height) DecodeSize(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var frame = BitmapDecoder
            .Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
            .Frames[0];

        return (frame.PixelWidth, frame.PixelHeight);
    }

    /// <summary>長辺が最大候補（2048）以下の画像は寸法が変わらず PNG が返ることを検証する</summary>
    [Fact(DisplayName = "小さい画像は寸法そのままで返る")]
    public void Shrink_SmallImage_KeepsDimensions()
    {
        RunSta(() =>
        {
            var input = CreatePng(100, 80);

            var result = WpfImageShrinker.Shrink(input, "image/png");

            result.Should().NotBeNull();
            DecodeSize(result!).Should().Be((100, 80));
        });
    }

    /// <summary>長辺が候補上限ちょうど（2048）の画像は縮小されず寸法が保たれることを検証する（境界）</summary>
    [Fact(DisplayName = "長辺 2048 ちょうどは縮小されない")]
    public void Shrink_ExactBoundaryEdge_IsNotScaled()
    {
        RunSta(() =>
        {
            var input = CreatePng(2048, 1024);

            var result = WpfImageShrinker.Shrink(input, "image/png");

            result.Should().NotBeNull();
            DecodeSize(result!).Should().Be((2048, 1024));
        });
    }

    /// <summary>長辺が候補上限を超える画像は長辺 2048 以下へ縮小され、アスペクト比が維持されることを検証する</summary>
    [Fact(DisplayName = "長辺超過は 2048 以下へ縮小しアスペクト比を維持する")]
    public void Shrink_OversizedEdge_ScalesDownPreservingAspect()
    {
        RunSta(() =>
        {
            var input = CreatePng(3000, 2000);

            var result = WpfImageShrinker.Shrink(input, "image/png");

            result.Should().NotBeNull();

            var (width, height) = DecodeSize(result!);

            // 長辺は候補上限 2048 以下（かつ元の 3000 より確実に小さい）へ収まる
            var longEdge = Math.Max(width, height);
            longEdge.Should().BeLessThanOrEqualTo(2048);
            longEdge.Should().BeGreaterThan(2000);

            // アスペクト比（元は 3:2 = 1.5）が丸め誤差の範囲で維持される
            var aspect = (double)width / height;
            aspect.Should().BeApproximately(1.5, 0.01);
        });
    }

    /// <summary>デコード不能なバイト列（画像でない）は null が返ることを検証する</summary>
    [Fact(DisplayName = "デコード不能なバイト列は null を返す")]
    public void Shrink_UndecodableData_ReturnsNull()
    {
        RunSta(() =>
        {
            var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };

            var result = WpfImageShrinker.Shrink(garbage, "image/png");

            result.Should().BeNull();
        });
    }

    /// <summary>
    /// 上限（5MB）を超える実 JPEG を実縮小器で添付にすると、添付の MIME が縮小後の形式（image/png）になり、
    /// その中身が PNG としてデコードできることを検証する（名乗りと中身が食い違わない）。
    /// 生成を軽くするため、小さい JPEG の終端（EOI）の後ろを詰め物で 5MB 超へ伸ばす（デコーダは EOI 以降を読まない）
    /// </summary>
    [Fact(DisplayName = "大きい JPEG を実縮小器で添付にすると PNG として返る")]
    public void CreateFromBytes_LargeJpegWithRealShrinker_ReturnsPng()
    {
        RunSta(() =>
        {
            var jpeg = CreateJpeg(400, 300);
            var large = new byte[QuickER.AI.ChatAttachmentLimits.MaxImageBytes + 1];
            jpeg.CopyTo(large, 0);

            var result = QuickER.AI.ChatAttachmentFactory.CreateFromBytes(
                "photo.jpg",
                large,
                WpfImageShrinker.Shrink
            );

            result.Success.Should().BeTrue();
            result.Attachment!.MediaType.Should().Be(WpfImageShrinker.OutputMediaType);
            DecodeSize(result.Attachment.Data).Should().Be((400, 300));

            using var stream = new MemoryStream(result.Attachment.Data);
            BitmapDecoder
                .Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                .Should()
                .BeOfType<PngBitmapDecoder>();
        });
    }

    /// <summary>指定サイズの単色 Bgr24 画像を JPEG バイト列にエンコードして返す</summary>
    private static byte[] CreateJpeg(int width, int height)
    {
        var stride = width * 3;
        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            new byte[height * stride],
            stride
        );

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    /// <summary>正規化後の MIME タイプ定数が image/png であることを検証する</summary>
    [Fact(DisplayName = "出力 MIME タイプは image/png")]
    public void OutputMediaType_IsPng()
    {
        WpfImageShrinker.OutputMediaType.Should().Be("image/png");
    }
}
