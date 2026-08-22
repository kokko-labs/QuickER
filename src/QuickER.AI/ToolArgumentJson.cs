using System.Text;
using System.Text.Json;

namespace QuickER.AI;

/// <summary>
/// LLM が出力するツール呼び出し引数 JSON の緩和修復と履歴サニタイズ。
/// </summary>
/// <remarks>
/// <para>
/// ローカル LLM 等の小型モデルは、長いツール引数で壊れた JSON（コードフェンス付き・末尾カンマ・
/// 文字列中の生改行・前後の散文など）を出すことがある。本クラスは 2 段の防御を提供する:
/// (1) <see cref="NormalizeForExecution"/>＝ツール実行前に決定的な修復を試み、直せる壊れ方なら
/// そもそもエラーにしない。(2) <see cref="SanitizeForHistory"/>＝修復不能でも会話履歴へは有効な
/// JSON だけを積む。壊れた引数文字列をそのまま履歴再送すると、履歴を検証するプロバイダー
/// （Ollama の OpenAI 互換層等）が以後すべての要求を HTTP 400 で拒否し、会話が恒久的に
/// 使用不能になるため（OpenAI 本家は履歴を再検証しないので顕在化しない）。
/// </para>
/// <para>
/// 修復は決定的なテキスト変換のみ（コードフェンス除去 → 文字列中の制御文字エスケープ →
/// 末尾カンマ/コメント許容の緩和パース → 前後の散文を落とす {..} 抽出）。推測的な変換
/// （シングルクォート置換等）は誤修復のリスクがあるため行わない。
/// </para>
/// </remarks>
public static class ToolArgumentJson
{
    /// <summary>緩和パースの許容範囲（末尾カンマ・コメント）。修復成功時は厳密 JSON へ再直列化する</summary>
    private static readonly JsonDocumentOptions LenientOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// ツール実行用の引数文字列を解決する。有効ならそのまま・修復できたら修復後・
    /// 修復不能なら原文のまま返す（ツールホストが解析エラーを返し、モデルにリトライさせる経路へ委ねる）。
    /// </summary>
    /// <param name="raw">モデルが出力した引数文字列（null/空は既存の「空＝空オブジェクト」扱いに任せるため素通し）</param>
    public static string NormalizeForExecution(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || IsValidJson(raw))
        {
            return raw ?? string.Empty;
        }

        return TryRepair(raw) ?? raw;
    }

    /// <summary>
    /// 会話履歴の再送用に引数文字列をサニタイズする。有効ならそのまま・修復できたら修復後・
    /// どちらも不能なら空オブジェクト <c>{}</c> を返す（解析エラーはツール結果として既に伝わっており、
    /// 壊れた原文を履歴に残す情報価値より、以後の要求がすべて拒否される害の方が大きい）。
    /// </summary>
    /// <param name="raw">履歴項目が保持する引数文字列</param>
    public static string SanitizeForHistory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "{}";
        }

        if (IsValidJson(raw))
        {
            return raw;
        }

        return TryRepair(raw) ?? "{}";
    }

    /// <summary>
    /// 壊れた JSON の決定的な修復を試みる。成功すれば厳密に有効な JSON 文字列を、不能なら null を返す。
    /// </summary>
    /// <param name="raw">修復対象の文字列</param>
    public static string? TryRepair(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // 1. コードフェンス（```json ... ```）を剝がし、文字列中の生制御文字をエスケープして再判定する
        var candidate = EscapeControlCharsInStrings(StripCodeFence(raw.Trim()));
        var repaired = ParseStrictOrLenient(candidate);

        if (repaired is not null)
        {
            return repaired;
        }

        // 2. 前後に散文が付いている場合に備え、最初の '{' から最後の '}' までを抜き出して再試行する
        var start = candidate.IndexOf('{');
        var end = candidate.LastIndexOf('}');

        if (start >= 0 && end > start)
        {
            return ParseStrictOrLenient(candidate.Substring(start, end - start + 1));
        }

        return null;
    }

    /// <summary>厳密に有効な JSON かを判定する</summary>
    private static bool IsValidJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 厳密パース → 緩和パース（末尾カンマ・コメント許容）の順で解釈し、有効なら厳密 JSON 文字列を返す。
    /// 緩和パースで通った場合は再直列化して末尾カンマ等を除去する。
    /// </summary>
    private static string? ParseStrictOrLenient(string candidate)
    {
        if (IsValidJson(candidate))
        {
            return candidate;
        }

        try
        {
            using var document = JsonDocument.Parse(candidate, LenientOptions);

            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>先頭・末尾のマークダウンコードフェンス（``` または ```json 等の言語タグ付き）を剝がす</summary>
    private static string StripCodeFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var firstLineEnd = value.IndexOf('\n');

        if (firstLineEnd < 0)
        {
            return value;
        }

        var inner = value.Substring(firstLineEnd + 1);
        var closing = inner.LastIndexOf("```", StringComparison.Ordinal);

        return closing >= 0 ? inner.Substring(0, closing).Trim() : inner.Trim();
    }

    /// <summary>
    /// JSON 文字列リテラル内の生の制御文字（改行・タブ等）をエスケープ表現へ置き換える。
    /// モデルが HTML 等の複数行テキストを引数に入れるとき、改行をエスケープし忘れる壊れ方が最も多いため。
    /// 文字列外の制御文字は JSON の空白として合法なので触らない。
    /// </summary>
    private static string EscapeControlCharsInStrings(string value)
    {
        var builder = new StringBuilder(value.Length);
        var inString = false;
        var escaped = false;

        foreach (var ch in value)
        {
            if (inString)
            {
                if (escaped)
                {
                    // 直前がバックスラッシュ＝この文字はエスケープ済みなのでそのまま通す
                    builder.Append(ch);
                    escaped = false;

                    continue;
                }

                if (ch == '\\')
                {
                    builder.Append(ch);
                    escaped = true;

                    continue;
                }

                if (ch == '"')
                {
                    builder.Append(ch);
                    inString = false;

                    continue;
                }

                if (ch < 0x20)
                {
                    builder.Append(
                        ch switch
                        {
                            '\n' => "\\n",
                            '\r' => "\\r",
                            '\t' => "\\t",
                            _ => "\\u" + ((int)ch).ToString("x4"),
                        }
                    );

                    continue;
                }

                builder.Append(ch);

                continue;
            }

            if (ch == '"')
            {
                inString = true;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
