using System.Text.Json.Nodes;
using QuickER.CodeGen.CSharp;

namespace QuickER.Cli;

/// <summary>
/// コード生成設定 JSON（quicker.json）で有効な全キーの機械可読カタログ（英語）。
/// </summary>
/// <remarks>
/// <para>
/// docs にアクセスできない外部 AI エージェントが、設定 JSON を自己発見的に書けるようにするための
/// 自己記述メタデータ。MCP ツール <c>get_generation_config_schema</c> がこのカタログを JSON 化して返す。
/// </para>
/// <para>
/// キーの正体は <see cref="GenerationConfigLoader"/> がリフレクションで（<c>node.Deserialize&lt;CodeGenerationOptions&gt;</c>）
/// 認識する集合＝<see cref="CodeGenerationOptions"/> の init 設定可能なインスタンスプロパティである。したがって
/// <see cref="Keys"/> は <see cref="CodeGenerationOptions"/> のそれと 1:1 で対応し、
/// <c>GenerationConfigSchemaTests</c> がリフレクションで完全一致（名前・型・既定値）を強制する
/// （将来オプションが増えてカタログ未更新なら赤になる）。<c>OutputPath</c> は <see cref="CodeGenerationOptions"/> の
/// プロパティではなく <c>OutputFileName</c> への別名（ローダーが橋渡し）のため独立キーとしては載せず、
/// <c>OutputFileName</c> の説明で言及する。
/// </para>
/// </remarks>
public static class GenerationConfigSchema
{
    /// <summary>設定 JSON のトップレベル説明文</summary>
    private const string SchemaDescription =
        "Configuration keys for the code generation settings JSON (quicker.json), passed as generate_csharp's `config` argument. "
        + "Keys are case-insensitive. Every key is optional; omitting one uses the listed default.";

