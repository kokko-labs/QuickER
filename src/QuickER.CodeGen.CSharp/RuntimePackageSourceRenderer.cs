namespace QuickER.CodeGen.CSharp;

/// <summary>
/// QuickER のランタイム（スキーマ非依存の固定コード）を、NuGet パッケージ用の C# ソースとしてレンダリングする。
/// </summary>
/// <remarks>
/// <para>
/// ソースの正本は <c>Templates/CSharpRuntime/*.scriban</c>（<see cref="ScribanCSharpRenderer"/> 経由）で、通常生成と同一。
/// ここでは「空の ER 図＋全機能 ON＋<c>runtime_package_export=true</c>＋<c>infra_visibility="public"</c>」でレンダリングし、
/// スキーマ依存物（Entity / EditModel / Mapper / I{Entity}Repository / DI 登録など）を一切含まない固定 infra だけを
/// 6 パッケージ（<see cref="RuntimePackages"/>）のソースへ切り出す。
/// </para>
/// <para>
/// 分割規則:
/// <list type="bullet">
///   <item><b>Core</b>（<see cref="RuntimePackages.Core"/>）: 共通基盤（属性・EntityBase・EditModelBase・VO 基底・
///     JSON コンバータ）＋方言中立の Repository 共通契約（IRepository・ISqlExecutor・SqlQuery・RawSqlMapper 等）</item>
///   <item><b>SqlServer / Sqlite</b>: 方言エンジンの固定コード（方言 Repository 基底・式木翻訳・実行器・接続ファクトリ・
///     方言別メタデータ）。<c>using QuickER.Runtime;</c> でコアの契約を参照する</item>
///   <item><b>EfCore</b>: EF Core 共通部品（EF Core 版 Repository 基底・VO 翻訳プラグイン・SaveConflict 変換・DbContext 基盤）。
///     同じく <c>using QuickER.Runtime;</c> 付き</item>
///   <item><b>InMemory</b>: DB 非依存のインメモリエンジン（InMemoryDataStore・InMemoryRepository 基底・保存ステージング・
///     式木評価）。ADO も EF Core も参照せず、同じく <c>using QuickER.Runtime;</c> 付き</item>
///   <item><b>AspNetCore</b>: リモートサーバーの固定エンジン（RemoteServerEngine・エラー分類・詳細公開ポリシー・
///     バイナリ転送の補助型）。ASP.NET Core の FrameworkReference のみに依存し、同じく <c>using QuickER.Runtime;</c> 付き</item>
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
    /// 方言実装（ADO 依存）・EF Core 部品・スキーマ依存物は含めない（BCL のみ依存）。
    /// </remarks>
    public string RenderCore()
    {
        var options = BuildAllFeaturesOptions();
        var model = BuildEmptyModel();

        // Runtime（共通基盤）＋ Repository 契約（ContractOnly）の using を、通常生成と同じ解決器から得る。
        // 契約のみのため ADO（SqlClient / Sqlite）・DI は付かない。
        // リモートクライアントの固定 infra（HttpRemoteRepository 等）を含めるため includeRemoteServices を立てるが、
        // DI 登録拡張（AddGeneratedHttpRemoteRepositories）はスキーマ依存物でパッケージに入れないため、
        // その using（Microsoft.Extensions.DependencyInjection(.Extensions)）は除外して Core の依存ゼロを保つ
        // （方言エンジンパッケージの除外と同じ理由）。
        var usings = ResolveUsings(
            options,
            [GenerationBucket.Runtime, GenerationBucket.Repository],
            dialect: "sqlserver",
            contractOnly: true,
            generateRepositories: true,
            crossUsings: [],
            includeRemoteServices: true,
            emitSchemaDependent: false
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
    /// QuickER の SQL Server エンジンパッケージ（<see cref="RuntimePackages.SqlServer"/>）のソースをレンダリングする。
    /// </summary>
    public string RenderSqlServer() => RenderDialectEngine("sqlserver", RuntimePackages.SqlServer);

    /// <summary>
    /// QuickER の SQLite エンジンパッケージ（<see cref="RuntimePackages.Sqlite"/>）のソースをレンダリングする。
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
            crossUsings: [RuntimePackages.Core],
            emitSchemaDependent: false
        );

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
    /// EF Core 共通部品パッケージ（<see cref="RuntimePackages.EntityFrameworkCore"/>）のソースをレンダリングする。
    /// </summary>
    /// <remarks>
    /// EF Core 版 Repository 基底・VO 翻訳プラグイン・SaveConflict 変換・DbContext 基盤と、EF Core が使うメタデータ
    /// （EntitySaveMetadata / EntityGraphSaver）を、名前空間 <c>QuickER.Runtime.EntityFrameworkCore</c> で 1 ファイルへ出力する。
    /// 共通契約（IRepository・SqlQuery 等）はコアを <c>using QuickER.Runtime;</c> で参照する（重複定義しない）。
    /// </remarks>
    public string RenderEfCore()
    {
        var options = BuildAllFeaturesOptions();

        // EF Core 部品はメタデータ（EntitySaveMetadata 等）を必要とする。runtime_package_export の EF Core パッケージ経路で
        // これらを EF Core 名前空間へ出すため、空の EF Core モデル（DbSet・構成なし）を与え、DbContext 基盤と固定 infra だけを描く。
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

        // EF Core パッケージは EntitySaveMetadata / EntityGraphSaver（バックエンド共通メタデータ）も内包するため、
        // EfCore バケットに加えて Repository 契約バケットの using（ConcurrentDictionary・DataAnnotations・
        // Globalization・式木リフレクション等）も取り込む。ContractOnly＝ADO は付かない（EF Core 依存のみ）。
        // DI（Microsoft.Extensions.DependencyInjection(.Extensions)）はコア・方言エンジンと違って除外しない
        // ＝EF Core Relational が DI 抽象を推移的に連れてくるため依存が増えず、除外する理由がない。
        var usings = ResolveUsings(
            options,
            [GenerationBucket.EfCore, GenerationBucket.Repository],
            dialect: "sqlserver",
            contractOnly: true,
            generateRepositories: false,
            crossUsings: [RuntimePackages.Core],
            emitSchemaDependent: false
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

    /// <summary>
    /// インメモリ基盤パッケージ（<see cref="RuntimePackages.InMemory"/>）のソースをレンダリングする。
    /// </summary>
    /// <remarks>
    /// インメモリエンジンの固定コード（InMemoryDataStore・InMemoryRepository 基底・保存ステージング・読み書きスコープ・
    /// 式木評価）と、それが使うバックエンド共通メタデータ（EntitySaveMetadata / SaveHookSession / EntityGraphSaver）を、
    /// 名前空間 <c>QuickER.Runtime.InMemory</c> で 1 ファイルへ出力する。共通契約（IRepository・SqlQuery・
    /// ISaveHookContext 等）はコアを <c>using QuickER.Runtime;</c> で参照する（重複定義しない）。
    /// 方言 ADO・EF Core・DI いずれにも依存しない（BCL のみ）。
    /// </remarks>
    public string RenderInMemory()
    {
        var options = BuildAllFeaturesOptions();
        var model = BuildEmptyModel();

        // InMemory バケットの using（BCL＋共有メタデータ用）＋コア契約 namespace（QuickER.Runtime）を付ける。
        // 方言実装（ADO）の using は Repository バケットを含めないため付かない。DI 登録拡張
        // （AddGeneratedInMemoryRepositories）はスキーマ依存物で出力されないため、DI の using も
        // 除いてパッケージを Microsoft.Extensions.DependencyInjection 非依存に保つ（コア・方言エンジンと同じ規則）。
        var usings = ResolveUsings(
            options,
            [GenerationBucket.InMemory],
            dialect: "sqlserver",
            contractOnly: false,
            generateRepositories: false,
            crossUsings: [RuntimePackages.Core],
            emitSchemaDependent: false
        );

        var scope = BuildScope(
            RuntimePackages.InMemory,
            usings,
            dialect: "sqlserver",
            runtime: false,
            renderContract: false,
            repositoryImpl: false,
            efCore: false,
            inMemory: true
        );

        return Wrap(_renderer.Render(model, options, scope));
    }

    /// <summary>
    /// リモートサーバー基盤パッケージ（<see cref="RuntimePackages.AspNetCore"/>）のソースをレンダリングする。
    /// </summary>
    /// <remarks>
    /// サーバー実装の固定コード（<c>RemoteServerEngine</c>＝リクエスト読み取り・例外分類・応答書き込み・汎用 CRUD
    /// マッピングと、<c>RemoteBadRequestException</c> / <c>RemoteErrorDetailPolicy</c> / バイナリ転送の補助型）を、
    /// 名前空間 <c>QuickER.Runtime.AspNetCore</c> で 1 ファイルへ出力する。共通契約（<c>RemoteJson</c>・転送エンベロープ・
    /// <c>SaveConflictException</c> 等）はコアを <c>using QuickER.Runtime;</c> で参照する（重複定義しない）。
    /// 外部依存は ASP.NET Core の <c>FrameworkReference</c> のみ（方言 ADO・EF Core は参照しない）。
    /// </remarks>
    public string RenderAspNetCore()
    {
        var options = BuildAllFeaturesOptions();
        var model = BuildEmptyModel();

        // RemoteServer バケットの using（ASP.NET Core・DI・ロギング・JSON）＋コア契約 namespace（QuickER.Runtime）。
        // DI の using はコア・方言エンジンと違って除外しない＝固定部のエンジン自身がリポジトリと ILoggerFactory を
        // DI から解決するため必須で、ASP.NET Core の FrameworkReference が推移的に連れてくるので依存も増えない
        // （EF Core パッケージと同じ理由）。
        var usings = ResolveUsings(
            options,
            [GenerationBucket.RemoteServer],
            dialect: "sqlserver",
            contractOnly: false,
            generateRepositories: false,
            crossUsings: [RuntimePackages.Core],
            emitSchemaDependent: false
        );

        var scope = BuildScope(
            RuntimePackages.AspNetCore,
            usings,
            dialect: "sqlserver",
            runtime: false,
            renderContract: false,
            repositoryImpl: false,
            efCore: false,
            remoteServer: true
        );

        return Wrap(_renderer.Render(model, options, scope));
    }

    /// <summary>
    /// 双方向同期エンジンパッケージ（<see cref="RuntimePackages.Sync"/>）のソースをレンダリングする。
    /// </summary>
    /// <remarks>
    /// 同期の固定エンジン（<c>SyncEngine</c>・<c>SyncJournal</c>・<c>SyncTable</c> 基底・<c>SyncSession</c>・
    /// 結果／競合レコード）を、名前空間 <c>QuickER.Runtime.Sync</c> で 1 ファイルへ出力する。共通契約
    /// （<c>IRepository</c>・<c>ISqlExecutor</c>・<c>ConcurrencyMode</c>・<c>SaveConflictException</c>・<c>EntityBase</c>）は
    /// コアを <c>using QuickER.Runtime;</c> で参照する。per-entity の記述子・デコレータ・直結差分ソース・DI 登録は
    /// スキーマ依存物として生成側に残るため、DI への依存も持たない（BCL のみ）。
    /// </remarks>
    public string RenderSync()
    {
        var options = BuildAllFeaturesOptions();
        var model = BuildEmptyModel();

        // HTTP 差分ソースの固定基底（HttpSyncServerSource＝コアの HttpRemoteRepository 派生）を含めるため
        // includeRemoteServices を立てる（HttpClient の using が要る）。DI 登録拡張と per-entity クライアントは
        // スキーマ依存物のため入らない（emitSchemaDependent: false）。
        var usings = ResolveUsings(
            options,
            [GenerationBucket.Sync],
            dialect: "sqlserver",
            contractOnly: false,
            generateRepositories: false,
            crossUsings: [RuntimePackages.Core],
            includeRemoteServices: true,
            emitSchemaDependent: false
        );

        var scope = BuildScope(
            RuntimePackages.Sync,
            usings,
            dialect: "sqlserver",
            runtime: false,
            renderContract: false,
            repositoryImpl: false,
            efCore: false,
            sync: true
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
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
            // リモートサービスのクライアント側固定 infra（RemoteJson・RemoteRepositoryException・HttpRemoteRepository 等）
            // は BCL のみ依存のため Core パッケージへ含める（per-entity クライアント・DI 登録はスキーマ依存物として
            // 常に生成側＝!runtime_package_export ゲートで除外される）
            GenerateRemoteServices = true,
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
        IReadOnlyList<string> crossUsings,
        bool includeRemoteServices = false,
        bool emitSchemaDependent = true
    )
    {
        // GenerateRepositories（Repository バケットの ADO using 有無）と GenerateRemoteServices
        // （HttpClient / JSON の using 有無。リモート固定 infra を内包するのはコアパッケージのみ）を
        // パッケージごとに切り替える。
        var usingOptions = new CodeGenerationOptions
        {
            GenerateEditModels = options.GenerateEditModels,
            GenerateMappers = options.GenerateMappers,
            GenerateRepositories = generateRepositories,
            GenerateEfCoreRepositories = options.GenerateEfCoreRepositories,
            GenerateRemoteServices = includeRemoteServices && options.GenerateRemoteServices,
            GenerateValueObjects = options.GenerateValueObjects,
            IncludeDataAnnotations = options.IncludeDataAnnotations,
            IncludeJsonIgnoreOnParentNavigation = options.IncludeJsonIgnoreOnParentNavigation,
            // パッケージソースは全機能 ON でレンダリングするため、無制限バイナリの共有ヘルパー・Stream
            // アクセサのエンジン（System.IO を要求する固定 infra）を常に含む
            ExcludeUnboundedBinaryColumns = true,
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
            // false のとき DI 登録拡張（スキーマ依存物）の using が落ちる＝コア・方言エンジンパッケージの
            // Microsoft.Extensions.DependencyInjection 非依存を保つ（分割生成の Runtime 系ファイルと共通の規則）
            EmitSchemaDependent = emitSchemaDependent,
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
        bool efCore,
        bool inMemory = false,
        bool remoteServer = false,
        bool sync = false
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
            // インメモリ基盤の固定 infra は専用パッケージ（QuickER.Runtime.InMemory）だけが持つ。
            // per-entity のインメモリ実装・シーダー・DI 登録はスキーマ依存物として生成側に残る
            // （EmitSchemaDependent=false が抑止する）。
            InMemory = inMemory,
            // サーバー実装の固定部（RemoteServerEngine ほか）は専用パッケージ（QuickER.Runtime.AspNetCore）だけが持つ。
            // per-entity のエンドポイント（GeneratedRemoteEndpoints）はスキーマ依存物として生成側に残る
            // （EmitSchemaDependent=false が抑止する）。
            RemoteServer = remoteServer,
            // 同期の固定エンジンは専用パッケージ（QuickER.Runtime.Sync）だけが持つ。per-entity の記述子・
            // デコレータ・直結差分ソース・DI 登録はスキーマ依存物として生成側に残る（EmitSchemaDependent=false が抑止する）。
            Sync = sync,
            Dialect = dialect,
            MultiDialect = false,
            BlockNamespace = false,
            RenderHeader = true,
            // パッケージ書き出しモード: 空図でも固定 infra を完全出力し、infra 型を public 化する。
            RuntimePackageExport = true,
            InfraVisibility = PublicVisibility,
            // per-entity クラス・DI 登録拡張・DbContext などのスキーマ依存物はパッケージに入れない
            // （従来の !runtime_package_export ゲートと同じ意味を、固定 infra 軸と直交する第 2 軸で表す）。
            EmitSchemaDependent = false,
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
            "// This runtime-package source file is generated from QuickER's Scriban templates"
            + Environment.NewLine
            + "// (Templates/CSharpRuntime/*.scriban). Do not edit by hand; regeneration overwrites it."
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
