using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Cli.Resources;
using QuickER.CodeGen.CSharp;
using QuickER.Documents;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Cli;

/// <summary>
/// quicker CLI のエントリポイント。<c>generate</c>（ER図JSON→コード）と
/// <c>scaffold</c>（DB直結→コード）の 2 サブコマンドを提供する。
/// </summary>
public static class CliApp
{
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>CLI が対応する DB プロバイダのレジストリ（新 DBMS 対応時はここへ実装を追加する）</summary>
    private static readonly DatabaseProviderRegistry Providers = new([
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ]);

    /// <summary>引数を解析してコマンドを実行する</summary>
    public static Task<int> InvokeAsync(string[] args)
    {
        TrySetUtf8Output();

        var root = new RootCommand(Strings.Cli_RootDescription);
        root.Subcommands.Add(BuildGenerateCommand());
        root.Subcommands.Add(BuildScaffoldCommand());
        return root.Parse(args).InvokeAsync();
    }

    /// <summary>日本語メッセージの文字化けを避けるため標準出力を UTF-8 にする（リダイレクト時の失敗は無視）</summary>
    private static void TrySetUtf8Output()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // 出力がリダイレクトされている場合などは設定できないが、致命的ではないため無視する
        }
    }

    // ---------------- generate ----------------

    private static Command BuildGenerateCommand()
    {
        var schema = new Option<FileInfo>("--schema")
        {
            Description = Strings.Cli_Opt_Schema,
            Required = true,
        };
        var output = new Option<DirectoryInfo>("--out")
        {
            Description = Strings.Cli_Opt_Out,
            Required = true,
        };
        var config = new Option<FileInfo>("--config") { Description = Strings.Cli_Opt_Config };
        var provider = ProviderOption();
        var generation = new GenerationOptionSet();

        var command = new Command("generate", Strings.Cli_Cmd_Generate)
        {
            schema,
            output,
            config,
            provider,
        };

        foreach (var option in generation.Options)
        {
            command.Add(option);
        }

        command.SetAction(parseResult =>
            RunGenerate(
                parseResult.GetValue(schema)!,
                parseResult.GetValue(output)!,
                parseResult.GetValue(config),
                parseResult.GetValue(provider)!,
                parseResult,
                generation
            )
        );

        return command;
    }

    private static int RunGenerate(
        FileInfo schemaFile,
        DirectoryInfo output,
        FileInfo? config,
        string providerName,
        ParseResult parseResult,
        GenerationOptionSet generation
    )
    {
        if (!schemaFile.Exists)
        {
            Console.Error.WriteLine(
                string.Format(Strings.Cli_SchemaFileNotFound, schemaFile.FullName)
            );
            return 1;
        }

        IDatabaseProvider provider;
        try
        {
            provider = Providers.Get(providerName);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var document = JsonStorageService.Load(schemaFile.FullName);

        // 新しいフォーマットの文書は未知のプロパティを黙って無視するため、警告してから続行する
        if (document.IsNewerFormat)
        {
            Console.Error.WriteLine(
                string.Format(
                    Strings.Cli_SchemaNewerFormatWarning,
                    document.Version,
                    DiagramDocument.CurrentVersion
                )
            );
        }

        CodeGenerationOptions options;
        try
        {
            options = LoadOptions(config, provider, parseResult, generation);
        }
        catch (RepositoryDialectUnsupportedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var dialectMappers = ResolveDialectTypeMappers(options);
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            dialectMappers,
            document.Schema,
            options
        );
        return WriteResult(result, output, options);
    }

    // ---------------- scaffold ----------------

    private static Command BuildScaffoldCommand()
    {
        var connection = new Option<string>("--connection")
        {
            Description = Strings.Cli_Opt_Connection,
            Required = true,
        };
        var output = new Option<DirectoryInfo>("--out")
        {
            Description = Strings.Cli_Opt_Out,
            Required = true,
        };
        var config = new Option<FileInfo>("--config") { Description = Strings.Cli_Opt_Config };
        var provider = ProviderOption();
        var generation = new GenerationOptionSet();

        var command = new Command("scaffold", Strings.Cli_Cmd_Scaffold)
        {
            connection,
            output,
            config,
            provider,
        };

        foreach (var option in generation.Options)
        {
            command.Add(option);
        }

        command.SetAction(
            (parseResult, cancellationToken) =>
                RunScaffoldAsync(
                    parseResult.GetValue(connection)!,
                    parseResult.GetValue(output)!,
                    parseResult.GetValue(config),
                    parseResult.GetValue(provider)!,
                    parseResult,
                    generation,
                    cancellationToken
                )
        );

        return command;
    }

    private static async Task<int> RunScaffoldAsync(
        string connectionString,
        DirectoryInfo output,
        FileInfo? config,
        string providerName,
        ParseResult parseResult,
        GenerationOptionSet generation,
        CancellationToken cancellationToken
    )
    {
        IDatabaseProvider provider;
        try
        {
            provider = Providers.Get(providerName);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        SchemaImportResult imported;
        try
        {
            imported = await provider
                .SchemaImporter.ImportAsync(connectionString, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Format(Strings.Cli_SchemaImportFailed, ex.Message));
            return 1;
        }

        var diagram = new ErDiagram
        {
            Entities = imported.Entities.ToList(),
            Relationships = imported.Relationships.ToList(),
        };
        CodeGenerationOptions options;
        try
        {
            options = LoadOptions(config, provider, parseResult, generation);
        }
        catch (RepositoryDialectUnsupportedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var dialectMappers = ResolveDialectTypeMappers(options);
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            dialectMappers,
            diagram,
            options
        );
        return WriteResult(result, output, options);
    }

    // ---------------- 共有 ----------------

    private static Option<string> ProviderOption() =>
        new("--provider")
        {
            Description = Strings.Cli_Opt_Provider,
            DefaultValueFactory = _ => SqlServerProvider.ProviderName,
        };

    /// <summary>
    /// 設定ファイル（quicker.json）を読み、CLI フラグ（設定キーと 1:1 対応する kebab-case フラグ群）で
    /// 上書きして生成オプションを構築する。優先順位は全キー一律「CLI フラグ ＞ 設定ファイル ＞ 既定値」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="generation"/> の各フラグは、指定された（＝<c>null</c> でない）値だけを設定 JSON の
    /// 該当キーへ上書きする（表駆動）。bool フラグは三値（未指定＝設定ファイルの値 / <c>--flag</c>＝true /
    /// <c>--flag false</c>＝false）、文字列フラグは空白でなければ上書きする。
    /// </para>
    /// <para>
    /// 表適用後に 2 つの後処理を行う。(1) <c>--repository-dialects</c> の特例＝フラグ・設定ファイルとも
    /// <see cref="CodeGenerationOptions.RepositoryDialects"/> 未指定なら <paramref name="provider"/> の名前
    /// （図の TargetDbms から導出）を単一要素で設定する。(2) 出力先の橋渡し＝設定 JSON に <c>OutputPath</c> があり
    /// <c>OutputFileName</c> が無ければ、<c>Path.GetFileName(OutputPath)</c>（非空のとき）を <c>OutputFileName</c> へ導出する
    /// （コアは従来どおり出力ファイル名のみを扱うため）。
    /// </para>
    /// <para>
    /// QuickER 版 Repository 生成（<c>GenerateRepositories</c>）が要求され、かつ実効方言に未対応方言が含まれる場合は
    /// <see cref="RepositoryDialectUnsupportedException"/> を送出する。
    /// </para>
    /// </remarks>
    private static CodeGenerationOptions LoadOptions(
        FileInfo? config,
        IDatabaseProvider provider,
        ParseResult parseResult,
        GenerationOptionSet generation
    )
    {
        var node = config is { Exists: true }
            ? JsonNode.Parse(File.ReadAllText(config.FullName))?.AsObject() ?? new JsonObject()
            : new JsonObject();

        // 表駆動: CLI で指定された各フラグ（null でないもの）だけを設定 JSON の該当キーへ上書きする
        generation.ApplyOverrides(parseResult, node);

        // 後処理1: RepositoryDialects の特例（フラグ・設定ファイルとも未指定なら図の方言で単一導出する）
        var repositoryDialects = parseResult.GetValue(generation.RepositoryDialects);

        if (!string.IsNullOrWhiteSpace(repositoryDialects))
        {
            var dialects = repositoryDialects
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            SetNodeValue(
                node,
                "RepositoryDialects",
                new JsonArray(dialects.Select(dialect => JsonValue.Create(dialect)).ToArray())
            );
        }
        else if (FindProperty(node, "RepositoryDialects") is not JsonArray { Count: > 0 })
        {
            // 設定ファイルに RepositoryDialects（非空）があればそれを温存し、無ければ図の方言（provider.Name）を
            // 単一要素で設定する（GUI で選んだ対象 DB がこの経路で CLI に伝わる）。
            SetNodeValue(
                node,
                "RepositoryDialects",
                new JsonArray(JsonValue.Create(provider.Name))
            );
        }

        // 後処理2: OutputPath → OutputFileName の導出（コアは出力ファイル名のみを扱うため橋渡しする）。
        // まず --output-path フラグ（指定時）を設定 JSON へ反映してから、そのファイル名部分を OutputFileName へ導出する
        var outputPath = parseResult.GetValue(generation.OutputPath);

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            SetNodeValue(node, "OutputPath", JsonValue.Create(outputPath));
        }

        DeriveOutputFileName(node);

        var options =
            node.Deserialize<CodeGenerationOptions>(OptionsJson) ?? new CodeGenerationOptions();

        if (options.GenerateRepositories)
        {
            IReadOnlyList<string> effectiveDialects;
            try
            {
                effectiveDialects = options.EffectiveRepositoryDialects;
            }
            catch (ArgumentException ex)
            {
                throw new RepositoryDialectUnsupportedException(ex.Message);
            }

            var unsupported = effectiveDialects
                .Where(dialect =>
                    !CodeGenerationOptions.SupportedRepositoryDialects.Contains(
                        dialect,
                        StringComparer.OrdinalIgnoreCase
                    )
                )
                .ToList();

            if (unsupported.Count > 0)
            {
                throw new RepositoryDialectUnsupportedException(
                    string.Format(
                        Strings.Cli_RepositoryDialectUnsupported,
                        string.Join(", ", unsupported),
                        string.Join(", ", CodeGenerationOptions.SupportedRepositoryDialects)
                    )
                );
            }
        }

        return options;
    }

    /// <summary>
    /// 設定 JSON に <c>OutputPath</c> があり <c>OutputFileName</c> が無ければ、そのファイル名部分を
    /// <c>OutputFileName</c> へ導出する（大文字小文字非依存でキーを探す＝GUI は camelCase で書き出すため）。
    /// </summary>
    private static void DeriveOutputFileName(JsonObject node)
    {
        // 既に OutputFileName があれば尊重する（手書き設定の明示指定を上書きしない）
        if (FindProperty(node, "OutputFileName") is not null)
        {
            return;
        }

        if (
            FindProperty(node, "OutputPath") is JsonValue outputPathValue
            && outputPathValue.TryGetValue(out string? outputPath)
            && !string.IsNullOrWhiteSpace(outputPath)
        )
        {
            var fileName = Path.GetFileName(outputPath);

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                node["OutputFileName"] = fileName;
            }
        }
    }

    /// <summary>
    /// 設定 JSON の指定キーを大文字小文字非依存で上書きする。既存の別綴りキー（例 camelCase の GUI 出力）を
    /// 取り除いてから正準キー（PascalCase）で設定する＝綴り違いの二重キーが残らないようにする。
    /// </summary>
    internal static void SetNodeValue(JsonObject node, string key, JsonNode? value)
    {
        var duplicates = node.Where(pair =>
                string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)
            )
            .Select(pair => pair.Key)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            node.Remove(duplicate);
        }

        node[key] = value;
    }

    /// <summary>設定 JSON から指定キーの値を大文字小文字非依存で探す（無ければ null）</summary>
    private static JsonNode? FindProperty(JsonObject node, string key)
    {
        foreach (var pair in node)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// QuickER 版 Repository の実効方言（<see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/>）ごとに、
    /// CLI のプロバイダレジストリから方言別の型マッパを解決する。
    /// </summary>
    /// <remarks>レジストリに存在しない方言名は除外する（生成本体側で図の方言の辞書へ代替される）</remarks>
    private static IReadOnlyDictionary<string, IColumnTypeMapper> ResolveDialectTypeMappers(
        CodeGenerationOptions options
    )
    {
        var mappers = new Dictionary<string, IColumnTypeMapper>(StringComparer.OrdinalIgnoreCase);

        foreach (var dialect in options.EffectiveRepositoryDialects)
        {
            if (Providers.TryGet(dialect, out var provider))
            {
                mappers[dialect] = provider.TypeMapper;
            }
        }

        return mappers;
    }

    /// <summary>生成結果の診断を表示し、エラーが無ければファイルを書き出す。終了コードを返す</summary>
    /// <remarks>
    /// <paramref name="options"/>.<see cref="CodeGenerationOptions.UseRuntimePackages"/> が有効な場合、
    /// 生成成功後に必要な PackageReference の案内（<see cref="RuntimePackageReferenceGuidance"/>）を続けて出力する。
    /// </remarks>
    private static int WriteResult(
        CodeGenerationResult result,
        DirectoryInfo output,
        CodeGenerationOptions options
    )
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"[{diagnostic.Severity}] {diagnostic.Message}");
        }

        if (result.HasErrors)
        {
            Console.Error.WriteLine(Strings.Cli_GenerationAborted);
            return 1;
        }

        Directory.CreateDirectory(output.FullName);
        var written = new GeneratedFileWriter().WriteFiles(output.FullName, result);

        foreach (var file in written)
        {
            Console.WriteLine(string.Format(Strings.Cli_GeneratedFile, file));
        }

        Console.WriteLine(string.Format(Strings.Cli_GeneratedCount, written.Count));

        if (options.UseRuntimePackages)
        {
            Console.WriteLine();

            foreach (
                var line in RuntimePackageReferenceGuidance.BuildGuidanceLines(
                    options,
                    RuntimePackages.ResolveGuidanceVersion()
                )
            )
            {
                Console.WriteLine(line);
            }
        }

        return 0;
    }
}

