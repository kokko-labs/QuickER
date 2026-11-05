using System.Text;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;
using Xunit.Sdk;

namespace QuickER.Tests.Gui.Services;

/// <summary>整列の種類（製品の軸は列挙でなく <see cref="AutoLayoutService"/> の公開の静的メソッド 4 つ）</summary>
internal enum LayoutKind
{
    /// <summary><see cref="AutoLayoutService.LayoutGrid(IList{EntityViewModel}, IList{RelationshipViewModel}, int)"/>（リレーション考慮）</summary>
    Grid,

    /// <summary><see cref="AutoLayoutService.LayoutTree"/></summary>
    Tree,

    /// <summary><see cref="AutoLayoutService.LayoutForceDirected"/></summary>
    Force,

    /// <summary><see cref="AutoLayoutService.LayoutAppend"/>（固定の表は動かさず、新しい表だけを置く）</summary>
    Append,
}

/// <summary>整列する図（表・リレーションと、追記で動かさない固定の表）</summary>
/// <param name="Entities">すべての表（追記では固定の表が先頭・新しい表が後ろ）</param>
/// <param name="Relationships">リレーション</param>
/// <param name="FixedCount">追記で動かさない固定の表の数（先頭から）。追記以外の整列では使わない</param>
internal sealed record LayoutDiagram(
    List<EntityViewModel> Entities,
    List<RelationshipViewModel> Relationships,
    int FixedCount
)
{
    /// <summary>固定の表</summary>
    public List<EntityViewModel> Fixed => Entities.Take(FixedCount).ToList();

    /// <summary>追記で置く新しい表</summary>
    public List<EntityViewModel> New => Entities.Skip(FixedCount).ToList();
}

/// <summary>追記が新しい表を置いた場所（製品の分岐：固定の群の下か右か。占有面積の小さい側を選ぶ）</summary>
internal enum AppendPlacement
{
    /// <summary>新しい表が無い（何もしない）</summary>
    Nothing,

    /// <summary>固定の群の右</summary>
    Right,

    /// <summary>固定の群の下</summary>
    Below,
}

/// <summary>図の形（形の名前 → 図のビルダ。1 形 1 か所）</summary>
/// <remarks>
/// 表は配置の前と見分けられるよう負の座標（-999）で作る。追記の図は、先頭の半分（切り上げ）を固定の表として
/// <see cref="AutoLayoutService.LayoutGrid(IList{EntityViewModel}, int)"/> で先に置き、残りを原点に積んだ新しい表にする。
/// 固定の群の外形を変える形は <see cref="AppendFixedLayouts"/> に固定の表の数と列の数を持つ。
/// 追記が選ぶ置き場所は形ごとに <see cref="AppendPlacementOf"/> で宣言し、全組のテストが実際の配置と照合する。
/// </remarks>
internal static class LayoutShapes
{
    /// <summary>形の名前と、表の数・リレーションの組（表の番号の組）</summary>
    public static IReadOnlyDictionary<
        string,
        (int Count, (int From, int To)[] Edges)
    > All { get; } =
        new Dictionary<string, (int, (int, int)[])>(StringComparer.Ordinal)
        {
            ["empty"] = (0, []),
            ["isolated-4"] = (4, []),
            ["chain-3"] = (3, [(0, 1), (1, 2)]),
            ["star-5"] = (5, [(0, 1), (0, 2), (0, 3), (0, 4)]),
            ["multi-parent-dag-7"] = (7, [(0, 1), (0, 2), (1, 3), (2, 3), (1, 4), (2, 5), (3, 6)]),
            ["independent-pairs-4"] = (8, [(0, 1), (2, 3), (4, 5), (6, 7)]),
            ["ring-8"] = (8, Ring(8, 1)),
            ["ring-chords-12"] = (12, [.. Ring(12, 1), .. Ring(12, 5)]),
            ["crossing-diagonals-4"] = (4, [(0, 3), (1, 2)]),
            ["star-with-isolated-8"] = (8, [(0, 1), (0, 2), (0, 3), (0, 4)]),
            ["random-50"] = (50, Random(50, 60, seed: 7)),
            ["tall-fixed-chain-5"] = (5, [(0, 1), (1, 2), (2, 3), (3, 4)]),
        };

