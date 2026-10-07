using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using AwesomeAssertions;
using QuickER.Behaviors;
using QuickER.Model;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// <see cref="RubberBandBehavior"/> が押下のたびに、どの押下からラバーバンドを始めるか（始めないか）を
/// 正しく決めることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 押下は選択面の子要素から <c>PreviewMouseDown</c> を発火させて流す（トンネルで選択面を通り、その途中で
/// WPF のクラスハンドラが各要素に <c>PreviewMouseLeftButtonDown</c> を発火させる。後者は直接ルートの
/// イベントなので、子要素で直接発火させても選択面へは届かない）。押下元は子要素になる＝製品と同じ判定の
/// 材料。押下時の判定は位置を使わないので、
/// 擬似のマウスイベントの位置が実際のカーソル位置になることは結果に影響しない。
/// </para>
/// <para>
/// 進行中状態は <c>[ThreadStatic]</c> なので、各テストは新しい STA スレッドで走らせる。
/// </para>
/// </remarks>
public class RubberBandBehaviorPressTests
{
    /// <summary>選択面と、その上のエンティティ要素・リレーションの線</summary>
    private sealed record Surface(Grid Canvas, Border Entity, Rectangle RelationshipLine);

    /// <summary>
    /// ビヘイビアを付けた選択面（DataContext は MainViewModel）に、エンティティ要素と、リレーションの項目
    /// （DataContext は RelationshipViewModel・中の線は DataContext を受け継ぐ）を載せる
    /// </summary>
    private static Surface CreateSurface()
    {
        var source = new EntityViewModel(new Entity { TableName = "A" });
        var target = new EntityViewModel(new Entity { TableName = "B" });
        var entity = new Border
        {
            Width = 100,
            Height = 50,
            DataContext = source,
        };
        var line = new Rectangle { Width = 100, Height = 14 };
        var relationshipItem = new Grid
        {
            DataContext = new RelationshipViewModel(
                new Relationship { SourceEntityId = source.Id, TargetEntityId = target.Id },
                source,
                target
            ),
        };
        relationshipItem.Children.Add(line);
        var canvas = new Grid { DataContext = new MainViewModel() };
        canvas.Children.Add(entity);
        canvas.Children.Add(relationshipItem);
        RubberBandBehavior.SetIsEnabled(canvas, true);
        return new Surface(canvas, entity, line);
    }

    /// <summary>指定要素の上で左ボタンを押す（押下元はその要素になる）</summary>
    private static void Press(UIElement element) =>
        element.RaiseEvent(
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent,
            }
        );

    [Fact(DisplayName = "空白の押下は保留を作り、エンティティの上の押下は作らない")]
    public void Press_OnBlankStartsPending_OnEntityDoesNot()
    {
        RunSta(() =>
        {
            var s = CreateSurface();

            Press(s.Canvas);
            RubberBandBehavior.IsSelectionActiveForTests.Should().BeTrue();
            RubberBandBehavior.CancelActiveSelection();

            Press(s.Entity);
            RubberBandBehavior.IsSelectionActiveForTests.Should().BeFalse();
        });
    }

    [Fact(DisplayName = "リレーションの線・ラベルの上の押下はラバーバンドの保留を作らない")]
    public void Press_OnRelationship_DoesNotStartPending()
    {
        RunSta(() =>
        {
            var s = CreateSurface();

            Press(s.RelationshipLine);

            // 保留を作ると、線を押して少し動かしただけでラバーバンドが始まり、その確定で
            // 押したリレーションの選択が外れる
            RubberBandBehavior.IsSelectionActiveForTests.Should().BeFalse();
        });
    }

    [Fact(DisplayName = "解放を受け取れなかった保留は、次のエンティティの上の押下で捨てる")]
    public void PressOnEntity_DiscardsStalePending()
    {
        RunSta(() =>
        {
            var s = CreateSurface();

            // 空白で押し、解放を受け取れないまま（面の外で離した等）次の押下が来る
            Press(s.Canvas);
            RubberBandBehavior.IsSelectionActiveForTests.Should().BeTrue();

            Press(s.Entity);

            // 残すと、エンティティのドラッグ中に古い起点から距離を測ってラバーバンドが始まり、
            // マウスのキャプチャを奪う
            RubberBandBehavior
                .IsSelectionActiveForTests.Should()
                .BeFalse("前の押下の保留はこの押下で捨てる");
        });
    }
}
