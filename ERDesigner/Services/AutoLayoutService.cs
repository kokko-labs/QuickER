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
///   <item><see cref="LayoutForceDirected"/>: 力学モデル（Fruchterman-Reingold 改）による自由配置レイアウト</item>
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

    /// <summary>バリセンタ法スイープの最大反復回数（並びが変化しなくなれば早期終了）</summary>
    private const int MaxBarycenterSweeps = 8;

    /// <summary>接続エンティティ間の理想間隔へ加える余白 (px)</summary>
    private const double FreeSpacing = 40.0;

    /// <summary>力学モデルの反復回数</summary>
    private const int FreeIterations = 300;

    /// <summary>全体重心へ引き戻す重力係数（連結成分の離散を防ぐ）</summary>
    private const double FreeGravity = 0.1;

    /// <summary>反発が働く距離の上限（理想距離に対する倍率）遠方ペアの反発を打ち切り配置をコンパクトに保つ</summary>
    private const double FreeRepulsionRange = 2.0;

    /// <summary>終盤の 1 反復あたり最大移動量 (px)</summary>
    private const double FreeMinTemperature = 2.0;

    /// <summary>重なり解消の最大パス数</summary>
    private const int MaxOverlapRemovalPasses = 100;

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
    /// <remarks>
    /// 連結成分ごとに最も次数の高いノードを起点とし、深さ順に各階層を縦へ配置する
    /// 各階層内の並び順はバリセンタ（重心）法で隣接階層の接続先に近づけ、リレーション線の交差を減らす
    /// 各階層は最も幅広い階層に対して中央寄せし、親子間の線の傾きを抑える
    /// </remarks>
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
        var depthOf = new Dictionary<EntityViewModel, int>();
        var levels = new Dictionary<int, List<EntityViewModel>>();

        foreach (var root in entities.OrderByDescending(e => adj[e].Count))
        {
            if (depthOf.ContainsKey(root))
            {
                continue;
            }

            var queue = new Queue<(EntityViewModel node, int depth)>();
            queue.Enqueue((root, 0));
            depthOf[root] = 0;

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
                    if (!depthOf.ContainsKey(nb))
                    {
                        depthOf[nb] = depth + 1;
                        queue.Enqueue((nb, depth + 1));
                    }
                }
            }
        }

        OrderLevelsByBarycenter(levels, adj, depthOf);

        // 階層ごとの最大高さを求め、深い階層ほど下方へ配置するための縦オフセット基準とする
        var depthHeight = new Dictionary<int, double>();

        foreach (var (depth, list) in levels)
        {
            depthHeight[depth] = list.Max(e => e.DisplayHeight);
        }

        // 階層ごとの合計幅を求め、最も幅広い階層に対して中央寄せするための横オフセット基準とする
        var levelWidth = levels.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(e => e.Width + GapX) - GapX);
        var maxLevelWidth = levelWidth.Values.Max();

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

            var xOffset = Margin + (maxLevelWidth - levelWidth[depth]) / 2;

            for (var i = 0; i < list.Count; i++)
            {
                list[i].X = xOffset;
                list[i].Y = yOffset;
                xOffset += list[i].Width + GapX;
            }
        }
    }

    /// <summary>リレーションで繋がったエンティティが引き合い、全エンティティが反発する力学モデルで自由配置する</summary>
    /// <remarks>
    /// Fruchterman-Reingold 法を改変し、可変サイズの矩形に合わせて理想距離を調整したうえで重なりを後処理で解消する
    /// <list type="number">
    ///   <item>BFS 順で円環状に初期配置し、近い接続が初期解で隣り合いやすくする</item>
    ///   <item>反発（全ペア）・引力（辺）・重力（重心へ）を線形冷却の温度で制限しながら反復する</item>
    ///   <item>反復後に矩形同士の重なりを軸方向の小さい侵入量から押し出して解消する</item>
    /// </list>
    /// 乱数・列挙順非決定要素を一切使わないため結果は決定的
    /// </remarks>
    /// <param name="entities">配置対象のエンティティ一覧</param>
    /// <param name="relationships">リレーション一覧（レイアウト上は無向として扱う）</param>
    public static void LayoutForceDirected(
        IList<EntityViewModel> entities,
        IList<RelationshipViewModel> relationships
    )
    {
        if (entities.Count == 0)
        {
            return;
        }

        var edges = BuildEdges(entities, relationships);

        // リレーションが無ければ引力が働かず散らばるだけのため従来格子へフォールバックする
        if (edges.Count == 0)
        {
            PlaceInGrid(entities, ResolveColumns(entities.Count, 0));
            return;
        }

        var n = entities.Count;

        // 反発・引力の理想距離は矩形の代表サイズ（幅と高さの大きい方）で決める
        var size = new double[n];

        for (var i = 0; i < n; i++)
        {
            size[i] = Math.Max(entities[i].Width, entities[i].DisplayHeight);
        }

        // 中心座標で力学計算する（最後に左上座標へ戻す）
        var pos = new (double X, double Y)[n];
        var order = BuildInitialOrder(n, edges);

        // 円環状の初期配置（BFS 順で等間隔に置き、接続が隣り合いやすくする）
        var circumference = 0.0;

        for (var i = 0; i < n; i++)
        {
            circumference += size[i] + GapX;
        }

        var radius = Math.Max(circumference / (2 * Math.PI), 1.0);

        for (var k = 0; k < n; k++)
        {
            var theta = 2 * Math.PI * k / n;
            pos[order[k]] = (radius * Math.Cos(theta), radius * Math.Sin(theta));
        }

        // ペアごとの理想距離 k_ij = (size_i + size_j) / 2 + FreeSpacing
        double Ideal(int i, int j) => (size[i] + size[j]) / 2 + FreeSpacing;

        var avgSize = 0.0;

        for (var i = 0; i < n; i++)
        {
            avgSize += size[i];
        }

        avgSize /= n;

        // 線形冷却の開始温度（平均サイズに比例させ、序盤の大きな移動を許す）
        var tStart = 2 * (avgSize + FreeSpacing);
        var disp = new (double X, double Y)[n];

        for (var iter = 0; iter < FreeIterations; iter++)
        {
            for (var i = 0; i < n; i++)
            {
                disp[i] = (0, 0);
            }

            // 反発: 全ペア i<j で k_ij² / d の力を互いに離れる方向へ
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    var dx = pos[i].X - pos[j].X;
                    var dy = pos[i].Y - pos[j].Y;
                    var d = LayoutGeometry.Distance(pos[i], pos[j]);
                    var ideal = Ideal(i, j);

                    // 十分離れたペアの反発は打ち切る（遠距離反発の累積による全体の膨張を防ぐ）
                    if (d > ideal * FreeRepulsionRange)
                    {
                        continue;
                    }

                    if (d < 0.01)
                    {
                        d = 0.01;
                    }

                    // 方向ベクトルが零なら任意方向 (1, 0) で押し離す
                    if (dx == 0 && dy == 0)
                    {
                        dx = 1;
                        dy = 0;
                    }

                    var force = ideal * ideal / d;
                    var ux = dx / d;
                    var uy = dy / d;

                    disp[i] = (disp[i].X + ux * force, disp[i].Y + uy * force);
                    disp[j] = (disp[j].X - ux * force, disp[j].Y - uy * force);
                }
            }

            // 引力: 辺ごとに d² / k_ij の力を互いに近づく方向へ
            foreach (var (a, b) in edges)
            {
                var dx = pos[a].X - pos[b].X;
                var dy = pos[a].Y - pos[b].Y;
                var d = LayoutGeometry.Distance(pos[a], pos[b]);

                if (d < 0.01)
                {
                    d = 0.01;
                }

                if (dx == 0 && dy == 0)
                {
                    dx = 1;
                    dy = 0;
                }

                var force = d * d / Ideal(a, b);
                var ux = dx / d;
                var uy = dy / d;

                disp[a] = (disp[a].X - ux * force, disp[a].Y - uy * force);
                disp[b] = (disp[b].X + ux * force, disp[b].Y + uy * force);
            }

            // 重力: 重心 C へ向けて (C - pos) × FreeGravity を加算し連結成分の離散を防ぐ
            var cx = 0.0;
            var cy = 0.0;

            for (var i = 0; i < n; i++)
            {
                cx += pos[i].X;
                cy += pos[i].Y;
            }

            cx /= n;
            cy /= n;

            for (var i = 0; i < n; i++)
            {
                disp[i] = (disp[i].X + (cx - pos[i].X) * FreeGravity, disp[i].Y + (cy - pos[i].Y) * FreeGravity);
            }

            // 線形冷却の温度（1 反復あたり最大移動量）で変位を制限してから一括適用する
            var t = tStart + (FreeMinTemperature - tStart) * iter / (FreeIterations - 1);

            for (var i = 0; i < n; i++)
            {
                var len = LayoutGeometry.Distance((0, 0), disp[i]);

                if (len > t)
                {
                    disp[i] = (disp[i].X / len * t, disp[i].Y / len * t);
                }

                pos[i] = (pos[i].X + disp[i].X, pos[i].Y + disp[i].Y);
            }
        }

        // 矩形重なり解消（ギャップ込みの矩形が重なる軸の小さい侵入量から押し出す）
        for (var pass = 0; pass < MaxOverlapRemovalPasses; pass++)
        {
            var anyOverlap = false;

            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    var minX = (entities[i].Width + entities[j].Width) / 2 + GapX;
                    var minY = (entities[i].DisplayHeight + entities[j].DisplayHeight) / 2 + GapY;
                    var dx = pos[i].X - pos[j].X;
                    var dy = pos[i].Y - pos[j].Y;
                    var overlapX = minX - Math.Abs(dx);
                    var overlapY = minY - Math.Abs(dy);

                    if (overlapX <= 0 || overlapY <= 0)
                    {
                        continue;
                    }

                    anyOverlap = true;

                    // 侵入量の小さい軸へ半分ずつ互いを逆向きに押し出す
                    if (overlapX < overlapY)
                    {
                        var dir = dx > 0 ? 1 : dx < 0 ? -1 : (i < j ? -1 : 1);
                        var push = overlapX / 2 * dir;
                        pos[i] = (pos[i].X + push, pos[i].Y);
                        pos[j] = (pos[j].X - push, pos[j].Y);
                    }
                    else
                    {
                        var dir = dy > 0 ? 1 : dy < 0 ? -1 : (i < j ? -1 : 1);
                        var push = overlapY / 2 * dir;
                        pos[i] = (pos[i].X, pos[i].Y + push);
                        pos[j] = (pos[j].X, pos[j].Y - push);
                    }
                }
            }

            if (!anyOverlap)
            {
                break;
            }
        }

        // 正規化: 左上座標の最小 X / Y が Margin になるよう全体を平行移動する
        var minLeft = double.MaxValue;
        var minTop = double.MaxValue;

        for (var i = 0; i < n; i++)
        {
            minLeft = Math.Min(minLeft, pos[i].X - entities[i].Width / 2);
            minTop = Math.Min(minTop, pos[i].Y - entities[i].DisplayHeight / 2);
        }

        var shiftX = Margin - minLeft;
        var shiftY = Margin - minTop;

        for (var i = 0; i < n; i++)
        {
            entities[i].X = pos[i].X + shiftX - entities[i].Width / 2;
            entities[i].Y = pos[i].Y + shiftY - entities[i].DisplayHeight / 2;
        }
    }

    /// <summary>バリセンタ（重心）法で各階層内の並び順を隣接階層の接続先に近づけ、線の交差を減らす</summary>
    /// <remarks>
    /// 上→下・下→上のスイープを交互に行い、各ノードを隣接階層の接続先の平均位置で安定ソートする
    /// 並びが変化しなくなれば早期終了する 乱数を使わないため結果は決定的
    /// </remarks>
    private static void OrderLevelsByBarycenter(
        Dictionary<int, List<EntityViewModel>> levels,
        Dictionary<EntityViewModel, List<EntityViewModel>> adj,
        Dictionary<EntityViewModel, int> depthOf
    )
    {
        var maxDepth = levels.Keys.Max();

        if (maxDepth == 0)
        {
            return;
        }

        // 階層内の現在位置の逆引き（ソートキー計算と「接続先なし」時の現状維持に使う）
        var pos = new Dictionary<EntityViewModel, int>();

        foreach (var list in levels.Values)
        {
            for (var i = 0; i < list.Count; i++)
            {
                pos[list[i]] = i;
            }
        }

        // 隣接階層の接続先の平均位置（接続先が無ければ現在位置を維持する）
        double Barycenter(EntityViewModel e, int neighborDepth)
        {
            var sum = 0.0;
            var count = 0;

            foreach (var nb in adj[e])
            {
                if (depthOf[nb] == neighborDepth)
                {
                    sum += pos[nb];
                    count++;
                }
            }

            return count > 0 ? sum / count : pos[e];
        }

        // 指定階層を隣接階層基準のバリセンタで安定ソートし、並びが変化したかを返す
        bool SortLevel(int depth, int neighborDepth)
        {
            var list = levels[depth];
            var sorted = list.OrderBy(e => Barycenter(e, neighborDepth)).ToList();
            var changed = false;

            for (var i = 0; i < sorted.Count; i++)
            {
                if (!ReferenceEquals(list[i], sorted[i]))
                {
                    changed = true;
                }

                list[i] = sorted[i];
                pos[sorted[i]] = i;
            }

            return changed;
        }

        for (var sweep = 0; sweep < MaxBarycenterSweeps; sweep++)
        {
            var changed = false;

            for (var d = 1; d <= maxDepth; d++)
            {
                changed |= SortLevel(d, d - 1);
            }

            for (var d = maxDepth - 1; d >= 0; d--)
            {
                changed |= SortLevel(d, d + 1);
            }

            if (!changed)
            {
                break;
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

            cost += LengthWeight * LayoutGeometry.Distance(pa, pb);

            // 端点以外のエンティティのセル上を線が通過していないか調べる
            for (var w = 0; w < count; w++)
            {
                if (w == a || w == b)
                {
                    continue;
                }

                if (LayoutGeometry.DistancePointToSegment(CellPoint(slotOf[w], columns), pa, pb) < NodeClearance)
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

                if (LayoutGeometry.SegmentsCross(pa, pb, CellPoint(slotOf[c], columns), CellPoint(slotOf[d], columns)))
                {
                    cost += CrossingWeight;
                }
            }
        }

        return cost;
    }

    /// <summary>マス目番号をセル間隔 = 1 の正規化座標へ変換する</summary>
    private static (double X, double Y) CellPoint(int slot, int columns) => (slot % columns, slot / columns);

}
