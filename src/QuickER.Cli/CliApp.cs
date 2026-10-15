using System.CommandLine;
using QuickER.Cli.Resources;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
using QuickER.Documents;
using QuickER.Mcp;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

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
    /// <returns>
    /// プロセス終了コード。想定外の例外は整形した 1 行を標準エラーへ出して 1（環境変数 <c>QUICKER_DEBUG=1</c>
    /// のときのみスタックトレース込みの全文）、中断（<see cref="OperationCanceledException"/>）は無出力で 130。
    /// </returns>
    public static async Task<int> InvokeAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var root = new RootCommand(Strings.Cli_RootDescription);
        root.Subcommands.Add(BuildGenerateCommand(stdout, stderr));
        root.Subcommands.Add(BuildScaffoldCommand(stdout, stderr));
        root.Subcommands.Add(BuildReverseCommand(stdout, stderr));
        root.Subcommands.Add(BuildMcpCommand());

        // 既定の例外ハンドラはスタックトレースをそのまま露出するため無効化し、自前で整形する
        // （ヘルプ・解析エラーの出力先も注入された writer へ揃える）。
        var configuration = new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = stdout,
            Error = stderr,
        };

        try
        {
            return await root.Parse(args).InvokeAsync(configuration).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C 等による中断。追加の出力はせず、SIGINT の慣例に従った終了コードを返す
            return 130;
        }
        catch (Exception ex)
        {
            // 想定外の例外はメッセージ 1 行だけを出す。全文（スタックトレース込み）は QUICKER_DEBUG=1 のときのみ
            stderr.WriteLine(
                Environment.GetEnvironmentVariable("QUICKER_DEBUG") == "1"
                    ? ex.ToString()
                    : string.Format(Strings.Cli_UnhandledError, ex.Message)
            );

            return 1;
        }
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
        var force = ForceOption();

        // 既定値を持たせない（未指定＝図の対象 DBMS を使う。解決は図を読んでから行う）
        var provider = new Option<string>("--provider")
        {
            Description = Strings.Cli_Opt_GenerateProvider,
        };
        var generation = new GenerationOptionSet();

        var command = new Command("generate", Strings.Cli_Cmd_Generate)
        {
            schema,
            output,
            config,
            force,
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
                    parseResult.GetValue(provider),
                    parseResult,
                    generation,
                    parseResult.GetValue(force),
                    stdout,
                    stderr,
                    cancellationToken
                )
        );

        return command;
    }

    /// <summary>generate を実行する（<paramref name="providerName"/> が未指定なら図の対象 DBMS を使う）</summary>
    /// <remarks>
    /// 方言の既定を図から決めるため、図の読込はプロバイダ解決より前に行う
    /// （scaffold は図を DB から取るのでプロバイダが先＝<see cref="GenerationExecutor.RunAsync"/> の既定の順序）。
    /// </remarks>
    private static Task<int> RunGenerateAsync(
        FileInfo schemaFile,
        DirectoryInfo output,
        FileInfo? config,
        string? providerName,
        ParseResult parseResult,
        GenerationOptionSet generation,
        bool force,
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

        var diagram = LoadSchemaDiagram(schemaFile, stderr);

        if (diagram is null)
        {
            return Task.FromResult(1);
        }

        var resolvedProviderName = GenerationExecutor.ResolveProviderName(providerName, diagram);

        // 方言を図から採ったのに未対応の名前だったときは、図の値であることと --provider で回避できることを
        // 名指しする（プロバイダ解決の汎用文言だけでは、利用者が書いていない名前の出どころが分からない）
        if (
            string.IsNullOrWhiteSpace(providerName)
            && !GenerationExecutor.IsRegisteredProvider(resolvedProviderName)
        )
        {
            stderr.WriteLine(
                string.Format(
                    Strings.Cli_DiagramTargetDbmsUnsupported,
                    resolvedProviderName,
                    GenerationExecutor.RegisteredProviderNames
                )
            );

            return Task.FromResult(1);
        }

        // 図と違う方言を明示したときは、型を変換せずに生成することを警告する（生成は続ける）。
        // 未対応の名前はこの後のプロバイダ解決が誤りとして報告するので、警告は対応する名前のときだけ出す
        if (
            GenerationExecutor.ProviderDiffersFromDiagram(providerName, diagram)
            && GenerationExecutor.IsRegisteredProvider(resolvedProviderName)
        )
        {
            stderr.WriteLine(
                string.Format(
                    Strings.Cli_ProviderDiffersFromDiagram,
                    GenerationExecutor.ResolveProvider(resolvedProviderName).Name,
                    SqlComment.Sanitize(diagram.TargetDbms)
                )
            );

            // 指定した方言が DB の採番する列を持てないなら、印の付いた列も名指しする（値がどこからも書かれなくなる）
            var targetProviderName = GenerationExecutor.ResolveProvider(resolvedProviderName).Name;
            var identityColumns = IdentityColumnMarks.NamesUnsupportedBy(
                diagram,
                targetProviderName
            );

            if (identityColumns.Count > 0)
            {
                stderr.WriteLine(
                    string.Format(
                        Strings.Cli_IdentityColumnsUnsupportedByProvider,
                        targetProviderName,
                        string.Join(", ", identityColumns)
                    )
                );
            }
        }

        return GenerationExecutor.RunAsync(
            resolvedProviderName,
            output,
            config,
            parseResult,
            generation,
            force,
            stdout,
            stderr,
            // 図は読込済み（方言の既定を決めるため先に読んだ）。プロバイダは使わないため受け取るだけ
            (_, _) => Task.FromResult<ErDiagram?>(diagram),
            cancellationToken
        );
    }

    /// <summary>保存形式の ER 図 JSON を読み込み、意味モデル（<see cref="ErDiagram"/>）を返す</summary>
    /// <remarks>
    /// 読込は形式検証込みの <see cref="JsonStorageService.TryLoad"/> を通す。無関係な JSON を「空図」として
    /// 読むと「ER 図にエンティティがありません」という原因と噛み合わない診断で落ちるため、
    /// 失敗種別ごとの文言を出して <c>null</c>（＝呼び出し側で終了コード 1）を返す。
    /// 新しいフォーマットの文書は未知のプロパティを黙って無視するため、警告してから続行する。
    /// </remarks>
    private static ErDiagram? LoadSchemaDiagram(FileInfo schemaFile, TextWriter stderr)
    {
        if (
            !JsonStorageService.TryLoad(
                schemaFile.FullName,
                out var document,
                out var error,
                out var exception
            )
        )
        {
            stderr.WriteLine(
                error switch
                {
                    DocumentLoadError.ReadFailed => string.Format(
                        Strings.Cli_SchemaReadFailed,
                        schemaFile.FullName,
                        exception!.Message
                    ),
                    DocumentLoadError.InvalidJson => string.Format(
                        Strings.Cli_SchemaInvalidJson,
                        schemaFile.FullName,
                        exception!.Message
                    ),
                    DocumentLoadError.NotDiagramDocument => string.Format(
                        Strings.Cli_SchemaNotDiagramDocument,
                        schemaFile.FullName
                    ),
                    DocumentLoadError.DuplicateId => string.Format(
                        Strings.Cli_SchemaDuplicateId,
                        schemaFile.FullName,
                        exception!.Message
                    ),
                    DocumentLoadError.MissingRequiredText => string.Format(
                        Strings.Cli_SchemaMissingRequiredText,
                        schemaFile.FullName,
                        exception!.Message
                    ),
                    _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
                }
            );

            return null;
        }

        if (document!.IsNewerFormat)
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

        // GUI の接続設定と同じ既定値を CLI にも出す（大規模スキーマの取込が ADO 既定で切れないようにする）
        var commandTimeout = new Option<int>("--command-timeout")
        {
            Description = Strings.Cli_Opt_CommandTimeout,
            DefaultValueFactory = _ => DbCommands.DefaultTimeoutSeconds,
        };
        var force = ForceOption();
        var provider = ProviderOption();
        var generation = new GenerationOptionSet();

        var command = new Command("scaffold", Strings.Cli_Cmd_Scaffold)
        {
            connection,
            output,
            config,
            commandTimeout,
            force,
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
                    parseResult.GetValue(commandTimeout),
                    parseResult.GetValue(provider)!,
                    parseResult,
                    generation,
                    parseResult.GetValue(force),
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
        int commandTimeoutSeconds,
        string providerName,
        ParseResult parseResult,
        GenerationOptionSet generation,
        bool force,
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
            force,
            stdout,
            stderr,
            (provider, ct) =>
                ImportDiagramAsync(provider, connectionString, commandTimeoutSeconds, stderr, ct),
            cancellationToken
        );

    /// <summary>DB へ直結してスキーマをインポートし、意味モデル（<see cref="ErDiagram"/>）を組み立てる（失敗時は表示済みで null）</summary>
    /// <remarks>
    /// 利用者の中断（<paramref name="cancellationToken"/> が取り消された状態の <see cref="OperationCanceledException"/>）は
    /// 取込の失敗として表示せず、そのまま伝える（<see cref="InvokeAsync(string[], TextWriter, TextWriter)"/> が 130 を返す）。
    /// トークンが取り消されていない <see cref="OperationCanceledException"/> はドライバや内部の時間切れなので、
    /// 他の例外と同じく取込の失敗として扱う（型だけで判定すると、失敗が「利用者の中断」に化けて黙って終わる）。
    /// </remarks>
    internal static async Task<ErDiagram?> ImportDiagramAsync(
        IDatabaseProvider provider,
        string connectionString,
        int commandTimeoutSeconds,
        TextWriter stderr,
        CancellationToken cancellationToken
    )
    {
        SchemaImportResult imported;

        try
        {
            imported = await provider
                .SchemaImporter.ImportAsync(
                    connectionString,
                    commandTimeoutSeconds,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
            when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            stderr.WriteLine(string.Format(Strings.Cli_SchemaImportFailed, ex.Message));
            return null;
        }

        // 宣言どおりに写し取れなかった箇所は stderr へ（取込自体は成功なので stdout の成果物は出す）
        // 種別ごとに「Warning: 見出し」の行を出し、その下に箇所を字下げして並べる
        foreach (var group in SchemaImportWarningText.Group(imported.Warnings))
        {
            stderr.WriteLine(string.Format(Strings.Cli_ImportWarningLine, group.Heading));

            foreach (var item in group.Items)
            {
                stderr.WriteLine("  - " + item);
            }
        }

        // 取り込んだ方言を図の対象 DBMS にする（reverse と同じ。型は --provider の方言で書かれている）
        return new ErDiagram
        {
            Entities = imported.Entities.ToList(),
            Relationships = imported.Relationships.ToList(),
            TargetDbms = provider.Name,
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
        var force = new Option<bool>("--force") { Description = Strings.Cli_Opt_ReverseForce };

        var command = new Command("reverse", Strings.Cli_Cmd_Reverse)
        {
            source,
            output,
            provider,
            force,
        };

        command.SetAction(parseResult =>
            RunReverse(
                parseResult.GetValue(source)!,
                parseResult.GetValue(output)!,
                parseResult.GetValue(provider)!,
                parseResult.GetValue(force),
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
        bool force,
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

        // 出力先にファイルがあれば、--force なしでは書かない。reverse が書くのはスキーマだけの新しい図で、
        // 既存の図のレイアウト・名前付きクエリ・Id を引き継がないため、上書きするとそれらが黙って消える
        // （GUI のマージ取込のような引き継ぎは CLI に無い）。将来版の図は、この版が表現できないデータまで
        // 失うので、その旨を名指しする文言にする（GUI の上書き保存・Schema JSON エクスポートの確認と同じ判定）。
        // 終了コードは generate の「上書きを断った」と同じ 2＝失敗一般（1）とスクリプトから区別できる
        if (!force && File.Exists(output.FullName))
        {
            var refusal = JsonStorageService.IsNewerFormatFile(output.FullName)
                ? Strings.Cli_ReverseNewerFormatRefused
                : Strings.Cli_ReverseOutputExistsRefused;
            stderr.WriteLine(string.Format(refusal, output.FullName));

            return GenerationExecutor.ModifiedFilesExitCode;
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
            result = new CSharpReverseParser().Parse(
                sourceText,
                provider.TypeCatalog,
                IdentifierLengthLimits.ForDbms(provider.Name)
            );
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

        // 出力先の親ディレクトリを用意する（generate 系と対称。作らないと原子的書き込みの一時ファイル名を
        // 含む素の DirectoryNotFoundException が出て、何が足りないのか読み取れない）
        var outputDirectory = output.Directory;

        if (outputDirectory is not null && !outputDirectory.Exists)
        {
            outputDirectory.Create();
        }

        // Layout=null＝スキーマのみ文書（layout キーが JSON へ出力されない）として保存する。
        // 既存ファイルへの上書きになり得るため、書き込み途中の中断で壊さないよう原子的に差し替える
        var document = new DiagramDocument { Schema = diagram, Layout = null };
        JsonStorageService.SaveAtomic(output.FullName, document);

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

    /// <summary>
    /// 生成後に手で編集された生成ファイルも上書きする <c>--force</c>（generate / scaffold が共有する）。
    /// 付けないときは、編集済みのファイルが 1 つでもあれば何も書かずに止める
    /// </summary>
    private static Option<bool> ForceOption() =>
        new("--force") { Description = Strings.Cli_Opt_Force };
}