    /// <summary>
    /// quicker.json の全キー（カテゴリ順＝output mode → namespaces → generation targets → value objects →
    /// data access → remote support → runtime &amp; documentation → attributes → output path）。
    /// </summary>
    public static IReadOnlyList<GenerationConfigKey> Keys { get; } =
    [
        // 出力モード
        new(
            "SplitFilesByCategory",
            "boolean",
            false,
            "Output mode",
            "Split output into one file and namespace per category (Entity / EditModel / Mapper / Repository / ValueObject / Runtime)."
        ),
        new(
            "LayeredOutput",
            "boolean",
            false,
            "Output mode",
            "Split generated files into per-layer subfolders (Domain / Presentation / Infrastructure / Server) under out_dir. Implies SplitFilesByCategory."
        ),
        new(
            "DomainLayerDirectory",
            "string",
            null,
            "Output mode",
            "Domain layer folder for LayeredOutput, relative to out_dir. Falls back to \"Domain\" when blank. Absolute paths and \"..\" are rejected."
        ),
        new(
            "PresentationLayerDirectory",
            "string",
            null,
            "Output mode",
            "Presentation layer folder for LayeredOutput, relative to out_dir. Falls back to \"Presentation\" when blank. Absolute paths and \"..\" are rejected."
        ),
        new(
            "InfrastructureLayerDirectory",
            "string",
            null,
            "Output mode",
            "Infrastructure layer folder for LayeredOutput, relative to out_dir. Falls back to \"Infrastructure\" when blank. Absolute paths and \"..\" are rejected."
        ),
        new(
            "ServerLayerDirectory",
            "string",
            null,
            "Output mode",
            "Server layer folder for LayeredOutput, relative to out_dir; only used when remote services are generated. Falls back to \"Server\" when blank. Absolute paths and \"..\" are rejected."
        ),
        // 名前空間
        new(
            "RootNamespace",
            "string",
            "Generated",
            "Namespaces",
            "The root namespace of the generated code (falls back to \"Generated\" when blank)."
        ),
        new(
            "RuntimeNamespace",
            "string",
            null,
            "Namespaces",
            "Namespace for the shared runtime infrastructure when SplitFilesByCategory is true; falls back to {RootNamespace}.Runtime. Ignored when UseRuntimePackages is true."
        ),
        new(
            "EntityNamespace",
            "string",
            null,
            "Namespaces",
            "Namespace for Entity classes when SplitFilesByCategory is true; falls back to RootNamespace."
        ),
        new(
            "EditModelNamespace",
            "string",
            null,
            "Namespaces",
            "Namespace for EditModel classes when SplitFilesByCategory is true; falls back to RootNamespace."
        ),
        new(
            "MapperNamespace",
            "string",
            null,
            "Namespaces",
            "Namespace for Mapper classes when SplitFilesByCategory is true; falls back to RootNamespace."
        ),
        new(
            "RepositoryNamespace",
            "string",
            null,
            "Namespaces",
            "Namespace for Repository classes when SplitFilesByCategory is true; falls back to RootNamespace."
        ),
        new(
            "ValueObjectNamespace",
            "string",
            null,
            "Namespaces",
            "Namespace for value object classes when SplitFilesByCategory is true; falls back to RootNamespace."
        ),
        // 生成対象
        new(
            "GenerateEditModels",
            "boolean",
            true,
            "Generation targets",
            "Generate WPF-binding-friendly EditModel classes. Entity classes are always generated (no key toggles them)."
        ),
        new(
            "GenerateMappers",
            "boolean",
            true,
            "Generation targets",
            "Generate Mapper classes that convert between Entity and EditModel."
        ),
        // 値オブジェクト
        new(
            "GenerateValueObjects",
            "boolean",
            false,
            "Value objects",
            "Generate a per-column value object type (such as CustomerIdValue) for every column."
        ),
        new(
            "UseGuidKeyForStringPrimaryKey",
            "boolean",
            false,
            "Value objects",
            "Make a string primary key a GuidKey value object (only when GenerateValueObjects is true and the primary key is a string)."
        ),
        // DB アクセス
        new(
            "GenerateRepositories",
            "boolean",
            false,
            "Data access",
            "Generate the QuickER Repository (a lightweight mini-ORM). No DB-access code is generated by default."
        ),
        new(
            "RepositoryDialects",
            "string[]",
            null,
            "Data access",
            "Dialects for which to emit the QuickER Repository (multi-target when two or more). When null or empty, a single dialect is derived from the provider / diagram target DBMS.",
            AllowedValues: ["sqlserver", "sqlite"]
        ),
        new(
            "ExcludeUnboundedBinaryColumns",
            "boolean",
            false,
            "Data access",
            "Mark unbounded binary columns (varbinary(max) / image / bytea, unbounded BLOB) with [UnboundedBinaryColumn] and exclude them from the QuickER Repository's SELECT / UPDATE (INSERT still writes all columns)."
        ),
        new(
            "GenerateEfCore",
            "boolean",
            false,
            "Data access",
            "Generate the EF Core QuickErDbContext, Fluent configuration, and EF Core Repository implementations."
        ),
        new(
            "GenerateInMemoryRepositories",
            "boolean",
            false,
            "Data access",
            "Generate DB-independent in-memory Repository implementations for prototyping and testing (raw-SQL methods throw)."
        ),
        new(
            "GenerateSyncSupport",
            "boolean",
            false,
            "Data access",
            "Generate the bidirectional sync support (engine, per-table descriptors, journaling decorators, direct differential sources, DI) for a server (SQL Server) plus local (SQLite) setup. Requires GenerateRepositories, exactly those two RepositoryDialects, and at least one table with a single primary-key column (a rowversion column makes that table incremental; tables without one sync last-write-wins)."
        ),
        // リモート対応
        new(
            "GenerateRemoteContracts",
            "boolean",
            false,
            "Remote support",
            "Additionally generate the remote-operation interface I{Entity}RemoteRepository (purely additive; existing code keeps compiling)."
        ),
        new(
            "GenerateRemoteServices",
            "boolean",
            false,
            "Remote support",
            "Generate HTTP + JSON client / server implementations for the remote surface; automatically implies GenerateRemoteContracts."
        ),
        // ランタイム・ドキュメント
        new(
            "UseRuntimePackages",
            "boolean",
            false,
            "Runtime & documentation",
            "Do not emit the fixed runtime code; reference the QuickER.Runtime.* NuGet packages instead."
        ),
        new(
            "GenerateApiDocs",
            "boolean",
            false,
            "Runtime & documentation",
            "Also output an API reference Markdown ({base name}.g.md, English canonical) alongside the generated code."
        ),
        new(
            "IncludeJapaneseApiDocs",
            "boolean",
            false,
            "Runtime & documentation",
            "Also produce the Japanese API reference Markdown ({base name}.ja.g.md); has no effect unless GenerateApiDocs is true."
        ),
        new(
            "ApiDocsDirectory",
            "string",
            null,
            "Runtime & documentation",
            "Output subfolder for the API reference Markdown, relative to out_dir (e.g. \"docs\"; several segments are allowed). Blank means out_dir itself. Absolute paths and \"..\" are rejected. Has no effect unless GenerateApiDocs is true; independent of LayeredOutput."
        ),
        new(
            "ApiDocsFileName",
            "string",
            null,
            "Runtime & documentation",
            "File name for the API reference Markdown. The extension is normalized to \".g.md\" and the Japanese version reuses the same base name (\".ja.g.md\"). Blank means the derived name: the output file base name, or the fixed \"ApiDocs.g.md\" when files are split. File names only; path separators are rejected (use ApiDocsDirectory for the folder). Has no effect unless GenerateApiDocs is true."
        ),
        // 属性
        new(
            "IncludeDataAnnotations",
            "boolean",
            true,
            "Attributes",
            "Apply DataAnnotations ([Required] / [MaxLength], etc.) and the DB-definition meta attributes ([DbTableMeta] / [DbColumnMeta])."
        ),
        new(
            "IncludeJsonIgnoreOnParentNavigation",
            "boolean",
            true,
            "Attributes",
            "Apply [JsonIgnore] to parent-reference navigations (guards against circular references during JSON serialization)."
        ),
        // 出力先
        new(
            "OutputFileName",
            "string",
            "QuickEREntities.g.cs",
            "Output path",
            "File name for single-file output; \".g.cs\" is appended if missing (ignored when SplitFilesByCategory is true). In the config JSON the GUI / docs also accept the alias `OutputPath`, whose file-name part is used as this name; the output directory is always generate_csharp's out_dir."
        ),
    ];

