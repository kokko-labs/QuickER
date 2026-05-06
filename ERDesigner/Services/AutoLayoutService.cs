using System.Collections.Generic;
using System.Linq;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>
/// エンティティを自動的に整列するサービスです。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="LayoutGrid"/>: シンプルな格子状レイアウト。</item>
///   <item><see cref="LayoutTree"/>: リレーションを利用した階層 (BFS) レイアウト。</item>
/// </list>
/// </remarks>
public static class AutoLayoutService
{
    /// <summary>横のギャップ (px)。</summary>
    private const double GapX = 40;

    /// <summary>縦のギャップ (px)。</summary>
    private const double GapY = 40;

    /// <summary>左上の余白 (px)。</summary>
    private const double Margin = 40;

    /// <summary>
    /// エンティティを格子状に並べ替えます。
    /// </summary>
    public static void LayoutGrid(IList<EntityViewModel> entities, int columns = 0)
    {
        if (entities.Count == 0)
        {
            return;
        }

        if (columns <= 0)
        {
            columns = (int)Math.Ceiling(Math.Sqrt(entities.Count));
        }

        // 各列の幅、各行の高さを事前計算
        var colWidths = new double[columns];
        var rowCount = (int)Math.Ceiling((double)entities.Count / columns);
        var rowHeights = new double[rowCount];

        for (var i = 0; i < entities.Count; i++)
        {
            var c = i % columns;
            var r = i / columns;
            colWidths[c] = Math.Max(colWidths[c], entities[i].Width + GapX);
            rowHeights[r] = Math.Max(rowHeights[r], entities[i].DisplayHeight + GapY);
        }

        for (var i = 0; i < entities.Count; i++)
        {
            var c = i % columns;
            var r = i / columns;
            var x = Margin;

            for (var ci = 0; ci < c; ci++)
            {
                x += colWidths[ci];
            }

            var y = Margin;

            for (var ri = 0; ri < r; ri++)
            {
                y += rowHeights[ri];
            }

            entities[i].X = x;
            entities[i].Y = y;
        }
    }

    /// <summary>
    /// リレーション (起点→終点) を辺と見なして BFS で階層レイアウトを行います。
    /// 接続のないエンティティは末尾の行にグリッド配置されます。
    /// </summary>
    /// <param name="entities">並べ替え対象のエンティティ一覧。</param>
    /// <param name="relationships">リレーション一覧（向きあり）。</param>
    public static void LayoutTree(IList<EntityViewModel> entities, IList<RelationshipViewModel> relationships)
    {
        if (entities.Count == 0)
        {
            return;
        }

        // 隣接リスト（無向で扱う）
        var adj = entities.ToDictionary(e => e, _ => new List<EntityViewModel>());

        foreach (var r in relationships)
        {
            if (adj.ContainsKey(r.Source) && adj.ContainsKey(r.Target))
            {
                adj[r.Source].Add(r.Target);
                adj[r.Target].Add(r.Source);
            }
        }

        // 入次数 0（または最も次数が少ないノード）から BFS
        var visited = new HashSet<EntityViewModel>();
        var levels = new Dictionary<int, List<EntityViewModel>>();

        foreach (var root in entities.OrderByDescending(e => adj[e].Count))
        {
            if (visited.Contains(root))
            {
                continue;
            }

            var queue = new Queue<(EntityViewModel node, int depth)>();
            queue.Enqueue((root, 0));
            visited.Add(root);

            while (queue.Count > 0)
            {
                var (node, depth) = queue.Dequeue();

                if (!levels.TryGetValue(depth, out var list))
                {
                    levels[depth] = list = new List<EntityViewModel>();
                }

                list.Add(node);

                foreach (var nb in adj[node])
                {
                    if (visited.Add(nb))
                    {
                        queue.Enqueue((nb, depth + 1));
                    }
                }
            }
        }

        // 各階層を縦に配置 (各エンティティの幅を考慮して重ならないようにする)
        // まず各階層の最大高さを計算
        var depthHeight = new Dictionary<int, double>();

        foreach (var (depth, list) in levels)
        {
            depthHeight[depth] = list.Max(e => e.DisplayHeight);
        }

        foreach (var (depth, list) in levels)
        {
            var yOffset = Margin;

            for (var d = 0; d < depth; d++)
            {
                if (depthHeight.TryGetValue(d, out var h))
                {
                    yOffset += h + GapY;
                }
            }

            var xOffset = Margin;

            for (var i = 0; i < list.Count; i++)
            {
                list[i].X = xOffset;
                list[i].Y = yOffset;
                xOffset += list[i].Width + GapX;
            }
        }
    }
}
