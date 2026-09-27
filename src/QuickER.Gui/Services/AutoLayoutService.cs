using System.Collections.Generic;
using System.Linq;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>エンティティを自動整列するサービス</summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="LayoutGrid(IList{EntityViewModel}, int)"/>: 格子状レイアウト（並び順のまま配置）</item>
///   <item><see cref="LayoutGrid(IList{EntityViewModel}, IList{RelationshipViewModel}, int)"/>: リレーション線の交差をできるだけ減らす格子状レイアウト</item>
///   <item><see cref="LayoutTree"/>: リレーションを辺と見なした階層（BFS）レイアウト</item>
///   <item><see cref="LayoutForceDirected"/>: 力学モデル＋辺の軸整列で、リレーション線が水平/垂直に近づく自由レイアウト</item>
/// </list>
/// </remarks>
public static class AutoLayoutService
{
    /// <summary>列間の横ギャップ (px)</summary>
    /// <remarks>
    /// リレーション線の両端マーカー（多重度記号、各 <c>MarkerSize</c> = 24px）と中央のラベルが
    /// 重ならず読めるよう、隣接（＝接続）エンティティ間の最短距離を確保する間隔
    /// </remarks>
    private const double GapX = 100;

    /// <summary>行間の縦ギャップ (px)</summary>
    /// <remarks><see cref="GapX"/> と同様にリレーションのマーカー・ラベルの可読性を確保する</remarks>
    private const double GapY = 100;

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

    /// <summary>ペア交換ヒルクライミングに使える判定回数の上限（超えたらそこまでの改善を採って打ち切る）</summary>
    /// <remarks>
    /// <para>
    /// 交差最小化の総当たりは「パス × 全ペア × 影響辺 × 全辺」で、テーブル数に対しておよそ 3 乗で伸びる。
    /// 上限を置かないと、数百テーブルの図（DB 取込では普通にある）を開いたときに画面が数十秒固まる
    /// （実測: 400 テーブルで 88 秒）。数え上げるのは<b>時間ではなく判定の回数</b>なので、同じ図なら
    /// 常に同じ位置で打ち切られる＝結果は決定的で、環境の速さに依らず再現する。
    /// </para>
    /// <para>
    /// 値は実測から決めた（この上限に達するまでの所要時間が 1〜2 秒に収まる範囲）。上限に達しない図
    /// （実測では 200 テーブル・200 リレーション程度まで）は最後まで最適化され、結果は上限が無い頃と同一。
    /// </para>
    /// </remarks>
    private const long MaxOptimizeChecks = 250_000_000;

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

    /// <summary>辺の軸整列フェーズで、1 パスごとに辺の短い成分を縮める割合（0〜1）</summary>
    private const double FreeAlignStrength = 0.5;

    /// <summary>辺の軸整列フェーズの反復パス数（整列と重なり解消を交互に適用する）</summary>
    private const int FreeAlignPasses = 60;

    /// <summary>コンパクションフェーズで全ノードを全体重心へ寄せる係数（1 パスあたり重心方向へ詰める割合）</summary>
    /// <remarks>空白を詰め、孤立ノード（リレーションなし）も中央へ引き寄せる 重なり解消が最小ギャップで支えるため潰れない</remarks>
    private const double FreeCompactStrength = 0.1;