/// <summary>
/// generate / scaffold が共有する「設定キー＝kebab-case フラグ」の生成系オプション束。
/// </summary>
/// <remarks>
/// quicker.json / codegen-settings.json の各設定キーに 1:1 対応する CLI フラグを定義し、CLI で指定された
/// （＝<c>null</c> でない）値だけを設定 JSON へ上書きする表駆動の橋渡しを担う。bool フラグは三値
/// （未指定＝設定ファイルの値 / <c>--flag</c>＝true / <c>--flag false</c>＝false）で、
/// <c>Option&lt;bool?&gt;</c> と <see cref="ArgumentArity.ZeroOrOne"/> ＋カスタムパーサで表現する。
/// </remarks>
internal sealed class GenerationOptionSet
{
    /// <summary>文字列フラグ（設定キー名 → Option）。指定時は空白でなければ設定 JSON の該当キーを上書きする</summary>
    private readonly List<(string Key, Option<string?> Option)> _stringFlags = new();

    /// <summary>三値 bool フラグ（設定キー名 → Option）。値ありのとき設定 JSON の該当キーを上書きする</summary>
    private readonly List<(string Key, Option<bool?> Option)> _boolFlags = new();

    /// <summary>QuickER 版 Repository の対象方言（カンマ区切り）。未指定時の単一導出は <see cref="CliApp"/> の後処理が担う</summary>
    public Option<string?> RepositoryDialects { get; } =
        new("--repository-dialects") { Description = Strings.Cli_Opt_RepositoryDialects };

