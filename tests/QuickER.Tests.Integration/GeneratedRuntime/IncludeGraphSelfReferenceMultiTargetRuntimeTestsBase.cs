using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedMultiTargetFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// edge-skip の実行時観測を<b>マルチターゲットフィクスチャ（値オブジェクト有効・EF Core なし）</b>の型で流す
/// 派生の共通基底。SQLite 方言版と SQL Server 方言版がスキーマ作成とリポジトリ解決だけを差し込む。
/// </summary>
public abstract class IncludeGraphSelfReferenceMultiTargetRuntimeTestsBase
    : IncludeGraphSelfReferenceRuntimeTestsBase<NodeEntity>
{
    /// <summary>方言別 DI で解決した自己参照テーブルのリポジトリを返す</summary>
    protected abstract INodeRepository Nodes();

    /// <summary>スキーマを作り直す（方言別 DDL）</summary>
    protected abstract Task ResetSchemaAsync();

    protected override async Task ResetAndSeedAsync()
    {
        await ResetSchemaAsync();

        await Nodes().InsertAsync(NewNode(1, null, "root"), Ct);
        await Nodes().InsertAsync(NewNode(2, 1, "child"), Ct);
    }

    /// <summary>自己参照エンティティを組み立てる（値オブジェクト有効の図）</summary>
    private static NodeEntity NewNode(int nodeId, int? parentNodeId, string label) =>
        new()
        {
            NodeId = NodeIdValue.Create(nodeId),
            ParentNodeId = parentNodeId is null
                ? null
                : ParentNodeIdValue.Create(parentNodeId.Value),
            Label = LabelValue.Create(label),
        };

    protected override Task<NodeEntity?> FetchNodeWithGraphAsync(int nodeId)
    {
        var key = NodeIdValue.Create(nodeId);

        return Nodes()
            .Query()
            .IncludeGraph()
            .Where(node => node.NodeId == key)
            .FirstOrDefaultAsync(Ct);
    }

    protected override Task<int> CountNodesAsync() => Nodes().Query().CountAsync(Ct);

    protected override IReadOnlyList<NodeEntity> ChildNodesOf(NodeEntity node) =>
        node.Nodes.ToList();

    protected override int? ParentNodeIdOf(NodeEntity node) => node.ParentNodeId?.Value;
}
