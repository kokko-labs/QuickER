using System.ClientModel;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;

namespace ERDesigner.Services;

/// <summary>AI スキーマ生成クライアントの抽象化（テストではモックへ差し替える）</summary>
public interface IAiSchemaClient
{
    /// <summary>自然言語の要件から <see cref="AiSchemaJson"/> を生成する</summary>
    Task<AiSchemaJson> GenerateAsync(AiGenerationSettings settings, CancellationToken ct = default);
}

/// <summary>OpenAI 公式 SDK（および OpenAI 互換の Ollama）でスキーマ JSON を取得するクライアント</summary>
public class OpenAiSchemaClient : IAiSchemaClient
{
    private const string SystemPromptTemplate =
        @"あなたは熟練のデータベース設計者です。
ユーザーの要件から第3正規形を意識したテーブル設計を行い、必ず指定された JSON スキーマだけを出力してください。
- tables 配列を返し、各テーブルは name / description / memo / columns を持つ。
        - 各 columns 要素は name / dataType / isPrimaryKey / isForeignKey / isNullable / description を持つ。
- テーブル名・カラム名は英数字とアンダースコアのみ。
- 各テーブルに description、各カラムに description を必ず付ける。
- 各テーブルに 1 つ以上の主キー (isPrimaryKey=true) を必ず含める。ただし主キーは原則 1 列のみとし、中間テーブル等で業務上の複合主キーが必須の場合のみ複数列を許可する。
- 各カラムの isNullable を必ず設定する。主キーは false、必須項目や通常の外部キーも false、任意入力の項目だけ true にする。
- 外部キーがあれば isForeignKey=true を付け、relationships にも記述する。外部キー列を同時に主キー（isPrimaryKey=true）にしてはならない。参照元テーブルのPKを引き継ぐ列は isForeignKey=true / isPrimaryKey=false にする。
- type は ""OneToOne"" / ""OneToMany"" / ""ManyToMany"" のいずれか。
- relationships の各要素には constraintName, onDelete, onUpdate も含める。onDelete / onUpdate は ""NO ACTION"" / ""CASCADE"" / ""SET NULL"" / ""SET DEFAULT"" のいずれかを使用する。
- dataType は SQL Server の型 (例: int, bigint, nvarchar(50), datetime2, decimal(10,2), bit) を使用。";

    private const string UpdateExistingInstruction =
        @"- 既存 ER 図の情報を踏まえ、ユーザー要件に応じた『更新後の完全なスキーマ』を返す。
- 既存 ER 図のテーブル名・カラム名の命名規則や単複数の方針は維持する。
- 明示的に削除指示がない既存テーブル・既存カラム・既存リレーションは極力維持する。
- 既存要素の名称変更が必要な場合も、要件から妥当な場合に限る。
- 追加・変更内容が分かるよう、description と memo を適切に更新する。";

    /// <summary>OpenAI の Structured Outputs に渡す強制 JSON スキーマ定義</summary>
    private static readonly byte[] SchemaBytes =
        """
        {
          "type": "object",
          "properties": {
            "tables": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "description": { "type": "string" },
                  "memo": { "type": "string" },
                  "columns": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "dataType": { "type": "string" },
                        "isPrimaryKey": { "type": "boolean" },
                        "isForeignKey": { "type": "boolean" },
                        "isNullable": {
                          "type": "boolean",
                          "description": "NULL を許容する場合は true。主キーは false。必須項目や通常の外部キーは false、任意項目のみ true。"
                        },
                        "description": { "type": "string" }
                      },

                      "required": ["name","dataType","isPrimaryKey","isForeignKey","isNullable","description"],
                      "additionalProperties": false
                    }
                  }
                },

                "required": ["name","description","memo","columns"],
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
                    "type": { "type": "string", "enum": ["OneToOne","OneToMany","ManyToMany"] },
                    "constraintName": { "type": "string" },
                    "onDelete": { "type": "string", "enum": ["NO ACTION","CASCADE","SET NULL","SET DEFAULT"] },
                    "onUpdate": { "type": "string", "enum": ["NO ACTION","CASCADE","SET NULL","SET DEFAULT"] }
                },

                "required": ["sourceTable","targetTable","type","constraintName","onDelete","onUpdate"],
                "additionalProperties": false
              }
            }
          },

          "required": ["tables","relationships"],
          "additionalProperties": false
        }

        """u8.ToArray();

