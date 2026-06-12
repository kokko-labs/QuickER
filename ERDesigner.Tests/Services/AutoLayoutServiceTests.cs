using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary><see cref="AutoLayoutService"/> の格子・階層レイアウトを検証するテストクラス</summary>
public class AutoLayoutServiceTests
{
    /// <summary>配置前と判別できるよう負座標で初期化したテスト用エンティティを生成する</summary>
    private static EntityViewModel NewEntity(string name = "E") =>
        new(
            new Entity
            {
                TableName = name,
                X = -999,
                Y = -999,
            }
        );

    /// <summary>格子レイアウトで同一行は同じ Y、次列は右、次行は下へ配置されることを検証する</summary>
    [Fact(DisplayName = "LayoutGrid: エンティティが格子状に並ぶ")]
    public void LayoutGrid_ArrangesInGrid()
    {
        var list = new List<EntityViewModel> { NewEntity(), NewEntity(), NewEntity(), NewEntity() };
        AutoLayoutService.LayoutGrid(list, columns: 2);

        list[0].X.Should().BeLessThan(list[1].X);
        list[0].Y.Should().Be(list[1].Y);
        list[2].Y.Should().BeGreaterThan(list[0].Y);
    }

    /// <summary>空コレクションを渡しても例外が発生しないことを検証する</summary>
    [Fact(DisplayName = "LayoutGrid: 空コレクションでも例外にならない")]
    public void LayoutGrid_Empty_DoesNotThrow()
    {
        var act = () => AutoLayoutService.LayoutGrid(new List<EntityViewModel>());
        act.Should().NotThrow();
    }

    /// <summary>2 エンティティ間のリレーションを表すテスト用 ViewModel を生成する</summary>
    private static RelationshipViewModel NewRelationship(EntityViewModel source, EntityViewModel target) =>
        new(new Relationship { SourceEntityId = source.Id, TargetEntityId = target.Id }, source, target);

