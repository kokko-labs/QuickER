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
}

/// <summary>1 つの生成ファイルが「どの名前空間で・どのバケットを含み・どの名前空間を using するか」を表す計画</summary>
public sealed class GeneratedFileSpec
{
    /// <summary>出力ファイル名</summary>
    public required string FileName { get; init; }

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
    /// 方言実装は別の方言実装スペックが担う。単一方言時は常に false（契約＋実装を同一スコープで従来どおり出力する）。
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

    /// <summary>指定バケットの名前空間を解決する（個別指定が空なら <c>{root}.{サフィックス}</c> へフォールバック）</summary>
    /// <remarks>
    /// この解決は分割時のみ使用する。フォールバックは UI のプリフィル（<see cref="DefaultSuffix"/>）と一致させ、
    /// 規約を 1 箇所に集約する（例 <c>{root}.Entities</c>、Runtime は <c>{root}.Runtime</c>）
    /// </remarks>
    public static string ResolveNamespace(CodeGenerationOptions options, GenerationBucket bucket)
    {
        var root = ResolveRootNamespace(options);
        var explicitValue = bucket switch
        {
            GenerationBucket.Runtime => options.RuntimeNamespace,
            GenerationBucket.ValueObject => options.ValueObjectNamespace,
            GenerationBucket.Entity => options.EntityNamespace,
            GenerationBucket.EditModel => options.EditModelNamespace,
            GenerationBucket.Mapper => options.MapperNamespace,
            GenerationBucket.Repository => options.RepositoryNamespace,
            // EfCore に個別の名前空間オプションは設けない（分割時は {RepositoryNamespace}.EntityFrameworkCore へ導出専用。
            // 方言別実装 {RepositoryNamespace}.SqlServer 等と同じ扱い）
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue.Trim();
        }

        return $"{root}.{DefaultSuffix(bucket)}";
    }

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

        // Entity を常に生成する＝何らかのクラスが必ず出力されるため、共有基盤（Runtime）は常に必要
        active.Add(GenerationBucket.Runtime);

