using System.Collections.Generic;
using System.Linq;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>エンティティを自動整列するサービス</summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="LayoutGrid(IList{EntityViewModel}, int)"/>: 格子状レイアウト（並び順のまま配置）</item>
///   <item><see cref="LayoutGrid(IList{EntityViewModel}, IList{RelationshipViewModel}, int)"/>: リレーション線の交差をできるだけ減らす格子状レイアウト</item>
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

    /// <summary>コスト関数における線同士の交差 1 件あたりの重み</summary>
    private const double CrossingWeight = 1000;

    /// <summary>コスト関数における線がエンティティのセル上を通過する 1 件あたりの重み</summary>
    private const double ThroughWeight = 100;

    /// <summary>コスト関数における線の長さ（セル単位）の重み</summary>
    private const double LengthWeight = 1;

    /// <summary>セル中心と線分の距離がこの値未満なら「線がエンティティ上を通過」と見なす（セル間隔 = 1 基準）</summary>
    private const double NodeClearance = 0.4;

    /// <summary>ペア交換ヒルクライミングの最大反復回数（改善が無くなれば早期終了）</summary>
    private const int MaxOptimizePasses = 20;

    /// <summary>エンティティを並び順のまま格子状に並べ替える</summary>
    /// <param name="columns">列数 0 以下なら要素数の平方根から自動決定する</param>
    public static void LayoutGrid(IList<EntityViewModel> entities, int columns = 0)
    {
        if (entities.Count == 0)
        {
            return;
        }

        PlaceInGrid(entities, ResolveColumns(entities.Count, columns));
    }

    /// <summary>リレーション線の交差ができるだけ少なくなる順序を求めてから格子状に並べ替える</summary>
    /// <remarks>
    /// 厳密な交差最小化は NP 困難のためヒューリスティックで近似する
    /// <list type="number">
    ///   <item>次数の高いノードを起点とした BFS で、接続されたエンティティが並びで隣接しやすい初期順序を作る</item>
    ///   <item>交差数・エンティティ跨ぎ数・線長を重み付けしたコストをペア交換ヒルクライミングで削減する</item>
    /// </list>
    /// 乱数は使わないため同じ入力に対する結果は決定的
    /// </remarks>
    /// <param name="entities">並べ替え対象のエンティティ一覧</param>
    /// <param name="relationships">リレーション一覧（レイアウト上は無向として扱う）</param>
    /// <param name="columns">列数 0 以下なら要素数の平方根から自動決定する</param>
    public static void LayoutGrid(
        IList<EntityViewModel> entities,
        IList<RelationshipViewModel> relationships,
        int columns = 0
    )
    {
        if (entities.Count == 0)
        {
            return;
        }

        var cols = ResolveColumns(entities.Count, columns);
        var edges = BuildEdges(entities, relationships);

        // リレーションが無ければ最適化する意味がないため従来配置にフォールバックする
        if (edges.Count == 0)
        {
            PlaceInGrid(entities, cols);
            return;
        }

        var order = BuildInitialOrder(entities.Count, edges);
        OptimizeOrder(order, edges, cols);

        PlaceInGrid(order.Select(i => entities[i]).ToList(), cols);
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

    /// <summary>列数指定が無効なら要素数の平方根から自動決定する</summary>
    private static int ResolveColumns(int count, int columns) =>
        columns > 0 ? columns : (int)Math.Ceiling(Math.Sqrt(count));

    /// <summary>与えられた並び順で格子状に座標を設定する</summary>
    private static void PlaceInGrid(IList<EntityViewModel> entities, int columns)
    {
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

    /// <summary>リレーションをエンティティ番号の辺一覧へ変換する（自己参照と重複ペアは除外）</summary>
    private static List<(int A, int B)> BuildEdges(
        IList<EntityViewModel> entities,
        IList<RelationshipViewModel> relationships
    )
    {
        var indexOf = new Dictionary<EntityViewModel, int>();

        for (var i = 0; i < entities.Count; i++)
        {
            indexOf[entities[i]] = i;
        }

        var edges = new List<(int A, int B)>();
        var seen = new HashSet<(int, int)>();

        foreach (var r in relationships)
        {
            if (!indexOf.TryGetValue(r.Source, out var a) || !indexOf.TryGetValue(r.Target, out var b))
            {
                continue;
            }

            // 自己参照は配置順に影響しないため除外 同一ペアの多重リレーションは 1 本に縮約する
            if (a == b)
            {
                continue;
            }

            var key = a < b ? (a, b) : (b, a);

            if (seen.Add(key))
            {
                edges.Add(key);
            }
        }

        return edges;
    }

    /// <summary>次数の高いノードを起点とした BFS で初期順序（マス目順 → エンティティ番号）を作る</summary>
    /// <remarks>接続されたエンティティが並びで隣接しやすくなり、ヒルクライミングの初期解として機能する</remarks>
    private static int[] BuildInitialOrder(int count, List<(int A, int B)> edges)
    {
        var adj = new List<int>[count];

        for (var i = 0; i < count; i++)
        {
            adj[i] = new List<int>();
        }

        foreach (var (a, b) in edges)
        {
            adj[a].Add(b);
            adj[b].Add(a);
        }

        var order = new List<int>(count);
        var visited = new bool[count];

        foreach (var root in Enumerable.Range(0, count).OrderByDescending(i => adj[i].Count))
        {
            if (visited[root])
            {
                continue;
            }

            var queue = new Queue<int>();
            queue.Enqueue(root);
            visited[root] = true;

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                order.Add(node);

                foreach (var nb in adj[node])
                {
                    if (!visited[nb])
                    {
                        visited[nb] = true;
                        queue.Enqueue(nb);
                    }
                }
            }
        }

        return order.ToArray();
    }

    /// <summary>ペア交換ヒルクライミングでマス目への割り当てコストを削減する</summary>
    /// <remarks>
    /// 改善する交換を見つけ次第採用し（first-improvement）、1 巡して改善が無ければ終了する
    /// 交換の影響はその 2 エンティティに接続する辺に限られるため、コスト差分は影響辺のみで評価する
    /// </remarks>
    /// <param name="order">マス目順のエンティティ番号配列（in-place で並べ替える）</param>
    private static void OptimizeOrder(int[] order, List<(int A, int B)> edges, int columns)
    {
        var count = order.Length;

        // エンティティ番号 → マス目番号の逆引きと、エンティティ番号 → 接続辺番号の索引を作る
        var slotOf = new int[count];

        for (var s = 0; s < count; s++)
        {
            slotOf[order[s]] = s;
        }

        var incident = new List<int>[count];

        for (var i = 0; i < count; i++)
        {
            incident[i] = new List<int>();
        }

        for (var ei = 0; ei < edges.Count; ei++)
        {
            incident[edges[ei].A].Add(ei);
            incident[edges[ei].B].Add(ei);
        }

        var affectedFlags = new bool[edges.Count];
        var affected = new List<int>();

        for (var pass = 0; pass < MaxOptimizePasses; pass++)
        {
            var improved = false;

            for (var si = 0; si < count - 1; si++)
            {
                for (var sj = si + 1; sj < count; sj++)
                {
                    var u = order[si];
                    var v = order[sj];

                    // 影響を受けるのは u・v に接続する辺のみ（重複しないよう集約）
                    affected.Clear();

                    foreach (var ei in incident[u])
                    {
                        if (!affectedFlags[ei])
                        {
                            affectedFlags[ei] = true;
                            affected.Add(ei);
                        }
                    }

                    foreach (var ei in incident[v])
                    {
                        if (!affectedFlags[ei])
                        {
                            affectedFlags[ei] = true;
                            affected.Add(ei);
                        }
                    }

                    if (affected.Count == 0)
                    {
                        continue;
                    }

                    var before = PartialCost(affected, affectedFlags, edges, slotOf, columns, count);

                    // 仮交換してコストを再評価し、改善しなければ元へ戻す
                    SwapSlots(order, slotOf, si, sj);

                    var after = PartialCost(affected, affectedFlags, edges, slotOf, columns, count);

                    if (after < before - 1e-9)
                    {
                        improved = true;
                    }
                    else
                    {
                        SwapSlots(order, slotOf, si, sj);
                    }

                    foreach (var ei in affected)
                    {
                        affectedFlags[ei] = false;
                    }
                }
            }

            if (!improved)
            {
                break;
            }
        }
    }

    /// <summary>order と slotOf の整合を保ったまま 2 つのマス目の中身を入れ替える</summary>
    private static void SwapSlots(int[] order, int[] slotOf, int si, int sj)
    {
        (order[si], order[sj]) = (order[sj], order[si]);
        slotOf[order[si]] = si;
        slotOf[order[sj]] = sj;
    }

    /// <summary>影響辺に関わるコスト（交差・エンティティ跨ぎ・線長）を集計する</summary>
    /// <remarks>
    /// 座標はマス目の行・列をそのまま使う（セル間隔 = 1 の正規化座標）
    /// 影響辺同士の交差は番号の小さい側でのみ数え、二重計上を防ぐ
    /// </remarks>
    private static double PartialCost(
        List<int> affected,
        bool[] affectedFlags,
        List<(int A, int B)> edges,
        int[] slotOf,
        int columns,
        int count
    )
    {
        var cost = 0.0;

        foreach (var ei in affected)
        {
            var (a, b) = edges[ei];
            var pa = CellPoint(slotOf[a], columns);
            var pb = CellPoint(slotOf[b], columns);

            cost += LengthWeight * Distance(pa, pb);

            // 端点以外のエンティティのセル上を線が通過していないか調べる
            for (var w = 0; w < count; w++)
            {
                if (w == a || w == b)
                {
                    continue;
                }

                if (DistancePointToSegment(CellPoint(slotOf[w], columns), pa, pb) < NodeClearance)
                {
                    cost += ThroughWeight;
                }
            }

            for (var fi = 0; fi < edges.Count; fi++)
            {
                if (fi == ei || (affectedFlags[fi] && fi < ei))
                {
                    continue;
                }

                var (c, d) = edges[fi];

                // 端点を共有する辺同士は交差と見なさない
                if (a == c || a == d || b == c || b == d)
                {
                    continue;
                }

                if (SegmentsCross(pa, pb, CellPoint(slotOf[c], columns), CellPoint(slotOf[d], columns)))
                {
                    cost += CrossingWeight;
                }
            }
        }

        return cost;
    }

    /// <summary>マス目番号をセル間隔 = 1 の正規化座標へ変換する</summary>
    private static (double X, double Y) CellPoint(int slot, int columns) => (slot % columns, slot / columns);

    /// <summary>2 点間のユークリッド距離</summary>
    private static double Distance((double X, double Y) p, (double X, double Y) q)
    {
        var dx = p.X - q.X;
        var dy = p.Y - q.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>点と線分の最短距離</summary>
    private static double DistancePointToSegment(
        (double X, double Y) p,
        (double X, double Y) a,
        (double X, double Y) b
    )
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lenSq = abx * abx + aby * aby;

        if (lenSq <= 0)
        {
            return Distance(p, a);
        }

        // 点 p を線分 ab へ射影し、線分の範囲内へクランプする
        var t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lenSq, 0, 1);
        return Distance(p, (a.X + t * abx, a.Y + t * aby));
    }

    /// <summary>2 線分が内部で交差するか判定する（端点での接触・共線の重なりは含めない）</summary>
    private static bool SegmentsCross(
        (double X, double Y) p1,
        (double X, double Y) p2,
        (double X, double Y) p3,
        (double X, double Y) p4
    )
    {
        var d1 = Orientation(p3, p4, p1);
        var d2 = Orientation(p3, p4, p2);
        var d3 = Orientation(p1, p2, p3);
        var d4 = Orientation(p1, p2, p4);

        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    /// <summary>3 点の回転方向（正: 反時計回り 負: 時計回り 0: 一直線）</summary>
    private static double Orientation((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
