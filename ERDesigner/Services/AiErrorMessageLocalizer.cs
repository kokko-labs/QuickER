using System.ClientModel;
using System.Net.Http;
using System.Text.Json;

namespace ERDesigner.Services;

/// <summary>
/// OpenAI / Ollama などから返る例外を、ユーザー向けの日本語メッセージに変換するヘルパー。
/// </summary>
public static class AiErrorMessageLocalizer
{
    /// <summary>例外を日本語メッセージに変換します。</summary>
    /// <param name="ex">発生した例外。</param>
    /// <returns>日本語表示用メッセージ。</returns>
    public static string ToJapanese(Exception ex)
    {
        if (ex is ClientResultException cre)
        {
            return TranslateClientResult(cre);
        }

        if (ex is JsonException)
        {
            return "AI からの応答を JSON として解釈できませんでした。プロンプトを変えて再実行してください。";
        }

        if (ex is TaskCanceledException || ex is OperationCanceledException)
        {
            return "通信がタイムアウトまたはキャンセルされました。ネットワーク接続を確認してください。";
        }

        if (ex is HttpRequestException)
        {
            return "サーバーに接続できませんでした。エンドポイント URL とネットワーク接続を確認してください。" + Environment.NewLine + "詳細: " + ex.Message;
        }

        return "予期しないエラーが発生しました: " + ex.Message;
    }

    private static string TranslateClientResult(ClientResultException ex)
    {
        // レスポンス本文から OpenAI のエラーコードを抽出
        var (code, message) = ParseOpenAiError(ex);
        var status = ex.Status;

        var prefix = $"HTTP {status}";

        switch (code)
        {
            case "insufficient_quota":
                return prefix
                    + " 利用枠不足: OpenAI アカウントのクレジット残高が不足しています。"
                    + Environment.NewLine
                    + "https://platform.openai.com/settings/organization/billing/overview から残高を確認・追加してください。";

            case "invalid_api_key":
                return prefix + " 認証エラー: API キーが無効です。正しいキーを入力するか再発行してください。";

            case "model_not_found":
                return prefix + " モデルが見つかりません: 指定したモデル名がアカウントで利用できません。モデル名を変更してください。";

            case "context_length_exceeded":
                return prefix + " コンテキスト長超過: 入力が長すぎます。要件を短くしてください。";

            case "rate_limit_exceeded":
                return prefix + " レート制限: リクエスト頻度が制限を超えました。しばらく待ってから再実行してください。";

            case "billing_hard_limit_reached":
                return prefix + " 課金上限到達: 月の利用上限に達しました。OpenAI の請求設定を確認してください。";
        }

        return status switch
        {
            401 => prefix + " 認証エラー: API キーが正しくありません。",
            403 => prefix + " 権限エラー: このリソースへのアクセス権がありません。",
            404 => prefix + " 見つかりません: モデル名またはエンドポイント URL が正しいか確認してください。",
            408 => prefix + " タイムアウト: サーバー応答が遅延しています。再実行してください。",
            429 => prefix + " レート制限または利用枠不足です。OpenAI の課金設定を確認してください。",
            >= 500 => prefix + " サーバーエラー: しばらく待ってから再実行してください。" + (string.IsNullOrEmpty(message) ? "" : Environment.NewLine + "詳細: " + message),
            _ => prefix + (string.IsNullOrEmpty(message) ? " 通信エラーが発生しました。" : ": " + message),
        };
    }

    private static (string? Code, string Message) ParseOpenAiError(ClientResultException ex)
    {
        try
        {
            var raw = ex.GetRawResponse()?.Content?.ToString();

            if (string.IsNullOrEmpty(raw))
            {
                return (null, ex.Message);
            }

            using var doc = JsonDocument.Parse(raw);

            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                var msg = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                return (code, msg ?? ex.Message);
            }
        }
        catch
        {
            // JSON でない / 形式が違う場合はそのまま
        }

        return (null, ex.Message);
    }
}
