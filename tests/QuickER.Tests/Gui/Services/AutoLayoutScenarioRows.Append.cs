using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 自動整列の行：追記の配置（固定の表を動かさず新しい表だけを置く）。
/// 「決定的」「矩形が重ならない」「固定の表が動かない」は共通の性質が同じ入力で受ける
/// </summary>
internal static partial class AutoLayoutScenarioRows
{
    /// <summary>追記の行</summary>
    private static IEnumerable<LayoutRow> AppendRows()
    {
        yield return Row(
                "append-no-new-entities-does-nothing-custom-2",
                "LayoutAppend: 新規が空なら何もしない"
            )
            .Custom(
                "固定 2 表・新規なし",
                LayoutKind.Append,
                () =>
                    new LayoutDiagram(
                        [FixedEntity("A", 100, 100), FixedEntity("B", 400, 100)],
                        [],
                        2
                    )
            );

        yield return Row(
                "append-keeps-fixed-and-places-new-custom-3-3",
                "LayoutAppend: 既存は不動・新規は原点に積まず重ならない"
            )
            .Custom(
                "固定 3 表（40,40 / 340,40 / 40,240）・新規 3 表（原点）",
                LayoutKind.Append,
                () =>
                    new LayoutDiagram(
                        [
                            FixedEntity("A", 40, 40),
                            FixedEntity("B", 340, 40),
                            FixedEntity("C", 40, 240),
                            OriginEntity("N0"),
                            OriginEntity("N1"),
                            OriginEntity("N2"),
                        ],
                        [],
                        3
                    )
            )
            .Metric(
                "新規は原点（0,0）に積まれない",
                diagram =>
                {
                    var stacked = diagram
                        .New.Where(e => e.X == 0 && e.Y == 0)
                        .Select(e => e.TableName)
                        .ToList();

                    return stacked.Count == 0
                        ? null
                        : $"原点に残った表: {string.Join(", ", stacked)}";
                }
            );

        yield return Row("append-is-deterministic-custom-3-4", "LayoutAppend: 結果が決定的である")
            .Custom(
                "固定 3 表（40,40 / 340,40 / 640,40）・新規 4 表・リレーション 2 本",
                LayoutKind.Append,
                () =>
                {
                    var entities = new List<EntityViewModel>
                    {
                        FixedEntity("A", 40, 40),
                        FixedEntity("B", 340, 40),
                        FixedEntity("C", 640, 40),
                        OriginEntity("N0"),
                        OriginEntity("N1"),
                        OriginEntity("N2"),
                        OriginEntity("N3"),
                    };
                    var relationships = new List<RelationshipViewModel>
                    {
                        LayoutShapes.NewRelationship(entities[3], entities[2]),
                        LayoutShapes.NewRelationship(entities[4], entities[0]),
                    };

                    return new LayoutDiagram(entities, relationships, 3);
                }
            );

        yield return Row(
                "append-measures-layout-quality-custom-6-3",
                "LayoutAppend: 代表シナリオの配置品質を実測する"
            )
            .Custom(
                "固定 6 表（力学モデルで整列済み）・新規 3 表（リレーション付き）",
                LayoutKind.Append,
                BuildQualityScenario
            )
            .Metric(
                "外接矩形の面積が 4,000,000 未満",
                diagram =>
                {
                    var quality = MeasureAppendQuality(diagram);

                    return quality.BoundingArea < 4_000_000 ? null : quality.Describe();
                }
            )
            .Metric(
                "新規のリレーション線が固定の表を貫通する数が新規リレーション数以下",
                diagram =>
                {
                    var quality = MeasureAppendQuality(diagram);

                    return quality.LinesThroughFixed <= quality.NewRelationshipCount
                        ? null
                        : quality.Describe();
                }
            )
            .Metric(
                "リレーション線同士の交差がリレーション数以下",
                diagram =>
                {
                    var quality = MeasureAppendQuality(diagram);

                    return quality.Crossings <= diagram.Relationships.Count
                        ? null
                        : quality.Describe();
                }
            );
    }

