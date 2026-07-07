using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FluentAssertions;
using QuickER.ViewModels;
using static QuickER.Tests.Views.WpfApplicationTestSupport;

namespace QuickER.Tests.Views;

/// <summary>
/// キャンバスサイズ（<see cref="MainViewModel.CanvasWidth"/> / <see cref="MainViewModel.CanvasHeight"/>）が
/// ビューポート実寸に追従し、図が収まっている間は不要なスクロールバーが出ないことを
/// 実 WPF レイアウト（ScrollViewer＋ズームホスト＋キャンバス）で検証する。
/// </summary>
/// <remarks>
/// MainWindow.xaml の DiagramScrollViewer / DiagramZoomHost / DiagramCanvas と同じ
/// 入れ子構造・バインディング・ScrollChanged 配線をコードで再現する。
/// スクロールバーの出没はレイアウト計算（extent とビューポートの比較）で決まるため、
/// ヘッドレスな VM 単体テストでは検証できず、実際に Measure/Arrange を回して確認する。
/// </remarks>
public class CanvasScrollBarVisibilityTests
{
    /// <summary>MainWindow.xaml と同じ構造のキャンバスホストを組み立てる</summary>
    private static (ScrollViewer Sv, MainViewModel Vm) BuildCanvasHost()
    {
        var vm = new MainViewModel();

        var canvas = new Grid();
        canvas.SetBinding(
            FrameworkElement.WidthProperty,
            new Binding(nameof(MainViewModel.CanvasWidth)) { Source = vm }
        );
        canvas.SetBinding(
            FrameworkElement.HeightProperty,
            new Binding(nameof(MainViewModel.CanvasHeight)) { Source = vm }
        );

        var scale = new ScaleTransform();
        BindingOperations.SetBinding(
            scale,
            ScaleTransform.ScaleXProperty,
            new Binding(nameof(MainViewModel.ZoomLevel)) { Source = vm }
        );
        BindingOperations.SetBinding(
            scale,
            ScaleTransform.ScaleYProperty,
            new Binding(nameof(MainViewModel.ZoomLevel)) { Source = vm }
        );
        var zoomHost = new Grid { LayoutTransform = scale };
        zoomHost.Children.Add(canvas);

        var sv = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = zoomHost,
        };

        // MainWindow.xaml.cs の DiagramScrollViewer_ScrollChanged と同じ配線
        sv.ScrollChanged += (_, _) =>
        {
            var zoom = vm.ZoomLevel;
            vm.ViewportContentBounds = new Rect(
                sv.HorizontalOffset / zoom,
                sv.VerticalOffset / zoom,
                sv.ViewportWidth / zoom,
                sv.ViewportHeight / zoom
            );
        };

        return (sv, vm);
    }

    /// <summary>指定サイズでレイアウトを確定させる（ScrollChanged→キャンバス再計算の収束まで回す）</summary>
    private static void Layout(ScrollViewer sv, double width, double height)
    {
        sv.Measure(new Size(width, height));
        sv.Arrange(new Rect(0, 0, width, height));
        sv.UpdateLayout();

        // ScrollChanged→キャンバスサイズ変更→スクロールバー出没の連鎖を確実に収束させる
        sv.UpdateLayout();
    }

    [Fact(DisplayName = "空図: キャンバスはビューポートと同寸になりスクロールバーが出ない")]
    public void EmptyDiagram_CanvasMatchesViewport_NoScrollBars()
    {
        RunSta(() =>
        {
            var (sv, vm) = BuildCanvasHost();

            Layout(sv, 1000, 700);

            sv.ComputedHorizontalScrollBarVisibility.Should().Be(Visibility.Collapsed);
            sv.ComputedVerticalScrollBarVisibility.Should().Be(Visibility.Collapsed);
            vm.CanvasWidth.Should().BeApproximately(1000, 1.0);
            vm.CanvasHeight.Should().BeApproximately(700, 1.0);
        });
    }

    [Fact(DisplayName = "図がビューポートに収まる: 余白込みでもスクロールバーが出ない")]
    public void SmallDiagram_FitsViewport_NoScrollBars()
    {
        RunSta(() =>
        {
            var (sv, vm) = BuildCanvasHost();
            vm.AddEntityCommand.Execute(null);
            var entity = vm.Entities[0];
            entity.X = 100;
            entity.Y = 100;
            vm.RefreshCanvasSize();

            Layout(sv, 1000, 700);

            sv.ComputedHorizontalScrollBarVisibility.Should().Be(Visibility.Collapsed);
            sv.ComputedVerticalScrollBarVisibility.Should().Be(Visibility.Collapsed);
        });
    }

    [Fact(DisplayName = "図がビューポートより大きい: スクロールバーが出てキャンバスは端＋余白 100")]
    public void LargeDiagram_ShowsScrollBars_WithMargin()
    {
        RunSta(() =>
        {
            var (sv, vm) = BuildCanvasHost();
            vm.AddEntityCommand.Execute(null);
            var entity = vm.Entities[0];
            entity.X = 2000;
            entity.Y = 1500;
            vm.RefreshCanvasSize();

            Layout(sv, 1000, 700);

            sv.ComputedHorizontalScrollBarVisibility.Should().Be(Visibility.Visible);
            sv.ComputedVerticalScrollBarVisibility.Should().Be(Visibility.Visible);
            vm.CanvasWidth.Should().Be(entity.X + entity.Width + 100);
            vm.CanvasHeight.Should().Be(entity.Y + entity.DisplayHeight + 100);
        });
    }

    [Fact(DisplayName = "ズームアウトで図が収まる: スクロールバーが消える")]
    public void ZoomOut_DiagramFits_ScrollBarsDisappear()
    {
        RunSta(() =>
        {
            var (sv, vm) = BuildCanvasHost();
            vm.AddEntityCommand.Execute(null);
            var entity = vm.Entities[0];
            entity.X = 1200;
            entity.Y = 800;
            vm.RefreshCanvasSize();

            // 等倍では収まらずスクロールバーが出る
            Layout(sv, 1000, 700);
            sv.ComputedHorizontalScrollBarVisibility.Should().Be(Visibility.Visible);

            // 50% へズームアウトすると論理ビューポートが広がり、図全体が収まる
            vm.ZoomLevel = 0.5;
            Layout(sv, 1000, 700);

            sv.ComputedHorizontalScrollBarVisibility.Should().Be(Visibility.Collapsed);
            sv.ComputedVerticalScrollBarVisibility.Should().Be(Visibility.Collapsed);
        });
    }

    [Fact(DisplayName = "ウィンドウ縮小: キャンバスがビューポートへ追従しスクロールバーが出ない")]
    public void ShrinkViewport_CanvasFollows_NoScrollBars()
    {
        RunSta(() =>
        {
            var (sv, vm) = BuildCanvasHost();

            Layout(sv, 1000, 700);
            vm.CanvasWidth.Should().BeApproximately(1000, 1.0);

            Layout(sv, 600, 400);

            sv.ComputedHorizontalScrollBarVisibility.Should().Be(Visibility.Collapsed);
            sv.ComputedVerticalScrollBarVisibility.Should().Be(Visibility.Collapsed);
            vm.CanvasWidth.Should().BeApproximately(600, 1.0);
            vm.CanvasHeight.Should().BeApproximately(400, 1.0);
        });
    }
}
