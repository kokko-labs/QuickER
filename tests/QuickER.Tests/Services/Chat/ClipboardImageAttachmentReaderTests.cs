using FluentAssertions;
using QuickER.AI;
using QuickER.AI.UI;

namespace QuickER.Tests.Services.Chat;

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

        name.Should().Be("クリップボード画像_20260706_130509.png");
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
        result.Attachment.FileName.Should().Be("クリップボード画像_20260706_130509.png");
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
}
