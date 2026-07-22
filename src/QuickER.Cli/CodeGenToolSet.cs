using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Documents;
using QuickER.Mcp;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Cli;

/// <summary>
/// ファイルベースのコード生成ツール群（<c>generate_csharp</c> / <c>generate_ddl</c>）を
/// <see cref="McpToolSet"/> として組み立てるファクトリ。
/// </summary>
/// <remarks>
/// <see cref="GenerationExecutor"/> / <see cref="GenerationConfigLoader"/> は合成ルート（CLI）在住のため、
/// 逆参照を避けてツール定義もこの CLI プロジェクト側に置く。定義（英語）に <see cref="FileParameterInjector"/> で
/// <c>file</c> 引数を注入し、実行は <c>file</c> を取り出して図を読み込み、既存の生成経路（<c>quicker generate</c>
/// と同一）へ流す。<c>generate</c> / <c>scaffold</c> の挙動には一切触れない（同一の生成コアを共有するのみ）。
/// </remarks>
public static class CodeGenToolSet
{
    /// <summary>注入される <c>file</c> パラメータ名</summary>
    private const string FileParameterName = "file";

    /// <summary>ER 図ファイルから C# コードを生成する <c>generate_csharp</c> ツールの定義（英語）</summary>
    public static ToolDefinition GenerateCSharpDefinition { get; } =
        new()
        {
            Name = "generate_csharp",
            Description =
                "Generates C# code (entities, repositories, etc.) from the given ER diagram file into an output directory, using the same pipeline as `quicker generate`. Optionally takes a generation settings JSON file (same semantics as `quicker generate --config`). The generated file list and diagnostics are returned as text.",
            DeferLoading = false,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    out_dir = new
                    {
                        type = "string",
                        description = "Output directory for the generated files (created if it does not exist).",
                    },
                    config = new
                    {
                        type = "string",
                        description = "Path to an existing generation settings JSON file (optional; same semantics as `quicker generate --config`).",
                    },
                    provider = new
                    {
                        type = "string",
                        @enum = new[] { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" },
                        description = "Target DBMS/provider (optional; defaults to the diagram's target DBMS, or sqlserver if unspecified).",
                    },
                },
                required = new[] { "out_dir" },
            },
        };

    /// <summary>ER 図ファイルから DDL（SQL）を生成して .sql へ書き出す <c>generate_ddl</c> ツールの定義（英語）</summary>
    public static ToolDefinition GenerateDdlDefinition { get; } =
        new()
        {
            Name = "generate_ddl",
            Description =
                "Generates a DDL (CREATE TABLE / foreign key) SQL script from the given ER diagram file for the target DBMS dialect, and writes it to a .sql file.",
            DeferLoading = false,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    out_file = new
                    {
                        type = "string",
                        description = "Output .sql file path (overwritten if it exists).",
                    },
                    provider = new
                    {
                        type = "string",
                        @enum = new[] { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" },
                        description = "Target DBMS/provider (optional; defaults to the diagram's target DBMS, or sqlserver if unspecified).",
                    },
                },
                required = new[] { "out_file" },
            },
        };

    /// <summary>ファイルベースのコード生成ツールセットを生成する</summary>
    /// <returns>公開ツール定義（<c>file</c> 注入済み）と実行デリゲートを対にした <see cref="McpToolSet"/></returns>
    public static McpToolSet Create()
    {
        var definitions = new[] { GenerateCSharpDefinition, GenerateDdlDefinition }
            .Select(FileParameterInjector.Inject)
            .ToList();

        return new McpToolSet(definitions, Dispatch);
    }

    /// <summary>ツール名・引数 JSON を受け取り、<c>file</c> を取り出してツールを実行する</summary>
    private static (string Result, bool Success) Dispatch(string toolName, string argumentsJson)
    {
        JsonElement args;

        try
        {
            args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        }
        catch (JsonException ex)
        {
            return ($"Invalid tool arguments (not valid JSON): {ex.Message}", false);
        }

        if (
            args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty(FileParameterName, out var fileEl)
            || fileEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(fileEl.GetString())
        )
        {
            return (
                $"The '{FileParameterName}' argument (path to the diagram JSON file) is required.",
                false
            );
        }

        var file = fileEl.GetString()!;

        try
        {
            return toolName switch
            {
                "generate_csharp" => GenerateCSharp(file, args),
                "generate_ddl" => GenerateDdl(file, args),
                _ => ($"Unsupported tool: {toolName}", false),
            };
        }
        catch (Exception ex)
        {
            // ツール実行中の例外はエラーテキストとして返し、プロセス（MCP サーバ）を落とさない
            return ($"Error: {ex.Message}", false);
        }
    }

    // ---------------- generate_csharp ----------------

    /// <summary>図を読み込み、既存の生成経路（<c>quicker generate</c> と同一コア）で C# コードを出力する</summary>
    private static (string, bool) GenerateCSharp(string file, JsonElement args)
    {
        var (document, error) = LoadDiagram(file);

        if (error is not null)
        {
            return (error, false);
        }

        var outDir = GetString(args, "out_dir");

        if (string.IsNullOrWhiteSpace(outDir))
        {
            return ("out_dir is required.", false);
        }

        var (provider, providerError) = ResolveProviderArg(args, document!.Schema);

        if (provider is null)
        {
            return (providerError!, false);
        }

        var configPath = GetString(args, "config");
        FileInfo? config = null;

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            if (!File.Exists(configPath))
            {
                return ($"Config file not found: {configPath}.", false);
            }

            config = new FileInfo(configPath);
        }

        // 生成コアの診断（Console.Error）と生成ファイル一覧（Console.Out）を捕捉してツール結果へ含める
        var buffer = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        int exitCode;

        try
        {
            Console.SetOut(buffer);
            Console.SetError(buffer);
            exitCode = GenerationExecutor.GenerateFromConfig(
                provider,
                document.Schema,
                config,
                new DirectoryInfo(outDir)
            );
        }
        catch (RepositoryDialectUnsupportedException ex)
        {
            // 設定検証エラー（生成前）は捕捉バッファへ乗らないため、メッセージを明示的に付す
            return (Combine(buffer.ToString(), $"C# code generation failed: {ex.Message}"), false);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var diagnostics = buffer.ToString();

        if (exitCode != 0)
        {
            return (Combine(diagnostics, "C# code generation failed."), false);
        }

        var sb = new StringBuilder();
        AppendNewerFormatWarning(sb, document);
        sb.AppendLine(
            $"Generated C# code from '{file}' into '{outDir}' (provider: {provider.Name})."
        );

        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            sb.AppendLine();
            sb.Append(diagnostics);
        }

        return (sb.ToString(), true);
    }

    // ---------------- generate_ddl ----------------

    /// <summary>図を読み込み、プロバイダの DDL 生成器で SQL を組み立てて out_file へ書き出す</summary>
    private static (string, bool) GenerateDdl(string file, JsonElement args)
    {
        var (document, error) = LoadDiagram(file);

        if (error is not null)
        {
            return (error, false);
        }

        var outFile = GetString(args, "out_file");

        if (string.IsNullOrWhiteSpace(outFile))
        {
            return ("out_file is required.", false);
        }

        var (provider, providerError) = ResolveProviderArg(args, document!.Schema);

        if (provider is null)
        {
            return (providerError!, false);
        }

        // 生成器は GUI のエクスポート（MainViewModel）と同一 API。UTF-8 で書き出す
        var ddl = provider.DdlGenerator.Build(document.Schema);
        File.WriteAllText(outFile, ddl, Encoding.UTF8);

        var sb = new StringBuilder();
        AppendNewerFormatWarning(sb, document);
        sb.Append($"Generated {provider.Name} DDL from '{file}' into '{outFile}'.");

        return (sb.ToString(), true);
    }

    // ---------------- helpers ----------------

    /// <summary>
    /// provider 引数を解決する。省略時は図の <see cref="ErDiagram.TargetDbms"/>（空なら sqlserver）を用いる。
    /// 未対応名は登録済みプロバイダを列挙したエラーを返す。
    /// </summary>
    private static (IDatabaseProvider? Provider, string? Error) ResolveProviderArg(
        JsonElement args,
        ErDiagram schema
    )
    {
        var providerName = GetString(args, "provider");

        if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = string.IsNullOrWhiteSpace(schema.TargetDbms)
                ? "sqlserver"
                : schema.TargetDbms;
        }

        try
        {
            return (GenerationExecutor.ResolveProvider(providerName), null);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// 図ファイルを読み込む（読み取り系）。不在・非 DiagramDocument はエラー。新フォーマットは拒否せず
    /// 読み込んで続行し、警告は結果テキストへ付す（<see cref="AppendNewerFormatWarning"/>）。
    /// </summary>
    /// <remarks>
    /// <see cref="QuickER.Mcp.Tools.DocumentErDiagramToolHost"/> のガードと同水準だが、あちらは internal で
    /// 再利用できないため CLI 側に最小限を重複実装する（DiagramDocument 検証の 2 箇所目）。
    /// </remarks>
    private static (DiagramDocument? Document, string? Error) LoadDiagram(string file)
    {
        if (!File.Exists(file))
        {
            return (null, $"Diagram file not found: {file}.");
        }

        string text;

        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex)
        {
            return (null, $"Failed to read diagram file '{file}': {ex.Message}");
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            return (null, $"Diagram file '{file}' is not valid JSON: {ex.Message}");
        }

        // JsonStorageService.Load は既定値を補うため、無関係な JSON も空図として読めてしまう。
        // ルートが Version・Schema を持つオブジェクトであることを確認してから読み込む。
        if (root is not JsonObject obj || obj["Version"] is null || obj["Schema"] is not JsonObject)
        {
            return (
                null,
                $"Diagram file '{file}' is not a DiagramDocument (expected an object with 'Version' and 'Schema'). Refusing to treat unrelated JSON as a diagram."
            );
        }

        return (JsonStorageService.Load(file), null);
    }

    /// <summary>新フォーマット文書のときのみ、警告行を結果テキストの先頭へ付す（読み取り系は続行する）</summary>
    private static void AppendNewerFormatWarning(StringBuilder sb, DiagramDocument document)
    {
        if (document.IsNewerFormat)
        {
            sb.AppendLine(
                $"Warning: this diagram was saved in a newer format (version {document.Version} > supported {DiagramDocument.CurrentVersion}); unknown data may be omitted."
            );
            sb.AppendLine();
        }
    }

    /// <summary>捕捉した診断テキストと固定メッセージを結合する（診断が空ならメッセージのみ）</summary>
    private static string Combine(string diagnostics, string message) =>
        string.IsNullOrWhiteSpace(diagnostics) ? message : $"{message}\n{diagnostics}";

    /// <summary>JSON 引数から文字列プロパティを取得する（無い・型不一致なら null）</summary>
    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
}
