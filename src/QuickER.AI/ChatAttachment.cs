using System.IO;

namespace QuickER.AI;

/// <summary>チャット添付の種別</summary>
public enum ChatAttachmentKind
{
    /// <summary>画像（PNG / JPEG / GIF / WebP）</summary>
    Image,

    /// <summary>PDF 文書</summary>
    Pdf,

    /// <summary>テキスト（UTF-8 として妥当と判定された内容。API キー接続では本文へインライン展開する）</summary>
    Text,

    /// <summary>その他バイナリ（Claude Code へのみ添付可。Read で読めるかは AI 次第）</summary>
    Binary,
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

/// <summary>
/// チャットエンジンが受け付けられる添付の種別集合（UI の可否判定に使う）。
/// 各接続がサポートする種別のビット和で表す（例 Anthropic=Images|Pdf|Text）。
/// </summary>
[Flags]
public enum AttachmentSupport
{
    /// <summary>添付非対応</summary>
    None = 0,

    /// <summary>画像（PNG / JPEG / GIF / WebP）</summary>
    Images = 1,

    /// <summary>PDF 文書</summary>
    Pdf = 2,

    /// <summary>テキスト（本文インライン展開 / Read）</summary>
    Text = 4,

    /// <summary>その他バイナリ（Claude Code のみ）</summary>
    Binary = 8,
}

/// <summary><see cref="AttachmentSupport"/> と <see cref="ChatAttachmentKind"/> の対応・判定の共有ヘルパ</summary>
public static class AttachmentSupportExtensions
{
    /// <summary>種別を対応する <see cref="AttachmentSupport"/> ビットへ写像する</summary>
    public static AttachmentSupport ToSupportFlag(this ChatAttachmentKind kind) =>
        kind switch
        {
            ChatAttachmentKind.Image => AttachmentSupport.Images,
            ChatAttachmentKind.Pdf => AttachmentSupport.Pdf,
            ChatAttachmentKind.Text => AttachmentSupport.Text,
            _ => AttachmentSupport.Binary,
        };

    /// <summary>指定の種別を受け付けられるか（フラグに該当ビットが立っているか）</summary>
    public static bool Allows(this AttachmentSupport support, ChatAttachmentKind kind) =>
        support.HasFlag(kind.ToSupportFlag());
}

/// <summary>プロバイダー選択から添付範囲を導く共有規則（合成ルートと UI・テストで同一判定を保つため）</summary>
public static class AttachmentSupportResolver
{
    /// <summary>
    /// API キー接続のプロバイダー選択に応じた添付範囲を返す。
    /// Claude=画像＋PDF＋テキスト・OpenAI／ローカル LLM=画像＋テキスト。
    /// テキストはコンテンツ型に依らず本文へインライン展開できるため、画像対応のプロバイダーで許可する。
    /// ローカル LLM は OpenAI 互換 API（同じ image コンテンツパート形式）を使うため OpenAI と同等に扱う
    /// （実際に画像を解釈できるかはモデル次第で、非対応モデルではモデル側のエラーになる）。
    /// バイナリは API キー接続では扱えない（Claude Code の Read 経路のみ）。
    /// </summary>
    public static AttachmentSupport ForApiKeyProvider(AiProvider provider) =>
        provider switch
        {
            AiProvider.Claude => AttachmentSupport.Images
                | AttachmentSupport.Pdf
                | AttachmentSupport.Text,
            AiProvider.OpenAI or AiProvider.LocalLlm => AttachmentSupport.Images
                | AttachmentSupport.Text,
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

    /// <summary>テキスト 1 件あたりの上限バイト数（200KB。本文インライン展開の肥大を防ぐ）</summary>
    public const long MaxTextBytes = 200L * 1024;

    /// <summary>バイナリ 1 件あたりの上限バイト数（32MB。Claude Code 経路のみ）</summary>
    public const long MaxBinaryBytes = 32L * 1024 * 1024;
}
