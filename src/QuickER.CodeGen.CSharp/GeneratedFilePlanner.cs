namespace QuickER.CodeGen.CSharp;

/// <summary>生成コードのカテゴリ（バケット）。共有基盤と各クラス種別を表す</summary>
public enum GenerationBucket
{
    /// <summary>共有基盤：基底クラス・属性・VO 基底・JSON コンバータ・RowState など</summary>
    Runtime,

    /// <summary>値オブジェクト（Value Object）の具象クラス</summary>
    ValueObject,

    /// <summary>Entity クラス</summary>
    Entity,

    /// <summary>EditModel クラス</summary>
    EditModel,

    /// <summary>Mapper クラス</summary>
    Mapper,

    /// <summary>Repository クラス群（インターフェース・基底・DI 拡張を含む）</summary>
    Repository,

    /// <summary>EF Core 用コード（DbContext と Fluent API 構成）</summary>
    EfCore,

    /// <summary>DB 非依存のインメモリ Repository 群（InMemoryDataStore・InMemory{Entity}Repository・シーダー・DI）</summary>
    InMemory,

    /// <summary>リモート面の ASP.NET Core サーバー実装（エンドポイントマッピング。常に別ファイル）</summary>
    RemoteServer,

    /// <summary>双方向同期の支援コード（同期記述子・ジャーナル記録デコレータ・直結差分ソース・DI 登録）</summary>
    Sync,

    /// <summary>リモート面の HTTP クライアント実装（Http{Entity}RemoteRepository・AddGeneratedHttpRemoteRepositories・OwnedHttpClient）</summary>
    /// <remarks>
    /// 契約（Repository バケット）から分離した実装バケット。分割時は <c>Repositories.Http.g.cs</c> へ単独出力し、
    /// 名前空間は契約と同じ <c>{RepositoryNamespace}</c> のまま（型 FQN・非分割出力を変えない＝ファイルだけを分ける）。
    /// 契約ファイルを「インターフェイス・DTO だけ」に純化し、HTTP 実装と DI 登録をインフラ側ファイルへ退避するための分割。
    /// </remarks>
    Http,
}

/// <summary>1 つの生成ファイルが「どの名前空間で・どのバケットを含み・どの名前空間を using するか」を表す計画</summary>
/// <remarks>record なのは層別出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）が計画確定後に
/// <c>with</c> で出力先ディレクトリだけを差し替えるため（他プロパティの複製漏れを構造的に防ぐ）</remarks>
public sealed record GeneratedFileSpec
{
    /// <summary>出力ファイル名</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// 出力ディレクトリからの相対サブディレクトリ（null＝出力ディレクトリ直下）。
    /// 層別出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）のときだけ層フォルダが入る。
    /// </summary>
    public string? RelativeDirectory { get; init; }

    /// <summary>このファイルの名前空間</summary>
    public required string NamespaceName { get; init; }

    /// <summary>このファイルに含めるバケット（テンプレートの出力範囲）</summary>
    public required IReadOnlyList<GenerationBucket> Buckets { get; init; }

    /// <summary>このファイルが参照する他ファイルの名前空間（自分自身の名前空間は除外・重複排除済み・昇順）</summary>
    public required IReadOnlyList<string> CrossNamespaceUsings { get; init; }

    /// <summary>
    /// このスペックがレンダリングするQuickER 版 Repository の方言（例: <c>"sqlserver"</c> / <c>"sqlite"</c>）。
    /// </summary>
    /// <remarks>
    /// 単一方言時・非 Repository スペックは実効単一方言をそのまま持つ（従来のレンダラー入力を保つ）。
    /// マルチ方言時は方言実装スペックが各方言を持つ。契約のみのスペック（<see cref="ContractOnly"/>）は
    /// 方言実装を出力しないため方言差分がなく、便宜上 sqlserver 相当を持つ（テンプレートは方言実装を描画しない）。
    /// </remarks>
    public required string Dialect { get; init; }

    /// <summary>
    /// このスペックが「中立契約のみ」を出力し、方言実装（SqlExecutor / 方言別 Repository 基底 / DI 等）を出さないか。
    /// </summary>
    /// <remarks>
    /// マルチ方言時に契約を 1 回だけ出すためのフラグ。true のとき Repository バケットは契約のみをレンダリングし、
    /// 方言実装は別の方言実装スペックが担う。単一方言時は常に false（契約＋実装を同一スコープへ出力する）。
    /// </remarks>
    public required bool ContractOnly { get; init; }

    /// <summary>
    /// マルチ方言レイアウト（実効方言 2 つ以上）かどうか。DI 拡張の方言別名＋keyed 版の出し分けに使う。
    /// </summary>
    /// <remarks>
    /// false（単一方言）でも、分割時は方言別実装レイアウト（契約 1 回＋方言別実装ファイル）を使うため true になる。
    /// DI 拡張の方言別名（<c>AddGenerated{方言}Repositories</c>）は本フラグに依らずエンジン別で統一される。
    /// </remarks>
    public required bool MultiDialect { get; init; }

    /// <summary>
    /// スキーマ非依存の固定 infra（契約・方言エンジン・EF Core 共通部品・インメモリ基盤・EntityBase/属性/VO 基底 等）を
    /// このファイルへ出力するか（既定 true）。
    /// </summary>
    /// <remarks>
    /// 分割時は固定 infra を <c>Runtime*.g.cs</c> へ集約し（true）、<c>Repositories*.g.cs</c> 等はスキーマ依存物だけに
    /// 純化する（false）。非分割は 1 ファイルへ全部入るため既定の true のまま
    /// （パッケージ参照モードでの抑止は <see cref="CodeGenerationOptions.UseRuntimePackages"/> が別途 AND する）。
    /// </remarks>
    public bool EmitSharedInfra { get; init; } = true;

    /// <summary>
    /// スキーマ依存物（per-entity のクラス・インターフェイス、DI 登録拡張、DbContext・シーダー等）を
    /// このファイルへ出力するか（既定 true）。
    /// </summary>
    /// <remarks>
    /// <see cref="EmitSharedInfra"/> と直交する第 2 軸。固定 infra 専用ファイル（<c>Runtime*.g.cs</c>）だけが false で、
    /// その他のファイルは true（非分割は 1 ファイルへ全部入るため既定の true）。
    /// </remarks>
    public bool EmitSchemaDependent { get; init; } = true;
}

/// <summary>層別出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）の振り分け先レイヤ</summary>
/// <remarks>
/// バケット→層の対応は QuickER 固定（ユーザーが変えられるのは各層のフォルダパスだけ）。
/// ドメイン層に Runtime コアと Repository 契約を置くのは、パッケージ参照モードで
/// ドメイン csproj が <c>QuickER.Runtime</c> を参照する構造とインライン生成を対称にするため
/// （契約は DDD のポート＝EditModel の DB 照合依存を「プレゼンテーション→ドメイン」参照へ収める）。
/// </remarks>
public enum GeneratedLayer
{
    /// <summary>ドメイン層（Entity / ValueObject / Repository 契約 / Runtime コア）</summary>
    Domain,

    /// <summary>プレゼンテーション層（EditModel / Mapper）</summary>
    Presentation,

    /// <summary>インフラストラクチャ層（方言別実装 / EF Core / インメモリ / 同期 / HTTP クライアントと各固定 infra）</summary>
    Infrastructure,

    /// <summary>サーバー層（リモートサーバー実装＋ASP.NET Core 固定部。FrameworkReference を要するため独立プロジェクト前提）</summary>
    Server,
}

/// <summary>
/// 生成オプションから「どのファイルを・どの名前空間で・どのバケット構成で出力するか」を決める純粋ロジック
/// </summary>
/// <remarks>生成サービスと UI のプレビューが同じ結果を共有するため、状態を持たない静的関数として実装する</remarks>
public static class GeneratedFilePlanner
{
    /// <summary>名前空間が空のときに使う最終フォールバック</summary>
    private const string DefaultRootNamespace = "Generated";

