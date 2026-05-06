namespace ERDesigner.Services;

/// <summary>AI プロバイダ種別。</summary>
public enum AiProvider
{
    /// <summary>OpenAI 公式 API (api.openai.com)。</summary>
    OpenAi,

    /// <summary>ローカル Ollama (OpenAI 互換 API)。</summary>
    Ollama,
}

/// <summary>AI が生成する識別子名の命名規則。</summary>
public enum AiIdentifierNamingStyle
{
    /// <summary>パスカルケース (例: <c>CustomerOrder</c>)。</summary>
    PascalCase,

    /// <summary>スネークケース (例: <c>customer_order</c>)。</summary>
    SnakeCase,
}

/// <summary>AI が生成するテーブル名の単複数。</summary>
public enum AiTableNameNumberStyle
{
    /// <summary>単数形 (例: <c>Customer</c>)。</summary>
    Singular,

    /// <summary>複数形 (例: <c>Customers</c>)。</summary>
    Plural,
}

/// <summary>AI スキーマ生成リクエストの設定値。</summary>
public class AiGenerationSettings
{
    /// <summary>AI プロバイダ。</summary>
    public AiProvider Provider { get; set; } = AiProvider.OpenAi;

    /// <summary>API キー (Ollama では未使用)。</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>モデル名 (例: <c>gpt-5.4-mini</c>, <c>llama3.1</c>)。</summary>
    public string Model { get; set; } = "gpt-5.4-mini";

    /// <summary>OpenAI 互換のエンドポイント URL。null/空ならプロバイダ既定値。</summary>
    public string? EndpointOverride { get; set; }

    /// <summary>ユーザーが入力したスキーマ要件 (自然言語)。</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>AI が生成するテーブル名・カラム名の命名規則。</summary>
    public AiIdentifierNamingStyle IdentifierNamingStyle { get; set; } = AiIdentifierNamingStyle.PascalCase;

    /// <summary>AI が生成するテーブル名の単複数。</summary>
    public AiTableNameNumberStyle TableNameNumberStyle { get; set; } = AiTableNameNumberStyle.Singular;

    /// <summary>プロバイダ既定のエンドポイント。</summary>
    public string ResolveEndpoint() =>
        !string.IsNullOrWhiteSpace(EndpointOverride)
            ? EndpointOverride!
            : Provider switch
            {
                AiProvider.Ollama => "http://localhost:11434/v1",
                _ => "https://api.openai.com/v1",
            };
}