    /// <summary>キーをまたぐ相関・排他ルール（型検査では表せない制約）</summary>
    /// <remarks>
    /// 実装側の硬いエラー（<c>CSharpCodeGenerationService</c> の検証群）と双方向で対応させる。
    /// ここに書いたのに実装が黙って通す／実装が止めるのにここに無い、のどちらも外部エージェントを誤らせる。
    /// </remarks>
    public static IReadOnlyList<string> Rules { get; } =
    [
        "Entity classes are always generated; there is no key to toggle them.",
        "A repository contract is generated when any of GenerateRepositories, GenerateEfCore, or GenerateInMemoryRepositories is true (all default to false); with none of them no data-access code is produced.",
        "GenerateMappers requires GenerateEditModels, because a Mapper converts between an Entity and its EditModel.",
        "GenerateRepositories / GenerateEfCore / GenerateInMemoryRepositories require IncludeDataAnnotations, because the runtime reads [Table] / [Key] / [Column] by reflection.",
        "Multi-target RepositoryDialects (two or more effective dialects) cannot be combined with GenerateEfCore.",
        "GenerateRemoteServices implies GenerateRemoteContracts.",
        "GenerateRemoteContracts / GenerateRemoteServices require a repository contract (GenerateRepositories, GenerateEfCore, or GenerateInMemoryRepositories); asking for them without one is a generation error.",
        "GenerateSyncSupport requires GenerateRepositories, exactly the two RepositoryDialects \"sqlserver\" and \"sqlite\", and at least one table with a single primary-key column.",
        "UseGuidKeyForStringPrimaryKey applies only when GenerateValueObjects is true and the primary key is a string.",
        "RepositoryDialects supports only \"sqlserver\" and \"sqlite\"; when null or empty, a single dialect is derived from the provider / diagram target DBMS.",
        "The namespace keys (RuntimeNamespace, EntityNamespace, EditModelNamespace, MapperNamespace, RepositoryNamespace, ValueObjectNamespace) apply only when SplitFilesByCategory is true.",
        "The layer directory keys (DomainLayerDirectory, PresentationLayerDirectory, InfrastructureLayerDirectory, ServerLayerDirectory) apply only when LayeredOutput is true; ServerLayerDirectory only matters when remote services are also generated.",
        "When LayeredOutput is true, blank namespace keys derive their defaults from the layer folders (path separators become dots, e.g. folder \"MyApp.Domain/Generated\" gives namespaces under MyApp.Domain.Generated), so the folders and namespaces stay aligned; explicit namespace keys still win. A layer folder that cannot form a C# namespace (a hyphen and so on) is a generation error unless every namespace in that layer is set explicitly.",
    ];