    /// <summary>ベース（ルート）名前空間を解決する（空白は除去し、空なら既定値）</summary>
    public static string ResolveRootNamespace(CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.IsNullOrWhiteSpace(options.RootNamespace)
            ? DefaultRootNamespace
            : options.RootNamespace.Trim();
    }

    /// <summary>指定バケットの名前空間を解決する（個別指定が空ならモード別の既定へフォールバック）</summary>
    /// <remarks>
    /// この解決は分割時のみ使用する。フォールバックは UI のプリフィル（<see cref="DefaultSuffix"/>）と一致させ、
    /// 規約を 1 箇所に集約する。既定は通常分割で <c>{root}.{サフィックス}</c>（例 <c>{root}.Entities</c>）、
    /// 層別出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）では <c>{層ルート}.{サフィックス}</c>
    /// （例 <c>MyApp.Domain.Entities</c>＝層ルートは <see cref="LayerNamespaceRoot"/> がフォルダパスから導出する。
    /// 出力フォルダと名前空間を揃えるための既定で、明示指定があればそちらが勝つ）
    /// </remarks>
    public static string ResolveNamespace(CodeGenerationOptions options, GenerationBucket bucket)
    {
        var explicitValue = bucket switch
        {
            GenerationBucket.Runtime => options.RuntimeNamespace,
            GenerationBucket.ValueObject => options.ValueObjectNamespace,
            GenerationBucket.Entity => options.EntityNamespace,
            GenerationBucket.EditModel => options.EditModelNamespace,
            GenerationBucket.Mapper => options.MapperNamespace,
            GenerationBucket.Repository => options.RepositoryNamespace,
            // EfCore に個別の名前空間オプションは設けない（通常分割では {RepositoryNamespace}.EntityFrameworkCore へ
            // 導出専用＝Plan 側の上書き。層別出力では {インフラ層ルート}.EntityFrameworkCore＝ここのフォールバック）
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue.Trim();
        }

        // 層別出力: 名前空間の既定を層フォルダ由来にする（フォルダと名前空間が揃う）。
        // 通常分割: {root}.{サフィックス}
        var baseNamespace = options.LayeredOutput
            ? LayerNamespaceRoot(options, LayerOfBucket(bucket))
            : ResolveRootNamespace(options);

        return $"{baseNamespace}.{DefaultSuffix(bucket)}";
    }

    /// <summary>バケットの既定名前空間が属する層（バケット水準の対応。方言実装・固定部などスペック水準の分類は <see cref="LayerOf"/>）</summary>
    public static GeneratedLayer LayerOfBucket(GenerationBucket bucket) =>
        bucket switch
        {
            GenerationBucket.Entity => GeneratedLayer.Domain,
            GenerationBucket.ValueObject => GeneratedLayer.Domain,
            // 契約（Repositories.g.cs）と Runtime コアはドメイン層（LayerOf と同じ判断＝DDD のポート／パッケージ対称）
            GenerationBucket.Repository => GeneratedLayer.Domain,
            GenerationBucket.Runtime => GeneratedLayer.Domain,
            GenerationBucket.EditModel => GeneratedLayer.Presentation,
            GenerationBucket.Mapper => GeneratedLayer.Presentation,
            GenerationBucket.EfCore => GeneratedLayer.Infrastructure,
            GenerationBucket.InMemory => GeneratedLayer.Infrastructure,
            GenerationBucket.Sync => GeneratedLayer.Infrastructure,
            GenerationBucket.Http => GeneratedLayer.Infrastructure,
            GenerationBucket.RemoteServer => GeneratedLayer.Server,
            _ => GeneratedLayer.Domain,
        };

    /// <summary>
    /// 層フォルダから名前空間ルートを導出する（パス区切り <c>/</c>・<c>\</c> を <c>.</c> へ変換。
    /// 例: <c>MyApp.Domain/Generated</c> → <c>MyApp.Domain.Generated</c>・既定フォルダ <c>Domain</c> → <c>Domain</c>）。
    /// </summary>
    /// <remarks>
    /// csproj の「プロジェクトフォルダ名＝RootNamespace」慣行に合わせ、フォルダと名前空間を機械的に揃える。
    /// 識別子として不正なフォルダ名（ハイフン等）は黙ってサニタイズせず、生成本体の診断がエラーにする
    /// （Plan はプレビューでも呼ばれるためここでは検証しない）。
    /// </remarks>
    public static string LayerNamespaceRoot(CodeGenerationOptions options, GeneratedLayer layer) =>
        string.Join('.', ResolveLayerDirectory(options, layer).TrimEnd('/', '\\').Split('/', '\\'));

    /// <summary>分割時のバケット既定名前空間サフィックス（UI のプリフィルと一致させる）</summary>
    public static string DefaultSuffix(GenerationBucket bucket) =>
        bucket switch
        {
            GenerationBucket.Runtime => "Runtime",
            GenerationBucket.ValueObject => "ValueObjects",
            GenerationBucket.Entity => "Entities",
            GenerationBucket.EditModel => "EditModels",
            GenerationBucket.Mapper => "Mappers",
            GenerationBucket.Repository => "Repositories",
            // ファイル名・名前空間サフィックスは配布パッケージ名のサフィックスと同一規則にする
            // （EF Core は略記 EfCore を使わず QuickER.Runtime.EntityFrameworkCore に揃える。
            //   C# の型名（EfCore{Entity}Repository・QuickErDbContext）は別軸のため現状維持）
            GenerationBucket.EfCore => "EntityFrameworkCore",
            GenerationBucket.InMemory => "InMemory",
            GenerationBucket.RemoteServer => "RemoteServer",
            GenerationBucket.Sync => "Sync",
            // HTTP クライアントはファイル名サフィックスのみに使う（名前空間は契約と同じ {RepositoryNamespace} へ
            // 上書きされるため、ここのサフィックスが名前空間に現れることはない）
            GenerationBucket.Http => "Http",
            _ => "Generated",
        };

    /// <summary>分割時のバケット既定ファイル名</summary>
    public static string DefaultFileName(GenerationBucket bucket) =>
        $"{DefaultSuffix(bucket)}.g.cs";

