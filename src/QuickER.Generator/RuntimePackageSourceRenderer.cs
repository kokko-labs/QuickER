namespace QuickER.Generator;

/// <summary>
/// QuickER のランタイム（スキーマ非依存の固定コード）を、NuGet パッケージ用の C# ソースとしてレンダリングする。
/// </summary>
/// <remarks>
/// <para>
/// ソースの正本は <c>Templates/CSharpRuntime.scriban</c>（<see cref="ScribanCSharpRenderer"/> 経由）で、通常生成と同一。
/// ここでは「空の ER 図＋全機能 ON＋<c>runtime_package_export=true</c>＋<c>infra_visibility="public"</c>」でレンダリングし、
/// スキーマ依存物（Entity / EditModel / Mapper / I{Entity}Repository / DI 登録など）を一切含まない固定 infra だけを
/// 4 パッケージ（<see cref="RuntimePackages"/>）のソースへ切り出す。
/// </para>
/// <para>
/// 分割規則:
/// <list type="bullet">
///   <item><b>Core</b>（<see cref="RuntimePackages.Core"/>）: 共通基盤（属性・EntityBase・EditModelBase・VO 基底・
///     JSON コンバータ）＋方言中立の Repository 共通契約（IRepository・ISqlExecutor・SqlQuery・RawSqlMapper 等）</item>
///   <item><b>SqlServer / Sqlite</b>: 方言エンジンの固定コード（方言 Repository 基底・式木翻訳・実行器・接続ファクトリ・
///     方言別メタデータ）。<c>using QuickER.Runtime;</c> でコアの契約を参照する</item>
///   <item><b>EfCore</b>: EF 共通部品（EF 版 Repository 基底・VO 翻訳プラグイン・SaveConflict 変換・DbContext 基盤）。
///     同じく <c>using QuickER.Runtime;</c> 付き</item>
/// </list>
/// </para>
/// </remarks>
public sealed class RuntimePackageSourceRenderer
{
    /// <summary>固定コードのソースを描画する Scriban レンダラー（通常生成と同一経路）</summary>
    private readonly ScribanCSharpRenderer _renderer = new();

    /// <summary>パッケージ書き出し時の固定 infra 可視性（別アセンブリ／別パッケージから参照させるため public）</summary>
    private const string PublicVisibility = "public";

    /// <summary>
    /// コアパッケージ（<see cref="RuntimePackages.Core"/>）のソースをレンダリングする。
    /// </summary>
    /// <remarks>
    /// 共通基盤（全機能 ON）＋方言中立の Repository 共通契約を、名前空間 <c>QuickER.Runtime</c> で 1 ファイルへ出力する。
    /// 方言実装（ADO 依存）・EF 部品・スキーマ依存物は含めない（BCL のみ依存）。
    /// </remarks>
    public string RenderCore()
    {
        var options = BuildAllFeaturesOptions();
        var model = BuildEmptyModel();

        // Runtime（共通基盤）＋ Repository 契約（ContractOnly）の using を、通常生成と同じ解決器から得る。
        // 契約のみのため ADO（SqlClient / Sqlite）・DI は付かない。
        var usings = ResolveUsings(
            options,
            [GenerationBucket.Runtime, GenerationBucket.Repository],
            dialect: "sqlserver",
            contractOnly: true,
            generateRepositories: true,
            crossUsings: []
        );

        var scope = BuildScope(
            RuntimePackages.Core,
            usings,
            dialect: "sqlserver",
            runtime: true,
            renderContract: true,
            repositoryImpl: false,
            efCore: false
        );

        return Wrap(_renderer.Render(model, options, scope));
    }

    /// <summary>
    /// 自作 SQL Server エンジンパッケージ（<see cref="RuntimePackages.SqlServer"/>）のソースをレンダリングする。
    /// </summary>
    public string RenderSqlServer() => RenderDialectEngine("sqlserver", RuntimePackages.SqlServer);

    /// <summary>
    /// 自作 SQLite エンジンパッケージ（<see cref="RuntimePackages.Sqlite"/>）のソースをレンダリングする。
    /// </summary>
    public string RenderSqlite() => RenderDialectEngine("sqlite", RuntimePackages.Sqlite);

