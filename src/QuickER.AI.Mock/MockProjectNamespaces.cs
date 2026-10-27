using QuickER.CodeGen.CSharp;

namespace QuickER.AI.Mock;

/// <summary>
/// スキャフォールドが <c>Generated/</c> へ出すデータ層の名前空間（README の using の案内に使う）。
/// </summary>
/// <param name="Entities">エンティティのクラスの名前空間</param>
/// <param name="Repositories">リポジトリ契約（<c>I{Entity}Repository</c>）の名前空間</param>
/// <param name="InMemoryRegistration"><c>AddGeneratedInMemoryRepositories</c> を宣言する名前空間</param>
/// <param name="DatabaseRegistration">
/// 方言の登録（<c>AddGenerated{方言}Repositories</c>）を宣言する名前空間（QuickER 版 Repository を出さない図は null）
/// </param>
/// <param name="Files">生成するファイル名と、そのファイルが宣言する名前空間（生成の計画の順）</param>
/// <remarks>
/// 値は手で書き写さず、スキャフォールドの生成オプションを生成器と同じ計画（<see cref="GeneratedFilePlanner.Plan"/>）に
/// 通して求める（<see cref="FromPlan"/>）。出力の形を変えても README が実際の <c>namespace</c> 宣言から離れない。
/// </remarks>
internal sealed record MockProjectNamespaces(
    string Entities,
    string Repositories,
    string InMemoryRegistration,
    string? DatabaseRegistration,
    IReadOnlyList<(string FileName, string Namespace)> Files
)
{
    /// <summary>生成オプションの計画から名前空間を求める</summary>
    /// <param name="options">スキャフォールドが生成に使うオプション</param>
    /// <param name="repositoryDialect">QuickER 版 Repository の方言（出さない図は null）</param>
    /// <exception cref="InvalidOperationException">計画に必要なファイルが無い場合（生成オプションの組み立ての誤り）</exception>
    internal static MockProjectNamespaces FromPlan(
        CodeGenerationOptions options,
        string? repositoryDialect
    )
    {
        var plan = GeneratedFilePlanner.Plan(options);

        // スキーマに依存するもの（エンティティ・per-entity の契約・DI 登録）を出すファイルだけを見る
        // （固定の基盤の Runtime*.g.cs は同じバケットを持っても DI 登録を出さない）
        GeneratedFileSpec Find(string description, Func<GeneratedFileSpec, bool> predicate) =>
            plan.FirstOrDefault(spec => spec.EmitSchemaDependent && predicate(spec))
            ?? throw new InvalidOperationException(
                $"The generation plan of the mock project has no file for {description}."
            );

        var entities = Find("the entities", spec => spec.Buckets.Contains(GenerationBucket.Entity));
        var contracts = Find(
            "the repository contracts",
            spec =>
                spec.Buckets.Contains(GenerationBucket.Repository)
                && (spec.ContractOnly || repositoryDialect is null)
        );
        var inMemory = Find(
            "the in-memory registration",
            spec => spec.Buckets.Contains(GenerationBucket.InMemory)
        );
        var database = repositoryDialect is null
            ? null
            : Find(
                $"the {repositoryDialect} registration",
                spec =>
                    spec.Buckets.Contains(GenerationBucket.Repository)
                    && !spec.ContractOnly
                    && string.Equals(
                        spec.Dialect,
                        repositoryDialect,
                        StringComparison.OrdinalIgnoreCase
                    )
            );

        return new MockProjectNamespaces(
            entities.NamespaceName,
            contracts.NamespaceName,
            inMemory.NamespaceName,
            database?.NamespaceName,
            plan.Select(spec => (spec.FileName, spec.NamespaceName)).ToList()
        );
    }
}