    /// <summary>追記で固定の群の外形を変える形（固定の表の数・固定の表を並べる列の数）</summary>
    /// <remarks>
    /// tall-fixed-chain-5 は固定の 4 表を 1 列に縦へ並べ、新しい表 1 つ＝下へ置く方が占有面積が小さい。固定の群の高さが
    /// 間隔（GapY）より高いので、下へ置く分岐が固定の群の下端を基準にしていることまで重なりで見える（低い群では見えない）
    /// </remarks>
    public static IReadOnlyDictionary<
        string,
        (int FixedCount, int FixedColumns)
    > AppendFixedLayouts { get; } =
        new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            ["tall-fixed-chain-5"] = (4, 1),
        };

    /// <summary>形ごとの、追記が選ぶ置き場所（宣言。全組のテストが実際の配置と照合する）</summary>
    public static AppendPlacement AppendPlacementOf(string shape) =>
        shape switch
        {
            "empty" => AppendPlacement.Nothing,
            "isolated-4" or "crossing-diagonals-4" or "ring-chords-12" or "tall-fixed-chain-5" =>
                AppendPlacement.Below,
            _ => AppendPlacement.Right,
        };

    /// <summary>追記の後の、新しい表の実際の置き場所（下＝どれも固定の群の下端より下・右＝どれも右端より右・どちらでもなければ null）</summary>
    public static AppendPlacement? ObservePlacement(LayoutDiagram diagram)
    {
        if (diagram.New.Count == 0)
        {
            return AppendPlacement.Nothing;
        }

        var fixedBottom = diagram.Fixed.Max(e => e.Y + e.DisplayHeight);
        var fixedRight = diagram.Fixed.Max(e => e.X + e.Width);

        if (diagram.New.All(e => e.Y >= fixedBottom))
        {
            return AppendPlacement.Below;
        }

        return diagram.New.All(e => e.X >= fixedRight) ? AppendPlacement.Right : null;
    }

    /// <summary>形の図を作る（追記なら先頭の半分を固定の表として先に置く）</summary>
    public static LayoutDiagram Build(string shape, LayoutKind kind)
    {
        var (count, edges) = All[shape];
        var entities = Enumerable.Range(0, count).Select(i => NewEntity($"E{i}")).ToList();
        var relationships = edges
            .Select(edge => NewRelationship(entities[edge.From], entities[edge.To]))
            .ToList();

        if (kind != LayoutKind.Append)
        {
            return new LayoutDiagram(entities, relationships, 0);
        }

        var (fixedCount, fixedColumns) = AppendFixedLayouts.TryGetValue(shape, out var layout)
            ? layout
            : ((count + 1) / 2, 0);
        AutoLayoutService.LayoutGrid(entities.Take(fixedCount).ToList(), fixedColumns);

        foreach (var entity in entities.Skip(fixedCount))
        {
            entity.X = 0;
            entity.Y = 0;
        }

        return new LayoutDiagram(entities, relationships, fixedCount);
    }

    /// <summary>配置前と判別できるよう負の座標で作る表</summary>
    public static EntityViewModel NewEntity(string name) =>
        new(new Entity { TableName = name }, new EntityLayout { X = -999, Y = -999 });

    /// <summary>2 つの表の間のリレーション</summary>
    public static RelationshipViewModel NewRelationship(
        EntityViewModel source,
        EntityViewModel target
    ) =>
        new(
            new Relationship { SourceEntityId = source.Id, TargetEntityId = target.Id },
            source,
            target
        );

    /// <summary>i → (i + step) % count のリング</summary>
    private static (int, int)[] Ring(int count, int step) =>
        Enumerable.Range(0, count).Select(i => (i, (i + step) % count)).ToArray();

    /// <summary>決まった種の乱数のリレーション（自己参照と重複を除く）</summary>
    private static (int, int)[] Random(int count, int edges, int seed)
    {
        var random = new Random(seed);
        var result = new HashSet<(int, int)>();

        while (result.Count < edges)
        {
            var from = random.Next(count);
            var to = random.Next(count);

            if (from != to && !result.Contains((to, from)))
            {
                result.Add((from, to));
            }
        }

        return result.OrderBy(edge => edge.Item1).ThenBy(edge => edge.Item2).ToArray();
    }
}

/// <summary>整列の品質の指標（重なり・交差・占有面積・軸整列・中心の距離。テストの写しをここ 1 か所へ寄せる）</summary>
internal static class LayoutMetrics
{
    /// <summary>表の中心</summary>
    public static (double X, double Y) Center(EntityViewModel entity) =>
        (entity.X + entity.Width / 2, entity.Y + entity.DisplayHeight / 2);

