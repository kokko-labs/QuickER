using System.IO;

namespace QuickER.AI;

/// <summary>添付作成に失敗した理由（呼び出し側でメッセージへ整形する）</summary>
public enum ChatAttachmentError
{
    /// <summary>対応していない拡張子・形式</summary>
    UnsupportedFormat,

    /// <summary>中身（マジックバイト）が拡張子と一致しない・判別できない</summary>
    ContentMismatch,

    /// <summary>画像がサイズ上限を超過している（縮小しても収まらない場合を含む）</summary>
    ImageTooLarge,

    /// <summary>PDF がサイズ上限を超過している</summary>
    PdfTooLarge,

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
/// 種別判定は拡張子とマジックバイトの両方で行い、上限（画像 5MB・PDF 32MB）を検証する。
/// 画像がサイズ超過のときは注入された「縮小デリゲート」を試し、なお超過なら拒否する。
/// WPF 非依存のため縮小の実装自体は持たず、差し込み口（デリゲート）だけを備える。
/// </summary>
public static class ChatAttachmentFactory
{
    /// <summary>対応拡張子（小文字・ドット付き）と MIME タイプ・種別の対応表</summary>
    private static readonly IReadOnlyDictionary<
        string,
        (string MediaType, ChatAttachmentKind Kind)
    > ExtensionMap = new Dictionary<string, (string, ChatAttachmentKind)>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        [".png"] = ("image/png", ChatAttachmentKind.Image),
        [".jpg"] = ("image/jpeg", ChatAttachmentKind.Image),
        [".jpeg"] = ("image/jpeg", ChatAttachmentKind.Image),
        [".gif"] = ("image/gif", ChatAttachmentKind.Image),
        [".webp"] = ("image/webp", ChatAttachmentKind.Image),
        [".pdf"] = ("application/pdf", ChatAttachmentKind.Pdf),
    };

    /// <summary>
    /// 画像のサイズ超過時に呼ぶ縮小デリゲートの型。
    /// 入力（元バイト列・MIME タイプ）を受け取り、縮小後のバイト列を返す。縮小できないときは null を返す。
    /// </summary>
    /// <remarks>本体（QuickER.AI）は WPF の画像 API を持たないため、実装は Gui 側で注入する。</remarks>
    public delegate byte[]? ImageShrinker(byte[] data, string mediaType);

    /// <summary>対応拡張子の一覧（ドット付き・小文字。UI のファイルフィルタ生成用）</summary>
    public static IReadOnlyCollection<string> SupportedExtensions => ExtensionMap.Keys.ToArray();

    /// <summary>ファイルパスから添付を生成する（拡張子とマジックバイトで種別を確定する）</summary>
    /// <param name="filePath">読み込むファイルのパス</param>
    /// <param name="shrinker">画像縮小デリゲート（null 可）</param>
    public static ChatAttachmentResult CreateFromFile(
        string filePath,
        ImageShrinker? shrinker = null
    )
    {
        var extension = Path.GetExtension(filePath);

        if (!ExtensionMap.ContainsKey(extension))
        {
            return ChatAttachmentResult.Fail(
                ChatAttachmentError.UnsupportedFormat,
                $"対応していない形式です: {Path.GetFileName(filePath)}（対応: PNG/JPEG/GIF/WebP/PDF）"
            );
        }

        var data = File.ReadAllBytes(filePath);
        return Create(Path.GetFileName(filePath), extension, data, shrinker);
    }

    /// <summary>
    /// ファイル名・バイト列から添付を生成する。拡張子で候補種別を決め、マジックバイトで実体を検証し、
    /// 種別ごとの上限を適用する（画像は超過時に縮小を試みる）。
    /// </summary>
    /// <param name="fileName">元ファイル名（拡張子を含む）</param>
    /// <param name="data">ファイルのバイト列</param>
    /// <param name="shrinker">画像縮小デリゲート（null 可）</param>
    public static ChatAttachmentResult CreateFromBytes(
        string fileName,
        byte[] data,
        ImageShrinker? shrinker = null
    ) => Create(fileName, Path.GetExtension(fileName), data, shrinker);

    /// <summary>ファイル名・拡張子・バイト列から添付を生成する共通処理</summary>
    private static ChatAttachmentResult Create(
        string fileName,
        string extension,
        byte[] data,
        ImageShrinker? shrinker
    )
    {
        if (!ExtensionMap.TryGetValue(extension, out var mapped))
        {
            return ChatAttachmentResult.Fail(
                ChatAttachmentError.UnsupportedFormat,
                $"対応していない形式です: {fileName}（対応: PNG/JPEG/GIF/WebP/PDF）"
            );
        }

        if (data.Length == 0)
        {
            return ChatAttachmentResult.Fail(
                ChatAttachmentError.Empty,
                $"ファイルが空です: {fileName}"
            );
        }

        // マジックバイトから実体の種別を判定し、拡張子由来の候補種別と一致するか確認する
        var detected = DetectKind(data);

        if (detected is null || detected.Value != mapped.Kind)
        {
            return ChatAttachmentResult.Fail(
                ChatAttachmentError.ContentMismatch,
                $"ファイルの中身が {extension} と一致しません: {fileName}"
            );
        }

        return mapped.Kind == ChatAttachmentKind.Image
            ? BuildImage(fileName, mapped.MediaType, data, shrinker)
            : BuildPdf(fileName, mapped.MediaType, data);
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
                    $"画像が上限（{ChatAttachmentLimits.MaxImageBytes / (1024 * 1024)}MB）を超えています: {fileName}"
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
                $"PDF が上限（{ChatAttachmentLimits.MaxPdfBytes / (1024 * 1024)}MB）を超えています: {fileName}"
            );
        }

        return ChatAttachmentResult.Ok(
            new ChatAttachment(fileName, ChatAttachmentKind.Pdf, mediaType, data)
        );
    }

    /// <summary>マジックバイト（先頭シグネチャ）から種別を判定する（不明なら null）</summary>
    internal static ChatAttachmentKind? DetectKind(byte[] data)
    {
        if (IsPng(data) || IsJpeg(data) || IsGif(data) || IsWebp(data))
        {
            return ChatAttachmentKind.Image;
        }

        if (IsPdf(data))
        {
            return ChatAttachmentKind.Pdf;
        }

        return null;
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
