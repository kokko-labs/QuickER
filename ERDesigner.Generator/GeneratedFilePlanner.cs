namespace ERDesigner.Generator;

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
            _ => "Generated",
        };

    /// <summary>分割時のバケット既定ファイル名</summary>
    public static string DefaultFileName(GenerationBucket bucket) =>
        $"{DefaultSuffix(bucket)}.g.cs";

    /// <summary>生成対象として有効なバケットを正準順で返す</summary>
    /// <remarks>
    /// 並び順は UI のカテゴリ別 namespace 欄と一致させる（Entity → EditModel → Mapper → Repository → ValueObject → Runtime）。
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

        if (options.GenerateRepositories)
        {
            active.Add(GenerationBucket.Repository);
        }

        if (options.GenerateValueObjects)
        {
            active.Add(GenerationBucket.ValueObject);
        }

        var anyClass =
            options.GenerateEntityClasses
            || options.GenerateEditModels
            || options.GenerateMappers
            || options.GenerateRepositories;

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

        return active
            .Select(bucket =>
            {
                var ownNamespace = namespaceByBucket[bucket];
                var crossUsings = namespaceByBucket
                    .Values.Where(ns => !string.Equals(ns, ownNamespace, StringComparison.Ordinal))
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
