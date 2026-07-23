using FluentAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using Xunit;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// <see cref="AutoLayoutService.LayoutAppend"/>（部分欠落レイアウトの追記配置）を検証するテストクラス。
/// 既存配置を一切動かさず、欠落分のみを空き領域へ重ならず配置することと、その配置品質（占有面積・交差数）を実測する。
/// </summary>
public class AutoLayoutAppendTests
{
    private readonly ITestOutputHelper _output;

    public AutoLayoutAppendTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>座標を持つ既存エンティティ（追記配置で不動であるべき対象）を生成する</summary>
    private static EntityViewModel FixedEntity(
        string name,
        double x,
        double y,
        double width = 200
    ) =>
        new(
            new Entity { TableName = name },
            new EntityLayout
            {
                X = x,
                Y = y,
                Width = width,
            }
        );

    /// <summary>配置前と判別できるよう原点で初期化した欠落（新規）エンティティを生成する</summary>
    private static EntityViewModel NewEntity(string name = "N") =>
        new(new Entity { TableName = name }, new EntityLayout { X = 0, Y = 0 });

    /// <summary>2 エンティティ間のリレーションを表すテスト用 ViewModel を生成する</summary>
    private static RelationshipViewModel Rel(EntityViewModel source, EntityViewModel target) =>
        new(
            new Relationship { SourceEntityId = source.Id, TargetEntityId = target.Id },
            source,
            target
        );

    /// <summary>矩形（Width × DisplayHeight、ギャップ加算なし）が重なるか</summary>
    private static bool Overlaps(EntityViewModel a, EntityViewModel b) =>
        a.X < b.X + b.Width
        && b.X < a.X + a.Width
        && a.Y < b.Y + b.DisplayHeight
        && b.Y < a.Y + a.DisplayHeight;

    /// <summary>エンティティ中心を返す</summary>
    private static (double X, double Y) Center(EntityViewModel e) =>
        (e.X + e.Width / 2, e.Y + e.DisplayHeight / 2);

    /// <summary>新規分が空である場合は何も配置せず例外にもならない</summary>
    [Fact(DisplayName = "LayoutAppend: 新規が空なら何もしない")]
    public void LayoutAppend_NoNewEntities_DoesNothing()
    {
        var fixedA = FixedEntity("A", 100, 100);
        var fixedB = FixedEntity("B", 400, 100);

        var act = () =>
            AutoLayoutService.LayoutAppend(
                new List<EntityViewModel> { fixedA, fixedB },
                new List<EntityViewModel>(),
                new List<RelationshipViewModel>()
            );

        act.Should().NotThrow();
        fixedA.X.Should().Be(100);
        fixedB.X.Should().Be(400);
    }