    /// <summary>
    /// 方言エンジンの固定コード（方言 Repository 基底・式木翻訳・実行器・接続ファクトリ・方言別メタデータ）を、
    /// 指定方言・指定名前空間で 1 ファイルへ出力する。コアの共通契約は <c>using QuickER.Runtime;</c> で参照する。
    /// </summary>
    private string RenderDialectEngine(string dialect, string packageNamespace)
    {
        var options = BuildAllFeaturesOptions();
        var model = BuildEmptyModel();

        // 方言実装スペックの using（その方言の ADO）＋コア契約 namespace（QuickER.Runtime）を付ける。
        // DI 登録拡張（AddGenerated*Repositories）はエンティティ別登録を含むスキーマ依存物で、
        // パッケージ書き出しでは出力しない（テンプレート側で runtime_package_export により抑止）ため、
        // DI の using も除いてパッケージを Microsoft.Extensions.DependencyInjection 非依存に保つ。
        var usings = ResolveUsings(
                options,
                [GenerationBucket.Repository],
                dialect: dialect,
                contractOnly: false,
                generateRepositories: true,
                crossUsings: [RuntimePackages.Core]
            )
            .Where(u => u != "Microsoft.Extensions.DependencyInjection")
            .ToList();

        var scope = BuildScope(
            packageNamespace,
            usings,
            dialect: dialect,
            runtime: false,
            renderContract: false,
            repositoryImpl: true,
            efCore: false
        );

        return Wrap(_renderer.Render(model, options, scope));
    }

    /// <summary>
    /// EF 共通部品パッケージ（<see cref="RuntimePackages.EntityFrameworkCore"/>）のソースをレンダリングする。
    /// </summary>
    /// <remarks>
    /// EF 版 Repository 基底・VO 翻訳プラグイン・SaveConflict 変換・DbContext 基盤と、EF が使うメタデータ
    /// （EntitySaveMetadata / EntityGraphSaver）を、名前空間 <c>QuickER.Runtime.EntityFrameworkCore</c> で 1 ファイルへ出力する。
    /// 共通契約（IRepository・SqlQuery 等）はコアを <c>using QuickER.Runtime;</c> で参照する（重複定義しない）。
    /// </remarks>
    public string RenderEfCore()
    {
        var options = BuildAllFeaturesOptions();

        // EF 部品はメタデータ（EntitySaveMetadata 等）を必要とする。runtime_package_export の EF パッケージ経路で
        // これらを EF 名前空間へ出すため、空の EF モデル（DbSet・構成なし）を与え、DbContext 基盤と固定 infra だけを描く。
        var model = new CSharpGenerationModel
        {
            NamespaceName = string.Empty,
            EntityClasses = [],
            EditModelClasses = [],
            MapperClasses = [],
            RepositoryClasses = [],
            ValueObjectClasses = [],
            EfCore = new CSharpEfCoreModel
            {
                DbSets = [],
                Entities = [],
                IgnoredBaseMembers = [],
            },
        };

        // EF パッケージは EntitySaveMetadata / EntityGraphSaver（バックエンド共通メタデータ）も内包するため、
        // EfCore バケットに加えて Repository 契約バケットの using（ConcurrentDictionary・DataAnnotations・
        // Globalization・式木リフレクション等）も取り込む。ContractOnly＝ADO・DI は付かない（EF 依存のみ）。
        var usings = ResolveUsings(
            options,
            [GenerationBucket.EfCore, GenerationBucket.Repository],
            dialect: "sqlserver",
            contractOnly: true,
            generateRepositories: false,
            crossUsings: [RuntimePackages.Core]
        );

        var scope = BuildScope(
            RuntimePackages.EntityFrameworkCore,
            usings,
            dialect: "sqlserver",
            runtime: false,
            renderContract: false,
            repositoryImpl: false,
            efCore: true
        );

        return Wrap(_renderer.Render(model, options, scope));
    }

    /// <summary>全機能 ON（VO・データアノテーション等すべて有効）の生成オプションを構築する</summary>
    /// <remarks>
    /// 固定 infra を最大構成で書き出すため、機能フラグはすべて有効にする。空の ER 図と組み合わせるため、
    /// エンティティ別のループは何も出さず、固定コードだけが残る。
    /// </remarks>
    private static CodeGenerationOptions BuildAllFeaturesOptions() =>
        new()
        {
            GenerateEntityClasses = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateEfCore = true,
            GenerateValueObjects = true,
            IncludeDataAnnotations = true,
            IncludeJsonIgnoreOnParentNavigation = true,
        };

