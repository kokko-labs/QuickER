using System.CommandLine;
using QuickER.Cli.Resources;
using QuickER.CodeReverse.CSharp;
using QuickER.Documents;
using QuickER.Mcp;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Cli;

/// <summary>
/// quicker CLI のエントリポイント。<c>generate</c>（ER図JSON→コード）・<c>scaffold</c>（DB直結→コード）・
/// <c>reverse</c>（C#→ER図）・<c>mcp</c>（stdio MCP サーバ）のサブコマンドを提供する。
/// </summary>
public static class CliApp
{
    /// <summary>引数を解析してコマンドを実行する（実コンソールへ出力する既定経路）</summary>
    /// <remarks>
    /// <see cref="TrySetUtf8Output"/> は <see cref="Console.Out"/> を作り直すため、writer の捕捉は必ずその後に行う。
    /// </remarks>
    public static Task<int> InvokeAsync(string[] args)
    {
        TrySetUtf8Output();

        return InvokeAsync(args, Console.Out, Console.Error);
    }

    /// <summary>出力先を注入して引数を解析・実行する</summary>
    /// <remarks>
    /// テストがプロセスグローバルなコンソール出力先を差し替えずに（＝並列実行中の他テストと競合せずに）
    /// 出力を捕捉するための注入版。既定版（<see cref="InvokeAsync(string[])"/>）は実コンソールへ出力する。
    /// こちらはコンソールの状態（出力エンコーディング）に一切触れない。
    /// </remarks>
    public static Task<int> InvokeAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var root = new RootCommand(Strings.Cli_RootDescription);
        root.Subcommands.Add(BuildGenerateCommand(stdout, stderr));
        root.Subcommands.Add(BuildScaffoldCommand(stdout, stderr));
        root.Subcommands.Add(BuildReverseCommand(stdout, stderr));
        root.Subcommands.Add(BuildMcpCommand());
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

    private static Command BuildGenerateCommand(TextWriter stdout, TextWriter stderr)
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
                    stdout,
                    stderr,
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
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken
    )
    {
        // schema 存在チェックはプロバイダ解決より前に行う（現状の検証順序を保存する）
        if (!schemaFile.Exists)
        {
            stderr.WriteLine(string.Format(Strings.Cli_SchemaFileNotFound, schemaFile.FullName));
            return Task.FromResult(1);
        }

        return GenerationExecutor.RunAsync(
            providerName,
            output,
            config,
            parseResult,
            generation,
            stdout,
            stderr,
            // generate の図取得は同期（JSON 読込）。プロバイダは使わないため受け取るだけ
            (_, _) => Task.FromResult<ErDiagram?>(LoadSchemaDiagram(schemaFile, stderr)),
            cancellationToken
        );
    }

    /// <summary>保存形式の ER 図 JSON を読み込み、意味モデル（<see cref="ErDiagram"/>）を返す</summary>
    /// <remarks>新しいフォーマットの文書は未知のプロパティを黙って無視するため、警告してから続行する</remarks>
    private static ErDiagram LoadSchemaDiagram(FileInfo schemaFile, TextWriter stderr)
    {
        var document = JsonStorageService.Load(schemaFile.FullName);

        if (document.IsNewerFormat)
        {
            stderr.WriteLine(
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

    private static Command BuildScaffoldCommand(TextWriter stdout, TextWriter stderr)
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
                    stdout,
                    stderr,
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
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken
    ) =>
        GenerationExecutor.RunAsync(
            providerName,
            output,
            config,
            parseResult,
            generation,
            stdout,
            stderr,
            (provider, ct) => ImportDiagramAsync(provider, connectionString, stderr, ct),
            cancellationToken
        );

    /// <summary>DB へ直結してスキーマをインポートし、意味モデル（<see cref="ErDiagram"/>）を組み立てる（失敗時は表示済みで null）</summary>
    private static async Task<ErDiagram?> ImportDiagramAsync(
        IDatabaseProvider provider,
        string connectionString,
        TextWriter stderr,
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
            stderr.WriteLine(string.Format(Strings.Cli_SchemaImportFailed, ex.Message));
            return null;
        }

        return new ErDiagram
        {
            Entities = imported.Entities.ToList(),
            Relationships = imported.Relationships.ToList(),
        };
    }

    // ---------------- reverse ----------------

    private static Command BuildReverseCommand(TextWriter stdout, TextWriter stderr)
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
                parseResult.GetValue(provider)!,
                stdout,
                stderr
            )
        );

        return command;
    }

    /// <summary>C# ソースをリバース解析し、スキーマのみの ER 図 JSON（layout キーなし）を書き出す</summary>
    private static int RunReverse(
        FileInfo sourceFile,
        FileInfo output,
        string providerName,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        // ソース存在チェックはプロバイダ解決より前に行う（generate の検証順序に揃える）
        if (!sourceFile.Exists)
        {
            stderr.WriteLine(
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
            stderr.WriteLine(ex.Message);

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
            stderr.WriteLine(ex.Message);

            return 1;
        }

        // 非致命の警告は標準エラーへ出す（generate の診断出力と同じ流儀）
        foreach (var warning in result.Warnings)
        {
            stderr.WriteLine(warning);
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

        stdout.WriteLine(string.Format(Strings.Cli_ReverseWritten, output.FullName));

        return 0;
    }

    // ---------------- mcp ----------------

    private static Command BuildMcpCommand()
    {
        // オプションなし（ステートレス設計。対象ファイルは各ツール呼び出しの file 引数で受ける）
        var command = new Command("mcp", Strings.Cli_Cmd_Mcp);

        command.SetAction((_, cancellationToken) => RunMcpAsync(cancellationToken));

        return command;
    }

    /// <summary>
    /// ER 図操作ツール（<see cref="DocumentErDiagramToolSet"/>）とコード生成ツール（<see cref="CodeGenToolSet"/>）を
    /// 合成した stdio MCP サーバを起動し、終了まで待機する。
    /// </summary>
    /// <remarks>
    /// stdio MCP サーバでは標準出力が JSON-RPC プロトコル専用チャネルになる。想定外の <c>Console.Write</c> が
    /// プロトコルへ混入するのを防ぐため、起動時に <see cref="Console.Out"/> を標準エラーへ退避する。
    /// stdio トランスポート自体は <see cref="Console.OpenStandardOutput"/> で生ストリームを直接取得するため、
    /// この退避の影響を受けない（プロトコル純度は stdio E2E テストが最終検証する）。
    /// </remarks>
    private static async Task<int> RunMcpAsync(CancellationToken cancellationToken)
    {
        Console.SetOut(Console.Error);

        var toolSets = new List<McpToolSet>
        {
            DocumentErDiagramToolSet.Create(),
            CodeGenToolSet.Create(),
        };

        await StdioMcpServerHost
            .RunAsync(toolSets, ErDiagramToolCatalog.ServerInstructions, cancellationToken)
            .ConfigureAwait(false);

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
