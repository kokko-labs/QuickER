using System.CommandLine;
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
        var ns = NamespaceOption();
        var split = SplitOption();
        var repositoryDialects = RepositoryDialectsOption();
        var runtimePackages = RuntimePackagesOption();
        var apiDocs = ApiDocsOption();
        var remoteContracts = RemoteContractsOption();

        var command = new Command("generate", Strings.Cli_Cmd_Generate)
        {
            schema,
            output,
            config,
            provider,
            ns,
            split,
            repositoryDialects,
            runtimePackages,
            apiDocs,
            remoteContracts,
        };

        command.SetAction(parseResult =>
            RunGenerate(
                parseResult.GetValue(schema)!,
                parseResult.GetValue(output)!,
                parseResult.GetValue(config),
                parseResult.GetValue(provider)!,
                parseResult.GetValue(ns),
                parseResult.GetValue(split),
                parseResult.GetValue(repositoryDialects),
                parseResult.GetValue(runtimePackages),
                parseResult.GetValue(apiDocs),
                parseResult.GetValue(remoteContracts)
            )
        );

        return command;
    }

    private static int RunGenerate(
        FileInfo schemaFile,
        DirectoryInfo output,
        FileInfo? config,
        string providerName,
        string? ns,
        bool split,
        string? repositoryDialects,
        bool runtimePackages,
        bool apiDocs,
        bool remoteContracts
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
            options = LoadOptions(
                config,
                ns,
                split,
                provider,
                repositoryDialects,
                runtimePackages,
                apiDocs,
                remoteContracts
            );
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
        var ns = NamespaceOption();
        var split = SplitOption();
        var repositoryDialects = RepositoryDialectsOption();
        var runtimePackages = RuntimePackagesOption();
        var apiDocs = ApiDocsOption();
        var remoteContracts = RemoteContractsOption();

        var command = new Command("scaffold", Strings.Cli_Cmd_Scaffold)
        {
            connection,
            output,
            config,
            provider,
            ns,
            split,
            repositoryDialects,
            runtimePackages,
            apiDocs,
            remoteContracts,
        };

        command.SetAction(
            (parseResult, cancellationToken) =>
                RunScaffoldAsync(
                    parseResult.GetValue(connection)!,
                    parseResult.GetValue(output)!,
                    parseResult.GetValue(config),
                    parseResult.GetValue(provider)!,
                    parseResult.GetValue(ns),
                    parseResult.GetValue(split),
                    parseResult.GetValue(repositoryDialects),
                    parseResult.GetValue(runtimePackages),
                    parseResult.GetValue(apiDocs),
                    parseResult.GetValue(remoteContracts),
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
        string? ns,
        bool split,
        string? repositoryDialects,
        bool runtimePackages,
        bool apiDocs,
        bool remoteContracts,
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
            options = LoadOptions(
                config,
                ns,
                split,
                provider,
                repositoryDialects,
                runtimePackages,
                apiDocs,
                remoteContracts
            );
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

    private static Option<string> NamespaceOption() =>
        new("--namespace") { Description = Strings.Cli_Opt_Namespace };

    private static Option<bool> SplitOption() =>
        new("--split") { Description = Strings.Cli_Opt_Split };

    private static Option<string> RepositoryDialectsOption() =>
        new("--repository-dialects") { Description = Strings.Cli_Opt_RepositoryDialects };

    private static Option<bool> RuntimePackagesOption() =>
        new("--runtime-packages") { Description = Strings.Cli_Opt_RuntimePackages };

    private static Option<bool> ApiDocsOption() =>
        new("--api-docs") { Description = Strings.Cli_Opt_ApiDocs };

    private static Option<bool> RemoteContractsOption() =>
        new("--remote-contracts") { Description = Strings.Cli_Opt_RemoteContracts };

    /// <summary>
    /// 設定ファイル（quicker.json）を読み、CLI フラグ・<c>--provider</c>・<c>--repository-dialects</c>・
    /// <c>--runtime-packages</c> で上書きして生成オプションを構築する。
    /// </summary>
    /// <remarks>
    /// <paramref name="repositoryDialects"/>（<c>--repository-dialects</c>）指定時は
    /// <see cref="CodeGenerationOptions.RepositoryDialects"/> へカンマ区切りの各方言を設定する。
    /// 未指定時は従来どおり <paramref name="provider"/> の名前を単一 <see cref="CodeGenerationOptions.RepositoryDialect"/>
    /// として設定する（設定ファイルの値は無視する。図の TargetDbms から導出される値のため CLI 引数を単一の正とする）。
    /// <paramref name="runtimePackages"/>（<c>--runtime-packages</c>）指定時は
    /// <see cref="CodeGenerationOptions.UseRuntimePackages"/> を true にする（未指定時は設定ファイルの値を使う）。
    /// <paramref name="apiDocs"/>（<c>--api-docs</c>）指定時は
    /// <see cref="CodeGenerationOptions.GenerateApiDocs"/> を true にする（未指定時は設定ファイルの値を使う）。
    /// <paramref name="remoteContracts"/>（<c>--remote-contracts</c>）指定時は
    /// <see cref="CodeGenerationOptions.GenerateRemoteContracts"/> を true にする（未指定時は設定ファイルの値を使う）。
    /// 自作 Repository 生成（<c>GenerateRepositories</c>）が要求され、かつ実効方言に未対応方言が含まれる場合は
    /// <see cref="RepositoryDialectUnsupportedException"/> を送出する
    /// </remarks>
    private static CodeGenerationOptions LoadOptions(
        FileInfo? config,
        string? ns,
        bool split,
        IDatabaseProvider provider,
        string? repositoryDialects = null,
        bool runtimePackages = false,
        bool apiDocs = false,
        bool remoteContracts = false
    )
    {
        var node = config is { Exists: true }
            ? JsonNode.Parse(File.ReadAllText(config.FullName))?.AsObject() ?? new JsonObject()
            : new JsonObject();

        if (!string.IsNullOrWhiteSpace(ns))
        {
            node["NamespaceName"] = ns;
        }

        if (split)
        {
            node["SplitFilesByCategory"] = true;
        }

        if (runtimePackages)
        {
            node["UseRuntimePackages"] = true;
        }

        if (apiDocs)
        {
            node["GenerateApiDocs"] = true;
        }

        if (remoteContracts)
        {
            node["GenerateRemoteContracts"] = true;
        }

        if (!string.IsNullOrWhiteSpace(repositoryDialects))
        {
            var dialects = repositoryDialects
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            node["RepositoryDialects"] = new JsonArray(
                dialects.Select(dialect => JsonValue.Create(dialect)).ToArray()
            );
        }
        else
        {
            node["RepositoryDialect"] = provider.Name;
        }

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
    /// 自作 Repository の実効方言（<see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/>）ごとに、
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

/// <summary>自作 Repository の生成が要求されたが、指定プロバイダの方言が未対応のときに送出する例外</summary>
internal sealed class RepositoryDialectUnsupportedException(string message) : Exception(message);
