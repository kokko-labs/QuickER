using System.ClientModel;
using System.Net.Http;
using System.Text.Json;
using Anthropic.Exceptions;
using QuickER.AI.Resources;

namespace QuickER.AI;

/// <summary>
/// AI 呼び出し (OpenAI / Anthropic / ローカル LLM 等) で発生した例外を、表示言語のユーザー向けメッセージへ変換するヘルパー
/// </summary>
public static class AiErrorMessageLocalizer
{
    /// <summary>例外を表示言語の表示用メッセージへ変換する</summary>
    /// <param name="ex">AI 呼び出しで発生した例外</param>
    /// <returns>ユーザーへ表示するメッセージ（表示言語は OS カルチャに従う）</returns>
    public static string ToUserMessage(Exception ex)
    {
        if (ex is ClientResultException cre)
        {
            return TranslateClientResult(cre);
        }

        if (ex is AnthropicApiException aae)
        {
            // Message は「Status Code: …」と本文の連結なので、本文の error.message を優先して取り出す
            var (_, detail) = ParseErrorBody(aae.ResponseBody, aae.ResponseBody);
            return TranslateStatus((int)aae.StatusCode, detail);
        }

        if (ex is AnthropicIOException)
        {
            return Strings.Ai_ConnectionFailed
                + Environment.NewLine
                + Strings.Ai_DetailLabel
                + ex.Message;
        }

        if (ex is JsonException)
        {
            return Strings.Ai_JsonParseError;
        }

        if (ex is TaskCanceledException || ex is OperationCanceledException)
        {
            return Strings.Ai_Timeout;
        }

        if (ex is HttpRequestException)
        {
            return Strings.Ai_ConnectionFailed
                + Environment.NewLine
                + Strings.Ai_DetailLabel
                + ex.Message;
        }

        return Strings.Ai_Unexpected + ex.Message;
    }

    /// <summary>HTTP エラー (<see cref="ClientResultException"/>) を OpenAI のエラーコードと HTTP ステータスに基づく表示用メッセージへ変換する</summary>
    private static string TranslateClientResult(ClientResultException ex)
    {
        // エラーコードが特定できればコード別の対処方法を優先して案内し、不明な場合は HTTP ステータス別の汎用メッセージへフォールバックする
        var (code, message) = ParseOpenAiError(ex);
        var status = ex.Status;

        var prefix = $"HTTP {status}";

        switch (code)
        {
            case "insufficient_quota":
                return prefix
                    + Strings.Ai_Http_InsufficientQuota
                    + Environment.NewLine
                    + Strings.Ai_Http_InsufficientQuotaBilling;

            case "invalid_api_key":
                return prefix + Strings.Ai_Http_InvalidApiKey;

            case "model_not_found":
                return prefix + Strings.Ai_Http_ModelNotFound;

            case "context_length_exceeded":
                return prefix + Strings.Ai_Http_ContextLengthExceeded;

            case "rate_limit_exceeded":
                return prefix + Strings.Ai_Http_RateLimitExceeded;

            case "billing_hard_limit_reached":
                return prefix + Strings.Ai_Http_BillingHardLimit;
        }

        return TranslateStatus(status, message);
    }

    /// <summary>HTTP ステータスに基づく表示用メッセージへ変換する（OpenAI・Anthropic の両 SDK の HTTP エラーで共有する）</summary>
    /// <param name="status">HTTP ステータスコード</param>
    /// <param name="message">サーバーが返したエラーメッセージ（無ければ空）</param>
    private static string TranslateStatus(int status, string? message)
    {
        var prefix = $"HTTP {status}";

        return status switch
        {
            401 => prefix + Strings.Ai_Http_401,
            403 => prefix + Strings.Ai_Http_403,
            404 => prefix + Strings.Ai_Http_404,
            408 => prefix + Strings.Ai_Http_408,
            429 => prefix + Strings.Ai_Http_429,
            >= 500 => prefix
                + Strings.Ai_Http_ServerError
                + (
                    string.IsNullOrEmpty(message)
                        ? ""
                        : Environment.NewLine + Strings.Ai_DetailLabel + message
                ),
            _ => prefix
                + (
                    string.IsNullOrEmpty(message)
                        ? Strings.Ai_Http_GenericCommError
                        : ": " + message
                ),
        };
    }

    /// <summary>OpenAI エラーレスポンス本文 (JSON) からエラーコードとメッセージを抽出する</summary>
    /// <returns>エラーコード (特定できない場合は null) と表示用メッセージのタプル</returns>
    private static (string? Code, string Message) ParseOpenAiError(ClientResultException ex)
    {
        string? raw;

        try
        {
            raw = ex.GetRawResponse()?.Content?.ToString();
        }
        catch
        {
            raw = null;
        }

        return ParseErrorBody(raw, ex.Message);
    }

    /// <summary>
    /// エラーレスポンス本文 (JSON) の <c>error.code</c> / <c>error.message</c> を抽出する。
    /// OpenAI（<c>{"error":{"code","message"}}</c>）と Anthropic（<c>{"type":"error","error":{"type","message"}}</c>）は
    /// 同じ <c>error.message</c> を持つため共有する（Anthropic は <c>code</c> を持たないので常に null）。
    /// </summary>
    /// <param name="raw">レスポンス本文（無ければ null）</param>
    /// <param name="fallback">本文から取り出せないときに使うメッセージ</param>
    /// <returns>エラーコード (特定できない場合は null) と表示用メッセージのタプル</returns>
    private static (string? Code, string Message) ParseErrorBody(string? raw, string fallback)
    {
        try
        {
            if (string.IsNullOrEmpty(raw))
            {
                return (null, fallback);
            }

            using var doc = JsonDocument.Parse(raw);

            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var code =
                    err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString()
                        : null;
                var msg =
                    err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString()
                        : null;
                return (code, msg ?? fallback);
            }
        }
        catch
        {
            // 本文が JSON でない・想定形式と異なる場合は既定のメッセージを使う
        }

        return (null, fallback);
    }
}