    /// <summary>出力先パス（設定キー <c>OutputPath</c> と同義）。CLI はそのファイル名部分のみを出力ファイル名として使う</summary>
    public Option<string?> OutputPath { get; } =
        new("--output-path") { Description = Strings.Cli_Opt_OutputPath };

    public GenerationOptionSet()
    {
        // 出力モード
        AddBool(
            "SplitFilesByCategory",
            "--split-files-by-category",
            Strings.Cli_Opt_SplitFilesByCategory
        );

        // 名前空間
        AddString("NamespaceName", "--namespace-name", Strings.Cli_Opt_NamespaceName);
        AddString("RuntimeNamespace", "--runtime-namespace", Strings.Cli_Opt_RuntimeNamespace);
        AddString("EntityNamespace", "--entity-namespace", Strings.Cli_Opt_EntityNamespace);
        AddString(
            "EditModelNamespace",
            "--edit-model-namespace",
            Strings.Cli_Opt_EditModelNamespace
        );
        AddString("MapperNamespace", "--mapper-namespace", Strings.Cli_Opt_MapperNamespace);
        AddString(
            "RepositoryNamespace",
            "--repository-namespace",
            Strings.Cli_Opt_RepositoryNamespace
        );
        AddString(
            "ValueObjectNamespace",
            "--value-object-namespace",
            Strings.Cli_Opt_ValueObjectNamespace
        );
        AddString("EfCoreNamespace", "--ef-core-namespace", Strings.Cli_Opt_EfCoreNamespace);

        // 生成対象
        AddBool("GenerateEditModels", "--generate-edit-models", Strings.Cli_Opt_GenerateEditModels);
        AddBool("GenerateMappers", "--generate-mappers", Strings.Cli_Opt_GenerateMappers);

        // 値オブジェクト
        AddBool(
            "GenerateValueObjects",
            "--generate-value-objects",
            Strings.Cli_Opt_GenerateValueObjects
        );
        AddBool(
            "UseGuidKeyForStringPrimaryKey",
            "--use-guid-key-for-string-primary-key",
            Strings.Cli_Opt_UseGuidKeyForStringPrimaryKey
        );

        // DB アクセス
        AddBool(
            "GenerateRepositories",
            "--generate-repositories",
            Strings.Cli_Opt_GenerateRepositories
        );
        AddBool(
            "ExcludeUnboundedBinaryColumns",
            "--exclude-unbounded-binary-columns",
            Strings.Cli_Opt_ExcludeUnboundedBinaryColumns
        );
        AddBool("GenerateEfCore", "--generate-ef-core", Strings.Cli_Opt_GenerateEfCore);
        AddBool(
            "GenerateInMemoryRepositories",
            "--generate-in-memory-repositories",
            Strings.Cli_Opt_GenerateInMemoryRepositories
        );

        // リモート対応
        AddBool(
            "GenerateRemoteContracts",
            "--generate-remote-contracts",
            Strings.Cli_Opt_GenerateRemoteContracts
        );
        AddBool(
            "GenerateRemoteServices",
            "--generate-remote-services",
            Strings.Cli_Opt_GenerateRemoteServices
        );

        // ランタイム・ドキュメント
        AddBool("UseRuntimePackages", "--use-runtime-packages", Strings.Cli_Opt_UseRuntimePackages);
        AddBool("GenerateApiDocs", "--generate-api-docs", Strings.Cli_Opt_GenerateApiDocs);

        // 属性
        AddBool(
            "IncludeDataAnnotations",
            "--include-data-annotations",
            Strings.Cli_Opt_IncludeDataAnnotations
        );
        AddBool(
            "IncludeJsonIgnoreOnParentNavigation",
            "--include-json-ignore-on-parent-navigation",
            Strings.Cli_Opt_IncludeJsonIgnoreOnParentNavigation
        );
    }