    /// <summary>既存エンティティは 1px も動かず、新規は原点に積まれず全矩形が重ならない</summary>
    [Fact(DisplayName = "LayoutAppend: 既存は不動・新規は原点に積まず重ならない")]
    public void LayoutAppend_KeepsFixedAndPlacesNewWithoutOverlap()
    {
        var fixedEntities = new List<EntityViewModel>
        {
            FixedEntity("A", 40, 40),
            FixedEntity("B", 340, 40),
            FixedEntity("C", 40, 240),
        };
        var fixedSnapshot = fixedEntities.Select(e => (e.X, e.Y)).ToList();

        var newEntities = new List<EntityViewModel>
        {
            NewEntity("N0"),
            NewEntity("N1"),
            NewEntity("N2"),
        };

        AutoLayoutService.LayoutAppend(
            fixedEntities,
            newEntities,
            new List<RelationshipViewModel>()
        );

        // 既存エンティティは 1px も動かない
        for (var i = 0; i < fixedEntities.Count; i++)
        {
            fixedEntities[i].X.Should().Be(fixedSnapshot[i].X);
            fixedEntities[i].Y.Should().Be(fixedSnapshot[i].Y);
        }

        // 新規は原点（0,0）に積まれない
        newEntities.Should().OnlyContain(e => e.X != 0 || e.Y != 0);

        // 全エンティティ（既存＋新規）の矩形が 1 組も重ならない
        var all = fixedEntities.Concat(newEntities).ToList();

        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                Overlaps(all[i], all[j])
                    .Should()
                    .BeFalse($"{all[i].TableName} と {all[j].TableName} が重なってはいけない");
            }
        }
    }

    /// <summary>固定と接続する新規は接続先に近い列へ寄る（バリセンタ寄せ）</summary>
    [Fact(DisplayName = "LayoutAppend: 固定と接続する新規が接続先に近い列へ寄る")]
    public void LayoutAppend_MovesConnectedNewEntitiesNearNeighbor()
    {
        // 横に広い固定群（下配置が選ばれ、列＝X 方向のバリセンタ寄せが効く構成）
        var left = FixedEntity("Left", 40, 40);
        var right = FixedEntity("Right", 1040, 40);
        var fixedEntities = new List<EntityViewModel> { left, right };

        // 2 つの新規をそれぞれ左端・右端の固定へ接続する
        var nearLeft = NewEntity("NearLeft");
        var nearRight = NewEntity("NearRight");
        var newEntities = new List<EntityViewModel> { nearLeft, nearRight };
        var rels = new List<RelationshipViewModel> { Rel(nearRight, right), Rel(nearLeft, left) };

        AutoLayoutService.LayoutAppend(fixedEntities, newEntities, rels);

        // 左の固定へ繋がる新規は、右の固定へ繋がる新規より左の列へ配置される
        nearLeft.X.Should().BeLessThan(nearRight.X);
    }

    /// <summary>同じ入力に対して常に同じ配置となる（決定的）</summary>
    [Fact(DisplayName = "LayoutAppend: 結果が決定的である")]
    public void LayoutAppend_IsDeterministic()
    {
        static (
            List<EntityViewModel> Fixed,
            List<EntityViewModel> New,
            List<RelationshipViewModel> Rels
        ) Build()
        {
            var f = new List<EntityViewModel>
            {
                FixedEntity("A", 40, 40),
                FixedEntity("B", 340, 40),
                FixedEntity("C", 640, 40),
            };
            var n = new List<EntityViewModel>
            {
                NewEntity("N0"),
                NewEntity("N1"),
                NewEntity("N2"),
                NewEntity("N3"),
            };
            var r = new List<RelationshipViewModel> { Rel(n[0], f[2]), Rel(n[1], f[0]) };
            return (f, n, r);
        }

        var (f1, n1, r1) = Build();
        var (f2, n2, r2) = Build();

        AutoLayoutService.LayoutAppend(f1, n1, r1);
        AutoLayoutService.LayoutAppend(f2, n2, r2);

        for (var i = 0; i < n1.Count; i++)
        {
            n2[i].X.Should().Be(n1[i].X);
            n2[i].Y.Should().Be(n1[i].Y);
        }
    }

    /// <summary>
    /// 代表シナリオ（自動整列済みの 6 エンティティ＋リレーション数本の図へ、リレーション付き 3 エンティティを
    /// 欠落として追加）で配置後の品質指標を実測し、異常値（矩形重なり・極端な面積膨張）をアサートで固定する。
    /// </summary>
    [Fact(DisplayName = "LayoutAppend: 代表シナリオの配置品質を実測する")]
    public void LayoutAppend_MeasuresLayoutQuality()
    {
        // 既存 6 エンティティ＋リレーション（星＋鎖）を LayoutForceDirected で自動整列 → これを固定群とする
        var f = Enumerable.Range(0, 6).Select(i => NewEntity($"F{i}")).ToList();
        var fixedRels = new List<RelationshipViewModel>
        {
            Rel(f[0], f[1]),
            Rel(f[0], f[2]),
            Rel(f[0], f[3]),
            Rel(f[3], f[4]),
            Rel(f[4], f[5]),
        };
        AutoLayoutService.LayoutForceDirected(f, fixedRels);

        // リレーション付き 3 エンティティを欠落（新規）として追加 各々が既存の別ノードへ接続する
        var n = Enumerable.Range(0, 3).Select(i => NewEntity($"N{i}")).ToList();

        foreach (var e in n)
        {
            e.AutoFitWidth();
        }

        var newRels = new List<RelationshipViewModel>
        {
            Rel(n[0], f[1]),
            Rel(n[1], f[2]),
            Rel(n[2], f[5]),
            Rel(n[0], n[1]),
        };
        var allRels = fixedRels.Concat(newRels).ToList();

        AutoLayoutService.LayoutAppend(f, n, allRels);

        var all = f.Concat(n).ToList();

        // (a) 全体バウンディングボックス面積
        var minX = all.Min(e => e.X);
        var minY = all.Min(e => e.Y);
        var maxX = all.Max(e => e.X + e.Width);
        var maxY = all.Max(e => e.Y + e.DisplayHeight);
        var bboxArea = (maxX - minX) * (maxY - minY);

        // (b) 新規のリレーション線が既存エンティティ矩形を貫通する数
        var lineThroughFixed = 0;

        foreach (var rel in newRels)
        {
            var p = Center(rel.Source);
            var q = Center(rel.Target);

            foreach (var fe in f)
            {
                // 線の端点であるエンティティ自身は除外する
                if (ReferenceEquals(fe, rel.Source) || ReferenceEquals(fe, rel.Target))
                {
                    continue;
                }

                if (SegmentIntersectsRect(p, q, fe))
                {
                    lineThroughFixed++;
                }
            }
        }

        // (c) リレーション線同士の交差数（全リレーション）
        var lineCrossings = LayoutGeometry.CountCrossings(allRels);

        _output.WriteLine("=== LayoutAppend 品質実測（6 固定＋3 新規） ===");
        _output.WriteLine(
            $"(a) 全体バウンディングボックス面積: {bboxArea:N0} px^2 "
                + $"({maxX - minX:N0} x {maxY - minY:N0})"
        );
        _output.WriteLine($"(b) 新規リレーション線が既存矩形を貫通する数: {lineThroughFixed}");
        _output.WriteLine($"(c) リレーション線同士の交差数: {lineCrossings}");

        // 矩形重なりゼロ（新規同士・新規と既存）
        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                Overlaps(all[i], all[j])
                    .Should()
                    .BeFalse($"{all[i].TableName} と {all[j].TableName} が重なってはいけない");
            }
        }

        // 極端な面積膨張の固定: 実測は約 45 万 px^2 程度 退行検知のため十分な上限を置く
        bboxArea.Should().BeLessThan(4_000_000);

        // 下配置では新規→固定の線が固定クラスタ上を通るため若干の貫通・交差は生じる（単純戦略のトレードオフ）。
        // 「すべてが交差する」ような破綻を退行検知するため、リレーション数を基準にした緩い上限で固定する。
        lineThroughFixed.Should().BeLessThanOrEqualTo(newRels.Count);
        lineCrossings.Should().BeLessThanOrEqualTo(allRels.Count);
    }

    /// <summary>線分が矩形（エンティティの外接矩形）と交差または内包されるか判定する</summary>
    private static bool SegmentIntersectsRect(
        (double X, double Y) p,
        (double X, double Y) q,
        EntityViewModel rect
    )
    {
        var x0 = rect.X;
        var y0 = rect.Y;
        var x1 = rect.X + rect.Width;
        var y1 = rect.Y + rect.DisplayHeight;

        // どちらかの端点が矩形内部にあれば交差
        if (Inside(p) || Inside(q))
        {
            return true;
        }

        // 矩形の 4 辺のいずれかと線分が交差すれば交差
        var tl = (x0, y0);
        var tr = (x1, y0);
        var br = (x1, y1);
        var bl = (x0, y1);

        return LayoutGeometry.SegmentsCross(p, q, tl, tr)
            || LayoutGeometry.SegmentsCross(p, q, tr, br)
            || LayoutGeometry.SegmentsCross(p, q, br, bl)
            || LayoutGeometry.SegmentsCross(p, q, bl, tl);

        bool Inside((double X, double Y) pt) =>
            pt.X >= x0 && pt.X <= x1 && pt.Y >= y0 && pt.Y <= y1;
    }
}
