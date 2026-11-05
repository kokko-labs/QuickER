using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 自動整列の行：格子・階層・力学モデルの配置の指標（交差・軸整列・占有面積・孤立表の近さ・正規化）。
/// 「決定的」「矩形が重ならない」「全件配置」は共通の性質が同じ入力で受けるので、行に指標を書かない
/// </summary>
internal static partial class AutoLayoutScenarioRows
{
    /// <summary>表の左上座標の最小値（<c>AutoLayoutService</c> の Margin）</summary>
    private const double Margin = 40;

    /// <summary>格子・階層・力学モデルの配置の行</summary>
    private static IEnumerable<LayoutRow> ServiceRows()
    {
        // 格子（リレーション考慮）
        yield return Row(
                "grid-removes-crossings-crossing-diagonals-4",
                "LayoutGrid(リレーション考慮): 交差するリレーションが解消される"
            )
            .Shape("crossing-diagonals-4", LayoutKind.Grid, columns: 2)
            .Metric("交差 0", diagram => ExpectCrossings(diagram, 0));

        yield return Row(
                "grid-places-all-without-overlap-custom-7",
                "LayoutGrid(リレーション考慮): 全エンティティが重複なく配置される"
            )
            .Custom(
                "7 表（0-6, 1-5, 2-4, 0-3）",
                LayoutKind.Grid,
                () => BuildEdges(7, [(0, 6), (1, 5), (2, 4), (0, 3)]),
                columns: 3
            );

        yield return Row(
                "grid-is-deterministic-custom-6",
                "LayoutGrid(リレーション考慮): 結果が決定的である"
            )
            .Custom(
                "6 表（0-5, 1-4, 2-3）",
                LayoutKind.Grid,
                () => BuildEdges(6, [(0, 5), (1, 4), (2, 3)]),
                columns: 2
            );

        yield return Row(
                "grid-reduces-crossings-vs-legacy-custom-12",
                "LayoutGrid(リレーション考慮): 従来配置より交差が減る"
            )
            .Custom("12 表（i→(i+5)%12 のリング）", LayoutKind.Grid, BuildStep5Ring, columns: 4)
            .Metric(
                "関係なしの LayoutGrid（4 列）より交差が少ない",
                diagram => FewerCrossingsThanLegacyGrid(diagram, 4)
            );

        // 階層
        yield return Row(
                "tree-reorders-levels-removes-crossings-custom-7",
                "LayoutTree: 階層内の並べ替えでリレーション線の交差が解消される"
            )
            .Custom(
                "7 表（R→A,B,C,D・A→X,Y・B→X）",
                LayoutKind.Tree,
                () => BuildEdges(7, [(0, 1), (0, 2), (0, 3), (0, 4), (1, 5), (1, 6), (2, 5)])
            )
            .Metric("交差 0", diagram => ExpectCrossings(diagram, 0));

        // 力学モデル
        yield return Row(
                "force-aligns-independent-pairs-independent-pairs-4",
                "LayoutForceDirected: 独立ペアのリレーション線が軸へ整列する"
            )
            .Shape("independent-pairs-4", LayoutKind.Force)
            .Metric(
                "すべての線の軸からのずれの比が 0.1 未満",
                diagram =>
                {
                    var bad = diagram
                        .Relationships.Where(rel => LayoutMetrics.AxisDeviation(rel) >= 0.1)
                        .Select(rel =>
                            $"{rel.Source.TableName}-{rel.Target.TableName}={LayoutMetrics.AxisDeviation(rel):F3}"
                        )
                        .ToList();

                    return bad.Count == 0 ? null : $"軸からずれた線: {string.Join(", ", bad)}";
                }
            );

        yield return Row(
                "force-aligns-majority-of-ring-edges-ring-8",
                "LayoutForceDirected: リング構成でも線の過半数が軸へ整列する"
            )
            .Shape("ring-8", LayoutKind.Force)
            .Metric(
                "軸からのずれの比が 0.25 未満の線が過半数",
                diagram =>
                {
                    var aligned = diagram.Relationships.Count(rel =>
                        LayoutMetrics.AxisDeviation(rel) < 0.25
                    );

                    return aligned > diagram.Relationships.Count / 2
                        ? null
                        : $"整列した線 {aligned} / {diagram.Relationships.Count}";
                }
            );

        yield return Row(
                "force-reduces-crossings-vs-grid-custom-12",
                "LayoutForceDirected: 従来格子配置より交差が減る"
            )
            .Custom("12 表（i→(i+5)%12 のリング）", LayoutKind.Force, BuildStep5Ring)
            .Metric(
                "関係なしの LayoutGrid（4 列）より交差が少ない",
                diagram => FewerCrossingsThanLegacyGrid(diagram, 4)
            );

        yield return Row(
                "force-produces-compact-layout-ring-8",
                "LayoutForceDirected: 配置がコンパクトに収まる"
            )
            .Shape("ring-8", LayoutKind.Force)
            .Metric(
                "外接矩形の面積が 400,000 未満",
                diagram =>
                {
                    var area = LayoutMetrics.BoundingArea(diagram.Entities);

                    return area < 400_000 ? null : $"面積 {area:N0}";
                }
            );

        yield return Row(
                "force-packs-isolated-entities-star-with-isolated-8",
                "LayoutForceDirected: 孤立エンティティもコンパクトに配置される"
            )
            .Shape("star-with-isolated-8", LayoutKind.Force)
            .Metric(
                "孤立の表（E5〜E7）の最寄りの表までの中心間距離が 360 未満",
                diagram =>
                {
                    var bad = diagram
                        .Entities.Skip(5)
                        .Select(entity =>
                            (
                                entity.TableName,
                                Distance: LayoutMetrics.NearestCenterDistance(
                                    entity,
                                    diagram.Entities
                                )
                            )
                        )
                        .Where(item => item.Distance >= 360)
                        .Select(item => $"{item.TableName}={item.Distance:F1}")
                        .ToList();

                    return bad.Count == 0
                        ? null
                        : $"取り残された孤立の表: {string.Join(", ", bad)}";
                }
            );

        yield return Row(
                "force-normalizes-to-margin-ring-8",
                "LayoutForceDirected: 座標が Margin 以上へ正規化される"
            )
            .Shape("ring-8", LayoutKind.Force)
            .Metric(
                "全表の X・Y が Margin（40）以上",
                diagram =>
                {
                    var bad = diagram
                        .Entities.Where(e => e.X < Margin - 1e-6 || e.Y < Margin - 1e-6)
                        .Select(e => $"{e.TableName} ({e.X}, {e.Y})")
                        .ToList();

                    return bad.Count == 0 ? null : $"Margin 未満: {string.Join(", ", bad)}";
                }
            );
    }

