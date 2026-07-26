using QuickER.Model;

namespace QuickER.AI;

/// <summary>AI プロバイダの種別</summary>
public enum AiProvider
{
    /// <summary>OpenAI 公式 API (api.openai.com)</summary>
    OpenAI,

    /// <summary>Anthropic Claude 公式 API (api.anthropic.com)</summary>
    Claude,

    /// <summary>OpenAI 互換 API のローカル LLM (Ollama / LM Studio / llama.cpp / vLLM 等)</summary>
    LocalLlm,
}

/// <summary>
/// ローカル LLM プロバイダ (<see cref="AiProvider.LocalLlm"/>) の既定値。
/// UI・ドライバ・設定の 3 箇所で同じ値を使うため、ここを唯一の正本とする。
/// </summary>
public static class LocalLlmDefaults
{
    /// <summary>既定のエンドポイント URL (Ollama の既定ポート。他の実装は UI のエンドポイント欄で上書きする)</summary>
    public const string Endpoint = "http://localhost:11434/v1";

    /// <summary>
    /// API キー未入力時に送る代替キー。認証を要求しないローカルサーバーでも
    /// OpenAI SDK が空キーを拒否するため、無害なダミー文字列を送る
    /// (値は Ollama 時代からの互換のため変更しない。受け側は認証不要なので内容に意味はない)。
    /// </summary>
    public const string PlaceholderApiKey = "ollama";
}

/// <summary>AI が生成する識別子名の命名規則</summary>
public enum AiIdentifierNamingStyle
{
    /// <summary>パスカルケース (例: <c>CustomerOrder</c>)</summary>
    PascalCase,

    /// <summary>スネークケース (例: <c>customer_order</c>)</summary>
    SnakeCase,
}

/// <summary>AI が生成するテーブル名の単数形・複数形の方針</summary>
public enum AiTableNameNumberStyle
{
    /// <summary>単数形 (例: <c>Customer</c>)</summary>
    Singular,

    /// <summary>複数形 (例: <c>Customers</c>)</summary>
    Plural,
}

/// <summary>AI スキーマ生成の実行モード</summary>
public enum AiGenerationMode
{
    /// <summary>自然言語要件から ER 図を新規生成する</summary>
    CreateNew,

    /// <summary>既存 ER 図へ追加・変更を加えた更新後の完全なスキーマを生成する</summary>
    UpdateExisting,
}

/// <summary>AI スキーマ生成リクエストの設定値</summary>
public class AiGenerationSettings
{
    /// <summary>使用する AI プロバイダ</summary>
    public AiProvider Provider { get; set; } = AiProvider.OpenAI;

    /// <summary>API キー (ローカル LLM では任意。空なら認証不要サーバー向けのダミーが送られる)</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>モデル名 (例: <c>gpt-5.4-mini</c>, <c>llama3.1</c>)</summary>
    public string Model { get; set; } = "gpt-5.4-mini";

    /// <summary>OpenAI 互換エンドポイント URL の上書き値。null または空ならプロバイダ既定値を使用する</summary>
    public string? EndpointOverride { get; set; }

    /// <summary>ユーザーが入力した自然言語のスキーマ要件</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>AI が生成するテーブル名・カラム名の命名規則</summary>
    public AiIdentifierNamingStyle IdentifierNamingStyle { get; set; } =
        AiIdentifierNamingStyle.PascalCase;

    /// <summary>AI が生成するテーブル名の単数形・複数形の方針</summary>
    public AiTableNameNumberStyle TableNameNumberStyle { get; set; } =
        AiTableNameNumberStyle.Singular;

    /// <summary>新規生成か既存 ER 図の更新かを示す実行モード</summary>
    public AiGenerationMode GenerationMode { get; set; } = AiGenerationMode.CreateNew;

    /// <summary>更新モード時に AI へ渡す既存 ER 図</summary>
    public ErDiagram? ExistingDiagram { get; set; }

    /// <summary>実際に使用するエンドポイント URL を解決する</summary>
    /// <returns><see cref="EndpointOverride"/> が指定されていればその値、未指定ならプロバイダ既定の URL</returns>
    public string ResolveEndpoint() =>
        !string.IsNullOrWhiteSpace(EndpointOverride)
            ? EndpointOverride!
            : Provider switch
            {
                // ローカル LLM の既定は Ollama の既定ポート（他の実装は UI のエンドポイント欄で上書きする）
                AiProvider.LocalLlm => LocalLlmDefaults.Endpoint,
                _ => "https://api.openai.com/v1",
            };
}
