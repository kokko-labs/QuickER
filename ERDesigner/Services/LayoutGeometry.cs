using System.Collections.Generic;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>レイアウト計算に使う幾何ユーティリティ</summary>
public static class LayoutGeometry
{
    /// <summary>2 点間のユークリッド距離</summary>
    public static double Distance((double X, double Y) p, (double X, double Y) q)
    {
        var dx = p.X - q.X;
        var dy = p.Y - q.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>点と線分の最短距離</summary>
    public static double DistancePointToSegment(
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
    public static bool SegmentsCross(
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
    public static double Orientation((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>エンティティ中心を結ぶ線分同士が内部で交差するリレーションのペア数を数える</summary>
    public static int CountCrossings(IList<RelationshipViewModel> relationships)
    {
        static (double X, double Y) Center(EntityViewModel e) =>
            (e.X + e.Width / 2, e.Y + e.DisplayHeight / 2);

        var crossings = 0;

        for (var i = 0; i < relationships.Count; i++)
        {
            for (var j = i + 1; j < relationships.Count; j++)
            {
                var a = Center(relationships[i].Source);
                var b = Center(relationships[i].Target);
                var c = Center(relationships[j].Source);
                var d = Center(relationships[j].Target);

                if (SegmentsCross(a, b, c, d))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }
}