    /// <summary>座標を持つ固定の表（元のテストと同じく幅 200）</summary>
    private static EntityViewModel FixedEntity(string name, double x, double y) =>
        new(
            new Entity { TableName = name },
            new EntityLayout
            {
                X = x,
                Y = y,
                Width = 200,
            }
        );

    /// <summary>配置前と判別できるよう原点で作る表</summary>
    private static EntityViewModel OriginEntity(string name) =>
        new(new Entity { TableName = name }, new EntityLayout { X = 0, Y = 0 });

    /// <summary>代表シナリオ：星＋鎖の 6 表を力学モデルで整列して固定にし、リレーション付きの 3 表を新しい表にする</summary>
    private static LayoutDiagram BuildQualityScenario()
    {
        var f = Enumerable.Range(0, 6).Select(i => OriginEntity($"F{i}")).ToList();
        var fixedRels = new List<RelationshipViewModel>
        {
            LayoutShapes.NewRelationship(f[0], f[1]),
            LayoutShapes.NewRelationship(f[0], f[2]),
            LayoutShapes.NewRelationship(f[0], f[3]),
            LayoutShapes.NewRelationship(f[3], f[4]),
            LayoutShapes.NewRelationship(f[4], f[5]),
        };
        AutoLayoutService.LayoutForceDirected(f, fixedRels);

        var n = Enumerable.Range(0, 3).Select(i => OriginEntity($"N{i}")).ToList();

        foreach (var e in n)
        {
            e.AutoFitWidth();
        }

        var newRels = new List<RelationshipViewModel>
        {
            LayoutShapes.NewRelationship(n[0], f[1]),
            LayoutShapes.NewRelationship(n[1], f[2]),
            LayoutShapes.NewRelationship(n[2], f[5]),
            LayoutShapes.NewRelationship(n[0], n[1]),
        };

        return new LayoutDiagram([.. f, .. n], [.. fixedRels, .. newRels], 6);
    }

    /// <summary>追記の配置品質の実測値</summary>
    private sealed record AppendQuality(
        double BoundingArea,
        double Width,
        double Height,
        int LinesThroughFixed,
        int NewRelationshipCount,
        int Crossings
    )
    {
        /// <summary>失敗の文に載せる実測値</summary>
        public string Describe() =>
            $"実測: 外接矩形 {BoundingArea:N0} px^2 ({Width:N0} x {Height:N0})、新規の線が固定の表を貫通 {LinesThroughFixed}（新規リレーション {NewRelationshipCount} 本）、線同士の交差 {Crossings}";
    }

    /// <summary>代表シナリオの整列後の図から、面積・貫通数・交差数を測る（新規のリレーション＝固定どうしの 5 本より後ろ）</summary>
    private static AppendQuality MeasureAppendQuality(LayoutDiagram diagram)
    {
        const int fixedRelationshipCount = 5;
        var newRels = diagram.Relationships.Skip(fixedRelationshipCount).ToList();
        var all = diagram.Entities;
        var width = all.Max(e => e.X + e.Width) - all.Min(e => e.X);
        var height = all.Max(e => e.Y + e.DisplayHeight) - all.Min(e => e.Y);
        var through = 0;

        foreach (var rel in newRels)
        {
            var p = LayoutMetrics.Center(rel.Source);
            var q = LayoutMetrics.Center(rel.Target);

            foreach (var fixedEntity in diagram.Fixed)
            {
                // 線の端点である表自身は除く
                if (
                    ReferenceEquals(fixedEntity, rel.Source)
                    || ReferenceEquals(fixedEntity, rel.Target)
                )
                {
                    continue;
                }

                if (SegmentIntersectsRect(p, q, fixedEntity))
                {
                    through++;
                }
            }
        }

        return new AppendQuality(
            width * height,
            width,
            height,
            through,
            newRels.Count,
            LayoutMetrics.Crossings(diagram.Relationships)
        );
    }

    /// <summary>線分が表の外接矩形と交差または内包されるか</summary>
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
