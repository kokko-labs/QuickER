using System;
using System.ClientModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;

namespace ERDesigner.Services;

/// <summary>AI スキーマ生成クライアントの抽象化。テストではモックに差し替えられます。</summary>
public interface IAiSchemaClient
{
    /// <summary>自然言語の要件から <see cref="AiSchemaJson"/> を生成します。</summary>
    Task<AiSchemaJson> GenerateAsync(AiGenerationSettings settings, CancellationToken ct = default);
}

/// <summary>
/// OpenAI 公式 SDK (および OpenAI 互換の Ollama) を使ってスキーマ JSON を取得するクライアント。
/// </summary>
public class OpenAiSchemaClient : IAiSchemaClient
{
    private const string SystemPrompt = @"あなたは熟練のデータベース設計者です。
ユーザーの要件から第3正規形を意識したテーブル設計を行い、必ず指定された JSON スキーマだけを出力してください。
- テーブル名・カラム名は英数字とアンダースコアのみ。
- 各テーブルに 1 つ以上の主キー (isPrimaryKey=true) を必ず含める。
- 外部キーがあれば isForeignKey=true を付け、relationships にも記述する。
- type は ""OneToOne"" / ""OneToMany"" / ""ManyToMany"" のいずれか。
- dataType は SQL Server の型 (例: int, bigint, nvarchar(50), datetime2, decimal(10,2), bit) を使用。";

    /// <summary>強制 JSON スキーマ (Structured Outputs)。</summary>
    private static readonly byte[] SchemaBytes = """
        {
          "type": "object",
          "properties": {
            "entities": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "displayName": { "type": "string" },
                  "tableName": { "type": "string" },
                  "memo": { "type": "string" },
                  "columns": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "dataType": { "type": "string" },
                        "isPrimaryKey": { "type": "boolean" },
                        "isForeignKey": { "type": "boolean" }
                      },
                      "required": ["name","dataType","isPrimaryKey","isForeignKey"],
                      "additionalProperties": false
                    }
                  }
                },
                "required": ["displayName","tableName","memo","columns"],
                "additionalProperties": false
              }
            },
            "relationships": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "sourceTable": { "type": "string" },
                  "targetTable": { "type": "string" },
                  "type": { "type": "string", "enum": ["OneToOne","OneToMany","ManyToMany"] }
                },
                "required": ["sourceTable","targetTable","type"],
                "additionalProperties": false
              }
            }
          },
          "required": ["entities","relationships"],
          "additionalProperties": false
        }
        """u8.ToArray();

    /// <inheritdoc />
    public async Task<AiSchemaJson> GenerateAsync(AiGenerationSettings settings, CancellationToken ct = default)
    {
        var endpoint = new Uri(settings.ResolveEndpoint());
        // Ollama は API キー不要だが OpenAI SDK は非空が必要なのでダミーを渡す
        var key = string.IsNullOrEmpty(settings.ApiKey) ? "ollama" : settings.ApiKey;

        var client = new ChatClient(
            model: settings.Model,
            credential: new ApiKeyCredential(key),
            options: new OpenAIClientOptions { Endpoint = endpoint });

        ChatCompletionOptions options;
        try
        {
            // OpenAI 本家は Structured Outputs (JSON Schema strict) を強制
            options = new ChatCompletionOptions
            {
                ResponseFormat = settings.Provider == AiProvider.OpenAi
                    ? ChatResponseFormat.CreateJsonSchemaFormat(
                        jsonSchemaFormatName: "er_schema",
                        jsonSchema: BinaryData.FromBytes(SchemaBytes),
                        jsonSchemaIsStrict: true)
                    : ChatResponseFormat.CreateJsonObjectFormat()
            };
        }
        catch
        {
            options = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() };
        }

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(settings.Prompt)
        };

        var completion = await client.CompleteChatAsync(messages, options, ct).ConfigureAwait(false);
        var text = completion.Value.Content[0].Text;

        return ParseSchemaResponse(text);
    }

    internal static AiSchemaJson ParseSchemaResponse(string text)
    {
        var normalized = ExtractJsonPayload(text);
        var json = JsonSerializer.Deserialize<AiSchemaJson>(normalized, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (json is null) throw new InvalidOperationException("AI 応答を JSON として解釈できませんでした。");
        return json;
    }

    private static string ExtractJsonPayload(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new JsonException("AI 応答が空です。");

        var trimmed = text.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                    trimmed = trimmed[..lastFence];
                trimmed = trimmed.Trim();
            }
        }

        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
                trimmed = trimmed.Substring(start, end - start + 1);
        }

        return trimmed;
    }
}
