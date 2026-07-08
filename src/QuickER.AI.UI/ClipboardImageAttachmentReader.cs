using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QuickER.AI;
using QuickER.AI.UI.Resources;

namespace QuickER.AI.UI;

/// <summary>
/// クリップボード上の画像を <see cref="ChatAttachment"/> へ変換するリーダー。
/// </summary>
/// <remarks>
/// WPF の <see cref="Clipboard"/> 参照（コードビハインド配線）と、変換ロジック（テスト可能）を分離する。
/// クリップボード画像は常に PNG へエンコードし、ファイル名は「クリップボード画像_yyyyMMdd_HHmmss.png」形式にする。
/// </remarks>
public static class ClipboardImageAttachmentReader
{
    /// <summary>クリップボード画像に付けるファイル名の接頭辞</summary>
    private static string FileNamePrefix => Strings.Attachment_ClipboardImagePrefix;

    /// <summary>クリップボード画像のファイル名を組み立てる（yyyyMMdd_HHmmss のタイムスタンプ付き PNG）</summary>
    /// <param name="timestamp">ファイル名に埋め込む時刻</param>
    public static string BuildFileName(DateTime timestamp) =>
        $"{FileNamePrefix}_{timestamp:yyyyMMdd_HHmmss}.png";

    /// <summary>
    /// PNG バイト列から画像添付を生成する変換ロジック（WPF 非依存・テスト対象）。
    /// タイムスタンプ由来のファイル名を付け、<see cref="ChatAttachmentFactory"/> の検証・縮小を通す。
    /// クリップボード画像専用の入口のため、内容が画像でなければ（PNG エンコードに失敗した想定）失敗を返す。
    /// </summary>
    /// <param name="pngData">PNG エンコード済みのバイト列</param>
    /// <param name="timestamp">ファイル名に使う時刻</param>
    /// <param name="shrinker">画像縮小デリゲート（null 可）</param>
    public static ChatAttachmentResult CreateFromPngBytes(
        byte[] pngData,
        DateTime timestamp,
        ChatAttachmentFactory.ImageShrinker? shrinker = null
    )
    {
        var result = ChatAttachmentFactory.CreateFromBytes(
            BuildFileName(timestamp),
            pngData,
            shrinker
        );

        // 全形式対応のファクトリは非画像も受理するが、ここは画像専用の入口なので画像以外は弾く
        if (result.Success && result.Attachment!.Kind != ChatAttachmentKind.Image)
        {
            return new ChatAttachmentResult(
                false,
                null,
                ChatAttachmentError.Empty,
                Strings.Attachment_ClipboardCaptureFailed
            );
        }

        return result;
    }

    /// <summary>
    /// 現在のクリップボードに画像があれば PNG バイト列を取り出す（無ければ null）。WPF の Clipboard に依存する。
    /// </summary>
    /// <remarks>コードビハインドから呼ぶ入り口。取り出した PNG は <see cref="CreateFromPngBytes"/> へ渡す。</remarks>
    public static byte[]? TryGetClipboardPng()
    {
        if (!Clipboard.ContainsImage())
        {
            return null;
        }

        var image = Clipboard.GetImage();

        if (image is null)
        {
            return null;
        }

        return EncodePng(image);
    }

    /// <summary><see cref="BitmapSource"/> を PNG バイト列へエンコードする</summary>
    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }
}
