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
