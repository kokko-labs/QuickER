using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ChatAttachmentFactory"/> の内容ベース種別判定（画像/PDF/テキスト/バイナリ）・
/// 上限検証・画像縮小デリゲートの呼び出し・超過拒否メッセージを検証するテストクラス。
/// 拡張子は判定に使わない（「読めるかは AI に任せる」思想）。
/// </summary>
public class ChatAttachmentFactoryTests
{
    /// <summary>PNG シグネチャ（89 50 4E 47 0D 0A 1A 0A）＋任意末尾でバイト列を作る</summary>
    private static byte[] PngBytes(int totalLength = 16)
    {
        var data = new byte[Math.Max(totalLength, 8)];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>PDF シグネチャ（%PDF-）＋任意末尾でバイト列を作る</summary>
    private static byte[] PdfBytes(long totalLength = 16)
    {
        var length = (int)Math.Max(totalLength, 5);
        var data = new byte[length];
        var signature = "%PDF-"u8.ToArray();
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>正しい PNG は画像として受理され、MIME・種別が設定されることを検証する</summary>
    [Fact(DisplayName = "正しい PNG は画像として受理される")]
    public void CreateFromBytes_ValidPng_Succeeds()
    {
        var result = ChatAttachmentFactory.CreateFromBytes("figure.png", PngBytes());

        result.Success.Should().BeTrue();
        result.Attachment!.Kind.Should().Be(ChatAttachmentKind.Image);
        result.Attachment!.MediaType.Should().Be("image/png");
    }

    /// <summary>正しい PDF は PDF として受理されることを検証する</summary>
    [Fact(DisplayName = "正しい PDF は PDF として受理される")]
    public void CreateFromBytes_ValidPdf_Succeeds()
    {
        var result = ChatAttachmentFactory.CreateFromBytes("spec.pdf", PdfBytes());

        result.Success.Should().BeTrue();
        result.Attachment!.Kind.Should().Be(ChatAttachmentKind.Pdf);
        result.Attachment!.MediaType.Should().Be("application/pdf");
    }

    /// <summary>拡張子が .png でも中身が PDF なら、内容で PDF と分類されることを検証する（拡張子不問）</summary>
    [Fact(DisplayName = "拡張子より内容を優先する（.png だが中身 PDF は PDF）")]
    public void CreateFromBytes_ContentOverridesExtension()
    {
        var result = ChatAttachmentFactory.CreateFromBytes("fake.png", PdfBytes());

        result.Success.Should().BeTrue();
        result.Attachment!.Kind.Should().Be(ChatAttachmentKind.Pdf);
    }

    /// <summary>UTF-8 テキスト内容は拡張子に依らずテキストとして受理されることを検証する</summary>
    [Fact(DisplayName = "UTF-8 テキストはテキストとして受理される")]
    public void CreateFromBytes_Utf8Text_ClassifiedAsText()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("Hello, これはテキスト。\nsecond line\ttab");

        var result = ChatAttachmentFactory.CreateFromBytes("note.log", data);

        result.Success.Should().BeTrue();
        result.Attachment!.Kind.Should().Be(ChatAttachmentKind.Text);
        result.Attachment!.MediaType.Should().Be("text/plain");
    }

    /// <summary>NUL バイトを含む内容はバイナリとして受理されることを検証する（拡張子が .txt でも内容優先）</summary>
    [Fact(DisplayName = "NUL 含む内容はバイナリとして受理される")]
    public void CreateFromBytes_NulBytes_ClassifiedAsBinary()
    {
        byte[] data = [0x00, 0x01, 0x02, 0xFF, 0x00, 0x42];

        var result = ChatAttachmentFactory.CreateFromBytes("data.txt", data);

        result.Success.Should().BeTrue();
        result.Attachment!.Kind.Should().Be(ChatAttachmentKind.Binary);
        result.Attachment!.MediaType.Should().Be("application/octet-stream");
    }

    /// <summary>不正な UTF-8 シーケンスはバイナリと分類されることを検証する（境界ケース）</summary>
    [Fact(DisplayName = "不正 UTF-8 はバイナリと分類される")]
    public void CreateFromBytes_InvalidUtf8_ClassifiedAsBinary()
    {
        // 0xC3 は 2 バイト UTF-8 の先頭バイトだが後続が無い不正シーケンス
        byte[] data = [0x48, 0x69, 0xC3, 0x28, 0x21];

        var result = ChatAttachmentFactory.CreateFromBytes("weird.txt", data);

        result.Success.Should().BeTrue();
        result.Attachment!.Kind.Should().Be(ChatAttachmentKind.Binary);
    }

    /// <summary>UTF-8 BOM 付き内容はテキストと分類されることを検証する</summary>
    [Fact(DisplayName = "UTF-8 BOM 付きはテキストと分類される")]
    public void CreateFromBytes_Utf8Bom_ClassifiedAsText()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        var body = System.Text.Encoding.UTF8.GetBytes("body");
        var data = bom.Concat(body).ToArray();

        var result = ChatAttachmentFactory.CreateFromBytes("bom.txt", data);

        result.Success.Should().BeTrue();
        result.Attachment!.Kind.Should().Be(ChatAttachmentKind.Text);
    }