    /// <summary>コマンドへ登録すべき全 Option を列挙する（文字列 → bool → 特例フラグの順）</summary>
    public IEnumerable<Option> Options
    {
        get
        {
            foreach (var (_, option) in _stringFlags)
            {
                yield return option;
            }

            foreach (var (_, option) in _boolFlags)
            {
                yield return option;
            }

            yield return RepositoryDialects;
            yield return OutputPath;
        }
    }

    /// <summary>
    /// CLI で指定された各フラグの値だけを設定 JSON へ上書きする（文字列は空白なら無視・bool は値ありのみ）。
    /// RepositoryDialects / OutputPath の特例は <see cref="CliApp"/> の後処理が扱うためここでは触らない。
    /// </summary>
    public void ApplyOverrides(ParseResult parseResult, JsonObject node)
    {
        foreach (var (key, option) in _stringFlags)
        {
            var value = parseResult.GetValue(option);

            if (!string.IsNullOrWhiteSpace(value))
            {
                CliApp.SetNodeValue(node, key, JsonValue.Create(value));
            }
        }

        foreach (var (key, option) in _boolFlags)
        {
            var value = parseResult.GetValue(option);

            if (value.HasValue)
            {
                CliApp.SetNodeValue(node, key, JsonValue.Create(value.Value));
            }
        }
    }

