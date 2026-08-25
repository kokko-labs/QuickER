using System.Collections.Generic;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;

namespace QuickER.Tests.TestDoubles;

/// <summary>
/// <see cref="IDiagramTransferHost"/> のスタブ（入出力コマンドサービスを MainViewModel なしで検証するため）。
/// </summary>
/// <remarks>置換呼び出しは記録のみ行い、状態（Model 等）はテストが直接設定する。</remarks>
internal sealed class StubDiagramTransferHost : IDiagramTransferHost
{
    /// <summary>BuildModel が返す意味モデル</summary>
    public ErDiagram Model { get; set; } = new();

    /// <summary>現在の対象 DBMS プロバイダ（SQL エクスポートを検証しないテストでは未設定でよい）</summary>
    public IDatabaseProvider CurrentProvider { get; set; } = null!;

    /// <summary>未保存変更の有無</summary>
    public bool IsDirty { get; set; }

    /// <summary>図が空で失うものが無いか（既定 true＝無確認で通る側）</summary>
    public bool HasNothingToLose { get; set; } = true;

    /// <summary>名前付きクエリの件数</summary>
    public int QueryCount { get; set; }

    /// <summary>RenderSvg に渡されたパスの記録</summary>
    public List<string> SvgRenderPaths { get; } = [];

    /// <summary>ReplaceWholesale に渡された図の記録</summary>
    public List<(
        IReadOnlyList<Entity> Entities,
        IReadOnlyList<Relationship> Relationships
    )> WholesaleReplacements { get; } = [];

    /// <summary>ReplaceMerged に渡された図の記録</summary>
    public List<ErDiagram> MergedReplacements { get; } = [];

    /// <inheritdoc />
    public ErDiagram BuildModel() => Model;

    /// <inheritdoc />
    public void RenderSvg(string path) => SvgRenderPaths.Add(path);

    /// <inheritdoc />
    public void ReplaceWholesale(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    ) => WholesaleReplacements.Add((entities, relationships));

    /// <inheritdoc />
    public void ReplaceMerged(ErDiagram diagram) => MergedReplacements.Add(diagram);
}
