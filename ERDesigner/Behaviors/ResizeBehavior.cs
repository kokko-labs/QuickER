using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ERDesigner.ViewModels;

namespace ERDesigner.Behaviors;

/// <summary>
/// エンティティカード右端のドラッグによる幅変更を実現する Attached Behavior。
/// 対象 Border の右端 6px 領域をドラッグするとカーソルが ↔ に変わり、幅を変更できます。
/// </summary>
public static class ResizeBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ResizeBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private const double GripWidth = 6;
    private static bool _isResizing;
    private static Point _startPos;
    private static double _startWidth;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue)
        {
            fe.MouseMove += OnMouseMove;
            fe.MouseLeftButtonDown += OnMouseDown;
            fe.MouseLeftButtonUp += OnMouseUp;
        }
        else
        {
            fe.MouseMove -= OnMouseMove;
            fe.MouseLeftButtonDown -= OnMouseDown;
            fe.MouseLeftButtonUp -= OnMouseUp;
        }
    }

    private static bool IsInGrip(MouseEventArgs e, FrameworkElement fe)
    {
        var pos = e.GetPosition(fe);
        return pos.X >= fe.ActualWidth - GripWidth;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;

        if (_isResizing)
        {
            var canvas = GetCanvas(fe);
            if (canvas is null) return;
            var current = e.GetPosition(canvas);
            var delta = current.X - _startPos.X;
            var newWidth = Math.Max(120, _startWidth + delta);
            fe.Width = newWidth;

            // Update ViewModel
            if (fe.DataContext is EntityViewModel vm)
                vm.Width = newWidth;
            e.Handled = true;
            return;
        }

        fe.Cursor = IsInGrip(e, fe) ? Cursors.SizeWE : null;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (!IsInGrip(e, fe)) return;

        _isResizing = true;
        _startWidth = fe.ActualWidth;
        var canvas = GetCanvas(fe);
        _startPos = e.GetPosition(canvas ?? fe);
        fe.CaptureMouse();
        e.Handled = true;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        if (sender is FrameworkElement fe)
            fe.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static Canvas? GetCanvas(DependencyObject d)
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(d);
        while (parent is not null)
        {
            if (parent is Canvas c) return c;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
