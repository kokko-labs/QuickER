using System.Collections.Generic;
using System.Linq;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>エンティティを自動整列するサービス</summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="LayoutGrid"/>: 格子状レイアウト</item>
///   <item><see cref="LayoutTree"/>: リレーションを辺と見なした階層（BFS）レイアウト</item>
/// </list>
/// </remarks>
public static class AutoLayoutService
{
    /// <summary>列間の横ギャップ (px)</summary>
    private const double GapX = 40;

    /// <summary>行間の縦ギャップ (px)</summary>
    private const double GapY = 40;

    /// <summary>左上の余白 (px)</summary>
    private const double Margin = 40;

    /// <summary>エンティティを格子状に並べ替える</summary>
    /// <param name="columns">列数 0 以下なら要素数の平方根から自動決定する</param>
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

        // 列ごとの最大幅・行ごとの最大高さを先に求め、可変サイズでも重ならないようにする
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

    /// <summary>リレーションを辺と見なして BFS で階層レイアウトを行う</summary>
    /// <remarks>連結成分ごとに最も次数の高いノードを起点とし、深さ順に各階層を縦へ配置する</remarks>
    /// <param name="entities">並べ替え対象のエンティティ一覧</param>
    /// <param name="relationships">リレーション一覧（レイアウト上は無向として扱う）</param>
    public static void LayoutTree(IList<EntityViewModel> entities, IList<RelationshipViewModel> relationships)
    {
        if (entities.Count == 0)
        {
            return;
        }

        // リレーションを無向グラフの隣接リストへ展開する
        var adj = entities.ToDictionary(e => e, _ => new List<EntityViewModel>());

        foreach (var r in relationships)
        {
            if (adj.ContainsKey(r.Source) && adj.ContainsKey(r.Target))
            {
                adj[r.Source].Add(r.Target);
                adj[r.Target].Add(r.Source);
            }
        }

        // 次数の多いノードを起点に BFS を回し、未訪問成分も順次起点化して全件を配置する
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

        // 階層ごとの最大高さを求め、深い階層ほど下方へ配置するための縦オフセット基準とする
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
