using System.CommandLine;
using QuickER.Cli.Resources;
using QuickER.CodeReverse.CSharp;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Cli;

/// <summary>
/// quicker CLI のエントリポイント。<c>generate</c>（ER図JSON→コード）と
/// <c>scaffold</c>（DB直結→コード）の 2 サブコマンドを提供する。
/// </summary>
public static class CliApp
{
    /// <summary>引数を解析してコマンドを実行する</summary>
    public static Task<int> InvokeAsync(string[] args)
    {
        TrySetUtf8Output();

        var root = new RootCommand(Strings.Cli_RootDescription);
        root.Subcommands.Add(BuildGenerateCommand());
        root.Subcommands.Add(BuildScaffoldCommand());
        root.Subcommands.Add(BuildReverseCommand());
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

        command.SetAction(
            (parseResult, cancellationToken) =>
                RunGenerateAsync(
                    parseResult.GetValue(schema)!,
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

    private static Task<int> RunGenerateAsync(
        FileInfo schemaFile,
        DirectoryInfo output,
        FileInfo? config,
        string providerName,
        ParseResult parseResult,
        GenerationOptionSet generation,
        CancellationToken cancellationToken
    )
    {
        // schema 存在チェックはプロバイダ解決より前に行う（現状の検証順序を保存する）
        if (!schemaFile.Exists)
        {
            Console.Error.WriteLine(
                string.Format(Strings.Cli_SchemaFileNotFound, schemaFile.FullName)
            );
            return Task.FromResult(1);
        }

        return GenerationExecutor.RunAsync(
            providerName,
            output,
            config,
            parseResult,
            generation,
            // generate の図取得は同期（JSON 読込）。プロバイダは使わないため受け取るだけ
            (_, _) => Task.FromResult<ErDiagram?>(LoadSchemaDiagram(schemaFile)),
            cancellationToken
        );
    }

    /// <summary>保存形式の ER 図 JSON を読み込み、意味モデル（<see cref="ErDiagram"/>）を返す</summary>
    /// <remarks>新しいフォーマットの文書は未知のプロパティを黙って無視するため、警告してから続行する</remarks>
    private static ErDiagram LoadSchemaDiagram(FileInfo schemaFile)
    {
        var document = JsonStorageService.Load(schemaFile.FullName);

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

        return document.Schema;
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

    private static Task<int> RunScaffoldAsync(
        string connectionString,
        DirectoryInfo output,
        FileInfo? config,
        string providerName,
        ParseResult parseResult,
        GenerationOptionSet generation,
        CancellationToken cancellationToken
    ) =>
        GenerationExecutor.RunAsync(
            providerName,
            output,
            config,
            parseResult,
            generation,
            (provider, ct) => ImportDiagramAsync(provider, connectionString, ct),
            cancellationToken
        );

    /// <summary>DB へ直結してスキーマをインポートし、意味モデル（<see cref="ErDiagram"/>）を組み立てる（失敗時は表示済みで null）</summary>
    private static async Task<ErDiagram?> ImportDiagramAsync(
        IDatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken
    )
    {
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
            return null;
        }

        return new ErDiagram
        {
            Entities = imported.Entities.ToList(),
            Relationships = imported.Relationships.ToList(),
        };
    }

    // ---------------- reverse ----------------

    private static Command BuildReverseCommand()
    {
        var source = new Option<FileInfo>("--source")
        {
            Description = Strings.Cli_Opt_ReverseSource,
            Required = true,
        };
        var output = new Option<FileInfo>("--out")
        {
            Description = Strings.Cli_Opt_ReverseOut,
            Required = true,
        };
        var provider = ProviderOption();

        var command = new Command("reverse", Strings.Cli_Cmd_Reverse) { source, output, provider };

        command.SetAction(parseResult =>
            RunReverse(
                parseResult.GetValue(source)!,
                parseResult.GetValue(output)!,
                parseResult.GetValue(provider)!
            )
        );

        return command;
    }

    /// <summary>C# ソースをリバース解析し、スキーマのみの ER 図 JSON（layout キーなし）を書き出す</summary>
    private static int RunReverse(FileInfo sourceFile, FileInfo output, string providerName)
    {
        // ソース存在チェックはプロバイダ解決より前に行う（generate の検証順序に揃える）
        if (!sourceFile.Exists)
        {
            Console.Error.WriteLine(
                string.Format(Strings.Cli_ReverseSourceFileNotFound, sourceFile.FullName)
            );

            return 1;
        }

        IDatabaseProvider provider;
        try
        {
            provider = GenerationExecutor.ResolveProvider(providerName);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);

            return 1;
        }

        CodeReverseResult result;
        try
        {
            var sourceText = File.ReadAllText(sourceFile.FullName);
            result = new CSharpReverseParser().Parse(sourceText, provider.TypeCatalog);
        }
        catch (CodeReverseException ex)
        {
            // 解析対象クラス 0 件などの致命的な問題（メッセージはローカライズ済み・案内込み）
            Console.Error.WriteLine(ex.Message);

            return 1;
        }

        // 非致命の警告は標準エラーへ出す（generate の診断出力と同じ流儀）
        foreach (var warning in result.Warnings)
        {
            Console.Error.WriteLine(warning);
        }

        // マージなしの新規図。--provider の方言で型を展開済み、TargetDbms も同方言を採用する
        var diagram = new ErDiagram
        {
            Entities = result.Entities.ToList(),
            Relationships = result.Relationships.ToList(),
            TargetDbms = provider.Name,
        };

        // Layout=null＝スキーマのみ文書（layout キーが JSON へ出力されない）として保存する
        var document = new DiagramDocument { Schema = diagram, Layout = null };
        JsonStorageService.Save(output.FullName, document);

        Console.WriteLine(string.Format(Strings.Cli_ReverseWritten, output.FullName));

        return 0;
    }

    // ---------------- 共有 ----------------

    private static Option<string> ProviderOption() =>
        new("--provider")
        {
            Description = Strings.Cli_Opt_Provider,
            DefaultValueFactory = _ => SqlServerProvider.ProviderName,
        };
}
