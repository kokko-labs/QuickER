using System.IO;
using System.Windows.Media.Imaging;
using QuickER.AI;

namespace QuickER.AI.UI;

/// <summary>
/// 送信待ち（Pending）の添付 1 件の表示用ラッパー。
/// チップ表示（ファイル名・画像サムネイル・種別）と、元の <see cref="ChatAttachment"/> を橋渡しする。
/// </summary>
public sealed class PendingAttachmentItem
{
    /// <summary>元の中立添付（送信時にそのままエンジンへ渡す）</summary>
    public ChatAttachment Attachment { get; }

    /// <summary>表示ファイル名</summary>
    public string FileName => Attachment.FileName;

    /// <summary>添付の種別（チップのアイコン分岐に使う）</summary>
    public ChatAttachmentKind Kind => Attachment.Kind;

    /// <summary>画像か（サムネイル表示・非画像アイコン表示の分岐に使う）</summary>
    public bool IsImage => Attachment.Kind == ChatAttachmentKind.Image;

    /// <summary>
    /// 非画像チップに表示するアイコン絵文字（PDF=📄・テキスト=📃・バイナリ=📦・画像は空）。
    /// XAML の DataTrigger を種別ごとに増やさず、1 つの TextBlock で切り替えられるようにする。
    /// </summary>
    public string KindIcon =>
        Attachment.Kind switch
        {
            ChatAttachmentKind.Pdf => "📄",
            ChatAttachmentKind.Text => "📃",
            ChatAttachmentKind.Binary => "📦",
            _ => string.Empty,
        };

    /// <summary>画像添付のサムネイル（生成不能・PDF のときは null）</summary>
    public BitmapImage? Thumbnail { get; }

    /// <summary>中立添付からチップ用表示アイテムを生成する（画像はサムネイルを試みる）</summary>
    /// <param name="attachment">元の添付</param>
    public PendingAttachmentItem(ChatAttachment attachment)
    {
        Attachment = attachment;
        Thumbnail = IsImage ? TryCreateThumbnail(attachment.Data) : null;
    }

    /// <summary>バイト列から小さなサムネイル画像を生成する（デコード不能なら null）</summary>
    private static BitmapImage? TryCreateThumbnail(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            // 表示に十分な小さめの復号（メモリ節約・チップ用途）
            image.DecodePixelWidth = 96;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception)
        {
            // WPF がデコードできない画像はサムネイルなしのチップにする（送信自体は妨げない）
            return null;
        }
    }
}