    /// <summary>2 つの表の矩形（Width × DisplayHeight・ギャップを足さない）が重なるか</summary>
    public static bool Overlaps(EntityViewModel a, EntityViewModel b) =>
        a.X < b.X + b.Width
        && b.X < a.X + a.Width
        && a.Y < b.Y + b.DisplayHeight
        && b.Y < a.Y + a.DisplayHeight;

    /// <summary>重なる表の組（表の名前）</summary>
    public static List<string> OverlappingPairs(IReadOnlyList<EntityViewModel> entities)
    {
        var pairs = new List<string>();

        for (var i = 0; i < entities.Count; i++)
        {
            for (var j = i + 1; j < entities.Count; j++)
            {
                if (Overlaps(entities[i], entities[j]))
                {
                    pairs.Add($"{entities[i].TableName}×{entities[j].TableName}");
                }
            }
        }

        return pairs;
    }

    /// <summary>リレーション線の交差の数（製品の <see cref="LayoutGeometry.CountCrossings"/>）</summary>
    public static int Crossings(IList<RelationshipViewModel> relationships) =>
        LayoutGeometry.CountCrossings(relationships);

    /// <summary>表の外接矩形の面積</summary>
    public static double BoundingArea(IReadOnlyList<EntityViewModel> entities) =>
        (entities.Max(e => e.X + e.Width) - entities.Min(e => e.X))
        * (entities.Max(e => e.Y + e.DisplayHeight) - entities.Min(e => e.Y));

    /// <summary>リレーション線の軸からのずれの比（短い方の差 ÷ 長い方の差。0＝水平か垂直）</summary>
    public static double AxisDeviation(RelationshipViewModel relationship)
    {
        var (sx, sy) = Center(relationship.Source);
        var (tx, ty) = Center(relationship.Target);
        var dx = Math.Abs(sx - tx);
        var dy = Math.Abs(sy - ty);

        return Math.Min(dx, dy) / Math.Max(Math.Max(dx, dy), 1e-9);
    }

    /// <summary>表の中心から、ほかの表の中心までの最短距離</summary>
    public static double NearestCenterDistance(
        EntityViewModel entity,
        IEnumerable<EntityViewModel> others
    )
    {
        var (x, y) = Center(entity);

        return others
            .Where(other => !ReferenceEquals(other, entity))
            .Min(other =>
            {
                var (ox, oy) = Center(other);

                return Math.Sqrt((x - ox) * (x - ox) + (y - oy) * (y - oy));
            });
    }
}