    /// <summary>
    /// 分割時、あるバケットのファイルが using すべき「他バケットの名前空間」を決めるための依存グラフ。
    /// </summary>
    /// <remarks>
    /// 根拠（<c>Templates/CSharpRuntime/*.scriban</c> の型参照から確定）:
    ///   Entity   → Runtime（EntityBase / 独自属性 / RowState）, ValueObject（プロパティ型が VO）
    ///   EditModel→ Runtime（EditModelBase / EditModelCollection）, ValueObject（VO 由来のパース・検証）,
    ///              Entity・Repository（DB 照合糖衣 ValidateUniqueAsync が {Entity} を組み立て I{Entity}Repository へ問い合わせる）
    ///   Mapper   → Entity（{Entity}）, EditModel（{Entity}EditModel）, Runtime（基底・RowState）
    ///   ValueObject → Runtime（VO 基底 ValueObjectBase / IValueObject / JSON コンバータ）
    ///   Repository → Entity（対象 Entity）, Runtime（契約基底・SqlQuery・メタデータ）, ValueObject（VO 束縛の unwrap）
    ///   EfCore   → Entity（DbSet&lt;{Entity}&gt; / Fluent 構成）, Repository（契約・AddGeneratedEfCoreRepositories）,
    ///              Runtime（EntityBase の Ignore 対象・翻訳プラグイン）, ValueObject（VO の変換構成）
    ///   Runtime  → （他バケットへ依存しない共有基盤）
    /// 依存先が有効バケットに存在しない構成（例: VO 無効）では、その名前空間はクロス using から自然に落ちる。
    /// </remarks>
    private static IReadOnlyList<GenerationBucket> BucketDependencies(GenerationBucket bucket) =>
        bucket switch
        {
            GenerationBucket.Entity => [GenerationBucket.Runtime, GenerationBucket.ValueObject],
            GenerationBucket.EditModel =>
            [
                GenerationBucket.Entity,
                GenerationBucket.Repository,
                GenerationBucket.Runtime,
                GenerationBucket.ValueObject,
            ],
            GenerationBucket.Mapper =>
            [
                GenerationBucket.Entity,
                GenerationBucket.EditModel,
                GenerationBucket.Runtime,
            ],
            GenerationBucket.ValueObject => [GenerationBucket.Runtime],
            GenerationBucket.Repository =>
            [
                GenerationBucket.Entity,
                GenerationBucket.Runtime,
                GenerationBucket.ValueObject,
            ],
            GenerationBucket.EfCore =>
            [
                GenerationBucket.Entity,
                GenerationBucket.Repository,
                GenerationBucket.Runtime,
                GenerationBucket.ValueObject,
            ],
            // インメモリ実装はエンティティ・中立契約（Repository バケット＝I{Entity}Repository / SqlQuery / EntityGraphSaver 等）・
            // 共有基盤（EntityBase / RowState）・VO（主キー unwrap）を参照する
            GenerationBucket.InMemory =>
            [
                GenerationBucket.Entity,
                GenerationBucket.Repository,
                GenerationBucket.Runtime,
                GenerationBucket.ValueObject,
            ],
            // サーバー実装はエンティティ・リモート契約（Repository バケット）・共有基盤（RemoteJson / エンベロープ /
            // SaveConflictException）・VO（主キー型）を参照する
            GenerationBucket.RemoteServer =>
            [
                GenerationBucket.Entity,
                GenerationBucket.Repository,
                GenerationBucket.Runtime,
                GenerationBucket.ValueObject,
            ],
            // 同期支援はエンティティ・中立契約（Repository バケット＝I{Entity}Repository / ISqlExecutor /
            // ConcurrencyMode / SaveConflictException）・共有基盤（EntityBase / RowState / RemoteJson）・
            // VO（主キーの unwrap）を参照する
            GenerationBucket.Sync =>
            [
                GenerationBucket.Entity,
                GenerationBucket.Repository,
                GenerationBucket.Runtime,
                GenerationBucket.ValueObject,
            ],
            // HTTP クライアントはエンティティ・リモート契約（Repository バケット＝I{Entity}RemoteRepository。
            // 通常は同一名前空間のため using からは自然に落ちる）・共有基盤（HttpRemoteRepository 基底 /
            // RemoteJson / RemotePaths）・VO（主キー型）を参照する
            GenerationBucket.Http =>
            [
                GenerationBucket.Entity,
                GenerationBucket.Repository,
                GenerationBucket.Runtime,
                GenerationBucket.ValueObject,
            ],
            _ => [],
        };

    /// <summary>生成対象として有効なバケットを正準順で返す</summary>
    /// <remarks>
    /// 並び順は UI のカテゴリ別 namespace 欄と一致させる（Entity → EditModel → Mapper → ValueObject → Repository → EfCore → Runtime）
    /// Runtime は何らかのクラスを生成する限り常に必要（共有基盤を保持するため）で、UI と同じく末尾に置く
    /// </remarks>
    public static IReadOnlyList<GenerationBucket> ActiveBuckets(CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var active = new List<GenerationBucket>();

        // Entity は全カテゴリの前提のため常に生成する
        active.Add(GenerationBucket.Entity);

        if (options.GenerateEditModels)
        {
            active.Add(GenerationBucket.EditModel);
        }

        if (options.GenerateMappers)
        {
            active.Add(GenerationBucket.Mapper);
        }

        if (options.GenerateValueObjects)
        {
            active.Add(GenerationBucket.ValueObject);
        }

        // Repository バケットは共通契約（インターフェイス・SqlQuery・メタデータ・グラフセーバ・RawSqlMapper 等）＋
        // QuickER の SQL Server 実装を保持する。契約は EF Core 側・インメモリ側も参照するため、QuickER 版 Repository・EF Core・
        // インメモリのいずれかが有効なら出力する。EF Core/インメモリ単独出力時は Repository バケットに「契約のみ」＋
        // インメモリ実装が入る（QuickER の ADO 実装はテンプレート内で出し分ける）
        if (options.GeneratesRepositoryContract)
        {
            active.Add(GenerationBucket.Repository);
        }

        // リモート面の HTTP クライアント（Http{Entity}RemoteRepository・AddGeneratedHttpRemoteRepositories）は
        // 契約から分離した実装バケット。分割時は Repositories.Http.g.cs へ単独出力し、契約ファイルを
        // インターフェイス・DTO だけに保つ（実装先が要るため Repository バケットが有効なときのみ）。
        // 非分割時は同一ファイル内の同じ位置へ描画される（テンプレートの物理順が位置を決める）。
        if (options.GenerateRemoteServices && options.GeneratesRepositoryContract)
        {
            active.Add(GenerationBucket.Http);
        }

        // インメモリ実装は方言非依存の独立バケット。分割時は Repositories.InMemory.g.cs へ単独出力し、
        // 非分割時は他バケットと同一ファイルへ連結する（EfCore バケットと同じ流儀）。
        if (options.GenerateInMemoryRepositories)
        {
            active.Add(GenerationBucket.InMemory);
        }

        if (options.GenerateEfCore)
        {
            active.Add(GenerationBucket.EfCore);
        }

        // 同期支援は方言別実装ではなく「サーバー方言とローカル方言の 2 実装を束ねる」独立バケット。
        // 分割時は Repositories.Sync.g.cs へ単独出力する（EfCore / InMemory と同じ流儀）。
        // Repository 契約が無い構成では実装先が無いため出力しない（診断側でも早期にエラーにする）。
        if (options.GenerateSyncSupport && options.GeneratesRepositoryContract)
        {
            active.Add(GenerationBucket.Sync);
        }

        // Entity を常に生成する＝何らかのクラスが必ず出力されるため、共有基盤（Runtime）は常に必要
        active.Add(GenerationBucket.Runtime);

        return active;
    }

    /// <summary>方言別実装の名前空間サフィックス（<c>{RepositoryNamespace}.SqlServer</c> 等）を返す</summary>
    /// <remarks>UI 入力を増やさず、方言名から自動導出する（プロバイダ名 sqlserver / sqlite に一致）</remarks>
    public static string DialectNamespaceSuffix(string dialect) =>
        string.Equals(dialect, "sqlite", StringComparison.OrdinalIgnoreCase)
            ? "Sqlite"
            : "SqlServer";

