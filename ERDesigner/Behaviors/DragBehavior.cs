using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ERDesigner.UndoRedo;
using ERDesigner.ViewModels;

namespace ERDesigner.Behaviors;

/// <summary>
/// 親 <see cref="Canvas"/> 上の要素をドラッグで移動できるようにする添付ビヘイビアです。
/// </summary>
/// <remarks>
/// <para>使い方（XAML）:</para>
/// <code>
/// &lt;Border beh:DragBehavior.IsEnabled="True"
///         beh:DragBehavior.UndoRedoManager="{Binding DataContext.UndoRedo, RelativeSource={RelativeSource AncestorType=Window}}" /&gt;
/// </code>
/// <para>
/// 動作:
/// <list type="number">
///   <item>マウス押下時に現在の位置を保存し、対象要素にマウスをキャプチャします。</item>
///   <item>マウス移動中は <see cref="EntityViewModel.X"/> / <see cref="EntityViewModel.Y"/> を直接更新します（リレーション線が追従するため）。</item>
///   <item>マウス解放時、移動量が閾値以上なら <see cref="MoveEntityCommand"/> を Undo スタックへ登録します（再 Execute はしません）。</item>
///   <item>移動量が閾値未満ならクリックとみなし、<see cref="MainViewModel.OnEntityClicked(EntityViewModel)"/> を呼び出して選択状態に切り替えます。</item>
/// </list>
/// </para>
/// </remarks>
public static class DragBehavior
{
    /// <summary>クリック判定の許容ピクセル数（この範囲内ならクリック扱い）。</summary>
    private const double ClickThreshold = 3.0;

    // ---------- 添付プロパティ ----------