    /// <summary>コンパクションフェーズの反復パス数（重心寄せと重なり解消を交互に適用する）</summary>
    private const int FreeCompactPasses = 60;

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
    public static void LayoutTree(
        IList<EntityViewModel> entities,
        IList<RelationshipViewModel> relationships
    )
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
        var levelWidth = levels.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Sum(e => e.Width + GapX) - GapX
        );
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

    /// <summary>リレーションで繋がったエンティティが引き合う力学モデルで配置を決め、リレーション線が水平/垂直に近づくよう整列する</summary>
    /// <remarks>
    /// 自由配置のグループ構造（繋がりの強いエンティティが近くに集まる）を保ったまま、
    /// 各リレーション辺を最も近い軸（水平 or 垂直）へ寄せて線を読みやすくする
    /// <list type="number">
    ///   <item>力学モデル（Fruchterman-Reingold 改）のシミュレーションで全エンティティの中心座標を求める（重心寄せのコンパクションと辺の軸整列を含む）</item>
    ///   <item>格子へスナップせず、中心座標を左上座標へ変換して <see cref="Margin"/> 正規化する</item>
    /// </list>
    /// 次数の高いノードに接続する全辺を同時に軸へ揃えることは原理的に不可能なため、結果は「なるべく水平/垂直」のベストエフォート
    /// 乱数・列挙順非決定要素を一切使わないため結果は決定的
    /// </remarks>
    /// <param name="entities">配置対象のエンティティ一覧</param>
    /// <param name="relationships">リレーション一覧（レイアウト上は無向として扱う）</param>
    /// <param name="columns">リレーションが無い場合のフォールバック格子の列数 0 以下なら要素数の平方根から自動決定する</param>
    public static void LayoutForceDirected(
        IList<EntityViewModel> entities,
        IList<RelationshipViewModel> relationships,
        int columns = 0
    )
    {
        if (entities.Count == 0)
        {
            return;
        }

        var edges = BuildEdges(entities, relationships);

        // リレーションが無ければ引力・整列力が働かず配置が決まらないため従来格子へフォールバックする
        if (edges.Count == 0)
        {
            PlaceInGrid(entities, ResolveColumns(entities.Count, columns));
            return;
        }

        var centers = ComputeForceDirectedCenters(entities, edges);
        PlaceAtCenters(entities, centers);
    }

    /// <summary>既存配置を一切動かさず、レイアウトの無い新規エンティティのみを空き領域へ格子状に追記配置する</summary>
    /// <remarks>
    /// 外部ツール（MCP サーバ等）がエンティティだけを追記し視覚情報を書かない文書や、再取込マージで新規テーブルが
    /// 加わった場合に、既存エンティティの位置を保ったまま欠落分だけを配置するために使う。
    /// <list type="number">
    ///   <item>固定群の外接矩形の下または右のうち、合成後の占有面積が小さくなる側を選ぶ</item>
    ///   <item>選んだ側の起点から <see cref="GapX"/>/<see cref="GapY"/> 準拠の格子で新規を並べる
    ///   （新規を固定の外接矩形の外側の領域へ限定するため、新規同士・新規と固定の矩形重なりはゼロになる）</item>
    ///   <item>固定エンティティと接続する新規は、接続先の座標（下配置なら X・右配置なら Y）で並べ替え、
    ///   接続先に近い列/行へ寄せる（バリセンタ寄せ）</item>
    /// </list>
    /// 既存メソッド（Grid/Tree/ForceDirected）と異なり固定群の座標は読むだけで書き換えない 乱数を使わないため決定的
    /// </remarks>
    /// <param name="fixedEntities">座標を変更してはならない既存エンティティ（読み取りのみ）</param>
    /// <param name="newEntities">空き領域へ配置する新規エンティティ（これらの X/Y のみ書き換える）</param>
    /// <param name="relationships">リレーション一覧（新規↔固定の接続でバリセンタ寄せに使う。無向として扱う）</param>
    public static void LayoutAppend(
        IList<EntityViewModel> fixedEntities,
        IList<EntityViewModel> newEntities,
        IList<RelationshipViewModel> relationships
    )
    {
        if (newEntities.Count == 0)
        {
            return;
        }

        // 固定群が無ければ追記先の基準が無いため、新規を左上から格子配置する（全欠落フォールバック相当）
        if (fixedEntities.Count == 0)
        {
            PlaceInGrid(newEntities, ResolveColumns(newEntities.Count, 0));
            return;
        }

        // 固定群の外接矩形 この矩形の外側にだけ新規を置けば固定との重なりは起きない
        var fx0 = fixedEntities.Min(e => e.X);
        var fy0 = fixedEntities.Min(e => e.Y);
        var fx1 = fixedEntities.Max(e => e.X + e.Width);
        var fy1 = fixedEntities.Max(e => e.Y + e.DisplayHeight);
        var fixedW = fx1 - fx0;
        var fixedH = fy1 - fy0;

        // 新規ブロックの区画数（列 or 行）は要素数の平方根で正方形に近づける
        var lanes = ResolveColumns(newEntities.Count, 0);

        // 下配置（rowMajor: lanes 列）・右配置（colMajor: lanes 行）それぞれのブロック寸法を見積もる
        var (belowW, belowH) = LayoutGridBlock(
            newEntities,
            lanes,
            rowMajor: true,
            0,
            0,
            apply: false
        );
        var (rightW, rightH) = LayoutGridBlock(
            newEntities,
            lanes,
            rowMajor: false,
            0,
            0,
            apply: false
        );

        // 合成後の占有面積が小さくなる側へ寄せる（同点は下＝縦方向の成長を優先）
        var belowArea = Math.Max(fixedW, belowW) * (fixedH + GapY + belowH);
        var rightArea = (fixedW + GapX + rightW) * Math.Max(fixedH, rightH);
        var placeBelow = belowArea <= rightArea;

        // 固定と接続する新規を接続先座標（下配置=X 基準・右配置=Y 基準）で並べ替え、接続先に近い列/行へ寄せる
        var ordered = OrderByFixedBarycenter(
            fixedEntities,
            newEntities,
            relationships,
            useX: placeBelow
        );

        if (placeBelow)
        {
            LayoutGridBlock(ordered, lanes, rowMajor: true, fx0, fy1 + GapY, apply: true);
        }
        else
        {
            LayoutGridBlock(ordered, lanes, rowMajor: false, fx1 + GapX, fy0, apply: true);
        }
    }

    /// <summary>格子ブロックの列幅・行高を求め、必要なら原点基準で各エンティティの左上座標を設定する</summary>
    /// <remarks>
    /// <paramref name="rowMajor"/> が真: 番号 i を col=i%lanes, row=i/lanes（lanes=列数）へ。
    /// 偽: row=i%lanes, col=i/lanes（lanes=行数）へ。列幅・行高は <see cref="PlaceInGrid"/> と同じく
    /// 該当セルの最大サイズ＋ギャップで求めるため、可変サイズでも重ならない。
    /// 返り値は末尾のギャップを除いたブロックの実占有幅・高さ。
    /// </remarks>
    private static (double Width, double Height) LayoutGridBlock(
        IList<EntityViewModel> ordered,
        int lanes,
        bool rowMajor,
        double originX,
        double originY,
        bool apply
    )
    {
        var count = ordered.Count;
        var otherLanes = (int)Math.Ceiling((double)count / lanes);
        var cols = rowMajor ? lanes : otherLanes;
        var rows = rowMajor ? otherLanes : lanes;
        var colWidths = new double[cols];
        var rowHeights = new double[rows];

        for (var i = 0; i < count; i++)
        {
            var (c, r) = CellOf(i, lanes, rowMajor);
            colWidths[c] = Math.Max(colWidths[c], ordered[i].Width + GapX);
            rowHeights[r] = Math.Max(rowHeights[r], ordered[i].DisplayHeight + GapY);
        }

        if (apply)
        {
            for (var i = 0; i < count; i++)
            {
                var (c, r) = CellOf(i, lanes, rowMajor);
                var x = originX;

                for (var ci = 0; ci < c; ci++)
                {
                    x += colWidths[ci];
                }

                var y = originY;

                for (var ri = 0; ri < r; ri++)
                {
                    y += rowHeights[ri];
                }

                ordered[i].X = x;
                ordered[i].Y = y;
            }
        }

        // ブロック外寸は末尾セルのギャップを除いた実占有幅・高さ
        var width = colWidths.Sum() - (cols > 0 ? GapX : 0);
        var height = rowHeights.Sum() - (rows > 0 ? GapY : 0);
        return (width, height);
    }

    /// <summary>格子番号を列・行へ変換する（rowMajor は列が先に進み、colMajor は行が先に進む）</summary>
    private static (int Col, int Row) CellOf(int i, int lanes, bool rowMajor) =>
        rowMajor ? (i % lanes, i / lanes) : (i / lanes, i % lanes);

    /// <summary>固定エンティティと接続する新規を接続先座標で並べ替える（接続先に近い列/行へ寄せるバリセンタ）</summary>
    /// <remarks>
    /// 固定と接続する新規は接続先中心の平均（<paramref name="useX"/>: X 座標・偽なら Y 座標）で昇順、
    /// 固定と接続しない新規は元の順序のまま後段へ置く（LINQ の安定ソート）。新規同士の接続は寄せ対象にしない。
    /// </remarks>
    private static List<EntityViewModel> OrderByFixedBarycenter(
        IList<EntityViewModel> fixedEntities,
        IList<EntityViewModel> newEntities,
        IList<RelationshipViewModel> relationships,
        bool useX
    )
    {
        var fixedSet = new HashSet<EntityViewModel>(fixedEntities);
        var newSet = new HashSet<EntityViewModel>(newEntities);

        // 新規 → 接続する固定エンティティの座標和・件数（平均＝バリセンタ算出用）
        var sum = new Dictionary<EntityViewModel, double>();
        var cnt = new Dictionary<EntityViewModel, int>();

        foreach (var r in relationships)
        {
            EntityViewModel? newEnd = null;
            EntityViewModel? fixedEnd = null;

            if (newSet.Contains(r.Source) && fixedSet.Contains(r.Target))
            {
                newEnd = r.Source;
                fixedEnd = r.Target;
            }
            else if (newSet.Contains(r.Target) && fixedSet.Contains(r.Source))
            {
                newEnd = r.Target;
                fixedEnd = r.Source;
            }

            if (newEnd is null || fixedEnd is null)
            {
                continue;
            }

            var center = useX
                ? fixedEnd.X + fixedEnd.Width / 2
                : fixedEnd.Y + fixedEnd.DisplayHeight / 2;
            sum[newEnd] = (sum.TryGetValue(newEnd, out var s) ? s : 0) + center;
            cnt[newEnd] = (cnt.TryGetValue(newEnd, out var c) ? c : 0) + 1;
        }

        // 接続あり（バリセンタ昇順）を先、接続なし（元順）を後 いずれも安定・決定的
        return newEntities
            .Select((e, i) => (Entity: e, Index: i))
            .OrderBy(t => cnt.ContainsKey(t.Entity) ? 0 : 1)
            .ThenBy(t => cnt.TryGetValue(t.Entity, out var c) ? sum[t.Entity] / c : 0.0)
            .ThenBy(t => t.Index)
            .Select(t => t.Entity)
            .ToList();
    }

    /// <summary>力学モデルのシミュレーションを実行し全エンティティの中心座標を返す</summary>
    /// <remarks>
    /// サイズ計算 → 円環状の初期配置 → 反発・引力・重力の力学反復 → 重心寄せコンパクション → 辺の軸整列 → 矩形重なり解消までを担い、
    /// 正規化（左上座標への変換）は呼び出し側に委ねる 乱数を使わないため結果は決定的
    /// </remarks>
    private static (double X, double Y)[] ComputeForceDirectedCenters(
        IList<EntityViewModel> entities,
        List<(int A, int B)> edges
    )
    {
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
                disp[i] = (
                    disp[i].X + (cx - pos[i].X) * FreeGravity,
                    disp[i].Y + (cy - pos[i].Y) * FreeGravity
                );
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

        // 矩形重なり解消の 1 パス（ギャップ込みの矩形が重なる軸の小さい侵入量から押し出す）重なりが残れば true
        bool ResolveOverlapsOnce()
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
                        var dir =
                            dx > 0 ? 1
                            : dx < 0 ? -1
                            : (i < j ? -1 : 1);
                        var push = overlapX / 2 * dir;
                        pos[i] = (pos[i].X + push, pos[i].Y);
                        pos[j] = (pos[j].X - push, pos[j].Y);
                    }
                    else
                    {
                        var dir =
                            dy > 0 ? 1
                            : dy < 0 ? -1
                            : (i < j ? -1 : 1);
                        var push = overlapY / 2 * dir;
                        pos[i] = (pos[i].X, pos[i].Y + push);
                        pos[j] = (pos[j].X, pos[j].Y - push);
                    }
                }
            }

            return anyOverlap;
        }

        // コンパクションフェーズ: 全ノード（孤立ノード含む）を全体重心へ寄せて空白を詰める
        // 重なり解消が最小ギャップで支えるため潰れず、外周へ離れたノード・リレーションなしノードが中央へ集まる
        for (var pass = 0; pass < FreeCompactPasses; pass++)
        {
            var ccx = 0.0;
            var ccy = 0.0;

            for (var i = 0; i < n; i++)
            {
                ccx += pos[i].X;
                ccy += pos[i].Y;
            }

            ccx /= n;
            ccy /= n;

            for (var i = 0; i < n; i++)
            {
                pos[i] = (
                    pos[i].X + (ccx - pos[i].X) * FreeCompactStrength,
                    pos[i].Y + (ccy - pos[i].Y) * FreeCompactStrength
                );
            }

            ResolveOverlapsOnce();
        }

        // 辺の軸整列フェーズ: 各辺を最寄りの軸（縦寄り→X を、横寄り→Y を）へ寄せ、間に重なり解消を挟んで配置を保つ
        // 冷却温度に縛られない専用パスで行うことで、FR 反復終盤の低温による整列不足を回避する 直交性を確定させる最後の支配的処理
        var degree = new int[n];

        foreach (var (a, b) in edges)
        {
            degree[a]++;
            degree[b]++;
        }

        for (var pass = 0; pass < FreeAlignPasses; pass++)
        {
            for (var i = 0; i < n; i++)
            {
                disp[i] = (0, 0);
            }

            // 各辺の整列変位を端点へ累積する（高次数ノードは後で次数で割り、過剰移動を防ぐ）
            foreach (var (a, b) in edges)
            {
                var dx = pos[a].X - pos[b].X;
                var dy = pos[a].Y - pos[b].Y;

                // 寄せる軸は、重なり回避に必要な最小間隔で正規化した比で決める
                // （生の dx,dy 比較だと、横長でも横方向に詰まったペアを横整列しようとして重なり解消と競合し収束しない）
                var minX = (entities[a].Width + entities[b].Width) / 2 + GapX;
                var minY = (entities[a].DisplayHeight + entities[b].DisplayHeight) / 2 + GapY;

                if (Math.Abs(dx) / minX <= Math.Abs(dy) / minY)
                {
                    // 縦寄り: X を揃えて垂直線にする
                    var shift = dx / 2 * FreeAlignStrength;
                    disp[a].X -= shift;
                    disp[b].X += shift;
                }
                else
                {
                    // 横寄り: Y を揃えて水平線にする
                    var shift = dy / 2 * FreeAlignStrength;
                    disp[a].Y -= shift;
                    disp[b].Y += shift;
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (degree[i] == 0)
                {
                    continue;
                }

                pos[i] = (pos[i].X + disp[i].X / degree[i], pos[i].Y + disp[i].Y / degree[i]);
            }

            ResolveOverlapsOnce();
        }

        // 最終的な重なり解消（収束または上限まで）
        for (var pass = 0; pass < MaxOverlapRemovalPasses; pass++)
        {
            if (!ResolveOverlapsOnce())
            {
                break;
            }
        }

        return pos;
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

    /// <summary>中心座標を各エンティティの左上座標へ変換し、最小 X・Y が <see cref="Margin"/> になるよう平行移動して正規化する</summary>
    private static void PlaceAtCenters(
        IList<EntityViewModel> entities,
        (double X, double Y)[] centers
    )
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;

        // いったん中心 → 左上へ直し、全体の最小座標を求める
        for (var i = 0; i < entities.Count; i++)
        {
            var left = centers[i].X - entities[i].Width / 2;
            var top = centers[i].Y - entities[i].DisplayHeight / 2;
            entities[i].X = left;
            entities[i].Y = top;
            minX = Math.Min(minX, left);
            minY = Math.Min(minY, top);
        }

        // 最小座標が Margin に揃うよう全体を平行移動する（辺の相対位置＝軸整列は保たれる）
        var offsetX = Margin - minX;
        var offsetY = Margin - minY;

        for (var i = 0; i < entities.Count; i++)
        {
            entities[i].X += offsetX;
            entities[i].Y += offsetY;
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
            if (
                !indexOf.TryGetValue(r.Source, out var a)
                || !indexOf.TryGetValue(r.Target, out var b)
            )
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
    private static void OptimizeOrder(int[] order, List<(int A, int B)> edges, int columns) =>
        new GridOrderOptimizer(order, edges, columns).Run();

    /// <summary>格子への割り当てをペア交換ヒルクライミングで改善する最適化器</summary>
    /// <remarks>
    /// <para>
    /// 改善する交換を見つけ次第採用し（first-improvement）、1 巡して改善が無ければ終了する。
    /// 交換の影響はその 2 エンティティに接続する辺に限られるため、コスト差分は影響辺のみで評価する。
    /// </para>
    /// <para>
    /// <b>1 回の交換で変わるのは 2 エンティティの位置と、それに接続する辺の外接矩形だけ</b>なので、
    /// 位置・外接矩形は配列で持ち回り、交換のたびに差分だけ更新する。コスト評価の内側で座標を
    /// 求め直すと、評価 1 回あたり辺の本数ぶんの除算が走る（評価回数は数十万回に達する）。
    /// </para>
    /// </remarks>
    private sealed class GridOrderOptimizer
    {
        /// <summary>マス目順のエンティティ番号（in-place で並べ替える呼び出し側の配列）</summary>
        private readonly int[] _order;

        /// <summary>エンティティ番号 → マス目番号の逆引き</summary>
        private readonly int[] _slotOf;

        /// <summary>辺の始点側の端点（タプルの <see cref="List{T}"/> より添字アクセスが軽い）</summary>
        private readonly int[] _edgeA;

        /// <summary>辺の終点側の端点</summary>
        private readonly int[] _edgeB;

        /// <summary>エンティティ番号 → 接続する辺番号</summary>
        private readonly List<int>[] _incident;

        /// <summary>エンティティ番号 → セル座標の X（セル間隔 = 1 の正規化座標）</summary>
        private readonly double[] _cellX;

        /// <summary>エンティティ番号 → セル座標の Y</summary>
        private readonly double[] _cellY;

        /// <summary>辺番号 → 外接矩形（交差判定の足切り用）</summary>
        /// <remarks>
        /// 4 本の配列でなく 1 本の構造体配列にするのは、交差判定が辺番号の昇順に全辺を舐めるため。
        /// 4 本だと 1 回の判定で 4 つの配列を同時に触り、走査のたびにキャッシュの流れが 4 本になる。
        /// </remarks>
        private readonly EdgeBounds[] _edgeBounds;

        /// <summary>辺番号 → 影響辺集合に入っているか（重複排除用）</summary>
        private readonly bool[] _affectedFlags;

        /// <summary>影響辺の集合（交換ごとに使い回す）</summary>
        private readonly List<int> _affected = [];

        private readonly int _columns;
        private readonly int _count;

        /// <summary>最終行の行番号（マス目はエンティティ数ちょうどではなく、最終行は途中で終わる）</summary>
        private readonly int _lastRow;

        /// <summary>これまでに行った判定の回数（<see cref="MaxOptimizeChecks"/> と突き合わせる）</summary>
        private long _checks;

        public GridOrderOptimizer(int[] order, List<(int A, int B)> edges, int columns)
        {
            _order = order;
            _columns = columns;
            _count = order.Length;
            _lastRow = (_count - 1) / columns;
            _slotOf = new int[_count];

            for (var s = 0; s < _count; s++)
            {
                _slotOf[order[s]] = s;
            }

            _cellX = new double[_count];
            _cellY = new double[_count];

            for (var i = 0; i < _count; i++)
            {
                _cellX[i] = _slotOf[i] % columns;
                _cellY[i] = _slotOf[i] / columns;
            }

            _edgeA = new int[edges.Count];
            _edgeB = new int[edges.Count];
            _incident = new List<int>[_count];

            for (var i = 0; i < _count; i++)
            {
                _incident[i] = [];
            }

            for (var ei = 0; ei < edges.Count; ei++)
            {
                _edgeA[ei] = edges[ei].A;
                _edgeB[ei] = edges[ei].B;
                _incident[edges[ei].A].Add(ei);
                _incident[edges[ei].B].Add(ei);
            }

            _edgeBounds = new EdgeBounds[edges.Count];
            _affectedFlags = new bool[edges.Count];

            for (var ei = 0; ei < edges.Count; ei++)
            {
                RefreshEdgeBounds(ei);
            }
        }

        /// <summary>改善が無くなる（または上限パス数に達する）まで交換を繰り返す</summary>
        public void Run()
        {
            for (var pass = 0; pass < MaxOptimizePasses; pass++)
            {
                var improved = false;

                for (var si = 0; si < _count - 1; si++)
                {
                    for (var sj = si + 1; sj < _count; sj++)
                    {
                        // 予算を使い切ったら、そこまでの改善を採用して打ち切る
                        if (_checks >= MaxOptimizeChecks)
                        {
                            return;
                        }

                        if (TrySwap(si, sj))
                        {
                            improved = true;
                        }
                    }
                }

                if (!improved)
                {
                    return;
                }
            }
        }

        /// <summary>2 つのマス目の中身を交換し、改善しなければ元へ戻す（改善したら <c>true</c>）</summary>
        private bool TrySwap(int si, int sj)
        {
            // 影響を受けるのは交換する 2 エンティティに接続する辺のみ（重複しないよう集約）
            _affected.Clear();
            CollectAffected(_order[si]);
            CollectAffected(_order[sj]);

            if (_affected.Count == 0)
            {
                return false;
            }

            var before = PartialCost();

            Swap(si, sj);

            var after = PartialCost();
            var improved = after < before - 1e-9;

            if (!improved)
            {
                Swap(si, sj);
            }

            foreach (var ei in _affected)
            {
                _affectedFlags[ei] = false;
            }

            return improved;
        }

        /// <summary>指定エンティティに接続する辺を影響辺集合へ加える</summary>
        private void CollectAffected(int entity)
        {
            foreach (var ei in _incident[entity])
            {
                if (!_affectedFlags[ei])
                {
                    _affectedFlags[ei] = true;
                    _affected.Add(ei);
                }
            }
        }

        /// <summary>order・slotOf とキャッシュ（セル座標・辺の外接矩形）の整合を保って交換する</summary>
        private void Swap(int si, int sj)
        {
            (_order[si], _order[sj]) = (_order[sj], _order[si]);

            var u = _order[si];
            var v = _order[sj];
            _slotOf[u] = si;
            _slotOf[v] = sj;

            // 動いたのはこの 2 件だけなので、位置と、その 2 件に接続する辺の外接矩形だけを更新する
            _cellX[u] = si % _columns;
            _cellY[u] = si / _columns;
            _cellX[v] = sj % _columns;
            _cellY[v] = sj / _columns;

            foreach (var ei in _incident[u])
            {
                RefreshEdgeBounds(ei);
            }

            foreach (var ei in _incident[v])
            {
                RefreshEdgeBounds(ei);
            }
        }

        /// <summary>辺の外接矩形を現在の位置から求め直す</summary>
        private void RefreshEdgeBounds(int ei)
        {
            var ax = _cellX[_edgeA[ei]];
            var ay = _cellY[_edgeA[ei]];
            var bx = _cellX[_edgeB[ei]];
            var by = _cellY[_edgeB[ei]];

            _edgeBounds[ei] = new EdgeBounds(
                Math.Min(ax, bx),
                Math.Max(ax, bx),
                Math.Min(ay, by),
                Math.Max(ay, by)
            );
        }

        /// <summary>影響辺に関わるコスト（交差・エンティティ跨ぎ・線長）を集計する</summary>
        /// <remarks>
        /// 座標はマス目の行・列をそのまま使う（セル間隔 = 1 の正規化座標）。
        /// 影響辺同士の交差は番号の小さい側でのみ数え、二重計上を防ぐ。
        /// </remarks>
        private double PartialCost()
        {
            var cost = 0.0;

            foreach (var ei in _affected)
            {
                var a = _edgeA[ei];
                var b = _edgeB[ei];
                var pa = (X: _cellX[a], Y: _cellY[a]);
                var pb = (X: _cellX[b], Y: _cellY[b]);

                cost += LengthWeight * LayoutGeometry.Distance(pa, pb);
                cost += ThroughCost(ei, a, b, pa, pb);
                cost += CrossingCost(ei, a, b, pa, pb);
            }

            return cost;
        }

        /// <summary>端点以外のエンティティのセル上を線が通過している件数ぶんのコスト</summary>
        /// <remarks>
        /// <b>絞り込みは判定を省くためのもので、コストの値は変えない。</b>全セルを走査せず、線分から
        /// <see cref="NodeClearance"/> 以内に入り得る行・列だけを数え上げる（格子配置なのでセルの位置が
        /// 行・列の番号そのもの＝範囲を計算で出せる）。範囲の外は距離が必ず離れるため、集計結果は
        /// 全数走査と完全に一致する。
        /// </remarks>
        private double ThroughCost(
            int ei,
            int a,
            int b,
            (double X, double Y) pa,
            (double X, double Y) pb
        )
        {
            var cost = 0.0;
            var bounds = _edgeBounds[ei];
            var rowFirst = Math.Max(0, (int)Math.Ceiling(bounds.MinY - NodeClearance));
            var rowLast = Math.Min(_lastRow, (int)Math.Floor(bounds.MaxY + NodeClearance));

            for (var row = rowFirst; row <= rowLast; row++)
            {
                // この行の帯（row ± NodeClearance）に入る線分の断片が取り得る X の範囲を求め、
                // さらに左右へ NodeClearance だけ広げた列だけを見る（これより外は距離が必ず離れる）
                var (bandMinX, bandMaxX) = SegmentXRangeInBand(
                    pa,
                    pb,
                    row - NodeClearance,
                    row + NodeClearance
                );
                var colFirst = Math.Max(0, (int)Math.Ceiling(bandMinX - NodeClearance));
                var colLast = Math.Min(_columns - 1, (int)Math.Floor(bandMaxX + NodeClearance));
                var rowBase = row * _columns;
                _checks += Math.Max(0, colLast - colFirst + 1);

                for (var col = colFirst; col <= colLast; col++)
                {
                    var slot = rowBase + col;

                    // 最終行は途中で終わる（マス目の数はエンティティ数ちょうどではない）
                    if (slot >= _count)
                    {
                        break;
                    }

                    var w = _order[slot];

                    if (w == a || w == b)
                    {
                        continue;
                    }

                    if (
                        LayoutGeometry.DistancePointToSegment((_cellX[w], _cellY[w]), pa, pb)
                        < NodeClearance
                    )
                    {
                        cost += ThroughWeight;
                    }
                }
            }

            return cost;
        }

        /// <summary>他の辺との交差件数ぶんのコスト</summary>
        /// <remarks>
        /// 2 線分の外接矩形が離れていれば交差し得ないため、そこで打ち切る（接している場合は判定へ進める）。
        /// これも判定を省くだけで、集計結果は全数判定と完全に一致する。
        /// </remarks>
        private double CrossingCost(
            int ei,
            int a,
            int b,
            (double X, double Y) pa,
            (double X, double Y) pb
        )
        {
            var cost = 0.0;
            var bounds = _edgeBounds[ei];
            _checks += _edgeBounds.Length;

            for (var fi = 0; fi < _edgeBounds.Length; fi++)
            {
                var other = _edgeBounds[fi];

                if (
                    other.MaxX < bounds.MinX
                    || other.MinX > bounds.MaxX
                    || other.MaxY < bounds.MinY
                    || other.MinY > bounds.MaxY
                )
                {
                    continue;
                }

                if (fi == ei || (_affectedFlags[fi] && fi < ei))
                {
                    continue;
                }

                var c = _edgeA[fi];
                var d = _edgeB[fi];

                // 端点を共有する辺同士は交差と見なさない
                if (a == c || a == d || b == c || b == d)
                {
                    continue;
                }

                if (
                    LayoutGeometry.SegmentsCross(
                        pa,
                        pb,
                        (_cellX[c], _cellY[c]),
                        (_cellX[d], _cellY[d])
                    )
                )
                {
                    cost += CrossingWeight;
                }
            }

            return cost;
        }

        /// <summary>辺の外接矩形（交差し得ない相手を判定へ進める前に外すための足切り）</summary>
        private readonly record struct EdgeBounds(
            double MinX,
            double MaxX,
            double MinY,
            double MaxY
        );

        /// <summary>横帯に入る線分の断片が取り得る X の範囲を返す（通過判定の絞り込み用）</summary>
        /// <remarks>
        /// 帯と線分の Y 範囲が重なることは呼び出し側が保証しており、返す範囲は<b>実際の断片を含む
        /// 上位集合</b>（端点で丸めるため広めに出ることがある）。広めに出ても距離判定は従来どおり
        /// 行うため、集計結果は変わらない。
        /// </remarks>
        private static (double MinX, double MaxX) SegmentXRangeInBand(
            (double X, double Y) pa,
            (double X, double Y) pb,
            double bandMinY,
            double bandMaxY
        )
        {
            var dy = pb.Y - pa.Y;

            // 水平な線分は帯の中で X 全体を取り得る
            if (dy == 0)
            {
                return (Math.Min(pa.X, pb.X), Math.Max(pa.X, pb.X));
            }

            var tLow = Math.Clamp((bandMinY - pa.Y) / dy, 0, 1);
            var tHigh = Math.Clamp((bandMaxY - pa.Y) / dy, 0, 1);
            var xLow = pa.X + ((pb.X - pa.X) * tLow);
            var xHigh = pa.X + ((pb.X - pa.X) * tHigh);

            return (Math.Min(xLow, xHigh), Math.Max(xLow, xHigh));
        }
    }
}