        return active;
    }

    /// <summary>
    /// 計画で各スペックへ載せる方言を、例外を投げずに解決する。
    /// </summary>
    /// <remarks>
    /// 実効方言（<see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/>）は未対応方言で例外を投げるが、
    /// Plan はプレビューなどでも呼ばれ、Repository 非生成時には図の方言名（例 mysql）が <see cref="CodeGenerationOptions.RepositoryDialects"/> に
    /// 残ることがある。ここで例外にすると生成しない構成のプレビューまで壊れるため、非例外で単一方言（先頭）を採り、
    /// 未対応方言は <c>sqlserver</c> 相当へフォールバックさせる（実効方言の検証・診断は生成本体が担う）。
    /// </remarks>
    private static IReadOnlyList<string> ResolvePlanningDialects(CodeGenerationOptions options)
    {
        try
        {
            return options.EffectiveRepositoryDialects;
        }
        catch (ArgumentException)
        {
            return ["sqlserver"];
        }
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
    /// 実効方言（<see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/>）が 1 つのときは現行プランを完全維持する
    /// （出力バイト不変）。2 つ以上のときは Repository バケットを「中立契約（1 回）」と「方言別実装（方言ごと）」に分割し、
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
    /// </remarks>
    public static IReadOnlyList<GeneratedFileSpec> Plan(CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var active = ActiveBuckets(options);
        // 実効方言の先頭を各スペックへ持たせる（単一方言＝現行値。方言リテラル参照をスコープ由来に一本化する）。
        // 型解決・診断・[SqlColumnType] 補完はマルチ辞書として M1 で機能する。
        // Plan はプレビュー等でも呼ばれ、未対応方言指定（Repository 非生成時に残る図の方言名）でも例外にしてはならない。
        // 実効方言の検証・診断は生成本体（CSharpCodeGenerationService.Generate）が担うため、ここでは非例外で先頭方言を採る
        // （未対応値は RepositoryDialectVariables 側で sqlserver 相当へフォールバックする）。
        var dialects = ResolvePlanningDialects(options);
        var primaryDialect = dialects[0];

        var repositoryActive = active.Contains(GenerationBucket.Repository);

        // 非分割: マルチ方言（実効方言 2 つ以上）で Repository を生成するときだけ、契約 1 回＋方言別 namespace 実装へ
        // 展開する。単一方言・Repository 非生成時は従来どおり全バケットを 1 ファイル・1 namespace へまとめる（バイト不変）。
        var repositoryMultiDialectInlineLayout =
            options.GenerateRepositories && dialects.Count >= 2 && repositoryActive;

        // 分割: 単一方言でも「契約 1 回＋方言別実装ファイル」レイアウトへ統一する（実効方言が 1 つでも
        // マルチターゲットと同じ形＝Repositories.g.cs＋Repositories.{方言}.g.cs）。Repository を生成するときのみ。
        var repositorySplitLayout = options.GenerateRepositories && repositoryActive;

        if (!options.SplitFilesByCategory)
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

            // 契約＋非 Repository バケット（Entity/EditModel/Mapper/VO/Runtime）は従来どおりルート namespace の
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

        // EF Core / インメモリ実装は方言別実装（{RepositoryNamespace}.SqlServer 等）と同じ扱いで、契約（Repository）
        // namespace のサブ名前空間 {RepositoryNamespace}.{接尾辞} へ導出する（専用の名前空間オプションは持たない）。
        // これらのバケットが有効なら Repository バケットも必ず有効（ActiveBuckets が保証）。
        foreach (var bucket in DerivedRepositorySubBuckets)
        {
            if (namespaceByBucket.ContainsKey(bucket))
            {
                namespaceByBucket[bucket] =
                    $"{namespaceByBucket[GenerationBucket.Repository]}.{DefaultSuffix(bucket)}";
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
            // （無差別に全バケットを using していた従来動作の不要 using を排除する）。
            // 有効でない依存先（例: VO 無効時の ValueObject）は自然に除外される。また依存先が
            // 自分と同一名前空間へ解決される場合は自分自身の using になるため除外する。
            var dependencyNamespaces = BucketDependencies(bucket)
                .Where(dependency => activeSet.Contains(dependency))
                .Select(dependency => namespaceByBucket[dependency])
                .ToList();

            // 固定 infra は Runtime 系ファイルへ分かれたため、実装バケット（EF Core・インメモリ）は
            // 対応する Runtime サブ名前空間も using する（パッケージ参照モードでは PackageRuntimeUsings が
            // 固定名前空間を付けるため runtimeNamespace は null＝ここでは何も足さない）。
            if (runtimeNamespace is not null && FixedRuntimeSuffix(bucket) is { } fixedSuffix)
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
                foreach (var dialect in dialects)
                {
                    splitSpecs.Add(
                        BuildDialectRepositorySpec(
                            options,
                            DialectRepositoryFileName(dialect),
                            ownNamespace,
                            dialect,
                            contractNamespace: ownNamespace,
                            extraCrossUsings: crossUsings,
                            // 方言エンジンの固定 infra は Runtime.{方言}.g.cs が持つ（ここは per-entity 実装＋DI のみ）
                            emitSharedInfra: false,
                            fixedRuntimeNamespace: runtimeNamespace is null
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
                    // EF Core 実装は Repositories.EntityFrameworkCore.g.cs、インメモリ実装は Repositories.InMemory.g.cs へ出す
                    // （いずれも方言別実装 Repositories.SqlServer.g.cs 等と同じ流儀）
                    FileName = DerivedRepositorySubBuckets.Contains(bucket)
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

        return splitSpecs;
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
            _ => null,
        };

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
        // 契約は Repository バケットが有効なときだけ載る（Entity 単独生成では従来どおり共有基盤のみ）。
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

        // 方言エンジンの固定部（方言 Repository 基底・式木翻訳・実行器・接続ファクトリ・方言別メタデータ）
        if (options.GenerateRepositories && repositoryActive)
        {
            foreach (var dialect in dialects)
            {
                var suffix = DialectNamespaceSuffix(dialect);
                specs.Add(
                    new GeneratedFileSpec
                    {
                        FileName = FixedRuntimeFileName(suffix),
                        NamespaceName = $"{runtimeNamespace}.{suffix}",
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
                runtimeNamespace,
                GenerationBucket.InMemory,
                primaryDialect
            );
        }
    }

    /// <summary>方言を持たない固定部サブファイル（EF Core / インメモリ）のスペックを追加する</summary>
    private static void AddFixedRuntimeSubSpec(
        List<GeneratedFileSpec> specs,
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
                NamespaceName = $"{runtimeNamespace}.{suffix}",
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
    /// サーバー実装は ASP.NET Core（FrameworkReference）を要するため、非分割でも本体ファイルへは連結しない。
    /// Repository バケット（＝リモート面の契約）が有効でない構成では何も追加しない（契約が無ければ実装先が無い）。
    /// 挿入位置は「Repository バケットを含む最後のスペックの直後」＝リモート面の契約・実装の隣に並べる
    /// （プレビュー・出力順で Repositories の下に RemoteServer が来る。非分割は本体 1 ファイルの後ろ＝従来どおり末尾）。
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

        if (!options.SplitFilesByCategory)
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
        var crossUsings = BucketDependencies(GenerationBucket.RemoteServer)
            .Where(dependency => activeSet.Contains(dependency))
            .Select(dependency => ResolveNamespace(options, dependency))
            .Where(ns => !string.Equals(ns, ownNamespace, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        InsertAfterRepositorySpecs(
            specs,
            new GeneratedFileSpec
            {
                FileName = DefaultFileName(GenerationBucket.RemoteServer),
                NamespaceName = ownNamespace,
                Buckets = [GenerationBucket.RemoteServer],
                CrossNamespaceUsings = crossUsings,
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
    /// リモート面の契約・実装の隣という位置づけを保つ。
    /// </remarks>
    private static void InsertAfterRepositorySpecs(
        List<GeneratedFileSpec> specs,
        GeneratedFileSpec remoteServerSpec
    )
    {
        var lastRepositoryIndex = specs.FindLastIndex(spec =>
            spec.Buckets.Contains(GenerationBucket.Repository) && spec.EmitSchemaDependent
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
    ];

    /// <summary>導出サブバケットの分割ファイル名（例: <c>Repositories.EntityFrameworkCore.g.cs</c>）＝方言別実装と同じ流儀</summary>
    private static string DerivedRepositoryFileName(GenerationBucket bucket) =>
        $"{DefaultSuffix(GenerationBucket.Repository)}.{DefaultSuffix(bucket)}.g.cs";
}
