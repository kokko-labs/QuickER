using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// edge-skip の実行時観測を<b>EF Core Sqlite 版</b>（値オブジェクト有効のクエリフィクスチャ）で流す派生。
/// </summary>
/// <remarks>
/// <para>
/// EF Core は <c>IncludeNode</c> ツリーを自前の <c>Include</c>/<c>ThenInclude</c> 連鎖へ組み替える別実装のため、
/// 「ツリーに無い辺は展開されない」が EF Core でも成り立つことは、ここを通さないと分からない。
/// </para>
/// <para>
/// 値オブジェクト有効の図で EF Core を通せるのは、自己参照 FK（<c>parent_node_id</c>）が参照先の主キーと同じ
/// <c>NodeIdValue</c> を共有するようになったため。型が割れていた頃はモデル検証が <c>DbContext</c> ごと落ちたので、
/// このクラスは<b>モデル検証が通ること自体の実行時証拠</b>でもある（<c>Model</c> は最初の取得で必ず構築される）。
/// </para>
/// </remarks>
public sealed class IncludeGraphSelfReferenceEfCoreRuntimeTests
    : IncludeGraphSelfReferenceRuntimeTestsBase<NodeEntity>,
        IDisposable
{
    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlite(_db.ReadWriteCreateConnectionString)
            )
            .BuildServiceProvider();

    private INodeRepository Nodes() => Provider().GetRequiredService<INodeRepository>();

    protected override async Task ResetAndSeedAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        await Nodes().InsertAsync(NewNode(1, null, "root"), Ct);
        await Nodes().InsertAsync(NewNode(2, 1, "child"), Ct);
    }

    /// <summary>自己参照エンティティを組み立てる（親キーも主キーと同じ VO 型）</summary>
    private static NodeEntity NewNode(int nodeId, int? parentNodeId, string label) =>
        new()
        {
            NodeId = NodeIdValue.Create(nodeId),
            ParentNodeId = parentNodeId is null ? null : NodeIdValue.Create(parentNodeId.Value),
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

    /// <summary>自己参照 FK と主キーが同じ VO 型を共有する（EF Core のモデル検証が通る前提そのもの）</summary>
    [Fact(DisplayName = "[IncludeGraph] 自己参照 FK は参照先の主キーと同じ VO 型を共有する")]
    public void SelfReferenceForeignKey_SharesPrimaryKeyValueObjectType()
    {
        typeof(NodeEntity)
            .GetProperty(nameof(NodeEntity.ParentNodeId))!
            .PropertyType.Should()
            .Be(
                typeof(NodeEntity).GetProperty(nameof(NodeEntity.NodeId))!.PropertyType,
                "FK プロパティの CLR 型が主キーと違うと EF Core はモデルごと拒否する"
            );
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }
}