    /// <summary>
    /// 出力ファイルの計画を作成する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 非分割時は全バケットを 1 ファイル（<see cref="CodeGenerationOptions.OutputFileName"/>・ルート名前空間）へまとめる。
    /// 分割時は有効バケットを 1 カテゴリ 1 ファイルへ展開し、各ファイルに他ファイルの名前空間を using として付与する
    /// （同一名前空間に解決されてもファイルは分け、自分自身の名前空間は using しない）。
    /// </para>
    /// <para>
    /// 実効方言（<see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/>）が 1 つのときは Repository バケットを
    /// 分割しない。2 つ以上のときは「中立契約（1 回）」と「方言別実装（方言ごと）」に分割し、
    /// 方言実装は <c>{RepositoryNamespace}.SqlServer</c> / <c>.Sqlite</c> の別 namespace へ出す（分割時は別ファイル、
    /// 非分割時は同一ファイルへ namespace ブロックとして連結）。
    /// </para>
    /// <para>
    /// EF Core 実装（<see cref="CodeGenerationOptions.GenerateEfCore"/>）は分割時、方言別実装と同じ流儀で
    /// <c>Repositories.EntityFrameworkCore.g.cs</c>・<c>{RepositoryNamespace}.EntityFrameworkCore</c>
    /// （契約 namespace のサブ名前空間へ導出専用）へ出す。
    /// </para>
    /// <para>
    /// 分割時はさらに、スキーマ非依存の固定 infra を <c>Runtime*.g.cs</c>（配布パッケージ <c>QuickER.Runtime*</c> と
    /// 1:1 対応）へ集約し、<c>Repositories*.g.cs</c> は per-entity・DI 登録・DbContext だけへ純化する。
    /// パッケージ参照モードでは <c>Runtime*.g.cs</c> を 1 本も計画しない（固定 infra はパッケージが持つ）。
    /// </para>
    /// <para>
    /// 前提: <paramref name="options"/> は生成本体（<c>CSharpCodeGenerationService.Generate</c>）が実効方言を検証済みか、
    /// GUI プレビューのように対応方言だけを選ばせた構成である。未対応方言は
    /// <see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/> が
    /// QuickER 版 Repository 非生成時には <c>sqlserver</c> へフォールバックし、生成時には例外にする。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<GeneratedFileSpec> Plan(CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var active = ActiveBuckets(options);
        // 実効方言の先頭を各スペックへ持たせる（単一方言＝現行値。方言リテラル参照をスコープ由来に一本化する）。
        // 型解決・診断・[SqlColumnType] 補完はマルチ辞書として M1 で機能する。
        var dialects = options.EffectiveRepositoryDialects;
        var primaryDialect = dialects[0];

        var repositoryActive = active.Contains(GenerationBucket.Repository);

        // 非分割: マルチ方言（実効方言 2 つ以上）で Repository を生成するときだけ、契約 1 回＋方言別 namespace 実装へ
        // 展開する。単一方言・Repository 非生成時は全バケットを 1 ファイル・1 namespace へまとめる。
        var repositoryMultiDialectInlineLayout =
            options.GenerateRepositories && dialects.Count >= 2 && repositoryActive;

        // 分割: 単一方言でも「契約 1 回＋方言別実装ファイル」レイアウトへ統一する（実効方言が 1 つでも
        // マルチターゲットと同じ形＝Repositories.g.cs＋Repositories.{方言}.g.cs）。Repository を生成するときのみ。
        var repositorySplitLayout = options.GenerateRepositories && repositoryActive;

        if (!options.EffectiveSplitFilesByCategory)
        {
            // 非分割: 全バケットを 1 ファイルへ。マルチ方言時は Repository を「契約スペック＋方言別実装スペック」へ
            // 展開し、同一ファイル名で連結する（RenderFiles が block namespace で連結・using を先頭へ集約）。
            if (!repositoryMultiDialectInlineLayout)
            {
                var singleSpecs = new List<GeneratedFileSpec>
                {
                    new()
                    {
                        FileName = options.OutputFileName,
                        NamespaceName = ResolveRootNamespace(options),
                        Buckets = active,
                        CrossNamespaceUsings = [],
                        Dialect = primaryDialect,
                        ContractOnly = false,
                        MultiDialect = false,
                    },
                };
                AddRemoteServerSpec(singleSpecs, options, active, primaryDialect);

                return singleSpecs;
            }

            var root = ResolveRootNamespace(options);
            var repositoryNamespace = ResolveNamespace(options, GenerationBucket.Repository);
            var specs = new List<GeneratedFileSpec>();

            // 契約＋非 Repository バケット（Entity/EditModel/Mapper/VO/Runtime）はルート namespace の
            // 契約スペックへまとめる。ContractOnly=true で Repository バケットは契約のみを描画する。
            specs.Add(
                new GeneratedFileSpec
                {
                    FileName = options.OutputFileName,
                    NamespaceName = root,
                    Buckets = active,
                    CrossNamespaceUsings = [],
                    Dialect = primaryDialect,
                    ContractOnly = true,
                    MultiDialect = true,
                }
            );

            // 方言別実装スペック（Repository バケットのみ・{RepositoryNamespace}.Suffix）。同一 OutputFileName で
            // 連結する。方言側は契約 namespace の型（I{Entity}Repository・IRepository・SqlQuery 等）を using する。
            foreach (var dialect in dialects)
            {
                specs.Add(
                    BuildDialectRepositorySpec(
                        options,
                        options.OutputFileName,
                        repositoryNamespace,
                        dialect,
                        root
                    )
                );
            }

            AddRemoteServerSpec(specs, options, active, primaryDialect);

            return specs;
        }

        // 分割生成は「固定 infra は Runtime 系ファイル・スキーマ依存物は Repositories 系ほか」の対称構成へ分ける。
        // Runtime 系（Runtime.g.cs / Runtime.{方言}.g.cs / Runtime.EntityFrameworkCore.g.cs / Runtime.InMemory.g.cs）は
        // 配布パッケージ QuickER.Runtime* と 1:1 対応し、Repositories 系は per-entity・DI 登録・DbContext だけになる
        // （＝パッケージ参照モードの on/off で内容が変わらない）。
        // パッケージ参照モードでは固定 infra をパッケージが持つため、Runtime 系ファイルを計画から落とすだけでよい
        // （他バケットの Runtime 向けクロス using は activeSet から外れて自然に落ち、代わりに
        //   GeneratedFileUsings が固定名前空間 using を付ける）。
        var emittedBuckets = options.UseRuntimePackages
            ? active.Where(bucket => bucket != GenerationBucket.Runtime).ToList()
            : active;

        var namespaceByBucket = emittedBuckets.ToDictionary(
            bucket => bucket,
            bucket => ResolveNamespace(options, bucket)
        );

        // 通常分割: EF Core / インメモリ実装は方言別実装（{RepositoryNamespace}.SqlServer 等）と同じ扱いで、契約（Repository）
        // namespace のサブ名前空間 {RepositoryNamespace}.{接尾辞} へ導出する（専用の名前空間オプションは持たない）。
        // これらのバケットが有効なら Repository バケットも必ず有効（ActiveBuckets が保証）。
        // 層別出力: 名前空間はフォルダ追従＝ResolveNamespace が {インフラ層ルート}.{接尾辞} を返しており上書きしない
        // （契約名前空間の下へインフラ実装がぶら下がる従来のねじれを解消する）。
        if (!options.LayeredOutput)
        {
            foreach (var bucket in DerivedRepositorySubBuckets)
            {
                if (namespaceByBucket.ContainsKey(bucket))
                {
                    namespaceByBucket[bucket] =
                        $"{namespaceByBucket[GenerationBucket.Repository]}.{DefaultSuffix(bucket)}";
                }
            }

            // HTTP クライアントはファイルだけを分け、名前空間は契約と同じ {RepositoryNamespace} に据え置く
            // （サブ名前空間にすると型 FQN と非分割出力まで変わるため。EfCore 等の導出サブバケットとは意図的に別規則。
            //   層別出力では {インフラ層ルート}.Http＝フォルダ追従の一環として移る）。
            if (namespaceByBucket.ContainsKey(GenerationBucket.Http))
            {
                namespaceByBucket[GenerationBucket.Http] = namespaceByBucket[
                    GenerationBucket.Repository
                ];
            }
        }

        var activeSet = emittedBuckets.ToHashSet();

        // 固定 infra ファイル（Runtime 系）の基底名前空間。パッケージ参照モードでは Runtime バケット自体を
        // 出さないため null で、スキーマ依存物は固定名前空間（QuickER.Runtime*）を using する。
        var runtimeNamespace = options.UseRuntimePackages
            ? null
            : namespaceByBucket[GenerationBucket.Runtime];

        var splitSpecs = new List<GeneratedFileSpec>();