    /// <summary>テキストが上限（200KB）を超えると TextTooLarge で明確なメッセージで拒否されることを検証する</summary>
    [Fact(DisplayName = "テキスト超過は明確なメッセージで拒否")]
    public void CreateFromBytes_LargeText_Fails()
    {
        var big = new byte[ChatAttachmentLimits.MaxTextBytes + 1];
        Array.Fill(big, (byte)'a');

        var result = ChatAttachmentFactory.CreateFromBytes("big.txt", big);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ChatAttachmentError.TextTooLarge);
        result.Message.Should().Contain("KB");
    }

    /// <summary>バイナリが上限（32MB）を超えると BinaryTooLarge で拒否されることを検証する</summary>
    [Fact(DisplayName = "バイナリ超過は拒否される")]
    public void CreateFromBytes_LargeBinary_Fails()
    {
        var big = new byte[ChatAttachmentLimits.MaxBinaryBytes + 1];
        big[0] = 0x00; // NUL でバイナリ確定

        var result = ChatAttachmentFactory.CreateFromBytes("big.bin", big);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ChatAttachmentError.BinaryTooLarge);
    }

    /// <summary>上限超過の画像で縮小デリゲートが呼ばれ、収まれば受理されることを検証する</summary>
    [Fact(DisplayName = "画像超過時は縮小デリゲートが呼ばれ収まれば受理")]
    public void CreateFromBytes_LargeImage_ShrinkerReducesAndSucceeds()
    {
        var large = PngBytes((int)ChatAttachmentLimits.MaxImageBytes + 1);
        var shrinkerCalled = false;

        ChatAttachmentFactory.ImageShrinker shrinker = (data, mediaType) =>
        {
            shrinkerCalled = true;
            // 上限内の縮小結果（PNG シグネチャ維持）を返す
            return PngBytes(1024);
        };

        var result = ChatAttachmentFactory.CreateFromBytes("big.png", large, shrinker);

        shrinkerCalled.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Attachment!.Data.Length.Should().Be(1024);
    }

    /// <summary>縮小してもなお上限超過なら ImageTooLarge で拒否され、分かるメッセージが付くことを検証する</summary>
    [Fact(DisplayName = "縮小しても超過なら明確なメッセージで拒否")]
    public void CreateFromBytes_LargeImage_ShrinkerStillTooLarge_Fails()
    {
        var large = PngBytes((int)ChatAttachmentLimits.MaxImageBytes + 1);

        ChatAttachmentFactory.ImageShrinker shrinker = (data, mediaType) =>
            PngBytes((int)ChatAttachmentLimits.MaxImageBytes + 1);

        var result = ChatAttachmentFactory.CreateFromBytes("big.png", large, shrinker);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ChatAttachmentError.ImageTooLarge);
        result.Message.Should().Contain("MB");
    }

    /// <summary>縮小デリゲートが無い（null）状態で画像が上限超過なら拒否されることを検証する</summary>
    [Fact(DisplayName = "縮小なしで画像超過は拒否")]
    public void CreateFromBytes_LargeImage_NoShrinker_Fails()
    {
        var large = PngBytes((int)ChatAttachmentLimits.MaxImageBytes + 1);

        var result = ChatAttachmentFactory.CreateFromBytes("big.png", large, shrinker: null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ChatAttachmentError.ImageTooLarge);
    }

    /// <summary>PDF が上限超過なら PdfTooLarge で拒否されることを検証する（PDF は縮小しない）</summary>
    [Fact(DisplayName = "PDF 超過は拒否される")]
    public void CreateFromBytes_LargePdf_Fails()
    {
        var large = PdfBytes(ChatAttachmentLimits.MaxPdfBytes + 1);

        var result = ChatAttachmentFactory.CreateFromBytes("huge.pdf", large);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ChatAttachmentError.PdfTooLarge);
    }

    /// <summary>空データは Empty で拒否されることを検証する</summary>
    [Fact(DisplayName = "空データは拒否される")]
    public void CreateFromBytes_Empty_Fails()
    {
        var result = ChatAttachmentFactory.CreateFromBytes("empty.png", []);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ChatAttachmentError.Empty);
    }

    /// <summary>各画像シグネチャ（JPEG/GIF/WebP）が画像として判定されることを検証する</summary>
    [Fact(DisplayName = "JPEG/GIF/WebP のマジックバイトを画像と判定する")]
    public void DetectKind_ImageSignatures_ReturnImage()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0x00];
        var gif = "GIF89a"u8.ToArray();
        var webp = new byte[12];
        "RIFF"u8.ToArray().CopyTo(webp, 0);
        "WEBP"u8.ToArray().CopyTo(webp, 8);

        ChatAttachmentFactory.DetectKind(jpeg).Should().Be(ChatAttachmentKind.Image);
        ChatAttachmentFactory.DetectKind(gif).Should().Be(ChatAttachmentKind.Image);
        ChatAttachmentFactory.DetectKind(webp).Should().Be(ChatAttachmentKind.Image);
    }

    /// <summary>未知のバイト列は種別不明（null）になることを検証する</summary>
    [Fact(DisplayName = "未知バイト列は種別不明")]
    public void DetectKind_Unknown_ReturnsNull()
    {
        byte[] unknown = [0x00, 0x01, 0x02, 0x03];

        ChatAttachmentFactory.DetectKind(unknown).Should().BeNull();
    }
}