    /// <summary>ドラッグ機能を有効にするかどうかを表す添付プロパティです。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DragBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged)
    );

    /// <summary><see cref="IsEnabledProperty"/> の値を設定します。</summary>
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    /// <summary><see cref="IsEnabledProperty"/> の値を取得します。</summary>
    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    /// <summary>移動コマンドを登録する <see cref="UndoRedoManager"/> を保持する添付プロパティです。</summary>
    public static readonly DependencyProperty UndoRedoManagerProperty = DependencyProperty.RegisterAttached(
        "UndoRedoManager",
        typeof(UndoRedoManager),
        typeof(DragBehavior),
        new PropertyMetadata(null)
    );

    /// <summary><see cref="UndoRedoManagerProperty"/> の値を設定します。</summary>
    public static void SetUndoRedoManager(DependencyObject d, UndoRedoManager value) => d.SetValue(UndoRedoManagerProperty, value);

    /// <summary><see cref="UndoRedoManagerProperty"/> の値を取得します。</summary>
    public static UndoRedoManager? GetUndoRedoManager(DependencyObject d) => (UndoRedoManager?)d.GetValue(UndoRedoManagerProperty);

    // ---------- 内部状態（ドラッグ中要素は1つだけ） ----------

    private static Point _startMouse;
    private static double _startX;
    private static double _startY;
    private static bool _isDragging;
    private static bool _isResizing;
    private static double _startWidth;
    private static FrameworkElement? _draggedElement;
    private static EntityViewModel? _draggedVm;

    /// <summary>右端リサイズグリップの幅 (px)。</summary>
    private const double GripWidth = 8;

    /// <summary>添付プロパティ <see cref="IsEnabledProperty"/> 変更時のハンドラ登録／解除を行います。</summary>
    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            // MouseBinding が MouseUp を Handled にする場合があるため handledEventsToo:true で登録。
            fe.AddHandler(UIElement.MouseLeftButtonDownEvent, (MouseButtonEventHandler)OnMouseDown, handledEventsToo: true);
            fe.AddHandler(UIElement.MouseMoveEvent, (MouseEventHandler)OnMouseMove, handledEventsToo: true);
            fe.AddHandler(UIElement.MouseLeftButtonUpEvent, (MouseButtonEventHandler)OnMouseUp, handledEventsToo: true);
            fe.AddHandler(UIElement.LostMouseCaptureEvent, (MouseEventHandler)OnLostCapture, handledEventsToo: true);
        }
        else
        {
            fe.RemoveHandler(UIElement.MouseLeftButtonDownEvent, (MouseButtonEventHandler)OnMouseDown);
            fe.RemoveHandler(UIElement.MouseMoveEvent, (MouseEventHandler)OnMouseMove);
            fe.RemoveHandler(UIElement.MouseLeftButtonUpEvent, (MouseButtonEventHandler)OnMouseUp);
            fe.RemoveHandler(UIElement.LostMouseCaptureEvent, (MouseEventHandler)OnLostCapture);
        }
    }

    /// <summary>指定要素の祖先方向で最初に見つかった <see cref="Canvas"/> を返します。</summary>
    private static Canvas? FindCanvas(DependencyObject? d)
    {
        while (d is not null and not Canvas)
        {
            d = VisualTreeHelper.GetParent(d);
        }

        return d as Canvas;
    }

    /// <summary>マウス押下: ドラッグ開始位置を記録しキャプチャします。</summary>
    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe)
        {
            return;
        }

        if (fe.DataContext is not EntityViewModel vm)
        {
            return;
        }

        var canvas = FindCanvas(fe);

        if (canvas is null)
        {
            return;
        }

        _draggedElement = fe;
        _draggedVm = vm;
        _startMouse = e.GetPosition(canvas);
        _startX = vm.X;
        _startY = vm.Y;

        // 右端グリップ判定 → リサイズモード
        var local = e.GetPosition(fe);

        if (local.X >= fe.ActualWidth - GripWidth && fe.ActualWidth > 0)
        {
            _isResizing = true;
            _isDragging = false;
            _startWidth = vm.Width;
        }
        else
        {
            _isDragging = true;
            _isResizing = false;
        }

        fe.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>マウス移動: ボタン押下中なら ViewModel の座標を更新します。</summary>
    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedElement is null || _draggedVm is null)
        {
            return;
        }

        // カーソル形状更新 (ドラッグ中でなければ)
        if (!_isDragging && !_isResizing && sender is FrameworkElement cursorFe)
        {
            var lp = e.GetPosition(cursorFe);
            cursorFe.Cursor = (lp.X >= cursorFe.ActualWidth - GripWidth && cursorFe.ActualWidth > 0) ? Cursors.SizeWE : null;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(treatAsClick: false);
            return;
        }

        var canvas = FindCanvas(_draggedElement);

        if (canvas is null)
        {
            return;
        }

        var pos = e.GetPosition(canvas);

        if (_isResizing)
        {
            var delta = pos.X - _startMouse.X;
            var newWidth = Math.Max(120, _startWidth + delta);
            _draggedVm.Width = newWidth;
        }
        else if (_isDragging)
        {
            var newX = _startX + (pos.X - _startMouse.X);
            var newY = _startY + (pos.Y - _startMouse.Y);
            // 画面外に出ないよう最小 0 制限 (キャンバスは自動拡大するが負座標は不可)
            _draggedVm.X = Math.Max(0, newX);
            _draggedVm.Y = Math.Max(0, newY);
        }
    }

    /// <summary>マウス解放: 移動量に応じてクリック扱い／Undo 登録を行います。</summary>
    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging && !_isResizing)
        {
            return;
        }

        var canvas = FindCanvas(_draggedElement);
        var movedPx = double.PositiveInfinity;

        if (canvas is not null)
        {
            var pos = e.GetPosition(canvas);
            movedPx = (pos - _startMouse).Length;
        }

        EndDrag(treatAsClick: !_isResizing && movedPx <= ClickThreshold);
    }

    /// <summary>キャプチャを失った場合（外部要因含む）に状態をリセットします。</summary>
    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging || _isResizing)
        {
            EndDrag(treatAsClick: false);
        }
    }

    /// <summary>ドラッグ状態を終了し、必要に応じて Undo/選択処理を行います。</summary>
    /// <param name="treatAsClick">true の場合、ViewModel のクリックハンドラを呼びます。</param>
    private static void EndDrag(bool treatAsClick)
    {
        var element = _draggedElement;
        var vm = _draggedVm;
        var oldX = _startX;
        var oldY = _startY;
        var wasResizing = _isResizing;

        _isDragging = false;
        _isResizing = false;
        _draggedElement = null;
        _draggedVm = null;

        if (element is null || vm is null)
        {
            return;
        }

        if (element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        element.Cursor = null;

        if (wasResizing)
        {
            // リサイズ完了 — 特に Undo 登録は省略 (Width 変更は軽微)
            return;
        }

        if (treatAsClick)
        {
            // クリック扱い: 位置を元に戻し、選択処理を ViewModel に委譲。
            vm.X = oldX;
            vm.Y = oldY;

            if (Window.GetWindow(element)?.DataContext is MainViewModel main)
            {
                main.OnEntityClicked(vm);
            }

            return;
        }

        if (vm.X != oldX || vm.Y != oldY)
        {
            var mgr = GetUndoRedoManager(element);
            mgr?.Push(new MoveEntityCommand(vm, oldX, oldY, vm.X, vm.Y));
        }

        // キャンバスサイズ再計算
        if (Window.GetWindow(element)?.DataContext is MainViewModel mainVm)
        {
            mainVm.RefreshCanvasSize();
        }
    }
}