/// <summary>整列を当てる（整列の種類ごとの呼び分けはここ 1 か所）</summary>
internal static class LayoutRunner
{
    /// <summary>図へ整列を当てる</summary>
    public static void Apply(LayoutKind kind, LayoutDiagram diagram, int columns = 0)
    {
        switch (kind)
        {
            case LayoutKind.Grid:
                AutoLayoutService.LayoutGrid(diagram.Entities, diagram.Relationships, columns);
                break;
            case LayoutKind.Tree:
                AutoLayoutService.LayoutTree(diagram.Entities, diagram.Relationships);
                break;
            case LayoutKind.Force:
                AutoLayoutService.LayoutForceDirected(
                    diagram.Entities,
                    diagram.Relationships,
                    columns
                );
                break;
            case LayoutKind.Append:
                AutoLayoutService.LayoutAppend(diagram.Fixed, diagram.New, diagram.Relationships);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>
    /// 形と種類によらない共通の性質を表明する：例外を出さない・2 回流して座標が同じ（決定的）・表の矩形が重ならない・
    /// 置いた表は配置前の負の座標から動く（追記は新しい表）・追記は固定の表を動かさない
    /// </summary>
    /// <param name="label">失敗の文に出す名前</param>
    /// <param name="build">図を作る（2 回呼ぶ）</param>
    /// <param name="kind">整列の種類</param>
    /// <param name="columns">列の数（0＝自動）</param>
    /// <returns>1 回目に整列した図（行ごとの指標が読む）</returns>
    public static LayoutDiagram AssertCommonProperties(
        string label,
        Func<LayoutDiagram> build,
        LayoutKind kind,
        int columns = 0
    )
    {
        var first = build();
        var second = build();
        var fixedBefore = first.Fixed.Select(e => (e.X, e.Y)).ToList();
        var problems = new List<string>();

        try
        {
            Apply(kind, first, columns);
            Apply(kind, second, columns);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new XunitException($"{label}: 整列が例外を投げた: {ex}");
        }

        for (var i = 0; i < first.Entities.Count; i++)
        {
            if (
                first.Entities[i].X != second.Entities[i].X
                || first.Entities[i].Y != second.Entities[i].Y
            )
            {
                problems.Add(
                    $"決定的でない: {first.Entities[i].TableName} が ({first.Entities[i].X}, {first.Entities[i].Y}) と ({second.Entities[i].X}, {second.Entities[i].Y})"
                );
            }
        }

        foreach (var pair in LayoutMetrics.OverlappingPairs(first.Entities))
        {
            problems.Add($"矩形が重なる: {pair}");
        }

        var placed = kind == LayoutKind.Append ? first.New : first.Entities;

        foreach (var entity in placed.Where(entity => entity.X < 0 || entity.Y < 0))
        {
            problems.Add($"配置前の負の座標のまま: {entity.TableName} ({entity.X}, {entity.Y})");
        }

        if (kind == LayoutKind.Append)
        {
            var fixedAfter = first.Fixed.Select(e => (e.X, e.Y)).ToList();

            if (!fixedAfter.SequenceEqual(fixedBefore))
            {
                problems.Add("追記が固定の表を動かした");
            }
        }

        if (problems.Count > 0)
        {
            var message = new StringBuilder($"{label}: 共通の性質に合わない");

            foreach (var problem in problems)
            {
                message.Append(Environment.NewLine).Append("  ").Append(problem);
            }

            throw new XunitException(message.ToString());
        }

        return first;
    }
}

/// <summary>整列の行（元のテストの図・整列・指標と閾値）</summary>
/// <param name="Shape">図の形の名前（<see cref="LayoutShapes.All"/>）か、行だけの図の説明</param>
/// <param name="Kind">整列の種類</param>
/// <param name="Columns">列の数（0＝自動）</param>
/// <param name="Build">図を作る（行だけの図でなければ <see cref="LayoutShapes.Build"/>）</param>
/// <param name="Metrics">行ごとの指標の表明（説明と、整列した図から違反の理由を返す関数。合えば null）</param>
internal sealed record LayoutScenario(
    string Shape,
    LayoutKind Kind,
    int Columns,
    Func<LayoutDiagram> Build,
    IReadOnlyList<(string Description, Func<LayoutDiagram, string?> Verify)> Metrics
);

/// <summary>整列の行を組み立てる（<see cref="AutoLayoutScenarioRows"/> の 1 行）</summary>
/// <param name="id">行の Id（「{整列}-{守っていること}-{形}」の英小文字とハイフン）</param>
/// <param name="description">何を確かめるか（元のテストの DisplayName）</param>
internal sealed class LayoutRow(string id, string description)
{
    private readonly List<(string, Func<LayoutDiagram, string?>)> _metrics = [];
    private string? _shape;
    private Func<LayoutDiagram>? _build;
    private LayoutKind? _kind;
    private int _columns;

    /// <summary>形の名前の図で整列する</summary>
    public LayoutRow Shape(string shape, LayoutKind kind, int columns = 0)
    {
        _shape = shape;
        _kind = kind;
        _columns = columns;
        _build = () => LayoutShapes.Build(shape, kind);
        return this;
    }

    /// <summary>行だけの図で整列する（形の表に無い図。説明は網羅の計数の形の名前にしない）</summary>
    public LayoutRow Custom(
        string what,
        LayoutKind kind,
        Func<LayoutDiagram> build,
        int columns = 0
    )
    {
        _shape = what;
        _kind = kind;
        _columns = columns;
        _build = build;
        return this;
    }

    /// <summary>指標の表明（元のテストの閾値のまま）</summary>
    public LayoutRow Metric(string what, Func<LayoutDiagram, string?> verify)
    {
        _metrics.Add((what, verify));
        return this;
    }

    /// <summary>表の行にする</summary>
    public ScenarioRow<LayoutScenario> Build()
    {
        if (_shape is null || _kind is not { } kind || _build is null)
        {
            throw new InvalidOperationException($"行 {id} に図と整列が無い");
        }

        return new ScenarioRow<LayoutScenario>(
            id,
            null,
            description,
            new LayoutScenario(_shape, kind, _columns, _build, [.. _metrics])
        );
    }
}
