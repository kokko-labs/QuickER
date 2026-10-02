using System.CommandLine;
using System.Globalization;
using QuickER.Cli.Resources;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

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
    /// 書き込み先に生成後に手で編集された生成ファイルがあり、上書きの指定（<c>force</c>）も無かったため
    /// 何も書かずに止めたことを表す終了コード
    /// </summary>
    /// <remarks>
    /// 失敗一般（1）と区別できるよう専用の値にする（CI のスクリプトが「生成物が手で直されている」を判別できる）。
    /// 上書きの指定方法の案内は入口ごとに違うため、呼び出し側（CLI＝<c>--force</c>・MCP＝<c>force</c> 引数）が出す。
    /// </remarks>
    public const int ModifiedFilesExitCode = 2;

    /// <summary>プロバイダ名を共有レジストリで解決する（未対応名は登録済み名を列挙した例外）</summary>
    /// <remarks>reverse コマンドなど、生成パイプラインを経由しないコマンドが型カタログを得るために使う</remarks>
    internal static IDatabaseProvider ResolveProvider(string providerName) =>
        Providers.Get(providerName);

    /// <summary>プロバイダ名が登録済みか（大文字小文字は登録簿の照合規則に従う）</summary>
    internal static bool IsRegisteredProvider(string providerName) =>
        Providers.TryGet(providerName, out _);

    /// <summary>登録済みのプロバイダ名を名前順・カンマ区切りで返す（エラー文言の案内用）</summary>
    internal static string RegisteredProviderNames =>
        string.Join(", ", Providers.All.Select(provider => provider.Name).Order());

    /// <summary>
    /// 図を入力に取る生成（CLI の generate・MCP の generate_csharp / generate_ddl）で使うプロバイダ名を決める。
    /// 明示されていればそれを、無ければ図の対象 DBMS を、図にも無ければ sqlserver を返す
    /// </summary>
    /// <remarks>
    /// 既定を図の方言にするのは、型の解決（DB 型 → C# 型）が方言ごとに違うため。固定の既定だと、
    /// 別の方言の図を指定し忘れたときに型が黙って別の方言で解決される。
    /// CLI と MCP で既定が割れないよう、規則はここだけに置く。
    /// </remarks>
    /// <param name="explicitName">利用者が指定したプロバイダ名（未指定なら null か空）</param>
    /// <param name="diagram">生成対象の図</param>
    internal static string ResolveProviderName(string? explicitName, ErDiagram diagram)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        return string.IsNullOrWhiteSpace(diagram.TargetDbms)
            ? SqlServerProvider.ProviderName
            : diagram.TargetDbms;
    }

    /// <summary>
    /// 明示されたプロバイダが図の対象 DBMS と違うか（未指定・図に対象 DBMS が無い・同じ方言なら false）
    /// </summary>
    /// <remarks>
    /// 図の列の型は対象 DBMS の表記で書かれており、生成は型を変換しない（変換は GUI の対象 DBMS 切替の担当）。
    /// 違う方言を明示した場合は、型がその方言の規則でそのまま解釈されることを警告する（CLI の generate と
    /// MCP の generate_csharp / generate_ddl が同じ判定を使う）。方言名の照合は登録簿と同じく大文字小文字を区別しない。
    /// </remarks>
    /// <param name="explicitName">利用者が指定したプロバイダ名（未指定なら null か空）</param>
    /// <param name="diagram">生成対象の図</param>
    internal static bool ProviderDiffersFromDiagram(string? explicitName, ErDiagram diagram) =>
        !string.IsNullOrWhiteSpace(explicitName)
        && !string.IsNullOrWhiteSpace(diagram.TargetDbms)
        && !string.Equals(
            explicitName.Trim(),
            diagram.TargetDbms.Trim(),
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>
    /// 生成の共通パイプラインを実行する。図の取得（<paramref name="resolveDiagram"/>）だけがコマンド固有で、
    /// 失敗時は <c>null</c>（エラー表示済み）を返して終了コード 1 で中断する。
    /// </summary>
    /// <remarks>
    /// 検証順序は「プロバイダ解決 → 設定読解（LoadOptions）→ 図の取得」。設定読解は図に依らないため、
    /// 時間のかかる図の取得（scaffold の DB 取込）より前に誤りを報告する。generate の schema 存在チェックのように
    /// プロバイダ解決より前に済ませるべき検証は、このメソッドを呼ぶ前にコマンド側で行うこと。
    /// generate は方言の既定を図から決めるため、図を読んでからこのメソッドを呼ぶ（読込済みの図を
    /// <paramref name="resolveDiagram"/> がそのまま返す）。
    /// 診断（標準エラー相当）と生成ファイル一覧（標準出力相当）は、引数の
    /// <paramref name="stdout"/> / <paramref name="stderr"/> へ書き出す。
    /// </remarks>
    public static async Task<int> RunAsync(
        string providerName,
        DirectoryInfo output,
        FileInfo? config,
        ParseResult parseResult,
        GenerationOptionSet generation,
        bool force,
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

        // 設定の読解は図に依らないため、図の取得より先に行う（scaffold が DB 取込を終えてから設定の誤りに気づかない）
        CodeGenerationOptions options;

        try
        {
            options = GenerationConfigLoader.LoadOptions(
                config,
                provider,
                parseResult,
                generation,
                stderr
            );
        }
        catch (GenerationConfigException ex)
        {
            // 設定ファイルの不在・不正 JSON・値の型違い・未対応の Repository 方言
            // （既定値のまま黙って続行せず、生成前に中止する）
            stderr.WriteLine(ex.Message);
            return 1;
        }

        var diagram = await resolveDiagram(provider, cancellationToken).ConfigureAwait(false);

        if (diagram is null)
        {
            // 図の取得に失敗（コマンド側でエラー表示済み）
            return 1;
        }

        var exitCode = GenerateWithResolvedOptions(
            provider,
            diagram,
            options,
            output,
            force,
            stdout,
            stderr
        );

        if (exitCode == ModifiedFilesExitCode)
        {
            stderr.WriteLine(Strings.Cli_ModifiedFilesForceHint);
        }

        return exitCode;
    }

    /// <summary>
    /// ParseResult 非依存で「設定ファイル（<paramref name="config"/>）＋既定値」からオプションを解決し、
    /// 図（<paramref name="diagram"/>）を <paramref name="output"/> へ生成する。CLI の <see cref="ParseResult"/> を
    /// 経由しない経路（MCP の generate_csharp ツール等）が使う。
    /// </summary>
    /// <remarks>
    /// 診断（stderr）と生成ファイル一覧（stdout）は、引数の <paramref name="stdout"/> / <paramref name="stderr"/>
    /// へ書き出す（呼び出し側は <see cref="StringWriter"/> を渡せばそのまま捕捉できる）。
    /// 設定エラー（<see cref="GenerationConfigException"/> ＝設定ファイル不在・不正 JSON・値の型違い。派生の
    /// <see cref="RepositoryDialectUnsupportedException"/> を含む）やその他の例外は呼び出し側へ伝播する。
    /// </remarks>
    public static int GenerateFromConfig(
        IDatabaseProvider provider,
        ErDiagram diagram,
        FileInfo? config,
        DirectoryInfo output,
        bool force,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        var options = GenerationConfigLoader.LoadOptions(config, provider, stderr);
        return GenerateWithResolvedOptions(
            provider,
            diagram,
            options,
            output,
            force,
            stdout,
            stderr
        );
    }

    /// <summary>解決済みオプションで方言別マッパを解決し、生成・書き出しを行う共通コア</summary>
    private static int GenerateWithResolvedOptions(
        IDatabaseProvider provider,
        ErDiagram diagram,
        CodeGenerationOptions options,
        DirectoryInfo output,
        bool force,
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
        return WriteResult(result, output, options, force, stdout, stderr);
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
    /// <para>
    /// <paramref name="force"/> が偽のとき、生成後に手で編集された既存ファイル（<see cref="GeneratedFileWriter.FindModifiedFiles"/>）
    /// があれば一覧を stderr へ出し、何も書かずに <see cref="ModifiedFilesExitCode"/> を返す。
    /// </para>
    /// <para>
    /// <paramref name="options"/>.<see cref="CodeGenerationOptions.UseRuntimePackages"/> が有効な場合、
    /// 生成成功後に必要な PackageReference の案内（<see cref="RuntimePackageReferenceGuidance"/>）を続けて出力する。
    /// </para>
    /// </remarks>
    private static int WriteResult(
        CodeGenerationResult result,
        DirectoryInfo output,
        CodeGenerationOptions options,
        bool force,
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

        var writer = new GeneratedFileWriter();

        // 生成後に手で編集された既存ファイルは、上書きの指定が無い限り 1 つも書かずに止める
        // （一部だけ書くと生成物どうしが食い違うため、判定は書き出しより前に全ファイル分まとめて行う）
        if (!force)
        {
            var modifiedPaths = writer.FindModifiedFiles(output.FullName, result);

            if (modifiedPaths.Count > 0)
            {
                stderr.WriteLine(Strings.Cli_ModifiedFilesDetected);

                foreach (var path in modifiedPaths)
                {
                    stderr.WriteLine($"  {path}");
                }

                return ModifiedFilesExitCode;
            }
        }

        Directory.CreateDirectory(output.FullName);
        var written = writer.WriteFiles(output.FullName, result);

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
