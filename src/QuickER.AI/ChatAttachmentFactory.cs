using System.IO;
using System.Text;
using QuickER.AI.Resources;

namespace QuickER.AI;

/// <summary>添付作成に失敗した理由（呼び出し側でメッセージへ整形する）</summary>
public enum ChatAttachmentError
{
    /// <summary>画像がサイズ上限を超過している（縮小しても収まらない場合を含む）</summary>
    ImageTooLarge,

    /// <summary>PDF がサイズ上限を超過している</summary>
    PdfTooLarge,

    /// <summary>テキストがサイズ上限を超過している（本文インライン展開の上限）</summary>
    TextTooLarge,

    /// <summary>バイナリがサイズ上限を超過している</summary>
    BinaryTooLarge,

    /// <summary>データが空</summary>
    Empty,
}

/// <summary>添付作成の結果（成功なら <see cref="Attachment"/>、失敗なら理由とメッセージ）</summary>
/// <param name="Success">成功したか</param>
/// <param name="Attachment">生成された添付（成功時のみ）</param>
/// <param name="Error">失敗理由（失敗時のみ）</param>
/// <param name="Message">失敗時の表示用メッセージ（成功時は空文字）</param>
public readonly record struct ChatAttachmentResult(
    bool Success,
    ChatAttachment? Attachment,
    ChatAttachmentError? Error,
    string Message
)
{
    /// <summary>成功結果を生成する</summary>
    internal static ChatAttachmentResult Ok(ChatAttachment attachment) =>
        new(true, attachment, null, string.Empty);

    /// <summary>失敗結果を生成する</summary>
    internal static ChatAttachmentResult Fail(ChatAttachmentError error, string message) =>
        new(false, null, error, message);
}

/// <summary>
/// バイト列・ファイルから <see cref="ChatAttachment"/> を生成・検証するファクトリ。
/// 「読めるかは AI に任せる」思想で拡張子は不問とし、内容（マジックバイト・スニッフィング）で
/// 画像 / PDF / テキスト / その他バイナリの 4 種へ分類する。各種別の上限を検証し、
/// 画像がサイズ超過のときは注入された「縮小デリゲート」を試す。
/// WPF 非依存のため縮小の実装自体は持たず、差し込み口（デリゲート）だけを備える。
/// </summary>
public static class ChatAttachmentFactory
{
    /// <summary>MIME タイプ既定値（種別ごと）</summary>
    private const string PdfMediaType = "application/pdf";
    private const string TextMediaType = "text/plain";
    private const string BinaryMediaType = "application/octet-stream";

    /// <summary>
    /// 画像のサイズ超過時に呼ぶ縮小デリゲートの型。
    /// 入力（元バイト列・MIME タイプ）を受け取り、縮小後のバイト列を返す。縮小できないときは null を返す。
    /// </summary>
    /// <remarks>本体（QuickER.AI）は WPF の画像 API を持たないため、実装は Gui 側で注入する。</remarks>
    public delegate byte[]? ImageShrinker(byte[] data, string mediaType);

    /// <summary>ファイルパスから添付を生成する（内容で種別を確定する。拡張子は不問）</summary>
    /// <param name="filePath">読み込むファイルのパス</param>
    /// <param name="shrinker">画像縮小デリゲート（null 可）</param>
    public static ChatAttachmentResult CreateFromFile(
        string filePath,
        ImageShrinker? shrinker = null
    )
    {
        var data = File.ReadAllBytes(filePath);
        return Create(Path.GetFileName(filePath), data, shrinker);
    }

    /// <summary>
    /// ファイル名・バイト列から添付を生成する。内容から種別を判定（画像/PDF/テキスト/バイナリ）し、
    /// 種別ごとの上限を適用する（画像は超過時に縮小を試みる）。拡張子は判定に使わない。
    /// </summary>
    /// <param name="fileName">元ファイル名（表示・書き出し用）</param>
    /// <param name="data">ファイルのバイト列</param>
    /// <param name="shrinker">画像縮小デリゲート（null 可）</param>
    public static ChatAttachmentResult CreateFromBytes(
        string fileName,
        byte[] data,
        ImageShrinker? shrinker = null
    ) => Create(fileName, data, shrinker);