    /// <summary>
    /// カタログを機械可読 JSON テキスト（英語・整形済み）へ組み立てる。
    /// </summary>
    /// <remarks>
    /// null 既定値（<c>RepositoryDialects</c> や分割時名前空間）は <c>"default": null</c> として明示的に出す。
    /// <c>allowedValues</c> は指定のあるキーだけに出す（無いキーには省く）。
    /// </remarks>
    public static string BuildJson()
    {
        var keys = new JsonArray();

        foreach (var key in Keys)
        {
            var node = new JsonObject
            {
                ["name"] = key.Name,
                ["type"] = key.Type,
                ["default"] = ToNode(key.Default),
                ["category"] = key.Category,
            };

            if (key.AllowedValues is { Count: > 0 })
            {
                node["allowedValues"] = new JsonArray(
                    key.AllowedValues.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()
                );
            }

            node["description"] = key.Description;
            keys.Add(node);
        }

        var root = new JsonObject
        {
            ["description"] = SchemaDescription,
            ["keys"] = keys,
            ["rules"] = new JsonArray(
                Rules.Select(rule => (JsonNode?)JsonValue.Create(rule)).ToArray()
            ),
            ["example"] = new JsonObject
            {
                ["RootNamespace"] = "MyApp.Generated",
                ["GenerateRepositories"] = true,
                ["RepositoryDialects"] = new JsonArray("sqlite"),
            },
        };

        return root.ToJsonString(
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );
    }

    /// <summary>カタログの既定値（bool / string / null）を JSON ノードへ変換する</summary>
    private static JsonNode? ToNode(object? value) =>
        value switch
        {
            null => null,
            bool b => JsonValue.Create(b),
            string s => JsonValue.Create(s),
            _ => JsonValue.Create(value.ToString()),
        };
}

/// <summary>
/// コード生成設定 JSON（quicker.json）の 1 キーのカタログ項目。
/// </summary>
/// <param name="Name">キー名（<see cref="CodeGenerationOptions"/> のプロパティ名と一致）</param>
/// <param name="Type">JSON 上の型（<c>boolean</c> / <c>string</c> / <c>string[]</c>）</param>
/// <param name="Default">既定値（bool / string / null）</param>
/// <param name="Category">分類（表示・整理用）</param>
/// <param name="Description">簡潔な技術英語の説明</param>
/// <param name="AllowedValues">取り得る値の限定一覧（無ければ null）</param>
public sealed record GenerationConfigKey(
    string Name,
    string Type,
    object? Default,
    string Category,
    string Description,
    IReadOnlyList<string>? AllowedValues = null
);
