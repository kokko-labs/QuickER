using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Documents;
using QuickER.Generator;
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
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>CLI が対応する DB プロバイダのレジストリ（新 DBMS 対応時はここへ実装を追加する）</summary>
    private static readonly DatabaseProviderRegistry Providers = new(
        [new SqlServerProvider()]
    );

    /// <summary>引数を解析してコマンドを実行する</summary>
    public static Task<int> InvokeAsync(string[] args)
    {
        TrySetUtf8Output();

        var root = new RootCommand("QuickER — ER図/データベースから C# コードを生成する CLI");
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
            Description = "入力する ER 図 JSON ファイル（アプリの保存形式）",
            Required = true,
        };
        var output = new Option<DirectoryInfo>("--out")
        {
            Description = "生成コードの出力先フォルダ",
            Required = true,
        };
        var config = new Option<FileInfo>("--config")
        {
            Description = "生成オプション設定ファイル（quicker.json）",
        };
        var provider = ProviderOption();
        var ns = NamespaceOption();
        var split = SplitOption();

        var command = new Command("generate", "ER 図 JSON から C# コードを生成する")
        {
            schema,
            output,
            config,
            provider,
            ns,
            split,
        };

        command.SetAction(parseResult =>
            RunGenerate(
                parseResult.GetValue(schema)!,
                parseResult.GetValue(output)!,
                parseResult.GetValue(config),
                parseResult.GetValue(provider)!,
                parseResult.GetValue(ns),
                parseResult.GetValue(split)
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
        bool split
    )
    {
        if (!schemaFile.Exists)
        {
            Console.Error.WriteLine($"スキーマファイルが見つかりません: {schemaFile.FullName}");
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
        var options = LoadOptions(config, ns, split);
        var result = DiagramCodeGenerator.Generate(provider.TypeMapper, document.Schema, options);
        return WriteResult(result, output);
    }

    // ---------------- scaffold ----------------

    private static Command BuildScaffoldCommand()
    {
        var connection = new Option<string>("--connection")
        {
            Description =
                "接続文字列（例: Server=.;Database=Foo;Integrated Security=true;TrustServerCertificate=true）",
            Required = true,
        };
        var output = new Option<DirectoryInfo>("--out")
        {
            Description = "生成コードの出力先フォルダ",
            Required = true,
        };
        var config = new Option<FileInfo>("--config")
        {
            Description = "生成オプション設定ファイル（quicker.json）",
        };
        var provider = ProviderOption();
        var ns = NamespaceOption();
        var split = SplitOption();

        var command = new Command(
            "scaffold",
            "データベースへ直接接続してスキーマから C# コードを生成する"
        )
        {
            connection,
            output,
            config,
            provider,
            ns,
            split,
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
            Console.Error.WriteLine($"データベースからのスキーマ取得に失敗しました: {ex.Message}");
            return 1;
        }

        var diagram = new ErDiagram
        {
            Entities = imported.Entities.ToList(),
            Relationships = imported.Relationships.ToList(),
        };
        var options = LoadOptions(config, ns, split);
        var result = DiagramCodeGenerator.Generate(provider.TypeMapper, diagram, options);
        return WriteResult(result, output);
    }

    // ---------------- 共有 ----------------

    private static Option<string> ProviderOption() =>
        new("--provider")
        {
            Description = "対象データベースの種類（既定: sqlserver）",
            DefaultValueFactory = _ => SqlServerProvider.ProviderName,
        };

    private static Option<string> NamespaceOption() =>
        new("--namespace") { Description = "生成コードのルート名前空間（設定ファイルを上書き）" };

    private static Option<bool> SplitOption() =>
        new("--split")
        {
            Description = "カテゴリごとに別ファイル・別名前空間で出力する（設定ファイルを上書き）",
        };

    /// <summary>設定ファイル（quicker.json）を読み、CLI フラグで上書きして生成オプションを構築する</summary>
    private static CodeGenerationOptions LoadOptions(FileInfo? config, string? ns, bool split)
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

        return node.Deserialize<CodeGenerationOptions>(OptionsJson) ?? new CodeGenerationOptions();
    }

    /// <summary>生成結果の診断を表示し、エラーが無ければファイルを書き出す。終了コードを返す</summary>
    private static int WriteResult(CodeGenerationResult result, DirectoryInfo output)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"[{diagnostic.Severity}] {diagnostic.Message}");
        }

        if (result.HasErrors)
        {
            Console.Error.WriteLine("生成エラーのため中止しました。");
            return 1;
        }

        Directory.CreateDirectory(output.FullName);
        var written = new GeneratedFileWriter().WriteFiles(output.FullName, result);

        foreach (var file in written)
        {
            Console.WriteLine($"生成: {file}");
        }

        Console.WriteLine($"{written.Count} 個のファイルを生成しました。");
        return 0;
    }
}
