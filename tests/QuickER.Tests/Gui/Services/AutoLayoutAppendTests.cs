using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using Xunit;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// <see cref="AutoLayoutService.LayoutAppend"/>（部分欠落レイアウトの追記配置）を検証するテストクラス。
/// 固定の表へ近い列へ寄せる配置を検証する（不動・重ならない・決定的・配置品質は AutoLayoutScenarioRows.Append の行）。
/// </summary>
public class AutoLayoutAppendTests
{
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
}
