using System.CommandLine;
using System.Globalization;
using QuickER.Cli.Resources;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Cli;

/// <summary>
/// generate / scaffold が共有するコード生成の共通パイプライン。
/// プロバイダ解決 → 図の取得（コマンド固有）→ 設定読解 → 型マッパ解決 → 生成 → 書き出しを 1 本にまとめる。
/// </summary>
internal static class GenerationExecutor
{
    /// <summary>CLI が対応する DB プロバイダのレジストリ（新 DBMS 対応時はここへ実装を追加する）</summary>
    private static readonly DatabaseProviderRegistry Providers = new([
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ]);

    /// <summary>プロバイダ名を共有レジストリで解決する（未対応名は登録済み名を列挙した例外）</summary>
    /// <remarks>reverse コマンドなど、生成パイプラインを経由しないコマンドが型カタログを得るために使う</remarks>
    internal static IDatabaseProvider ResolveProvider(string providerName) =>
        Providers.Get(providerName);

    /// <summary>
    /// 生成の共通パイプラインを実行する。図の取得（<paramref name="resolveDiagram"/>）だけがコマンド固有で、
    /// 失敗時は <c>null</c>（エラー表示済み）を返して終了コード 1 で中断する。
    /// </summary>
    /// <remarks>
    /// 検証順序は「プロバイダ解決 → 図の取得 → 設定読解（LoadOptions）」。generate の schema 存在チェックのように
    /// プロバイダ解決より前に済ませるべき検証は、このメソッドを呼ぶ前にコマンド側で行うこと。
    /// 診断（標準エラー相当）と生成ファイル一覧（標準出力相当）は、引数の
    /// <paramref name="stdout"/> / <paramref name="stderr"/> へ書き出す。
    /// </remarks>
    public static async Task<int> RunAsync(
        string providerName,
        DirectoryInfo output,
        FileInfo? config,
        ParseResult parseResult,
        GenerationOptionSet generation,
        TextWriter stdout,
        TextWriter stderr,
        Func<IDatabaseProvider, CancellationToken, Task<ErDiagram?>> resolveDiagram,
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
            stderr.WriteLine(ex.Message);
            return 1;
        }

        var diagram = await resolveDiagram(provider, cancellationToken).ConfigureAwait(false);

        if (diagram is null)
        {
            // 図の取得に失敗（コマンド側でエラー表示済み）
            return 1;
        }

        CodeGenerationOptions options;
        try
        {
            options = GenerationConfigLoader.LoadOptions(config, provider, parseResult, generation);
        }
        catch (GenerationConfigException ex)
        {
            // 設定ファイルの不在・不正 JSON（既定値のまま黙って続行せず、生成前に中止する）
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (RepositoryDialectUnsupportedException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }

        return GenerateWithResolvedOptions(provider, diagram, options, output, stdout, stderr);
    }

    /// <summary>
    /// ParseResult 非依存で「設定ファイル（<paramref name="config"/>）＋既定値」からオプションを解決し、
    /// 図（<paramref name="diagram"/>）を <paramref name="output"/> へ生成する。CLI の <see cref="ParseResult"/> を
    /// 経由しない経路（MCP の generate_csharp ツール等）が使う。
    /// </summary>
    /// <remarks>
    /// 診断（stderr）と生成ファイル一覧（stdout）は、引数の <paramref name="stdout"/> / <paramref name="stderr"/>
    /// へ書き出す（呼び出し側は <see cref="StringWriter"/> を渡せばそのまま捕捉できる）。
    /// 設定エラー（<see cref="GenerationConfigException"/> ＝設定ファイル不在・不正 JSON /
    /// <see cref="RepositoryDialectUnsupportedException"/>）やその他の例外は呼び出し側へ伝播する。
    /// </remarks>
    public static int GenerateFromConfig(
        IDatabaseProvider provider,
        ErDiagram diagram,
        FileInfo? config,
        DirectoryInfo output,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        var options = GenerationConfigLoader.LoadOptions(config, provider);
        return GenerateWithResolvedOptions(provider, diagram, options, output, stdout, stderr);
    }

    /// <summary>解決済みオプションで方言別マッパを解決し、生成・書き出しを行う共通コア</summary>
    private static int GenerateWithResolvedOptions(
        IDatabaseProvider provider,
        ErDiagram diagram,
        CodeGenerationOptions options,
        DirectoryInfo output,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        var dialectMappers = ResolveDialectTypeMappers(options);
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            dialectMappers,
            diagram,
            options
        );
        return WriteResult(result, output, options, stdout, stderr);
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
        CodeGenerationOptions options,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            stderr.WriteLine($"[{diagnostic.Severity}] {diagnostic.Message}");
        }

        if (result.HasErrors)
        {
            stderr.WriteLine(Strings.Cli_GenerationAborted);
            return 1;
        }

        Directory.CreateDirectory(output.FullName);
        var written = new GeneratedFileWriter().WriteFiles(output.FullName, result);

        foreach (var file in written)
        {
            stdout.WriteLine(string.Format(Strings.Cli_GeneratedFile, file));
        }

        stdout.WriteLine(string.Format(Strings.Cli_GeneratedCount, written.Count));

        if (options.UseRuntimePackages)
        {
            stdout.WriteLine();

            foreach (
                var line in RuntimePackageReferenceGuidance.BuildGuidanceLines(
                    options,
                    RuntimePackages.ResolveGuidanceVersion(),
                    CultureInfo.CurrentUICulture
                )
            )
            {
                stdout.WriteLine(line);
            }
        }

        return 0;
    }
}
