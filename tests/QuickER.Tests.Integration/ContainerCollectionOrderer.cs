using QuickER.Tests.Integration;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(ContainerCollectionOrderer))]

namespace QuickER.Tests.Integration;

/// <summary>
/// テストのコレクションを流す順：コンテナのコレクションを宣言の順（夜間を 1 つのジョブで流していた頃の長い順）に先に始め、残りは xunit の既定の順のまま
/// </summary>
/// <remarks>
/// <para>
/// xunit v3 の既定（並列のモード Collections・Conservative）は、同時に流すコレクションの数を maxParallelThreads（CPU の数）で絞り、
/// コレクションは枠を取ってから最後のテストが終わるまで枠を持つ。枠を待つコレクションは、この順で並ぶ。既定の順（一意の Id の序数の順）
/// では、コンテナ 1 つで直列に流れる長いコレクション（SQL Server）が後ろに並ぶと、それが始まってから終わるまでがジョブの終わりを決め、
/// 仕事の総量を枠に詰めた下限より大きく遅れる。長い順に先に始めると、終わりはその下限に近づく。
/// </para>
/// <para>
/// 宣言の順は、夜間を 1 つのジョブで流していた頃の実測の長い順（2026-11-20 の夜間の TRX）。夜間の Integration を簡易 DSL の総当たりと
/// それ以外の 2 つのジョブに分けた後は、どちらのジョブも、そのジョブに流れる宣言したコレクションの先頭の 4 つが同時に始まるので、その 4 つの中の順では
/// 終わりが変わらず、順はそのままにしている。分けた後の 3 夜の実測（2026-11-24・25 の夜間の TRX・遅い夜の値）：簡易 DSL のジョブは
/// 4 クラスが別々のコレクション（PostgreSQL 23.4 分・Oracle 22.5 分・MySQL 20.5 分・SQL Server の簡易 DSL 18.0 分）で、どれも 0〜1 分で
/// 始まる。残りのジョブは先頭の 4 つ（SQL Server 18.1 分・Oracle 6.4 分・MySQL 8.9 分・SQL Server の 2 つ目 15.2 分）が 0〜1 分で始まる。
/// その後に夜間の種を増やした見込みでも、残りのジョブで長いのは SQL Server（約 23 分）と SQL Server の 2 つ目（約 20.5 分）で、どちらも
/// 先頭の 4 つに入る。普段の CI（4 枠）では、どちらも普段に重い SQL Server の 2 つ（約 7.5 分ずつ・開発機）が最初の 4 枠に入る。
/// コンテナのコレクション（<c>CollectionDefinition</c> を持ち <c>ICollectionFixture</c> を実装するクラス）は、どれも
/// <see cref="Order"/> に載せる（載せ漏れと、コンテナのコレクションでない型の宣言は、照合の表の行
/// <c>integration-container-collections-ordered</c>（QuickER.Tests の <c>ParityTests</c>）が名指しする）。
/// </para>
/// </remarks>
public sealed class ContainerCollectionOrderer : ITestCollectionOrderer
{
    /// <summary>先に始めるコンテナのコレクション（この順に始める）</summary>
    internal static readonly Type[] Order =
    [
        typeof(SqlServerContainerCollection),
        typeof(OracleContainerCollection),
        typeof(MySqlContainerCollection),
        typeof(SqlServerSecondContainerCollection),
        typeof(SqlServerDslFuzzContainerCollection),
        typeof(PostgreSqlContainerCollection),
        typeof(PostGisContainerCollection),
    ];

    /// <summary>宣言の型の名前 → 順の位置</summary>
    private static readonly Dictionary<string, int> Positions = Order
        .Select((type, index) => (type.FullName!, index))
        .ToDictionary(pair => pair.Item1, pair => pair.index, StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections
    )
        where TTestCollection : ITestCollection
    {
        // 宣言したコレクションを宣言の順に先頭へ、残りは既定の順のまま後ろへ
        var declared = testCollections
            .Where(collection => PositionOf(collection) is not null)
            .OrderBy(collection => PositionOf(collection));
        var rest = DefaultTestCollectionOrderer.Instance.OrderTestCollections(
            testCollections.Where(collection => PositionOf(collection) is null).ToList()
        );

        return [.. declared, .. rest];
    }

    /// <summary>宣言の順の位置（宣言に無いコレクションは null）</summary>
    private static int? PositionOf(ITestCollection collection) =>
        collection.TestCollectionClassName is { } name
        && Positions.TryGetValue(name, out var position)
            ? position
            : null;
}
