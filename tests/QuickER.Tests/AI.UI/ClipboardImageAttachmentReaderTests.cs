using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport;
using AiUiStrings = QuickER.AI.UI.Resources.Strings;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="ClipboardImageAttachmentReader"/> のクリップボード画像→添付変換ロジック
/// （WPF Clipboard 非依存の部分）を検証するテストクラス。
/// </summary>
public class ClipboardImageAttachmentReaderTests
{
    /// <summary>PNG シグネチャ＋任意末尾でバイト列を作る</summary>
    private static byte[] PngBytes(int totalLength = 16)
    {
        var data = new byte[Math.Max(totalLength, 8)];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>ファイル名がタイムスタンプ付き PNG 形式になることを検証する</summary>
    [Fact(DisplayName = "ファイル名はタイムスタンプ付き PNG")]
    public void BuildFileName_UsesTimestampedPngFormat()
    {
        var name = ClipboardImageAttachmentReader.BuildFileName(new DateTime(2026, 7, 6, 13, 5, 9));

        name.Should().Be($"{AiUiStrings.Attachment_ClipboardImagePrefix}_20260706_130509.png");
    }

    /// <summary>ファイル名の日時が書式のカルチャに依らない（タイ語の仏暦にならない）ことを検証する</summary>
    [Fact(DisplayName = "ファイル名の日時は書式のカルチャに依らず西暦")]
    public void BuildFileName_IsCultureInvariant()
    {
        foreach (var culture in CultureScope.Cultures)
        {
            using var scope = new CultureScope(culture);

            ClipboardImageAttachmentReader
                .BuildFileName(new DateTime(2026, 7, 6, 13, 5, 9))
                .Should()
                .Be($"{AiUiStrings.Attachment_ClipboardImagePrefix}_20260706_130509.png", culture);
        }
    }

    /// <summary>PNG バイト列から画像添付が生成され、名前・種別・MIME が正しいことを検証する</summary>
    [Fact(DisplayName = "PNG バイト列から画像添付を生成する")]
    public void CreateFromPngBytes_ProducesImageAttachment()
    {
        var result = ClipboardImageAttachmentReader.CreateFromPngBytes(
            PngBytes(),
            new DateTime(2026, 7, 6, 13, 5, 9)
        );

        result.Success.Should().BeTrue();
        result.Attachment.Should().NotBeNull();
        result.Attachment!.Kind.Should().Be(ChatAttachmentKind.Image);
        result.Attachment.MediaType.Should().Be("image/png");
        result
            .Attachment.FileName.Should()
            .Be($"{AiUiStrings.Attachment_ClipboardImagePrefix}_20260706_130509.png");
    }

    /// <summary>中身が PNG でない（マジックバイト不一致）場合は失敗結果になることを検証する</summary>
    [Fact(DisplayName = "非 PNG バイト列は失敗する")]
    public void CreateFromPngBytes_NonPng_Fails()
    {
        var result = ClipboardImageAttachmentReader.CreateFromPngBytes(
            new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 },
            DateTime.Now
        );

        result.Success.Should().BeFalse();
    }

    /// <summary>2x2 の実ビットマップを作る（PNG への変換が通る正常系の確認用）</summary>
    private static BitmapSource SmallBitmap()
    {
        var pixels = new byte[2 * 2 * 4];
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 2 * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// クリップボードの読み取りが投げる例外のうち、「画像なし」へ畳むもの。
    /// 他のプロセスがクリップボードを開いているとき（COMException）と、WIC がビットマップを解釈できないとき。
    /// </summary>
    public static TheoryData<Exception> FoldedClipboardExceptions =>
        new()
        {
            new COMException("CLIPBRD_E_CANT_OPEN", unchecked((int)0x800401D0)),
            new ExternalException("clipboard busy"),
            new NotSupportedException("no imaging component"),
            new FileFormatException("broken bitmap"),
        };

    /// <summary>
    /// 画像の有無を問う段で例外が出ても null を返す（Ctrl+V のキーハンドラから例外が抜けてアプリが落ちない）ことを検証する
    /// </summary>
    [Theory(DisplayName = "画像の有無の問い合わせが投げても null を返す")]
    [MemberData(nameof(FoldedClipboardExceptions))]
    public void TryGetClipboardPng_ContainsImageThrows_ReturnsNull(Exception exception)
    {
        var png = ClipboardImageAttachmentReader.TryGetClipboardPng(
            () => throw exception,
            () => SmallBitmap()
        );

        png.Should().BeNull();
    }

    /// <summary>画像の取り出しで例外が出ても null を返すことを検証する</summary>
    [Theory(DisplayName = "画像の取り出しが投げても null を返す")]
    [MemberData(nameof(FoldedClipboardExceptions))]
    public void TryGetClipboardPng_GetImageThrows_ReturnsNull(Exception exception)
    {
        var png = ClipboardImageAttachmentReader.TryGetClipboardPng(
            () => true,
            () => throw exception
        );

        png.Should().BeNull();
    }

    /// <summary>畳む対象でない例外（想定外の不具合）は黙らせないことを検証する</summary>
    [Fact(DisplayName = "畳む対象でない例外はそのまま伝わる")]
    public void TryGetClipboardPng_UnexpectedException_Propagates()
    {
        var act = () =>
            ClipboardImageAttachmentReader.TryGetClipboardPng(
                () => true,
                () => throw new InvalidOperationException("bug")
            );

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>画像が無い・取り出しが null のときは null を返すことを検証する</summary>
    [Fact(DisplayName = "画像が無ければ null を返す")]
    public void TryGetClipboardPng_NoImage_ReturnsNull()
    {
        ClipboardImageAttachmentReader
            .TryGetClipboardPng(() => false, () => SmallBitmap())
            .Should()
            .BeNull();
        ClipboardImageAttachmentReader.TryGetClipboardPng(() => true, () => null).Should().BeNull();
    }

    /// <summary>画像があれば PNG バイト列を返すことを検証する（正常系）</summary>
    [Fact(DisplayName = "画像があれば PNG バイト列を返す")]
    public void TryGetClipboardPng_Image_ReturnsPngBytes()
    {
        var png = ClipboardImageAttachmentReader.TryGetClipboardPng(
            () => true,
            () => SmallBitmap()
        );

        png.Should().NotBeNull();
        png!.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    }
}
