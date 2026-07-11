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
    /// このスペックがレンダリングする自作 Repository の方言（例: <c>"sqlserver"</c> / <c>"sqlite"</c>）。
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
    /// <remarks>false（単一方言）のとき DI は従来の <c>AddGeneratedRepositories</c>（バイト不変）。</remarks>
    public required bool MultiDialect { get; init; }

    /// <summary>
    /// このスペックが DB 非依存のインメモリ Repository 群（<c>InMemory{Entity}Repository</c>・<c>InMemoryDataStore</c>・
    /// <c>InMemorySampleData</c>・<c>AddGeneratedInMemoryRepositories</c>）を出力するか。
    /// </summary>
    /// <remarks>
    /// インメモリ実装は方言非依存のため、Repository バケットを含み契約を出すスペック（単一方言＝契約＋実装スペック、
    /// マルチ方言＝契約スペック）で 1 度だけ出力する。方言実装スペック（ContractOnly=false かつ MultiDialect）では出さない。
    /// 既定 false（未指定のスペックは常に false でバイト不変）。
    /// </remarks>
    public bool InMemory { get; init; }
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
        return string.IsNullOrWhiteSpace(options.NamespaceName)
            ? DefaultRootNamespace
            : options.NamespaceName.Trim();
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
            GenerationBucket.EfCore => options.EfCoreNamespace,
            // RemoteServer に個別の名前空間オプションは設けない（既定 {root}.RemoteServer へフォールバック）
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
            GenerationBucket.EfCore => "EfCore",
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
    /// 根拠（<c>Templates/CSharpRuntime.scriban</c> の型参照から確定）:
    ///   Entity   → Runtime（EntityBase / 独自属性 / RowState）, ValueObject（プロパティ型が VO）
    ///   EditModel→ Runtime（EditModelBase / EditModelCollection）, ValueObject（VO 由来のパース・検証）
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
            GenerationBucket.EditModel => [GenerationBucket.Runtime, GenerationBucket.ValueObject],
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

        if (options.GenerateEntityClasses)
        {
            active.Add(GenerationBucket.Entity);
        }

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
        // 自作 SQL Server 実装を保持する。契約は EF Core 側・インメモリ側も参照するため、自作実装・EF Core・
        // インメモリのいずれかが有効なら出力する。EF/インメモリ単独出力時は Repository バケットに「契約のみ」＋
        // インメモリ実装が入る（自作 ADO 実装はテンプレート内で出し分ける）
        if (
            options.GenerateRepositories
            || options.GenerateEfCore
            || options.GenerateInMemoryRepositories
        )
        {
            active.Add(GenerationBucket.Repository);
        }

        if (options.GenerateEfCore)
        {
            active.Add(GenerationBucket.EfCore);
        }

        var anyClass =
            options.GenerateEntityClasses
            || options.GenerateEditModels
            || options.GenerateMappers
            || options.GenerateRepositories
            || options.GenerateEfCore
            || options.GenerateInMemoryRepositories;

        if (anyClass)
        {
            active.Add(GenerationBucket.Runtime);
        }

        return active;
    }

    /// <summary>
    /// 計画で各スペックへ載せる方言を、例外を投げずに解決する。
    /// </summary>
    /// <remarks>
    /// 実効方言（<see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/>）は未対応方言で例外を投げるが、
    /// Plan はプレビューなどでも呼ばれ、Repository 非生成時には図の方言名（例 mysql）が <see cref="CodeGenerationOptions.RepositoryDialect"/> に
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

        // マルチ方言（実効方言 2 つ以上）で Repository を生成するときのみ、契約 1 回＋方言別 namespace 実装の
        // 新レイアウトを使う。単一方言・Repository 非生成時は従来レイアウトを完全維持する（バイト不変）。
        var multiDialect =
            options.GenerateRepositories
            && dialects.Count >= 2
            && active.Contains(GenerationBucket.Repository);

        if (!options.SplitFilesByCategory)
        {
            // 非分割: 全バケットを 1 ファイルへ。マルチ方言時は Repository を「契約スペック＋方言別実装スペック」へ
            // 展開し、同一ファイル名で連結する（RenderFiles が block namespace で連結・using を先頭へ集約）。
            if (!multiDialect)
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
                        InMemory =
                            options.GenerateInMemoryRepositories
                            && active.Contains(GenerationBucket.Repository),
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
                    // インメモリ実装は方言非依存のため契約スペックへ 1 度だけ載せる（方言実装スペックには載せない）
                    InMemory = options.GenerateInMemoryRepositories,
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

        // パッケージ参照モードの分割生成では、共有基盤（Runtime バケット）は固定 infra だけで構成されるため
        // ファイルを作らない（全型がパッケージ QuickER.Runtime に移る）。他バケットの Runtime 向けクロス using は
        // activeSet から外れることで自然に落ち、代わりに GeneratedFileUsings が固定名前空間 using を付ける。
        var emittedBuckets = options.UseRuntimePackages
            ? active.Where(bucket => bucket != GenerationBucket.Runtime).ToList()
            : active;

        var namespaceByBucket = emittedBuckets.ToDictionary(
            bucket => bucket,
            bucket => ResolveNamespace(options, bucket)
        );
        var activeSet = emittedBuckets.ToHashSet();

        var splitSpecs = new List<GeneratedFileSpec>();

        foreach (var bucket in emittedBuckets)
        {
            var ownNamespace = namespaceByBucket[bucket];
            // 依存グラフから「実際に参照する他バケット」の名前空間だけを using する
            // （無差別に全バケットを using していた従来動作の不要 using を排除する）。
            // 有効でない依存先（例: VO 無効時の ValueObject）は自然に除外される。また依存先が
            // 自分と同一名前空間へ解決される場合は自分自身の using になるため除外する。
            var crossUsings = BucketDependencies(bucket)
                .Where(dependency => activeSet.Contains(dependency))
                .Select(dependency => namespaceByBucket[dependency])
                .Where(ns => !string.Equals(ns, ownNamespace, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ns => ns, StringComparer.Ordinal)
                .ToList();

            // マルチ方言時の Repository バケットは契約のみを自 namespace へ出し、方言別実装は別ファイルへ分ける。
            if (multiDialect && bucket == GenerationBucket.Repository)
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
                        // インメモリ実装は方言非依存のため契約（Repository バケット）スペックへ 1 度だけ載せる
                        InMemory = options.GenerateInMemoryRepositories,
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
                            extraCrossUsings: crossUsings
                        )
                    );
                }

                continue;
            }

            splitSpecs.Add(
                new GeneratedFileSpec
                {
                    FileName = DefaultFileName(bucket),
                    NamespaceName = ownNamespace,
                    Buckets = [bucket],
                    CrossNamespaceUsings = crossUsings,
                    Dialect = primaryDialect,
                    ContractOnly = false,
                    MultiDialect = false,
                    // 分割・単一方言時は Repository バケットのファイルへインメモリ実装を載せる
                    InMemory =
                        options.GenerateInMemoryRepositories
                        && bucket == GenerationBucket.Repository,
                }
            );
        }

        AddRemoteServerSpec(splitSpecs, options, active, primaryDialect);

        return splitSpecs;
    }

    /// <summary>非分割時のサーバー実装ファイル名（例: <c>MyApp.g.cs</c> → <c>MyApp.RemoteServer.g.cs</c>）</summary>
    public static string RemoteServerFileName(string outputFileName)
    {
        const string suffix = ".g.cs";
        var baseName = outputFileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? outputFileName[..^suffix.Length]
            : outputFileName;

        return $"{baseName}.RemoteServer{suffix}";
    }

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
            }
        );
    }

    /// <summary>
    /// サーバー実装スペックを「Repository バケットを含む最後のスペックの直後」へ挿入する
    /// （分割時に Repositories（方言別実装含む）の下・EfCore / Runtime より前へ並べるため）。
    /// </summary>
    private static void InsertAfterRepositorySpecs(
        List<GeneratedFileSpec> specs,
        GeneratedFileSpec remoteServerSpec
    )
    {
        var lastRepositoryIndex = specs.FindLastIndex(spec =>
            spec.Buckets.Contains(GenerationBucket.Repository)
        );

        // 呼び出し元で Repository バケットの有効性を確認済みだが、万一見つからない場合は末尾へ退避する
        var insertIndex = lastRepositoryIndex < 0 ? specs.Count : lastRepositoryIndex + 1;
        specs.Insert(insertIndex, remoteServerSpec);
    }

    /// <summary>方言別実装スペックを組み立てる（Repository バケットのみ・{RepositoryNamespace}.Suffix・契約 namespace を using）</summary>
    private static GeneratedFileSpec BuildDialectRepositorySpec(
        CodeGenerationOptions options,
        string fileName,
        string repositoryNamespace,
        string dialect,
        string contractNamespace,
        IReadOnlyList<string>? extraCrossUsings = null
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

        var orderedCrossUsings = crossUsings
            .Where(ns => !string.Equals(ns, dialectNamespace, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        return new GeneratedFileSpec
        {
            FileName = fileName,
            NamespaceName = dialectNamespace,
            Buckets = [GenerationBucket.Repository],
            CrossNamespaceUsings = orderedCrossUsings,
            Dialect = dialect,
            ContractOnly = false,
            MultiDialect = true,
        };
    }

    /// <summary>方言別実装の分割ファイル名（例: <c>Repositories.SqlServer.g.cs</c>）</summary>
    private static string DialectRepositoryFileName(string dialect) =>
        $"{DefaultSuffix(GenerationBucket.Repository)}.{DialectNamespaceSuffix(dialect)}.g.cs";
}