        foreach (var bucket in emittedBuckets)
        {
            // 共有基盤（Runtime バケット）は固定 infra 専用ファイル群としてループの後でまとめて計画する
            // （契約・方言エンジン・EF Core・インメモリの固定部と同じ場所で扱うため）
            if (bucket == GenerationBucket.Runtime)
            {
                continue;
            }

            var ownNamespace = namespaceByBucket[bucket];
            // 依存グラフから「実際に参照する他バケット」の名前空間だけを using する
            // （全バケットを無差別に using すると、参照しない名前空間まで開くことになる）。
            // 有効でない依存先（例: VO 無効時の ValueObject）は自然に除外される。また依存先が
            // 自分と同一名前空間へ解決される場合は自分自身の using になるため除外する。
            var dependencyNamespaces = BucketDependencies(bucket)
                .Where(dependency => activeSet.Contains(dependency))
                .Select(dependency => namespaceByBucket[dependency])
                .ToList();

            // 固定 infra は Runtime 系ファイルへ分かれたため、実装バケット（EF Core・インメモリ）は
            // 対応する Runtime サブ名前空間も using する（パッケージ参照モードでは PackageRuntimeUsings が
            // 固定名前空間を付けるため runtimeNamespace は null＝ここでは何も足さない）。
            // 層別出力では固定部ファイルが per-entity と同じ層サブ名前空間（{インフラ}.{接尾辞}）へ統合される
            // ＝自分自身の名前空間なので足さない。
            if (
                !options.LayeredOutput
                && runtimeNamespace is not null
                && FixedRuntimeSuffix(bucket) is { } fixedSuffix
            )
            {
                dependencyNamespaces.Add($"{runtimeNamespace}.{fixedSuffix}");
            }

            var crossUsings = OrderCrossUsings(dependencyNamespaces, ownNamespace);

            // 分割時の Repository バケットは契約のみを自 namespace へ出し、方言別実装は別ファイルへ分ける
            // （単一方言でも同レイアウト）。インメモリ実装は独立バケットとして別ファイルへ分かれる。
            if (repositorySplitLayout && bucket == GenerationBucket.Repository)
            {
                splitSpecs.Add(
                    new GeneratedFileSpec
                    {
                        FileName = DefaultFileName(bucket),
                        NamespaceName = ownNamespace,
                        Buckets = [bucket],
                        CrossNamespaceUsings = crossUsings,
                        Dialect = primaryDialect,
                        ContractOnly = true,
                        MultiDialect = true,
                        // 固定 infra（契約本体）は Runtime.g.cs が持つ。ここは per-entity 契約・AddSaveHook・
                        // HTTP クライアント・射影 DTO などのスキーマ依存物だけ。
                        EmitSharedInfra = false,
                    }
                );

                // 方言別実装ファイル（Repositories.SqlServer.g.cs 等）。契約 namespace（＝Repository 自身の namespace）
                // と、方言実装が参照する他バケット（Entity 等）を using する。
                // 層別出力では方言 namespace の基底を契約でなくインフラ層ルートにする（{インフラ}.SqlServer 等＝
                // Runtime.{方言}.g.cs の固定部と同一 namespace へ統合されるため fixedRuntimeNamespace の追加 using も不要）。
                foreach (var dialect in dialects)
                {
                    splitSpecs.Add(
                        BuildDialectRepositorySpec(
                            options,
                            DialectRepositoryFileName(dialect),
                            options.LayeredOutput
                                ? LayerNamespaceRoot(options, GeneratedLayer.Infrastructure)
                                : ownNamespace,
                            dialect,
                            contractNamespace: ownNamespace,
                            extraCrossUsings: crossUsings,
                            // 方言エンジンの固定 infra は Runtime.{方言}.g.cs が持つ（ここは per-entity 実装＋DI のみ）
                            emitSharedInfra: false,
                            fixedRuntimeNamespace: runtimeNamespace is null || options.LayeredOutput
                                ? null
                                : $"{runtimeNamespace}.{DialectNamespaceSuffix(dialect)}"
                        )
                    );
                }

                continue;
            }

            splitSpecs.Add(
                new GeneratedFileSpec
                {
                    // EF Core 実装は Repositories.EntityFrameworkCore.g.cs、インメモリ実装は Repositories.InMemory.g.cs、
                    // HTTP クライアントは Repositories.Http.g.cs へ出す（いずれも方言別実装 Repositories.SqlServer.g.cs 等と同じ流儀。
                    // Http は名前空間だけ契約と同一のため DerivedRepositorySubBuckets には含めず、ファイル名のみ同規則を使う）
                    FileName =
                        bucket == GenerationBucket.Http
                        || DerivedRepositorySubBuckets.Contains(bucket)
                            ? DerivedRepositoryFileName(bucket)
                            : DefaultFileName(bucket),
                    NamespaceName = ownNamespace,
                    Buckets = [bucket],
                    CrossNamespaceUsings = crossUsings,
                    Dialect = primaryDialect,
                    ContractOnly = false,
                    MultiDialect = false,
                    // 固定 infra は Runtime 系ファイルが持つ（Entity/EditModel/Mapper/VO のように固定 infra を
                    // そもそも描画しないバケットでも、意味を揃えて false で統一する）
                    EmitSharedInfra = false,
                }
            );
        }

        // 固定 infra ファイル（Runtime 系）。パッケージ参照モードでは 1 本も作らない。
        if (runtimeNamespace is not null)
        {
            AddFixedRuntimeSpecs(
                splitSpecs,
                options,
                runtimeNamespace,
                dialects,
                activeSet,
                primaryDialect
            );
        }

        AddRemoteServerSpec(splitSpecs, options, active, primaryDialect);