    /// <summary>ファイル名・バイト列から添付を生成する共通処理（内容分類→種別別の上限検証）</summary>
    private static ChatAttachmentResult Create(
        string fileName,
        byte[] data,
        ImageShrinker? shrinker
    )
    {
        if (data.Length == 0)
        {
            return ChatAttachmentResult.Fail(
                ChatAttachmentError.Empty,
                string.Format(Strings.Attachment_Empty, fileName)
            );
        }

        // 内容から種別を確定する（画像→PDF→テキスト→バイナリの優先順で判定）
        var (kind, mediaType) = Classify(data);

        return kind switch
        {
            ChatAttachmentKind.Image => BuildImage(fileName, mediaType, data, shrinker),
            ChatAttachmentKind.Pdf => BuildPdf(fileName, mediaType, data),
            ChatAttachmentKind.Text => BuildText(fileName, mediaType, data),
            _ => BuildBinary(fileName, mediaType, data),
        };
    }

    /// <summary>
    /// バイト列の内容から種別と MIME タイプを判定する。
    /// マジックバイトで画像・PDF を確定し、残りは UTF-8 テキスト判定（BOM・制御文字比率）で
    /// テキスト / その他バイナリへ振り分ける。
    /// </summary>
    internal static (ChatAttachmentKind Kind, string MediaType) Classify(byte[] data)
    {
        var imageMediaType = DetectImageMediaType(data);

        if (imageMediaType is not null)
        {
            return (ChatAttachmentKind.Image, imageMediaType);
        }

        if (IsPdf(data))
        {
            return (ChatAttachmentKind.Pdf, PdfMediaType);
        }

        return IsProbablyText(data)
            ? (ChatAttachmentKind.Text, TextMediaType)
            : (ChatAttachmentKind.Binary, BinaryMediaType);
    }

    /// <summary>画像添付を組み立てる（上限超過時は縮小を試み、なお超過なら拒否する）</summary>
    private static ChatAttachmentResult BuildImage(
        string fileName,
        string mediaType,
        byte[] data,
        ImageShrinker? shrinker
    )
    {
        if (data.LongLength > ChatAttachmentLimits.MaxImageBytes)
        {
            var shrunk = shrinker?.Invoke(data, mediaType);

            if (shrunk is null || shrunk.LongLength > ChatAttachmentLimits.MaxImageBytes)
            {
                return ChatAttachmentResult.Fail(
                    ChatAttachmentError.ImageTooLarge,
                    string.Format(
                        Strings.Attachment_ImageTooLarge,
                        ChatAttachmentLimits.MaxImageBytes / (1024 * 1024),
                        fileName
                    )
                );
            }

            data = shrunk;
        }

        return ChatAttachmentResult.Ok(
            new ChatAttachment(fileName, ChatAttachmentKind.Image, mediaType, data)
        );
    }

    /// <summary>PDF 添付を組み立てる（上限超過なら拒否する。縮小は行わない）</summary>
    private static ChatAttachmentResult BuildPdf(string fileName, string mediaType, byte[] data)
    {
        if (data.LongLength > ChatAttachmentLimits.MaxPdfBytes)
        {
            return ChatAttachmentResult.Fail(
                ChatAttachmentError.PdfTooLarge,
                string.Format(
                    Strings.Attachment_PdfTooLarge,
                    ChatAttachmentLimits.MaxPdfBytes / (1024 * 1024),
                    fileName
                )
            );
        }

        return ChatAttachmentResult.Ok(
            new ChatAttachment(fileName, ChatAttachmentKind.Pdf, mediaType, data)
        );
    }

    /// <summary>テキスト添付を組み立てる（上限超過なら明確なメッセージで拒否する）</summary>
    private static ChatAttachmentResult BuildText(string fileName, string mediaType, byte[] data)
    {
        if (data.LongLength > ChatAttachmentLimits.MaxTextBytes)
        {
            return ChatAttachmentResult.Fail(
                ChatAttachmentError.TextTooLarge,
                string.Format(
                    Strings.Attachment_TextTooLarge,
                    ChatAttachmentLimits.MaxTextBytes / 1024,
                    fileName
                )
            );
        }

        return ChatAttachmentResult.Ok(
            new ChatAttachment(fileName, ChatAttachmentKind.Text, mediaType, data)
        );
    }

