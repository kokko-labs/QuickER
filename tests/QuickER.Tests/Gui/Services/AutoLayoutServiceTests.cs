using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="AutoLayoutService"/> の格子・階層レイアウトを検証するテストクラス</summary>
public class AutoLayoutServiceTests
{
    /// <summary>配置前と判別できるよう負座標で初期化したテスト用エンティティを生成する</summary>
    private static EntityViewModel NewEntity(string name = "E") =>
        new(new Entity { TableName = name }, new EntityLayout { X = -999, Y = -999 });

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
    private static RelationshipViewModel NewRelationship(
        EntityViewModel source,
        EntityViewModel target
    ) =>
        new(
            new Relationship { SourceEntityId = source.Id, TargetEntityId = target.Id },
            source,
            target
        );

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

    /// <summary>力学モデル配置が、各エンティティの座標を 1 回だけ代入することを検証する</summary>
    /// <remarks>
    /// 途中の左上座標を代入してから平行移動すると座標の変更通知が 2 倍になり、線・ミニマップ・キャンバスの
    /// 大きさの再計算もそのたびに走る。結果の座標は同じなので、座標の表明では区別できない。
    /// </remarks>
    [Fact(DisplayName = "LayoutForceDirected: 各エンティティの X・Y の変更通知は 1 回だけ")]
    public void LayoutForceDirected_AssignsEachCoordinateOnce()
    {
        var a = NewEntity("A");
        var b = NewEntity("B");
        var c = NewEntity("C");
        var entities = new List<EntityViewModel> { a, b, c };
        var rels = new List<RelationshipViewModel> { NewRelationship(a, b), NewRelationship(b, c) };
        var notifications = new Dictionary<(EntityViewModel, string), int>();

        foreach (var entity in entities)
        {
            entity.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(EntityViewModel.X) or nameof(EntityViewModel.Y))
                {
                    var key = (entity, e.PropertyName);
                    notifications[key] = notifications.GetValueOrDefault(key) + 1;
                }
            };
        }

        AutoLayoutService.LayoutForceDirected(entities, rels);

        notifications.Should().NotBeEmpty("配置で座標が動く");
        notifications.Values.Should().OnlyContain(count => count == 1);
    }

    /// <summary>力学モデル配置で空コレクションを渡しても例外が発生しないことを検証する</summary>
    [Fact(DisplayName = "LayoutForceDirected: 空コレクションでも例外にならない")]
    public void LayoutForceDirected_Empty_DoesNotThrow()
    {
        var act = () =>
            AutoLayoutService.LayoutForceDirected(
                new List<EntityViewModel>(),
                new List<RelationshipViewModel>()
            );
        act.Should().NotThrow();
    }

    /// <summary>リレーションが無い場合は従来格子（LayoutGrid）と全座標一致することを検証する</summary>
    [Fact(DisplayName = "LayoutForceDirected: リレーションなしなら従来格子と一致する")]
    public void LayoutForceDirected_WithoutRelationships_MatchesGrid()
    {
        var force = Enumerable.Range(0, 5).Select(i => NewEntity($"E{i}")).ToList();
        var grid = Enumerable.Range(0, 5).Select(i => NewEntity($"E{i}")).ToList();

        AutoLayoutService.LayoutForceDirected(force, new List<RelationshipViewModel>());
        AutoLayoutService.LayoutGrid(grid);

        for (var i = 0; i < force.Count; i++)
        {
            force[i].X.Should().Be(grid[i].X);
            force[i].Y.Should().Be(grid[i].Y);
        }
    }

    /// <summary>説明表示時は説明を含む表示高さを使って行間が確保されることを検証する</summary>
    [Fact(DisplayName = "LayoutGrid: 説明表示時は説明込みの高さで整列される")]
    public void LayoutGrid_UsesDisplayHeightWhenDescriptionsAreVisible()
    {
        var first = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
                Description =
                    "テーブル説明が複数行になるように十分長い文字列です。テーブル説明が複数行になるように十分長い文字列です。",
                Columns =
                {
                    new Column
                    {
                        Name = "CustomerName",
                        DataType = "nvarchar(100)",
                        Description =
                            "カラム説明も折り返されるように十分長い文字列を設定しています。",
                    },
                },
            },
            new EntityLayout { Width = 220 }
        );
        first.ShowDescriptionsInDiagram = true;

        var second = NewEntity("Customers");
        second.ShowDescriptionsInDiagram = true;

        var list = new List<EntityViewModel> { first, second };

        AutoLayoutService.LayoutGrid(list, columns: 1);

        second.Y.Should().BeGreaterThan(first.Y + first.DisplayHeight);
    }
}