        // 層別出力ならバケット→層の固定対応で各スペックへ層フォルダを付与する（内容・名前・順序は不変）
        return ApplyLayerDirectories(splitSpecs, options);
    }

    /// <summary>クロス using を「自分自身を除外・重複排除・序数昇順」で整える（分割時の唯一の正）</summary>
    private static List<string> OrderCrossUsings(
        IEnumerable<string> namespaces,
        string ownNamespace
    ) =>
        namespaces
            .Where(ns => !string.Equals(ns, ownNamespace, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// そのバケットのスキーマ依存物が参照する固定 infra ファイルの Runtime サブ名前空間サフィックスを返す
    /// （対応する固定部ファイルが無いバケットは null）。
    /// </summary>
    /// <remarks>
    /// 方言別実装（Repository バケット）は方言ごとにサフィックスが変わるため、ここではなく
    /// <see cref="BuildDialectRepositorySpec"/> の引数で受け渡す。
    /// </remarks>
    private static string? FixedRuntimeSuffix(GenerationBucket bucket) =>
        bucket switch
        {
            GenerationBucket.EfCore => DefaultSuffix(GenerationBucket.EfCore),
            GenerationBucket.InMemory => DefaultSuffix(GenerationBucket.InMemory),
            GenerationBucket.Sync => DefaultSuffix(GenerationBucket.Sync),
            _ => null,
        };

    /// <summary>
    /// サーバー実装の固定部ファイル・名前空間のサフィックス（配布パッケージ <see cref="RuntimePackages.AspNetCore"/> と同名規則）。
    /// </summary>
    /// <remarks>
    /// RemoteServer バケットは <see cref="ActiveBuckets"/> に載らず（サーバー実装は常に専用スペック）、
    /// <see cref="FixedRuntimeSuffix"/> のバケットループを通らないため、<see cref="AddRemoteServerSpec"/> が直接使う。
    /// スキーマ依存側のファイル名・名前空間サフィックスは <c>RemoteServer</c> で、両者は別軸。
    /// </remarks>
    private const string AspNetCoreSuffix = "AspNetCore";

    /// <summary>
    /// 分割時の固定 infra ファイル（<c>Runtime*.g.cs</c>）のスペックを、配布パッケージと 1:1 対応する構成で追加する。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>Runtime.g.cs</c>（<c>{Runtime}</c>）: 共有基盤＋方言中立の契約＝<see cref="RuntimePackages.Core"/> 相当</item>
    ///   <item><c>Runtime.SqlServer.g.cs</c> / <c>Runtime.Sqlite.g.cs</c>（<c>{Runtime}.{方言}</c>）: 方言エンジン</item>
    ///   <item><c>Runtime.EntityFrameworkCore.g.cs</c>（<c>{Runtime}.EntityFrameworkCore</c>）: EF Core 共通部品</item>
    ///   <item><c>Runtime.InMemory.g.cs</c>（<c>{Runtime}.InMemory</c>）: インメモリ基盤</item>
    /// </list>
    /// いずれも <see cref="GeneratedFileSpec.EmitSchemaDependent"/> が false で、per-entity・DI 登録・DbContext は含まない。
    /// 方言エンジン・EF Core・インメモリの各ファイルは共通契約をコア相当のファイルから using で参照する
    /// （パッケージが <c>using QuickER.Runtime;</c> でコアを参照するのと同じ構造）。
    /// </remarks>
    private static void AddFixedRuntimeSpecs(
        List<GeneratedFileSpec> specs,
        CodeGenerationOptions options,
        string runtimeNamespace,
        IReadOnlyList<string> dialects,
        IReadOnlySet<GenerationBucket> activeSet,
        string primaryDialect
    )
    {
        var repositoryActive = activeSet.Contains(GenerationBucket.Repository);

        // 共有基盤（EntityBase・属性・VO 基底・JSON コンバータ）＋方言中立の Repository 共通契約。
        // 契約は Repository バケットが有効なときだけ載る（Entity 単独生成では共有基盤のみ）。
        specs.Add(
            new GeneratedFileSpec
            {
                FileName = DefaultFileName(GenerationBucket.Runtime),
                NamespaceName = runtimeNamespace,
                Buckets = repositoryActive
                    ? [GenerationBucket.Runtime, GenerationBucket.Repository]
                    : [GenerationBucket.Runtime],
                CrossNamespaceUsings = [],
                Dialect = primaryDialect,
                // 契約のみ（方言エンジンは方言別の固定部ファイルが担う）
                ContractOnly = true,
                MultiDialect = true,
                EmitSchemaDependent = false,
            }
        );

        // 方言エンジンの固定部（方言 Repository 基底・式木翻訳・実行器・接続ファクトリ・方言別メタデータ）。
        // 層別出力では per-entity 実装（Repositories.{方言}.g.cs）と同じ {インフラ層ルート}.{方言} へ統合する
        // （型衝突なし＝両ファイルは元から対で参照し合う。フォルダと名前空間を揃える追従の一環）。
        if (options.GenerateRepositories && repositoryActive)
        {
            foreach (var dialect in dialects)
            {
                var suffix = DialectNamespaceSuffix(dialect);
                var fixedNamespaceBase = options.LayeredOutput
                    ? LayerNamespaceRoot(options, GeneratedLayer.Infrastructure)
                    : runtimeNamespace;
                specs.Add(
                    new GeneratedFileSpec
                    {
                        FileName = FixedRuntimeFileName(suffix),
                        NamespaceName = $"{fixedNamespaceBase}.{suffix}",
                        Buckets = [GenerationBucket.Repository],
                        CrossNamespaceUsings = [runtimeNamespace],
                        Dialect = dialect,
                        ContractOnly = false,
                        MultiDialect = true,
                        EmitSchemaDependent = false,
                    }
                );
            }
        }

        // EF Core 共通部品の固定部（EF Core 版 Repository 基底・VO 翻訳プラグイン・SaveConflict 変換・共通メタデータ）
        if (activeSet.Contains(GenerationBucket.EfCore))
        {
            AddFixedRuntimeSubSpec(
                specs,
                options,
                runtimeNamespace,
                GenerationBucket.EfCore,
                primaryDialect
            );
        }

        // インメモリ基盤の固定部（InMemoryDataStore・InMemoryRepository 基底・ステージング・共通メタデータ）
        if (activeSet.Contains(GenerationBucket.InMemory))
        {
            AddFixedRuntimeSubSpec(
                specs,
                options,
                runtimeNamespace,
                GenerationBucket.InMemory,
                primaryDialect
            );
        }

        // 同期エンジンの固定部（SyncEngine・ジャーナル・セッション抑制・結果／競合レコード）
        if (activeSet.Contains(GenerationBucket.Sync))
        {
            AddFixedRuntimeSubSpec(
                specs,
                options,
                runtimeNamespace,
                GenerationBucket.Sync,
                primaryDialect
            );
        }
    }

    /// <summary>方言を持たない固定部サブファイル（EF Core / インメモリ / 同期）のスペックを追加する</summary>
    /// <remarks>
    /// 名前空間は通常分割で <c>{Runtime}.{接尾辞}</c>、層別出力では per-entity 側と同じ
    /// <c>{インフラ層ルート}.{接尾辞}</c>（＝<see cref="ResolveNamespace"/> のフォールバックと一致）へ統合する。
    /// </remarks>
    private static void AddFixedRuntimeSubSpec(
        List<GeneratedFileSpec> specs,
        CodeGenerationOptions options,
        string runtimeNamespace,
        GenerationBucket bucket,
        string primaryDialect
    )
    {
        var suffix = DefaultSuffix(bucket);
        specs.Add(
            new GeneratedFileSpec
            {
                FileName = FixedRuntimeFileName(suffix),
                NamespaceName = options.LayeredOutput
                    ? ResolveNamespace(options, bucket)
                    : $"{runtimeNamespace}.{suffix}",
                Buckets = [bucket],
                CrossNamespaceUsings = [runtimeNamespace],
                Dialect = primaryDialect,
                ContractOnly = false,
                MultiDialect = false,
                EmitSchemaDependent = false,
            }
        );
    }

    /// <summary>固定 infra ファイルの分割ファイル名（例: <c>Runtime.SqlServer.g.cs</c>）</summary>
    private static string FixedRuntimeFileName(string suffix) =>
        $"{DefaultSuffix(GenerationBucket.Runtime)}.{suffix}.g.cs";

    /// <summary>生成 C# ファイルの拡張子サフィックス（<c>.g.cs</c>）</summary>
    internal const string GeneratedCSharpSuffix = ".g.cs";

    /// <summary>末尾の <c>.g.cs</c>（大文字小文字無視）を取り除いたベース名を返す（付いていなければそのまま返す）</summary>
    internal static string StripGeneratedCSharpSuffix(string fileName) =>
        fileName.EndsWith(GeneratedCSharpSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^GeneratedCSharpSuffix.Length]
            : fileName;

    /// <summary>非分割時のサーバー実装ファイル名（例: <c>MyApp.g.cs</c> → <c>MyApp.RemoteServer.g.cs</c>）</summary>
    public static string RemoteServerFileName(string outputFileName) =>
        $"{StripGeneratedCSharpSuffix(outputFileName)}.RemoteServer{GeneratedCSharpSuffix}";

    /// <summary>
    /// リモートサービス生成（<see cref="CodeGenerationOptions.GenerateRemoteServices"/>）時に、サーバー実装の
    /// スペック（常に別ファイル）を計画へ追加する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// サーバー実装は ASP.NET Core（FrameworkReference）を要するため、非分割でも本体ファイルへは連結しない。
    /// Repository バケット（＝リモート面の契約）が有効でない構成では何も追加しない（契約が無ければ実装先が無い）。
    /// 挿入位置は「Repository バケットを含む最後のスペックの直後」＝リモート面の契約・実装の隣に並べる
    /// （プレビュー・出力順で Repositories の下に RemoteServer が来る。非分割は本体 1 ファイルの後ろ＝末尾）。
    /// </para>
    /// <para>
    /// 分割時は他バケットと同じ対称構成で、固定部（<c>RemoteServerEngine</c> ほか）を <c>Runtime.AspNetCore.g.cs</c>
    /// （<c>{Runtime}.AspNetCore</c>＝配布パッケージ <see cref="RuntimePackages.AspNetCore"/> 相当）へ切り出し、
    /// <c>RemoteServer.g.cs</c> は per-entity のエンドポイントだけへ純化する。パッケージ参照モードでは固定部ファイルを
    /// 計画せず（パッケージが持つ）、非分割は 1 ファイルへ同居する（<c>EmitSharedInfra</c> の既定 true ＋
    /// <c>CSharpCodeGenerationService</c> 側の <c>!UseRuntimePackages</c> の AND が効く）。
    /// </para>
    /// </remarks>
    private static void AddRemoteServerSpec(
        List<GeneratedFileSpec> specs,
        CodeGenerationOptions options,
        IReadOnlyList<GenerationBucket> active,
        string primaryDialect
    )
    {
        if (!options.GenerateRemoteServices || !active.Contains(GenerationBucket.Repository))
        {
            return;
        }

        if (!options.EffectiveSplitFilesByCategory)
        {
            // 非分割: 本体と同じルート namespace（同一プロジェクト内なら using 不要）で別ファイルへ出す
            InsertAfterRepositorySpecs(
                specs,
                new GeneratedFileSpec
                {
                    FileName = RemoteServerFileName(options.OutputFileName),
                    NamespaceName = ResolveRootNamespace(options),
                    Buckets = [GenerationBucket.RemoteServer],
                    CrossNamespaceUsings = [],
                    Dialect = primaryDialect,
                    ContractOnly = false,
                    MultiDialect = false,
                }
            );

            return;
        }

        // 分割: 専用 namespace（{root}.RemoteServer）へ出し、依存グラフから他バケットの namespace を using する。
        // パッケージ参照モードでは Runtime バケットのファイルが無いため using から自然に落ちる
        // （共有基盤の型は GeneratedFileUsings が付ける QuickER.Runtime で解決される）。
        var activeSet = (
            options.UseRuntimePackages
                ? active.Where(bucket => bucket != GenerationBucket.Runtime)
                : active
        ).ToHashSet();
        var ownNamespace = ResolveNamespace(options, GenerationBucket.RemoteServer);
        var dependencyNamespaces = BucketDependencies(GenerationBucket.RemoteServer)
            .Where(dependency => activeSet.Contains(dependency))
            .Select(dependency => ResolveNamespace(options, dependency))
            .ToList();

        // 固定部ファイル（Runtime.AspNetCore.g.cs）は他の Runtime 系と同じく、パッケージ参照モードでは計画しない
        // （そのときは GeneratedFileUsings が固定名前空間 QuickER.Runtime.AspNetCore の using を付ける）
        if (!options.UseRuntimePackages)
        {
            var runtimeNamespace = ResolveNamespace(options, GenerationBucket.Runtime);
            // 層別出力ではサーバー固定部も per-entity と同じサーバー層のフォルダへ入るため、
            // 名前空間の基底をサーバー層ルートにする（{サーバー}.AspNetCore＝フォルダ追従）
            var aspNetCoreNamespace = options.LayeredOutput
                ? $"{LayerNamespaceRoot(options, GeneratedLayer.Server)}.{AspNetCoreSuffix}"
                : $"{runtimeNamespace}.{AspNetCoreSuffix}";

            // 固定部は共通契約（RemoteJson・エンベロープ・SaveConflictException 等）をコア相当のファイルから
            // using で参照する（パッケージが using QuickER.Runtime; するのと同じ構造）
            specs.Add(
                new GeneratedFileSpec
                {
                    FileName = FixedRuntimeFileName(AspNetCoreSuffix),
                    NamespaceName = aspNetCoreNamespace,
                    Buckets = [GenerationBucket.RemoteServer],
                    CrossNamespaceUsings = [runtimeNamespace],
                    Dialect = primaryDialect,
                    ContractOnly = false,
                    MultiDialect = false,
                    EmitSchemaDependent = false,
                }
            );

            // per-entity 側は固定部の namespace も using する（方言別実装が Runtime.{方言} を using するのと同型）
            dependencyNamespaces.Add(aspNetCoreNamespace);

            // 同期エンドポイントは同期の固定部（ISyncServerSource・同期エンベロープ・操作名）を参照する。
            // per-entity の同期生成物（記述子・デコレータ・HTTP 差分ソース）は参照しないため、Sync バケット
            // そのものではなく固定部の namespace だけを足す（層別出力では固定部が {インフラ}.Sync へ統合
            // されているため ResolveNamespace のフォールバックがそのまま固定部の namespace になる）。
            if (activeSet.Contains(GenerationBucket.Sync))
            {
                dependencyNamespaces.Add(
                    options.LayeredOutput
                        ? ResolveNamespace(options, GenerationBucket.Sync)
                        : $"{runtimeNamespace}.{DefaultSuffix(GenerationBucket.Sync)}"
                );
            }
        }

        InsertAfterRepositorySpecs(
            specs,
            new GeneratedFileSpec
            {
                FileName = DefaultFileName(GenerationBucket.RemoteServer),
                NamespaceName = ownNamespace,
                Buckets = [GenerationBucket.RemoteServer],
                CrossNamespaceUsings = OrderCrossUsings(dependencyNamespaces, ownNamespace),
                Dialect = primaryDialect,
                ContractOnly = false,
                MultiDialect = false,
                // サーバー実装はスキーマ依存物のみ（固定 infra は Runtime 系ファイルが持つ）
                EmitSharedInfra = false,
            }
        );
    }

    /// <summary>
    /// サーバー実装スペックを「Repository バケットを含むスキーマ依存ファイルの最後の直後」へ挿入する
    /// （分割時に Repositories（方言別実装含む）の下・EfCore / Runtime 系より前へ並べるため）。
    /// </summary>
    /// <remarks>
    /// 固定 infra ファイル（<c>Runtime.g.cs</c> / <c>Runtime.{方言}.g.cs</c>）も Repository バケットを含むため、
    /// スキーマ依存（<see cref="GeneratedFileSpec.EmitSchemaDependent"/>）であることを条件に加えて
    /// リモート面の契約・実装の隣という位置づけを保つ。HTTP クライアントファイル（Http バケット）も
    /// リモート面の実装のため同じ並びに含める（分割時は Repositories.Http.g.cs の直後に RemoteServer.g.cs が来る）。
    /// </remarks>
    private static void InsertAfterRepositorySpecs(
        List<GeneratedFileSpec> specs,
        GeneratedFileSpec remoteServerSpec
    )
    {
        var lastRepositoryIndex = specs.FindLastIndex(spec =>
            (
                spec.Buckets.Contains(GenerationBucket.Repository)
                || spec.Buckets.Contains(GenerationBucket.Http)
            ) && spec.EmitSchemaDependent
        );

        // 呼び出し元で Repository バケットの有効性を確認済みだが、万一見つからない場合は末尾へ退避する
        var insertIndex = lastRepositoryIndex < 0 ? specs.Count : lastRepositoryIndex + 1;
        specs.Insert(insertIndex, remoteServerSpec);
    }

    /// <summary>方言別実装スペックを組み立てる（Repository バケットのみ・{RepositoryNamespace}.Suffix・契約 namespace を using）</summary>
    /// <param name="emitSharedInfra">
    /// 方言エンジンの固定 infra を同じファイルへ描画するか。非分割（1 ファイルへ全部入る）は true、
    /// 分割は false（固定 infra は <c>Runtime.{方言}.g.cs</c> が持つ）。
    /// </param>
    /// <param name="fixedRuntimeNamespace">
    /// 分割時に方言エンジンの固定 infra が居る名前空間（<c>{Runtime}.{方言}</c>）。null なら追加 using なし
    /// （非分割・パッケージ参照モード）。
    /// </param>
    private static GeneratedFileSpec BuildDialectRepositorySpec(
        CodeGenerationOptions options,
        string fileName,
        string repositoryNamespace,
        string dialect,
        string contractNamespace,
        IReadOnlyList<string>? extraCrossUsings = null,
        bool emitSharedInfra = true,
        string? fixedRuntimeNamespace = null
    )
    {
        var dialectNamespace = $"{repositoryNamespace}.{DialectNamespaceSuffix(dialect)}";

        // 方言実装は契約 namespace（I{Entity}Repository / IRepository / SqlQuery / SqlQueryPlan / CascadeNavigation 等）を using する。
        // 分割時は Entity 等の他バケット namespace も引き継ぐ（extraCrossUsings）。自 namespace は using しない。
        var crossUsings = new List<string> { contractNamespace };

        if (extraCrossUsings is not null)
        {
            crossUsings.AddRange(extraCrossUsings);
        }

        // 分割時は方言エンジン（SqlServerRepository / ISqlConnectionFactory 等）が別ファイルへ分かれるため、その namespace も using する
        if (fixedRuntimeNamespace is not null)
        {
            crossUsings.Add(fixedRuntimeNamespace);
        }

        return new GeneratedFileSpec
        {
            FileName = fileName,
            NamespaceName = dialectNamespace,
            Buckets = [GenerationBucket.Repository],
            CrossNamespaceUsings = OrderCrossUsings(crossUsings, dialectNamespace),
            Dialect = dialect,
            ContractOnly = false,
            MultiDialect = true,
            EmitSharedInfra = emitSharedInfra,
        };
    }

    /// <summary>方言別実装の分割ファイル名（例: <c>Repositories.SqlServer.g.cs</c>）</summary>
    private static string DialectRepositoryFileName(string dialect) =>
        $"{DefaultSuffix(GenerationBucket.Repository)}.{DialectNamespaceSuffix(dialect)}.g.cs";

    /// <summary>
    /// 分割時、契約（Repository）namespace のサブ名前空間・サブファイルへ導出するバケット（方言実装と同じ流儀の後付け特例）。
    /// </summary>
    /// <remarks>
    /// これらは専用の名前空間オプションを持たず、namespace は <c>{RepositoryNamespace}.{接尾辞}</c>、
    /// ファイル名は <c>Repositories.{接尾辞}.g.cs</c> へ一律に導出する（方言別実装 <c>Repositories.SqlServer.g.cs</c> 等と同型）。
    /// </remarks>
    private static readonly GenerationBucket[] DerivedRepositorySubBuckets =
    [
        GenerationBucket.EfCore,
        GenerationBucket.InMemory,
        GenerationBucket.Sync,
    ];

    /// <summary>導出サブバケットの分割ファイル名（例: <c>Repositories.EntityFrameworkCore.g.cs</c>）＝方言別実装と同じ流儀</summary>
    private static string DerivedRepositoryFileName(GenerationBucket bucket) =>
        $"{DefaultSuffix(GenerationBucket.Repository)}.{DefaultSuffix(bucket)}.g.cs";

    /// <summary>層の既定フォルダ名（未指定・空白時のフォールバック。GUI のプリフィルとも一致させる）</summary>
    public static string DefaultLayerDirectory(GeneratedLayer layer) =>
        layer switch
        {
            GeneratedLayer.Domain => "Domain",
            GeneratedLayer.Presentation => "Presentation",
            GeneratedLayer.Infrastructure => "Infrastructure",
            GeneratedLayer.Server => "Server",
            _ => "Generated",
        };

    /// <summary>層のフォルダパスを解決する（明示指定が空白なら既定フォルダ名へフォールバック）</summary>
    public static string ResolveLayerDirectory(CodeGenerationOptions options, GeneratedLayer layer)
    {
        var explicitValue = layer switch
        {
            GeneratedLayer.Domain => options.DomainLayerDirectory,
            GeneratedLayer.Presentation => options.PresentationLayerDirectory,
            GeneratedLayer.Infrastructure => options.InfrastructureLayerDirectory,
            GeneratedLayer.Server => options.ServerLayerDirectory,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(explicitValue)
            ? DefaultLayerDirectory(layer)
            : explicitValue.Trim();
    }

    /// <summary>
    /// スペックの振り分け先レイヤを「含有バケット＋契約フラグ」だけから導出する（分割時の全スペックが一意に分類できる）。
    /// </summary>
    /// <remarks>
    /// 判定順が意味を持つのは Repository バケットだけ:
    /// 方言実装スペック（Repositories.{方言}.g.cs / Runtime.{方言}.g.cs＝<see cref="GeneratedFileSpec.MultiDialect"/> が
    /// true かつ契約のみでない）だけがインフラ層で、契約スペック（Repositories.g.cs / Runtime.g.cs）はドメイン層。
    /// 判別に <see cref="GeneratedFileSpec.ContractOnly"/> 単独を使えないのは、QuickER 版 Repository を生成しない構成
    /// （EF Core / インメモリ単独）では契約ファイルが方言実装レイアウトを通らず ContractOnly=false のまま出るため。
    /// それ以外のバケットは層が 1 対 1 に決まる（RemoteServer バケットは per-entity・固定部ともサーバー層＝
    /// ASP.NET Core の FrameworkReference を要するため通常のクラスライブラリへ置けない）。
    /// </remarks>
    public static GeneratedLayer LayerOf(GeneratedFileSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Buckets.Contains(GenerationBucket.RemoteServer))
        {
            return GeneratedLayer.Server;
        }

        if (
            spec.Buckets.Contains(GenerationBucket.EditModel)
            || spec.Buckets.Contains(GenerationBucket.Mapper)
        )
        {
            return GeneratedLayer.Presentation;
        }

        if (
            spec.Buckets.Contains(GenerationBucket.EfCore)
            || spec.Buckets.Contains(GenerationBucket.InMemory)
            || spec.Buckets.Contains(GenerationBucket.Sync)
            || spec.Buckets.Contains(GenerationBucket.Http)
        )
        {
            return GeneratedLayer.Infrastructure;
        }

        if (
            spec.Buckets.Contains(GenerationBucket.Repository)
            && !spec.ContractOnly
            && spec.MultiDialect
        )
        {
            return GeneratedLayer.Infrastructure;
        }

        // Entity / ValueObject / Runtime コア / Repository 契約はドメイン層
        return GeneratedLayer.Domain;
    }

    /// <summary>
    /// 層別出力時、計画済みスペックへ層フォルダ（<see cref="GeneratedFileSpec.RelativeDirectory"/>）を付与する。
    /// </summary>
    /// <remarks>
    /// 層別出力でなければ何もしない（RelativeDirectory は null のまま＝出力ディレクトリ直下）。
    /// ファイル名・名前空間・バケット構成には触れない＝本メソッドが変えるのは配置だけ。
    /// パスの妥当性検証（絶対パス・<c>..</c> の拒否）は生成本体の診断が担い、ここでは値を素通しする
    /// （Plan はプレビューでも呼ばれるため例外を投げない）。
    /// </remarks>
    private static IReadOnlyList<GeneratedFileSpec> ApplyLayerDirectories(
        List<GeneratedFileSpec> specs,
        CodeGenerationOptions options
    )
    {
        if (!options.LayeredOutput)
        {
            return specs;
        }

        return specs
            .Select(spec =>
                spec with
                {
                    RelativeDirectory = ResolveLayerDirectory(options, LayerOf(spec)),
                }
            )
            .ToList();
    }
}
