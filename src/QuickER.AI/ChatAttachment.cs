using System.IO;

namespace QuickER.AI;

/// <summary>チャット添付の種別</summary>
public enum ChatAttachmentKind
{
    /// <summary>画像（PNG / JPEG / GIF / WebP）</summary>
    Image,

    /// <summary>PDF 文書</summary>
    Pdf,
}

/// <summary>
/// チャット／モック会話へ同梱する添付ファイル 1 件（WPF 非依存の中立表現）。
/// 種別・ファイル名・MIME タイプ・バイト列を保持し、各バックエンドドライバがこれを SDK 固有の
/// コンテンツブロックへ変換する。生成・検証は <see cref="ChatAttachmentFactory"/> の責務。
/// </summary>
/// <param name="FileName">元ファイル名（表示・作業フォルダ書き出し用。パスは含めない）</param>
/// <param name="Kind">種別（画像 / PDF）</param>
/// <param name="MediaType">MIME タイプ（例 <c>image/png</c> / <c>application/pdf</c>）</param>
/// <param name="Data">ファイルのバイト列</param>
public sealed record ChatAttachment(
    string FileName,
    ChatAttachmentKind Kind,
    string MediaType,
    byte[] Data
)
{
    /// <summary>バイト列を base64 文字列へ変換する（画像/PDF ブロック組み立て用）</summary>
    public string ToBase64() => Convert.ToBase64String(Data);
}

/// <summary>チャットエンジンが受け付けられる添付の範囲（UI の可否判定に使う）</summary>
public enum AttachmentSupport
{
    /// <summary>添付非対応</summary>
    None,

    /// <summary>画像のみ対応</summary>
    Images,

    /// <summary>画像と PDF に対応</summary>
    ImagesAndPdf,
}

/// <summary>プロバイダー選択から添付範囲を導く共有規則（合成ルートと UI・テストで同一判定を保つため）</summary>
public static class AttachmentSupportResolver
{
    /// <summary>
    /// API キー接続のプロバイダー選択に応じた添付範囲を返す。
    /// Claude=画像＋PDF・OpenAI=画像・その他（Ollama 等）=なし。
    /// </summary>
    public static AttachmentSupport ForApiKeyProvider(AiProvider provider) =>
        provider switch
        {
            AiProvider.Claude => AttachmentSupport.ImagesAndPdf,
            AiProvider.OpenAI => AttachmentSupport.Images,
            _ => AttachmentSupport.None,
        };
}

/// <summary>添付の上限・対応形式に関する共有定数</summary>
public static class ChatAttachmentLimits
{
    /// <summary>画像 1 枚あたりの上限バイト数（5MB）</summary>
    public const long MaxImageBytes = 5L * 1024 * 1024;

    /// <summary>1 メッセージあたりの画像枚数上限</summary>
    public const int MaxImagesPerMessage = 5;

    /// <summary>PDF 1 件あたりの上限バイト数（32MB。Anthropic 上限に整合）</summary>
    public const long MaxPdfBytes = 32L * 1024 * 1024;
}