    /// <summary>表の数とリレーション（表の番号の組）から、元のテストと同じ初期座標（負）の図を作る</summary>
    private static LayoutDiagram BuildEdges(int count, (int From, int To)[] edges)
    {
        var entities = Enumerable
            .Range(0, count)
            .Select(i => LayoutShapes.NewEntity($"E{i}"))
            .ToList();
        var relationships = edges
            .Select(edge => LayoutShapes.NewRelationship(entities[edge.From], entities[edge.To]))
            .ToList();

        return new LayoutDiagram(entities, relationships, 0);
    }

    /// <summary>12 表の i→(i+5)%12 のリング</summary>
    private static LayoutDiagram BuildStep5Ring() =>
        BuildEdges(12, [.. Enumerable.Range(0, 12).Select(i => (i, (i + 5) % 12))]);

    /// <summary>交差の数が期待と等しいか</summary>
    private static string? ExpectCrossings(LayoutDiagram diagram, int expected)
    {
        var crossings = LayoutMetrics.Crossings(diagram.Relationships);

        return crossings == expected ? null : $"交差 {crossings}（期待 {expected}）";
    }

    /// <summary>同じ図をリレーションなしの格子（従来配置）で並べたときより、交差が厳密に少ないか</summary>
    private static string? FewerCrossingsThanLegacyGrid(LayoutDiagram diagram, int columns)
    {
        var legacy = BuildStep5Ring();
        AutoLayoutService.LayoutGrid(legacy.Entities, columns);

        var actual = LayoutMetrics.Crossings(diagram.Relationships);
        var baseline = LayoutMetrics.Crossings(legacy.Relationships);

        return actual < baseline ? null : $"交差 {actual}（従来配置 {baseline} より少ないはず）";
    }
}
