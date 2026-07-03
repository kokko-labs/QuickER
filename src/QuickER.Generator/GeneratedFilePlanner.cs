namespace QuickER.Generator;

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
        // 自作 SQL Server 実装を保持する。契約は EF Core 側も参照するため、自作実装・EF Core のどちらかが有効なら出力する。
        // EF 単独出力時は Repository バケットに「契約のみ」が入る（自作実装はテンプレート内で出し分ける）
        if (options.GenerateRepositories || options.GenerateEfCore)
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
            || options.GenerateEfCore;

        if (anyClass)
        {
            active.Add(GenerationBucket.Runtime);
        }

        return active;
    }

    /// <summary>
    /// 出力ファイルの計画を作成する
    /// </summary>
    /// <remarks>
    /// 非分割時は全バケットを 1 ファイル（<see cref="CodeGenerationOptions.OutputFileName"/>・ルート名前空間）へまとめる。
    /// 分割時は有効バケットを 1 カテゴリ 1 ファイルへ展開し、各ファイルに他ファイルの名前空間を using として付与する
    /// （同一名前空間に解決されてもファイルは分け、自分自身の名前空間は using しない）
    /// </remarks>
    public static IReadOnlyList<GeneratedFileSpec> Plan(CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var active = ActiveBuckets(options);

        if (!options.SplitFilesByCategory)
        {
            return
            [
                new GeneratedFileSpec
                {
                    FileName = options.OutputFileName,
                    NamespaceName = ResolveRootNamespace(options),
                    Buckets = active,
                    CrossNamespaceUsings = [],
                },
            ];
        }

        var namespaceByBucket = active.ToDictionary(
            bucket => bucket,
            bucket => ResolveNamespace(options, bucket)
        );
        var activeSet = active.ToHashSet();

        return active
            .Select(bucket =>
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

                return new GeneratedFileSpec
                {
                    FileName = DefaultFileName(bucket),
                    NamespaceName = ownNamespace,
                    Buckets = [bucket],
                    CrossNamespaceUsings = crossUsings,
                };
            })
            .ToList();
    }
}