    /// <summary>バイナリ添付を組み立てる（上限超過なら拒否する。縮小は行わない）</summary>
    private static ChatAttachmentResult BuildBinary(string fileName, string mediaType, byte[] data)
    {
        if (data.LongLength > ChatAttachmentLimits.MaxBinaryBytes)
        {
            return ChatAttachmentResult.Fail(
                ChatAttachmentError.BinaryTooLarge,
                string.Format(
                    Strings.Attachment_BinaryTooLarge,
                    ChatAttachmentLimits.MaxBinaryBytes / (1024 * 1024),
                    fileName
                )
            );
        }

        return ChatAttachmentResult.Ok(
            new ChatAttachment(fileName, ChatAttachmentKind.Binary, mediaType, data)
        );
    }

    /// <summary>マジックバイト（先頭シグネチャ）から種別を判定する（画像/PDF のみ・不明なら null）</summary>
    /// <remarks>テキスト/バイナリ判定は <see cref="Classify"/> のスニッフィングで行う（マジックバイトを持たないため）。</remarks>
    internal static ChatAttachmentKind? DetectKind(byte[] data)
    {
        if (DetectImageMediaType(data) is not null)
        {
            return ChatAttachmentKind.Image;
        }

        if (IsPdf(data))
        {
            return ChatAttachmentKind.Pdf;
        }

        return null;
    }

    /// <summary>画像のマジックバイトから MIME タイプを判定する（画像でなければ null）</summary>
    private static string? DetectImageMediaType(byte[] data)
    {
        if (IsPng(data))
        {
            return "image/png";
        }

        if (IsJpeg(data))
        {
            return "image/jpeg";
        }

        if (IsGif(data))
        {
            return "image/gif";
        }

        if (IsWebp(data))
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>
    /// バイト列が UTF-8 テキストとして妥当かをスニッフィングする（明快さ優先の簡易判定）。
    /// UTF-8 BOM 付きは即テキスト。NUL バイトを含めばバイナリ。制御文字（改行・タブ等を除く）の
    /// 比率が高ければバイナリ。UTF-8 として厳格にデコードできればテキストとみなす。
    /// </summary>
    internal static bool IsProbablyText(byte[] data)
    {
        // UTF-8 BOM（EF BB BF）はテキスト確定
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return true;
        }

        // NUL バイトはテキストに現れない → バイナリとみなす
        if (Array.IndexOf(data, (byte)0x00) >= 0)
        {
            return false;
        }

        // 制御文字（許容: タブ 0x09・改行 0x0A・復帰 0x0D）の比率が高ければバイナリ
        var controlCount = 0;

        foreach (var b in data)
        {
            if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D)
            {
                controlCount++;
            }
        }

        if ((double)controlCount / data.Length > 0.05)
        {
            return false;
        }

        // 最後に UTF-8 として厳格デコードできるかを確認する（不正シーケンスは例外→バイナリ）
        try
        {
            var strict = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true
            );
            strict.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>PNG シグネチャ（89 50 4E 47 0D 0A 1A 0A）か</summary>
    private static bool IsPng(byte[] d) =>
        StartsWith(d, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    /// <summary>JPEG シグネチャ（FF D8 FF）か</summary>
    private static bool IsJpeg(byte[] d) => StartsWith(d, [0xFF, 0xD8, 0xFF]);

    /// <summary>GIF シグネチャ（GIF87a / GIF89a）か</summary>
    private static bool IsGif(byte[] d) =>
        StartsWith(d, "GIF87a"u8.ToArray()) || StartsWith(d, "GIF89a"u8.ToArray());

    /// <summary>WebP シグネチャ（RIFF????WEBP）か</summary>
    private static bool IsWebp(byte[] d) =>
        d.Length >= 12
        && StartsWith(d, "RIFF"u8.ToArray())
        && d[8] == (byte)'W'
        && d[9] == (byte)'E'
        && d[10] == (byte)'B'
        && d[11] == (byte)'P';

    /// <summary>PDF シグネチャ（%PDF-）か</summary>
    private static bool IsPdf(byte[] d) => StartsWith(d, "%PDF-"u8.ToArray());

    /// <summary>バイト列が指定シグネチャで始まるかを判定する</summary>
    private static bool StartsWith(byte[] data, byte[] signature)
    {
        if (data.Length < signature.Length)
        {
            return false;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            if (data[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }
}
