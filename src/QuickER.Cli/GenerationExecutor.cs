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

    /// <summary>
    /// 生成の共通パイプラインを実行する。図の取得（<paramref name="resolveDiagram"/>）だけがコマンド固有で、
    /// 失敗時は <c>null</c>（エラー表示済み）を返して終了コード 1 で中断する。
    /// </summary>
    /// <remarks>
    /// 検証順序は「プロバイダ解決 → 図の取得 → 設定読解（LoadOptions）」。generate の schema 存在チェックのように
    /// プロバイダ解決より前に済ませるべき検証は、このメソッドを呼ぶ前にコマンド側で行うこと。
    /// </remarks>
    public static async Task<int> RunAsync(
        string providerName,
        DirectoryInfo output,
        FileInfo? config,
        ParseResult parseResult,
        GenerationOptionSet generation,
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
            Console.Error.WriteLine(ex.Message);
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
                    RuntimePackages.ResolveGuidanceVersion(),
                    CultureInfo.CurrentUICulture
                )
            )
            {
                Console.WriteLine(line);
            }
        }

        return 0;
    }
}