    /// <summary>エンティティ中心を結ぶ 2 線分が内部で交差するリレーションのペア数を数える</summary>
    private static int CountCrossings(IList<RelationshipViewModel> rels)
    {
        static (double X, double Y) Center(EntityViewModel e) =>
            (e.X + e.Width / 2, e.Y + e.DisplayHeight / 2);

        static double Orient((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        var crossings = 0;

        for (var i = 0; i < rels.Count; i++)
        {
            for (var j = i + 1; j < rels.Count; j++)
            {
                var (a, b) = (Center(rels[i].Source), Center(rels[i].Target));
                var (c, d) = (Center(rels[j].Source), Center(rels[j].Target));

                var d1 = Orient(c, d, a);
                var d2 = Orient(c, d, b);
                var d3 = Orient(a, b, c);
                var d4 = Orient(a, b, d);

                if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    /// <summary>並び順のままだと対角線同士が交差する構成で、交差が解消されることを検証する</summary>
    [Fact(DisplayName = "LayoutGrid(リレーション考慮): 交差するリレーションが解消される")]
    public void LayoutGrid_WithRelationships_RemovesCrossings()
    {
        var a = NewEntity("A");
        var b = NewEntity("B");
        var c = NewEntity("C");
        var d = NewEntity("D");
        var entities = new List<EntityViewModel> { a, b, c, d };

        // 並び順のまま 2 列格子へ置くと A-D / B-C の対角線が交差する
        var rels = new List<RelationshipViewModel> { NewRelationship(a, d), NewRelationship(b, c) };

        AutoLayoutService.LayoutGrid(entities, rels, columns: 2);

        CountCrossings(rels).Should().Be(0);
    }

    /// <summary>リレーション考慮版でも全エンティティが重複なく格子上へ配置されることを検証する</summary>
    [Fact(DisplayName = "LayoutGrid(リレーション考慮): 全エンティティが重複なく配置される")]
    public void LayoutGrid_WithRelationships_PlacesAllEntitiesWithoutOverlap()
    {
        var entities = Enumerable.Range(0, 7).Select(i => NewEntity($"E{i}")).ToList();

        var rels = new List<RelationshipViewModel>
        {
            NewRelationship(entities[0], entities[6]),
            NewRelationship(entities[1], entities[5]),
            NewRelationship(entities[2], entities[4]),
            NewRelationship(entities[0], entities[3]),
        };

        AutoLayoutService.LayoutGrid(entities, rels, columns: 3);

        // 全件が配置済み（初期値の負座標から移動）かつ座標が重複しないこと
        entities.Should().OnlyContain(e => e.X >= 0 && e.Y >= 0);
        entities.Select(e => (e.X, e.Y)).Should().OnlyHaveUniqueItems();
    }

    /// <summary>同じ入力に対して常に同じ配置となる（乱数を使わない）ことを検証する</summary>
    [Fact(DisplayName = "LayoutGrid(リレーション考慮): 結果が決定的である")]
    public void LayoutGrid_WithRelationships_IsDeterministic()
    {
        static (List<EntityViewModel> Entities, List<RelationshipViewModel> Rels) Build()
        {
            var entities = Enumerable.Range(0, 6).Select(i => NewEntity($"E{i}")).ToList();

            var rels = new List<RelationshipViewModel>
            {
                NewRelationship(entities[0], entities[5]),
                NewRelationship(entities[1], entities[4]),
                NewRelationship(entities[2], entities[3]),
            };

            return (entities, rels);
        }

        var (first, firstRels) = Build();
        var (second, secondRels) = Build();

        AutoLayoutService.LayoutGrid(first, firstRels, columns: 2);
        AutoLayoutService.LayoutGrid(second, secondRels, columns: 2);

        for (var i = 0; i < first.Count; i++)
        {
            second[i].X.Should().Be(first[i].X);
            second[i].Y.Should().Be(first[i].Y);
        }
    }

    /// <summary>密に接続されたグラフで、従来の並び順配置より交差が少なくなることを検証する</summary>
    [Fact(DisplayName = "LayoutGrid(リレーション考慮): 従来配置より交差が減る")]
    public void LayoutGrid_WithRelationships_ReducesCrossingsComparedToLegacy()
    {
        // リング + 弦の構成を、接続と無関係な並び順で渡す（従来配置では多数の交差が生じる）
        static (List<EntityViewModel> Entities, List<RelationshipViewModel> Rels) Build()
        {
            var entities = Enumerable.Range(0, 12).Select(i => NewEntity($"E{i}")).ToList();
            var rels = new List<RelationshipViewModel>();

            for (var i = 0; i < 12; i++)
            {
                rels.Add(NewRelationship(entities[i], entities[(i + 5) % 12]));
            }

            return (entities, rels);
        }

        var (legacy, legacyRels) = Build();
        var (optimized, optimizedRels) = Build();

        AutoLayoutService.LayoutGrid(legacy, columns: 4);
        AutoLayoutService.LayoutGrid(optimized, optimizedRels, columns: 4);

        CountCrossings(optimizedRels).Should().BeLessThan(CountCrossings(legacyRels));
    }

    /// <summary>リレーションが無い場合は従来の並び順どおり配置されることを検証する</summary>
    [Fact(DisplayName = "LayoutGrid(リレーション考慮): リレーションなしなら従来配置と一致する")]
    public void LayoutGrid_WithoutRelationships_MatchesLegacyLayout()
    {
        var withRels = Enumerable.Range(0, 5).Select(i => NewEntity($"E{i}")).ToList();
        var legacy = Enumerable.Range(0, 5).Select(i => NewEntity($"E{i}")).ToList();

        AutoLayoutService.LayoutGrid(withRels, new List<RelationshipViewModel>(), columns: 2);
        AutoLayoutService.LayoutGrid(legacy, columns: 2);

        for (var i = 0; i < withRels.Count; i++)
        {
            withRels[i].X.Should().Be(legacy[i].X);
            withRels[i].Y.Should().Be(legacy[i].Y);
        }
    }

    /// <summary>階層レイアウトで最も次数の多いノードを起点に深さ順で縦配置されることを検証する</summary>
    [Fact(DisplayName = "LayoutTree: 接続されたエンティティが階層レイアウトされる")]
    public void LayoutTree_ArrangesByDepth()
    {
        var a = NewEntity("A");
        var b = NewEntity("B");
        var c = NewEntity("C");
        var entities = new List<EntityViewModel> { a, b, c };

        var rels = new List<RelationshipViewModel>
        {
            new(new Relationship { SourceEntityId = a.Id, TargetEntityId = b.Id }, a, b),
            new(new Relationship { SourceEntityId = b.Id, TargetEntityId = c.Id }, b, c),
        };

        AutoLayoutService.LayoutTree(entities, rels);

        // ルート(最も次数が多い b)が上、a,cがその下
        b.Y.Should().BeLessThan(a.Y);
        b.Y.Should().BeLessThan(c.Y);
    }

    /// <summary>BFS の発見順のままだと交差する多親構成で、バリセンタ法により交差が解消されることを検証する</summary>
    [Fact(DisplayName = "LayoutTree: 階層内の並べ替えでリレーション線の交差が解消される")]
    public void LayoutTree_ReordersLevels_RemovesCrossings()
    {
        var r = NewEntity("R");
        var a = NewEntity("A");
        var b = NewEntity("B");
        var c = NewEntity("C");
        var d = NewEntity("D");
        var x = NewEntity("X");
        var y = NewEntity("Y");
        var entities = new List<EntityViewModel> { r, a, b, c, d, x, y };

        // BFS では X, Y の順で発見されるが、その並びだと A-Y と B-X が交差する
        var rels = new List<RelationshipViewModel>
        {
            NewRelationship(r, a),
            NewRelationship(r, b),
            NewRelationship(r, c),
            NewRelationship(r, d),
            NewRelationship(a, x),
            NewRelationship(a, y),
            NewRelationship(b, x),
        };

        AutoLayoutService.LayoutTree(entities, rels);

        CountCrossings(rels).Should().Be(0);
    }

    /// <summary>ノード数の少ない階層が幅広の階層に対して中央寄せされることを検証する</summary>
    [Fact(DisplayName = "LayoutTree: 各階層が中央寄せされる")]
    public void LayoutTree_CentersEachLevel()
    {
        var r = NewEntity("R");
        var a = NewEntity("A");
        var b = NewEntity("B");
        var entities = new List<EntityViewModel> { r, a, b };

        var rels = new List<RelationshipViewModel> { NewRelationship(r, a), NewRelationship(r, b) };

        AutoLayoutService.LayoutTree(entities, rels);

        // ルート単独の階層は 2 ノードの子階層の中央へ寄り、左端の子より右に位置する
        r.X.Should().BeGreaterThan(a.X);
        r.X.Should().BeLessThan(b.X);
    }

    /// <summary>説明表示時は説明を含む表示高さを使って行間が確保されることを検証する</summary>
    [Fact(DisplayName = "LayoutGrid: 説明表示時は説明込みの高さで整列される")]
    public void LayoutGrid_UsesDisplayHeightWhenDescriptionsAreVisible()
    {
        var first = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
                Width = 220,
                Description = "テーブル説明が複数行になるように十分長い文字列です。テーブル説明が複数行になるように十分長い文字列です。",
                Columns =
                {
                    new Column
                    {
                        Name = "CustomerName",
                        DataType = "nvarchar(100)",
                        Description = "カラム説明も折り返されるように十分長い文字列を設定しています。",
                    },
                },
            }
        );
        first.ShowDescriptionsInDiagram = true;

        var second = NewEntity("Customers");
        second.ShowDescriptionsInDiagram = true;

        var list = new List<EntityViewModel> { first, second };

        AutoLayoutService.LayoutGrid(list, columns: 1);

        second.Y.Should().BeGreaterThan(first.Y + first.DisplayHeight);
    }
}
