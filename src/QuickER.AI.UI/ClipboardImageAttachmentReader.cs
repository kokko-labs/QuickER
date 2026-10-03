using System.IO;
using System.Runtime.InteropServices;
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
    /// <remarks>
    /// コードビハインドから呼ぶ入り口。取り出した PNG は <see cref="CreateFromPngBytes"/> へ渡す。
    /// 読み取れなかったときも null を返す（理由は <see cref="TryGetClipboardPng(Func{bool}, Func{BitmapSource?})"/>）。
    /// </remarks>
    public static byte[]? TryGetClipboardPng() =>
        TryGetClipboardPng(Clipboard.ContainsImage, Clipboard.GetImage);

    /// <summary>
    /// クリップボードの読み取り手段を受け取り、画像があれば PNG バイト列を返す（無い・読み取れなければ null）。
    /// </summary>
    /// <param name="containsImage">クリップボードに画像があるかを問う手段</param>
    /// <param name="getImage">クリップボードの画像を取り出す手段</param>
    /// <remarks>
    /// <para>
    /// 呼び手は Ctrl+V のキーハンドラで、null なら既定のテキスト貼り付けに委ねる。
    /// 読み取りの失敗を例外のまま返すとキーハンドラから抜けてアプリが落ちるため、次の失敗は「画像なし」として扱う。
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="ExternalException"/>（<see cref="COMException"/> を含む）: 他のプロセスがクリップボードを開いている
    /// （CLIPBRD_E_CANT_OPEN）。画像の変換で WIC が失敗したときもこの型で届く</item>
    /// <item><see cref="NotSupportedException"/> / <see cref="FileFormatException"/>: クリップボードのビットマップを
    /// WIC が解釈できない</item>
    /// </list>
    /// <para>それ以外の例外（メモリ不足など）は黙らせない。</para>
    /// </remarks>
    internal static byte[]? TryGetClipboardPng(
        Func<bool> containsImage,
        Func<BitmapSource?> getImage
    )
    {
        try
        {
            if (!containsImage())
            {
                return null;
            }

            var image = getImage();

            if (image is null)
            {
                return null;
            }

            return EncodePng(image);
        }
        catch (Exception ex)
            when (ex is ExternalException or NotSupportedException or FileFormatException)
        {
            return null;
        }
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