    /// <summary>スキーマ依存物を持たない空の生成モデルを構築する（固定 infra だけを残すため）</summary>
    private static CSharpGenerationModel BuildEmptyModel() =>
        new()
        {
            NamespaceName = string.Empty,
            EntityClasses = [],
            EditModelClasses = [],
            MapperClasses = [],
            RepositoryClasses = [],
            ValueObjectClasses = [],
            EfCore = null,
        };

    /// <summary>
    /// 通常生成と同じ using 解決器（<see cref="GeneratedFileUsings"/>）で、パッケージソースの using を得る。
    /// </summary>
    /// <remarks>using の正本を 1 箇所に保つため、バケット・方言・契約有無をスペックへ写して解決させる。</remarks>
    private static IReadOnlyList<string> ResolveUsings(
        CodeGenerationOptions options,
        IReadOnlyList<GenerationBucket> buckets,
        string dialect,
        bool contractOnly,
        bool generateRepositories,
        IReadOnlyList<string> crossUsings
    )
    {
        // GenerateRepositories は Repository バケットの ADO using 有無に影響するため、パッケージごとに切り替える。
        var usingOptions = generateRepositories
            ? options
            : new CodeGenerationOptions
            {
                GenerateEntityClasses = options.GenerateEntityClasses,
                GenerateEditModels = options.GenerateEditModels,
                GenerateMappers = options.GenerateMappers,
                GenerateRepositories = false,
                GenerateEfCore = options.GenerateEfCore,
                GenerateValueObjects = options.GenerateValueObjects,
                IncludeDataAnnotations = options.IncludeDataAnnotations,
                IncludeJsonIgnoreOnParentNavigation = options.IncludeJsonIgnoreOnParentNavigation,
            };

        var spec = new GeneratedFileSpec
        {
            FileName = "Package.g.cs",
            NamespaceName = string.Empty,
            Buckets = buckets,
            CrossNamespaceUsings = crossUsings,
            Dialect = dialect,
            ContractOnly = contractOnly,
            MultiDialect = false,
        };

        return GeneratedFileUsings.Resolve(spec, usingOptions);
    }

    /// <summary>パッケージソース 1 ファイル分の描画スコープを組み立てる</summary>
    private static RenderScope BuildScope(
        string namespaceName,
        IReadOnlyList<string> usings,
        string dialect,
        bool runtime,
        bool renderContract,
        bool repositoryImpl,
        bool efCore
    ) =>
        new()
        {
            NamespaceName = namespaceName,
            Usings = usings,
            Runtime = runtime,
            // VO・Entity・EditModel・Mapper の具象は空図で何も出ないため、バケットのオン/オフは固定 infra の
            // 出力位置（render_runtime）に集約する。値オブジェクト基底は render_runtime 側で描かれる。
            ValueObjects = false,
            Entities = false,
            EditModels = false,
            Mappers = false,
            EfCore = efCore,
            RepositoryImpl = repositoryImpl,
            RenderContract = renderContract,
            Dialect = dialect,
            MultiDialect = false,
            BlockNamespace = false,
            RenderHeader = true,
            // パッケージ書き出しモード: 空図でも固定 infra を完全出力し、infra 型を public 化する。
            RuntimePackageExport = true,
            InfraVisibility = PublicVisibility,
        };

    /// <summary>
    /// レンダリング結果の先頭へ「パッケージソース・手編集禁止」の日本語コメントを添える。
    /// </summary>
    /// <remarks>
    /// 先頭行はレンダラーが出力する <c>// &lt;auto-generated /&gt;</c>。その直後に本ファイルの由来（Scriban 正本）を明記する。
    /// </remarks>
    private static string Wrap(string rendered)
    {
        const string marker = "// <auto-generated />";
        var note =
            "// このファイルは QuickER の Scriban テンプレート（Templates/CSharpRuntime.scriban）から生成される"
            + Environment.NewLine
            + "// ランタイムパッケージ用ソースです。手で編集しないでください（再生成で上書きされます）。"
            + Environment.NewLine;

        // 先頭の auto-generated 行の直後へ由来コメントを差し込む（ヘッダの一部として自然に見せる）。
        if (rendered.StartsWith(marker, StringComparison.Ordinal))
        {
            var insertAt = marker.Length + Environment.NewLine.Length;
            return rendered[..insertAt] + note + rendered[insertAt..];
        }

        return marker + Environment.NewLine + note + rendered;
    }
}