    /// <summary>文字列フラグを追加する</summary>
    private void AddString(string key, string flag, string description) =>
        _stringFlags.Add((key, new Option<string?>(flag) { Description = description }));

    /// <summary>三値 bool フラグを追加する</summary>
    private void AddBool(string key, string flag, string description) =>
        _boolFlags.Add((key, BuildBoolFlag(flag, description)));

    /// <summary>
    /// 三値 bool フラグ（<c>--flag</c> 単独＝true / <c>--flag false</c>＝false / 未指定＝null）の Option を作る。
    /// </summary>
    private static Option<bool?> BuildBoolFlag(string flag, string description) =>
        new(flag)
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
            CustomParser = ParseNullableBool,
        };

    /// <summary>
    /// 三値 bool フラグの値を解釈する。値トークンが無ければ true（<c>--flag</c> 単独）、あれば <c>true</c>/<c>false</c> を採る。
    /// フラグ自体が未指定のときはこのパーサは呼ばれず、既定 <c>null</c>（＝設定ファイルの値を使う）になる。
    /// </summary>
    private static bool? ParseNullableBool(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return true;
        }

        var token = result.Tokens[0].Value;

        if (bool.TryParse(token, out var value))
        {
            return value;
        }

        result.AddError(string.Format(Strings.Cli_InvalidBooleanFlag, token));
        return null;
    }
}

/// <summary>QuickER 版 Repository の生成が要求されたが、指定プロバイダの方言が未対応のときに送出する例外</summary>
internal sealed class RepositoryDialectUnsupportedException(string message) : Exception(message);
