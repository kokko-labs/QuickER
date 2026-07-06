using System.Text;

namespace QuickER.AI;

/// <summary>
/// テキスト添付を API キー接続のユーザーメッセージ本文へインライン展開する共有ヘルパ。
/// コンテンツ型が API 固定の接続（Anthropic / OpenAI）では、テキストを専用コンテンツブロックに
/// できないため、本文末尾に「ファイル名見出し＋fenced block」で連結する。
/// 画像・PDF は各ドライバがコンテンツブロックのまま扱う（本ヘルパはテキストのみを対象とする）。
/// </summary>
public static class TextAttachmentInliner
{
    /// <summary>
    /// ユーザー本文と添付一覧から、テキスト添付をインライン展開した実効本文を組み立てる。
    /// テキスト添付が無ければ元の本文をそのまま返す（履歴の再送でも同じ結果になり再現性を保つ）。
    /// </summary>
    /// <param name="text">元のユーザー本文</param>
    /// <param name="attachments">添付一覧（テキスト以外は無視する）</param>
    public static string BuildEffectiveText(string text, IReadOnlyList<ChatAttachment>? attachments)
    {
        if (attachments is not { Count: > 0 })
        {
            return text;
        }

        var builder = new StringBuilder(text);

        foreach (var attachment in attachments)
        {
            if (attachment.Kind != ChatAttachmentKind.Text)
            {
                continue;
            }

            builder
                .Append("\n\n【添付ファイル: ")
                .Append(attachment.FileName)
                .Append("】\n```\n")
                .Append(DecodeText(attachment.Data))
                .Append("\n```");
        }

        return builder.ToString();
    }

    /// <summary>テキスト添付のバイト列を UTF-8 文字列へ復号する（BOM は除去する）</summary>
    private static string DecodeText(byte[] data)
    {
        // UTF-8 BOM（EF BB BF）が付いていれば取り除いてから復号する
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        }

        return Encoding.UTF8.GetString(data);
    }
}