    /// <inheritdoc />
    public async Task<AiSchemaJson> GenerateAsync(AiGenerationSettings settings, CancellationToken ct = default)
    {
        var endpoint = new Uri(settings.ResolveEndpoint());
        // Ollama は API キー不要だが OpenAI SDK は非空キーを要求するためダミーを渡す
        var key = string.IsNullOrEmpty(settings.ApiKey) ? "ollama" : settings.ApiKey;

        var client = new ChatClient(model: settings.Model, credential: new ApiKeyCredential(key), options: new OpenAIClientOptions { Endpoint = endpoint });

        ChatCompletionOptions options;

        try
        {
            // OpenAI 本家は厳密な JSON Schema を強制し、互換プロバイダーには JSON オブジェクト形式のみ要求する
            options = new ChatCompletionOptions
            {
                ResponseFormat =
                    settings.Provider == AiProvider.OpenAI
                        ? ChatResponseFormat.CreateJsonSchemaFormat(jsonSchemaFormatName: "er_schema", jsonSchema: BinaryData.FromBytes(SchemaBytes), jsonSchemaIsStrict: true)
                        : ChatResponseFormat.CreateJsonObjectFormat(),
            };
        }
        catch
        {
            // スキーマ形式が SDK 側で非対応の場合は JSON オブジェクト形式へフォールバックする
            options = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() };
        }

        var messages = new ChatMessage[] { new SystemChatMessage(BuildSystemPrompt(settings)), new UserChatMessage(settings.Prompt) };

        var completion = await client.CompleteChatAsync(messages, options, ct).ConfigureAwait(false);
        var text = completion.Value.Content[0].Text;
        var schema = ParseSchemaResponse(text);

        // 新規生成時のみ命名規則を後処理で強制する（更新モードは既存命名を維持するため正規化しない）
        if (settings.GenerationMode != AiGenerationMode.UpdateExisting)
        {
            schema.NormalizeTableNames(settings.TableNameNumberStyle);
            schema.NormalizeIdentifiers(settings.IdentifierNamingStyle);
        }

        return schema;
    }

    /// <summary>AI 応答テキストから JSON 部分を抽出し <see cref="AiSchemaJson"/> へ解釈する</summary>
    internal static AiSchemaJson ParseSchemaResponse(string text)
    {
        var normalized = ExtractJsonPayload(text);
        var json = JsonSerializer.Deserialize<AiSchemaJson>(normalized, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (json is null)
        {
            throw new InvalidOperationException("AI 応答を JSON として解釈できませんでした。");
        }

        return json;
    }

    /// <summary>コードフェンスや前後の説明文を除去し、JSON オブジェクト本体のみを取り出す</summary>
    private static string ExtractJsonPayload(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JsonException("AI 応答が空です。");
        }

        var trimmed = text.Trim();

        // ```json ... ``` のようなコードフェンスで囲まれている場合は中身のみ取り出す
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');

            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

                if (lastFence >= 0)
                {
                    trimmed = trimmed[..lastFence];
                }

                trimmed = trimmed.Trim();
            }
        }

        // 先頭が { でない場合は最初の { から最後の } までを JSON 本体と見なして切り出す
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');

            if (start >= 0 && end > start)
            {
                trimmed = trimmed.Substring(start, end - start + 1);
            }
        }

        return trimmed;
    }

    /// <summary>命名規則や既存スキーマの指定を反映したシステムプロンプトを組み立てる</summary>
    private static string BuildSystemPrompt(AiGenerationSettings settings)
    {
        var namingInstruction = settings.IdentifierNamingStyle switch
        {
            AiIdentifierNamingStyle.SnakeCase => "- テーブル名・カラム名は必ずスネークケース (例: customer_order, customer_id) にする。",
            _ => "- テーブル名・カラム名は必ずパスカルケース (例: CustomerOrder, CustomerId) にする。",
        };

        var tableNumberInstruction = settings.TableNameNumberStyle switch
        {
            AiTableNameNumberStyle.Plural => "- テーブル名は必ず複数形 (例: Customers, Orders) にする。",
            _ => "- テーブル名は必ず単数形 (例: Customer, Order) にする。",
        };

        if (settings.GenerationMode == AiGenerationMode.UpdateExisting && settings.ExistingDiagram?.Entities.Count > 0)
        {
            // 既存 ER 図は AI が読みやすいよう、出力 JSON と同形の簡潔な構造へ正規化して渡します。
            var existingSchemaJson = JsonSerializer.Serialize(AiSchemaJson.FromDiagram(settings.ExistingDiagram), new JsonSerializerOptions { WriteIndented = true });

            return $"{SystemPromptTemplate}\n{UpdateExistingInstruction}\n以下は現在の ER 図です。この内容を前提に更新してください。\n{existingSchemaJson}";
        }

        return $"{SystemPromptTemplate}\n{namingInstruction}\n{tableNumberInstruction}";
    }
}
