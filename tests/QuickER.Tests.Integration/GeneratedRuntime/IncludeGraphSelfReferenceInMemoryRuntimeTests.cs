using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedInMemoryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// edge-skip の実行時観測を<b>インメモリ Repository</b>（値オブジェクト無効のフィクスチャ）で流す派生。
/// 実 DB を使わないため Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// インメモリ実行器は SQL を持たず、ストアの行を外部キーで突き合わせてナビゲーションを組み立てる。
/// 「Include ツリーに無い辺は組み立てない」がここでも成り立つことを固定する
/// （FK が揃っているのだから“親切に”埋めてしまう実装は容易に書けてしまう）。
/// </remarks>
public sealed class IncludeGraphSelfReferenceInMemoryRuntimeTests
    : IncludeGraphSelfReferenceRuntimeTestsBase<NodeEntity>
{
    /// <summary>全リポジトリで共有するインメモリストア</summary>
    private readonly InMemoryDataStore _store = new();

    /// <summary>自己参照テーブルのリポジトリ（シード済みストアを共有する）</summary>
    private InMemoryNodeRepository Nodes => new(_store);

    protected override async Task ResetAndSeedAsync()
    {
        _store.Clear();

        await Nodes.InsertAsync(NewNode(1, null, "root"), Ct);
        await Nodes.InsertAsync(NewNode(2, 1, "child"), Ct);
    }

    /// <summary>自己参照エンティティを組み立てる（インメモリフィクスチャは値オブジェクト無効）</summary>
    private static NodeEntity NewNode(int nodeId, int? parentNodeId, string label) =>
        new()
        {
            NodeId = nodeId,
            ParentNodeId = parentNodeId,
            Label = label,
        };

    protected override Task<NodeEntity?> FetchNodeWithGraphAsync(int nodeId) =>
        Nodes.Query().IncludeGraph().Where(node => node.NodeId == nodeId).FirstOrDefaultAsync(Ct);

    protected override Task<int> CountNodesAsync() => Nodes.Query().CountAsync(Ct);

    protected override IReadOnlyList<NodeEntity> ChildNodesOf(NodeEntity node) =>
        node.Nodes.ToList();

    protected override int? ParentNodeIdOf(NodeEntity node) => node.ParentNodeId;
}
