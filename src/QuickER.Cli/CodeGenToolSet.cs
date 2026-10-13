using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Documents;
using QuickER.Mcp;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Settings;

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
                        description = "Path to an existing generation settings JSON file (optional; same semantics as `quicker generate --config`). Call get_generation_config_schema for the full list of keys.",
                    },
                    provider = new
                    {
                        type = "string",
                        @enum = TargetDbmsNames.All,
                        description = "Target DBMS/provider (optional; defaults to the diagram's target DBMS, or sqlserver if unspecified).",
                    },
                    force = new
                    {
                        type = "boolean",
                        description = "Overwrite generated files even if they have been edited by hand since they were generated (optional; default false). Without it, generation writes nothing and lists the edited files when any exist. Set it only after the user confirms their edits can be discarded; otherwise move the edits into a separate file (for example, a partial class) first.",
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
                "Generates a DDL (CREATE TABLE / foreign key) SQL script from the given ER diagram file for the target DBMS dialect, and writes it to a .sql file. The output path must end in .sql and its parent directory must already exist; any other path is rejected without writing anything.",
            DeferLoading = false,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    out_file = new
                    {
                        type = "string",
                        description = "Output file path, which must have a .sql extension (overwritten if it exists). The parent directory is not created.",
                    },
                    provider = new
                    {
                        type = "string",
                        @enum = TargetDbmsNames.All,
                        description = "Target DBMS/provider (optional; defaults to the diagram's target DBMS, or sqlserver if unspecified).",
                    },
                },
                required = new[] { "out_file" },
            },
        };

    /// <summary>
    /// コード生成設定 JSON（quicker.json）の全キーを機械可読 JSON で返す <c>get_generation_config_schema</c> ツールの定義（英語）。
    /// </summary>
    /// <remarks>
    /// docs にアクセスできない外部エージェントが config を自己発見的に書けるようにするための情報系ツール。
    /// 唯一 <c>file</c> 引数を取らない（<see cref="Create"/> で <see cref="FileParameterInjector"/> の対象外にする）。
    /// </remarks>
    public static ToolDefinition GetGenerationConfigSchemaDefinition { get; } =
        new()
        {
            Name = "get_generation_config_schema",
            Description =
                "Returns a machine-readable JSON catalog of every key valid in the code generation settings JSON (quicker.json), which is passed as generate_csharp's `config` argument: each key's name, type, default, category, allowed values, and description, plus cross-key rules and an example. Use it to write a config without external docs. Unlike every other tool, this one takes no arguments (in particular, no `file`).",
            DeferLoading = false,
            InputSchema = new { type = "object", properties = new { } },
        };

    /// <summary>ファイルベースのコード生成ツールセットを生成する</summary>
    /// <returns>公開ツール定義と実行デリゲートを対にした <see cref="McpToolSet"/></returns>
    /// <remarks>
    /// <c>file</c> の注入はツールごとに行う＝図を対象にするツール（<c>generate_csharp</c> / <c>generate_ddl</c>）だけへ
    /// <see cref="FileParameterInjector"/> を適用し、引数不要の情報系ツール <c>get_generation_config_schema</c> は除外する。
    /// </remarks>
    public static McpToolSet Create()
    {
        var definitions = new[] { GenerateCSharpDefinition, GenerateDdlDefinition }
            .Select(FileParameterInjector.Inject)
            .Append(GetGenerationConfigSchemaDefinition)
            .ToList();

        return new McpToolSet(definitions, Dispatch);
    }

    /// <summary>ツール名・引数 JSON を受け取り、UI 言語を英語に固定してツールを実行する</summary>
    /// <remarks>
    /// <para>
    /// 結果テキストには、生成コアが CLI と共有する resx の文言（生成ファイルの一覧・件数・生成器の診断・
    /// 設定エラー・ランタイムパッケージの案内）が入る。MCP の呼び手は外部 AI エージェントで、結果は機械向けの
    /// 英語に統一する（ツール定義と同じ）ため、実行中だけ <see cref="CultureInfo.CurrentUICulture"/> を
    /// <see cref="CultureInfo.InvariantCulture"/>（＝中立 resx の英語）にする。
    /// </para>
    /// <para>
    /// 変えるのは UI 言語だけで <see cref="CultureInfo.CurrentCulture"/> には触れない（日時・数値の書式を CLI・GUI と
    /// 同じに保つ）。この入口は同期で、設定した値は呼び出し元へ残るため、finally で必ず元へ戻す。
    /// UI 言語はスレッド（非同期の流れ）ごとの値なので、並行に届いた別の呼び出しには影響しない。
    /// </para>
    /// </remarks>
    private static (string Result, bool Success) Dispatch(string toolName, string argumentsJson)
    {
        var previousUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        try
        {
            return DispatchCore(toolName, argumentsJson);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUICulture;
        }
    }

    /// <summary>ツール名・引数 JSON を受け取り、<c>file</c> を取り出してツールを実行する</summary>
    private static (string Result, bool Success) DispatchCore(string toolName, string argumentsJson)
    {
        // 引数不要の情報系ツールは file 検証より前に処理する（引数 JSON は参照しない）
        if (toolName == "get_generation_config_schema")
        {
            return (GenerationConfigSchema.BuildJson(), true);
        }

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

        // 図と違う方言を明示したときの警告（以降の結果の先頭へ付す。成功時は将来版の警告の後）
        var providerWarning = DescribeProviderMismatch(args, document.Schema, provider);

        (string, bool) Fail(string message) => (PrependLine(providerWarning, message), false);

        var configPath = GetString(args, "config");
        FileInfo? config = null;

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            if (!File.Exists(configPath))
            {
                return Fail($"Config file not found: {configPath}.");
            }

            config = new FileInfo(configPath);
        }

        // 省略時は false。真偽値以外は黙って既定へ倒さず拒否する（上書きの指定を取り違えない）
        var force = false;

        if (args.TryGetProperty("force", out var forceEl))
        {
            if (forceEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Fail("force must be a boolean (true or false).");
            }

            force = forceEl.GetBoolean();
        }

        // 生成コアの診断（stderr）と生成ファイル一覧（stdout）を StringWriter で直接受け取る
        // （Console を差し替えない＝並列実行中の他テスト・他ツールと競合しない）
        var buffer = new StringWriter();
        int exitCode;

        try
        {
            exitCode = GenerationExecutor.GenerateFromConfig(
                provider,
                document.Schema,
                config,
                new DirectoryInfo(outDir),
                force,
                buffer,
                buffer
            );
        }
        catch (GenerationConfigException ex)
        {
            // 設定エラー（生成前＝設定ファイルの不在・不正 JSON・値の型違い・未対応の Repository 方言）は
            // 捕捉バッファへ乗らないため、メッセージを明示的に付す（バッファ側の未知キー警告も一緒に返す）
            return Fail(Combine(buffer.ToString(), $"C# code generation failed: {ex.Message}"));
        }

        var diagnostics = buffer.ToString();

        if (exitCode == GenerationExecutor.ModifiedFilesExitCode)
        {
            // CLI の --force 案内の代わりに、このツールでの上書き方法を英語で添える
            return Fail(
                Combine(
                    diagnostics,
                    "C# code generation wrote nothing because the files listed below have been edited by hand since they were generated. "
                        + "Ask the user whether their edits can be discarded. If so, call generate_csharp again with force: true; "
                        + "otherwise move the edits into a separate file (for example, a partial class) first."
                )
            );
        }

        if (exitCode != 0)
        {
            return Fail(Combine(diagnostics, "C# code generation failed."));
        }

        var sb = new StringBuilder();
        AppendNewerFormatWarning(sb, document);
        AppendWarningLine(sb, providerWarning);
        sb.Append($"Generated C# code from '{file}' into '{outDir}' (provider: {provider.Name}).")
            .Append('\n');

        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            sb.Append('\n');
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

        var outFileError = ValidateDdlOutputPath(outFile);

        if (outFileError is not null)
        {
            return (outFileError, false);
        }

        var (provider, providerError) = ResolveProviderArg(args, document!.Schema);

        if (provider is null)
        {
            return (providerError!, false);
        }

        // 生成器は GUI のエクスポート（DiagramExportService）と同一 API・同一符号化（BOM 付き UTF-8）。
        // 書き込みは原子的に行い、途中で落ちても出力先を壊れた SQL で残さない
        var ddl = provider.DdlGenerator.Build(document.Schema);
        AtomicFile.WriteAllText(outFile, ddl, Encoding.UTF8);

        var sb = new StringBuilder();
        AppendNewerFormatWarning(sb, document);
        AppendWarningLine(sb, DescribeProviderMismatch(args, document.Schema, provider));
        sb.Append($"Generated {provider.Name} DDL from '{file}' into '{outFile}'.");

        return (sb.ToString(), true);
    }

    /// <summary>DDL 出力先（<c>.sql</c> 限定・親ディレクトリ実在）を検証する。問題なければ null</summary>
    /// <remarks>
    /// <para>
    /// MCP の呼び手は外部 AI エージェントで、出力先は素通しにすると任意パス・任意拡張子のファイルを
    /// DDL で上書きできる書き込みプリミティブになる。柵は <c>create_diagram</c> と同じ流儀＝
    /// 「用途に合う拡張子か」と「親ディレクトリが実在するか」の 2 点だけを見る。
    /// </para>
    /// <para>
    /// 絶対パス・<c>..</c> は拒否しない（<c>generate_ddl</c> は元からユーザー指定の出力先へ書くツールで、
    /// <c>create_diagram</c> / <c>generate_csharp</c> も出力先の位置そのものは制限していない）。
    /// 防ぐべきは「SQL スクリプト以外の何かに化ける」ことと「置き場を黙って掘る」ことの 2 つで、
    /// 位置の制限は MCP サーバを起動する側の作業ディレクトリ・権限の担当。
    /// </para>
    /// </remarks>
    private static string? ValidateDdlOutputPath(string outFile)
    {
        if (!string.Equals(Path.GetExtension(outFile), ".sql", StringComparison.OrdinalIgnoreCase))
        {
            return $"out_file must be a path with a .sql extension: {outFile}. "
                + "generate_ddl only writes SQL scripts and refuses to overwrite anything else.";
        }

        // 親ディレクトリを黙って掘らない（create_diagram と同じ規則）
        var directory = Path.GetDirectoryName(outFile);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            return $"Parent directory does not exist: {directory}. Create the directory first.";
        }

        return null;
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
        var providerName = GenerationExecutor.ResolveProviderName(
            GetString(args, "provider"),
            schema
        );

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
    /// 読み込み（不在の案内・形式の検証・失敗の文言）は ER 図操作ツールと共有の <see cref="DiagramFileReader.Read"/>（英語正本）。
    /// 読み取りは <see cref="DiagramFileGate"/> のロックの中で行う（同じプロセスの ER 図操作ツールが同じファイルを
    /// 保存している最中に読むと、共有違反で読み取りが失敗するため）。生成そのものは読み込んだ文書だけを使うので
    /// ロックの外で行う。
    /// </remarks>
    private static (DiagramDocument? Document, string? Error) LoadDiagram(string file) =>
        DiagramFileGate.Run(() => LoadDiagramCore(file));

    /// <summary>図ファイルを読み込む本体（呼び出し側が <see cref="DiagramFileGate"/> のロックを取っている）</summary>
    /// <remarks>決まり文句（不在の案内・形式の検証）は ER 図操作ツールと共有の <see cref="DiagramFileReader.Read"/></remarks>
    private static (DiagramDocument? Document, string? Error) LoadDiagramCore(string file) =>
        DiagramFileReader.Read(file);

    /// <summary>新フォーマット文書のときのみ、警告行を結果テキストの先頭へ付す（読み取り系は続行する）</summary>
    /// <remarks>文言は ER 図操作ツールと共有の <see cref="DiagramFileReader.NewerFormatWarning"/></remarks>
    private static void AppendNewerFormatWarning(StringBuilder sb, DiagramDocument document)
    {
        if (DiagramFileReader.NewerFormatWarning(document) is { } warning)
        {
            sb.Append(warning).Append('\n').Append('\n');
        }
    }

    /// <summary>
    /// <c>provider</c> を明示し、それが図の対象 DBMS と違うときの警告行を返す（同じ方言・省略時は null）
    /// </summary>
    /// <remarks>
    /// 判定は CLI の generate と共有（<see cref="GenerationExecutor.ProviderDiffersFromDiagram"/>）。生成は続ける＝
    /// 型は変換されず、指定した方言の規則でそのまま解釈されることを、呼び手（AI エージェント）へ伝える。
    /// 図の対象 DBMS はファイル由来の任意文字列なので、1 行に収まるよう改行・制御文字を畳む。
    /// </remarks>
    private static string? DescribeProviderMismatch(
        JsonElement args,
        ErDiagram schema,
        IDatabaseProvider provider
    ) =>
        GenerationExecutor.ProviderDiffersFromDiagram(GetString(args, "provider"), schema)
            ? $"Warning: provider '{provider.Name}' differs from the diagram's target DBMS '{SqlComment.Sanitize(schema.TargetDbms)}'. "
                + $"Column types were not converted; they are read as written under {provider.Name}'s rules. "
                + "To change the dialect, switch the target DBMS in the QuickER GUI, which converts the types first."
            : null;

    /// <summary>警告があれば、その行と空行を結果テキストへ足す（無ければ何もしない）</summary>
    private static void AppendWarningLine(StringBuilder sb, string? warning)
    {
        if (warning is not null)
        {
            sb.Append(warning).Append('\n');
            sb.Append('\n');
        }
    }

    /// <summary>警告があれば、結果テキストの先頭へ 1 行足す（無ければそのまま返す）</summary>
    private static string PrependLine(string? warning, string text) =>
        warning is null ? text : $"{warning}\n{text}";

    /// <summary>捕捉した診断テキストと固定メッセージを結合する（診断が空ならメッセージのみ）</summary>
    private static string Combine(string diagnostics, string message) =>
        string.IsNullOrWhiteSpace(diagnostics) ? message : $"{message}\n{diagnostics}";

    /// <summary>JSON 引数から文字列プロパティを取得する（無い・型不一致なら null）</summary>
    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
}
